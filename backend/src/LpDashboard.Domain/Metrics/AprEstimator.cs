namespace LpDashboard.Domain.Metrics;

public sealed record AprEstimate(
    double Concentration,
    double TimeInRange,
    double ReferenceHalfWidth,
    double Scale,
    double Apr,
    double AprLow,
    double AprHigh,
    double DepositShare);

/// <param name="DaysInRange">Days whose closing price was inside the range.</param>
/// <param name="FirstExitDay">1-based day of the first close outside the range; null if it never left.</param>
/// <param name="LowMove">Lowest close as a fraction of the starting price, minus 1 (never above 0).</param>
/// <param name="HighMove">Highest close as a fraction of the starting price, minus 1 (never below 0).</param>
public sealed record PoolHistory(int DaysInRange, int Days, int? FirstExitDay, double LowMove, double HighMove);

/// <summary>
/// Estimates the fee APR of a concentrated-liquidity range from pool-wide numbers.
///
/// A pool's published fee APR is fees ÷ TVL, which already reflects how concentrated its typical LP is. So the
/// estimate scales it by how much better (or worse) the chosen range does than a reference range:
/// <c>apr = poolApr × C(w)·T(w) ÷ C(ref)·T(ref)</c>, where C is the liquidity concentration of a symmetric range
/// ±w versus full range and T is the expected share of time the price stays inside it. The reference range is a
/// multiple of the pool's own volatility, which is an assumption and is returned so the UI can show it.
/// Fees only: impermanent loss is not subtracted.
/// </summary>
public static class AprEstimator
{
    public const double ReferenceSigmas = 1.5;
    public const double ReferenceHorizonDays = 30;
    public const double MinHalfWidth = 0.0005;
    public const double MaxHalfWidth = 0.95;

    /// <summary>Liquidity per dollar of a range ±<paramref name="halfWidth"/> around the price, relative to full range (v3 math).</summary>
    public static double Concentration(double halfWidth)
    {
        var w = Math.Clamp(halfWidth, MinHalfWidth, MaxHalfWidth);
        var ratio = (1 - w) / (1 + w);
        return 1 / (1 - Math.Pow(ratio, 0.25));
    }

    /// <summary>
    /// Average, over each day of the horizon, of the probability that a driftless lognormal price that started at the
    /// centre of the range is still inside it. <paramref name="annualVolatility"/> is a fraction (0.6 = 60%).
    /// </summary>
    public static double TimeInRange(double halfWidth, double annualVolatility, int days)
    {
        if (days < 1) throw new ArgumentOutOfRangeException(nameof(days));
        if (annualVolatility <= 0) return 1;

        var w = Math.Clamp(halfWidth, MinHalfWidth, MaxHalfWidth);
        var upper = Math.Log(1 + w);
        var lower = Math.Log(1 - w);
        var dailySigma = annualVolatility / Math.Sqrt(365);

        double sum = 0;
        for (var t = 1; t <= days; t++)
        {
            var s = dailySigma * Math.Sqrt(t);
            sum += NormalCdf(upper / s) - NormalCdf(lower / s);
        }
        return sum / days;
    }

    /// <summary>Annualised standard deviation of daily log returns of a price series (daily spacing), as a fraction.</summary>
    public static double AnnualisedVolatility(IReadOnlyList<double> dailyPrices)
    {
        var returns = new List<double>();
        for (var i = 1; i < dailyPrices.Count; i++)
        {
            if (dailyPrices[i - 1] > 0 && dailyPrices[i] > 0) returns.Add(Math.Log(dailyPrices[i] / dailyPrices[i - 1]));
        }
        if (returns.Count < 2) return 0;

        var mean = returns.Average();
        var variance = returns.Sum(r => (r - mean) * (r - mean)) / (returns.Count - 1);
        return Math.Sqrt(variance) * Math.Sqrt(365);
    }

    /// <summary>Half-width, as a fraction, of the symmetric range ±w that has the same upper/lower price ratio as the tick range.</summary>
    public static double HalfWidthFromTicks(int tickLower, int tickUpper)
    {
        var ratio = Math.Pow(1.0001, tickLower - tickUpper); // lower price / upper price
        return Math.Clamp((1 - ratio) / (1 + ratio), MinHalfWidth, MaxHalfWidth);
    }

    public static double ReferenceHalfWidth(double annualVolatility)
    {
        var sigmaHorizon = annualVolatility * Math.Sqrt(ReferenceHorizonDays / 365);
        return Math.Clamp(Math.Exp(ReferenceSigmas * sigmaHorizon) - 1, 0.002, 0.5);
    }

