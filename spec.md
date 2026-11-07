# Borrow coverage: do my LP earnings pay the interest on my loans?

Status: spec, not built. Produced from an interview with the owner; decisions are recorded as decisions, guesses as **Assumptions** (section 13).

## Build status (2026-11-07)

Built: domain calculators (cost, manual accrual, coverage, collateral trend and change split, targets) with tests; manual loans (entities, two migrations, API); Aave v3 on Base (live account, reserves, rates, health factor; daily history through an archive-capable RPC, up to 120 days per sync); Borrow page and the Earnings card; sync hook after the LP sync.

Checked: all backend tests pass; the frontend builds; the pages render with a manual loan and with a real Aave account read live from the public Base RPC.

Checked against a real chain (2026-11-07, Alchemy archive RPC on Base): the Aave **daily history**. The first version estimated each day's block from the recent block rate, which broke when Base's block time changed (every older day resolved to the latest block, so interest and supply yield read zero). Blocks are now found by searching block timestamps (`BlockLocator`), and a rebuilt history shows rising indices and about $12 to $14 of interest a day. Still worth comparing one past month with Aave's interface.

Differences from this document:
- Coverage, break-even, cost, collateral and what-if are as specified. Warnings are built in the browser from structured data (so hidden balances stay hidden), not returned by the server.
- Historical Aave health factor is not stored; the health factor shown is the live one (A9 is moot until a snapshot history is added).
- The change split's "Borrowed or repaid" row also shows when a manual loan's total debt is restated by a new terms row (A10).
- When history is incomplete, days before the first stored one show that first balance (flat) instead of zero, with a note on the page.

## 1. Problem

The owner supplies LP-related tokens as collateral on **Aave v3 (Base)**, borrows against them, and also has a **Binance** loan. The loans cost interest every day. The LP positions in the same wallet earn fees and (for staked Aerodrome positions) AERO emissions. The owner cannot currently tell:

1. Is LP income larger than the interest I pay? By how much?
2. If it is not, how big is the shortfall and what would fix it?
3. How close am I to liquidation while I wait for the LP to pay it off?

## 2. Goal and non-goals

Goal: one answer, visible on the Earnings page, backed by a Borrow page that shows where the number comes from and what to do about it.

Non-goals (v1):

- Executing anything (the app stays read-only: no repay, borrow or supply).
- Chains other than Base, Aave versions other than v3, Aave stable-rate debt (disabled on Base).
- Binance data from the Binance API. Decision: **manual only**. Binance's loan endpoints need a signed API key and the app stays free of exchange credentials. Revisit later as an opt-in with a read-only key.
- Per-loan or per-position linking: **all LP income is pooled against all debt** (decision).
- Telegram alerts for borrow events (decision: in-app warnings only).
- Predicting future prices or rates. What-if is a sensitivity tool, not a forecast.

## 3. Decisions from the interview

| Topic | Decision |
| --- | --- |
| Lending sources | Aave v3 on Base (read from chain) + manual loans (Binance) |
| Data entry | Chain read for Aave, **manual override** allowed (correct or add off-chain loans) |
| Relationship LP to loan | Collateral is the LP's token; income and debt are pooled, not linked |
| "Pays off" basis | **Accrued** (earned, claimed or not), not claimed-only |
| Headline | **Coverage ratio, trailing 30 days**, with 7d / 90d toggle |
| Ratio formula | `(LP fees + LP rewards + Aave supply yield) / borrow interest`. Net USD additionally subtracts gas |
| Aave history | **Backfill from loan start** |
| Wallets | Aave account and LP positions are the same wallet (watched owners) |
| UI placement | New **Borrow** page + **headline card on Earnings** |
| Management tools | Health-factor warning, break-even / what-if, repay-vs-keep suggestion |
| Binance risk | Manual collateral + margin-call/liquidation LTV inputs, computed LTV gauge |
| Warnings | In-app only (banners and badges) |
| Binance terms | Principal + APR + start date, **with rate history** |
| Binance data | **Manual only.** No Binance API key is stored; the app stays free of exchange credentials |
| Cost view | Show what each loan costs **per day, week and month**, grouped by wallet, beside LP income at the same pace |
| Collateral and debt trend | Chart collateral and debt over time with collateral ÷ debt and LTV, split every change into its cause, and offer a target calculator. The suggestions (change split, steady-state hints, target calculator) are **proposed by Claude and not yet confirmed by the owner** |

