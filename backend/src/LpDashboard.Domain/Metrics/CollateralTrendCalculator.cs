namespace LpDashboard.Domain.Metrics;

/// <summary>
/// End of one day for one balance (a supplied asset or a borrowed asset): the tokens held, the price, and how many
/// tokens accrued that day (supply yield for collateral, interest for debt).
/// Whatever else changed the balance (<c>tokens - previous tokens - accrual</c>) is a deposit, withdrawal, borrow or repayment.
/// </summary>
public sealed record BalanceDay(DateOnly Date, decimal Tokens, decimal PriceUsd, decimal AccrualTokens)
{
    public decimal ValueUsd => Tokens * PriceUsd;
}

/// <summary>A loan (or an Aave account) as two contiguous daily series.</summary>
public sealed record TrendLeg(string Label, IReadOnlyList<BalanceDay> Collateral, IReadOnlyList<BalanceDay> Debt);

public sealed record TrendPoint(DateOnly Date, decimal CollateralUsd, decimal DebtUsd)
{
    /// <summary>Collateral divided by debt (higher is safer); null without debt.</summary>
    public decimal? Ratio => DebtUsd > 0 ? CollateralUsd / DebtUsd : null;

    /// <summary>Debt as a percent of collateral; null without collateral.</summary>
    public decimal? LtvPercent => CollateralUsd > 0 ? DebtUsd / CollateralUsd * 100m : null;
}

public enum TrendEventKind { CollateralAdded, CollateralRemoved, Borrowed, Repaid }

public sealed record TrendEvent(DateOnly Date, TrendEventKind Kind, decimal Usd, string Label);

/// <summary>Where the change in collateral and debt over a window came from. Each side sums exactly to end minus start.</summary>
public sealed record ChangeSplit(
    DateOnly From,
    DateOnly To,
    decimal CollateralStartUsd,
    decimal PriceMovesUsd,
    decimal DepositsUsd,
    decimal SupplyYieldUsd,
    decimal CollateralEndUsd,
    decimal DebtStartUsd,
    decimal InterestUsd,
    decimal BorrowedUsd,
    decimal DebtPriceMovesUsd,
    decimal DebtEndUsd);

public sealed record Repayment(decimal RepayUsd, decimal AddCollateralUsd);

public static class CollateralTrendCalculator
{
    /// <summary>Flows below this many dollars are rounding noise, not a deposit or a borrow.</summary>
    private const decimal EventThresholdUsd = 1m;

