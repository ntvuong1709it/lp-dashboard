using System.Collections.Concurrent;
using LpDashboard.Application.Borrow;
using LpDashboard.Application.Market;
using LpDashboard.Application.Risk;
using LpDashboard.Domain.Market;
using LpDashboard.Domain.Metrics;
using Microsoft.Extensions.Options;

namespace LpDashboard.Domain.Tests;

public class MarketWatchServiceTests
{
    // Tuesday 2026-11-17, 10:00 Eastern.
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);
    private static DateOnly D(string s) => DateOnly.Parse(s);

    // ---- fakes

    private sealed class Gauge
    {
        private int _current, _max;
        public int Max => _max;
        public async Task<T> RunAsync<T>(Func<T> result)
        {
            var now = Interlocked.Increment(ref _current);
            int seen;
            while ((seen = _max) < now && Interlocked.CompareExchange(ref _max, now, seen) != seen) { }
            await Task.Delay(15);
            Interlocked.Decrement(ref _current);
            return result();
        }
    }

    private sealed class FakeEcon(Gauge gauge) : IEconomicCalendarSource
    {
        public Dictionary<DateOnly, RawEconomicEvent[]> Rows { get; } = [];
        public bool Fail { get; set; }
        public ConcurrentBag<DateOnly> Calls { get; } = [];

        public Task<SourceOutcome<IReadOnlyList<RawEconomicEvent>>> GetDayAsync(DateOnly day, CancellationToken ct)
        {
            Calls.Add(day);
            return gauge.RunAsync(() => Fail
                ? SourceOutcome<IReadOnlyList<RawEconomicEvent>>.Fail("down")
                : SourceOutcome<IReadOnlyList<RawEconomicEvent>>.Ok(Rows.GetValueOrDefault(day) ?? []));
        }
    }

    private sealed class FakeEarn(Gauge gauge) : IEarningsCalendarSource
    {
        public Dictionary<DateOnly, RawEarning[]> Rows { get; } = [];
        public bool Fail { get; set; }
        public ConcurrentBag<DateOnly> Calls { get; } = [];

        public Task<SourceOutcome<IReadOnlyList<RawEarning>>> GetDayAsync(DateOnly day, CancellationToken ct)
        {
            Calls.Add(day);
            return gauge.RunAsync(() => Fail
                ? SourceOutcome<IReadOnlyList<RawEarning>>.Fail("down")
                : SourceOutcome<IReadOnlyList<RawEarning>>.Ok(Rows.GetValueOrDefault(day) ?? []));
        }
    }

    private sealed class FakeHeadlines : IHeadlineSource
    {
        public List<RawHeadline> Items { get; } = [];
        public bool Fail { get; set; }

        public Task<SourceOutcome<IReadOnlyList<RawHeadline>>> GetLatestAsync(CancellationToken ct) =>
            Task.FromResult(Fail
                ? SourceOutcome<IReadOnlyList<RawHeadline>>.Fail("down")
                : SourceOutcome<IReadOnlyList<RawHeadline>>.Ok(Items.OrderByDescending(i => i.PublishedAt).ToList()));
    }

    private sealed class FakeFearGreed : IFearGreedSource
    {
        public List<RawFearGreedDay> Days { get; } = [];
        public bool Fail { get; set; }
        public int Calls { get; private set; }

        public Task<SourceOutcome<IReadOnlyList<RawFearGreedDay>>> GetDaysAsync(CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Fail
                ? SourceOutcome<IReadOnlyList<RawFearGreedDay>>.Fail("down")
                : SourceOutcome<IReadOnlyList<RawFearGreedDay>>.Ok(Days.ToList()));
        }
    }

    private sealed class FakeLoans(IReadOnlyList<LoanDto>? loans = null, bool throws = false) : ILoanSource
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<LoanDto>> GetLoansAsync(IReadOnlyCollection<string> owners, TimeZoneInfo zone, CancellationToken ct)
        {
            Calls++;
            if (throws) throw new InvalidOperationException("boom");
            return Task.FromResult(loans ?? []);
        }
    }

    private sealed class Rig
    {
        public Gauge Gauge { get; } = new();
        public FakeEcon Econ { get; }
        public FakeEarn Earn { get; }
        public FakeHeadlines Headlines { get; } = new();
        public FakeFearGreed FearGreed { get; } = new();
        public FakeLoans Loans { get; set; } = new();
        public FakeClock Clock { get; } = new(Now);
        public MarketCache Cache { get; }
        public MarketSettings Settings { get; } = new();

        public Rig()
        {
            Econ = new FakeEcon(Gauge);
            Earn = new FakeEarn(Gauge);
            Cache = new MarketCache(Clock);
        }

        public Task<MarketWatchDto> GetAsync(int days = 14, IReadOnlyCollection<string>? owners = null) =>
            new MarketWatchService(Econ, Earn, Headlines, FearGreed, Loans, Cache, Options.Create(new MarketOptions()),
                    Options.Create(Settings), Clock)
                .GetAsync(days, owners, TimeZoneInfo.Utc, CancellationToken.None);
    }

    private static RawEconomicEvent Ev(string day, string time, string name, string? actual = null, string? consensus = null) =>
        new(D(day), time, "United States", name, actual, consensus, null);

    private static RawEarning Earn(string day, string timing, string symbol, string company, string cap, string eps = "$1.00") =>
        new(D(day), timing, symbol, company, cap, eps);

    private static LoanDto Loan(string label, string kind, decimal? value, RiskLevel level, decimal? drop, decimal? collateral,
        decimal debt = 10_000m, decimal? margin = 70m, decimal? liq = 78m) =>
        new(label, "manual", "x", label, "0xabc", "USDC", null, debt, debt, 5m, 0m, new LoanCostDto(0, 0, 0, 0),
            collateral, null, null, new LoanRiskDto(kind, value, level, drop, liq, margin), false);

    // ---- which days are fetched

    [Fact]
    public async Task Weekdays_from_three_days_back_to_the_end_of_the_window_are_fetched_with_limited_concurrency()
    {
        var rig = new Rig();
        rig.Settings.MaxConcurrentDays = 3;

        await rig.GetAsync(days: 14);

        string[] expected = ["2026-11-16", "2026-11-17", "2026-11-18", "2026-11-19", "2026-11-20", "2026-11-23", "2026-11-24",
            "2026-11-25", "2026-11-26", "2026-11-27", "2026-11-30", "2026-12-01"];
        Assert.Equal(expected.Select(D), rig.Econ.Calls.OrderBy(d => d));
        Assert.Equal(expected.Select(D), rig.Earn.Calls.OrderBy(d => d));
        Assert.InRange(rig.Gauge.Max, 2, 3);
    }

    [Fact]
    public async Task A_window_of_seven_days_ends_a_week_out()
    {
        var rig = new Rig();

        await rig.GetAsync(days: 7);

        Assert.Equal(D("2026-11-24"), rig.Econ.Calls.Max());
    }

    [Fact]
    public async Task Any_other_window_is_fourteen_days()
    {
        var rig = new Rig();

        await rig.GetAsync(days: 30);

        Assert.Equal(D("2026-12-01"), rig.Econ.Calls.Max());
    }

    // ---- macro events

    [Fact]
    public async Task Macro_rows_are_filtered_placed_and_tagged()
    {
        var rig = new Rig();
        rig.Econ.Rows[D("2026-11-18")] =
        [
            Ev("2026-11-18", "08:30", "Core CPI", consensus: "3.0%"),
            Ev("2026-11-18", "09:10", "Fed Waller Speaks"),
            Ev("2026-11-18", "08:55", "Redbook"),
        ];
        rig.Econ.Rows[D("2026-11-17")] =
        [
            Ev("2026-11-17", "08:30", "Core CPI", actual: "3.1%", consensus: "3.0%"),
            Ev("2026-11-17", "14:00", "Fed Interest Rate Decision", consensus: "4.00%"),
        ];

        var r = await rig.GetAsync();

        var up = r.Upcoming.Single(e => e.Id.Contains("2026-11-18"));
        Assert.Equal("Core CPI", up.Name);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 13, 30, 0, TimeSpan.Zero), up.At);
        Assert.Equal("High", up.Impact);
        Assert.Equal("Macro", up.Category);
        Assert.Equal("3.0%", up.Forecast);
        Assert.Null(up.Actual);
        Assert.Null(up.Tag);

        var done = Assert.Single(r.Happened);
        Assert.Equal("RiskOff", done.Tag);
        Assert.Equal("3.1%", done.Actual);
        Assert.Contains("hotter", done.Reason);

        var decision = r.Upcoming.Single(e => e.Name == "Fed Interest Rate Decision");
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 19, 0, 0, TimeSpan.Zero), decision.At);
        Assert.Equal(2, r.Upcoming.Count); // no Waller, no Redbook
    }

    [Fact]
    public async Task Duplicate_rows_collapse_keeping_the_one_with_a_forecast()
    {
        var rig = new Rig();
        rig.Econ.Rows[D("2026-11-18")] =
        [
            Ev("2026-11-18", "08:30", "Retail Sales"),
            Ev("2026-11-18", "08:30", "Retail Sales", consensus: "0.1%"),
        ];

        var r = await rig.GetAsync();

        Assert.Equal("0.1%", Assert.Single(r.Upcoming).Forecast);
    }

    [Fact]
    public async Task A_result_that_never_came_is_dropped_after_three_hours_but_kept_while_pending()
    {
        var rig = new Rig();
        rig.Econ.Rows[D("2026-11-17")] =
        [
            Ev("2026-11-17", "09:00", "Retail Sales", consensus: "0.1%"), // 14:00Z, one hour ago
            Ev("2026-11-17", "06:30", "JOLTS", consensus: "7.0M"),        // 11:30Z, three and a half hours ago
        ];

        var r = await rig.GetAsync();

        Assert.Equal("Retail Sales", Assert.Single(r.Upcoming).Name);
    }

    [Fact]
    public async Task Rows_with_a_time_that_is_not_a_clock_time_are_skipped()
    {
        var rig = new Rig();
        rig.Econ.Rows[D("2026-11-18")] = [Ev("2026-11-18", "All Day", "Retail Sales", consensus: "0.1%")];

        var r = await rig.GetAsync();

        Assert.Empty(r.Upcoming);
    }

    [Fact]
    public async Task Upcoming_is_soonest_first_and_happened_newest_first()
    {
        var rig = new Rig();
        rig.Econ.Rows[D("2026-11-19")] = [Ev("2026-11-19", "08:30", "Core CPI", consensus: "3.0%")];
        rig.Econ.Rows[D("2026-11-18")] = [Ev("2026-11-18", "08:30", "Core PCE", consensus: "2.8%")];
        rig.Econ.Rows[D("2026-11-16")] = [Ev("2026-11-16", "08:30", "CPI", actual: "3.0%", consensus: "3.0%")];
        rig.Econ.Rows[D("2026-11-17")] = [Ev("2026-11-17", "08:30", "PPI", actual: "2.0%", consensus: "2.0%")];

        var r = await rig.GetAsync();

        Assert.Equal(["Core PCE", "Core CPI"], r.Upcoming.Select(e => e.Name));
        Assert.Equal(["PPI", "CPI"], r.Happened.Select(e => e.Name));
    }

    // ---- earnings

    [Fact]
    public async Task Earnings_are_filtered_timed_and_move_to_happened_once_reported()
    {
        var rig = new Rig();
        rig.Earn.Rows[D("2026-11-17")] =
            [Earn("2026-11-17", "pre-market", "WMT", "Walmart Inc.", "$1,016,233,530,000", "$0.64")];
        rig.Earn.Rows[D("2026-11-19")] =
        [
            Earn("2026-11-19", "after-hours", "INTU", "Intuit Inc.", "$91,314,545,000"),
            Earn("2026-11-19", "not-supplied", "CPRT", "Copart", "$29,486,612,500"), // too small
            Earn("2026-11-19", "not-supplied", "ZZZ", "Zed Corp", "$60,000,000,000"),
            Earn("2026-11-19", "not-supplied", "ZZZ", "Zed Corp", "$60,000,000,000"), // duplicate
        ];

        var r = await rig.GetAsync();

        var wmt = Assert.Single(r.Happened);
        Assert.Equal("Walmart Inc. earnings", wmt.Name);
        Assert.Equal("Earnings", wmt.Category);
        Assert.Equal("High", wmt.Impact);
        Assert.Equal("Before open", wmt.Timing);
        Assert.Equal("$0.64", wmt.Forecast);
        Assert.Null(wmt.Actual);
        Assert.Null(wmt.Tag);

        Assert.Equal(["Intuit Inc. earnings", "Zed Corp earnings"], r.Upcoming.Select(e => e.Name));
        var intu = r.Upcoming[0];
        Assert.Equal("After close", intu.Timing);
        Assert.Equal("Medium", intu.Impact);
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 21, 30, 0, TimeSpan.Zero), intu.At); // 16:30 Eastern
        Assert.Equal("Time not set", r.Upcoming[1].Timing);
    }

    // ---- sources failing

    [Fact]
    public async Task A_dead_calendar_with_no_cache_is_unavailable_and_the_rest_still_works()
    {
        var rig = new Rig { };
        rig.Econ.Fail = true;
        rig.Headlines.Items.Add(new RawHeadline("Fed holds", "Federal Reserve", "https://x/1", Now.AddHours(-1)));

        var r = await rig.GetAsync();

        Assert.Equal("unavailable", r.Sources.Calendar);
        Assert.Equal("ok", r.Sources.Earnings);
        Assert.Equal("ok", r.Sources.Headlines);
        Assert.Single(r.Headlines);
    }

    [Fact]
    public async Task A_calendar_that_fails_after_working_serves_old_data_as_stale()
    {
        var rig = new Rig();
        rig.Econ.Rows[D("2026-11-18")] = [Ev("2026-11-18", "08:30", "Core CPI", consensus: "3.0%")];
        var first = await rig.GetAsync();
        Assert.Equal("ok", first.Sources.Calendar);

        rig.Econ.Fail = true;
        rig.Clock.Advance(TimeSpan.FromHours(2));
        var second = await rig.GetAsync();

        Assert.Equal("stale", second.Sources.Calendar);
        Assert.Equal("Core CPI", Assert.Single(second.Upcoming).Name);
    }

    [Fact]
    public async Task All_sources_down_gives_an_empty_page_not_an_error()
    {
        var rig = new Rig();
        rig.Econ.Fail = true;
        rig.Earn.Fail = true;
        rig.Headlines.Fail = true;
        rig.FearGreed.Fail = true;

        var r = await rig.GetAsync();

        Assert.Empty(r.Upcoming);
        Assert.Empty(r.Happened);
        Assert.Empty(r.Headlines);
        Assert.Null(r.FearGreed);
        Assert.Equal(new MarketSourcesDto("unavailable", "unavailable", "unavailable", "unavailable"), r.Sources);
    }

    [Fact]
    public async Task Headlines_are_limited_to_the_ten_newest()
    {
        var rig = new Rig();
        for (var i = 0; i < 15; i++)
            rig.Headlines.Items.Add(new RawHeadline($"h{i}", "Federal Reserve", $"https://x/{i}", Now.AddHours(-i)));

        var r = await rig.GetAsync();

        Assert.Equal(10, r.Headlines.Count);
        Assert.Equal("h0", r.Headlines[0].Title);
    }

    // ---- fear and greed (the rig's clock is 2026-11-17 15:00 UTC)

    private static RawFearGreedDay Fg(string day, int value) => new(D(day), value);

    [Fact]
    public async Task Fear_greed_days_come_oldest_first_with_zones_bands_and_comparisons()
    {
        var rig = new Rig();
        rig.FearGreed.Days.AddRange([Fg("2026-11-16", 12), Fg("2026-11-17", 11), Fg("2026-11-10", 29), Fg("2026-10-18", 59)]);

        var r = await rig.GetAsync();

        var fg = r.FearGreed!;
        Assert.Equal([D("2026-10-18"), D("2026-11-10"), D("2026-11-16"), D("2026-11-17")], fg.Days.Select(d => d.Day));
        Assert.Equal([FearGreedZone.Greed, FearGreedZone.Fear, FearGreedZone.ExtremeFear, FearGreedZone.ExtremeFear], fg.Days.Select(d => d.Zone));
        Assert.Equal(11, fg.Today.Value);
        Assert.Equal(12, fg.Yesterday!.Value);
        Assert.Equal(29, fg.LastWeek!.Value);
        Assert.Equal(59, fg.LastMonth!.Value);
        Assert.Equal(FearGreedZones.Bands, fg.Bands);
        Assert.Equal("ok", r.Sources.FearGreed);
    }

    [Fact]
    public async Task A_missing_comparison_day_is_null_not_a_neighbour()
    {
        var rig = new Rig();
        rig.FearGreed.Days.AddRange([Fg("2026-11-17", 11), Fg("2026-11-15", 20)]);

        var fg = (await rig.GetAsync()).FearGreed!;

        Assert.Null(fg.Yesterday);
        Assert.Null(fg.LastWeek);
        Assert.Null(fg.LastMonth);
        Assert.Equal(2, fg.Days.Count);
    }

    [Fact]
    public async Task Two_rows_for_one_day_collapse_to_the_first()
    {
        var rig = new Rig();
        rig.FearGreed.Days.AddRange([Fg("2026-11-17", 11), Fg("2026-11-17", 99)]);

        var fg = (await rig.GetAsync()).FearGreed!;

        Assert.Equal(11, Assert.Single(fg.Days).Value);
    }

    [Fact]
    public async Task Readings_with_an_implausible_date_are_dropped()
    {
        var rig = new Rig();
        rig.FearGreed.Days.AddRange([Fg("1970-01-01", 40), Fg("2030-01-01", 50), Fg("2026-11-17", 11), Fg("2026-11-16", 12)]);

        var r = await rig.GetAsync();

        var fg = r.FearGreed!;
        Assert.Equal([D("2026-11-16"), D("2026-11-17")], fg.Days.Select(d => d.Day));
        Assert.Equal(11, fg.Today.Value);
        Assert.Equal(12, fg.Yesterday!.Value);
        Assert.Equal("ok", r.Sources.FearGreed);
    }

    [Theory]
    [InlineData("2026-11-15", "ok")]    // two days before the rig's 2026-11-17: still today's reading for a viewer behind UTC
    [InlineData("2026-11-14", "stale")] // three days: the source answers but has stopped updating
    public async Task A_reading_that_stopped_updating_is_not_reported_as_ok(string latestDay, string expected)
    {
        var rig = new Rig();
        rig.FearGreed.Days.Add(Fg(latestDay, 11));

        var r = await rig.GetAsync();

        Assert.Equal(expected, r.Sources.FearGreed);
        Assert.NotNull(r.FearGreed);
    }

    [Fact]
    public async Task Fear_greed_does_not_need_an_owner()
    {
        var rig = new Rig();
        rig.FearGreed.Days.Add(Fg("2026-11-17", 11));

        var r = await rig.GetAsync(owners: null);

        Assert.NotNull(r.FearGreed);
        Assert.Null(r.Loan);
    }

    [Fact]
    public async Task Fear_greed_is_fetched_once_an_hour()
    {
        var rig = new Rig();
        rig.FearGreed.Days.Add(Fg("2026-11-17", 11));

        await rig.GetAsync();
        await rig.GetAsync();
        Assert.Equal(1, rig.FearGreed.Calls);

        rig.Clock.Advance(TimeSpan.FromMinutes(61));
        await rig.GetAsync();
        Assert.Equal(2, rig.FearGreed.Calls);
    }

    [Fact]
    public async Task A_dead_fear_greed_source_is_unavailable_and_the_rest_still_works()
    {
        var rig = new Rig();
        rig.FearGreed.Fail = true;
        rig.Headlines.Items.Add(new RawHeadline("Fed holds", "Federal Reserve", "https://x/1", Now.AddHours(-1)));

        var r = await rig.GetAsync();

        Assert.Null(r.FearGreed);
        Assert.Equal("unavailable", r.Sources.FearGreed);
        Assert.Equal("ok", r.Sources.Headlines);
        Assert.Single(r.Headlines);
    }

    [Fact]
    public async Task Fear_greed_that_fails_after_working_is_stale_then_unavailable_after_the_stale_limit()
    {
        var rig = new Rig();
        rig.FearGreed.Days.Add(Fg("2026-11-17", 11));
        Assert.Equal("ok", (await rig.GetAsync()).Sources.FearGreed);

        rig.FearGreed.Fail = true;
        rig.Clock.Advance(TimeSpan.FromHours(2));
        var stale = await rig.GetAsync();
        Assert.Equal("stale", stale.Sources.FearGreed);
        Assert.Equal(11, stale.FearGreed!.Today.Value);

        rig.Clock.Advance(TimeSpan.FromHours(25));
        var gone = await rig.GetAsync();
        Assert.Equal("unavailable", gone.Sources.FearGreed);
        Assert.Null(gone.FearGreed);
    }

    // ---- loan

    [Fact]
    public async Task Without_owners_there_is_no_loan_and_no_borrow_call()
    {
        var rig = new Rig { Loans = new FakeLoans([Loan("A", "Ltv", 52m, RiskLevel.Warn, 30m, 20_000m)]) };

        var r = await rig.GetAsync(owners: null);

        Assert.Null(r.Loan);
        Assert.Equal(0, rig.Loans.Calls);
    }

    [Fact]
    public async Task The_loan_with_the_worst_risk_is_chosen()
    {
        var rig = new Rig
        {
            Loans = new FakeLoans(
            [
                Loan("warn30", "Ltv", 52m, RiskLevel.Warn, 30m, 20_000m),
                Loan("critical15", "Ltv", 66m, RiskLevel.Critical, 15m, 20_000m),
                Loan("warn20", "Ltv", 60m, RiskLevel.Warn, 20m, 20_000m),
            ]),
        };

        var r = await rig.GetAsync(owners: ["0xabc"]);

        Assert.Equal("critical15", r.Loan!.LoanLabel);
        Assert.Equal("Ltv", r.Loan.Stress.Kind);
        Assert.Equal(66m, r.Loan.Stress.Value);
    }

    [Fact]
    public async Task Among_equal_levels_the_smaller_drop_to_liquidation_wins()
    {
        var rig = new Rig
        {
            Loans = new FakeLoans(
            [
                Loan("warn30", "Ltv", 52m, RiskLevel.Warn, 30m, 20_000m),
                Loan("warn20", "Ltv", 60m, RiskLevel.Warn, 20m, 20_000m),
            ]),
        };

        var r = await rig.GetAsync(owners: ["0xabc"]);

        Assert.Equal("warn20", r.Loan!.LoanLabel);
    }

    [Fact]
    public async Task Loans_that_cannot_be_stressed_are_skipped()
    {
        var rig = new Rig
        {
            Loans = new FakeLoans(
            [
                Loan("noprice", "Ltv", 90m, RiskLevel.Critical, 5m, null),
                Loan("none", "None", null, RiskLevel.Ok, null, 20_000m),
                Loan("ok", "Ltv", 35m, RiskLevel.Ok, 55m, 20_000m, debt: 7_000m),
            ]),
        };

        var r = await rig.GetAsync(owners: ["0xabc"]);

        Assert.Equal("ok", r.Loan!.LoanLabel);
    }

    [Fact]
    public async Task No_usable_loan_gives_null()
    {
        var rig = new Rig
        {
            Loans = new FakeLoans([Loan("noprice", "Ltv", 90m, RiskLevel.Critical, 5m, null), Loan("none", "None", null, RiskLevel.Ok, null, 1m)]),
        };

        Assert.Null((await rig.GetAsync(owners: ["0xabc"])).Loan);
    }

    [Fact]
    public async Task A_failing_borrow_read_leaves_the_calendar_working()
    {
        var rig = new Rig { Loans = new FakeLoans(throws: true) };
        rig.Econ.Rows[D("2026-11-18")] = [Ev("2026-11-18", "08:30", "Core CPI", consensus: "3.0%")];

        var r = await rig.GetAsync(owners: ["0xabc"]);

        Assert.Null(r.Loan);
        Assert.Single(r.Upcoming);
    }
}
