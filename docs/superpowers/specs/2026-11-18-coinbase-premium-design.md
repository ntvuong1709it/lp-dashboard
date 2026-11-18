# US buying vs selling (Coinbase premium) on Market watch: design

Status: approved and built 2026-11-18 (not committed). Plan: [2026-11-18-coinbase-premium.md](../plans/2026-11-18-coinbase-premium.md). UI mock (option B, the gauge, chosen by the owner): https://claude.ai/artifact/Pcu8DUw649tkrP5mvRi4NF

Size: a small change. It adds one source, one field on the existing Market watch response, one panel pair and one chip, and follows the Fear & Greed pattern (`2026-11-18-fear-greed-design.md`).

## Intent

The owner wants to know what US traders are doing lately: buying more or selling more. Coinbase is where US money trades Bitcoin; Binance is mostly the rest of the world. When Coinbase's dollar price runs above Binance's, US buyers are paying more; below, US sellers are pushing. The panel turns that gap into a plain word, a number and a 30-day picture, next to the Fear & Greed mood.

## Goals, non-goals, anti-goals

Goals
- A gauge (selling to buying, zero in the middle) with the current word and number, three comparison rows (Yesterday, 7-day average, 30-day average) and a 30-day bar chart of daily averages, on the Market watch page, under Fear & Greed.
- A chip on the Overview line.
- No key, no cost, nothing stored in the database, nothing personal sent out.

Non-goals
- Alerts when the reading flips.
- Other regional premiums, other exchanges, ETH or other coins.
- Hourly chart, or history beyond 30 days.
- A fallback to a second reference exchange (Bybit and OKX also answered on 2026-11-18, so it can be added later if Binance gets blocked).

Anti-goals
- Reading as advice. The words say who is paying more; never "buy" or "sell" to the owner. The note says it is a clue about who is buying, not a count of trades and not a forecast.
- A stablecoin wobble shown as US flow. On 2026-11-18 USDT traded at 0.9989 and the raw Coinbase-vs-USDT gap read about -0.11% while the real gap in dollars was about 0.00%.
- A dead source shown as a number, or old data shown as fresh.

## Approaches

1. Classic: Coinbase BTC-USD against Binance BTC-USDT. Simplest, matches the well-known index, but wrong whenever USDT drifts from $1. Not chosen.
2. **Dollar-corrected (chosen).** Binance BTC-USDT multiplied by Coinbase's USDT-USD price gives Binance's Bitcoin price in real dollars; the gap to Coinbase BTC-USD is the premium. One extra request; all three feeds were checked live.
3. Coinbase against Kraken or Bitstamp USD. Pure USD, but both are US/EU venues, so the US-against-offshore contrast the owner asked for is weaker. Not chosen.

The owner chose approach 2 and the gauge layout (option B).

## Design

Data (public, no key, checked 2026-11-18). The reference host for Binance is `data-api.binance.vision`, its public market-data host, because `api.binance.com` answers HTTP 451 from US addresses. Both base addresses are config.
- Coinbase `GET /products/BTC-USD/candles?granularity=3600&start=..&end=..` and the same for `USDT-USD`. Each call returns at most 300 candles, so 30 days of hours (720) is three calls per product.
- Binance `GET /api/v3/klines?symbol=BTCUSDT&interval=1h&limit=720`, one call.
- Join the three series on candle open time and use close prices. An hour missing from any of them is dropped. Per hour: `premium% = (coinbaseBtcUsd - binanceBtcUsdt x coinbaseUsdtUsd) / (binanceBtcUsdt x coinbaseUsdtUsd) x 100`.

Backend (follow the existing Market layers)
- Domain: pure `PremiumZones` (five zones from a percent value, one place for the edges) and `PremiumMath` (the hourly gap, UTC-day averages, rolling averages), with xUnit tests at every zone edge and for the math. Zones (my first guess, to tune against real data): flat inside +/-0.03%, "a bit" up to +/-0.10%, "hard" beyond. Gauge scale +/-0.30%.
- Application: `IBitcoinPremiumSource` returning a `SourceOutcome` of joined hourly points, like `IFearGreedSource`. `MarketWatchService` fetches it in parallel with the other sources through `MarketCache`, with the same stale rule. New setting `PremiumTtlMinutes` (my call: 15). The service builds the block on the server, as for Fear & Greed, so the rules are unit tested: Today = average of the last 24 hourly gaps (rolling, not the partial UTC day); Yesterday = the last full UTC day's average; 7-day average = the last 168 hours; 30-day average = all hours; the chart = one average per UTC day, oldest first, today's bar partial. A comparison row is null when under half its hours exist.
- Infrastructure: the adapter with saved fixtures from the real responses. A non-200, bad body, empty result or any one of the seven requests failing counts as "did not answer". A price that is not positive, a USDT price outside 0.90 to 1.10 or an hourly gap beyond +/-5% drops that hour (my call: a glitch candle must not become a spike on the chart). Short answers are handled by the coverage rules above, not by failing. Failures never name the URL.
- `MarketWatchDto` gets `Premium` (null when unavailable) and `MarketSourcesDto` gets `Premium` ("ok", "stale", "unavailable"). The block is "unavailable" when fewer than 12 usable hours exist in the last 24; "stale" when the cache is old or the newest hour is over 3 hours old.

Frontend (matches the owner-approved mock)
- Types in `api/types.ts`; no new hook, the panel and chip use `useMarketWatch`.
- Market watch page: a new row under Fear & Greed, same 5/7 split. Left "US buying vs selling": gauge with the current zone, the white tick (as the other gauge), the zone chip, the three rows and the note. Right "Last 30 days": green/red daily bars around a zero line, a faint "flat" band, a hover readout, lowest and highest. If the source is unavailable, one panel says so, and the existing notice line names it.
- Overview line: one chip, "US flow +0.06%, buying a bit", coloured by direction, left out when unavailable.
- Colours reuse the five zone tokens (down to up) and the existing tick. Both themes, down to phone width.

## Constraints

- Only a time range and product names are sent; no wallet address or user data. No key.
- Unofficial use of public endpoints; they may change or block (Binance already blocks some regions). Everything sits behind the interface and a failure shows "unavailable".
- Coinbase and Binance public limits (about 10 requests a second) are far above this use: seven calls, cached 15 minutes.
- No database change and no background service.

## Left to the builder

- Exact class and file names, following the Fear & Greed files as the model.
- Whether to generalise `FearGreedGauge` to take a signed range or write a small sibling.
- How the chart's flat band and zone edges reach the frontend (keep one copy of the edges, in the response, as with the Fear & Greed bands).
- Wording of the empty and unavailable states.
- Chunking of the Coinbase calls and the ordering of requests (parallel is fine).
