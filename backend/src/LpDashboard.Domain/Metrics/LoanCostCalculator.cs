namespace LpDashboard.Domain.Metrics;

/// <summary>Interest a loan costs per period at today's debt and rate.</summary>
public sealed record LoanCost(decimal PerDay, decimal PerWeek, decimal PerMonth, decimal PerYear)
{
    public static readonly LoanCost Zero = new(0, 0, 0, 0);

    public static LoanCost operator +(LoanCost a, LoanCost b) =>
        new(a.PerDay + b.PerDay, a.PerWeek + b.PerWeek, a.PerMonth + b.PerMonth, a.PerYear + b.PerYear);

    /// <summary>The same cost scaled to a period; used for LP income at the same pace.</summary>
    public static LoanCost FromPerDay(decimal perDay) => new(perDay, perDay * 7, perDay * 365 / 12, perDay * 365);
}

/// <summary>
/// perYear = debt * APR, perDay = perYear / 365, perWeek = 7 days, perMonth = a twelfth of the year.
/// Simple APR: Aave compounds, so its real cost is slightly higher.
/// </summary>
public static class LoanCostCalculator
{
    public static LoanCost Cost(decimal debtUsd, decimal aprPercent)
    {
        if (debtUsd <= 0 || aprPercent <= 0) return LoanCost.Zero;
        var year = debtUsd * aprPercent / 100m;
        var day = year / 365m;
        return new LoanCost(day, day * 7, year / 12m, year);
    }

    public static LoanCost Sum(IEnumerable<LoanCost> costs) => costs.Aggregate(LoanCost.Zero, (a, c) => a + c);
}
