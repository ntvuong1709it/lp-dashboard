# Market watch: what is coming that can move stocks and crypto, and what it means for my loan

Status: built 2026-11-17 (all four build steps). Where the build differs from this spec: the live calendar needed more name rules and the "No record found" answer (section 4), the headline feeds listed there are in `appsettings.json`, upcoming days are cached 3 hours and 8 days are fetched at once (cold load about 9 s), and the Overview card and banner lead with the soonest High-impact event, earnings included. UI mock: [market-watch-mock.html](market-watch-mock.html) (approved, with the changes under "Differences from the mock").

Implementation notes for whoever builds it:
- Follow the repo conventions in `PLAN.md` / `README.md`: pure logic in the C# Domain layer with xUnit tests (no frontend test runner), HTTP behind interfaces in Application, adapters in Infrastructure, API in `LpDashboard.Api`.
- The user pushes and opens PRs themselves; commit only when asked. Build in the order under "Build order" and **stop after each step** so the user can verify.
- Never put wallet addresses or other user data in outbound requests. The sources below only ever receive a date. Never log or print API keys or the RPC URL.

## 1. Problem

The owner provides liquidity on Base and borrows against LP tokens. Prices of crypto follow the stock market and rate expectations: a hot CPI print, an FOMC decision or an Nvidia earnings call can move the loan toward margin call within hours. Today nothing in the app says what is coming, so the owner finds out when the price has already moved.

## 2. Goal, non-goals, anti-goals

Goal: before a high-impact event, the owner can glance at the Overview page and see (a) what the next events are and when, (b) how much price fall the loan can take, and (c) what a 10, 20 and 30 percent fall would cost in extra collateral. After the event, the result is shown against the forecast with a plain risk-on / risk-off tag.

Non-goals (v1):
- Alerts. Heads-up Telegram messages were offered and declined.
- AI. No LLM reads headlines or writes explanations. No API key, no cost.
- Crypto-specific events (options expiry, token unlocks, ETF decisions). No free source was found.
- Non-US macro events and non-US earnings.
- Headline tags. Headlines are shown untagged.
- Persistence. Nothing is stored in the database; no migration.
- News matched to the owner's holdings.

Anti-goals:
- Anything that reads as investment advice. Tags describe the result against the forecast ("hotter than expected"), never "sell" or "buy".
- A missing tag read as "all clear". The UI says an untagged event is not a judgement.
- Wrong data shown as right. A dead source shows "unavailable", never guesses.

## 3. Decisions from the interview

| Topic | Decision |
| --- | --- |
| What to cover | Scheduled macro events (FOMC, inflation, jobs, GDP) and big-company earnings, plus a short headlines list. |
| After the event | A risk-on / risk-off / neutral tag from a fixed rule (actual against consensus), with the numbers as the reason. |
| Loan | Tie the loan in: buffer, price drop to liquidation, and a stress table with collateral to add. |
| Placement | Compact "Market watch" card on Overview under the attention strip, plus a full Market watch page (`/market`, new nav item). |
| Data | Approach A: Nasdaq's public calendar endpoints and the Federal Reserve press feed, no keys, each behind an interface. Registered APIs (FRED, Finnhub) and an LLM-built calendar were rejected: keys and money, or invented dates. |
| Alerts | None. |

## 4. Sources checked (2026-11-17)

Probed from the build machine with a plain GET and a browser-like `User-Agent`, no key:

