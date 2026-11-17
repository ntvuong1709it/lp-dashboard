# Market Watch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show upcoming macro and earnings events, their results tagged risk-on or risk-off, and a loan stress table, on a new Market watch page and a compact Overview card.

**Architecture:** Pure rules (impact, tags, time, loan stress) in the C# Domain layer with xUnit tests. Three source interfaces in Application with Nasdaq and Fed-feed adapters in Infrastructure that never throw for provider failures. A `MarketWatchService` merges them behind an in-memory cache and serves one endpoint, `GET /api/market/watch`. No database, no background service. The React frontend adds a page and an Overview card.

**Tech Stack:** .NET 10 (ASP.NET Core, xUnit), React 19 + TanStack Query + Tailwind 4, TypeScript.

**Spec:** [docs/market-watch-spec.md](../../market-watch-spec.md). UI mock: [docs/market-watch-mock.html](../../market-watch-mock.html). Read both first.

## Global Constraints

- Outbound requests carry a date or nothing, never a wallet address or other user data (spec: header).
- Never log or print API keys or the RPC URL. No new key is needed.
- **Do not commit.** The user commits and opens PRs. Where a task would commit, it stops and reports instead.
- **Stop after Task 2, Task 5, Task 7 and Task 8** (spec "Build order" steps 1 to 4): say what was verified and what was not, and wait for the user's go-ahead.
- Namespaces: `LpDashboard.Domain.Market`, `LpDashboard.Application.Market`, `LpDashboard.Infrastructure.Market`. Config section `Market` (both `MarketOptions` in Domain and `MarketSettings` in Application bind to it, as `Risk` does).
- Reuse, do not copy: `SourceOutcome<T>` (`LpDashboard.Application.Risk`), `RiskHttp.GetJsonAsync` and `RequestPacer` (`LpDashboard.Infrastructure.Risk`, internal, same assembly).
- Test commands: `cd backend; dotnet test LpDashboard.slnx --filter "FullyQualifiedName~Market"`; frontend: `cd frontend; npm run build`.
- v1 scope limits from the spec: US only; no alerts, no AI, no database, no 30-day window, no crypto events, no tag for jobs/GDP/retail/earnings, no tag on headlines.

## Review Focus

- A real Nasdaq row `Atlanta Fed GDPNow` contains "GDP" but is not a GDP release: it must be dropped, not shown as High. (Task 1)
- Nasdaq's `gmt` column is US Eastern: a release at 08:30 must land on 13:30Z in winter and 12:30Z in summer, and the days around both DST changes must not shift by an hour. (Task 1, Task 3)
- Blank, `&nbsp;` or unparseable actual / consensus values never throw and never produce a tag. (Task 1, Task 3)
- One source down (calendar, earnings or headlines) leaves the other blocks working and reports that source as `stale` or `unavailable`; all three down returns an empty page, not a 500. (Task 4)
- A wallet with no loan, a loan with no collateral price, or `kind: "None"` gives `loan: null`, and the page still works. (Task 2, Task 4)

## File Structure

Backend (`backend/src/...`):
- `LpDashboard.Domain/Market/MarketOptions.cs`: thresholds and name lists (config `Market`).
- `LpDashboard.Domain/Market/MarketTypes.cs`: enums and small records shared by the rules.
- `LpDashboard.Domain/Market/MarketText.cs`: HTML-entity decoding, blank detection, value parsing.
- `LpDashboard.Domain/Market/EventClassifier.cs`: which events appear, impact and family.
- `LpDashboard.Domain/Market/ResultTagger.cs`: actual against consensus to a tag with a reason.
- `LpDashboard.Domain/Market/EasternTime.cs`: Eastern local date and time to a UTC instant.
- `LpDashboard.Domain/Market/LoanStressCalculator.cs`: buffer and stress table.
- `LpDashboard.Application/Market/MarketAbstractions.cs`: source interfaces, raw records.
- `LpDashboard.Application/Market/MarketSettings.cs`: TTLs, concurrency, rate, feeds.
- `LpDashboard.Application/Market/MarketCache.cs`: fresh / stale cache.
- `LpDashboard.Application/Market/MarketDtos.cs`, `MarketWatchService.cs`.
- `LpDashboard.Infrastructure/Market/NasdaqEconomicCalendarSource.cs`, `NasdaqEarningsCalendarSource.cs`, `FeedHeadlineSource.cs`.
- `LpDashboard.Api/Controllers/MarketWatchController.cs`; edits to both `DependencyInjection.cs` files and `appsettings.json`.

