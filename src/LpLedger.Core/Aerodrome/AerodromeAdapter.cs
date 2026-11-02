using System.Numerics;
using LpLedger.Core.Models;
using static LpLedger.Core.ConcentratedLiquidityMath;

namespace LpLedger.Core.Aerodrome;

/// <summary>Reads Aerodrome Slipstream (Base) positions for a wallet and normalizes them.</summary>
public sealed class AerodromeAdapter(IChainReader reader, IPriceSource prices)
{
    public const string Aero = "0x940181a94a35a4569e4529a3cdfb74e38fd98631";
    private const string Zero = "0x0000000000000000000000000000000000000000";

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public async Task<WalletResult> GetPositionsAsync(string owner, bool demo = false)
    {
        var warnings = new List<string>();
        var refs = await reader.CandidatesAsync(owner);
        var logs = refs.Count > 0 ? await reader.LiquidityLogsAsync(owner, refs) : [];
        var times = await reader.BlockTimestampsAsync(logs.Select(l => l.BlockNumber).Distinct().ToList());

        var built = await Task.WhenAll(refs.Select(r => BuildSafeAsync(r, owner, logs, times, warnings)));
        var positions = built.OfType<Position>()
            .OrderBy(p => p.State == PositionState.Closed)
            .ThenByDescending(p => p.ValueUsd)
            .ToList();
        return new WalletResult(owner, demo, positions, PnlCalculator.Summarize(positions), warnings);
    }

    private async Task<Position?> BuildSafeAsync(
        PositionRef pos, string owner, IReadOnlyList<RawLiquidityLog> logs, IReadOnlyDictionary<long, long> times, List<string> warnings)
    {
        try { return await BuildAsync(pos, owner, logs, times, warnings); }
        catch (Exception ex)
        {
            lock (warnings) warnings.Add($"Position #{pos.TokenId} could not be read ({ex.Message}) and is left out of the totals.");
            return null;
        }
    }