| Source | Result | Use |
| --- | --- | --- |
| `GET https://api.nasdaq.com/api/calendar/economicevents?date=YYYY-MM-DD` | Works. One call per day, ~30 to 90 rows. Row: `gmt`, `country`, `eventName`, `actual`, `consensus`, `previous`, `description`. | Macro events and results. |
| `GET https://api.nasdaq.com/api/calendar/earnings?date=YYYY-MM-DD` | Works. Row: `time` (`time-pre-market`, `time-after-hours`, `time-not-supplied`), `symbol`, `name`, `marketCap` (`"$1,016,233,530,000"`), `epsForecast` (`"$0.64"`), `fiscalQuarterEnding`, `noOfEsts`. **No actual EPS.** | Earnings dates and forecast. |
| `GET https://www.federalreserve.gov/feeds/press_monetary.xml` (and `press_all.xml`) | Works. RSS 2.0: `title`, `link`, `pubDate`, `category`. | Headlines (Fed statements and schedule changes). |
| `https://nfs.faireconomy.media/ff_calendar_thisweek.json` (impact-rated calendar) | No response from the probe. | Not used. |
| CoinDesk and Cointelegraph RSS | No response from the probe; unverified from the server. | Optional extra feeds, see below. |
| FRED API | Needs a key. | Not used. |
| BLS schedule file | 403. | Not used. |

Quirks the adapter must handle (seen in the real responses):
- `gmt` is **not GMT**: UK Core CPI shows `02:00`, which is 07:00 UK time, so it is **US Eastern**. Convert with the `America/New_York` zone (daylight saving included) and return UTC instants. A test must pin a date on each side of a DST change. Re-verify the offset against a known release (for example US Retail Sales at 08:30 Eastern) before trusting it.
- Empty values come as `"&nbsp;"` or `" "`. Treat both as "not published yet". Text is HTML-escaped (`&lt;`, `&amp;`): decode it.
- A day with no events is not an error body: Nasdaq answers `{"data":null,...,"status":{"rCode":200,"bCodeMessage":[{"code":1002,"errorMessage":"... No record found."}]}}`. The adapter reads that as an empty day (found live, 2026-11-17; without it every empty weekday made the calendar "stale").
- The same name can appear twice (headline reading and month-on-month or year-on-year). Keep one row per event and time, preferring the row that has a consensus.
- Values are strings with units: `"3.1%"`, `"1.380M"`, `"19.80K"`, `"$0.64"`. Parse them; an unparseable value means no tag, not an error.
- The endpoints are unofficial and may change or block. Everything sits behind interfaces (`IEconomicCalendarSource`, `IEarningsCalendarSource`, `IHeadlineSource`), so a failure degrades to "unavailable".

## 5. What is shown

### Events

Each event has: UTC time, local time shown in the browser's zone, name, category (`Macro`, `Earnings`), impact (`High`, `Medium`), previous / forecast / actual, and after release a tag.

Which events appear (rules live in `MarketOptions`, config section `Market`, so they can change without code):
- **Country**: United States only.
- **High impact** (name contains, case-insensitive, whole words): `Fed Interest Rate Decision` (the decision), `FOMC Press Conference`, `FOMC Minutes`, `Fed Chair` (speeches), `CPI`, `Core CPI`, `PCE`, `Core PCE`, `PPI`, `Nonfarm Payrolls`, `Unemployment Rate`, `GDP`.
- **Medium impact**: `Retail Sales`, `Initial Jobless Claims`, `JOLTS`, `ISM` PMIs, `Consumer Confidence`, `Michigan`.
- **Dropped even when a name above matches** (`Market:ExcludeNames`): `GDPNow`, `Member` (FOMC member speeches), `Dallas Fed`, `PCE Prices`, `GDP Price Index`, `GDP Sales` (components of the GDP release that repeat the PCE and GDP rows). Found in the live calendar on 2026-11-17.
- **Everything else is dropped** (mortgage indexes, inventories and similar noise).
- **Earnings**: High when market cap is at least `Market:EarningsHighMarketCapUsd` (default 200 billion) or the symbol is on `Market:EarningsWatchlist` (default NVDA, AAPL, MSFT, AMZN, GOOGL, META, TSLA, COIN, MSTR). Medium when at least 50 billion. Everything else is dropped.

### Tags (after release)

Pure Domain function: `(event family, actual, consensus) -> RiskOn | RiskOff | Neutral | None`, plus a reason string made from the numbers ("Actual 3.1% vs forecast 3.0%: hotter than expected, fewer cuts priced in"). Never fed by free text.

