using LpDashboard.Domain.Market;

namespace LpDashboard.Domain.Tests;

public class PremiumSeriesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int month, int day, int hour) => new(2026, month, day, hour, 0, 0, TimeSpan.Zero);

    /// <summary>One hour for every hour from <paramref name="from"/> to <paramref name="to"/>, both included.</summary>
    private static List<PremiumHour> Series(DateTimeOffset from, DateTimeOffset to, Func<DateTimeOffset, decimal> percent)
    {
        var list = new List<PremiumHour>();
        for (var t = from; t <= to; t = t.AddHours(1)) list.Add(new PremiumHour(t, percent(t)));
        return list;
    }

    private static List<PremiumHour> Series(DateTimeOffset from, DateTimeOffset to, decimal percent) => Series(from, to, _ => percent);

    /// <summary>The last <paramref name="count"/> hours up to and including <paramref name="now"/>.</summary>
    private static List<PremiumHour> Last(int count, decimal percent, DateTimeOffset? now = null)
    {
        var end = now ?? Now;
        return Series(end.AddHours(-(count - 1)), end, percent);
    }

    [Fact]
    public void Today_is_the_rolling_24_hours()
    {
        var hours = Series(At(11, 16, 15), Now, t => t < At(11, 17, 16) ? 0.20m : 0.04m);

        var s = PremiumSeries.Summarise(hours, Now)!;

        Assert.Equal(0.04m, s.Today.Percent);
        Assert.Equal(PremiumZone.BuyingABit, s.Today.Zone);
        Assert.Equal(Now, s.AsOf);
    }

    [Fact]
    public void Fewer_than_12_hours_in_the_last_24_is_null()
    {
        var older = Series(At(11, 10, 0), At(11, 17, 15), 0.05m);

        Assert.Null(PremiumSeries.Summarise(older.Concat(Series(At(11, 18, 5), Now, 0.05m)), Now)); // 11 in the window
        Assert.NotNull(PremiumSeries.Summarise(older.Concat(Series(At(11, 18, 4), Now, 0.05m)), Now)); // 12 in the window
    }

    [Theory]
    [InlineData(0.0349, 0.03, PremiumZone.Flat)]
    [InlineData(0.035, 0.04, PremiumZone.BuyingABit)]
    [InlineData(-0.10, -0.10, PremiumZone.SellingHard)]
    public void Zone_follows_the_rounded_value(double raw, double shown, PremiumZone zone)
    {
        var s = PremiumSeries.Summarise(Last(24, (decimal)raw), Now)!;

        Assert.Equal((decimal)shown, s.Today.Percent);
        Assert.Equal(zone, s.Today.Zone);
    }

    [Fact]
    public void Days_are_utc_day_averages_oldest_first_and_today_is_partial()
    {
        var hours = Series(At(10, 10, 0), Now, t => t >= At(11, 18, 0) ? 0.10m : 0.05m);

        var s = PremiumSeries.Summarise(hours, Now)!;

        Assert.Equal(30, s.Days.Count);
        Assert.Equal(new DateOnly(2026, 10, 20), s.Days[0].Day);
        Assert.Equal(new DateOnly(2026, 11, 18), s.Days[^1].Day);
        Assert.Equal(0.10m, s.Days[^1].Percent); // only its 16 hours, 00:00 to 15:00
        Assert.Equal(PremiumZone.BuyingHard, s.Days[^1].Zone);
        Assert.Equal(0.05m, s.Days[0].Percent);
        Assert.True(s.Days.Zip(s.Days.Skip(1), (a, b) => a.Day < b.Day).All(x => x));
    }

    [Fact]
    public void A_day_with_no_hours_is_absent()
    {
        var hours = Series(At(10, 10, 0), Now, 0.05m).Where(h => h.At.Day != 5 || h.At.Month != 11).ToList();

        var s = PremiumSeries.Summarise(hours, Now)!;

        Assert.Equal(29, s.Days.Count);
        Assert.DoesNotContain(s.Days, d => d.Day == new DateOnly(2026, 11, 5));
    }

    [Fact]
    public void Yesterday_is_the_previous_full_utc_day()
    {
        var hours = Series(At(11, 10, 0), Now, t => t.Day == 17 && t.Month == 11 ? 0.10m : 0.04m);

        var s = PremiumSeries.Summarise(hours, Now)!;

        Assert.Equal(0.10m, s.Yesterday!.Percent);
        Assert.Equal(PremiumZone.BuyingHard, s.Yesterday.Zone);
    }

    [Fact]
    public void Yesterday_with_fewer_than_12_hours_is_null()
    {
        var hours = Series(At(11, 10, 0), At(11, 16, 23), 0.04m)
            .Concat(Series(At(11, 17, 13), At(11, 17, 23), 0.10m)) // 11 hours
            .Concat(Series(At(11, 18, 0), Now, 0.04m)).ToList();

        Assert.Null(PremiumSeries.Summarise(hours, Now)!.Yesterday);
    }

    [Fact]
    public void Just_after_utc_midnight_today_is_still_24_hours_and_yesterday_is_the_full_day()
    {
        var now = new DateTimeOffset(2026, 11, 18, 0, 30, 0, TimeSpan.Zero);
        var hours = Series(At(11, 10, 0), At(11, 16, 23), 0.04m)
            .Concat(Series(At(11, 17, 0), At(11, 17, 23), 0.10m))
            .Concat([new PremiumHour(At(11, 18, 0), -0.20m)]).ToList();

        var s = PremiumSeries.Summarise(hours, now)!;

        Assert.Equal(0.09m, s.Today.Percent); // 23 hours at 0.10 and one at -0.20: 2.10 / 24 = 0.0875
        Assert.Equal(0.10m, s.Yesterday!.Percent);
        Assert.Equal(new DateOnly(2026, 11, 18), s.Days[^1].Day);
        Assert.Equal(-0.20m, s.Days[^1].Percent); // a one-hour day, not hidden and not averaged into yesterday
    }

    [Fact]
    public void Week_needs_84_of_the_last_168_hours()
    {
        Assert.Equal(0.05m, PremiumSeries.Summarise(Last(100, 0.05m), Now)!.Week!.Percent);
        Assert.Null(PremiumSeries.Summarise(Last(83, 0.05m), Now)!.Week);
    }

    [Fact]
    public void Month_needs_360_of_the_last_720_hours()
    {
        Assert.Equal(0.05m, PremiumSeries.Summarise(Last(400, 0.05m), Now)!.Month!.Percent);
        Assert.Null(PremiumSeries.Summarise(Last(359, 0.05m), Now)!.Month);
    }

    [Fact]
    public void Duplicates_and_future_hours_are_ignored()
    {
        var hours = Last(24, 0.04m)
            .Concat([new PremiumHour(Now, 0.50m)]) // same hour again: the first one wins
            .Concat([new PremiumHour(Now.AddHours(1), 0.90m)]) // not yet happened
            .ToList();

        var s = PremiumSeries.Summarise(hours, Now)!;

        Assert.Equal(0.04m, s.Today.Percent);
        Assert.Equal(Now, s.AsOf);
    }

    [Fact]
    public void Bands_are_the_domain_bands() =>
        Assert.Equal(PremiumZones.Bands, PremiumSeries.Summarise(Last(24, 0.05m), Now)!.Bands);
}