    public static IReadOnlyList<TrendPoint> Series(IReadOnlyList<TrendLeg> legs, DateOnly from, DateOnly to)
    {
        var points = new List<TrendPoint>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            decimal collateral = 0, debt = 0;
            foreach (var leg in legs)
            {
                collateral += Find(leg.Collateral, day)?.ValueUsd ?? 0;
                debt += Find(leg.Debt, day)?.ValueUsd ?? 0;
            }
            points.Add(new TrendPoint(day, collateral, debt));
        }
        return points;
    }

    /// <summary>
    /// Chained day by day: price move = previous tokens * (price - previous price); accrual and flows are valued at that day's
    /// price. The three parts add up to the change exactly, and each deposit counts at the price of its day.
    /// </summary>
    public static ChangeSplit Split(IReadOnlyList<TrendLeg> legs, DateOnly from, DateOnly to)
    {
        decimal cStart = 0, cEnd = 0, cPrice = 0, cFlow = 0, cYield = 0;
        decimal dStart = 0, dEnd = 0, dPrice = 0, dFlow = 0, dInterest = 0;

        foreach (var leg in legs)
        {
            cStart += Find(leg.Collateral, from)?.ValueUsd ?? 0;
            cEnd += Find(leg.Collateral, to)?.ValueUsd ?? 0;
            dStart += Find(leg.Debt, from)?.ValueUsd ?? 0;
            dEnd += Find(leg.Debt, to)?.ValueUsd ?? 0;

            Walk(leg.Collateral, from, to, ref cPrice, ref cFlow, ref cYield);
            Walk(leg.Debt, from, to, ref dPrice, ref dFlow, ref dInterest);
        }

        return new ChangeSplit(from, to, cStart, cPrice, cFlow, cYield, cEnd, dStart, dInterest, dFlow, dPrice, dEnd);
    }

    /// <summary>Days on which collateral or debt moved by something other than accrual or price.</summary>
    public static IReadOnlyList<TrendEvent> Events(IReadOnlyList<TrendLeg> legs, DateOnly from, DateOnly to)
    {
        var events = new List<TrendEvent>();
        foreach (var leg in legs)
        {
            for (var day = from.AddDays(1); day <= to; day = day.AddDays(1))
            {
                if (Flow(leg.Collateral, day) is { } c && Math.Abs(c) >= EventThresholdUsd)
                    events.Add(new TrendEvent(day, c > 0 ? TrendEventKind.CollateralAdded : TrendEventKind.CollateralRemoved, c, leg.Label));
                if (Flow(leg.Debt, day) is { } d && Math.Abs(d) >= EventThresholdUsd)
                    events.Add(new TrendEvent(day, d > 0 ? TrendEventKind.Borrowed : TrendEventKind.Repaid, d, leg.Label));
            }
        }
        return events.OrderBy(e => e.Date).ToList();
    }

    /// <summary>
    /// Repay or add collateral to reach a health factor of <paramref name="target"/> (health factor = collateral * threshold / debt).
    /// <paramref name="liquidationThreshold"/> is a fraction, e.g. 0.753.
    /// </summary>
    public static Repayment? ForHealthFactor(decimal collateralUsd, decimal debtUsd, decimal liquidationThreshold, decimal target)
    {
        if (debtUsd <= 0 || liquidationThreshold <= 0 || target <= 0) return null;
        var current = collateralUsd * liquidationThreshold / debtUsd;
        if (current >= target) return new Repayment(0, 0);
        return new Repayment(debtUsd - collateralUsd * liquidationThreshold / target, target * debtUsd / liquidationThreshold - collateralUsd);
    }

    /// <summary>Repay or add collateral to bring debt / collateral down to <paramref name="targetLtvPercent"/>.</summary>
    public static Repayment? ForLtv(decimal collateralUsd, decimal debtUsd, decimal targetLtvPercent)
    {
        if (debtUsd <= 0 || collateralUsd <= 0 || targetLtvPercent <= 0) return null;
        var target = targetLtvPercent / 100m;
        if (debtUsd / collateralUsd <= target) return new Repayment(0, 0);
        return new Repayment(debtUsd - target * collateralUsd, debtUsd / target - collateralUsd);
    }

    private static void Walk(IReadOnlyList<BalanceDay> days, DateOnly from, DateOnly to, ref decimal price, ref decimal flow, ref decimal accrual)
    {
        var byDate = days.ToDictionary(d => d.Date);
        for (var day = from.AddDays(1); day <= to; day = day.AddDays(1))
        {
            if (!byDate.TryGetValue(day, out var today) || !byDate.TryGetValue(day.AddDays(-1), out var yesterday)) continue;
            price += yesterday.Tokens * (today.PriceUsd - yesterday.PriceUsd);
            accrual += today.AccrualTokens * today.PriceUsd;
            flow += (today.Tokens - yesterday.Tokens - today.AccrualTokens) * today.PriceUsd;
        }
    }

    private static decimal? Flow(IReadOnlyList<BalanceDay> days, DateOnly day)
    {
        var today = Find(days, day);
        var yesterday = Find(days, day.AddDays(-1));
        return today is null || yesterday is null ? null : (today.Tokens - yesterday.Tokens - today.AccrualTokens) * today.PriceUsd;
    }

    private static BalanceDay? Find(IReadOnlyList<BalanceDay> days, DateOnly day)
    {
        if (days.Count == 0) return null;
        var offset = day.DayNumber - days[0].Date.DayNumber;
        if (offset >= 0 && offset < days.Count && days[offset].Date == day) return days[offset];
        return days.FirstOrDefault(d => d.Date == day);
    }
}
