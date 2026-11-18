# Crypto fear & greed on Market watch: design

Status: approved and built 2026-11-18 (not committed). Plan: [2026-11-18-fear-greed.md](../plans/2026-11-18-fear-greed.md). UI mock (option A plus the Overview chip, chosen by the owner): https://claude.ai/artifact/CiT8zRNiXKpWZNdFCghYr6

Size: a small change. It adds one source, one field on the existing Market watch response, one panel pair and one chip. It follows the pattern in `docs/market-watch-spec.md`.

## Intent

The owner borrows against LP tokens, and crypto prices swing with mood. Market watch already says what is coming and what a fall does to the loan. It does not say how worried or excited the market is today. The owner pointed at CoinMarketCap's Fear & Greed gauge and wants the same reading, crypto only, with a bit of history so a quick slide into fear is visible.

## Goals, non-goals, anti-goals

Goals
- A Fear & Greed gauge (0 to 100, five zones) with Yesterday, Last week and Last month, and a 30-day chart, on the Market watch page.
- A mood chip on the Overview line.
- No key, no cost, nothing stored in the database, nothing personal sent out.

Non-goals
- Stocks (CNN's index answered a plain request with HTTP 418 and is unofficial).
- Alerts when the mood crosses a zone.
- Other indicators from the reference (altcoin season, ETF flows).
- Any history older than 30 days.

Anti-goals
- Reading as advice. The copy says "a mood reading, not a forecast". No "buy" or "sell", no colour that implies one.
- A dead source shown as a number. When it fails the panel says it is unavailable.

## Approaches

1. **Fold it into the existing Market watch response (chosen).** The service fetches the index next to the calendars and headlines, through the same cache, and the response gets a `fearGreed` block and a source status. The Overview chip reads the same query the line already uses, so it adds no request. Trade-off: one more call in the cold load, but it is a single small request made in parallel.
2. A separate endpoint and its own hook. Cleaner to test alone, but the Overview line would make a second request and the page would show two loading states. Not chosen.
3. The browser calls alternative.me directly. Breaks the "sources sit behind interfaces and degrade to unavailable" rule, hands the owner's IP to a third party and skips the cache. Rejected.

## Design

Data source: `GET https://api.alternative.me/fng/?limit=31`, no key. Checked 2026-11-18. Returns 31 daily rows, newest first: `value` (string 0 to 100), `value_classification`, `timestamp` (unix seconds, midnight UTC). It updates once a day. Index zones as the source labels them: 0 to 25 Extreme fear, 26 to 46 Fear, 47 to 54 Neutral, 55 to 75 Greed, 76 to 100 Extreme greed.

Backend (follow the existing Market layers)
- Application: `IFearGreedSource` next to the other sources, returning a `SourceOutcome` of days, like `IHeadlineSource`. `MarketWatchService` fetches it in parallel with the calendars, through `MarketCache`, with the same stale rule (serve old data up to the stale limit and mark the source `stale`; with nothing, `unavailable`). A new setting, `FearGreedTtlMinutes` (my call: 60), sits in `MarketSettings`.
- Domain: a pure zone function from value to zone, with xUnit tests at every edge (25/26, 46/47, 54/55, 75/76). The zone comes from the value, not from the source's label text, so colours and words cannot disagree.
- Infrastructure: the adapter, with a saved fixture from the real response. It treats a non-200, a bad body, an empty list or a value outside 0 to 100 as "did not answer".
- `MarketWatchDto` gets `FearGreed` (null when unavailable): the days oldest first, each with date, value and zone. `MarketSourcesDto` gets `FearGreed` with "ok", "stale" or "unavailable". The response also carries Today, Yesterday, Last week and Last month, picked by date on the server (7 days and 30 days before the latest reading; null when that day is missing), so the rule is unit tested like the rest of the Market logic. Changed from the first draft, which picked them in the frontend, because the frontend has no test runner.

Frontend
- Types in `api/types.ts`; no new hook, the panel and the chip use `useMarketWatch`.
- Market watch page: a new row under the loan test and above the calendar, in the same 5/7 split. Left panel "Crypto fear & greed": the gauge with the current zone lit and a white tick (the same tick the range bars use), the zone chip, the three comparison rows and the one-line note. Right panel "Last 30 days": a line over faint zone bands, a hover readout, and the low and high. If the source is unavailable, one panel says so and the rest of the page is unchanged; the existing notice line also names it.
- Overview line: one chip, "Crypto mood 11, extreme fear", coloured by zone, left out when the source is unavailable.
- Colours use the existing tokens only: down for extreme fear, warn for fear, a neutral grey for neutral, up (lighter, then full) for greed and extreme greed. Both themes, as in the mock.

## Constraints

- No wallet address or other user data goes in the request, only the day count. No key to protect.
- Unofficial third-party endpoint: it may change or block, so everything sits behind the interface and a failure shows "unavailable".
- No database change and no background service.
- The page and chip must work in dark and light, down to phone width, with the chart readable (draw it at its real width).

## Left to the builder

- Exact class and file names, following `Risk/` and the existing Market files as the model.
- How the chart's zone bands get their edges (the zone function lives in the Domain layer and the frontend has no test runner, so keep one copy of the edges, in the response if that is cleaner).
- Wording of the empty and unavailable states.
- Whether `Last month` should be the oldest of the 31 days (my default) or the same date a calendar month ago.
