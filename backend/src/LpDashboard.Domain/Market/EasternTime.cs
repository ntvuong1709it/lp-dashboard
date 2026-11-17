namespace LpDashboard.Domain.Market;

/// <summary>
/// Nasdaq's calendar labels its time column "gmt" but it is US Eastern (UK Core CPI, released at 07:00 UK time, reads
/// 02:00). This turns an Eastern date and time into a UTC instant, daylight saving included.
/// </summary>
public static class EasternTime
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <summary>The US Eastern calendar date at this instant: the calendar's "today".</summary>
    public static DateOnly DateOf(DateTimeOffset utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, Zone).DateTime);

    public static DateTimeOffset ToUtc(DateOnly day, TimeOnly time)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        // GetUtcOffset never throws for a time that does not exist (the spring-forward hour) or happens twice.
        return new DateTimeOffset(local, Zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