    /// <param name="poolApr">Pool fee APR in percent on the basis the caller chose (for example the 7-day rate).</param>
    /// <param name="poolAprLow">Lowest pool APR of the other rates on hand, in percent, for the band.</param>
    /// <param name="poolAprHigh">Highest, in percent.</param>
    public static AprEstimate Estimate(
        double poolApr, double poolAprLow, double poolAprHigh,
        double halfWidth, double annualVolatility, int days, double capitalUsd, double tvlUsd)
    {
        var reference = ReferenceHalfWidth(annualVolatility);
        var c = Concentration(halfWidth);
        var t = TimeInRange(halfWidth, annualVolatility, days);
        var cRef = Concentration(reference);
        var tRef = TimeInRange(reference, annualVolatility, days);

        var scale = c * t / (cRef * tRef);
        var share = tvlUsd > 0 ? capitalUsd * c / (tvlUsd * cRef) : 0;
        return new AprEstimate(c, t, reference, scale, poolApr * scale, Math.Min(poolAprLow, poolApr) * scale, Math.Max(poolAprHigh, poolApr) * scale, share);
    }

    /// <summary>
    /// Impermanent loss, as a fraction of the starting value, of a position opened at the centre of a range of
    /// ±<paramref name="halfWidth"/> once the price has moved by <paramref name="move"/> (0.05 = up 5%, -0.05 = down 5%).
    /// The loss is measured against simply holding the starting tokens and is never negative.
    /// </summary>
    public static double ImpermanentLoss(double halfWidth, double move)
    {
        var w = Math.Clamp(halfWidth, MinHalfWidth, MaxHalfWidth);
        double pa = 1 - w, pb = 1 + w, sa = Math.Sqrt(pa), sb = Math.Sqrt(pb);
        var p = Math.Max(1 + move, 1e-9);

        var x0 = 1 - 1 / sb;
        var y0 = 1 - sa;
        var start = x0 + y0;

        double x, y;
        if (p <= pa) { x = 1 / sa - 1 / sb; y = 0; }
        else if (p >= pb) { x = 0; y = sb - sa; }
        else { x = 1 / Math.Sqrt(p) - 1 / sb; y = Math.Sqrt(p) - sa; }

        var held = x0 * p + y0;
        var value = x * p + y;
        return Math.Max(0, (held - value) / start);
    }

    /// <summary>
    /// The smallest price move in one direction at which impermanent loss reaches <paramref name="feeFraction"/> (the fees
    /// earned as a fraction of capital), as a fraction; null when fees outweigh the loss however far the price goes.
    /// </summary>
    public static double? BreakEvenMove(double halfWidth, double feeFraction, bool up)
    {
        if (feeFraction <= 0) return 0;
        double lo = 0, hi = up ? 5 : 0.99;
        double Loss(double m) => ImpermanentLoss(halfWidth, up ? m : -m);
        if (Loss(hi) < feeFraction) return null;

        for (var i = 0; i < 60; i++)
        {
            var mid = (lo + hi) / 2;
            if (Loss(mid) < feeFraction) lo = mid; else hi = mid;
        }
        return hi;
    }

    /// <summary>How a range of ±<paramref name="halfWidth"/> around the first price would have fared over a daily price series.</summary>
    public static PoolHistory Backtest(IReadOnlyList<double> dailyPrices, double halfWidth)
    {
        var w = Math.Clamp(halfWidth, MinHalfWidth, MaxHalfWidth);
        var start = dailyPrices[0];
        double lower = start * (1 - w), upper = start * (1 + w);

        int inRange = 0, days = 0;
        int? firstExit = null;
        double low = 0, high = 0;
        for (var i = 1; i < dailyPrices.Count; i++)
        {
            var p = dailyPrices[i];
            days++;
            if (p >= lower && p <= upper) inRange++;
            else firstExit ??= i;
            low = Math.Min(low, p / start - 1);
            high = Math.Max(high, p / start - 1);
        }
        return new PoolHistory(inRange, days, firstExit, low, high);
    }

    private static double NormalCdf(double x) => 0.5 * (1 + Erf(x / Math.Sqrt(2)));

    // Abramowitz and Stegun 7.1.26, absolute error below 1.5e-7.
    private static double Erf(double x)
    {
        var sign = Math.Sign(x);
        x = Math.Abs(x);
        var t = 1 / (1 + 0.3275911 * x);
        var y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return sign * y;
    }
}
