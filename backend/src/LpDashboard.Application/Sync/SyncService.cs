using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Pricing;
using LpDashboard.Domain;
using LpDashboard.Domain.Calculations;
using LpDashboard.Domain.Entities;
using LpDashboard.Domain.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LpDashboard.Application.Sync;

/// <summary>Incrementally syncs one (chain, protocol, owner) combination. Use a fresh DI scope per call.</summary>
public sealed class SyncService(
    IEnumerable<IPositionProvider> providers,
    IPositionRepository positions,
    ITokenRepository tokens,
    IPoolRepository pools,
    ISnapshotRepository snapshots,
    ISyncCursorRepository cursors,
    PriceService prices,
    IUnitOfWork unitOfWork,
    IOptions<SyncOptions> options,
    TimeProvider time,
    ILogger<SyncService> logger)
{
    private readonly Dictionary<string, Token> _tokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Pool> _pools = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<string>> SyncAsync(Chain chain, Protocol protocol, string owner, CancellationToken ct, ISyncProgress? progress = null)
    {
        var provider = providers.First(p => p.Chain == chain && p.Protocol == protocol);
        owner = owner.ToLowerInvariant();
        var now = time.GetUtcNow().UtcDateTime;
        var errors = new List<string>();

        progress?.Report(SyncStage.FetchingLatestBlock);
        var latest = await provider.GetLatestBlockAsync(ct);
        progress?.Report(SyncStage.DiscoveringPositions);
        var discovered = await provider.DiscoverAsync(owner, ct);
        var existing = (await positions.ListForOwnerAsync(chain, protocol, owner, ct)).ToDictionary(p => Key(p.PositionManager, p.TokenId));
        RepairRewardClaims(discovered, existing.Values);
        var cursor = await cursors.GetAsync(chain, protocol, owner, ct);

        var fromBlock = cursor is null ? long.MaxValue : cursor.LastSyncedBlock + 1;
        foreach (var d in discovered.Where(d => cursor is null || !existing.ContainsKey(Key(d.PositionManager, d.TokenId))))
        {
            fromBlock = Math.Min(fromBlock, d.FirstBlock);
        }

        progress?.Report(SyncStage.FetchingEvents, 0, discovered.Count);
        IReadOnlyList<RawPositionEvent> raw = discovered.Count > 0 && fromBlock <= latest
            ? await provider.GetEventsAsync(owner, discovered, fromBlock, latest, ct, progress)
            : [];

        var positionsPerTx = raw
            .GroupBy(e => e.TxHash, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(e => Key(e.PositionManager, e.TokenId)).Distinct().Count(), StringComparer.OrdinalIgnoreCase);
        var rawByPosition = raw.ToLookup(e => Key(e.PositionManager, e.TokenId));

        for (var i = 0; i < discovered.Count; i++)
        {
            progress?.Report(SyncStage.SyncingPositions, i, discovered.Count);
            var d = discovered[i];
            var key = Key(d.PositionManager, d.TokenId);
            try
            {
                await SyncPositionAsync(provider, owner, d, existing.GetValueOrDefault(key), rawByPosition[key].ToList(), positionsPerTx, now, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Sync failed for {Chain}/{Protocol} position {TokenId}", chain, protocol, d.TokenId);
                var error = $"{chain}/{protocol} #{d.TokenId}: {ex.Message}";
                errors.Add(error);
                progress?.ReportError(error);
            }
        }

        progress?.Report(SyncStage.Saving, discovered.Count, discovered.Count);

        // A failed position must be retried from the same block next time.
        if (errors.Count == 0)
        {
            if (cursor is null)
            {
                cursor = new SyncCursor { Chain = chain, Protocol = protocol, Owner = owner };
                cursors.Add(cursor);
            }
            cursor.LastSyncedBlock = latest;
            cursor.UpdatedAt = now;
        }

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Synced {Chain}/{Protocol} {Owner}: {Positions} positions, {Events} new raw events, {Errors} errors",
            chain, protocol, owner, discovered.Count, raw.Count, errors.Count);
        return errors;
    }

    private async Task SyncPositionAsync(
        IPositionProvider provider,
        string owner,
        DiscoveredPosition discovered,
        Position? position,
        List<RawPositionEvent> raw,
        Dictionary<string, int> positionsPerTx,
        DateTime now,
        CancellationToken ct)
    {
        var known = position?.Events.Select(e => (e.TxHash, e.LogIndex)).ToHashSet() ?? [];
        var newRaw = raw.Where(r => !known.Contains((r.TxHash, r.LogIndex))).OrderBy(r => r.BlockNumber).ThenBy(r => r.LogIndex).ToList();
        var txHashes = (position?.Events.Select(e => e.TxHash) ?? []).Concat(newRaw.Select(r => r.TxHash)).Distinct().ToList();

        // Remote calls first so a failure leaves tracked entities untouched.
        var state = await provider.GetStateAsync(owner, discovered, txHashes, ct);

        var key0 = provider.GetPriceKey(state.Token0.Address);
        var key1 = provider.GetPriceKey(state.Token1.Address);
        var rewardKey = state.RewardToken is null ? null : provider.GetPriceKey(state.RewardToken.Address);

        var queries = new List<PriceQuery>();
        foreach (var r in newRaw)
        {
            var ts = ToUnix(r.Timestamp);
            queries.Add(new PriceQuery(key0, ts));
            queries.Add(new PriceQuery(key1, ts));
            queries.Add(new PriceQuery(provider.NativePriceKey, ts));
            if (r.RewardToken is not null) queries.Add(new PriceQuery(provider.GetPriceKey(r.RewardToken.Address), ts));
        }
        var historical = await prices.GetHistoricalAsync(queries, ct);
        decimal? Historical(string key, DateTime timestamp) => historical.GetValueOrDefault(new PriceQuery(key, ToUnix(timestamp)));

        IReadOnlyDictionary<string, decimal> current = state.IsBurned
            ? new Dictionary<string, decimal>()
            : await prices.GetCurrentAsync(rewardKey is null ? [key0, key1] : [key0, key1, rewardKey], ct);
        decimal? Current(string? key) => key is not null && current.TryGetValue(key, out var p) ? p : null;

        var token0 = await UpsertTokenAsync(provider.Chain, state.Token0, ct);
        var token1 = await UpsertTokenAsync(provider.Chain, state.Token1, ct);
        var pool = await UpsertPoolAsync(provider, state, token0, token1, ct);

        if (position is null)
        {
            position = new Position
            {
                Chain = provider.Chain,
                Protocol = provider.Protocol,
                PositionManager = discovered.PositionManager,
                TokenId = discovered.TokenId,
                Owner = owner
            };
            positions.Add(position);
        }

        position.Pool = pool;
        position.TickLower = state.TickLower;
        position.TickUpper = state.TickUpper;
        position.Liquidity = state.Liquidity;
        position.IsStaked = discovered.IsStaked;
        position.Gauge = discovered.Gauge;

        foreach (var r in newRaw)
        {
            var share = Math.Max(1, positionsPerTx.GetValueOrDefault(r.TxHash, 1));
            // Charge gas once per (position, tx) and only when the owner paid it (not keepers/automation).
            var chargeGas = r.TxFrom.Equals(owner, StringComparison.OrdinalIgnoreCase)
                && !position.Events.Any(e => e.TxHash.Equals(r.TxHash, StringComparison.OrdinalIgnoreCase));
            var gasNative = chargeGas ? r.TxGasNative / share : 0;
            var nativePrice = gasNative > 0 ? Historical(provider.NativePriceKey, r.Timestamp) : null;

            position.Events.Add(new PositionEvent
            {
                Type = r.Type,
                TxHash = r.TxHash,
                LogIndex = r.LogIndex,
                BlockNumber = r.BlockNumber,
                Timestamp = r.Timestamp,
                Amount0 = TokenAmount.FromRaw(r.Amount0Raw, token0.Decimals),
                Amount1 = TokenAmount.FromRaw(r.Amount1Raw, token1.Decimals),
                RewardTokenAddress = r.RewardToken?.Address,
                RewardTokenSymbol = r.RewardToken?.Symbol,
                RewardAmount = r.RewardToken is null ? 0 : TokenAmount.FromRaw(r.RewardRaw, r.RewardToken.Decimals),
                Price0Usd = Historical(key0, r.Timestamp),
                Price1Usd = Historical(key1, r.Timestamp),
                RewardPriceUsd = r.RewardToken is null ? null : Historical(provider.GetPriceKey(r.RewardToken.Address), r.Timestamp),
                GasNative = gasNative,
                GasUsd = gasNative == 0 ? 0 : gasNative * nativePrice
            });
        }

        var (owed0, owed1) = CollectSplitter.Apply(position.Events);
        var ordered = CollectSplitter.Order(position.Events).ToList();
        var first = ordered.FirstOrDefault();
        position.OpenedAt = first?.Timestamp;
        position.OpenedBlock = first?.BlockNumber ?? discovered.FirstBlock;

        var collectable0 = TokenAmount.FromRaw(state.Collectable0Raw, token0.Decimals);
        var collectable1 = TokenAmount.FromRaw(state.Collectable1Raw, token1.Decimals);
        var pending = state.RewardToken is null ? 0 : TokenAmount.FromRaw(state.PendingRewardRaw, state.RewardToken.Decimals);
        var (amount0, amount1) = state.IsBurned
            ? (0m, 0m)
            : PositionMath.GetAmounts(state.Liquidity, state.SqrtPriceX96, state.TickLower, state.TickUpper, token0.Decimals, token1.Decimals);

        var isClosed = state.IsBurned || (state.Liquidity.IsZero && collectable0 == 0 && collectable1 == 0 && pending == 0);
        position.Status = isClosed ? PositionStatus.Closed : PositionStatus.Open;
        position.ClosedAt = isClosed ? ordered.LastOrDefault()?.Timestamp ?? now : null;

        position.CurrentTick = state.CurrentTick;
        position.SqrtPriceX96 = state.SqrtPriceX96;
        position.CurrentAmount0 = amount0;
        position.CurrentAmount1 = amount1;
        position.Uncollected0 = Math.Max(0, collectable0 - owed0);
        position.Uncollected1 = Math.Max(0, collectable1 - owed1);
        position.PendingReward = pending;
        position.RewardTokenAddress = state.RewardToken?.Address;
        position.RewardTokenSymbol = state.RewardToken?.Symbol;
        position.Price0Usd = Current(key0) ?? position.Price0Usd;
        position.Price1Usd = Current(key1) ?? position.Price1Usd;
        position.RewardPriceUsd = Current(rewardKey);
        position.ValuedAt = now;

        if (!isClosed)
        {
            await AddSnapshotIfDueAsync(position, state.BlockNumber, now, ct);
        }
    }

    private async Task AddSnapshotIfDueAsync(Position position, long block, DateTime now, CancellationToken ct)
    {
        if (position.Id != 0)
        {
            var last = await snapshots.GetLatestAsync(position.Id, ct);
            if (last is not null && now - last.Timestamp < TimeSpan.FromMinutes(options.Value.SnapshotIntervalMinutes))
                return;
        }

        var value = position.CurrentAmount0 * (position.Price0Usd ?? 0) + position.CurrentAmount1 * (position.Price1Usd ?? 0);
        var uncollected = position.Uncollected0 * (position.Price0Usd ?? 0) + position.Uncollected1 * (position.Price1Usd ?? 0)
            + position.PendingReward * (position.RewardPriceUsd ?? 0);

        snapshots.Add(new PositionSnapshot
        {
            Position = position,
            Timestamp = now,
            BlockNumber = block,
            CurrentTick = position.CurrentTick ?? 0,
            Amount0 = position.CurrentAmount0,
            Amount1 = position.CurrentAmount1,
            Uncollected0 = position.Uncollected0,
            Uncollected1 = position.Uncollected1,
            PendingReward = position.PendingReward,
            Price0Usd = position.Price0Usd,
            Price1Usd = position.Price1Usd,
            RewardPriceUsd = position.RewardPriceUsd,
            ValueUsd = value,
            UncollectedUsd = uncollected
        });
    }

    private async Task<Token> UpsertTokenAsync(Chain chain, TokenInfo info, CancellationToken ct)
    {
        var address = info.Address.ToLowerInvariant();
        if (_tokens.TryGetValue(address, out var cached)) return cached;

        var token = await tokens.FindAsync(chain, address, ct);
        if (token is null)
        {
            token = new Token { Chain = chain, Address = address, Symbol = info.Symbol, Decimals = info.Decimals };
            tokens.Add(token);
        }
        _tokens[address] = token;
        return token;
    }

    private async Task<Pool> UpsertPoolAsync(IPositionProvider provider, PositionChainState state, Token token0, Token token1, CancellationToken ct)
    {
        var address = state.PoolAddress.ToLowerInvariant();
        if (_pools.TryGetValue(address, out var cached)) return cached;

        var pool = await pools.FindAsync(provider.Chain, address, ct);
        if (pool is null)
        {
            pool = new Pool
            {
                Chain = provider.Chain,
                Protocol = provider.Protocol,
                Address = address,
                Token0 = token0,
                Token1 = token1,
                Fee = state.Fee,
                TickSpacing = state.TickSpacing
            };
            pools.Add(pool);
        }
        _pools[address] = pool;
        return pool;
    }

    /// <summary>
    /// Gauge claims carry no tokenId and were once split across every position ever staked in the gauge.
    /// Drop shares from positions that were not staked at the claim and give their amount (and gas) back to the rest.
    /// </summary>
    private void RepairRewardClaims(IReadOnlyList<DiscoveredPosition> discovered, IEnumerable<Position> existing)
    {
        var tracked = discovered.Where(d => d.StakePeriods is not null).ToDictionary(d => Key(d.PositionManager, d.TokenId));
        var claims = existing
            .SelectMany(p => p.Events.Where(e => e.Type == PositionEventType.RewardClaim).Select(e => (Position: p, Event: e)))
            .GroupBy(x => (TxHash: x.Event.TxHash.ToLowerInvariant(), x.Event.LogIndex))
            .ToList();

        foreach (var group in claims)
        {
            var valid = group.Where(x => !tracked.TryGetValue(Key(x.Position.PositionManager, x.Position.TokenId), out var d)
                || d.StakePeriods!.Any(s => s.Covers(x.Event.BlockNumber, x.Event.LogIndex))).ToList();
            var invalid = group.Except(valid).ToList();
            if (invalid.Count == 0) continue;

            var total = group.Sum(x => x.Event.RewardAmount);
            foreach (var (position, e) in invalid)
            {
                position.Events.Remove(e);
                if (e.GasNative == 0) continue;

                // Keep the gas on this position if it has another event in the same tx; otherwise it belongs to the real claimers.
                var sibling = position.Events.FirstOrDefault(o => o.TxHash.Equals(e.TxHash, StringComparison.OrdinalIgnoreCase));
                List<PositionEvent> receivers = sibling is not null ? [sibling] : valid.Select(v => v.Event).ToList();
                if (receivers.Count == 0) continue;
                foreach (var r in receivers)
                {
                    r.GasNative += e.GasNative / receivers.Count;
                    r.GasUsd = e.GasUsd is null ? r.GasUsd : (r.GasUsd ?? 0) + e.GasUsd / receivers.Count;
                }
            }

            foreach (var (_, e) in valid)
            {
                e.RewardAmount = total / valid.Count;
            }

            logger.LogInformation("Reassigned gauge claim {TxHash}:{LogIndex}: removed from {Removed} position(s), kept on {Kept}",
                group.Key.TxHash, group.Key.LogIndex, invalid.Count, valid.Count);
        }
    }

    private static string Key(string positionManager, string tokenId) => $"{positionManager.ToLowerInvariant()}:{tokenId}";

    private static long ToUnix(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
