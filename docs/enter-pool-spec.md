# Enter-pool planner (swap amounts + hedge)

Status: built (2026-11-10), awaiting the user's verification against a real wallet and the Aerodrome deposit screen. The numbers in the mockup are the reference behaviour: `docs/enter-pool-mock.html` (open it in a browser; the formulas in its `<script>` are the maths this spec describes, in JavaScript).

Implementation notes for whoever builds it:
- Follow the repo conventions in `PLAN.md` / `README.md`: maths in the C# Domain layer with xUnit tests (there is no frontend test runner), API in `Api/Controllers`, UI in `frontend/src/components/pools/`.
- The user pushes and opens PRs themselves; commit only when asked.
- Build in the order under "Build order", and stop after each step to let the user verify.
- "Assumptions I made" below were not individually confirmed; the user approved the plan as a whole.

## Context

Entering a concentrated-liquidity range needs a specific token mix: a WETH/USDC range of ±10% needs roughly 50/50 by value, a range above price needs only one token. Today the Pools page shows APR, fees and break-even for a range, but not what to actually hold to enter it. The user wants, for any pool: "I have $X, or 1.2 WETH + 800 USDC, tell me exactly how much WETH and USDC to end up with, how much to swap, what that costs", plus "what would it cost and do to hedge the position".

The app is read-only (no wallet connection, no signing), so the feature is a calculator, not an executor.

Mockup of the final placement: `docs/enter-pool-mock.html`. It shows the Pools table with the range panel on the right and the new dashed "Enter this range" section at its bottom, in the same style as `docs/borrow-coverage-mock.html`. It is interactive with sample data (pools, prices, balances are made up).

## Decisions made with the user

| Topic | Decision |
| --- | --- |
| Placement | **Pools page side panel** (final): a new "Enter this range" section at the bottom of `RangePanel`, under the APR estimate, reusing the panel's pool, range width and capital. No new route or nav item. (Earlier ideas, a dedicated `/enter` page, are dropped.) |
| Inputs | Budget in USD, token amounts I hold, mixed wallet balances |
| Wallet | Manual entry plus balances of the watched wallets (read-only RPC). No wallet connect, no signing |
| Hedge outputs | Delta-neutral size, cost of hedging (funding/borrow vs fee APR), P&L scenarios |

## Assumptions I made (the second round of questions was declined; confirm or change)

1. Swap cost = pool fee tier-style assumption: one "fee + slippage" percent input (default 0.10%) plus gas from existing config. No live quote in v1. A QuoterV2 quote can replace it later behind the same field.
2. Hedge instrument is modelled as a short of the volatile token with a manual funding/borrow rate (% a year) and an opening fee. The same math covers a perp short and an Aave borrow-and-sell. No external funding API in v1; Aave's rate can be prefilled from the Borrow module when the user has a loan.
3. Volatile/stable pools hedge the volatile leg. Volatile/volatile pools (WETH/cbBTC) list one short per leg, to USD. `AssetClassifier` (`Domain/Metrics/PortfolioExposureCalculator.cs`) decides which legs are volatile.
4. Hedge ratio slider 0-100%. Re-hedge cost over time is v2.
5. Native ETH is treated as WETH; a small gas reserve is subtracted from ETH holdings.
6. Spec lives in `docs/enter-pool-spec.md` (root `spec.md` is the built Borrow spec and stays untouched).

## Calculation model (pure domain, C#, xUnit)

