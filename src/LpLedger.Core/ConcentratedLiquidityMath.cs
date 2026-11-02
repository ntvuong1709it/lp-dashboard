using System.Numerics;

namespace LpLedger.Core;

/// <summary>Concentrated-liquidity math in floating point. Double precision (~1e-15) is plenty for display values.</summary>
public static class ConcentratedLiquidityMath
{
    private static readonly double Q96 = Math.Pow(2, 96);

    public static double FromUnits(BigInteger value, int decimals) => (double)value / Math.Pow(10, decimals);

    public static double TickToSqrtPrice(int tick) => Math.Pow(1.0001, tick / 2.0);

    public static double SqrtPriceFromX96(BigInteger x) => (double)x / Q96;

    /// <summary>Human price of token0 in token1.</summary>
    public static double PriceFromSqrtX96(BigInteger x, int dec0, int dec1)
    {
        var s = SqrtPriceFromX96(x);
        return s * s * Math.Pow(10, dec0 - dec1);
    }

    public static double PriceFromTick(int tick, int dec0, int dec1) =>
        Math.Pow(1.0001, tick) * Math.Pow(10, dec0 - dec1);

    /// <summary>Token amounts (human units) held by <paramref name="liquidity"/> between two ticks at the current sqrt price.</summary>
    public static (double Amount0, double Amount1) AmountsForLiquidity(
        BigInteger liquidity, double sqrtPrice, int tickLower, int tickUpper, int dec0, int dec1)
    {
        var sa = TickToSqrtPrice(tickLower);
        var sb = TickToSqrtPrice(tickUpper);
        var l = (double)liquidity;
        double a0 = 0, a1 = 0;
        if (sqrtPrice <= sa) a0 = l * (1 / sa - 1 / sb);
        else if (sqrtPrice >= sb) a1 = l * (sb - sa);
        else
        {
            a0 = l * (1 / sqrtPrice - 1 / sb);
            a1 = l * (sqrtPrice - sa);
        }
        return (a0 / Math.Pow(10, dec0), a1 / Math.Pow(10, dec1));
    }
}