Tests (`backend/tests/LpDashboard.Domain.Tests/`): `MarketTextTests.cs`, `EventClassifierTests.cs`, `ResultTaggerTests.cs`, `EasternTimeTests.cs`, `LoanStressTests.cs`, `MarketSourceParserTests.cs`, `MarketCacheTests.cs`, `MarketWatchServiceTests.cs`; fixtures in `Fixtures/market/`.

Frontend (`frontend/src/...`): `api/market.ts`, additions to `api/types.ts`, `lib/market.ts`, `components/market/*`, `pages/MarketPage.tsx`, edits to `main.tsx`, `components/header/Header.tsx`, `pages/OverviewPage.tsx`.

---

## Step 1 of the spec: Domain and tests

### Task 1: Event rules (text, classification, tags, time)

**Files:**
- Create: `Domain/Market/MarketOptions.cs`, `MarketTypes.cs`, `MarketText.cs`, `EventClassifier.cs`, `ResultTagger.cs`, `EasternTime.cs`
- Test: `MarketTextTests.cs`, `EventClassifierTests.cs`, `ResultTaggerTests.cs`, `EasternTimeTests.cs`

**Interfaces:**
- Produces:
  - `enum EventCategory { Macro, Earnings }`, `enum EventImpact { Medium, High }`, `enum EventFamily { Other, Inflation, RateDecision }`, `enum RiskTag { None, RiskOn, RiskOff, Neutral }`
  - `sealed record Classified(EventImpact Impact, EventFamily Family)`
  - `sealed record TagResult(RiskTag Tag, string? Reason)`
  - `MarketOptions` (section `"Market"`) with these properties and defaults from the spec: `string[] Countries = ["United States"]`; `HighImpactNames = ["Fed Interest Rate Decision","FOMC Minutes","Fed Chair","CPI","Core CPI","PCE","Core PCE","PPI","Producer Price Index","Nonfarm Payrolls","Unemployment Rate","GDP"]`; `MediumImpactNames = ["Retail Sales","Initial Jobless Claims","JOLTS","ISM","Consumer Confidence","Michigan"]`; `ExcludeNames = ["GDPNow","Member"]`; `RateDecisionNames = ["Fed Interest Rate Decision"]`; `InflationNames = ["CPI","PCE","PPI","Producer Price Index"]`; `decimal EarningsHighMarketCapUsd = 200_000_000_000m`; `decimal EarningsMediumMarketCapUsd = 50_000_000_000m`; `EarningsWatchlist = ["NVDA","AAPL","MSFT","AMZN","GOOGL","META","TSLA","COIN","MSTR"]`; `decimal InflationTolerancePct = 0.05m`; `decimal BufferThinBelowPercent = 40m`; `decimal BufferTightBelowPercent = 25m`; `decimal WatchWithinPoints = 8m`; `decimal HfWatchBelow = 1.25m`; `decimal HfTarget = 1.2m`; `int[] FallPercents = [10,20,30]`
  - `static class MarketText`: `string Clean(string? raw)` (HTML-decode, trim, `&nbsp;` to empty); `bool TryParseValue(string? raw, out decimal value)` (handles `%`, `$`, thousands commas, `K`/`M`/`B`/`T` suffixes; blank or `N/A` is false)
  - `static class EventClassifier`: `Classified? ClassifyMacro(string country, string eventName, MarketOptions o)` (null = drop); `EventImpact? ClassifyEarnings(string symbol, string? marketCapRaw, MarketOptions o)` (null = drop)
  - `static class ResultTagger`: `TagResult Tag(EventFamily family, string? actualRaw, string? consensusRaw, MarketOptions o)`
  - `static class EasternTime`: `DateTimeOffset ToUtc(DateOnly day, TimeOnly time)` (zone `America/New_York`, DST included)

