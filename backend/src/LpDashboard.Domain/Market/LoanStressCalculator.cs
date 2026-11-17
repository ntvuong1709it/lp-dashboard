namespace LpDashboard.Domain.Market;

public enum BufferLevel { Comfortable, Thin, Tight }

public enum StressStatus { Ok, Watch, MarginCall, Liquidation }

/// <param name="Value">The loan-to-value percent (LTV loans) or the health factor (Aave loans) after the fall.</param>
/// <param name="AddCollateralUsd">Collateral to add, at post-fall prices, to stay under the margin-call level (LTV loans) or to restore the target health factor.</param>
public sealed record StressRow(int FallPercent, decimal Value, StressStatus Status, decimal AddCollateralUsd);

/// <param name="Kind">"Ltv" or "HealthFactor".</param>
public sealed record LoanStress(
    string Kind,
    decimal Value,
    decimal DropToLiquidationPercent,
    BufferLevel Buffer,
    decimal? MarginCallLtvPercent,
    decimal? LiquidationLtvPercent,
    decimal CollateralUsd,
    decimal DebtUsd,
    IReadOnlyList<StressRow> Rows);

/// <summary>
/// What a price fall does to one loan. The collateral is assumed to fall one for one with price, which is the worst case:
/// LP collateral usually falls less.
/// </summary>
public static class LoanStressCalculator
{
    /// <param name="kind">The Borrow risk kind: "Ltv" or "HealthFactor". Anything else gives null.</param>
    /// <returns>Null when there is nothing to stress: no kind, no value, no collateral price, or an LTV loan without a liquidation level.</returns>
    public static LoanStress? Compute(
        string kind,
        decimal? value,
        decimal? marginCallLtvPercent,
        decimal? liquidationLtvPercent,
        decimal? collateralUsd,
        decimal debtUsd,
        MarketOptions o)
    {
        if (value is not > 0 || collateralUsd is not > 0) return null;
        var v = value.Value;
        var collateral = collateralUsd.Value;

        return kind switch
        {
            "Ltv" when liquidationLtvPercent is > 0 => ForLtv(v, marginCallLtvPercent, liquidationLtvPercent.Value, collateral, debtUsd, o),
            "HealthFactor" => ForHealthFactor(v, collateral, debtUsd, o),
            _ => null,
        };
    }

    private static LoanStress ForLtv(decimal ltv, decimal? margin, decimal liq, decimal collateral, decimal debt, MarketOptions o)
    {
        var target = margin ?? liq;
        var rows = o.FallPercents.Select(fall =>
        {
            var left = 1m - fall / 100m;
            var stressed = ltv / left;
            var status = stressed >= liq ? StressStatus.Liquidation
                : margin is not null && stressed >= margin ? StressStatus.MarginCall
                : stressed >= target - o.WatchWithinPoints ? StressStatus.Watch
                : StressStatus.Ok;
            var add = Math.Max(0m, debt / (target / 100m) - collateral * left);
            return new StressRow(fall, stressed, status, add);
        }).ToList();

        var drop = Math.Max(0m, (1m - ltv / liq) * 100m);
        return new LoanStress("Ltv", ltv, drop, BufferFor(drop, o), margin, liq, collateral, debt, rows);
    }

    private static LoanStress ForHealthFactor(decimal hf, decimal collateral, decimal debt, MarketOptions o)
    {
        var rows = o.FallPercents.Select(fall =>
        {
            var left = 1m - fall / 100m;
            var stressed = hf * left;
            var status = stressed < 1m ? StressStatus.Liquidation : stressed < o.HfWatchBelow ? StressStatus.Watch : StressStatus.Ok;
            var add = Math.Max(0m, o.HfTarget * collateral / hf - collateral * left);
            return new StressRow(fall, stressed, status, add);
        }).ToList();

        var drop = Math.Max(0m, (1m - 1m / hf) * 100m);
        return new LoanStress("HealthFactor", hf, drop, BufferFor(drop, o), null, null, collateral, debt, rows);
    }

    private static BufferLevel BufferFor(decimal dropToLiquidation, MarketOptions o) =>
        dropToLiquidation >= o.BufferThinBelowPercent ? BufferLevel.Comfortable
        : dropToLiquidation >= o.BufferTightBelowPercent ? BufferLevel.Thin
        : BufferLevel.Tight;
}
