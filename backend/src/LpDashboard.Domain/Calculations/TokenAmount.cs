using System.Numerics;

namespace LpDashboard.Domain.Calculations;

public static class TokenAmount
{
    private const int MaxFractionDigits = 18;

    public static decimal FromRaw(BigInteger raw, int decimals)
    {
        if (decimals <= 0)
        {
            return Clamp(raw);
        }

        var divisor = BigInteger.Pow(10, decimals);
        var whole = BigInteger.DivRem(raw, divisor, out var remainder);
        var fractionDigits = System.Math.Min(decimals, MaxFractionDigits);
        var fraction = remainder * BigInteger.Pow(10, fractionDigits) / divisor;
        return Clamp(whole) + (decimal)fraction / Pow10(fractionDigits);
    }

    public static decimal FromDouble(double value)
    {
        if (double.IsNaN(value)) return 0m;
        if (value >= (double)decimal.MaxValue) return decimal.MaxValue;
        if (value <= (double)decimal.MinValue) return decimal.MinValue;
        return (decimal)value;
    }

    private static decimal Clamp(BigInteger value)
    {
        if (value > (BigInteger)decimal.MaxValue) return decimal.MaxValue;
        if (value < (BigInteger)decimal.MinValue) return decimal.MinValue;
        return (decimal)value;
    }

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++) result *= 10m;
        return result;
    }
}
