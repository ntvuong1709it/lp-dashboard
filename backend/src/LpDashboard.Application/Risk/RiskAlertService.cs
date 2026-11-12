using System.Globalization;
using LpDashboard.Application.Abstractions;
using LpDashboard.Domain;
using LpDashboard.Domain.Entities;
using LpDashboard.Domain.Risk;
using Microsoft.Extensions.Logging;

namespace LpDashboard.Application.Risk;

/// <summary>
/// Turns risk changes into Telegram alerts (docs/risk-screen-spec.md section 9). One alert per (token or pool, key) until it
/// clears; the first check of a token never alerts; the alert record is written before the message is sent, so a failed run
/// or a failed delivery can never repeat an alert. Delivery failures are logged, never thrown.
/// </summary>
public sealed class RiskAlertService(
    IRiskRepository repository,
    IUnitOfWork unitOfWork,
    IAlertNotifier notifier,
    TimeProvider time,
    ILogger<RiskAlertService> logger)
{
    public const string LiquidityDropCode = "LIQUIDITY_DROP";
    private const string LevelWorsenedCode = "LEVEL_WORSENED";
    private const string TopHolderRiseCode = "TOP_HOLDER_SHARE_RISE";

    /// <summary>
    /// Call after the check has been stored. Clears the alerts whose problem is gone (so they can fire again), then alerts on
    /// the new worsening changes. Only held tokens alert; starred tokens are checked and recorded without alerts.
    /// </summary>
    public async Task HandleTokenCheckAsync(Chain chain, TokenCheckResult result, bool held, CancellationToken ct)
    {
        var assessment = result.Assessment;
        var token = assessment.Facts.TokenAddress.ToLowerInvariant();
        var now = time.GetUtcNow().UtcDateTime;
        var open = await repository.ListOpenAlertsAsync(RiskSubjectKind.Token, chain, token, ct);

        foreach (var state in open.Where(s => IsResolved(s, assessment)))
            state.ClearedAt = now;

        var messages = new List<string>();
        if (held && notifier.IsConfigured && !result.Comparison.IsFirstCheck)
        {
            var openKeys = open.Where(s => s.ClearedAt is null).Select(s => s.Code).ToHashSet(StringComparer.Ordinal);
            var lines = new List<string>();
            foreach (var change in result.Comparison.Changes.Where(c => c.AlertWorthy))
            {
                var key = KeyOf(change);
                if (!openKeys.Add(key)) continue;
                repository.Add(new RiskAlertState { Kind = RiskSubjectKind.Token, Chain = chain, Subject = token, Code = key, FirstAlertedAt = now });
                lines.Add("- " + change.Description);
            }

            if (lines.Count > 0)
            {
                var label = assessment.Facts.Symbol is { Length: > 0 } s ? $"{s} ({Short(token)})" : Short(token);
                messages.Add($"Risk got worse: {label}, now {assessment.Verdict.Level}\n{string.Join("\n", lines)}\n{RiskLinks.ExplorerAddress(chain, token)}");
            }
        }

        // The record first, then the message: whatever happens next, this alert is never sent twice.
        await unitOfWork.SaveChangesAsync(ct);
        await SendAsync(messages, ct);
    }

    /// <summary>
    /// A held pool's liquidity against about a day earlier, at today's prices. <paramref name="change"/> is null when there was
    /// no baseline. Alerts once; re-armed when the drop is gone.
    /// </summary>
    public async Task HandlePoolLiquidityAsync(Chain chain, string pool, string label, PoolLiquidityChange? change, decimal thresholdPct, CancellationToken ct)
    {
        pool = pool.ToLowerInvariant();
        var now = time.GetUtcNow().UtcDateTime;
        var state = await repository.FindOpenAlertAsync(RiskSubjectKind.Pool, chain, pool, LiquidityDropCode, ct);
        var messages = new List<string>();

        if (change is not null && change.Exceeds(thresholdPct))
        {
            if (state is null && notifier.IsConfigured)
            {
                repository.Add(new RiskAlertState { Kind = RiskSubjectKind.Pool, Chain = chain, Subject = pool, Code = LiquidityDropCode, FirstAlertedAt = now });
                messages.Add($"Pool liquidity dropped: {label}\n{Usd(change.ValueThenUsd)} -> {Usd(change.ValueNowUsd)} ({change.DropPct.ToString("0.#", CultureInfo.InvariantCulture)}% down) over about a day, both valued at today's prices\n{RiskLinks.ExplorerAddress(chain, pool)}");
            }
        }
        else if (state is not null && change is not null)
        {
            state.ClearedAt = now;
        }

        await unitOfWork.SaveChangesAsync(ct);
        await SendAsync(messages, ct);
    }

    private async Task SendAsync(List<string> messages, CancellationToken ct)
    {
        foreach (var message in messages)
        {
            if (await notifier.SendAsync(message, ct) is { } reason)
                logger.LogWarning("Risk alert not delivered: {Reason}", reason);
        }
    }

    /// <summary>The alert key of a change: one per finding code, except where a new address or severity is news of its own.</summary>
    internal static string KeyOf(RiskChange change) => change.Kind switch
    {
        RiskChangeKind.LevelWorsened => $"{LevelWorsenedCode}:{change.Key}",
        RiskChangeKind.ControllerChanged => $"CONTROLLER_CHANGED:{change.Key}",
        RiskChangeKind.ImplementationChanged => $"IMPLEMENTATION_CHANGED:{change.Key}",
        RiskChangeKind.TopHolderShareRose => TopHolderRiseCode,
        RiskChangeKind.SeverityRaised => $"{change.Code}:{change.Key}",
        _ => change.Code,
    };

    /// <summary>
    /// A finding's alert is over when the finding is gone and at least one source that can see it answered. The level and
    /// holder-rise alerts are over once the token is Green again; address-keyed alerts are one-offs and are left alone.
    /// </summary>
    private static bool IsResolved(RiskAlertState state, TokenRiskAssessment assessment)
    {
        if (state.ClearedAt is not null) return false;
        // A level alert is over once the token is below the level it alerted at (never on an inconclusive check).
        if (state.Code.StartsWith(LevelWorsenedCode + ":", StringComparison.Ordinal))
        {
            var alerted = Enum.TryParse<RiskLevel>(state.Code[(LevelWorsenedCode.Length + 1)..], out var level) ? Rank(level) : null;
            var current = Rank(assessment.Verdict.Level);
            return alerted is { } a && current is { } c && c < a;
        }
        if (state.Code is TopHolderRiseCode) return assessment.Verdict.Level == RiskLevel.Green;
        if (state.Code.StartsWith("CONTROLLER_CHANGED:", StringComparison.Ordinal) || state.Code.StartsWith("IMPLEMENTATION_CHANGED:", StringComparison.Ordinal))
            return assessment.Verdict.Level == RiskLevel.Green;

        var code = state.Code.Split(':', 2)[0];
        var sources = RiskCodes.SourcesFor(code);
        var evaluable = sources.Count == 0 || assessment.Facts.Sources.Any(s => s.Answered && sources.Contains(s.Name));
        return evaluable && !assessment.Findings.Any(f => f.Code == code && f.Severity != RiskSeverity.Info);
    }

    private static int? Rank(RiskLevel level) => level switch { RiskLevel.Green => 0, RiskLevel.Yellow => 1, RiskLevel.Red => 2, _ => null };

    private static string Short(string address) => address.Length > 12 ? $"{address[..6]}...{address[^4..]}" : address;

    private static string Usd(decimal v) => "$" + v.ToString("#,0", CultureInfo.InvariantCulture);
}