- [ ] **Step 1: Write the failing tests** (names and assertions; values from the spec and the real 2026-11-17 samples)
  - `MarketTextTests`: `Clean("&nbsp;") == ""`, `Clean("a &amp; b") == "a & b"`; `TryParseValue("3.1%")` gives 3.1; `"$0.64"` gives 0.64; `"1.380M"` gives 1_380_000; `"19.80K"` gives 19_800; `"$1,016,233,530,000"` gives 1016233530000; `"&nbsp;"`, `" "`, `"N/A"`, `"abc"`, `null` are false.
  - `EventClassifierTests`: `("United States","Core CPI")` is High + Inflation; `"Fed Interest Rate Decision"` is High + RateDecision; `"Retail Sales"` and `"Core Retail Sales"` are Medium; `"Initial Jobless Claims"` Medium; `"Fed Chair Powell Speaks"` High; `"FOMC Member Williams Speaks"` null; **`"Atlanta Fed GDPNow"` null**; `"MBA Mortgage Applications"` null; `"Export Price Index"` null; `("United Kingdom","Core CPI")` null; matching is case-insensitive and on whole words (`"GDP"` matches `"GDP Growth Rate QoQ"`). Earnings: `("WMT","$1,016,233,530,000")` High; `("XYZ","$60,000,000,000")` Medium; `("XYZ","$5,000,000,000")` null; `("NVDA","N/A")` High; `("XYZ",null)` null.
  - `ResultTaggerTests`: Inflation `("3.1%","3.0%")` is RiskOff and the reason contains `3.1%`, `3.0%` and `hotter`; `("2.9%","3.0%")` RiskOn with `cooler`; `("3.04%","3.0%")` Neutral; `("3.1%","")` and `("&nbsp;","3.0%")` and `("abc","3.0%")` are `None` with null reason. RateDecision `("4.25%","4.00%")` RiskOff; `("4.00%","4.00%")` Neutral; `("3.75%","4.00%")` RiskOn. `EventFamily.Other` always `None`.
  - `EasternTimeTests`: `ToUtc(2026-11-19, 08:30)` is `2026-11-19T13:30Z`; `ToUtc(2026-10-30, 08:30)` is `12:30Z`; `ToUtc(2026-11-02, 08:30)` is `13:30Z`; `ToUtc(2026-03-06, 08:30)` is `13:30Z`; `ToUtc(2026-03-09, 08:30)` is `12:30Z`.

- [ ] **Step 2: Run the tests and confirm they fail to compile or fail** (`dotnet test ... --filter "FullyQualifiedName~Market"`): types not defined.
- [ ] **Step 3: Implement** the files above with the signatures in Interfaces. Name matching: case-insensitive whole-word regex per configured name; `ExcludeNames` checked first and also whole-word; medium names are checked only after high names miss. `"Fed Chair"` matches any name containing the phrase. The tagger's reason strings follow the spec's example ("Actual 3.1% vs forecast 3.0%: hotter than expected, fewer cuts priced in"; cooler: "...: cooler than expected, more cuts priced in"; rate decision: "Rate 4.25% vs forecast 4.00%: higher than expected" / "lower than expected"). Compare in percentage points; for `"3.04%"` vs `"3.0%"` the difference 0.04 is within `InflationTolerancePct`.
- [ ] **Step 4: Run the tests and confirm they pass**, and that the whole suite still passes (`dotnet test LpDashboard.slnx`, 556 tests at the last handover plus the new ones).

### Task 2: Loan stress calculator

**Files:**
- Create: `Domain/Market/LoanStressCalculator.cs`
- Test: `LoanStressTests.cs`

**Interfaces:**
- Consumes: `MarketOptions` (Task 1)
- Produces:
  - `enum BufferLevel { Comfortable, Thin, Tight }`, `enum StressStatus { Ok, Watch, MarginCall, Liquidation }`
  - `sealed record StressRow(int FallPercent, decimal Value, StressStatus Status, decimal AddCollateralUsd)` (`Value` is LTV percent or health factor)
  - `sealed record LoanStress(string Kind, decimal Value, decimal DropToLiquidationPercent, BufferLevel Buffer, decimal? MarginCallLtvPercent, decimal? LiquidationLtvPercent, decimal CollateralUsd, decimal DebtUsd, IReadOnlyList<StressRow> Rows)`
  - `static class LoanStressCalculator`: `LoanStress? Compute(string kind, decimal? value, decimal? marginCallLtvPercent, decimal? liquidationLtvPercent, decimal? collateralUsd, decimal debtUsd, MarketOptions o)`. `kind` is the Borrow `LoanRiskDto.Kind`: `"Ltv"` or `"HealthFactor"`.

