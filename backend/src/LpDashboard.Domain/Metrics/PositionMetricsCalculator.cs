using LpDashboard.Domain.Calculations;
using LpDashboard.Domain.Entities;

namespace LpDashboard.Domain.Metrics;

/// <summary>Live valuation of an open position; null for closed positions.</summary>
public sealed record PositionValuation(
    decimal Amount0,
    decimal Amount1,
    decimal Uncollected0,
    decimal Uncollected1,
    decimal PendingReward,
    decimal? Price0Usd,
    decimal? Price1Usd,
    decimal? RewardPriceUsd);

public sealed record PositionMetrics
{
    public decimal Deposited0 { get; init; }
    public decimal Deposited1 { get; init; }
    public decimal Withdrawn0 { get; init; }
    public decimal Withdrawn1 { get; init; }
    public decimal CollectedFee0 { get; init; }
    public decimal CollectedFee1 { get; init; }

    public decimal DepositedUsd { get; init; }
    public decimal WithdrawnUsd { get; init; }
    public decimal CollectedFeesUsd { get; init; }
    public decimal UncollectedFeesUsd { get; init; }
    public decimal ClaimedRewardsUsd { get; init; }
    public decimal PendingRewardsUsd { get; init; }
    public decimal FeesEarnedUsd { get; init; }
    public decimal CurrentValueUsd { get; init; }
    public decimal GasUsd { get; init; }
    public decimal PnlUsd { get; init; }
    public decimal? PnlPercent { get; init; }
    public decimal HodlValueUsd { get; init; }
    public decimal ImpermanentLossUsd { get; init; }
    public decimal PnlVsHodlUsd { get; init; }
    public decimal? FeeAprPercent { get; init; }
    public double? AgeDays { get; init; }
    public bool HasMissingPrices { get; init; }
}

/// <summary>
/// PnL       = Current + Withdrawn(at time) + FeesEarned - Deposited(at time) - Gas
/// HODL      = deposited token amounts valued at reference prices (current, or at close for closed positions)
/// IL        = (current + withdrawn - deposited) token amounts valued at reference prices (fees excluded)
/// PnLvsHodl = Current + Withdrawn(at time) + FeesEarned - Gas - HODL
/// </summary>
public static class PositionMetricsCalculator
{
    private const double MinAprDays = 1.0 / 24;

    public static PositionMetrics Calculate(
        IReadOnlyCollection<PositionEvent> events,
        PositionValuation? current,
        DateTime? openedAt,
        DateTime? closedAt,
        DateTime now)
    {
        var ordered = CollectSplitter.Order(events).ToList();
        var missing = false;

        decimal Usd(decimal amount, decimal? price)
        {
            if (amount == 0) return 0;
            if (price is null) { missing = true; return 0; }
            return amount * price.Value;
        }

        decimal dep0 = 0, dep1 = 0, wd0 = 0, wd1 = 0, fee0 = 0, fee1 = 0;
        decimal depositedUsd = 0, withdrawnUsd = 0, collectedUsd = 0, rewardsUsd = 0, gasUsd = 0;
        decimal capital = 0;
        double capitalDays = 0;
        DateTime? previous = null;

        foreach (var e in ordered)
        {
            if (previous is not null)
            {
                capitalDays += (double)capital * (e.Timestamp - previous.Value).TotalDays;
            }
            previous = e.Timestamp;

            if (e.GasNative > 0)
            {
                if (e.GasUsd is null) missing = true;
                gasUsd += e.GasUsd ?? 0;
            }

            switch (e.Type)
            {
                case PositionEventType.IncreaseLiquidity:
                    dep0 += e.Amount0;
                    dep1 += e.Amount1;
                    var d = Usd(e.Amount0, e.Price0Usd) + Usd(e.Amount1, e.Price1Usd);
                    depositedUsd += d;
                    capital += d;
                    break;
                case PositionEventType.DecreaseLiquidity:
                    wd0 += e.Principal0;
                    wd1 += e.Principal1;
                    var w = Usd(e.Principal0, e.Price0Usd) + Usd(e.Principal1, e.Price1Usd);
                    withdrawnUsd += w;
                    capital = System.Math.Max(0, capital - w);
                    break;
                case PositionEventType.Collect:
                    fee0 += e.Fee0;
                    fee1 += e.Fee1;
                    collectedUsd += Usd(e.Fee0, e.Price0Usd) + Usd(e.Fee1, e.Price1Usd);
                    break;
                case PositionEventType.RewardClaim:
                    rewardsUsd += Usd(e.RewardAmount, e.RewardPriceUsd);
                    break;
            }
        }

        var cur0 = current?.Amount0 ?? 0;
        var cur1 = current?.Amount1 ?? 0;
        var currentValue = current is null ? 0 : Usd(cur0, current.Price0Usd) + Usd(cur1, current.Price1Usd);
        var uncollectedUsd = current is null ? 0 : Usd(current.Uncollected0, current.Price0Usd) + Usd(current.Uncollected1, current.Price1Usd);
        var pendingUsd = current is null ? 0 : Usd(current.PendingReward, current.RewardPriceUsd);

        var ref0 = current?.Price0Usd ?? ordered.LastOrDefault(e => e.Price0Usd is not null)?.Price0Usd;
        var ref1 = current?.Price1Usd ?? ordered.LastOrDefault(e => e.Price1Usd is not null)?.Price1Usd;

        var feesEarned = collectedUsd + uncollectedUsd + rewardsUsd + pendingUsd;
        var pnl = currentValue + withdrawnUsd + feesEarned - depositedUsd - gasUsd;
        var hodl = Usd(dep0, ref0) + Usd(dep1, ref1);
        var il = Usd(cur0 + wd0 - dep0, ref0) + Usd(cur1 + wd1 - dep1, ref1);
        var pnlVsHodl = currentValue + withdrawnUsd + feesEarned - gasUsd - hodl;

        var end = closedAt ?? now;
        double? ageDays = openedAt is null ? null : (end - openedAt.Value).TotalDays;

        decimal? apr = null;
        if (previous is not null && ageDays is >= MinAprDays)
        {
            capitalDays += (double)capital * System.Math.Max(0, (end - previous.Value).TotalDays);
            var averageCapital = capitalDays / ageDays.Value;
            if (averageCapital > 0)
            {
                apr = TokenAmount.FromDouble((double)feesEarned / averageCapital / ageDays.Value * 365 * 100);
            }
        }

        return new PositionMetrics
        {
            Deposited0 = dep0,
            Deposited1 = dep1,
            Withdrawn0 = wd0,
            Withdrawn1 = wd1,
            CollectedFee0 = fee0,
            CollectedFee1 = fee1,
            DepositedUsd = depositedUsd,
            WithdrawnUsd = withdrawnUsd,
            CollectedFeesUsd = collectedUsd,
            UncollectedFeesUsd = uncollectedUsd,
            ClaimedRewardsUsd = rewardsUsd,
            PendingRewardsUsd = pendingUsd,
            FeesEarnedUsd = feesEarned,
            CurrentValueUsd = currentValue,
            GasUsd = gasUsd,
            PnlUsd = pnl,
            PnlPercent = depositedUsd > 0 ? pnl / depositedUsd * 100 : null,
            HodlValueUsd = hodl,
            ImpermanentLossUsd = il,
            PnlVsHodlUsd = pnlVsHodl,
            FeeAprPercent = apr,
            AgeDays = ageDays,
            HasMissingPrices = missing
        };
    }
}
