using System.Numerics;

namespace LpDashboard.Domain.Calculations;

/// <summary>Concentrated-liquidity math (Uniswap v3 style). Doubles are adequate for display/valuation.</summary>
public static class PositionMath
{
    private static readonly double Q96 = System.Math.Pow(2, 96);

    public static double TickToSqrtPrice(int tick) => System.Math.Pow(1.0001, tick / 2.0);

    public static double SqrtPriceX96ToSqrtPrice(BigInteger sqrtPriceX96) => (double)sqrtPriceX96 / Q96;

    /// <summary>Price of token0 denominated in token1, adjusted for decimals.</summary>
    public static double TickToPrice(int tick, int decimals0, int decimals1) =>
        System.Math.Pow(1.0001, tick) * System.Math.Pow(10, decimals0 - decimals1);

    public static double SqrtPriceX96ToPrice(BigInteger sqrtPriceX96, int decimals0, int decimals1)
    {
        var sqrt = SqrtPriceX96ToSqrtPrice(sqrtPriceX96);
        return sqrt * sqrt * System.Math.Pow(10, decimals0 - decimals1);
    }

    public static bool IsInRange(int currentTick, int tickLower, int tickUpper) =>
        currentTick >= tickLower && currentTick < tickUpper;

    /// <summary>Raw (non-decimal-adjusted) token amounts held by a position.</summary>
    public static (double Amount0, double Amount1) GetAmountsRaw(BigInteger liquidity, double sqrtPrice, int tickLower, int tickUpper) =>
        GetAmountsRaw((double)liquidity, sqrtPrice, tickLower, tickUpper);

    public static (double Amount0, double Amount1) GetAmountsRaw(double l, double sqrtPrice, int tickLower, int tickUpper)
    {
        if (l <= 0) return (0, 0);

        var sa = TickToSqrtPrice(tickLower);
        var sb = TickToSqrtPrice(tickUpper);

        if (sqrtPrice <= sa) return (l * (sb - sa) / (sa * sb), 0);
        if (sqrtPrice >= sb) return (0, l * (sb - sa));
        return (l * (sb - sqrtPrice) / (sqrtPrice * sb), l * (sqrtPrice - sa));
    }

    /// <summary>
    /// Liquidity implied by raw token amounts added to / removed from a range. When both amounts are non-zero the
    /// price was in range, so the implied sqrt price is solved from their ratio first.
    /// </summary>
    public static double GetLiquidityRaw(double amount0, double amount1, int tickLower, int tickUpper)
    {
        var sa = TickToSqrtPrice(tickLower);
        var sb = TickToSqrtPrice(tickUpper);
        if (sb <= sa || (amount0 <= 0 && amount1 <= 0)) return 0;
        if (amount1 <= 0) return amount0 * sa * sb / (sb - sa);
        if (amount0 <= 0) return amount1 / (sb - sa);

        // a0 = L(sb - s)/(s*sb), a1 = L(s - sa)  =>  a0*sb*s^2 + (a1 - a0*sa*sb)*s - a1*sb = 0
        var a = amount0 * sb;
        var b = amount1 - amount0 * sa * sb;
        var c = -amount1 * sb;
        var s = (-b + System.Math.Sqrt(b * b - 4 * a * c)) / (2 * a);
        return s > sa ? amount1 / (s - sa) : amount0 * sa * sb / (sb - sa);
    }

    /// <summary>Raw sqrt price from a decimal-adjusted price of token0 in token1.</summary>
    public static double PriceToSqrtPrice(double price, int decimals0, int decimals1) =>
        System.Math.Sqrt(price * System.Math.Pow(10, decimals1 - decimals0));

    public static int SqrtPriceToTick(double sqrtPrice) =>
        (int)System.Math.Floor(System.Math.Log(sqrtPrice * sqrtPrice) / System.Math.Log(1.0001));

    public static (decimal Amount0, decimal Amount1) GetAmounts(
        BigInteger liquidity, BigInteger sqrtPriceX96, int tickLower, int tickUpper, int decimals0, int decimals1)
    {
        var (raw0, raw1) = GetAmountsRaw(liquidity, SqrtPriceX96ToSqrtPrice(sqrtPriceX96), tickLower, tickUpper);
        return (
            TokenAmount.FromDouble(raw0 / System.Math.Pow(10, decimals0)),
            TokenAmount.FromDouble(raw1 / System.Math.Pow(10, decimals1)));
    }
}