Reuse `Domain/Calculations/PositionMath.cs` (`TickToSqrtPrice`, `GetAmountsRaw`, `GetLiquidityRaw`). Add:
- `AmountsPerLiquidity(price, lower, upper)`: x = 1/√P − 1/√Pb, y = √P − √Pa (clamped when the price is outside the range: all token0 below, all token1 above).
- Tick snapping to `TickSpacing` for the chosen bounds.
- `EntryPlanner.Plan(holdings, prices, range, swapCostFraction)`: solves the swap so the amounts match the range ratio. Selling token0: `s = (w0·y − w1·x) / (y + x·P·(1−c))`; selling token1 is the mirror. Returns swap side/amount/proceeds, final amounts a0/a1, liquidity, swap cost, leftover dust.
- `HedgePlanner`: short size = a0 × hedge ratio (delta = token0 amount); cost per day = notional × rate / 365; hedge cost over the holding period (30 days) = carry + open/close trading fee (0.05% each, configurable) × notional, where carry = −funding rate for a perp short (earned when funding is positive) and +borrow rate for borrow-and-sell. For each price move m: position value at P′ (closed form, clamped at the range edges) minus entry value = price effect; short P&L = `short × (P − P′)`; fees = position value × income APR × days / 365 × the share of a straight-line price path (entry price to scenario price) that lies inside the snapped range, so a scenario that exits the range earns fewer days of fees (shown as "n of 30 days in range"). Margin (perp only) = notional / leverage; liquidation price ≈ entry × (1 + 1/leverage − 0.5% maintenance); net APR uses position value plus margin. Edge shorts = token0 amount in the position at the lower bound (all token0) and at the upper bound (zero), times the hedge ratio. Valuation uses the pool price for the ratio/swap and USD reference prices for dollar values; for volatile/volatile pools both legs are valued in USD and each leg gets its own short. `NoHedge = price effect + fees`, `Hedged = price effect + short P&L + fees − hedge cost`. The API returns both series, plus the outcome range (min/max over ±30%) and hedged net APR vs unhedged fee APR.

This is the same swap solver described in `docs/rebalance-simulator.md` section 1; build it once in `EntryPlanner` and let the simulator call it.

## API

- `POST /api/pools/{id}/entry-plan`, body `{ lowerPercent, upperPercent (or tickLower/tickUpper), budgetUsd | holdings[], paysWith, swapCostPct, gasUsd, incomeAprPct, incomeBasis: "fees"|"emissions", hedge { instrument: "perp"|"borrow", ratio, ratePct, leverage, openFeePct } }`. Pure computation, no persisted state, same pattern as `POST /api/pools/{id}/estimate`.
- Response: pool price (slot0) and USD reference prices with their timestamp; snapped ticks, bounds, actual width; amounts a0/a1 and USD value; swap side, amount, proceeds, swap fee cost, gas, swap share of TVL (warning flag above 0.1%); deposit amounts rounded to token decimals plus leftover dust; hedge: short size per leg, notional, carry per day (signed, + = earned), margin, liquidation price and whether it is below the range top, net APR on position plus margin; edge shorts (at lower bound, at upper bound) and the ±5% re-hedge deltas; `scenarios[]` every 2% from -30% to +30% with price, in-range days, fees, price effect, hedge P&L, hedge cost, entry cost, no-hedge net, hedged net (omitted for volatile/volatile pools).
- Pool state gap: `PoolDto` has no token addresses, decimals or slot0. Add a pool-state resolver (extend `PoolExplorerService`, reusing `UniswapV3StyleProvider.GetPoolStateAsync`, currently private) returning tokens, decimals, sqrtPriceX96, tick, and prices from `PriceService.GetCurrentAsync`.
- `GET /api/wallet/balances?owner=&tokens=`: batched `balanceOf` through `IPositionProvider.GetTokenBalancesAsync` plus `eth_getBalance` (new thin wrapper in `EvmRpcClient`). Follows the `owner` query convention (`OwnerParam`).

## Frontend