    private async Task<Position?> BuildAsync(
        PositionRef pos, string owner, IReadOnlyList<RawLiquidityLog> allLogs, IReadOnlyDictionary<long, long> times, List<string> warnings)
    {
        var id = pos.TokenId;
        var raw = await reader.PositionAsync(pos);
        if (raw is null)
        {
            lock (warnings) warnings.Add($"Position #{id} could not be read (burned, and its state before the burn is unavailable), so it is left out of the totals.");
            return null;
        }
        var holder = await reader.OwnerOfAsync(pos);
        if (holder is null && !raw.Burned) return null;
        var pool = await reader.PoolAsync(pos.Manager, raw.Token0, raw.Token1, raw.TickSpacing);

        // Custody: held by the wallet, or staked in the pool's gauge on the wallet's behalf. Anything else is not theirs.
        var staked = false;
        var gauge = Zero;
        if (!raw.Burned && !Same(holder!, owner))
        {
            gauge = await reader.GaugeAsync(pool);
            if (Same(gauge, Zero) || !Same(holder!, gauge)) return null;
            if (!await reader.StakedContainsAsync(gauge, owner, id)) return null;
            staked = true;
        }

        var t0Task = reader.TokenAsync(raw.Token0);
        var t1Task = reader.TokenAsync(raw.Token1);
        var slotTask = reader.Slot0Async(pool);
        var nowTask = prices.CurrentAsync([raw.Token0, raw.Token1, Aero]);
        await Task.WhenAll(t0Task, t1Task, slotTask, nowTask);
        var (t0, t1, slot, now) = (t0Task.Result, t1Task.Result, slotTask.Result, nowTask.Result);
        var p0 = now[raw.Token0.ToLowerInvariant()];
        var p1 = now[raw.Token1.ToLowerInvariant()];
        var pAero = now[Aero];

        var (amount0, amount1) = AmountsForLiquidity(
            raw.Liquidity, SqrtPriceFromX96(slot.SqrtPriceX96), raw.TickLower, raw.TickUpper, t0.Decimals, t1.Decimals);
        TokenAmount[] tokens =
        [
            new(raw.Token0, t0.Symbol, t0.Decimals, amount0, PnlCalculator.Usd(amount0, p0)),
            new(raw.Token1, t1.Symbol, t1.Decimals, amount1, PnlCalculator.Usd(amount1, p1)),
        ];
        var valueUsd = tokens[0].Usd + tokens[1].Usd;

        // Rewards: staked positions earn AERO emissions, unstaked ones earn trading fees.
        TokenAmount[] rewardTokens;
        if (raw.Burned) rewardTokens = [];
        else if (staked)
        {
            var earned = FromUnits(await reader.EarnedAeroAsync(gauge, owner, id), 18);
            rewardTokens = [new(Aero, "AERO", 18, earned, PnlCalculator.Usd(earned, pAero))];
        }
        else
        {
            var f = await reader.PendingFeesAsync(owner, pos);
            var a0 = FromUnits(f.Amount0, t0.Decimals);
            var a1 = FromUnits(f.Amount1, t1.Decimals);
            rewardTokens =
            [
                new(raw.Token0, t0.Symbol, t0.Decimals, a0, PnlCalculator.Usd(a0, p0)),
                new(raw.Token1, t1.Symbol, t1.Decimals, a1, PnlCalculator.Usd(a1, p1)),
            ];
        }
        var unclaimedUsd = rewardTokens.Sum(t => t.Usd);

        // History, each event valued at the price when it happened.
        var k0 = raw.Token0.ToLowerInvariant();
        var k1 = raw.Token1.ToLowerInvariant();
        var events = new List<PositionEvent>();
        foreach (var l in allLogs.Where(l => l.Ref.Key == pos.Key).OrderBy(l => l.BlockNumber).ThenBy(l => l.LogIndex))
        {
            var ts = times[l.BlockNumber];
            var px = await prices.AtAsync(ts, [raw.Token0, raw.Token1]);
            var a0 = FromUnits(l.Amount0, t0.Decimals);
            var a1 = FromUnits(l.Amount1, t1.Decimals);
            events.Add(new PositionEvent(
                l.Kind, ts, l.BlockNumber, l.TxHash, l.LogIndex, a0, a1,
                PnlCalculator.Usd(a0, px[k0]) + PnlCalculator.Usd(a1, px[k1])));
        }

        var first = events.FirstOrDefault(e => e.Kind == EventKind.Add);
        double? priceEntry = null;
        if (first is not null)
        {
            var px = await prices.AtAsync(first.Timestamp, [raw.Token0, raw.Token1]);
            priceEntry = (double)(px[k0] / px[k1]);
        }

        var closed = raw.Liquidity.IsZero || raw.Burned;
        var inRange = slot.Tick >= raw.TickLower && slot.Tick < raw.TickUpper;
        var last = events.LastOrDefault();

        // PnL is wrong, not just incomplete, when these are missing, so say so per position.
        var incomplete = new List<string> { "Gas is not tracked yet." };
        if (staked) incomplete.Add("AERO already claimed to the wallet is not tracked yet.");

        var pnl = PnlCalculator.Compute(new PnlInput(
            events.Select(e => new PnlEvent(e.Kind, e.Amount0, e.Amount1, e.Usd)).ToList(),
            valueUsd, p0, p1, unclaimedUsd, null));

        return new Position(
            Id: $"aerodrome:base:{pos.Manager[2..8].ToLowerInvariant()}-{id}", Protocol: "aerodrome", Chain: "Base",
            PositionManager: pos.Manager.ToLowerInvariant(), TokenId: id.ToString(),
            PoolAddress: pool, Pair: $"{t0.Symbol} / {t1.Symbol}", TickSpacing: raw.TickSpacing, Staked: staked,
            State: closed ? PositionState.Closed : inRange ? PositionState.In : PositionState.Out,
            PriceLower: PriceFromTick(raw.TickLower, t0.Decimals, t1.Decimals),
            PriceUpper: PriceFromTick(raw.TickUpper, t0.Decimals, t1.Decimals),
            PriceCurrent: PriceFromSqrtX96(slot.SqrtPriceX96, t0.Decimals, t1.Decimals),
            PriceEntry: priceEntry, Tokens: tokens, ValueUsd: valueUsd,
            Rewards: new Rewards(staked ? RewardKind.AeroEmissions : RewardKind.TradingFees, unclaimedUsd, rewardTokens),
            Pnl: pnl, OpenedAt: first?.Timestamp, ClosedAt: closed ? last?.Timestamp : null,
            Events: events, Incomplete: incomplete);
    }
}
