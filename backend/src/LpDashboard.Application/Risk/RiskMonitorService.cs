using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Pools;
using LpDashboard.Application.Pricing;
using LpDashboard.Domain;
using LpDashboard.Domain.Entities;
using LpDashboard.Domain.Risk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LpDashboard.Application.Risk;

public sealed record RiskNightlySummary(int TokensChecked, int TokensSkipped, int TokensFailed, int PoolsChecked);

public sealed record LiquiditySummary(int PoolsSnapshotted, int PoolsUnread, int DropsAlerted);

/// <summary>
/// The scheduled work behind the risk screen: the nightly re-check of the tokens the owner holds or follows, and the hourly
/// pool-balance snapshots with the liquidity-drop alert. The hosted service calls these after, and never inside, a position sync.
/// </summary>
public sealed class RiskMonitorService(
    RiskService risk,
    RiskAlertService alerts,
    IWatchedAddressRepository watched,
    IPositionRepository positions,
    IRiskRepository repository,
    IUnitOfWork unitOfWork,
    IEnumerable<IPositionProvider> providers,
    PriceService prices,
    PoolExplorerService explorer,
    IOptions<RiskSettings> settings,
    TimeProvider time,
    ILogger<RiskMonitorService> logger)
{
    /// <summary>How far from "a day ago" the baseline snapshot may be; snapshots are hourly, a restart can leave gaps.</summary>
    private static readonly TimeSpan BaselineTolerance = TimeSpan.FromHours(6);

    /// <param name="onlyStale">Skip tokens checked within the cache window: the catch-up run after a restart.</param>
    public async Task<RiskNightlySummary> RunNightlyAsync(bool onlyStale, CancellationToken ct)
    {
        var owners = await OwnersAsync(ct);
        var scope = await risk.ScopeAsync(owners, ct);
        var now = time.GetUtcNow().UtcDateTime;
        var cache = TimeSpan.FromHours(settings.Value.CacheHours);
        var pause = TimeSpan.FromSeconds(Math.Max(0, settings.Value.NightlyPauseSeconds));
        int checkedCount = 0, skipped = 0, failed = 0, pools = 0;

        var latest = onlyStale
            ? (await repository.ListLatestAsync(RiskSubjectKind.Token, Chain.Base, scope.Tokens.Keys.ToList(), ct)).ToDictionary(r => r.Subject, StringComparer.OrdinalIgnoreCase)
            : [];

        // Held first: they alert, so they are worth being first in line if the night runs long.
        foreach (var (token, info) in scope.Tokens.OrderByDescending(t => t.Value.Reasons.Contains("held")).ThenBy(t => t.Key, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (onlyStale && latest.TryGetValue(token, out var row) && now - row.CheckedAt < cache)
            {
                skipped++;
                continue;
            }

            try
            {
                var result = await risk.CheckTokenAsync(Chain.Base, token, ct, info.Symbol);
                await alerts.HandleTokenCheckAsync(Chain.Base, result, held: info.Reasons.Contains("held"), ct);
                checkedCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad token must not stop the night; the next night tries it again.
                failed++;
                logger.LogWarning("Nightly risk check of {Token} failed: {Type}", token, ex.GetType().Name);
            }

            if (pause > TimeSpan.Zero) await Task.Delay(pause, ct);
        }

        // Starred pools: their own findings (age, emissions, gauge). Their tokens were just checked, so this reads the cache.
        foreach (var star in await repository.ListStarsAsync(Chain.Base, ct))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await risk.GetPoolAsync(star.ExplorerId, refresh: true, ct);
                pools++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Nightly risk check of starred pool {Pool} failed: {Type}", star.ExplorerId, ex.GetType().Name);
            }
        }

        logger.LogInformation("Nightly risk run: {Checked} tokens checked, {Skipped} fresh, {Failed} failed, {Pools} starred pools", checkedCount, skipped, failed, pools);
        return new RiskNightlySummary(checkedCount, skipped, failed, pools);
    }

    /// <summary>
    /// Writes a balance snapshot for every held and starred pool, and for held pools compares it with the snapshot of about a
    /// day ago to alert on a liquidity drop. Reads balances from the node (no outside service), never writes a price.
    /// </summary>
    public async Task<LiquiditySummary> RunLiquidityAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var refs = await PoolsInScopeAsync(ct);
        var snapshotted = 0;
        var unread = 0;
        var drops = 0;

        foreach (var byChain in refs.GroupBy(p => p.Chain))
        {
            var provider = providers.FirstOrDefault(p => p.Chain == byChain.Key);
            if (provider is null)
            {
                unread += byChain.Count();
                continue;
            }

            var list = byChain.ToList();
            IReadOnlyList<System.Numerics.BigInteger?> balances;
            IReadOnlyDictionary<string, decimal> priced;
            try
            {
                balances = await provider.GetTokenBalancesAsync(
                    list.SelectMany(p => new[] { (p.Address, p.Token0.Address), (p.Address, p.Token1.Address) }).ToList(), ct);
                priced = await prices.GetCurrentAsync(
                    list.SelectMany(p => new[] { provider.GetPriceKey(p.Token0.Address), provider.GetPriceKey(p.Token1.Address) }).Distinct().ToList(), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Liquidity snapshot read failed: {Type}", ex.GetType().Name);
                unread += list.Count;
                continue;
            }

            for (var i = 0; i < list.Count; i++)
            {
                var pool = list[i];
                if (balances[2 * i] is not { } raw0 || balances[2 * i + 1] is not { } raw1)
                {
                    unread++;
                    continue;
                }

                if (pool.Held && priced.TryGetValue(provider.GetPriceKey(pool.Token0.Address), out var p0) && priced.TryGetValue(provider.GetPriceKey(pool.Token1.Address), out var p1))
                {
                    var baseline = await repository.GetSnapshotAtOrBeforeAsync(pool.Chain, pool.Address, now.AddHours(-24), BaselineTolerance, ct);
                    var change = baseline is null
                        ? null
                        : PoolLiquidityDropDetector.Compare(baseline.Token0Raw, baseline.Token1Raw, raw0, raw1, pool.Token0.Decimals, pool.Token1.Decimals, p0, p1);
                    await alerts.HandlePoolLiquidityAsync(pool.Chain, pool.Address, pool.Label, change, settings.Value.LiquidityDropPct, ct);
                    if (change is not null && change.Exceeds(settings.Value.LiquidityDropPct)) drops++;
                }

                repository.Add(new PoolLiquiditySnapshot { Chain = pool.Chain, PoolAddress = pool.Address, At = now, Token0Raw = raw0, Token1Raw = raw1 });
                snapshotted++;
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        await repository.PruneSnapshotsAsync(now.AddDays(-Math.Max(1, settings.Value.LiquidityRetentionDays)), ct);
        logger.LogInformation("Pool liquidity snapshots: {Count} written, {Unread} unreadable, {Drops} drops", snapshotted, unread, drops);
        return new LiquiditySummary(snapshotted, unread, drops);
    }

    /// <summary>Pools of the owners' open positions (held) and starred pools, deduplicated by address.</summary>
    private async Task<List<PoolRef>> PoolsInScopeAsync(CancellationToken ct)
    {
        var result = new Dictionary<string, PoolRef>(StringComparer.OrdinalIgnoreCase);
        var owners = await OwnersAsync(ct);
        if (owners.Count > 0)
        {
            foreach (var position in await positions.QueryAsync(new PositionFilter(owners, PositionStatus.Open), ct))
            {
                if (position.Pool is not { } pool) continue;
                result[pool.Address] = new PoolRef(
                    pool.Chain, pool.Address.ToLowerInvariant(),
                    new TokenInfo(pool.Token0.Address, pool.Token0.Symbol, pool.Token0.Decimals),
                    new TokenInfo(pool.Token1.Address, pool.Token1.Symbol, pool.Token1.Decimals),
                    Held: true, $"{pool.Token0.Symbol}/{pool.Token1.Symbol}");
            }
        }

        foreach (var star in await repository.ListStarsAsync(Chain.Base, ct))
        {
            try
            {
                // The explorer's id has no contract address; the first time, ask the chain which pool it is.
                var state = await explorer.ResolvePoolStateAsync(star.ExplorerId, ct);
                var address = state.PoolAddress.ToLowerInvariant();
                if (!result.ContainsKey(address))
                    result[address] = new PoolRef(star.Chain, address, state.Token0, state.Token1, Held: false, star.Symbol);
                await RememberPoolAddressAsync(star.ExplorerId, address, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Could not resolve starred pool {Pool}: {Type}", star.ExplorerId, ex.GetType().Name);
            }
        }

        return [.. result.Values];
    }

    private async Task RememberPoolAddressAsync(string explorerId, string address, CancellationToken ct)
    {
        if (await repository.FindStarAsync(Chain.Base, explorerId, ct) is { } tracked && tracked.PoolAddress != address)
        {
            tracked.PoolAddress = address;
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private async Task<IReadOnlyCollection<string>> OwnersAsync(CancellationToken ct) =>
        (await watched.ListAsync(ct)).Where(a => a.ChainFamily == ChainFamily.Evm).Select(a => a.Address).ToList();

    private sealed record PoolRef(Chain Chain, string Address, TokenInfo Token0, TokenInfo Token1, bool Held, string Label);
}
