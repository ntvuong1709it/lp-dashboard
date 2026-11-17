using LpDashboard.Domain.Market;

namespace LpDashboard.Application.Market;

/// <param name="Upcoming">Soonest first.</param>
/// <param name="Happened">The last few days, newest first.</param>
/// <param name="Loan">The loan with the worst risk, stressed; null when there is no usable loan or no owner was given.</param>
public sealed record MarketWatchDto(
    DateTimeOffset UpdatedAt,
    MarketSourcesDto Sources,
    IReadOnlyList<MarketEventDto> Upcoming,
    IReadOnlyList<MarketEventDto> Happened,
    IReadOnlyList<HeadlineDto> Headlines,
    LoanStressDto? Loan);

/// <summary>Each value is "ok", "stale" (old or partial data) or "unavailable".</summary>
public sealed record MarketSourcesDto(string Calendar, string Earnings, string Headlines);

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