| Family | Rule |
| --- | --- |
| Inflation (`CPI`, `Core CPI`, `PCE`, `Core PCE`, `PPI`) | Actual above consensus by more than the tolerance: `RiskOff`. Below by more than the tolerance: `RiskOn`. Within it: `Neutral`. |
| Rate decision | Actual rate above consensus: `RiskOff`. Below: `RiskOn`. Equal: `Neutral`. |
| Everything else (jobs, GDP, retail sales, earnings) | `None`. The direction depends on the regime ("bad news is good news"), so no tag is better than a wrong one. The row still shows actual against forecast. |

`Market:InflationToleranceDefault` is 0.05 percentage points. If consensus or actual does not parse, the tag is `None`.

Earnings after the event show "Reported" with no tag: the source has no actual EPS. (Nasdaq has a per-company surprise endpoint; checking whether it is worth a call for the watchlist is optional and not required for v1.)

### Headlines

Latest items from the Fed press feed, plus any URLs in `Market:HeadlineFeeds`. Checked from the build machine on 2026-11-17 (with a redirect-following GET and a `User-Agent`): CoinDesk, Cointelegraph and CNBC Finance answer, so `appsettings.json` lists them next to the Fed feed. Merged newest first, the top 10 are mostly crypto and market news; Fed items appear when they are recent. Untagged. Each item: title, source, age, link.

### Loan

Computed server-side in the Domain layer from the existing Borrow data, for the loan with the worst risk (the same loan the Borrow page calls `worstRisk`), using that loan's `collateralUsd`, `debtUsd` and `risk`.

- Buffer: `Comfortable` when the price drop to liquidation is above 40 percent, `Thin` between 25 and 40, `Tight` below 25 (`Market:BufferThinBelowPercent`, `BufferTightBelowPercent`).
- Stress table for falls of 10, 20 and 30 percent, assuming the collateral falls 1:1 with price (the worst case; LP collateral usually falls less, and the UI says so).
  - LTV loans: new LTV = LTV / (1 - fall). Status `OK`, `Watch` (within 8 points of margin call), `Margin call`, `Liquidation`. "Add to stay under margin call" = max(0, debt / marginCallLtv - collateral x (1 - fall)).
  - Health factor loans (Aave): new HF = HF x (1 - fall). Status `OK`, `Watch` (below 1.25), `Liquidation` (below 1.0). "Add to keep HF at 1.2" = max(0, 1.2 x collateral / HF - collateral x (1 - fall)).
  - No loan or no price: the loan blocks are hidden and the calendar still works.

## 6. API and refresh

`GET /api/market/watch?days=7|14&owner=...` returns:

```
{ updatedAt, sources: { calendar, earnings, headlines } each "ok" | "stale" | "unavailable",
  upcoming: Event[], happened: Event[] (last 3 days),
  headlines: Headline[], loan: LoanStress | null }
```

- The calendar and headlines do not depend on the owner; only `loan` does.
- No database and no background service. An in-memory cache keeps one entry per source and day: upcoming days for 1 hour, today for 10 minutes, past days for 24 hours once an actual is present, headlines for 15 minutes. On the first load, day calls run in parallel with a small limit (for example 4) and a timeout per call.
- A failed call serves the cached copy up to 24 hours old and marks the source `stale`; with no cache the source is `unavailable`.
- The window is 7 or 14 days. (The mock also offered 30; that is 60+ calls per refresh and is left out of v1.)

## 7. UI

