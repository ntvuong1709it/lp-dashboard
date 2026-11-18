using LpDashboard.Domain.Market;

namespace LpDashboard.Application.Market;

/// <param name="Upcoming">Soonest first.</param>
/// <param name="Happened">The last few days, newest first.</param>
/// <param name="Loan">The loan with the worst risk, stressed; null when there is no usable loan or no owner was given.</param>
/// <param name="FearGreed">The crypto Fear and Greed index; null exactly when its source status is "unavailable".</param>
public sealed record MarketWatchDto(
    DateTimeOffset UpdatedAt,
    MarketSourcesDto Sources,
    IReadOnlyList<MarketEventDto> Upcoming,
    IReadOnlyList<MarketEventDto> Happened,
    IReadOnlyList<HeadlineDto> Headlines,
    LoanStressDto? Loan,
    FearGreedDto? FearGreed);

/// <summary>Each value is "ok", "stale" (old or partial data) or "unavailable".</summary>
public sealed record MarketSourcesDto(string Calendar, string Earnings, string Headlines, string FearGreed);

/// <param name="Day">The UTC day of the reading.</param>
public sealed record FearGreedDayDto(DateOnly Day, int Value, FearGreedZone Zone);

/// <param name="Bands">The five zones with their value ranges, so the chart draws the same edges the words use.</param>
/// <param name="Days">Oldest first, one per day.</param>
/// <param name="Today">The newest reading. It is the reading of its <c>Day</c>, which can be yesterday for a viewer ahead of UTC.</param>
/// <param name="Yesterday">The reading 1, 7 and 30 days before <paramref name="Today"/>; null when that day is missing.</param>
public sealed record FearGreedDto(
    IReadOnlyList<FearGreedBand> Bands,
    IReadOnlyList<FearGreedDayDto> Days,
    FearGreedDayDto Today,
    FearGreedDayDto? Yesterday,
    FearGreedDayDto? LastWeek,
    FearGreedDayDto? LastMonth);

/// <param name="Timing">Earnings only: "Before open", "After close" or "Time not set". Null for macro events.</param>
/// <param name="Category">"Macro" or "Earnings".</param>
/// <param name="Impact">"High" or "Medium".</param>
/// <param name="Tag">"RiskOn", "RiskOff" or "Neutral" once the result is in; null otherwise.</param>
/// <param name="Reason">The numbers behind the tag; null exactly when there is no tag.</param>
public sealed record MarketEventDto(
    string Id,
    DateTimeOffset At,
    string? Timing,
    string Name,
    string Category,
    string Impact,
    string? Previous,
    string? Forecast,
    string? Actual,
    string? Tag,
    string? Reason);

public sealed record HeadlineDto(string Title, string Source, string Url, DateTimeOffset PublishedAt, string? ImageUrl);

public sealed record LoanStressDto(string LoanLabel, LoanStress Stress);