## 4. Definitions

All amounts in USD, priced at accrual time (same rule the Earnings page uses: diff token amounts first, price afterwards).

- **LP fees** and **LP rewards**: `DailyEarnings.FeesUsd` and `RewardsUsd` from `EarningsCalculator`, summed over the window. Accrued basis, so unclaimed fees and pending AERO count. Closed positions still count for the days they earned.
- **Borrow interest**: interest that accrued on debt in the window, from Aave and from manual loans (section 5). Never includes principal movements.
- **Supply yield**: interest that accrued on Aave aToken balances (collateral) in the window. Principal movements excluded.
- **Gas**: sum of `PositionEvent.GasUsd` for LP events (claims, rebalances, adds/removes) of the owner's positions inside the window. Aave and Binance transaction costs are not included in v1.
- **LP income** = fees + rewards + supply yield.
- **Coverage ratio** = LP income / borrow interest. `null` ("no interest in window") when interest is 0.
- **Net carry (USD)** = LP income - borrow interest - gas.
- **Status**: `Covered` ratio >= 1.25, `Thin` 1.0 to 1.25, `Shortfall` < 1.0, `NoDebt` when interest is 0. Cut-offs are constants in one place so they are easy to change.
- **Window**: the last N local days (N = 7, 30, 90), today included as a partial day, bucketed with the same local time zone `EarningsCalculator` already uses, so LP and loan figures cover the same days.

Run-rate view (forward looking, shown beside the trailing number, labelled as such):

