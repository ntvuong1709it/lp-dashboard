namespace LpDashboard.Domain.Metrics;

public enum CoverageStatus { NoDebt, Shortfall, Thin, Covered }

/// <summary>LP income against borrow interest over a window; every figure is USD accrued in the window.</summary>
public sealed record Coverage(
    decimal LpFeesUsd,
    decimal LpRewardsUsd,
    decimal SupplyYieldUsd,
    decimal InterestUsd,
    decimal GasUsd)
{
    public decimal LpIncomeUsd => LpFeesUsd + LpRewardsUsd + SupplyYieldUsd;
    public decimal NetCarryUsd => LpIncomeUsd - InterestUsd - GasUsd;
    /// <summary>Income divided by interest; null when no interest accrued.</summary>
    public decimal? Ratio => InterestUsd > 0 ? LpIncomeUsd / InterestUsd : null;
    public CoverageStatus Status => BorrowCoverageCalculator.StatusOf(Ratio);
}

public enum RepayMode { None, Surplus, Shortfall }

/// <summary>A suggestion only; the app never moves money.</summary>
public sealed record RepaySuggestion(RepayMode Mode, decimal AmountUsd, string? LoanLabel, decimal AprPercent, decimal SavingPerYearUsd);

public sealed record LoanRate(string Label, decimal AprPercent);

public enum RiskLevel { Ok, Warn, Critical }

public static class BorrowCoverageCalculator
{
    public const decimal CoveredAt = 1.25m;
    public const decimal ShortfallBelow = 1.0m;

    /// <summary>Health factor below these is a warning / critical (app-wide defaults).</summary>
    public const decimal HealthWarn = 1.5m;
    public const decimal HealthCritical = 1.2m;

    /// <summary>Manual loans: within this share of the liquidation LTV is a warning / critical.</summary>
    public const decimal LtvWarnShare = 0.15m;
    public const decimal LtvCriticalShare = 0.05m;

    public static CoverageStatus StatusOf(decimal? ratio) =>
        ratio is null ? CoverageStatus.NoDebt
        : ratio >= CoveredAt ? CoverageStatus.Covered
        : ratio >= ShortfallBelow ? CoverageStatus.Thin
        : CoverageStatus.Shortfall;

    public static RiskLevel HealthFactorLevel(decimal healthFactor) =>
        healthFactor < HealthCritical ? RiskLevel.Critical : healthFactor < HealthWarn ? RiskLevel.Warn : RiskLevel.Ok;

    public static RiskLevel LtvLevel(decimal ltvPercent, decimal liquidationLtvPercent) =>
        ltvPercent >= liquidationLtvPercent * (1 - LtvCriticalShare) ? RiskLevel.Critical
        : ltvPercent >= liquidationLtvPercent * (1 - LtvWarnShare) ? RiskLevel.Warn
        : RiskLevel.Ok;

    /// <summary>Percent the collateral can fall before an LTV loan is liquidated.</summary>
    public static decimal LtvDropToLiquidationPercent(decimal ltvPercent, decimal liquidationLtvPercent) =>
        ltvPercent <= 0 ? 100 : Math.Max(0, (1 - ltvPercent / liquidationLtvPercent) * 100m);

    /// <summary>Percent all collateral can fall before the health factor reaches 1 (assumes only collateral moves).</summary>
    public static decimal HealthFactorDropToLiquidationPercent(decimal healthFactor) =>
        healthFactor <= 1 ? 0 : (1 - 1 / healthFactor) * 100m;

    /// <summary>
    /// Surplus: put the window's net carry toward the highest-rate loan. Shortfall: the yearly interest to cut to reach 1.0x,
    /// and how much of the highest-rate loan that is. Needs at least one loan with a positive rate.
    /// </summary>
    public static RepaySuggestion Repay(Coverage coverage, int windowDays, IReadOnlyCollection<LoanRate> loans)
    {
        var top = loans.Where(l => l.AprPercent > 0).OrderByDescending(l => l.AprPercent).FirstOrDefault();
        if (top is null || windowDays <= 0 || coverage.InterestUsd <= 0) return new RepaySuggestion(RepayMode.None, 0, null, 0, 0);

        var apr = top.AprPercent / 100m;
        if (coverage.Ratio >= 1 && coverage.NetCarryUsd > 0)
            return new RepaySuggestion(RepayMode.Surplus, coverage.NetCarryUsd, top.Label, top.AprPercent, coverage.NetCarryUsd * apr);

        var perYear = (coverage.InterestUsd - coverage.LpIncomeUsd) / windowDays * 365m;
        if (perYear <= 0) return new RepaySuggestion(RepayMode.None, 0, null, 0, 0);
        return new RepaySuggestion(RepayMode.Shortfall, perYear / apr, top.Label, top.AprPercent, perYear);
    }
}
