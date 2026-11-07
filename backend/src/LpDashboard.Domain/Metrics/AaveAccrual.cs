namespace LpDashboard.Domain.Metrics;

/// <summary>
/// Aave balances grow with an index (liquidity index for supplied tokens, variable debt index for debt), so what a balance
/// earned between two days is <c>previous balance * (index / previous index - 1)</c>. The rest of the balance change is a flow.
/// </summary>
public static class AaveAccrual
{
    /// <summary>Tokens accrued on <paramref name="previousBalance"/> as the index moved; zero without a previous index.</summary>
    public static decimal Accrued(decimal previousBalance, decimal previousIndex, decimal index) =>
        previousBalance <= 0 || previousIndex <= 0 || index <= previousIndex ? 0 : previousBalance * (index / previousIndex - 1m);

    /// <summary>Aave rates are per year in ray (1e27); this gives percent.</summary>
    public static decimal RayRateToPercent(System.Numerics.BigInteger rayRate) =>
        (decimal)(rayRate / System.Numerics.BigInteger.Pow(10, 17)) / 1e8m;
}