- **Interest run-rate per day** = sum over loans of `debtUsd * borrowApr / 365` at current balances and rates.
- **Income run-rate per day** = trailing 7-day LP income / 7 (fees + rewards + supply yield).
- **Cost of a loan** (what you pay, forward looking): `perYear = debtUsd * APR`, `perDay = perYear / 365`, `perWeek = 7 * perDay`, `perMonth = perYear / 12`. `debtUsd` is today's debt at today's price (Aave: debt-token balance including accrued interest; manual: latest terms row, priced live). `APR` is the current Aave variable borrow rate, or the latest manual terms row. Closed loans are excluded. Costs are grouped by the wallet the loan belongs to, each group with a subtotal, plus a grand total.
- **Income at the same pace** = the income run-rate above, scaled to the same periods (`perDay`, `* 7`, `* 365 / 12`). **Left over** = income at pace minus total cost per period, before gas.
- **Collateral USD** (at a day's end) = sum of supplied-token balance times price over reserves that count as collateral that day (Aave), or collateral amount in force times price (manual). **Debt USD** = debt-token balance times price (Aave), or principal in force plus simple interest accrued since that row (manual).
- **Collateral ÷ debt** = collateral USD / debt USD (higher is safer). **LTV** = debt USD / collateral USD. Both are shown for all loans together, or for one loan.
- **Change split** over a window from day `s` to day `e`: collateral change = price moves (`balance_(d-1) * (price_d - price_(d-1))`) + deposits and withdrawals (net tokens times `price_d`) + supply yield (yield tokens times `price_d`), each summed day by day over the window; debt change = interest added + borrowing or repayment (+ price move of a non-stable debt asset). Chaining day by day values every deposit at the price of its day and still makes the parts add up to the total exactly.
- **Break-even LP income** = interest run-rate per day. **Required LP APR** = interest run-rate per year / current open-position value.

## 5. Data and calculation

### 5.1 Aave v3 on Base (on-chain)

New provider next to `UniswapV3StyleProvider`, behind a new abstraction (`ILendingProvider`), using the existing `EvmRpcClient`, rate limiter and `SyncCursor`.

Live reads each sync (latest block):

- `Pool.getUserAccountData(user)`: total collateral, total debt (USD, 8 decimals), liquidation threshold, LTV, **health factor**.
- Per reserve the user touches: aToken balance (collateral/supplied), variable debt token balance (borrowed), `currentLiquidityRate` (supply APR) and `currentVariableBorrowRate` (borrow APR) from `getReserveData`. Rates are in ray (1e27) and are APRs; display APY too if useful.
- Pool and data-provider addresses are resolved via the chain's `PoolAddressesProvider`, which is configured (never hard-coded in code). **Verify the Base address from Aave's address book before first use.**

Daily accrual (the backfill and the ongoing job use the same code):

1. For each UTC day boundary since the first Aave interaction, find the block (the app already maps timestamps to blocks for events) and read aToken and debt-token `balanceOf(user)` at that block (needs archive state; Alchemy provides it).
2. Collect Aave `Supply`, `Withdraw`, `Borrow`, `Repay`, `LiquidationCall` logs for the user (indexed topics) to know principal flows per interval.
3. Per reserve per day:
   `interest = balance_end - balance_start - borrowed + repaid`
   `supplyYield = aBalance_end - aBalance_start - supplied + withdrawn`
   (liquidation treated as repay of debt and withdraw of collateral). Clamp tiny negatives from rounding to 0.
4. Price each reserve with the stored hourly `TokenPrice` series (key `base:<token address>`); flag `HasMissingPrices` instead of guessing, same as Earnings.
5. Store the end-of-day balance (tokens) of each supplied and borrowed reserve, whether it counted as collateral that day, and the net deposit and borrow tokens of the day. These rows drive the collateral and debt history. Historical health factor is approximated with each reserve's current liquidation threshold; live snapshots store the chain's real value from now on.

Backfill starts at the user's first Aave event (first log found), not at a fixed lookback. The job is resumable via `SyncCursor` (`Kind = AaveAccrual`, last completed UTC day) and is chunked so it respects `MaxCallsPerSecond`. Progress is reported through the existing sync-progress UI. If the RPC plan cannot serve archive reads or wide log ranges, the app says so and falls back to "from today forward" for that wallet instead of failing silently.

Manual override: the user can add a manual loan for off-chain debt, and can **hide** an Aave reserve or override a day's interest if the chain read is wrong (stored as an adjustment row, shown with an "edited" mark; never silently).

### 5.2 Manual loans (Binance)

A manual loan has:

- Label, platform (default "Binance"), the **wallet it belongs to** (picked from the followed wallets; it groups the cost view and fits the privacy model), borrowed asset (symbol + price key), optional closed date.
- **Terms history**: rows of `{effectiveFrom, principal (token units), aprPercent}`. A rate change, a top-up or a partial repayment is just a new row. This covers "rate history" without a transaction ledger.
- Collateral (for risk and for the collateral ÷ debt history): asset, a **collateral amount history** (rows of `{effectiveFrom, amount}`; add a row on every top-up or withdrawal), plus margin-call LTV (optional) and liquidation LTV.
- Debt over time = the principal of the terms row in force plus simple interest accrued since that row's date. Enter the total debt Binance shows whenever you add a row so the line matches Binance.

Interest accrues daily as **simple interest**: `principal * apr / 365` per day, with the principal and APR in force that day, priced at that day's price. (Assumption A1: Binance flexible loans accrue hourly and do not compound.)

Price keys: a picker of common assets mapped to DefiLlama/CoinGecko keys (BTC, ETH, BNB, USDT, USDC, ...) plus a free-text key for others. Missing price shows a warning, not a zero.

### 5.3 Risk

- Aave: use the chain's **health factor** as is. Warning levels default to **< 1.5 warn, < 1.2 critical**.
- Manual loan: `LTV = debtUsd / collateralUsd` from live prices; `buffer = liquidationLtv / LTV` shown as a health-style gauge; warn when LTV is within 15% of liquidation LTV, critical within 5%. If margin-call LTV is entered it is shown as an extra marker.
- Show also **collateral price drop to liquidation** (%), the number most useful to the owner. For Aave this is derived from the volatile collateral's share of total collateral and assumes only collateral moves (stated in the UI).
- Thresholds are app-wide settings with the defaults above (per-loan thresholds were not requested).

## 6. Management features

1. **What you pay**: for every loan, the interest cost per day, week, month and year, grouped by wallet with a subtotal per wallet and a grand total. Below it, LP income at the same pace and what is left over. Answers "how many dollars of interest do I owe each period" without any history. Aave rates float, so the numbers move with the rate.
2. **Coverage trend**: daily bars of LP income vs interest plus the cumulative net-carry line, for the chosen window. Shows whether the trend is improving.
3. **Break-even**: the three numbers from section 4 (required income per day, required LP APR, current LP APR), with one sentence: "You need $X/day to cover interest; LP is making $Y/day."
4. **What-if** (client-side sliders over a server-computed baseline, nothing persisted):
   - Borrow rate: -50% .. +100% of current.
   - LP income: -80% .. +100% of trailing level.
   - Collateral price move: -60% .. +30%.
   - Outputs: coverage ratio, net carry per day, health factor / LTV gauge. A collateral move changes HF and LTV only; LP income is not re-derived from price (stated in the UI).
5. **Repay vs keep** (suggestion text plus numbers, no execution):
   - Surplus case (coverage >= 1): "Net carry over the window is $S. Using it to repay the highest-APR loan (`<label>` at `a`%) would save about `S * a` per year."
   - Shortfall case (coverage < 1): "To reach 1.0x at current income, interest must fall by $F/year. Repaying about `F / a` of `<label>` does that." Loans are ranked by APR, highest first.
   - If there are warnings (HF/LTV low), repayment is also shown as the lever that raises health factor, using the same numbers.
6. **Warnings** (in-app banners and badges on Earnings, Borrow and the header): coverage `Shortfall`, health factor / LTV warn and critical, stale data (last Aave sync older than N intervals), missing prices, missing RPC archive support. No notifications are sent outside the app.
7. **Collateral and debt over time**: a chart of collateral USD and debt USD (7/30/90 days; all loans, Aave only or Binance only) with markers for deposits, withdrawals, borrows and repays, and a collateral ÷ debt line below it. For a single loan the ratio chart also draws the warn line and the liquidation line. Tiles show collateral, debt and ratio now, each with its change over the window and an up or down arrow (green when it moved the safe way). Each loan row shows its own ratio and change.
8. **What changed**: splits the change in collateral and in debt over the window into causes (section 4, change split) and names the biggest driver in one sentence. Answers "did the ratio get worse because of the market or because of me".
9. **Keep it steady**: (a) how much interest adds to debt per month and whether LP income covers it; (b) the dollars per month needed to hold collateral ÷ debt where it is when prices are flat: repay the month's interest, or add collateral worth `ratio * interest` (shown in dollars, not as a ratio change); (c) a **target calculator**: enter a target health factor (Aave) or target LTV (Binance) and see how much to repay or how much collateral to add, with the repay amount shown in months of left-over LP income. It never executes anything. The dollar figures in (a) and (b) have a Day / Week / Month toggle (default Month); the **What you pay** table already shows all periods side by side.

## 7. UI

Per the project convention, the Borrow page and the Earnings card get an HTML mock published as an artifact and approved **before** any frontend code is written.

### Earnings page: "Interest coverage" headline card

- Big number: `1.8x` coverage ratio (30d default, 7d / 90d toggle), status chip (Covered / Thin / Shortfall).
- Under it: `+ $412 net carry` and `LP income $1,030 vs interest $570`.
- A cost line: `Interest costs $3.77 a day, $115 a month`.
- A health badge (worst of Aave HF and manual LTV), a line `Collateral ÷ debt 2.04× ▼ 0.11 in 30d`, and a link "Open Borrow".
- Hidden entirely when there are no loans; shows an "Add a loan" call to action instead of zeros.

### Borrow page (`/borrow`, nav item next to Earnings)

1. Banner area for warnings.
2. Summary row: coverage ratio, net carry, interest, LP income split (fees, rewards, supply yield), gas.
   Directly below it, the **What you pay** table: wallet group rows (with subtotals), loan rows with debt, APR and cost per day / week / month / year, then total, LP income at the same pace, and left over (green or red).
3. Run-rate panel: interest/day vs income/day, required LP APR.
4. Chart: daily LP income vs interest, cumulative net carry.
5. Loans table: source (Aave / Binance), asset, debt, APR, interest in window, collateral ÷ debt with its change over the window, risk gauge. Aave rows are read-only apart from hide/adjust; manual rows have edit/delete. "Add manual loan" opens a form with the terms-history editor.
6. **Collateral and debt over time** card (selector All / Aave / Binance), then **What changed** and **Keep it steady** side by side.
7. Risk panel: Aave health factor trend and liquidation distance; manual LTV gauges.
8. What-if panel and Repay-vs-keep panel.

Respect existing privacy mode, price-invert toggle and wallet selection. Loading, empty, error and "backfill in progress (day X of Y)" states are specified in the mock.

## 8. API (all require `owner`, like the rest of the app)

```
GET    /api/borrow/summary?owner=&days=30   -> BorrowSummaryDto
GET    /api/borrow/daily?owner=&days=90      -> DailyCoverageDto[]   (income, interest, gas, net per day)
GET    /api/borrow/loans?owner=              -> LoanDto[]            (Aave + manual, with risk and cost per day/week/month/year, grouped by wallet)
GET    /api/borrow/risk/history?owner=&days= -> RiskPointDto[]       (HF / LTV over time, from snapshots)
GET    /api/borrow/collateral-history?owner=&days=&loan=  -> CollateralPointDto[]  (collateral USD, debt USD, ratio, LTV per day, plus deposit/borrow events)
GET    /api/borrow/changes?owner=&days=&loan=             -> ChangesDto            (start, price, flows, yield, interest, end; for collateral and debt)
POST   /api/borrow/loans/manual              -> create manual loan
PUT    /api/borrow/loans/manual/{id}         -> edit loan and its terms history
DELETE /api/borrow/loans/manual/{id}
POST   /api/borrow/loans/aave/adjust         -> hide reserve / override a day
```

`BorrowSummaryDto`: `window`, `coverage {ratio, status, lpFeesUsd, lpRewardsUsd, supplyYieldUsd, interestUsd, gasUsd, netCarryUsd}`, `runRate {interestPerDayUsd, incomePerDayUsd, requiredLpApr, currentLpApr}`, `costs {wallets[{owner, loans[{id, label, debtUsd, aprPercent, perDayUsd, perWeekUsd, perMonthUsd, perYearUsd}], subtotal}], total, incomeAtPace, leftOver}`, `risk {worstKind, worstValue, collateralDropToLiquidationPct}`, `repay {mode, amountUsd, loanLabel, savingPerYearUsd}`, `warnings[]`, `dataQuality {missingPriceDays, backfill {done,total}, stale}`.

What-if runs in the browser from `loans` + `summary` (no endpoint), so sliders are instant.

## 9. Backend design

- **Domain** (pure, unit-tested): `Metrics/LoanCostCalculator` (per-loan and per-wallet cost per day, week, month, year), `Metrics/CollateralTrendCalculator` (daily collateral, debt, ratio, LTV, the change split and the target calculator), `Metrics/BorrowCoverageCalculator` (ratio, net carry, status, run-rate, break-even, repay suggestion), `Metrics/ManualLoanAccrual` (terms history to daily interest), `Metrics/AaveAccrualCalculator` (balances + flows to daily interest/yield), `Metrics/LoanRiskCalculator` (HF, LTV, buffer, collateral drop to liquidation).
- **Entities**: `ManualLoan`, `ManualLoanTerm`, `ManualLoanCollateral`, `LendingAccrualDay` (owner, source, reserve/loan, date, debt tokens, interest tokens/USD, supply-yield tokens/USD, end-of-day supplied and borrowed tokens, collateral-enabled flag, net deposit and borrow tokens, missing-price flag, edited flag), `LendingSnapshot` (owner, timestamp, HF, collateral USD, debt USD, per-reserve borrow/supply APR), `LendingAdjustment`. One EF migration.
- **Application**: `Lending/LendingSyncService` (called by `SyncCoordinator` after position sync), `Queries/BorrowQueryService`. Coverage joins `LendingAccrualDay` with existing `EarningsCalculator` output and LP gas, so the ratio never has a second definition of LP income.
- **Infrastructure**: `Evm/AaveV3Provider`, ABI additions in `Abi.cs`, config under `Evm:Chains:0:Lending:Aave {PoolAddressesProvider, StartBlock?}`.
- Snapshot cadence follows `Sync:SnapshotIntervalMinutes`.

## 10. Acceptance criteria

1. With an Aave wallet and one manual loan, the Earnings card shows a ratio and net carry for 7/30/90 days; numbers equal the Borrow page's.
2. Aave interest for a past month matches Aave's own interface (or the wallet's debt-token balance change minus flows) within 1%.
3. A manual loan with two rate rows accrues the old rate before the change date and the new rate after, to the day.
4. Interest 0 shows "no interest", never infinity or NaN; missing prices show a warning and are excluded, not zero-priced.
5. Health factor below 1.5 / 1.2 and manual LTV near liquidation show warn / critical banners; nothing is sent externally.
6. What-if at 0% / 0% / 0% equals the live numbers; a -30% collateral move lowers HF proportionally.
7. First add of a wallet with Aave history shows backfill progress and finishes without exceeding the configured RPC rate; restarting mid-backfill resumes.
8. No Aave and no manual loans: no card, no errors, Borrow page shows the empty state.
9. A manual loan of $6,000 at 9.2% shows $1.51 a day, $10.59 a week and $46.00 a month; loans group under their wallet and wallet subtotals add up to the grand total. A closed loan disappears from the cost view.
10. Over any window, the collateral change equals price moves plus deposits/withdrawals plus supply yield, and the debt change equals interest plus borrowing/repayment (plus debt-asset price), each summing to end minus start to the cent. Chart markers line up with the real transactions.
11. Target calculator: Aave debt $14,200, collateral $32,800, health factor 1.74, target 2.0 gives repay about $1,851 or add about $4,916 of collateral. A target already met shows "already at or above target".

