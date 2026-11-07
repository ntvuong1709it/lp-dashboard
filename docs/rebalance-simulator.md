# Rebalance simulator — design

Status: design only, not built. Listed on the Features page as "Later: Test a new range against past price action and see projected fees."

## Goal

For an open position, answer: **"If I move to range X, would I have done better, and is the move worth its cost?"**

Three options are compared side by side for one position:

| Option    | Meaning                                                              |
| --------- | -------------------------------------------------------------------- |
| Stay      | Keep the current range                                               |
| Rebalance | Withdraw, swap to the new ratio, mint in the new range               |
| Withdraw  | Exit to the two tokens and hold (the existing HODL baseline)         |

Non-goals: executing anything (the app is read-only), predicting future prices, multi-position optimisation, v4/Solana math (follows protocol support).

## What the user does

1. Opens a position (drawer or detail page) and clicks **Simulate rebalance**.
2. Picks a new range: presets (±5 / 10 / 20 % around current price, "same width, re-centred"), or drags handles on the range bar / types prices. Ticks snap to the pool's `TickSpacing`.
3. Picks a lookback window (7 / 30 / 90 days, capped at the pool's available data).
4. Sees results, and can tweak the range live.

Route: `/positions/:id/simulate`. Range state lives in the URL query (`?lower=&upper=&days=`) so a scenario can be shared and reopened.

## Results shown

- **Backtest table** (Stay vs Rebalance): fees earned, % time in range, fee APR on capital, value change from price movement (IL), rebalance cost, **net result**, and **breakeven days** (cost ÷ extra daily fees vs Stay).
- **Chart 1, price path**: historical price with both ranges shaded, in-range spans highlighted.
- **Chart 2, cumulative net result** over the window for Stay / Rebalance / Withdraw.
- **Chart 3, value at price**: position value across a price axis (current vs new range, and HODL), which is a forward-looking "what if price goes to X" view that needs no history.
- **Required swap**: how much of which token must be swapped to reach the new ratio, with estimated cost.
- Honest caveats shown inline (see Limits).

## Calculation model

All of it reuses `PositionMath` (`GetAmountsRaw`, `GetLiquidityRaw`, `TickToPrice`) and lives in a new pure domain class, `Domain/Metrics/RebalanceSimulator.cs`, with xUnit tests alongside the existing ones.

### 1. New position from current holdings

Take the position's current principal plus uncollected fees (what a withdraw yields): `A0, A1` and USD value `V`. Solve for the swap that makes the amounts match the new range at the current price. With `r = amount1/amount0` required by `[lower, upper]` at price `P`, swap `x` token0 → token1 so that `(A0 − x) × r = A1 + x × P × (1 − swapCost)`. Then `L' = GetLiquidityRaw(...)`. If price is outside the new range the position is single-sided and the swap is all of one token.

### 2. Rebalance cost

`cost = gas + swapFee + priceImpact`
- gas: median of the user's own past tx gas for these event types (data already stored in `PositionEvent.GasUsd`), falling back to a chain default in config. Three txs: decrease/collect, swap, mint.
- swapFee: swapped notional × the same pool's fee tier (assumes the swap routes through the pool itself, a conservative simple model).
- priceImpact: user-adjustable input, default 0.05 % of swapped notional. Not modelled from pool depth in v1.

### 3. Fee income (the hard part)

Fees per swap are `|amountIn| × feeTier`, earned by liquidity that is active at that tick. A position of liquidity `L'` in range earns `swapFee × L' / (L_active + L')` of each swap. The pool `Swap` event carries `amount0, amount1, sqrtPriceX96, liquidity, tick`, so it is enough to replay history without a subgraph (consistent with the "on-chain only" decision).

Two phases because ingestion is the expensive part:

**Phase A, no new ingestion (ship first).** Estimate fee yield from the position's own realised history: `feeYield = FeesEarned ÷ (time-weighted liquidity × time in range)`. Apply to the new range using the historical price path from `TokenPrices` (hourly, DefiLlama) to decide in/out of range each hour. Widening/narrowing scales yield by `old width ÷ new width` (liquidity concentration), a documented first-order approximation. Cheap, works for any position with fee history, but ignores that volume varies with time and that the other positions in the pool compete.

**Phase B, Swap-log backtest (accurate).** A new sync step pulls pool `Swap` logs (same adaptive `eth_getLogs` machinery in `EvmRpcClient`) and stores them *aggregated*, not raw:

```
PoolFeeBucket
  PoolId, HourUtc, TickBucket (tick floored to TickSpacing),
  Fee0PerLiquidity, Fee1PerLiquidity,   // Σ feeAmount / activeLiquidity, as strings/decimals
  VolumeUsd, SwapCount
```

Fee for any range = Σ over hours and over tick buckets inside `[lower, upper)` of `L' × feePerLiquidity`. Because buckets are per tick, any hypothetical range is answerable from one stored dataset. Ingestion is lazy and per pool: only pools that have an open position are backfilled (default 30 days, extendable), then kept current by the existing sync loop. Cursor stored in `SyncCursor` the same way as position history. Dilution `L'/(L_active+L')` is applied when `L'` is large relative to pool liquidity (flag when above ~5 %).

Phase B replaces the Phase A estimator behind the same interface (`IFeeEstimator`), so the UI and API do not change; the response just carries `method: "calibrated" | "swap-replay"` and the UI labels it.

### 4. Backtest loop

Hourly steps over the window. At each step: price from the price series (or, in Phase B, the last swap tick), in range or not, fees accrued for that hour, position value at that price (`GetAmountsRaw` × prices). Out of range earns zero fees and sits in one token. "Stay" starts from the position's actual value at window start; "Rebalance" starts at the same value minus cost, with the new range. Same engine for both so results are comparable.

## API

```
POST /api/positions/{id}/simulate
{
  "tickLower": -201200, "tickUpper": -198800,   // or priceLower/priceUpper
  "lookbackDays": 30,
  "priceImpactBps": 5,                           // optional
  "gasUsdOverride": null                         // optional
}
→ SimulationDto {
    method, window{from,to}, dataQuality{hoursCovered, hoursMissingPrice},
    stay{ feesUsd, timeInRange, ilUsd, netUsd, series[] },
    rebalance{ same fields + costUsd, swap{token,amount}, newAmounts, breakevenDays },
    withdraw{ netUsd, series[] },
    valueAtPrice[]   // price axis curve for current range, new range, hodl
  }
```

POST because the body is a scenario, and it is a pure computation with no persisted state. Fits the controller pattern in `PositionsController`; logic goes in a new `Application/Simulation/RebalanceSimulationService` which loads the position, pool and price series and calls the domain simulator. Protocols that can't be simulated yet return 400 with a clear message.

## Frontend

- New page `SimulatePage.tsx` plus `RangePicker` component (reuse `RangeBar` visuals, add draggable handles) and `useSimulation` hook in `api/client.ts` (debounced, keeps previous data while loading).
- Entry points: button in `PositionDrawer`, the detail page, and a row action on out-of-range positions (where the question is most likely asked).
- Recharts for the three charts, reusing `chartTheme.ts`. Respect the existing privacy mode and invert-price toggle.
- Mark the Features page entry as planned → done when shipped.

## Limits and honesty

- Backtests are history, not forecasts; shown with that wording.
- Phase A ignores pool-wide volume changes and competing liquidity; Phase B ignores the effect your own liquidity would have had on others' routing and uses simplified swap cost.
- Price series is hourly DefiLlama, so intra-hour range exits are missed.
- Staked Slipstream positions earn emissions rather than swap fees: simulation covers swap fees only and says so; emissions are excluded from both sides.
- Missing prices: windows with gaps are shortened or flagged via `dataQuality`.

## Testing

- Domain: swap-amount solver (token ratios round-trip through `GetLiquidityRaw`/`GetAmountsRaw`), out-of-range single-sided cases, breakeven maths, backtest on a synthetic price path with known answer (price stays in range → fees = rate × time).
- Phase B: fee-per-liquidity aggregation against a hand-built Swap sequence; replaying the user's *actual* range and comparing with their *actual* collected fees is the validation test for the whole model, and should be shown as a "model vs actual" sanity line in the UI.

## Build order

1. Domain `RebalanceSimulator` + tests, using Phase A estimator.
2. `RebalanceSimulationService` + `POST …/simulate`.
3. Frontend page with range picker, value-at-price chart, results table.
4. Backtest charts.
5. Phase B: `PoolFeeBucket` entity + migration, Swap ingestion in `UniswapV3StyleProvider`, `SwapReplayFeeEstimator`, "model vs actual" check.

## Open questions

- Phase A first, or go straight to Phase B? (A is ~3 days and lets us validate UX; B is the accurate version and adds a migration plus RPC load.)
- Default lookback and how far back Phase B should backfill, given Alchemy `eth_getLogs` costs on busy Base pools.
- Should the simulator also suggest a range (e.g. best backtested width), or stay a manual what-if tool?
