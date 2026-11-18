namespace LpDashboard.Domain.Market;

public enum PremiumZone { SellingHard, SellingABit, Flat, BuyingABit, BuyingHard }

/// <summary>The percent range of one zone on the gauge. The bands run from -<see cref="PremiumZones.GaugeSpan"/> to +<see cref="PremiumZones.GaugeSpan"/> without a gap.</summary>
public sealed record PremiumBand(PremiumZone Zone, decimal From, decimal To);

/// <summary>
/// The five zones of the Coinbase premium (Coinbase's Bitcoin price against Binance's, both in dollars, in percent). Positive
/// means US buyers are paying more. Words and colours both come from here. The edges are first guesses, to be tuned against
/// real readings.
/// </summary>
public static class PremiumZones
{
    /// <summary>The gauge shows -0.30 to +0.30 percent; a reading beyond it sits at the end of its zone.</summary>
    public const decimal GaugeSpan = 0.30m;

    /// <summary>Flat runs from minus this to plus this, both ends included.</summary>
    public const decimal FlatEdge = 0.03m;

    /// <summary>At or beyond plus or minus this is "hard".</summary>
    public const decimal HardEdge = 0.10m;

    public static IReadOnlyList<PremiumBand> Bands { get; } =
    [
        new(PremiumZone.SellingHard, -GaugeSpan, -HardEdge),
        new(PremiumZone.SellingABit, -HardEdge, -FlatEdge),
        new(PremiumZone.Flat, -FlatEdge, FlatEdge),
        new(PremiumZone.BuyingABit, FlatEdge, HardEdge),
        new(PremiumZone.BuyingHard, HardEdge, GaugeSpan),
    ];

    /// <summary>The zone of a percent that has already been rounded for display, so the word always matches the number.</summary>
    public static PremiumZone Of(decimal percent) =>
        percent <= -HardEdge ? PremiumZone.SellingHard
        : percent < -FlatEdge ? PremiumZone.SellingABit
        : percent <= FlatEdge ? PremiumZone.Flat
        : percent < HardEdge ? PremiumZone.BuyingABit
        : PremiumZone.BuyingHard;
}

public static class PremiumMath
{
    /// <summary>
    /// Coinbase's Bitcoin price against Binance's in real dollars, in percent. Binance quotes in USDT, so its price is first
    /// multiplied by what Coinbase says one USDT is worth; otherwise a USDT that is off $1 would read as US buying or selling.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A price is not above 0.</exception>
    public static decimal HourlyPercent(decimal coinbaseBtcUsd, decimal binanceBtcUsdt, decimal coinbaseUsdtUsd)
    {
        if (coinbaseBtcUsd <= 0) throw new ArgumentOutOfRangeException(nameof(coinbaseBtcUsd), coinbaseBtcUsd, "A price must be above 0.");
        if (binanceBtcUsdt <= 0) throw new ArgumentOutOfRangeException(nameof(binanceBtcUsdt), binanceBtcUsdt, "A price must be above 0.");
        if (coinbaseUsdtUsd <= 0) throw new ArgumentOutOfRangeException(nameof(coinbaseUsdtUsd), coinbaseUsdtUsd, "A price must be above 0.");

        var binanceUsd = binanceBtcUsdt * coinbaseUsdtUsd;
        return (coinbaseBtcUsd - binanceUsd) / binanceUsd * 100m;
    }

    /// <summary>Two decimals, halves away from zero: the precision shown to the owner.</summary>
    public static decimal Round(decimal percent) => Math.Round(percent, 2, MidpointRounding.AwayFromZero);
}

/// <param name="At">The candle's open time, UTC.</param>
public sealed record PremiumHour(DateTimeOffset At, decimal Percent);

/// <summary>A rounded average and its zone.</summary>
public sealed record PremiumReading(decimal Percent, PremiumZone Zone);

/// <summary>The average of one UTC day. Today's is of the hours so far.</summary>
public sealed record PremiumDay(DateOnly Day, decimal Percent, PremiumZone Zone);

/// <param name="Days">Oldest first, the 30 UTC days ending with today; a day with no hours is left out.</param>
/// <param name="Today">The average of the hourly gaps of the last 24 hours (rolling, not the UTC day).</param>
/// <param name="Yesterday">The previous full UTC day; null when under half its hours exist.</param>
/// <param name="Week">The last 168 hours; null under 84 of them.</param>
/// <param name="Month">The last 720 hours; null under 360 of them.</param>
/// <param name="AsOf">The newest hour used.</param>
public sealed record PremiumSummary(
    IReadOnlyList<PremiumBand> Bands,
    IReadOnlyList<PremiumDay> Days,
    PremiumReading Today,
    PremiumReading? Yesterday,
    PremiumReading? Week,
    PremiumReading? Month,
    DateTimeOffset AsOf);

public static class PremiumSeries
{
    private const int ChartDays = 30;
    private const int MinHoursToday = 12;
    private const int MinHoursYesterday = 12;
    private const int MinHoursWeek = 84;
    private const int MinHoursMonth = 360;

    /// <summary>
    /// Turns hourly gaps into the numbers the panel shows. Returns null when the last 24 hours hold fewer than 12 hours:
    /// an average of a few hours would look like a reading. A duplicate hour keeps the first row; hours after
    /// <paramref name="now"/> are ignored.
    /// </summary>
    public static PremiumSummary? Summarise(IEnumerable<PremiumHour> hours, DateTimeOffset now)
    {
        now = now.ToUniversalTime();
        var list = hours
            .Select(h => h with { At = h.At.ToUniversalTime() })
            .Where(h => h.At <= now)
            .GroupBy(h => h.At)
            .Select(g => g.First())
            .ToList();

        PremiumReading? Window(int hoursBack, int minHours)
        {
            var w = list.Where(h => h.At > now.AddHours(-hoursBack)).ToList();
            return w.Count >= minHours ? Reading(w.Average(h => h.Percent)) : null;
        }

        if (Window(24, MinHoursToday) is not { } today) return null;

        var utcToday = DateOnly.FromDateTime(now.UtcDateTime);
        var yesterdayDay = utcToday.AddDays(-1);
        var yesterdayHours = list.Where(h => DateOnly.FromDateTime(h.At.UtcDateTime) == yesterdayDay).ToList();
        var yesterday = yesterdayHours.Count >= MinHoursYesterday ? Reading(yesterdayHours.Average(h => h.Percent)) : null;

        var firstDay = utcToday.AddDays(-(ChartDays - 1));
        var days = list
            .GroupBy(h => DateOnly.FromDateTime(h.At.UtcDateTime))
            .Where(g => g.Key >= firstDay)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var r = Reading(g.Average(h => h.Percent));
                return new PremiumDay(g.Key, r.Percent, r.Zone);
            })
            .ToList();

        return new PremiumSummary(
            PremiumZones.Bands, days, today, yesterday, Window(168, MinHoursWeek), Window(720, MinHoursMonth), list.Max(h => h.At));
    }

    private static PremiumReading Reading(decimal average)
    {
        var rounded = PremiumMath.Round(average);
        return new PremiumReading(rounded, PremiumZones.Of(rounded));
    }
}
