# Crypto Fear & Greed Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show the crypto Fear & Greed index on Market watch (gauge, comparisons, 30-day chart) and as a chip on the Overview line.

**Architecture:** A new `IFearGreedSource` (alternative.me, no key) is read through the existing `MarketCache` by `MarketWatchService`, which adds a `FearGreed` block and a source status to the existing `/api/market/watch` response. Zones come from one pure Domain function. The frontend reads the same `useMarketWatch` query for both the page row and the Overview chip.

**Tech Stack:** C# / .NET 10, xUnit, ASP.NET Core; React 19, TypeScript, Tailwind 4, recharts, TanStack Query.

**Spec:** [docs/superpowers/specs/2026-11-18-fear-greed-design.md](../specs/2026-11-18-fear-greed-design.md). UI mock (approved, option A plus chip): https://claude.ai/artifact/CiT8zRNiXKpWZNdFCghYr6. Pattern to follow: [docs/market-watch-spec.md](../../market-watch-spec.md).

## Global Constraints

- Crypto only. Source: `GET https://api.alternative.me/fng/?limit=31`, no key. Nothing but the day count is sent; nothing personal, nothing logged with the URL.
- Zones from the value, never from the source's label text: ExtremeFear 0 to 25, Fear 26 to 46, Neutral 47 to 54, Greed 55 to 75, ExtremeGreed 76 to 100.
- Cache `Market:FearGreedTtlMinutes` default 60; stale up to the existing `StaleMaxHours` (24), then "unavailable". No database change, no background service.
- Copy: "a mood reading, not a forecast"; never "buy" or "sell". Colours only from `src/tokens.css` tokens; both themes; works at 390px wide.
- Pure logic lives in the C# Domain layer with xUnit tests (the frontend has no test runner). Tests never call the network.
- Do not commit; the owner commits and pushes. After each task stop and say what was verified and what was not.
- Never run servers on ports 5080 or 5173 (the owner's backend uses them); see Task 6.

## Review Focus

- alternative.me answers 200 with HTML or `"metadata":{"error":...}`: the source is unavailable, the page still loads (Task 2).
- A row with a value outside 0 to 100, a non-number, or a bad timestamp: skipped, never plotted (Task 2).
- The source dies after working: stale with the old data for up to 24 h, then unavailable (Task 3).
- A short answer or a missing day: Yesterday, Last week and Last month are null, never a neighbouring day, and the UI hides those rows (Tasks 3, 4).
- A reading dated yesterday (UTC) while the viewer is already on the next day: the panel shows the reading's date, never the word "today" (Task 4).

---

### Task 1: Zone rules in the Domain

**Files:**
- Create: `backend/src/LpDashboard.Domain/Market/FearGreed.cs`
- Test: `backend/tests/LpDashboard.Domain.Tests/FearGreedZoneTests.cs`

**Interfaces:**
- Produces: `enum FearGreedZone { ExtremeFear, Fear, Neutral, Greed, ExtremeGreed }`; `record FearGreedBand(FearGreedZone Zone, int From, int To)` (both ends inclusive); `static class FearGreedZones { IReadOnlyList<FearGreedBand> Bands { get; }  FearGreedZone Of(int value) }`. `Of` throws `ArgumentOutOfRangeException` outside 0 to 100.

- [ ] **Step 1: Write the failing tests** in `FearGreedZoneTests`: a `[Theory]` `Value_maps_to_its_zone` with (0, 25, ExtremeFear), (26, 46, Fear), (47, 54, Neutral), (55, 75, Greed), (76, 100, ExtremeGreed), asserting both the first and last value of each band; `Values_outside_0_to_100_throw` for -1 and 101; `Bands_cover_0_to_100_without_gap_or_overlap` (first From 0, last To 100, each From equals the previous To plus 1, and `Of(b.From)` and `Of(b.To)` equal `b.Zone` for every band).
- [ ] **Step 2: Run** `dotnet test backend/tests/LpDashboard.Domain.Tests --filter FearGreedZoneTests`. Expected: build FAIL, types not defined.
- [ ] **Step 3: Implement** `FearGreed.cs` as above; `Of` finds the band that contains the value.
- [ ] **Step 4: Run the same command.** Expected: PASS.

### Task 2: The alternative.me source

**Files:**
- Modify: `backend/src/LpDashboard.Application/Market/MarketAbstractions.cs`
- Create: `backend/src/LpDashboard.Infrastructure/Market/AlternativeMeFearGreedSource.cs`
- Modify: `backend/src/LpDashboard.Infrastructure/DependencyInjection.cs` (`AddMarket`, and its summary comment)
- Create: `backend/tests/LpDashboard.Domain.Tests/Fixtures/market/alternative_fng_limit31.json`
- Test: `backend/tests/LpDashboard.Domain.Tests/FearGreedSourceTests.cs`

**Interfaces:**
- Produces (Application): `record RawFearGreedDay(DateOnly Day, int Value)`; `interface IFearGreedSource { Task<SourceOutcome<IReadOnlyList<RawFearGreedDay>>> GetDaysAsync(CancellationToken ct); }`. A successful answer is never empty and is sorted oldest first.
- Produces (Infrastructure): `internal sealed class AlternativeMeFearGreedSource(HttpClient http) : IFearGreedSource` and `internal static SourceOutcome<IReadOnlyList<RawFearGreedDay>> Parse(JsonElement root)`. Failure reasons: `"unreadable response"`, `"no days"`.
- Consumes: `SourceOutcome<T>` (`LpDashboard.Application.Risk`).

- [ ] **Step 1: Save the fixture.** Run `curl -s "https://api.alternative.me/fng/?limit=31" -o backend/tests/LpDashboard.Domain.Tests/Fixtures/market/alternative_fng_limit31.json`. It holds no personal data. (The csproj already copies `Fixtures\**\*.json`.)
- [ ] **Step 2: Write the failing tests** in `FearGreedSourceTests` (helpers `Json(string)` and `FixtureText` as in `MarketSourceParserTests`):
  - `Real_response_is_read`: fixture parses, `Answered`, 31 rows, days strictly ascending, every value in 0 to 100.
  - `Rows_are_read_and_sorted_oldest_first` with inline `{"data":[{"value":"11","value_classification":"Extreme Fear","timestamp":"1794960000"},{"value":"12","value_classification":"Extreme Fear","timestamp":"1794873600"}]}`: result `[(2026-11-17, 12), (2026-11-18, 11)]`.
  - `Bad_rows_are_skipped_and_good_ones_kept`: values `"101"`, `"-1"`, `"abc"`, a row without `timestamp`, a row with `"timestamp":"x"`, plus one good row: only the good row remains.
  - `Two_rows_for_one_day_keep_the_first`.
  - `Unusable_bodies_fail` (`[Theory]`): `{"data":[]}`, `{"data":"x"}`, `{"foo":1}`, `{"data":[{"value":"5","timestamp":"1794960000"}],"metadata":{"error":"rate limited"}}`, and a body where every row is bad. Each: not `Answered`.
  - HTTP behaviour with a small `StubHandler : HttpMessageHandler` (status or exception per test), client `BaseAddress = https://api.alternative.me/`: `A_500_answer_fails_without_throwing`, `A_network_error_fails_without_throwing` (handler throws `HttpRequestException`), `An_html_body_with_status_200_fails_without_throwing`, and `The_request_asks_for_31_days_and_sends_nothing_else` (captured request URI is exactly `https://api.alternative.me/fng/?limit=31`, no other query, no cookies or auth header).
- [ ] **Step 3: Run** `dotnet test backend/tests/LpDashboard.Domain.Tests --filter FearGreedSourceTests`. Expected: build FAIL.
- [ ] **Step 4: Implement** the interface and record, then the adapter. `Parse`: reads `data` array, `value` and `timestamp` as strings parsed with the invariant culture, day from the unix time in UTC, drops invalid rows and duplicate days (first wins), fails when `metadata.error` is non-null or nothing is left. `GetDaysAsync`: `GET fng/?limit=31`; any non-success status, `HttpRequestException`, `JsonException` or timeout (a `TaskCanceledException` while `ct` is not cancelled) becomes `SourceOutcome.Fail(...)`, never an exception, and no failure text contains the URL (same shape as `FeedHeadlineSource.FetchAsync`).
- [ ] **Step 5: Register it** in `AddMarket` with `AddHttpClient<IFearGreedSource, AlternativeMeFearGreedSource>`: base address `https://api.alternative.me/`, 15 s timeout, the same User-Agent as the headlines client.
- [ ] **Step 6: Run** the filter again, then `dotnet build backend/LpDashboard.slnx`. Expected: tests PASS, build succeeds.

### Task 3: Market watch response and service

**Files:**
- Modify: `backend/src/LpDashboard.Application/Market/MarketSettings.cs` (add `public int FearGreedTtlMinutes { get; set; } = 60;`)
- Modify: `backend/src/LpDashboard.Application/Market/MarketDtos.cs`
- Modify: `backend/src/LpDashboard.Application/Market/MarketWatchService.cs`
- Modify: `backend/tests/LpDashboard.Domain.Tests/MarketWatchServiceTests.cs`

**Interfaces:**
- Consumes: `IFearGreedSource`, `RawFearGreedDay` (Task 2); `FearGreedZones`, `FearGreedBand`, `FearGreedZone` (Task 1).
- Produces: `record FearGreedDayDto(DateOnly Day, int Value, FearGreedZone Zone)`; `record FearGreedDto(IReadOnlyList<FearGreedBand> Bands, IReadOnlyList<FearGreedDayDto> Days, FearGreedDayDto Today, FearGreedDayDto? Yesterday, FearGreedDayDto? LastWeek, FearGreedDayDto? LastMonth)`. `MarketSourcesDto` gains a fourth member `FearGreed` (same "ok", "stale", "unavailable"). `MarketWatchDto` gains a last member `FearGreedDto? FearGreed`. The `MarketWatchService` constructor takes `IFearGreedSource fearGreed` right after `headlines`. `Days` is oldest first; `Today` is the newest day; `Yesterday`, `LastWeek`, `LastMonth` are the days exactly 1, 7 and 30 days before `Today.Day`, or null when that day is not in `Days`. `FearGreed` is null exactly when the status is "unavailable". The JSON enum converter already writes zones as strings.

- [ ] **Step 1: Update the test rig, then write the failing tests.** Add `FakeFearGreed : IFearGreedSource` (public `List<RawFearGreedDay> Days`, `bool Fail`, `int Calls`; returns `Fail` or `Ok(Days)`), a `Rig.FearGreed` property, and pass it to the service in `Rig.GetAsync`. Change `All_sources_down_gives_an_empty_page_not_an_error` to also fail it and expect `new MarketSourcesDto("unavailable", "unavailable", "unavailable", "unavailable")` and `Assert.Null(r.FearGreed)`. The rig's clock is 2026-11-17 15:00 UTC. New tests:
  - `Fear_greed_days_come_oldest_first_with_zones_bands_and_comparisons`: source gives (Nov 16, 12), (Nov 17, 11), (Nov 10, 29), (Oct 18, 59) in that order. Expect days Oct 18, Nov 10, Nov 16, Nov 17; zones Greed, Fear, ExtremeFear, ExtremeFear; `Today.Value` 11, `Yesterday.Value` 12, `LastWeek.Value` 29, `LastMonth.Value` 59; `Bands` equals `FearGreedZones.Bands`; `Sources.FearGreed` is "ok".
  - `A_missing_comparison_day_is_null_not_a_neighbour`: source gives (Nov 17, 11), (Nov 15, 20): `Yesterday`, `LastWeek`, `LastMonth` all null, `Days.Count` 2.
  - `Two_rows_for_one_day_collapse_to_the_first`.
  - `Fear_greed_does_not_need_an_owner`: `GetAsync(owners: null)` returns it, with `Loan` null.
  - `Fear_greed_is_fetched_once_an_hour`: two calls in a row give `Calls == 1`; after `Clock.Advance(61 minutes)` the next call gives 2.
  - `A_dead_fear_greed_source_is_unavailable_and_the_rest_still_works`: `FearGreed` null, status "unavailable", `Sources.Headlines` "ok".
  - `Fear_greed_that_fails_after_working_is_stale_then_unavailable_after_the_stale_limit`: ok first; fail and advance 2 h: status "stale", `Today` still present; advance a further 25 h: "unavailable" and null.
- [ ] **Step 2: Run** `dotnet test backend/tests/LpDashboard.Domain.Tests --filter MarketWatchServiceTests`. Expected: build FAIL.
- [ ] **Step 3: Implement** the settings, DTOs and service change. Fetch with `cache.GetOrFetchAsync` under the key `"fng"` (TTL `FearGreedTtlMinutes`, stale max `StaleMaxHours`) and the existing `AnswerAsync`, started in parallel with the headline task. Build the block with a private static method that sorts by day, drops duplicate days and maps zones through `FearGreedZones.Of`; status through the existing `StatusOf<T>(T?, CacheState)`.
- [ ] **Step 4: Run** the filter, then the whole suite `dotnet test backend/LpDashboard.slnx`. Expected: all PASS.

### Task 4: The Market watch row

**Files:**
- Modify: `frontend/src/api/types.ts` (Market watch section), `frontend/src/tokens.css`, `frontend/src/pages/MarketPage.tsx`
- Create: `frontend/src/lib/fearGreed.ts`, `frontend/src/components/market/FearGreedRow.tsx`, `FearGreedGauge.tsx`, `FearGreedChart.tsx`

**Interfaces:**
- Produces types (camelCase JSON, `day` is a `"2026-11-18"` string): `FearGreedZone = "ExtremeFear" | "Fear" | "Neutral" | "Greed" | "ExtremeGreed"`; `FearGreedBandDto { zone; from; to }`; `FearGreedDayDto { day; value; zone }`; `FearGreedDto { bands; days; today; yesterday: FearGreedDayDto | null; lastWeek: ...; lastMonth: ... }`. `MarketSourcesDto` gains `fearGreed: MarketSourceStatus`; `MarketWatchDto` gains `fearGreed: FearGreedDto | null`.
- Produces `lib/fearGreed.ts`: `ZONE_LABEL` ("Extreme fear", "Fear", "Neutral", "Greed", "Extreme greed"); `ZONE_CHIP` (extreme fear `bg-down-fill/15 text-down`, fear `bg-warn-fill/15 text-warn`, neutral `bg-raised text-fg-2`, both greed zones `bg-up-fill/15 text-up`); `ZONE_VAR` (`"var(--zone-1)"` to `"var(--zone-5)"`); `fearGreedDay(day: string): string` ("Nov 18", read in UTC so the date does not shift); `fearGreedRange(days): { low: FearGreedDayDto; high: FearGreedDayDto }`.
- Produces components: `FearGreedRow({ data }: { data: MarketWatchDto })`, `FearGreedGauge({ value, zone, bands })`, `FearGreedChart({ days, bands })`.

- [ ] **Step 1: Types.** Add the types above to `types.ts`.
- [ ] **Step 2: Tokens.** In `tokens.css` `:root` add `--zone-1: var(--down-fill); --zone-2: var(--warn-fill); --zone-3: var(--control); --zone-4: color-mix(in srgb, var(--up-fill) 55%, var(--panel)); --zone-5: var(--up-fill);` with a one-line comment (the five fear and greed zones). One definition serves both themes because the tokens it reads change per theme. `scripts/check-contrast.mjs` reads hex tokens only and skips these.
- [ ] **Step 3: `lib/fearGreed.ts`** as specified.
- [ ] **Step 4: `FearGreedGauge`**: a semicircle SVG, `viewBox="0 0 260 142"`, role `img` with an aria-label giving the value and zone. One arc per band between `band.from` and the next band's `from` (the last to 100), with a 0.9 degree gap, stroke `ZONE_VAR[zone]`, width 13, opacity 1 for the band holding `value` and 0.28 for the others. A circle in `var(--marker)` with a `var(--panel)` ring at the value. The value in the centre in the `num` font at 54px, and "0" and "100" under the ends. Angle for a value: `180 - value * 1.8` degrees from the left.
- [ ] **Step 5: `FearGreedChart`**: recharts `AreaChart` in a `ResponsiveContainer` (height 210), y axis fixed 0 to 100 with ticks 0, 25, 50, 75, 100, x axis with the first, middle and last day via `fearGreedDay`. One `ReferenceArea` per band (`fill={ZONE_VAR[zone]}`, `fillOpacity={0.1}`), the line and a faint area in `chart.a` from `lib/chartTheme.ts`, a dot on the last point filled with its zone colour, and a `Tooltip` that shows the date, the value and the zone chip. Below it, "Low {n} on {date}" and "High {n} on {date}".
- [ ] **Step 6: `FearGreedRow`**: when `data.fearGreed` is null, one `Panel` titled "Crypto fear & greed" saying "Fear and greed is unavailable right now." Otherwise a grid with the same classes as the top row of `MarketPage` (`grid gap-4 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)]`): left `Panel` "Crypto fear & greed" (action text "Updated daily", and "Out of date" in `text-warn` when `data.sources.fearGreed === "stale"`) holding the gauge, a `Chip` with the zone label, a line "Reading of {fearGreedDay(today.day)}" (never the word "today"), rows for Yesterday, Last week and Last month showing the zone chip and the value (a row is left out when its day is null), and the note "How worried or excited the crypto market is, from 0 to 100. A mood reading, not a forecast."; right `Panel` "Last 30 days" (action "Source: alternative.me") holding the chart. Use the `Panel` from `components/ui.tsx` and `Chip` from `components/borrow/bits.tsx`.
- [ ] **Step 7: Wire into `MarketPage`**: render `<FearGreedRow data={data} />` between the `NextUp`/`FallPanel` grid and `CalendarStrip`, and add `["fear and greed index", data.sources.fearGreed]` to the `notices` list.
- [ ] **Step 8: Run** from `frontend/`: `npm run typecheck`, then `node scripts/check-contrast.mjs`. Expected: no errors; the contrast script passes as before.

### Task 5: The Overview chip

**Files:**
- Create: `frontend/src/components/market/FearGreedChip.tsx`
- Modify: `frontend/src/components/market/MarketLine.tsx`

**Interfaces:**
- Consumes: `FearGreedDto`, `ZONE_CHIP`, `ZONE_LABEL` (Task 4).
- Produces: `FearGreedChip({ fearGreed }: { fearGreed: FearGreedDto })`, text `Crypto mood {today.value}, {zone label in lower case}` in a `Chip`, coloured by `ZONE_CHIP`, with a title "Crypto fear and greed index. A mood reading, not a forecast."

- [ ] **Step 1: Implement** the chip and render it in `MarketLine` after the "stop earning" chip, only when `data.fearGreed` is not null. It must not change the line's `risky` (amber) rule: a mood reading never turns the line warning.
- [ ] **Step 2: Run** `npm run typecheck` and `npm run build` from `frontend/`. Expected: both succeed.

### Task 6: Check it in the real app and update the docs

**Files:**
- Modify: `docs/market-watch-spec.md` (status line, plus a short section "Crypto fear & greed" pointing to the design), `docs/superpowers/specs/2026-11-18-fear-greed-design.md` (status: built)

- [ ] **Step 1: Live source check.** `curl -s "https://api.alternative.me/fng/?limit=31"` returns 31 rows whose latest timestamp is today (UTC). If not, say so in the report.
- [ ] **Step 2: Start a second instance on other ports with a throwaway database.** Build to a scratch folder (`dotnet build backend/src/LpDashboard.Api -o <scratch>/api-out`), then run `dotnet <scratch>/api-out/LpDashboard.Api.dll --urls http://localhost:5190` with environment `ConnectionStrings__Default="Data Source=<scratch>/test.db"`, `Cors__Origins__0=http://localhost:5191` and `Sync__RunOnStartup=false`. From `frontend/`: `VITE_API_PROXY=http://localhost:5190 npx vite --port 5191`. Create a test account on the app's own setup page with a generated password; do not print it.
- [ ] **Step 3: Check `/market`.** The new row sits under the loan test and above the calendar; the gauge number and zone match the live reading; hovering the chart shows date, value and zone; the comparison rows match the source's yesterday, 7 and 30 days back; the Overview line shows the chip. Check dark and light and a 390px-wide window. Check that the console and `read_network_requests` show no errors, and that `/api/market/watch` carries the `fearGreed` block.
- [ ] **Step 4: Check the failure path in the page.** The source failing is covered by the Task 2 and 3 tests; here check how the page looks. With `javascript_tool`, wrap `window.fetch` so the `/api/market/watch` answer has `fearGreed: null` and `sources.fearGreed: "unavailable"`, then press Refresh. Expected: the panel says "Fear and greed is unavailable right now", the chip is gone, the notice line names the index, the rest of the page is unchanged. Repeat with `sources.fearGreed: "stale"` and the data kept: "Out of date" shows on the panel.
- [ ] **Step 5: Stop both instances and delete the scratch database.** Update the two docs. Report what was verified and what was not.
