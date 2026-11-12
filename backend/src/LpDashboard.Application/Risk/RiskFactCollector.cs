using LpDashboard.Application.Abstractions;
using LpDashboard.Domain;
using LpDashboard.Domain.Risk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LpDashboard.Application.Risk;

/// <summary>
/// Asks every source about a token or pool, in parallel, each with its own timeout, and tolerates failures: a source that
/// did not answer leaves its block of facts empty and is listed in <c>Sources</c>, so the evaluator can say "6 of 9 checks ran".
/// Only token and pool addresses are ever sent to a source.
/// </summary>
public sealed class RiskFactCollector(
    IGoPlusSource goPlus,
    IHoneypotSource honeypot,
    IOnChainFactsSource onChain,
    IMarketFactsSource market,
    IContractInfoSource contract,
    IPriceSource prices,
    IEnumerable<IPositionProvider> providers,
    IOptions<RiskSettings> settings,
    TimeProvider time,
    ILogger<RiskFactCollector> logger)
{
    public async Task<TokenRiskFacts> CollectTokenAsync(Chain chain, string token, string? knownSymbol, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var priceKey = PriceKey(chain, token);

        var goPlusTask = RunAsync(RiskSources.GoPlus, c => goPlus.GetTokenAsync(chain, token, c), ct);
        var honeypotTask = RunAsync(RiskSources.HoneypotIs, c => honeypot.GetTokenAsync(chain, token, c), ct);
        var onChainTask = RunAsync(RiskSources.Rpc, c => onChain.GetTokenAsync(chain, token, c), ct);
        var marketTask = RunAsync(RiskSources.DexScreener, c => market.GetTokenAsync(chain, token, c), ct);
        // Basescan is optional (it needs a key): without one it is neither asked nor counted.
        var contractTask = contract.IsConfigured
            ? RunAsync(RiskSources.Etherscan, c => contract.GetTokenAsync(chain, token, c), ct)
            : null;
        var priceTask = RunAsync(RiskSources.Prices, c => ReadPriceAsync(priceKey, now, c), ct);

        await Task.WhenAll(new Task?[] { goPlusTask, honeypotTask, onChainTask, marketTask, contractTask, priceTask }.OfType<Task>());
        var gp = await goPlusTask;
        var hp = await honeypotTask;
        var chainReading = await onChainTask;
        var mk = await marketTask;
        var ct2 = contractTask is null ? null : await contractTask;
        var pr = await priceTask;

        var sources = new List<SourceResult>
        {
            ToResult(RiskSources.GoPlus, true, gp),
            ToResult(RiskSources.HoneypotIs, true, hp),
            ToResult(RiskSources.Rpc, true, chainReading),
            ToResult(RiskSources.DexScreener, false, mk),
        };
        if (ct2 is not null) sources.Add(ToResult(RiskSources.Etherscan, false, ct2));
        sources.Add(ToResult(RiskSources.Prices, false, pr));

        var symbol = chainReading.Value?.Symbol is { Length: > 0 } s ? s : gp.Value?.Symbol ?? knownSymbol;
        return new TokenRiskFacts(
            token.ToLowerInvariant(), symbol, now, sources,
            gp.Value, hp.Value, chainReading.Value?.Facts, mk.Value, ct2?.Value, pr.Value);
    }

    /// <summary>The pool's own facts. <paramref name="hasGauge"/> is true for Aerodrome Slipstream pools, the only ones with a gauge to read.</summary>
    public async Task<PoolRiskFacts> CollectPoolAsync(
        Chain chain, string pool, bool hasGauge, decimal? feeAprPct, decimal? rewardAprPct, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var marketTask = RunAsync(RiskSources.DexScreener, c => market.GetPoolAsync(chain, pool, c), ct);
        var gaugeTask = hasGauge
            ? RunAsync(RiskSources.Rpc, c => onChain.GetGaugeStatusAsync(chain, pool, c), ct)
            : null;
        await Task.WhenAll(new Task?[] { marketTask, gaugeTask }.OfType<Task>());

        var mk = await marketTask;
        var gauge = gaugeTask is null ? null : await gaugeTask;
        var sources = new List<SourceResult> { ToResult(RiskSources.DexScreener, true, mk) };
        if (gauge is not null) sources.Add(ToResult(RiskSources.Rpc, false, gauge));
        // Rates come from the pool explorer's cache; without them the emission check has nothing to read.
        sources.Add(feeAprPct is null
            ? new SourceResult(RiskSources.PoolStats, false, false, "pool not in the explorer's list")
            : new SourceResult(RiskSources.PoolStats, false, true));

        return new PoolRiskFacts(
            pool.ToLowerInvariant(), now, sources, mk.Value?.CreatedAt, feeAprPct, rewardAprPct,
            gauge?.Value is { HasGauge: true } g ? g.Alive : null);
    }

    private async Task<SourceOutcome<PriceFacts>> ReadPriceAsync(string key, DateTime now, CancellationToken ct)
    {
        try
        {
            var current = await prices.GetCurrentAsync([key], ct);
            var back = new DateTimeOffset(now.AddDays(-settings.Value.PriceHistoryDays), TimeSpan.Zero).ToUnixTimeSeconds();
            var history = await prices.GetHistoricalAsync([key], back, ct);
            decimal? price = current.TryGetValue(key, out var p) ? p : null;
            return SourceOutcome<PriceFacts>.Ok(new PriceFacts(price, history.ContainsKey(key)));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return SourceOutcome<PriceFacts>.Fail("price source did not answer");
        }
    }

    private string PriceKey(Chain chain, string token) =>
        providers.FirstOrDefault(p => p.Chain == chain)?.GetPriceKey(token) ?? $"{chain.ToString().ToLowerInvariant()}:{token.ToLowerInvariant()}";

    private async Task<SourceOutcome<T>> RunAsync<T>(string name, Func<CancellationToken, Task<SourceOutcome<T>>> call, CancellationToken ct)
        where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.Value.SourceTimeoutSeconds)));
        try
        {
            return await call(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return SourceOutcome<T>.Fail("timeout");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never the message: some provider errors echo the request URL.
            logger.LogWarning("Risk source {Source} failed: {Type}", name, ex.GetType().Name);
            return SourceOutcome<T>.Fail("error");
        }
    }

    private static SourceResult ToResult<T>(string name, bool required, SourceOutcome<T> outcome) where T : class =>
        new(name, required, outcome.Answered, outcome.Answered ? null : outcome.Failure ?? "no data");
}