## 11. Testing

- Domain unit tests: cost per day/week/month/year and wallet subtotals, change split adds up exactly, ratio and LTV, target calculator, coverage and status boundaries, zero-interest and zero-income cases, gas subtraction, run-rate, repay suggestion both branches, manual accrual across rate changes and closed date, Aave accrual with synthetic balances and flows (borrow and repay inside an interval do not count as interest), HF/LTV math, what-if identity.
- Provider test with a fake RPC for the accrual job (resume from cursor, missing archive fallback), in the style of `SyncServiceTests`.
- Manual validation against Aave's UI and a Binance statement for one real month before declaring done.

## 12. Build order

1. Domain calculators + tests, manual loans (entities, migration, endpoints, accrual).
2. Cost-per-loan endpoint and the "What you pay" panel (answers the first question with manual loans alone), then the coverage summary joining LP earnings and the Earnings card.
3. Aave live reads (HF, balances, APRs) and risk panel.
4. Aave backfill and daily accrual, progress reporting, adjustments.
5. Collateral and debt history (needs the stored end-of-day balances from step 4), the change split and the target calculator; then charts, what-if, repay-vs-keep, warnings.
6. Mock approval is a gate before step 2's UI and step 5 (see section 7).

## 13. Assumptions and open items to confirm

- **A1** Binance interest is simple, accrued daily from the entered principal and APR (no compounding, no hourly granularity).
- **A2** Supply yield counts for every supplied Aave reserve, not only collateral-enabled ones.
- **A3** Gas covers LP-side transactions only; Aave and Binance transaction costs are excluded.
- **A4** Cut-offs: Covered 1.25x, Shortfall 1.0x; HF 1.5 / 1.2; manual LTV 15% / 5% from liquidation. Adjust after seeing real numbers.
- **A5** AERO emissions are priced at accrual time, so a falling AERO price lowers "income" retroactively only through the price used each day (as on the Earnings page today).
- **A6** The Base `PoolAddressesProvider` address and the RPC plan's archive/log limits are verified at implementation time; failure degrades to forward-only history with a visible notice.
- **A7** One shared set of thresholds, stored app-wide, not per loan.
- **A8** Cost per week is 7 days of daily cost and per month is one twelfth of the year. The cost view uses simple APR, so Aave's compounding makes the real figure slightly higher.
- **A9** Historical Aave health factor uses each reserve's current liquidation threshold (governance changes are rare); the live value comes from the chain.
- **A10** Manual debt over time is the entered principal plus simple interest since that row, so rows should carry the total debt Binance shows.
- **A11** The change split uses end-of-day balances and end-of-day prices, so intraday price moves between a deposit and the day's close land in the price line.
- Open: should the header show a global coverage chip next to the price ticker? (Not selected; left out.)
- Open: later, optional per-position income linking if the pooled view proves too coarse.
