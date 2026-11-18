using System.Globalization;
using LpDashboard.Application.Risk;
using LpDashboard.Domain.Market;
using Microsoft.Extensions.Options;

namespace LpDashboard.Application.Market;

/// <summary>
/// Builds the Market watch: the economic and earnings calendars of the days around today, the latest headlines and the
/// stressed loan. Sources are read through <see cref="MarketCache"/>; one that is down leaves the others working and is
/// reported as "stale" or "unavailable", never as a guess.
/// </summary>
public sealed class MarketWatchService(
    IEconomicCalendarSource economic,
    IEarningsCalendarSource earnings,
    IHeadlineSource headlines,
    IFearGreedSource fearGreed,
    ILoanSource loans,
    MarketCache cache,
    IOptions<MarketOptions> options,
    IOptions<MarketSettings> settings,
    TimeProvider time)
{
    private const int PastDays = 3;
    private const int MaxHeadlines = 10;

    /// <summary>A result that is still missing this long after its time is not coming: the row is dropped.</summary>
    private static readonly TimeSpan PendingResultGrace = TimeSpan.FromHours(3);

    private static readonly TimeOnly PreMarket = new(8, 0);
    private static readonly TimeOnly AfterClose = new(16, 30);
    private static readonly string[] ClockFormats = ["HH:mm", "H:mm"];

    private sealed record DayResult<T>(DateOnly Day, IReadOnlyList<T>? Rows, CacheState State);

    /// <param name="days">7 or 14; anything else is 14.</param>
    /// <param name="owners">Null leaves the loan out.</param>
    public async Task<MarketWatchDto> GetAsync(int days, IReadOnlyCollection<string>? owners, TimeZoneInfo zone, CancellationToken ct)
    {
        var o = options.Value;
        var now = time.GetUtcNow();
        var window = days == 7 ? 7 : 14;
        var today = EasternTime.DateOf(now);
        var fetchDays = Enumerable.Range(-PastDays, PastDays + window + 1)
            .Select(today.AddDays)
            .Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .ToList();

        using var gate = new SemaphoreSlim(Math.Max(1, settings.Value.MaxConcurrentDays));
        var econTasks = fetchDays.Select(d => FetchDayAsync("econ", d, today, gate, token => economic.GetDayAsync(d, token),
            rows => rows.Any(r => r.Actual is not null), ct)).ToList();
        var earnTasks = fetchDays.Select(d => FetchDayAsync("earn", d, today, gate, token => earnings.GetDayAsync(d, token),
            _ => true, ct)).ToList();
        var headlineTask = FetchHeadlinesAsync(ct);
        var fearGreedTask = FetchFearGreedAsync(ct);
        var loanTask = LoanAsync(owners, zone, o, ct);

        var econ = await Task.WhenAll(econTasks);
        var earn = await Task.WhenAll(earnTasks);
        var (headlineRows, headlineState) = await headlineTask;
        var (fearGreedRows, fearGreedState) = await fearGreedTask;
        var fearGreedBlock = BuildFearGreed(fearGreedRows, DateOnly.FromDateTime(now.UtcDateTime));

        var upcoming = new List<MarketEventDto>();
        var happened = new List<MarketEventDto>();
        AddMacro(econ.SelectMany(r => r.Rows ?? []), now, o, upcoming, happened);
        AddEarnings(earn.SelectMany(r => r.Rows ?? []), now, o, upcoming, happened);

        return new MarketWatchDto(
            now,
            new MarketSourcesDto(
                StatusOf(econ), StatusOf(earn), StatusOf(headlineRows, headlineState),
                FearGreedStatus(fearGreedBlock, fearGreedRows, fearGreedState, DateOnly.FromDateTime(now.UtcDateTime))),
            upcoming.OrderBy(e => e.At).ToList(),
            happened.OrderByDescending(e => e.At).ToList(),
            (headlineRows ?? []).OrderByDescending(h => h.PublishedAt).Take(MaxHeadlines)
                .Select(h => new HeadlineDto(h.Title, h.Source, h.Url, h.PublishedAt, h.ImageUrl)).ToList(),
            await loanTask,
            fearGreedBlock);
    }

    // ---- calendars

    private async Task<DayResult<T>> FetchDayAsync<T>(
        string prefix, DateOnly day, DateOnly today, SemaphoreSlim gate,
        Func<CancellationToken, Task<SourceOutcome<IReadOnlyList<T>>>> call, Func<IReadOnlyList<T>, bool> hasResult, CancellationToken ct)
    {
        var s = settings.Value;
        TimeSpan TtlFor(IReadOnlyList<T> rows) =>
            day > today ? TimeSpan.FromMinutes(s.UpcomingTtlMinutes)
            : day == today || !hasResult(rows) ? TimeSpan.FromMinutes(s.TodayTtlMinutes)
            : TimeSpan.FromHours(s.PastTtlHours);

        await gate.WaitAsync(ct);
        try
        {
            var (rows, state) = await cache.GetOrFetchAsync<IReadOnlyList<T>>(
                $"{prefix}:{day:yyyy-MM-dd}", TtlFor, TimeSpan.FromHours(s.StaleMaxHours), token => AnswerAsync(call, token), ct);
            return new DayResult<T>(day, rows, state);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<(IReadOnlyList<RawHeadline>? Rows, CacheState State)> FetchHeadlinesAsync(CancellationToken ct)
    {
        var s = settings.Value;
        return await cache.GetOrFetchAsync<IReadOnlyList<RawHeadline>>(
            "headlines", TimeSpan.FromMinutes(s.HeadlinesTtlMinutes), TimeSpan.FromHours(s.StaleMaxHours),
            token => AnswerAsync(headlines.GetLatestAsync, token), ct);
    }

    private async Task<(IReadOnlyList<RawFearGreedDay>? Rows, CacheState State)> FetchFearGreedAsync(CancellationToken ct)
    {
        var s = settings.Value;
        return await cache.GetOrFetchAsync<IReadOnlyList<RawFearGreedDay>>(
            "fng", TimeSpan.FromMinutes(s.FearGreedTtlMinutes), TimeSpan.FromHours(s.StaleMaxHours),
            token => AnswerAsync(fearGreed.GetDaysAsync, token), ct);
    }

    /// <summary>Readings older than this many days before the UTC date, or later than tomorrow, are not believed (a bad row would become the chart's first point or "today").</summary>
    private const int FearGreedOldestDays = 45;

    /// <summary>A newest reading older than this many days means the source answers but has stopped updating.</summary>
    private const int FearGreedStaleAfterDays = 2;

    /// <summary>"ok" only when the fetch is fresh and the newest reading is recent: a source that keeps answering with old numbers is not "ok".</summary>
    private static string FearGreedStatus(FearGreedDto? block, IReadOnlyList<RawFearGreedDay>? rows, CacheState state, DateOnly utcToday)
    {
        if (block is null) return "unavailable";
        var status = StatusOf(rows, state);
        return status == "ok" && block.Today.Day < utcToday.AddDays(-FearGreedStaleAfterDays) ? "stale" : status;
    }

    /// <summary>Oldest first, one reading per day (the first of a day wins). Null when no usable reading is left.</summary>
    private static FearGreedDto? BuildFearGreed(IReadOnlyList<RawFearGreedDay>? rows, DateOnly utcToday)
    {
        if (rows is null) return null;
        var days = rows
            .Where(d => d.Value is >= 0 and <= 100)
            .Where(d => d.Day >= utcToday.AddDays(-FearGreedOldestDays) && d.Day <= utcToday.AddDays(1))
            .DistinctBy(d => d.Day)
            .OrderBy(d => d.Day)
            .Select(d => new FearGreedDayDto(d.Day, d.Value, FearGreedZones.Of(d.Value)))
            .ToList();
        if (days.Count == 0) return null;

        var today = days[^1];
        FearGreedDayDto? Back(int daysBack) => days.FirstOrDefault(d => d.Day == today.Day.AddDays(-daysBack));
        return new FearGreedDto(FearGreedZones.Bands, days, today, Back(1), Back(7), Back(30));
    }

    /// <summary>The source's value, or null when it did not answer or took longer than the source timeout.</summary>
    private async Task<T?> AnswerAsync<T>(Func<CancellationToken, Task<SourceOutcome<T>>> call, CancellationToken ct) where T : class
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(settings.Value.SourceTimeoutSeconds));
        try
        {
            var outcome = await call(cts.Token);
            return outcome.Answered ? outcome.Value : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static string StatusOf<T>(IReadOnlyList<DayResult<T>> days) =>
        days.All(d => d.State == CacheState.None) ? "unavailable"
        : days.Any(d => d.State != CacheState.Fresh) ? "stale"
        : "ok";

    private static string StatusOf<T>(T? value, CacheState state) where T : class =>
        value is null ? "unavailable" : state == CacheState.Stale ? "stale" : "ok";

    // ---- events

    private static void AddMacro(
        IEnumerable<RawEconomicEvent> rows, DateTimeOffset now, MarketOptions o, List<MarketEventDto> upcoming, List<MarketEventDto> happened)
    {
        // The same release can appear twice (headline and month-on-month); the row with a forecast is the one to show.
        foreach (var group in rows.GroupBy(r => (r.Day, r.TimeEastern, Name: r.Name.ToLowerInvariant())))
        {
            var row = group.FirstOrDefault(r => r.Consensus is not null) ?? group.First();
            if (EventClassifier.ClassifyMacro(row.Country, row.Name, o) is not { } classified) continue;
            if (!TimeOnly.TryParseExact(row.TimeEastern, ClockFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock)) continue;

            var at = EasternTime.ToUtc(row.Day, clock);
            var tag = row.Actual is null ? new TagResult(RiskTag.None, null) : ResultTagger.Tag(classified.Family, row.Actual, row.Consensus, o);
            var dto = new MarketEventDto(
                $"macro:{row.Day:yyyy-MM-dd}:{row.TimeEastern}:{row.Name}", at, null, row.Name, "Macro", classified.Impact.ToString(),
                row.Previous, row.Consensus, row.Actual, tag.Tag == RiskTag.None ? null : tag.Tag.ToString(), tag.Reason);

            if (row.Actual is not null) happened.Add(dto);
            else if (at >= now - PendingResultGrace) upcoming.Add(dto);
        }
    }

    private static void AddEarnings(
        IEnumerable<RawEarning> rows, DateTimeOffset now, MarketOptions o, List<MarketEventDto> upcoming, List<MarketEventDto> happened)
    {
        foreach (var group in rows.GroupBy(r => (r.Day, Symbol: r.Symbol.ToUpperInvariant())))
        {
            var row = group.First();
            if (EventClassifier.ClassifyEarnings(row.Symbol, row.MarketCap, o) is not { } impact) continue;

            // The source gives a slot, not a time: before the open, after the close, or unknown (shown as after the close).
            var at = EasternTime.ToUtc(row.Day, row.Timing == "pre-market" ? PreMarket : AfterClose);
            var timing = row.Timing switch { "pre-market" => "Before open", "after-hours" => "After close", _ => "Time not set" };
            var company = string.IsNullOrWhiteSpace(row.Company) ? row.Symbol : row.Company;
            var dto = new MarketEventDto(
                $"earn:{row.Day:yyyy-MM-dd}:{row.Symbol}", at, timing, $"{company} earnings", "Earnings", impact.ToString(),
                null, row.EpsForecast, null, null, null);

            (at <= now ? happened : upcoming).Add(dto);
        }
    }

    // ---- loan

    private async Task<LoanStressDto?> LoanAsync(IReadOnlyCollection<string>? owners, TimeZoneInfo zone, MarketOptions o, CancellationToken ct)
    {
        if (owners is null || owners.Count == 0) return null;

        IReadOnlyList<Borrow.LoanDto> all;
        try
        {
            all = await loans.GetLoansAsync(owners, zone, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // the calendar is still worth showing
        }

        var candidates = all
            .Where(l => l.Risk.Kind is "Ltv" or "HealthFactor" && l.CollateralUsd is > 0)
            .OrderByDescending(l => l.Risk.Level)
            .ThenBy(l => l.Risk.DropToLiquidationPercent ?? decimal.MaxValue);
        foreach (var loan in candidates)
        {
            var stress = LoanStressCalculator.Compute(
                loan.Risk.Kind, loan.Risk.Value, loan.Risk.MarginCallLtvPercent, loan.Risk.LiquidationLtvPercent, loan.CollateralUsd, loan.DebtUsd, o);
            if (stress is not null) return new LoanStressDto(loan.Label, stress);
        }
        return null;
    }
}
