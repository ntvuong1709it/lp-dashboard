namespace LpDashboard.Domain.Risk;

public enum RiskChangeKind
{
    Appeared,
    Cleared,
    SeverityRaised,
    SeverityLowered,
    LevelWorsened,
    LevelImproved,
    ControllerChanged,
    ImplementationChanged,
    TopHolderShareRose,
}

/// <param name="Code">The finding code this is about; empty for a pure level change.</param>
/// <param name="Description">Before and after, ready for the alert message and the "recent changes" list.</param>
/// <param name="IsWorse">The change is for the worse.</param>
/// <param name="AlertWorthy">The change is one of the Telegram triggers in docs/risk-screen-spec.md section 9.</param>
/// <param name="Key">What distinguishes this change from another of the same kind: the new severity, the new controller or implementation address, the holder.</param>
public sealed record RiskChange(RiskChangeKind Kind, string Code, string Description, bool IsWorse, bool AlertWorthy, string? Key = null);

/// <param name="IsFirstCheck">There was no previous check. A first check records changes as nothing and never alerts.</param>
public sealed record RiskComparison(bool IsFirstCheck, IReadOnlyList<RiskChange> Changes)
{
    public static readonly RiskComparison First = new(true, []);

    public bool ShouldAlert => !IsFirstCheck && Changes.Any(c => c.AlertWorthy);
}

/// <summary>
/// Compares two checks of the same token or pool. Pure. A finding is only reported as cleared when at least one source
/// that can produce it answered this time, so a source outage never looks like "the problem went away" (and the
/// recovery never looks like "the problem is new").
/// </summary>
public static class RiskChangeDetector
{
    /// <summary>Findings that always alert when they appear or get worse on a held token.</summary>
    private static readonly HashSet<string> AlwaysAlert =
    [
        RiskCodes.Honeypot, RiskCodes.PrivilegedMintOrBlacklist, RiskCodes.UpgradeableProxy, RiskCodes.TopHolderConcentration,
    ];

    /// <summary>The top holder's share has to rise by more than this many percentage points to alert.</summary>
    public const decimal TopHolderRiseAlertPoints = 10m;

    public static RiskComparison Compare(TokenRiskAssessment? previous, TokenRiskAssessment current)
    {
        if (previous is null) return RiskComparison.First;

        var changes = new List<RiskChange>();
        Level(changes, previous.Verdict, current.Verdict);
        Findings(changes, previous.Findings, current.Findings, current.Facts.Sources, alwaysAlert: true);

        // Controller and implementation need both checks to have read the chain, otherwise "changed" is a guess.
        if (previous.Facts.OnChain is not null && current.Facts.OnChain is not null)
        {
            var before = RiskFactsAnalysis.Controller(previous.Facts);
            var after = RiskFactsAnalysis.Controller(current.Facts);
            if (after is not null && !RiskAddresses.Same(before?.Address, after.Address))
                changes.Add(new(RiskChangeKind.ControllerChanged, RiskCodes.PrivilegedMintOrBlacklist,
                    $"Controller changed: {before?.Address ?? "none (renounced or no owner)"} -> {after.Address}", true, true, after.Address));

            var implBefore = RiskFactsAnalysis.Implementation(previous.Facts);
            var implAfter = RiskFactsAnalysis.Implementation(current.Facts);
            if (implAfter is not null && !RiskAddresses.Same(implBefore, implAfter))
                changes.Add(new(RiskChangeKind.ImplementationChanged, RiskCodes.UpgradeableProxy,
                    $"Proxy implementation changed: {implBefore ?? "none"} -> {implAfter}", true, true, implAfter));
        }

        if (RiskFactsAnalysis.TopHolder(previous.Facts) is { } was && RiskFactsAnalysis.TopHolder(current.Facts) is { } now
            && now.SharePct - was.SharePct > TopHolderRiseAlertPoints)
            changes.Add(new(RiskChangeKind.TopHolderShareRose, RiskCodes.TopHolderConcentration,
                $"Largest holder's share rose from {was.SharePct:0.##}% to {now.SharePct:0.##}% ({now.Address})", true, true, now.Address));

        return new RiskComparison(false, changes);
    }

    public static RiskComparison ComparePool(PoolRiskAssessment? previous, PoolRiskAssessment current)
    {
        if (previous is null) return RiskComparison.First;

        var changes = new List<RiskChange>();
        Level(changes, previous.OwnVerdict, current.OwnVerdict);
        Findings(changes, previous.Findings, current.Findings, current.Facts.Sources, alwaysAlert: false);
        return new RiskComparison(false, changes);
    }

    // Green < Yellow < Red. Partial and NotChecked have no rank: a check that could not conclude is not a baseline.
    private static int? Rank(RiskLevel level) => level switch
    {
        RiskLevel.Green => 0,
        RiskLevel.Yellow => 1,
        RiskLevel.Red => 2,
        _ => null,
    };

    private static void Level(List<RiskChange> changes, RiskVerdict previous, RiskVerdict current)
    {
        var after = Rank(current.Level);
        if (after is null) return;
        var before = Rank(previous.Level);

        // From an inconclusive check, a jump to Red still alerts (Red is never a guess), a jump to Yellow does not.
        if (before is null)
        {
            if (after == 2)
                changes.Add(new(RiskChangeKind.LevelWorsened, "", $"Risk level {previous.Level} -> {current.Level}", true, true, current.Level.ToString()));
            return;
        }

        if (after > before)
            changes.Add(new(RiskChangeKind.LevelWorsened, "", $"Risk level {previous.Level} -> {current.Level}", true, true, current.Level.ToString()));
        else if (after < before)
            changes.Add(new(RiskChangeKind.LevelImproved, "", $"Risk level {previous.Level} -> {current.Level}", false, false));
    }

    private static void Findings(
        List<RiskChange> changes,
        IReadOnlyList<RiskFinding> previous,
        IReadOnlyList<RiskFinding> current,
        IReadOnlyList<SourceResult> currentSources,
        bool alwaysAlert)
    {
        var before = previous.Where(f => f.Severity != RiskSeverity.Info && !RiskCodes.IsBookkeeping(f.Code)).ToDictionary(f => f.Code);
        var after = current.Where(f => f.Severity != RiskSeverity.Info && !RiskCodes.IsBookkeeping(f.Code)).ToDictionary(f => f.Code);

        foreach (var (code, now) in after)
        {
            if (!before.TryGetValue(code, out var was))
            {
                changes.Add(new(RiskChangeKind.Appeared, code, $"{now.Title}: {now.Evidence}", true, alwaysAlert && AlwaysAlert.Contains(code)));
            }
            else if (now.Severity > was.Severity)
            {
                changes.Add(new(RiskChangeKind.SeverityRaised, code, $"{now.Title}: {was.Severity} -> {now.Severity}. {now.Evidence}", true,
                    alwaysAlert && AlwaysAlert.Contains(code), now.Severity.ToString()));
            }
            else if (now.Severity < was.Severity)
            {
                changes.Add(new(RiskChangeKind.SeverityLowered, code, $"{now.Title}: {was.Severity} -> {now.Severity}. {now.Evidence}", false, false));
            }
        }

        foreach (var (code, was) in before)
        {
            if (after.ContainsKey(code)) continue;
            var sources = RiskCodes.SourcesFor(code);
            var evaluable = sources.Count == 0 || currentSources.Any(s => s.Answered && sources.Contains(s.Name));
            if (evaluable)
                changes.Add(new(RiskChangeKind.Cleared, code, $"{was.Title} cleared (was {was.Severity}: {was.Evidence})", false, false));
        }
    }
}
