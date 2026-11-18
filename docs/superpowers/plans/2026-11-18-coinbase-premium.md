# US Buying vs Selling (Coinbase Premium) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show who is paying more for Bitcoin, US buyers or the rest of the world, on Market watch (gauge, comparisons, 30-day bars) and as a chip on the Overview line.

**Architecture:** A new `IBitcoinPremiumSource` reads hourly candles from Coinbase (BTC-USD, USDT-USD) and Binance (BTC-USDT), joins them by hour and returns the hourly gap in dollars. `MarketWatchService` reads it through the existing `MarketCache` and adds a `Premium` block and a source status to `/api/market/watch`. All rules (zones, averages, coverage) are pure Domain code. The frontend reads the same `useMarketWatch` query for the page row and the Overview chip.

**Tech Stack:** C# / .NET 10, xUnit, ASP.NET Core; React 19, TypeScript, Tailwind 4, recharts, TanStack Query.

**Spec:** [docs/superpowers/specs/2026-11-18-coinbase-premium-design.md](../specs/2026-11-18-coinbase-premium-design.md). UI mock (option B, the gauge, chosen by the owner): https://claude.ai/artifact/Pcu8DUw649tkrP5mvRi4NF. Pattern to follow: [2026-11-18-fear-greed.md](2026-11-18-fear-greed.md) (copy its file shapes and test helpers).

## Global Constraints