As in the mock (`market-watch-mock.html`), minus the alerts card. Frontend follows the existing Overview components (`StatCard`, `Panel`, the attention strip's chips).

- **Overview card**, under `AttentionStrip`: next high-impact event with countdown (or "just happened" with result and tag), the loan buffer block, and the stress table. A line lists the next two events and links to the page. With no loan, the middle and right columns are replaced by a one-line hint to set up loans on Borrow. With sources unavailable, a muted one-line notice.
- **Market watch page** `/market`, nav item "Market": impact filter (all or high only), window (7 or 14 days), banner (colour follows the buffer), Upcoming, Already happened, Your loan with the stress table, Latest headlines.
- The page and card honour the hide-balances toggle, like the rest of the app.
- Times: the UI converts UTC to the browser's zone. Countdown is computed in the browser.

## 8. Differences from the mock

The mock was drawn before the source check. In the build:
- No BTC / ETH options expiry row (no source).
- "Why" lines are rule-made reasons from the numbers, not AI text, and the "(AI read, may be wrong)" note goes. In its place the tag has a tooltip: "From actual against forecast. Not a judgement of what happens next."
- Headlines have no risk tag.
- Earnings after the event show "Reported" with no tag (Walmart's neutral tag in the mock does not happen).
- No 30-day window.
- No heads-up alerts card.

## 9. Build order

Stop after each step and say what was verified and what was not.

1. **Domain and tests**: event impact classification, value parsing, tag rules, loan buffer and stress table (both LTV and health factor), time conversion from US Eastern across a DST change. Pure logic, xUnit.
2. **Sources and endpoint**: the three adapters with fixtures saved from the real responses (strip anything personal; there is none), the cache, `MarketWatchService`, `GET /api/market/watch`, tests with canned responses, including a dead source and the `&nbsp;` and duplicate-row cases. Check the Eastern-time offset and the crypto RSS feeds against the live network here.
3. **Market watch page**: types, API hook, route, nav item, page.
4. **Overview card**.

## 11. Crypto fear & greed (added 2026-11-18)

Market watch also shows the crypto Fear and Greed index: a gauge with yesterday, last week and last month, and a 30-day chart on the page, and a chip on the Overview line. Source: `https://api.alternative.me/fng/?limit=31` (no key, unofficial, only the day count is sent), cached 60 minutes (`Market:FearGreedTtlMinutes`), stale up to `StaleMaxHours`, then "unavailable". The zones (0 to 25, 26 to 46, 47 to 54, 55 to 75, 76 to 100) live in `FearGreedZones` in the Domain layer. It is a mood reading, not a forecast, and never turns the Overview line amber. Design and plan: `docs/superpowers/specs/2026-11-18-fear-greed-design.md`, `docs/superpowers/plans/2026-11-18-fear-greed.md`.

## 12. US buying vs selling, the Coinbase premium (added 2026-11-18)

Market watch also shows who is paying more for Bitcoin, US buyers or the rest of the world: a gauge with the last 24 hours, yesterday, a 7-day and a 30-day average, and 30 daily bars on the page, and a chip on the Overview line. The gap is Coinbase BTC-USD against Binance BTC-USDT converted to dollars with Coinbase's USDT-USD price (the raw USDT gap read -0.11% on 2026-11-18 when the real gap was about 0.00%). Sources, no key, only product names and a time range sent: Coinbase `products/{BTC-USD,USDT-USD}/candles` (three 300-candle calls each) and Binance `api/v3/klines` on `data-api.binance.vision` (`api.binance.com` answers 451 from US addresses). Cached 15 minutes (`Market:PremiumTtlMinutes`); stale up to `StaleMaxHours`, then "unavailable"; "stale" also when the newest hour is over 3 hours old. The zones (flat inside +/-0.03%, "a bit" up to +/-0.10%, "hard" beyond) live in `PremiumZones` in the Domain layer and are first guesses: on the first live check all 30 daily values lay between -0.09% and 0.00%. It says who is paying more, never "buy" or "sell", and is not a forecast. Design and plan: `docs/superpowers/specs/2026-11-18-coinbase-premium-design.md`, `docs/superpowers/plans/2026-11-18-coinbase-premium.md`.

## 10. Left to the builder

- Exact class names and folder layout (follow `Risk/` as the model).
- Whether the Nasdaq per-company earnings-surprise call is worth adding (see section 5).
- Wording of tooltips and empty states.
