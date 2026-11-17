using LpDashboard.Domain.Market;

namespace LpDashboard.Domain.Tests;

public class EasternTimeTests
{
    private static DateTimeOffset At(int y, int m, int d, int hh, int mm) =>
        new(y, m, d, hh, mm, 0, TimeSpan.Zero);

    [Fact]
    public void Date_of_an_instant_is_the_eastern_date()
    {
        Assert.Equal(new DateOnly(2026, 11, 17), EasternTime.DateOf(At(2026, 11, 18, 3, 30))); // 22:30 the evening before
        Assert.Equal(new DateOnly(2026, 11, 18), EasternTime.DateOf(At(2026, 11, 18, 5, 30)));
    }

    [Theory]
    [InlineData(2026, 11, 19, 13, 30)] // winter: UTC-5
    [InlineData(2026, 11, 2, 13, 30)]  // first Monday after DST ended on 1 Nov
    [InlineData(2026, 10, 30, 12, 30)] // last Friday before: UTC-4
    [InlineData(2026, 3, 6, 13, 30)]   // Friday before DST started on 8 Mar
    [InlineData(2026, 3, 9, 12, 30)]   // Monday after
    public void Half_past_eight_eastern_to_utc(int y, int m, int d, int utcHour, int utcMinute) =>
        Assert.Equal(At(y, m, d, utcHour, utcMinute), EasternTime.ToUtc(new DateOnly(y, m, d), new TimeOnly(8, 30)));
}