- Gap per hour: `(coinbaseBtcUsd - binanceBtcUsdt x coinbaseUsdtUsd) / (binanceBtcUsdt x coinbaseUsdtUsd) x 100`, from the hourly close prices, joined on candle open time. An hour missing from any of the three feeds is dropped.
- Hosts (config, `Market:CoinbaseBaseUrl` default `https://api.exchange.coinbase.com/`, `Market:BinanceBaseUrl` default `https://data-api.binance.vision/`; `api.binance.com` answers 451 from US addresses). Requests: Coinbase `products/BTC-USD/candles` and `products/USDT-USD/candles` with `granularity=3600`, at most 300 candles each so three calls each; Binance `api/v3/klines?symbol=BTCUSDT&interval=1h&limit=720`. No key. Only product names and a time range are sent; nothing personal; no failure text contains a URL.
- Zones in percent: Flat from -0.03 to +0.03 inclusive; "a bit" up to but not including +/-0.10; "hard" at or beyond +/-0.10. Gauge scale +/-0.30. The value is rounded to 2 decimals (away from zero) before the zone is chosen, so the word always matches the number shown.
- Today = average of the hourly gaps with open time in (now - 24 h, now]; Yesterday = the last full UTC day; 7-day = last 168 hours; 30-day = last 720 hours; chart = one average per UTC day, today partial. Coverage: Today needs at least 12 hours, Yesterday 12, 7-day 84, 30-day 360; below that the comparison is null and below 12 for Today the whole block is unavailable.
- Cache `Market:PremiumTtlMinutes` default 15; stale up to the existing `StaleMaxHours` (24), then "unavailable"; status "stale" also when the newest hour is more than 3 hours old. No database change, no background service.
- Copy: says who is paying more; never "buy" or "sell" to the owner; the note says it is a clue, not a count of trades and not a forecast. Colours only from `src/tokens.css` tokens (reuse `--zone-1` to `--zone-5`); both themes; works at 390px wide.
- Pure logic lives in the C# Domain layer with xUnit tests (the frontend has no test runner). Tests never call the network.
- Do not commit; the owner commits and pushes. After each task stop and say what was verified and what was not.
- Never run servers on ports 5080 or 5173 (the owner's backend uses them); see Task 7.

## Review Focus

- Binance blocks the region (HTTP 451) or either exchange answers 200 with HTML or an error body: the source is unavailable and the page still loads (Task 3).
- One of the three feeds has gaps (an exchange pause): only common hours are used; under 12 usable hours in the last 24 means unavailable, never a number built on a few hours (Tasks 2, 4).
- A glitch candle (price 0 or negative, USDT-USD outside 0.90 to 1.10, or a gap beyond +/-5%): that hour is dropped, never plotted (Task 3).
- The feeds answer but stop updating (newest hour more than 3 hours old): status "stale", not "ok" (Task 4).
- Just after UTC midnight: Today is still the rolling 24 hours, Yesterday is the full previous day, and the first bar of the chart is a short day, not a spike (Task 2).

---

### Task 1: Zones and the hourly gap in the Domain

**Files:**
- Create: `backend/src/LpDashboard.Domain/Market/BitcoinPremium.cs`
- Test: `backend/tests/LpDashboard.Domain.Tests/PremiumZoneTests.cs`

**Interfaces:**
- Produces: `enum PremiumZone { SellingHard, SellingABit, Flat, BuyingABit, BuyingHard }`; `record PremiumBand(PremiumZone Zone, decimal From, decimal To)`; `static class PremiumZones { const decimal GaugeSpan = 0.30m; IReadOnlyList<PremiumBand> Bands { get; }  PremiumZone Of(decimal percent) }` (`Of` expects an already rounded value and never throws; bands run -0.30 to -0.10, -0.10 to -0.03, -0.03 to 0.03, 0.03 to 0.10, 0.10 to 0.30); `static class PremiumMath { decimal HourlyPercent(decimal coinbaseBtcUsd, decimal binanceBtcUsdt, decimal coinbaseUsdtUsd)  decimal Round(decimal percent) }`. `HourlyPercent` throws `ArgumentOutOfRangeException` when any price is not above 0. `Round` is 2 decimals, away from zero.

- [ ] **Step 1: Write the failing tests** in `PremiumZoneTests`: a `[Theory]` `Percent_maps_to_its_zone` with (-0.30, SellingHard), (-0.10, SellingHard), (-0.0999, SellingABit), (-0.04, SellingABit), (-0.03, Flat), (0, Flat), (0.03, Flat), (0.04, BuyingABit), (0.0999, BuyingABit), (0.10, BuyingHard), (0.55, BuyingHard), (-0.55, SellingHard); `Bands_are_contiguous_from_minus_to_plus_the_gauge_span` (first `From` is -0.30, last `To` is +0.30, each `From` equals the previous `To`); `Hourly_gap_is_the_dollar_corrected_difference` with (101, 100, 1.0) = 1.0, (99, 100, 1.0) = -1.0, (100, 100, 1.0) = 0, and the real 2026-11-18 prices (63472.77, 63543.97, 0.99888) within 0.001 of 0 (the raw gap against USDT would be -0.11); `Non_positive_prices_throw` for each of the three arguments being 0 and -1; `Round_goes_to_two_decimals_away_from_zero`: 0.035 gives 0.04, -0.035 gives -0.04, 0.0349 gives 0.03.
- [ ] **Step 2: Run** `dotnet test backend/tests/LpDashboard.Domain.Tests --filter PremiumZoneTests`. Expected: build FAIL, types not defined.
- [ ] **Step 3: Implement** `BitcoinPremium.cs` as above (model it on `FearGreed.cs`). Keep the three edge numbers (0.03, 0.10, 0.30) as named constants in one place.
- [ ] **Step 4: Run the same command.** Expected: PASS.

### Task 2: Summarise the hours (averages, coverage, days)

**Files:**
- Modify: `backend/src/LpDashboard.Domain/Market/BitcoinPremium.cs`
- Test: `backend/tests/LpDashboard.Domain.Tests/PremiumSeriesTests.cs`

**Interfaces:**
- Consumes: `PremiumZone`, `PremiumBand`, `PremiumZones`, `PremiumMath.Round` (Task 1).
- Produces: `record PremiumHour(DateTimeOffset At, decimal Percent)` (`At` is the candle open time, UTC); `record PremiumReading(decimal Percent, PremiumZone Zone)`; `record PremiumDay(DateOnly Day, decimal Percent, PremiumZone Zone)`; `record PremiumSummary(IReadOnlyList<PremiumBand> Bands, IReadOnlyList<PremiumDay> Days, PremiumReading Today, PremiumReading? Yesterday, PremiumReading? Week, PremiumReading? Month, DateTimeOffset AsOf)`; `static class PremiumSeries { PremiumSummary? Summarise(IEnumerable<PremiumHour> hours, DateTimeOffset now) }`. All percents are rounded with `PremiumMath.Round` and the zone comes from the rounded value. `Days` is oldest first, covers the 30 UTC days ending with the UTC day of `now`, skips days with no hours. `AsOf` is the newest hour's `At`. Returns null when the last 24 hours hold fewer than 12 hours. Duplicate `At` values: the first wins. Hours later than `now` are ignored.

- [ ] **Step 1: Write the failing tests** in `PremiumSeriesTests` with a helper `Hours(DateTimeOffset firstOpen, int count, decimal percent)` and `now` = 2026-11-18 15:00 UTC (an hour open at 15:00 is the current, partial hour):
  - `Today_is_the_rolling_24_hours`: hours from 2026-11-16 15:00 to now at 0.20 before 2026-11-17 16:00 and at 0.04 from then on: `Today.Percent` 0.04, zone BuyingABit; `AsOf` is 15:00 today.
  - `Fewer_than_12_hours_in_the_last_24_is_null`: 11 hours in the window (older hours plentiful) gives null; 12 gives a value.
  - `Zone_follows_the_rounded_value`: a day averaging 0.0349 gives Percent 0.03 and Flat; 0.035 gives 0.04 and BuyingABit; -0.10 exactly gives SellingHard.
  - `Days_are_utc_day_averages_oldest_first_and_today_is_partial`: 40 days of hours: `Days.Count` 30, first day 2026-10-20, last day 2026-11-18 averaging only its 16 hours (00:00 to 15:00); a day with no hours is absent (remove 2026-11-05 and check the count is 29).
  - `Yesterday_is_the_previous_full_utc_day_or_null`: previous UTC day with 24 hours of 0.10 gives 0.10 (BuyingHard); with only 11 hours gives null; at now = 2026-11-18 00:30 UTC "yesterday" is Nov 17, and Today still averages the last 24 rolling hours.
  - `Week_and_month_need_enough_coverage`: 100 of the last 168 hours gives a Week value, 83 gives null; 400 of 720 gives Month, 359 gives null.
  - `Duplicates_and_future_hours_are_ignored`: two rows for one `At` (first wins); an hour dated after `now` is not counted.
  - `Bands_are_the_domain_bands`: `Bands` equals `PremiumZones.Bands`.
- [ ] **Step 2: Run** `dotnet test backend/tests/LpDashboard.Domain.Tests --filter PremiumSeriesTests`. Expected: build FAIL.
- [ ] **Step 3: Implement** the records and `PremiumSeries.Summarise` with LINQ: dedupe and filter first, then the windows above (`At > now-24h && At <= now`, 168 and 720 hours the same way, UTC day groups for `Days` and Yesterday).
- [ ] **Step 4: Run** the same command. Expected: PASS.

### Task 3: The Coinbase and Binance source

**Files:**
- Modify: `backend/src/LpDashboard.Application/Market/MarketAbstractions.cs`, `backend/src/LpDashboard.Application/Market/MarketSettings.cs` (add `PremiumTtlMinutes = 15`, `CoinbaseBaseUrl`, `BinanceBaseUrl` with the defaults in Global Constraints)
- Create: `backend/src/LpDashboard.Infrastructure/Market/CoinbaseBinancePremiumSource.cs`
- Modify: `backend/src/LpDashboard.Infrastructure/DependencyInjection.cs` (`AddMarket` and its summary comment)
- Create: `backend/tests/LpDashboard.Domain.Tests/Fixtures/market/coinbase_btcusd_candles_3600.json`, `coinbase_usdtusd_candles_3600.json`, `binance_btcusdt_klines_1h.json`
- Test: `backend/tests/LpDashboard.Domain.Tests/PremiumSourceTests.cs`

**Interfaces:**
- Produces (Application): `interface IBitcoinPremiumSource { Task<SourceOutcome<IReadOnlyList<PremiumHour>>> GetHoursAsync(DateTimeOffset until, CancellationToken ct); }`. A successful answer is never empty and is sorted oldest first; `until` is the end of the range (the service passes now).
- Produces (Infrastructure): `internal sealed class CoinbaseBinancePremiumSource(IHttpClientFactory clients) : IBitcoinPremiumSource` using named clients `"coinbase-exchange"` and `"binance-data"`; `internal static IReadOnlyDictionary<long, decimal> ParseCoinbase(JsonElement root)` and `ParseBinance(JsonElement root)` (open time in unix seconds to close price; rows that are not arrays, short, non-numeric or not above 0 are skipped; the whole body unreadable gives an empty dictionary); `internal static IReadOnlyList<PremiumHour> Join(IReadOnlyDictionary<long, decimal> btcUsd, IReadOnlyDictionary<long, decimal> btcUsdt, IReadOnlyDictionary<long, decimal> usdtUsd)`.
- Consumes: `PremiumHour`, `PremiumMath.HourlyPercent` (Tasks 1, 2); `SourceOutcome<T>` (`LpDashboard.Application.Risk`).

- [ ] **Step 1: Save the fixtures** with `curl -s "https://api.exchange.coinbase.com/products/BTC-USD/candles?granularity=3600"`, the same for `USDT-USD`, and `curl -s "https://data-api.binance.vision/api/v3/klines?symbol=BTCUSDT&interval=1h&limit=720"`, each to the file names above. No personal data. (The csproj already copies `Fixtures\**\*.json`.) A Coinbase row is `[openTimeSeconds, low, high, open, close, volume]` as JSON numbers; a Binance row is `[openTimeMs, "open", "high", "low", "close", ...]` with strings. Close is index 4 in both.
- [ ] **Step 2: Write the failing tests** in `PremiumSourceTests` (reuse `Json(string)` and `FixtureText` helpers; a `StubHandler : HttpMessageHandler` answering by request path):
  - `Real_fixtures_parse_and_join`: both Coinbase fixtures and the Binance fixture parse to non-empty dictionaries and `Join` gives at least 100 hours, ascending, every `Percent` within +/-1.
  - `Coinbase_rows_are_read_by_open_time_with_the_close`: `[[1794960000,1,2,3,4.5,9]]` gives `{1794960000: 4.5}`; `Binance_rows_are_read_with_ms_converted_to_seconds`: `[[1794960000000,"1","2","3","4.5","9"]]` gives `{1794960000: 4.5}`.
  - `Bad_rows_are_skipped`: rows `"x"`, `[1]`, a price `0`, a price `-3`, a string where Coinbase wants a number, and one good row: only the good row remains.
  - `Join_keeps_only_common_hours_and_computes_the_gap`: inputs with hours 1, 2, 3 in BTC-USD, 2, 3, 4 in BTC-USDT, 1 to 4 in USDT-USD, so hours 2 and 3 come out; with BTC-USD 101, BTC-USDT 100, USDT-USD 1 the gap is 1.0.
  - `Glitch_hours_are_dropped`: USDT-USD 0.89 and 1.11 (outside 0.90 to 1.10) drop the hour; a gap of 5.1 and of -5.1 drops the hour, 4.9 stays.
  - `The_requests_are_exactly_these`: with `until` = 2026-11-18T14:20:00Z the captured URIs are Coinbase `products/BTC-USD/candles?granularity=3600&start=..&end=..` and the same for `USDT-USD`, three each, with H = 2026-11-18T14:00:00Z (`until` floored to the hour) the three ranges are `end = H - 300h x i` and `start = end - 299h` for i = 0, 1, 2 (consecutive, 300 candles each, no overlap), written as `yyyy-MM-ddTHH:mm:ssZ`; and one Binance `api/v3/klines?symbol=BTCUSDT&interval=1h&limit=720`; no other query, no auth header, no cookie.
  - `Any_failed_request_fails_the_source` (`[Theory]`: the BTC-USD call, the USDT-USD call, the Binance call): a 451 or 500 status, an `HttpRequestException`, an HTML body with status 200, and a timeout (`TaskCanceledException` while `ct` is not cancelled) each give a non-`Answered` outcome without throwing, and `Reason` contains neither "http" nor the host name.
  - `An_empty_join_fails`: all three feeds answer but share no hour: not `Answered`.
- [ ] **Step 3: Run** `dotnet test backend/tests/LpDashboard.Domain.Tests --filter PremiumSourceTests`. Expected: build FAIL.
- [ ] **Step 4: Implement** the interface, settings and adapter. `GetHoursAsync` floors `until` to the hour, makes the seven requests in parallel, fails the whole source if any one fails, and returns `Join(...)` sorted oldest first (fail with reason `"no hours"` when empty, `"unreadable response"` for bad bodies, `"unreachable"` and `"timed out"` as in `AlternativeMeFearGreedSource`). Dedupe the Coinbase chunks by open time.
- [ ] **Step 5: Register** in `AddMarket`: `AddHttpClient("coinbase-exchange", ...)` and `AddHttpClient("binance-data", ...)` with the base addresses read from `MarketSettings` (bind the `Market` section there, as the code already does for the options), 15 s timeout and the same User-Agent as the other Market clients, then `AddSingleton<IBitcoinPremiumSource, CoinbaseBinancePremiumSource>()` (or the same lifetime the other Market sources use). Update the summary comment to mention Coinbase and Binance.
- [ ] **Step 6: Run** the filter again, then `dotnet build backend/LpDashboard.slnx`. Expected: tests PASS, build succeeds.

### Task 4: Market watch response and service

**Files:**
- Modify: `backend/src/LpDashboard.Application/Market/MarketDtos.cs`, `backend/src/LpDashboard.Application/Market/MarketWatchService.cs`
- Modify: `backend/tests/LpDashboard.Domain.Tests/MarketWatchServiceTests.cs`

**Interfaces:**
- Consumes: `IBitcoinPremiumSource` (Task 3); `PremiumSeries.Summarise`, `PremiumSummary` (Task 2).
- Produces: `MarketSourcesDto` gains a fifth member `Premium` ("ok", "stale", "unavailable"). `MarketWatchDto` gains a last member `PremiumSummary? Premium` (the Domain record is the response shape), null exactly when the status is "unavailable". The `MarketWatchService` constructor takes `IBitcoinPremiumSource premium` right after `fearGreed`. Fetch under cache key `"premium"` with TTL `PremiumTtlMinutes` and stale max `StaleMaxHours`, calling `premium.GetHoursAsync(time.GetUtcNow(), token)` through the existing `AnswerAsync`, in parallel with the other sources. Status: "unavailable" when the summary is null; "stale" when the cache state is stale or `now - AsOf` is over 3 hours; else "ok".

- [ ] **Step 1: Update the test rig, then write the failing tests.** Add `FakePremium : IBitcoinPremiumSource` (public `List<PremiumHour> Hours`, `bool Fail`, `int Calls`, `DateTimeOffset? LastUntil`; returns `Fail` or `Ok(Hours)`), `Rig.Premium`, and pass it to the service in `Rig.GetAsync`. Change `All_sources_down_gives_an_empty_page_not_an_error` to fail it too, expect five "unavailable" and `Assert.Null(r.Premium)`. The rig's clock is 2026-11-17 15:00 UTC. New tests:
  - `Premium_block_is_built_from_the_hours`: 40 days of hourly values 0.05 up to the clock hour: `Premium.Today.Percent` 0.05, zone BuyingABit, `Days.Count` 30, `Sources.Premium` "ok", `Bands` equal to `PremiumZones.Bands`; `FakePremium.LastUntil` equals the clock.
  - `Premium_does_not_need_an_owner`: `owners: null` still returns it, `Loan` null.
  - `Premium_is_fetched_once_per_ttl`: two calls give `Calls == 1`; after `Clock.Advance(16 minutes)` the next gives 2.
  - `A_dead_premium_source_is_unavailable_and_the_rest_still_works`: `Premium` null, status "unavailable", `Sources.Headlines` "ok".
  - `Too_few_recent_hours_is_unavailable`: only 11 hours in the last 24 gives null and "unavailable".
  - `Premium_that_fails_after_working_is_stale_then_unavailable`: ok first; fail and advance 20 minutes: "stale" and `Today` still present; advance 25 h more: "unavailable" and null.
  - `A_source_that_stopped_updating_is_stale`: hours end 4 hours before the clock, fetch succeeds: status "stale" with the data present; hours ending 2 hours before: "ok".
- [ ] **Step 2: Run** `dotnet test backend/tests/LpDashboard.Domain.Tests --filter MarketWatchServiceTests`. Expected: build FAIL.
- [ ] **Step 3: Implement** the DTO members and the service change as above, following the Fear & Greed code in the same file.
- [ ] **Step 4: Run** the filter, then the whole suite `dotnet test backend/LpDashboard.slnx`. Expected: all PASS.

### Task 5: The Market watch row

**Files:**
- Modify: `frontend/src/api/types.ts` (Market watch section), `frontend/src/pages/MarketPage.tsx`
- Create: `frontend/src/lib/premium.ts`, `frontend/src/components/market/PremiumRow.tsx`, `PremiumGauge.tsx`, `PremiumChart.tsx`

**Interfaces:**
- Produces types (camelCase JSON; `day` is `"2026-11-18"`; `asOf` an ISO instant; percents are numbers): `PremiumZone = "SellingHard" | "SellingABit" | "Flat" | "BuyingABit" | "BuyingHard"`; `PremiumBandDto { zone; from; to }`; `PremiumDayDto { day; percent; zone }`; `PremiumReadingDto { percent; zone }`; `PremiumDto { bands; days; today; yesterday: PremiumReadingDto | null; week: ...; month: ...; asOf: string }`. `MarketSourcesDto` gains `premium: MarketSourceStatus`; `MarketWatchDto` gains `premium: PremiumDto | null` (null exactly when `sources.premium` is "unavailable").
- Produces `lib/premium.ts`: `PREMIUM_LABEL` ("US selling hard", "US selling a bit", "Flat", "US buying a bit", "US buying hard"); `PREMIUM_SHORT` ("selling hard", "selling a bit", "flat", "buying a bit", "buying hard"); `PREMIUM_CHIP` (both selling zones `bg-down-fill/15 text-down`, Flat `bg-raised text-fg-2`, both buying zones `bg-up-fill/15 text-up`); `PREMIUM_VAR` (`"var(--zone-1)"` to `"var(--zone-5)"` in order); `formatPremium(percent: number): string` ("+0.06%", "−0.03%", "0.00%": a sign only when not zero, true minus sign); `premiumRange(days: PremiumDayDto[]): { low: PremiumDayDto; high: PremiumDayDto }` (earlier day wins a tie); `premiumAxisMax(days): number` (the larger of 0.15 and the biggest absolute value rounded up to the next 0.05). Dates reuse `fearGreedDay` from `lib/fearGreed.ts`.
- Produces components: `PremiumRow({ data }: { data: MarketWatchDto })`, `PremiumGauge({ percent, zone, bands })`, `PremiumChart({ days })`.

- [ ] **Step 1: Types.** Add the types above to `types.ts`.
- [ ] **Step 2: `lib/premium.ts`** as specified.
- [ ] **Step 3: `PremiumGauge`**: the same half circle as `FearGreedGauge` (same viewBox, radius, ring, white tick), but the angle for a value is `180 - ((clamp(percent, -0.30, 0.30) + 0.30) / 0.60) * 180` degrees from the left and each arc runs from its band's `from` to its `to` in that scale with the same small gap, stroke `PREMIUM_VAR[zone]`, opacity 1 for the band holding `percent` (by `zone`) and 0.28 for the others. The centre shows `formatPremium(percent)` in the `num` font at 38px, "Selling" under the left end and "Buying" under the right end, role `img` with an aria-label "US buying versus selling {formatPremium}: {label}".
- [ ] **Step 4: `PremiumChart`**: recharts `BarChart` in a `ResponsiveContainer` (height 210), one `Bar` over `days` with `Cell` fills `var(--up-fill)` for percent at or above 0 and `var(--down-fill)` below (the last bar at full opacity, the rest at 0.62), a `ReferenceArea` from -0.03 to +0.03 (`fill="var(--raised)"`, opacity 0.55, label "flat"), a `ReferenceLine` at 0, y domain `[-m, m]` with `m = premiumAxisMax(days)` and ticks `-m, 0, m` formatted with `formatPremium`, x axis with the first, middle and last day via `fearGreedDay`, a `Tooltip` showing date, `formatPremium` and a zone chip (`PREMIUM_CHIP`, `PREMIUM_LABEL`), and below "Lowest {n} on {date}" and "Highest {n} on {date}" from `premiumRange`. `isAnimationActive={false}`. Colours from tokens and `lib/chartTheme.ts` only.
- [ ] **Step 5: `PremiumRow`**: when `data.premium` is null, one `Panel` titled "US buying vs selling" saying "The Bitcoin premium is unavailable right now." Otherwise a grid with the same classes as `FearGreedRow` (`grid gap-4 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)] lg:items-stretch`): left `Panel` "US buying vs selling" (action "Updated hourly", and "Out of date" in `text-warn` when `data.sources.premium === "stale"`) holding the gauge, a `Chip` with the zone label, "Average of the last 24 hours", rows Yesterday, 7-day average and 30-day average with the zone chip and `formatPremium` (a row is left out when null), and the note "Coinbase's Bitcoin price against Binance's, both in dollars. Above zero, US buyers are paying more. It is a clue about who is buying, not a count of trades and not a forecast."; right `Panel` "Last 30 days" (action "Daily average · Coinbase vs Binance") holding the chart. Use `Panel` from `components/ui.tsx` and `Chip` from `components/borrow/bits.tsx`.
- [ ] **Step 6: Wire into `MarketPage`**: render `<PremiumRow data={data} />` directly after `<FearGreedRow data={data} />` and add `["bitcoin premium", data.sources.premium]` to `notices`.
- [ ] **Step 7: Run** from `frontend/`: `npm run typecheck`, then `node scripts/check-contrast.mjs`. Expected: no errors; the contrast script passes as before.

### Task 6: The Overview chip

**Files:**
- Create: `frontend/src/components/market/PremiumChip.tsx`
- Modify: `frontend/src/components/market/MarketLine.tsx`

**Interfaces:**
- Consumes: `PremiumDto`, `PREMIUM_CHIP`, `PREMIUM_SHORT`, `formatPremium` (Task 5).
- Produces: `PremiumChip({ premium }: { premium: PremiumDto })`, text `US flow {formatPremium(today.percent)}, {PREMIUM_SHORT[today.zone]}` in a `Chip` coloured by `PREMIUM_CHIP`, with a title "Coinbase price against Binance's, in dollars. A clue about who is buying, not a forecast."

- [ ] **Step 1: Implement** the chip and render it in `MarketLine` right after `FearGreedChip`, only when `data.premium` is not null. It must not change the line's `risky` (amber) rule.
- [ ] **Step 2: Run** `npm run typecheck` and `npm run build` from `frontend/`. Expected: both succeed.

### Task 7: Check it in the real app and update the docs

**Files:**
- Modify: `docs/market-watch-spec.md` (a short section "12. US buying vs selling (added 2026-11-18)" in the style of section 11), `docs/superpowers/specs/2026-11-18-coinbase-premium-design.md` (status: built)

- [ ] **Step 1: Live source check.** `curl` the Coinbase candles for BTC-USD and USDT-USD and the Binance klines with the URLs from Global Constraints. Each answers 200 with data whose newest open time is within the last hour. If Binance answers 451 from this machine, say so in the report.
- [ ] **Step 2: Start a second instance on other ports with a throwaway database.** Build to a scratch folder (`dotnet build backend/src/LpDashboard.Api -o <scratch>/api-out`), then run `dotnet <scratch>/api-out/LpDashboard.Api.dll --urls http://localhost:5190` with environment `ConnectionStrings__Default="Data Source=<scratch>/test.db"`, `Cors__Origins__0=http://localhost:5191` and `Sync__RunOnStartup=false`. From `frontend/`: `VITE_API_PROXY=http://localhost:5190 npx vite --port 5191`. Create a test account on the app's own setup page with a generated password; do not print it.
- [ ] **Step 3: Check `/market`.** The new row sits directly under Fear & Greed; the gauge number and word match `/api/market/watch`'s `premium.today`; the chart has 30 or fewer bars, today's last; hovering shows date, value and zone; the Yesterday, 7-day and 30-day rows match the JSON; the Overview line shows the chip. Check dark and light and a 390px-wide window. Check that the console and `read_network_requests` show no errors.
- [ ] **Step 4: Check the failure path in the page.** The source failing is covered by the Task 3 and 4 tests; here check how the page looks. With `javascript_tool`, wrap `window.fetch` so the watch answer has `premium: null` and `sources.premium: "unavailable"`, then press Refresh. Expected: the panel says "The Bitcoin premium is unavailable right now.", the chip is gone, the notice line names the premium, the rest of the page is unchanged. Repeat with `sources.premium: "stale"` and the data kept: "Out of date" shows on the panel.
- [ ] **Step 5: Stop both instances and delete the scratch database.** Update the two docs. Report what was verified and what was not, including the real reading and whether the zone thresholds feel right against it (they are first guesses).
