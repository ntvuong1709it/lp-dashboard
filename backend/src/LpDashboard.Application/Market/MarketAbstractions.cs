using LpDashboard.Application.Risk;

namespace LpDashboard.Application.Market;

/// <summary>One row of the economic calendar as the source gave it, cleaned of HTML escapes. A value that is not published yet is null.</summary>
/// <param name="TimeEastern">"08:30", or text such as "All Day". The source calls the column "gmt" but it is US Eastern.</param>
public sealed record RawEconomicEvent(DateOnly Day, string TimeEastern, string Country, string Name, string? Actual, string? Consensus, string? Previous);

/// <param name="Timing">"pre-market", "after-hours" or "not-supplied".</param>
/// <param name="MarketCap">Dollar text such as "$1,016,233,530,000", as the source gave it.</param>
public sealed record RawEarning(DateOnly Day, string Timing, string Symbol, string Company, string? MarketCap, string? EpsForecast);

public sealed record RawHeadline(string Title, string Source, string Url, DateTimeOffset PublishedAt, string? ImageUrl = null);

/// <summary>The economic calendar of one day (Nasdaq). Like every source it returns a failure instead of throwing.</summary>
public interface IEconomicCalendarSource
{
    Task<SourceOutcome<IReadOnlyList<RawEconomicEvent>>> GetDayAsync(DateOnly day, CancellationToken ct);
}

/// <summary>The earnings calendar of one day (Nasdaq): who reports, when, with market cap and forecast EPS. There is no actual EPS.</summary>
public interface IEarningsCalendarSource
{
    Task<SourceOutcome<IReadOnlyList<RawEarning>>> GetDayAsync(DateOnly day, CancellationToken ct);
}

/// <summary>Latest items of the configured news feeds, newest first.</summary>
public interface IHeadlineSource
{
    Task<SourceOutcome<IReadOnlyList<RawHeadline>>> GetLatestAsync(CancellationToken ct);
}