- [ ] **Step 1: Write the failing tests** (collateral 20,000; margin call 70; liquidation 78)
  - LTV 52, debt 10,400: DropToLiquidation ≈ 33.33, Buffer Thin. Rows for 10, 20, 30: LTV ≈ 57.78 / 65.00 / 74.29; status Ok / Watch / MarginCall; AddCollateralUsd 0 / 0 / ≈ 857.14.
  - LTV 35, debt 7,000: Buffer Comfortable, all rows Ok, all add 0. LTV 66, debt 13,200: Buffer Tight; rows 73.33 MarginCall (add ≈ 857.14), 82.50 Liquidation (add ≈ 2,857.14), 94.29 Liquidation (add ≈ 4,857.14).
  - No margin-call level (null) with liquidation 78: the target for "add" and the Watch band is the liquidation level.
  - HealthFactor 1.5, collateral 20,000, debt 10,000: DropToLiquidation ≈ 33.33; rows 10/20/30: 1.35 / 1.20 / 1.05; Ok / Watch / Watch (1.20 is below the 1.25 watch level; the plan first said Ok for the middle row, corrected while building); add 0 / 0 / ≈ 2,000.00 (target HF 1.2). HF below 1.0 after a fall is Liquidation.
  - Returns null for: kind `"None"`, null value, null or non-positive collateral, `Ltv` with null liquidation level.
- [ ] **Step 2: Run and confirm failure.**
- [ ] **Step 3: Implement `Compute`.** LTV: `ltv / (1 - fall)`; status Liquidation at or above the liquidation level, MarginCall at or above margin call (when there is one), Watch within `WatchWithinPoints` below the target level, else Ok; add = `max(0, debt / (target/100) - collateral * (1 - fall))`, target = margin call level, or the liquidation level when there is none. HF: `hf * (1 - fall)`; Liquidation below 1.0, Watch below `HfWatchBelow`; add = `max(0, HfTarget * collateral / hf - collateral * (1 - fall))`. Buffer from DropToLiquidation: above `BufferThinBelowPercent` Comfortable, from `BufferTightBelowPercent` up to it Thin, below Tight. DropToLiquidation: LTV `(1 - ltv/liqLtv) * 100`; HF `(1 - 1/hf) * 100`.
- [ ] **Step 4: Run and confirm everything passes.**
- [ ] **Step 5: STOP.** Report to the user what was verified (tests passing, counts) and that no source or UI exists yet. Do not commit.

---

## Step 2 of the spec: Sources and endpoint

### Task 3: Source interfaces and adapters (parse recorded responses)

**Files:**
- Create: `Application/Market/MarketAbstractions.cs`, `MarketSettings.cs`; `Infrastructure/Market/NasdaqEconomicCalendarSource.cs`, `NasdaqEarningsCalendarSource.cs`, `FeedHeadlineSource.cs`; `Fixtures/market/nasdaq_events_20261118.json`, `nasdaq_earnings_20261119.json`, `fed_press_monetary.xml`, `nasdaq_events_synthetic_dupes.json`
- Test: `MarketSourceParserTests.cs`

**Interfaces:**
- Consumes: `SourceOutcome<T>` (`Ok`, `Fail`, `Answered`), `RiskHttp.GetJsonAsync(HttpClient, RequestPacer, string relativeUrl, CancellationToken)` returning a `JsonResponse(Document, Failure)`.
- Produces (Application):
  - `sealed record RawEconomicEvent(DateOnly Day, string TimeEastern, string Country, string Name, string? Actual, string? Consensus, string? Previous)`
  - `sealed record RawEarning(DateOnly Day, string Timing, string Symbol, string Company, string? MarketCap, string? EpsForecast)` where `Timing` is `"pre-market"`, `"after-hours"` or `"not-supplied"`
  - `sealed record RawHeadline(string Title, string Source, string Url, DateTimeOffset PublishedAt)`
  - `interface IEconomicCalendarSource { Task<SourceOutcome<IReadOnlyList<RawEconomicEvent>>> GetDayAsync(DateOnly day, CancellationToken ct); }`, `IEarningsCalendarSource { Task<SourceOutcome<IReadOnlyList<RawEarning>>> GetDayAsync(DateOnly day, CancellationToken ct); }`, `IHeadlineSource { Task<SourceOutcome<IReadOnlyList<RawHeadline>>> GetLatestAsync(CancellationToken ct); }`
  - `MarketSettings` (section `"Market"`): `int RequestsPerMinute = 240`, `int MaxConcurrentDays = 4`, `int SourceTimeoutSeconds = 15`, `int UpcomingTtlMinutes = 60`, `int TodayTtlMinutes = 10`, `int PastTtlHours = 24`, `int HeadlinesTtlMinutes = 15`, `int StaleMaxHours = 24`, `List<HeadlineFeed> HeadlineFeeds` (default one: `Federal Reserve`, `https://www.federalreserve.gov/feeds/press_monetary.xml`), `record HeadlineFeed(string Name, string Url)`.
