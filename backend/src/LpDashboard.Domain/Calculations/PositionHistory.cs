using LpDashboard.Domain.Entities;

namespace LpDashboard.Domain.Calculations;

public sealed record PriceSample(DateTime Timestamp, decimal Price0Usd, decimal Price1Usd);

public sealed record HistoryPoint(
    DateTime Timestamp,
    decimal ValueUsd,
    decimal ValueWithFeesUsd,
    decimal HodlValueUsd,
    decimal Price0Usd,
    decimal Price1Usd,
    int CurrentTick);

/// <summary>
/// Reconstructs a position's value over time from its events and token prices, for periods without stored snapshots.
/// Liquidity is inferred from deposit/withdraw amounts and the pool price from the token USD prices, so values are estimates.
/// Fees are only counted once claimed (uncollected fees are not known historically).
/// </summary>
public static class PositionHistory
{
    private const double DustRatio = 1e-4;

    private sealed record State(double Liquidity, decimal Deposited0, decimal Deposited1, decimal ClaimedUsd);

    /// <param name="orderedEvents">Events in chain order (see <c>CollectSplitter.Order</c>).</param>
    public static IReadOnlyList<HistoryPoint> Build(
        IReadOnlyList<PositionEvent> orderedEvents,
        int tickLower,
        int tickUpper,
        int decimals0,
        int decimals1,
        IEnumerable<PriceSample> samples)
    {
        var scale0 = System.Math.Pow(10, decimals0);
        var scale1 = System.Math.Pow(10, decimals1);

        var before = new List<State>(orderedEvents.Count);
        var after = new List<State>(orderedEvents.Count);
        var state = new State(0, 0, 0, 0);
        double peak = 0;
        foreach (var e in orderedEvents)
        {
            before.Add(state);
            state = e.Type switch
            {
                PositionEventType.IncreaseLiquidity => state with
                {
                    Liquidity = state.Liquidity + Liquidity(e),
                    Deposited0 = state.Deposited0 + e.Amount0,
                    Deposited1 = state.Deposited1 + e.Amount1
                },
                PositionEventType.DecreaseLiquidity => state with
                {
                    Liquidity = System.Math.Max(0, state.Liquidity - Liquidity(e))
                },
                PositionEventType.Collect => state with
                {
                    ClaimedUsd = state.ClaimedUsd + e.Fee0 * (e.Price0Usd ?? 0) + e.Fee1 * (e.Price1Usd ?? 0)
                },
                PositionEventType.RewardClaim => state with
                {
                    ClaimedUsd = state.ClaimedUsd + e.RewardAmount * (e.RewardPriceUsd ?? 0)
                },
                _ => state
            };
            peak = System.Math.Max(peak, state.Liquidity);
            // Inferred liquidity is approximate; a near-total withdrawal closes the position.
            if (state.Liquidity < peak * DustRatio) state = state with { Liquidity = 0 };
            after.Add(state);
        }

        var points = new List<HistoryPoint>();
        for (var i = 0; i < orderedEvents.Count; i++)
        {
            var e = orderedEvents[i];
            if (e.Price0Usd is not { } p0 || e.Price1Usd is not { } p1) continue;
            // A withdrawal is plotted with the liquidity it removed, a deposit with the liquidity it added.
            var s = e.Type switch
            {
                PositionEventType.IncreaseLiquidity => after[i],
                PositionEventType.DecreaseLiquidity => before[i],
                _ => null
            };
            if (s is not null) AddPoint(e.Timestamp, s, p0, p1);
        }

        foreach (var sample in samples)
        {
            var index = -1;
            for (var i = 0; i < orderedEvents.Count && orderedEvents[i].Timestamp <= sample.Timestamp; i++) index = i;
            if (index >= 0) AddPoint(sample.Timestamp, after[index], sample.Price0Usd, sample.Price1Usd);
        }

        return points.OrderBy(p => p.Timestamp).ToList();

        double Liquidity(PositionEvent e) =>
            PositionMath.GetLiquidityRaw((double)e.Amount0 * scale0, (double)e.Amount1 * scale1, tickLower, tickUpper);

        void AddPoint(DateTime timestamp, State s, decimal price0, decimal price1)
        {
            if (s.Liquidity <= 0 || price0 <= 0 || price1 <= 0) return;

            var sqrtPrice = PositionMath.PriceToSqrtPrice((double)(price0 / price1), decimals0, decimals1);
            var (raw0, raw1) = PositionMath.GetAmountsRaw(s.Liquidity, sqrtPrice, tickLower, tickUpper);
            var value = TokenAmount.FromDouble(raw0 / scale0) * price0 + TokenAmount.FromDouble(raw1 / scale1) * price1;

            points.Add(new HistoryPoint(
                timestamp,
                value,
                value + s.ClaimedUsd,
                s.Deposited0 * price0 + s.Deposited1 * price1,
                price0,
                price1,
                PositionMath.SqrtPriceToTick(sqrtPrice)));
        }
    }
}