- New `frontend/src/components/pools/EntryPanel.tsx`, rendered at the end of the panel body in `RangePanel.tsx` (after "How this is estimated", inside the existing rounded box or as its own bordered section). Props: `pool`, `lowerPercent`, `upperPercent` (the panel's bounds), `capital`, `feeApr` (from the estimate already fetched), `emissions`. Pool, range and capital come from the panel, so there is no pool picker or route. Selecting another pool in the table re-targets it automatically.
- Layout, v1 order (see `docs/enter-pool-mock.html`, the source of truth for layout): a prominent "Current price" block at the top of the panel (pool price, e.g. "3,401.10 USDC per WETH", with the USD reference and its age) and a range bar marking the current price between the snapped bounds; range with snapped bounds and a "Requested ±X%, actual …" line; starting point (Capital above / Tokens I hold, wallet picker, paying with); swap fee % and gas USD; plan sentence and swap-size-vs-TVL warning; tiles (need token0, need token1, swap and gas cost); "Deposit this" box with Copy buttons and leftover dust; hedge block (instrument, ratio, rate, leverage for perp, income basis Fees/Emissions and income APR, short-now / carry-per-day / net-APR tiles, margin and liquidation line with warning, short size now / at lower bound / at upper bound with ±5% re-hedge line, with-vs-without comparison, chart and table). Single column sized for the ~500px panel.
- Capital field behaviour: in "Capital above" mode it drives the amounts (scaling both tokens; "paying with" says which token you start from). In "Tokens I hold" mode it is read-only and shows the USD value of the entered balances, so it never looks editable while being ignored.
- Hedge block shows **with vs without hedge** side by side, always visible (hedge ratio 0% = no hedge, no separate on/off). A price-move slider (-30% to +30%, default -10%) feeds a comparison table, one column per choice: fees earned, price effect on the position, hedge profit or loss, hedge cost (funding plus open/close trading fees), net result in USD and % of capital. A verdict line states what the hedge adds or costs at that move and the outcome range across -30% to +30% with and without the hedge. The chart and 5-row table below plot the same net result (fees and hedge cost included), "No hedge" vs "With hedge", with the range bounds marked. The table has three columns: move, No hedge, With hedge. Each With hedge cell shows the net result and, on a smaller second line, only the signed difference versus no hedge (e.g. "+$310"), with no label text and no separate "Hedge adds" column.
- Hedge cost sign convention: a "Hedge with" select picks the instrument. **Perp short**: input is the funding rate; positive funding means longs pay shorts, so the short *earns* it (cost = −rate). **Borrow and sell**: input is the borrow rate; positive means you pay (cost = +rate). The tiles say "you earn" or "you pay" so the sign is never ambiguous, and the funding/borrow row in the comparison is signed from your point of view (a negative result is a cost). Trading fees are always a cost. Net APR is shown hedged next to the unhedged fee APR.
- Hook `useEntryPlan` in `api/client.ts` (debounced via `useDebounced`, keeps previous data) mirrors `usePoolEstimate`.
- Range bounds are editable independently (v1, user decision): each bound card has a percent input (lower = % below the current price, upper = % above) with − / + steppers (0.5 step). The presets and the ±% slider set both bounds together; editing either bound by hand makes the range "custom" (presets deselect, slider label reads "custom, -5% / +20%"). Inputs are clamped (lower 0.1-99%, upper 0.1-500%), and the snapped bound and its actual % are shown under each input. A skewed range changes the required token mix (e.g. -5% / +20% needs more WETH), which the plan, hedge and scenarios already follow. This needs `RangePanel` to hold `lowerPercent` and `upperPercent` instead of one `width` (its existing `BoundCard` steppers already move bounds together; they should move one bound at a time, and `usePoolEstimate` must accept two widths or keep the APR estimate on the symmetric average). One-sided "above/below price" ranges (bound at 0%) stay out of scope.
- Persist hedge/funding/swap-cost inputs with `lib/usePersistentState.ts`; wallet prefill from watched wallets; the panel stays collapsible on small screens (existing `Close` button behaviour).
- Chart: Recharts, `lib/chartTheme.ts`. The scenario slider reads from the series the API returns (see API), so moving it does not refetch.
- Use `ui.tsx` primitives (`Panel`, `Segmented`, `MiniStat`), `lib/format.ts` / `lib/pools.ts` formatters, and respect privacy mode (`lib/privacy.ts` `maskedBalance`).

## Limits and honesty (shown in the UI)

- Estimates, not quotes. Price impact is an input, not read from pool depth.
- A static hedge drifts as price moves in range; scenarios use a hedge sized at entry.
- Funding rates change; the rate is the user's input.
- Staked Slipstream positions earn emissions instead of fees; net APR uses whichever number the Pools panel shows.

## Critical files

- Edit: `frontend/src/components/pools/RangePanel.tsx` (render `EntryPanel`), `frontend/src/api/client.ts`, `frontend/src/api/types.ts`, `backend/src/LpDashboard.Application/Pools/PoolExplorerService.cs`, `backend/src/LpDashboard.Infrastructure/Evm/EvmRpcClient.cs`, `backend/src/LpDashboard.Domain/Calculations/PositionMath.cs`
- New: `backend/src/LpDashboard.Domain/Metrics/EntryPlanner.cs` and `HedgePlanner.cs`, tests in `backend/tests/LpDashboard.Domain.Tests/`, controller action next to the pools estimate endpoint, `frontend/src/components/pools/EntryPanel.tsx`, `docs/enter-pool-spec.md`, `docs/enter-pool-mock.html`

## Build order

1. Mockup and spec are saved in `docs/` (done).
2. Domain `EntryPlanner` + `HedgePlanner` with tests (round-trip through `GetLiquidityRaw`/`GetAmountsRaw`, one-sided ranges, price outside range, swap direction both ways).
3. Pool-state resolver + `POST /entry-plan`.
4. `EntryPanel` in the Pools side panel: starting amounts, plan and costs.
5. Wallet balance endpoint and prefill.
6. Hedge block and scenarios.

## Verification

- `cd backend; dotnet test LpDashboard.slnx` (new planner tests; use `--artifacts-path` if the API is running).
- `cd frontend; npm run build`.
- In the UI: pick WETH/USDC, set a range, enter $5,000; check a0·P + a1 equals the value after swap costs, ratio matches the range, and setting the price outside the range gives one token only. Compare the needed amounts against the Aerodrome deposit screen for the same ticks.
- Hedged scenario P&L is near zero at small price moves and the net APR drops by funding × notional ÷ value.

## Review enhancements: items 1-10 are IN v1 (user decision); 11-14 are engineering notes

All ten are reflected in the mockup (`docs/enter-pool-mock.html`). Decision for item 4: volatile/volatile pools get the plan, per-leg short sizes and margin, but no price-scenario comparison in v1 (shown as a note in the UI); stable-quote pools get the full comparison.

Correctness:
1. **Fees leave when the price leaves the range.** The mockup earns full 30-day fees at every scenario, which overstates the +/-30% cases. Model a straight-line price path to the scenario price and scale fees by the share of the 30 days spent in range (reuse `AprEstimator.TimeInRange`). Show "in range X of 30 days" per scenario.
2. **Use the pool's own price for the swap, DefiLlama only for USD.** Ratio and swap rate come from slot0 (`sqrtPriceX96`) and decimals; USD prices value the result. A mismatch between the two otherwise skews the plan. Show "price as of" time.
3. **Show the snapped result, not the requested one.** Bounds snap to `TickSpacing`; amounts are computed from the snapped ticks, and the panel shows the actual lower/upper price and actual width.
4. **Volatile/volatile pools (WETH/cbBTC):** the valuation must use both USD prices, not assume a $1 quote. v1 hedge for these pools lists one short per leg; if that is too much, v1 hedges stable-quote pools only and shows the plan without a hedge for the rest.
5. **Include gas** (swap plus mint, plus hedge open) from the existing median gas data (`PositionEvent.GasUsd`) in the swap-cost tile; the current mockup omits it.
6. **Staked Slipstream positions earn emissions, not fees.** Give the earnings basis a "Fees / Emissions (staked)" choice so hedged net APR uses the right income.

Decision value:
7. **Margin and liquidation for a perp short.** Add "leverage" (default 3x): margin needed, liquidation price, and net APR measured on position value plus margin. A hedge that gets liquidated at the upper range edge is the main way this fails.
8. **Hedge size at the range edges.** A static hedge drifts; show the short size needed at the lower and upper bounds and the re-hedge trigger (about 5% move) so the user knows what they will have to adjust.
9. **Swap size warning:** flag when the swap is more than about 0.1% of the pool's TVL (price impact matters), using TVL already on `PoolDto`.
10. **Copy buttons and a "deposit this" summary** with the exact two token amounts and leftover dust, so the numbers can be typed straight into the Aerodrome/Uniswap deposit screen.

Engineering:
11. API returns the full scenario series (every 2%, -30% to +30%) in one response; the slider and table only index into it (no refetch per slider move).
12. Wallet balances endpoint accepts only addresses already watched by the account (reuse `OwnerParam` validation), so it cannot be used to burn RPC quota (see HANDOVER item 7, public hosting). With "All wallets" selected, ask which wallet to prefill from.
13. Extra verification: invariants (value after swap = value before - cost; ratio 0% hedge equals no hedge; 100% hedge scenario slope is about 0 near entry; perp funding sign test) and a golden test that feeds an existing position's real deposit amounts into `EntryPlanner` and checks the liquidity against the on-chain value within rounding.
14. v2 candidates, not in v1: skewed/one-sided ranges ("above price"), swapping from a third token the pool does not contain, live QuoterV2 quote, public funding-rate feed, top-up of an existing position.

## Open questions

- Confirm assumptions 1-6 above (especially hedge model and the spec path).
- Prefill funding from a public API (Binance/Hyperliquid) in v2, or keep it manual?
- Should the planner also accept an existing position to top up (same range)?