- Produces (Infrastructure): `internal static` parse methods used by the tests: `NasdaqEconomicCalendarSource.Parse(JsonElement root, DateOnly day)`, `NasdaqEarningsCalendarSource.Parse(JsonElement root, DateOnly day)`, `FeedHeadlineSource.Parse(string xml, string sourceName)`, each returning `SourceOutcome<IReadOnlyList<...>>`.

- [ ] **Step 1: Save fixtures from the live responses** (keep them small; they contain no personal data). Fetch with `curl -A "Mozilla/5.0"` the economic events for 2026-11-18 and earnings for 2026-11-19 and the Fed monetary feed, then trim each to about 12 rows including `Retail Sales` (both rows), `Core Retail Sales`, `Fed Waller Speaks`, `Atlanta Fed GDPNow`, `MBA Mortgage Applications`, one `Walmart` earnings row; save as the fixture names above. Hand-make `nasdaq_events_synthetic_dupes.json` with a published-actual row, a blank-actual row, a row whose actual is `&nbsp;`, and duplicate names where only one has a consensus.
- [ ] **Step 2: Write the failing tests**
  - Events: `Parse(fixture 2026-11-18)` is `Answered`; `Name`, `Country`, `TimeEastern` ("08:30") are read; `Actual` for an unpublished row is null (not `"&nbsp;"`); the two `Retail Sales` rows are both returned (deduping is the service's job, Task 4); text entities are decoded.
  - A body without `data.rows` is `Fail("unreadable response")`; `rows: null` gives an empty answered list.
  - Earnings: `Parse` reads `Symbol` `WMT`, `Timing` `pre-market` for `time-pre-market`, `MarketCap` string unchanged, `EpsForecast` `$0.64`; `time-after-hours` gives `after-hours`; anything else `not-supplied`.
  - Headlines: `Parse(fed fixture, "Federal Reserve")` returns items with `Title`, `Url`, `PublishedAt` parsed from the RFC 822 date (`Thu, 12 Nov 2026 17:45:00 GMT` is `2026-11-12T17:45:00Z`), newest first; malformed XML is `Fail`.
- [ ] **Step 3: Run and confirm failure.**
- [ ] **Step 4: Implement** the records, interfaces, settings and the three adapters. The two Nasdaq adapters take `HttpClient` plus `IOptions<MarketSettings>`, build a `RequestPacer(settings.RequestsPerMinute)`, call `GetJsonAsync(_http, _pacer, "api/calendar/economicevents?date=yyyy-MM-dd", ct)` (earnings: `api/calendar/earnings?date=...`) and call `Parse`. `FeedHeadlineSource` fetches each configured feed with the same `HttpClient`, tolerates one feed failing (returns the others; `Fail` only if all fail), and merges by `PublishedAt` descending. Never include a URL in a failure reason. Keep `Parse` pure so tests need no network.
- [ ] **Step 5: Run and confirm everything passes.**

### Task 4: Cache and `MarketWatchService`

**Files:**
- Create: `Application/Market/MarketCache.cs`, `MarketDtos.cs`, `MarketWatchService.cs`
- Test: `MarketCacheTests.cs`, `MarketWatchServiceTests.cs`

**Interfaces:**
- Consumes: Tasks 1 to 3; `BorrowQueryService.GetLoansAsync(IReadOnlyCollection<string> owners, int days, TimeZoneInfo zone, CancellationToken)` returning `IReadOnlyList<LoanDto>` (`Risk.Kind`, `Risk.Value`, `Risk.Level` (`RiskLevel` Ok/Warn/Critical), `Risk.DropToLiquidationPercent`, `Risk.MarginCallLtvPercent`, `Risk.LiquidationLtvPercent`, `CollateralUsd`, `DebtUsd`, `Label`); `TimeProvider`.
- Produces:
  - `enum CacheState { Fresh, Stale, None }`; `sealed class MarketCache(TimeProvider time)` with `Task<(T? Value, CacheState State)> GetOrFetchAsync<T>(string key, TimeSpan ttl, TimeSpan staleMax, Func<CancellationToken, Task<T?>> fetch, CancellationToken ct) where T : class`. `fetch` returns null on failure. Fresh within `ttl`; on failure serves the old value as `Stale` up to `staleMax`, else `None`. Singleton, thread-safe, one in-flight fetch per key.
  - DTOs (camelCase JSON via the app default): `MarketWatchDto(DateTimeOffset UpdatedAt, MarketSourcesDto Sources, IReadOnlyList<MarketEventDto> Upcoming, IReadOnlyList<MarketEventDto> Happened, IReadOnlyList<HeadlineDto> Headlines, LoanStressDto? Loan)`; `MarketSourcesDto(string Calendar, string Earnings, string Headlines)` with values `"ok"`, `"stale"`, `"unavailable"`; `MarketEventDto(string Id, DateTimeOffset At, string? Timing, string Name, string Category, string Impact, string? Previous, string? Forecast, string? Actual, string? Tag, string? Reason)` (`Category` `"Macro"`/`"Earnings"`, `Impact` `"High"`/`"Medium"`, `Tag` `"RiskOn"`/`"RiskOff"`/`"Neutral"` or null, `Timing` `"Before open"`/`"After close"`/`"Time not set"` for earnings, null for macro); `HeadlineDto(string Title, string Source, string Url, DateTimeOffset PublishedAt)`; `LoanStressDto(string LoanLabel, LoanStress Stress)` (the Domain record, serialised as is, enums as strings).
  - `sealed class MarketWatchService`: `Task<MarketWatchDto> GetAsync(int days, IReadOnlyCollection<string>? owners, TimeZoneInfo zone, CancellationToken ct)`; `days` is 7 or 14 (anything else is treated as 14).

- [ ] **Step 1: Write the failing tests** (fake sources; a `TimeProvider` subclass for the clock; `now` = 2026-11-17T15:00Z)
  - `MarketCacheTests`: second call within `ttl` does not call `fetch`; after `ttl` it does; a failing `fetch` after a success returns the old value with `Stale`; beyond `staleMax` it returns `None`; two concurrent calls on one key call `fetch` once.
  - `MarketWatchServiceTests`:
    - Days fetched are today minus 3 to today plus `days`, weekends skipped, never more than `MaxConcurrentDays` at once.
    - Macro rows are dropped or kept per `EventClassifier`; a macro event with a non-blank actual goes to `Happened` with its tag; one without goes to `Upcoming`.
    - Duplicate name and time rows collapse to one, keeping the row that has a consensus.
    - `At` for `("2026-11-19","08:30")` is `2026-11-19T13:30:00Z`.
    - Earnings: pre-market, after-hours and not-supplied map to `Timing` labels; an earnings row is `Upcoming` until its assumed time (pre-market 08:00 Eastern, after-hours and not-supplied 16:30 Eastern) has passed, then `Happened` with no tag and `Actual` null.
    - Sorted: `Upcoming` soonest first, `Happened` newest first, headlines newest first.
    - **Dead sources:** calendar fails and no cache gives `Sources.Calendar == "unavailable"` while earnings and headlines still return; a source that fails after a success gives `"stale"` with the old data; all three failing returns empty lists, no exception.
    - Loan: owners null gives `Loan == null`; with loans, the loan with the highest `Risk.Level`, then the smallest `DropToLiquidationPercent`, is chosen; a loan with null `CollateralUsd` or `Kind == "None"` is skipped; none usable gives null.
- [ ] **Step 2: Run and confirm failure.**
- [ ] **Step 3: Implement** `MarketCache`, the DTOs and `MarketWatchService`. Cache keys: `econ:{day}`, `earn:{day}`, `headlines`. TTL by day: before today `PastTtlHours` once an actual exists in any row (otherwise `TodayTtlMinutes`), today `TodayTtlMinutes`, later `UpcomingTtlMinutes`. Per-source status from the states of that source's day fetches (any `None` with no data at all gives `unavailable`; any `Stale` gives `stale`). Use a `SemaphoreSlim(MaxConcurrentDays)` for the day fetches and a per-call timeout from `SourceTimeoutSeconds`. Event ids: stable string from category, day, time and name.
- [ ] **Step 4: Run and confirm everything passes.**

### Task 5: Endpoint, wiring, live check

**Files:**
- Create: `Api/Controllers/MarketWatchController.cs`
- Modify: `Infrastructure/DependencyInjection.cs` (new `AddMarket`), `Application/DependencyInjection.cs`, `Api/appsettings.json`

**Interfaces:**
- Consumes: `MarketWatchService.GetAsync` (Task 4), `OwnerParam.TryParse`.
- Produces: `GET /api/market/watch?days=7|14&owner=<addr,...>&timeZone=<iana>` returning `MarketWatchDto`. `owner` optional: absent gives `Loan == null`; present but invalid is 400. `timeZone` handled the way `BorrowController` does.

- [ ] **Step 1: Implement the controller** (`[Route("api/market")]`, `[HttpGet("watch")]`, `ProducesResponseType<MarketWatchDto>`), modelled on `BorrowController`'s owner and time-zone handling.
- [ ] **Step 2: Register**: `services.Configure<MarketOptions>` and `Configure<MarketSettings>` from section `Market`; `AddHttpClient<IEconomicCalendarSource, NasdaqEconomicCalendarSource>` and `<IEarningsCalendarSource, NasdaqEarningsCalendarSource>` with `BaseAddress https://api.nasdaq.com/`, timeout 15 s, a browser-like `User-Agent` and `Accept: application/json`; `AddHttpClient<IHeadlineSource, FeedHeadlineSource>`; in Application `AddSingleton<MarketCache>()` and `AddScoped<MarketWatchService>()`. Add a `Market` block to `appsettings.json` with the defaults that differ from code (empty block is fine if all defaults live in code; add only `HeadlineFeeds`).
- [ ] **Step 3: Build and run the full suite**: `dotnet build LpDashboard.slnx` and `dotnet test LpDashboard.slnx`: all pass.
- [ ] **Step 4: Live check** (start the API, call it with `curl`; do not paste any key or wallet into the report). Confirm: the response has the three `sources` as `ok`; a known release such as US Retail Sales on 2026-11-18 has `At` `...T13:30:00Z`; a high-impact event appears with a forecast; `&nbsp;` never appears in any field; the first load time. If the first load takes more than 10 seconds, report it with the number. Read the real Nasdaq `eventName` values for the FOMC decision, PCE, jobs and GDP releases on their release days (only `Core CPI` and `Retail Sales` were seen in the samples) and correct the default name lists in `MarketOptions` if a high-impact release is missing or a noise row appears; add the corrected name as a test case in `EventClassifierTests`. Try CoinDesk and Cointelegraph RSS from the app's network: add each to `Market:HeadlineFeeds` only if it answers; otherwise report that it did not.
- [ ] **Step 5: STOP.** Report what was verified (tests, live call results, timings) and what was not (UI).

---

## Step 3 of the spec: Market watch page

### Task 6: Frontend data layer

**Files:**
- Create: `frontend/src/api/market.ts`, `frontend/src/lib/market.ts`
- Modify: `frontend/src/api/types.ts`

**Interfaces:**
- Consumes: the DTOs of Task 4; `request` from `api/http.ts`; the existing `timeZone` constant used by `useBorrowSummary` in `api/client.ts`.
- Produces: TypeScript types `MarketWatchDto`, `MarketEventDto`, `HeadlineDto`, `MarketSourcesDto`, `LoanStressDto`, `StressRow` mirroring the C# records exactly (enums as string unions); `useMarketWatch(owner: string | null, days: 7 | 14)` (TanStack Query, key `["market", owner, days]`, `staleTime` 5 min, `refetchInterval` 10 min, not gated on `owner`); in `lib/market.ts`: `countdownLabel(targetMs: number, nowMs: number): string` (`"1d 23h"`, `"5h 12m"`, `"42m"`, `"now"`), `impactClass(impact)`, `tagLabel(tag)` (`"Risk-on"`, `"Risk-off"`, `"Neutral"`), `bufferLabel(buffer)`, `statusLabel(status)`, `groupByDay(events: MarketEventDto[]): { day: string; events: MarketEventDto[] }[]` using the browser's zone.

- [ ] **Step 1: Add the types and hook**, mirroring `useBorrowSummary`'s style.
- [ ] **Step 2: Implement the helpers** in `lib/market.ts`.
- [ ] **Step 3: Verify**: `cd frontend; npm run build` passes (type check).

### Task 7: Market watch page and nav

**Files:**
- Create: `frontend/src/pages/MarketPage.tsx`, `frontend/src/components/market/EventList.tsx`, `LoanStressTable.tsx`, `HeadlinesPanel.tsx`, `MarketBanner.tsx`
- Modify: `frontend/src/main.tsx` (route `market`), `frontend/src/components/header/Header.tsx` (a `NavLink to="/market"` labelled "Market" after Risk)

**Interfaces:**
- Consumes: Task 6; `Panel`, `Segmented`, `Skeleton`, `ErrorText`, `Empty` from `components/ui.tsx`; `useOwner`; `useNow` (1-minute interval for countdowns); `formatUsd` and `useHideBalances` so the USD amounts honour the hide-balances toggle.
- Produces: `MarketPage` (route `/market`); `LoanStressTable({ loan }: { loan: LoanStressDto })` and `MarketBanner({ data }: { data: MarketWatchDto })`, both reused by Task 8.

- [ ] **Step 1: Build the page to the mock** ([market-watch-mock.html](../../market-watch-mock.html), Market watch view): header with the impact filter (All impact / High only) and window (`7 days` / `14 days`, via `Segmented`), the banner (info / warn / crit by `loan.stress.buffer`, text as in the mock, no loan gives an info banner with just the next event), Upcoming and Already happened lists grouped by day with impact chip, category, previous / forecast / actual and the tag with the tooltip "From actual against forecast. Not a judgement of what happens next." (no AI note), the loan card with the stress table and the 1:1 worst-case note, latest headlines (title, source, age, link, no tag). Source status: a muted notice when a source is `stale` or `unavailable`. Earnings rows show the timing label instead of a clock time.
- [ ] **Step 2: Empty and error states**: skeleton while loading, `ErrorText` on error, "Nothing in this window." per list, and no loan card (with a one-line hint linking to Borrow) when `loan` is null.
- [ ] **Step 3: Verify in the browser**: start the dev server (`.claude/launch.json`, via `preview_start`; do not run the server through Bash), open `/market`, check no console errors, the filter and window controls change the lists, the hide-balances toggle hides the add-collateral amounts, and the layout at phone width (375) has no horizontal page scroll. Take a screenshot as proof. `npm run build` passes.
- [ ] **Step 4: STOP.** Report what was verified and what was not (for example, no real-wallet loan if none was available).

---

## Step 4 of the spec: Overview card

### Task 8: Overview card

**Files:**
- Create: `frontend/src/components/market/NextUpCard.tsx`
- Modify: `frontend/src/pages/OverviewPage.tsx` (insert the card directly under `AttentionStrip`), `docs/market-watch-spec.md` (status line), `HANDOVER.md` (a row in "What is built")

**Interfaces:**
- Consumes: `useMarketWatch` (Task 6), `LoanStressTable` (Task 7), `countdownLabel`, `useNow`.
- Produces: `NextUpCard()`: self-contained card, no props.

- [ ] **Step 1: Implement `NextUpCard`** to the mock's Overview view: left column the next High-impact upcoming event with a countdown (or, when a High-impact event happened in the last 12 hours, "Just happened" with its actual, forecast and tag, and the next event named below); middle column the loan buffer block; right column the stress table; footer line listing the next two events and a link to `/market`. No loan: the middle and right columns are replaced by one hint line with a link to Borrow. Sources `unavailable` with no data: a muted one-line notice instead of the card body. Use window 7.
- [ ] **Step 2: Insert it in `OverviewPage.tsx`** below the attention strip and above the stat cards.
- [ ] **Step 3: Update docs**: spec status becomes "built", noting any differences found while building; one row in `HANDOVER.md`'s table.
- [ ] **Step 4: Verify in the browser**: Overview shows the card, countdown ticks, link to `/market` works, no console errors, phone width has no horizontal scroll, a screenshot. `npm run build` passes and `dotnet test LpDashboard.slnx` still passes.
- [ ] **Step 5: STOP.** Report verified and not verified; do not commit.

---

## Self-review

- **Spec coverage:** events and impact rules (Task 1, 3, 4); tags (1); loan buffer and stress, LTV and HF (2); sources and quirks, including Eastern time, `&nbsp;`, duplicates, units (1, 3, 4); API, cache, stale and unavailable (4, 5); window 7 or 14 and weekends (4); UI page and card, hide-balances, phone width (7, 8); differences from the mock honoured (7, 8); build order and stop points (header, tasks 2, 5, 7, 8); no alerts, no AI, no database, no 30-day (Global Constraints). The spec's optional per-company earnings surprise call is intentionally not planned.
- **Type consistency:** `Classified`, `TagResult`, `LoanStress`, `StressRow`, `BufferLevel`, `StressStatus` defined in Tasks 1 and 2 and used by name in Task 4; DTO names in Task 4 match Task 6.
- **Proportion:** the plan is a set of interfaces, test names with values, and decisions; implementation bodies are left to the implementer.
