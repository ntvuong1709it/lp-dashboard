# Smart-LP watcher: who earns in this pool, and how do they set their range?

Status: spec, not built (2026-11-11). Produced from an interview with the owner. Decisions are recorded as decisions; guesses are under **Assumptions** and were not individually confirmed. Companion spec: [risk-screen-spec.md](risk-screen-spec.md) (it reuses the pool LP index defined here for its `LP_CONCENTRATION` check).

Implementation notes for whoever builds it:
- Follow the repo conventions in `PLAN.md` / `README.md`: maths in the C# Domain layer with xUnit tests (no frontend test runner), RPC behind `IPositionProvider`, API in `Api/Controllers`, UI in `frontend/src/`.
- The user pushes and opens PRs themselves; commit only when asked. Build in the order under "Build order" and **stop after each step** so the user can verify.
- Never read, print or commit `.env` or any key. Followed-wallet addresses and labels are stored only in the app's own database and are never sent to any outside service.
- The root `spec.md` is the built Borrow spec and stays untouched.

## 1. Problem

Choosing a range is the main LP decision, and the owner has only estimates for it (the APR estimator and the Enter planner). The chain already holds a real-world answer: the wallets that have been earning in a pool, with their ranges, sizes, holding times and rebalances. Today the app can only show wallets the owner types in, and there is no way to find the good ones.

## 2. Goal and non-goals

Goal: for a pool, list the wallets that LP there, rank them by how well they did, show the typical range of the best ones next to the Enter planner, let the owner follow a wallet and drill into its positions, and alert when a followed wallet moves.

Non-goals (v1):
- Copy trading, or any execution. The app stays read-only.
- Detecting that several addresses belong to one entity (no clustering).
- LPs that never hold an NFT position: vaults and managers that call the pool directly (Gamma, Arrakis-style) mint under their own contract and are invisible to a position-manager scan. Contract wallets that do hold NFTs are labelled (see section 6).
- Hedged performance. A wallet's off-chain hedges (perps) are invisible, so its LP-only result understates or overstates its real result. The UI says so.
- Chains other than Base, and protocols other than Aerodrome Slipstream and Uniswap v3.
- Following wallets by social handle, ENS lookup, or any identity linking.
- A single composite "smart score". Columns are sortable and the metrics are shown, not blended.

## 3. Decisions from the interview

| Topic | Decision |
| --- | --- |
| Spec location | `docs/smart-lp-spec.md` and `docs/risk-screen-spec.md`; root `spec.md` untouched. |
| Discovery | **On-demand log scan** over a bounded window (default 30 days) using the owner's RPC and the existing Uniswap-v3-style provider; results cached. No third-party indexer (a public Slipstream subgraph was searched for and not found). |
| Scope | **Aerodrome Slipstream and Uniswap v3 on Base**, any pool in the Pools explorer. |
| Ranking metrics | **All four**: return vs holding (annualized), fee and reward yield on capital, time in range, consistency across positions. |
| v1 outputs | **All four**: pool leaderboard; typical range of the top wallets; wallet drill-down; follow and unfollow. |
| Followed wallets | A **`followed` flag** on the existing watched-address record: excluded from the owner's totals, Positions, Earnings and Borrow; synced once a day instead of every sync cycle. |
| Alerts | **Followed wallet moves** by Telegram (see section 8 for how this fits a daily sync). |

## 4. How discovery works (the technical core)

Position-manager events carry a `tokenId` but not the pool, and pool events carry the range but name the position manager as the owner. So a pool's LP wallets are found by joining the two:

1. **Pool log scan.** `eth_getLogs` filtered by the **pool address** (cheap, selective) for `Mint` and `Burn` over the window. Each log has a transaction hash and the tick range.
2. **Per-transaction resolution.** For each distinct transaction, fetch the receipt (batched; the provider already does this in `GetLogsFromOwnerTransactionsAsync`) and read the position manager's `IncreaseLiquidity` / `DecreaseLiquidity` logs in it. Each gives a `tokenId`. Confirm the pool with one `positions(tokenId)` read, cached per token id (a transaction can touch several pools).
3. **Holder.** The NFT's current holder is `ownerOf(tokenId)`. For Slipstream, an NFT held by a gauge is staked: the holder is the staker, read from the NFT's `Transfer` history using the existing `StakePeriod` logic (which already handles positions minted and staked in one transaction, see `MediatedStakeTests`). Burned positions use the last holder from `Transfer` logs.
4. **Result**: a `PoolPositionRef` per token id (pool, holder, first block, staked or not, gauge, range, current liquidity, last activity block). This is only a discovery list; performance is computed afterwards by the existing pipeline (section 5).

Scans are **incremental**: each pool remembers the last block scanned, and the next scan starts after it. The first scan covers `SmartLp:WindowDays` (default 30, max 180).

**Coverage honesty.** A position that had no event in the window is not found. The scan therefore reports `coverage = discovered liquidity in range / pool.liquidity()` at the current tick. The UI shows it ("found 84% of the liquidity active now") and the risk screen's `LP_CONCENTRATION` is marked partial when coverage is below 90%.

**Cost control.** One scan at a time; 6-hour cooldown per pool (a manual "rescan" respects it unless the owner overrides); a scan that would need more `eth_getLogs` requests than the existing guard allows is stopped with the same message the provider already gives; progress is shown like the sync progress bar. This matters because the app can be hosted publicly with one RPC key (HANDOVER item 7).

## 5. Scoring

Scoring reuses the position pipeline rather than a second implementation of the maths.

- **Stage 1, from the index only** (cheap, every wallet): number of positions, closed positions, first and last activity, range widths, deposits and withdrawals in USD at event time (from `PositionEvent` amounts and `PriceService`), holding times, JIT flag. Enough to apply the thresholds and to pick candidates.
- **Stage 2, full metrics for the top N candidates** by peak capital (`SmartLp:MaxScored`, default 40): run the sync for the known token ids of that wallet in this pool (events, current state, unclaimed fees and rewards) and store them as ordinary `Position` and `PositionEvent` rows under that wallet address. Rows of wallets that are not watched are already hidden from the owner's views (same behaviour as the orphaned data of removed wallets), so they cannot leak into the owner's totals. Then compute with the existing calculators (`PerformanceCalculator`, `EarningsCalculator`, `TimeInRangeCalculator`, `PositionHistory` for estimates).
  - The current sync path starts with `DiscoverAsync(owner)`; scoring needs a path that takes the known `DiscoveredPosition` list and skips discovery (see open questions).

### Metrics per wallet, for this pool, over the wallet's positions in it

| Metric | Definition |
| --- | --- |
| Return vs holding, annualized | (current value + claimed + unclaimed fees and rewards in USD) minus (deposits net of withdrawals), compared with holding the deposited tokens at current prices (the app's existing P/L vs HODL), annualized by the capital-weighted average holding time. Shown as "n/a" when the average holding time is under 7 days: annualizing a few days gives meaningless numbers. |
| Fee and reward yield | (fees + rewards earned, USD) divided by time-weighted average capital, annualized. Ignores price moves. Staked Aerodrome positions earn AERO emissions instead of swap fees (the fees go to veAERO voters), so the table splits **fees** and **rewards** and shows the staked share of capital. |
| Time in range | Capital-weighted share of time the wallet's positions were in range (existing calculator; measured where snapshots exist, estimated with "~" otherwise, same convention as the Positions table). |
| Consistency | Share of **closed** positions with a positive result vs holding, with the count ("7 of 9 closed"). |

Supporting columns: peak capital, number of positions (open, closed), average holding time, median range half-width, staked share, flags (see below).

### Thresholds (config `SmartLp`, defaults)

A wallet is ranked only when it has **at least 5 positions in the pool, 3 of them closed, at least 14 days between first and latest activity, and peak capital of at least $5,000**. Wallets below that appear under "too little history" with the reason, unranked. The default sort is return vs holding (annualized); every metric column is sortable.

### Flags and default exclusions

- **JIT or bot**: a position that lived under 10 minutes or was added and removed in the same block. Wallets with more than 50% such positions are hidden by default (toggle "show bots").
- **Contract wallet** (`eth_getCode` non-empty: smart wallet, manager, vault): hidden by default (toggle "include contracts") and excluded from the typical-range statistics, because managers rebalance mechanically.
- **Hedge unknown** footnote on the table: LP-only result.

## 6. Typical range of the top wallets

For the top 10 ranked wallets (not flagged), computed from their positions in this pool:
- **Median half-width** in percent, with the 25th to 75th percentile band, relative to the price when the position was opened.
- **Centering**: median offset of the range centre from the opening price (percent), and the share of positions that were one-sided.
- **Median lifetime** of a position and **median time between a close and the next open** in the same pool (their rebalance interval).
- **Median time in range.**

The pool side panel shows this compactly next to the owner's own half-width (`PoolDto.MyHalfWidthPercent` already exists), and a **Use median width** button sets the panel's range, which feeds the Enter planner. If fewer than 3 wallets pass the thresholds the section says "not enough data" instead of showing statistics.

## 7. Data model (EF Core, one migration; SQLite and Postgres)

- `WatchedAddress` gains `IsFollowed` (bool, default false), `FollowedAt`, `AlertsEnabled` (bool, default false). The label already exists.
- `PoolScan`: `Id`, `Chain`, `Protocol`, `PoolAddress`, `FromBlock`, `ScannedThroughBlock`, `State`, `StartedAt`, `FinishedAt`, `Error`, `PositionsFound`, `Coverage`.
- `PoolPositionRef`: `Chain`, `PositionManager`, `TokenId`, `PoolAddress`, `Holder`, `IsStaked`, `Gauge`, `FirstBlock`, `LastActivityBlock`, `TickLower`, `TickUpper`, `LiquidityRaw`, `IsBurned`, `HolderIsContract`.
- `WalletPoolScore`: `Chain`, `PoolAddress`, `Wallet`, `WindowDays`, `ComputedAt`, and one column per metric above plus flags and `Rankable`/reason. Recomputed when older than `SmartLp:ScoreCacheHours` (6) or after a rescan.
- No new table for wallet positions: stage 2 writes the existing `Positions` / `PositionEvents` / `SyncCursors` rows.

## 8. Followed wallets

- **Follow / unfollow** from the leaderboard or the wallet page: sets `IsFollowed` (creating the `WatchedAddress` if the wallet is not there yet), with an editable label. Unfollow clears the flag; **stored data is kept**, consistent with the existing "removing a wallet keeps its data" decision. Maximum `SmartLp:MaxFollowed` (default 50).
- **Isolation from the owner's own numbers**: the wallet picker, "all my wallets" selection, Positions, Earnings, P&L, Borrow, the header totals and the Telegram range alerts (`RangeTransitionDetector`) use only wallets with `IsFollowed = false`. Followed wallets appear in their own "Followed" section on the Addresses page. Because API calls name their owners explicitly (`OwnerParam`), the drill-down simply passes the followed address.
- **Sync schedule**: the background coordinator syncs followed wallets at most once per `Sync:FollowedIntervalHours` (default 24), not every `Sync:IntervalMinutes`. A followed wallet with `AlertsEnabled` is synced every sync cycle instead, and the toggle's tooltip says it costs RPC quota; at most `SmartLp:MaxAlertWallets` (default 10) can have it on.
- **Move alerts** (Telegram via `IAlertNotifier`, only wallets with `AlertsEnabled`): a position **opened** (with the pool, range, half-width and USD size), **closed** (with its result vs holding), or **rebalanced** (a close followed by an open in the same pool within 24 hours). Sent only after the new state is saved. The first sync of a wallet never alerts. Moves under `SmartLp:MinAlertUsd` (default $1,000) are ignored. De-duplicated by token id and event.
- Since a followed wallet is synced across **all** its pools (the existing pipeline discovers everything), it also feeds the wallet's overall record, not only this pool.

## 9. API

- `POST /api/pools/{poolId}/lp-scan`: start (or resume incrementally) a scan; 202 with status; 429 with `Retry-After` inside the cooldown. `GET /api/pools/{poolId}/lp-scan`: status, progress, coverage, `scannedThroughBlock`, `finishedAt`.
- `GET /api/pools/{poolId}/lps?includeContracts=false&showBots=false&sort=returnApr`: leaderboard rows (metrics, flags, rankable and reason, `isFollowed`), coverage, computedAt, thresholds used.
- `GET /api/pools/{poolId}/lps/typical-range`: the section 6 statistics with the wallet count behind them.
- `PUT /api/addresses/{id}/follow` (body: label, alertsEnabled) and `DELETE /api/addresses/{id}/follow`; `GET /api/addresses` returns `isFollowed` and `alertsEnabled`.
- Drill-down reuses the existing positions, events and P&L endpoints with `owner=`.
- Same single-password auth as the rest of the API; no endpoint takes an RPC URL or key.

## 10. Frontend

- **Pool side panel** (`RangePanel`): a "Who earns here" section between the APR estimate and "Enter this range": scan state or "Scan this pool" button (with the cost warning and last scan time), coverage, the typical-range block with the **Use median width** button, the top 3 wallets, and a link "All wallets in this pool".
- **Leaderboard page** `/pools/:poolId/lps` (full width, like the Pools page): table of the section 5 columns, sortable, with the staked/unstaked split, flags as badges, thresholds explained in a tooltip, toggles for contracts and bots, a "too little history" group, coverage and `computedAt` ("scored 2 h ago"), a Follow button per row. Scan progress uses the existing progress components.
- **Wallet page** `/wallets/:address` (a followed wallet or any address from the leaderboard): the positions table, detail drawer and P&L cards reused with that owner; header with the label, a "followed" or "not followed" badge and the Follow button; a banner when it is not one of the owner's wallets ("another wallet: not part of your totals").
- **Addresses page**: a separate "Followed wallets" section with label editing, the alerts toggle (showing how many of the allowed are used) and unfollow.
- Follow the existing theme tokens, the `hide balances` toggle (USD sizes) and the `UpdatedAgo` convention.

## 11. Assumptions I made (not confirmed one by one)

1. **Move alerts vs a daily sync.** A daily sync would make "followed wallet moves" up to 24 hours late, so alerts require a per-wallet toggle that syncs that wallet every cycle, capped at 10. The owner chose daily sync and the alert but did not discuss this conflict; confirm or change.
2. Thresholds (5 positions, 3 closed, 14 days, $5,000, 7-day annualization floor, 10-minute JIT limit, $1,000 alert size) are starting points; all are config.
3. Contract wallets and bots are hidden by default; contract wallets are excluded from the typical-range statistics.
4. The leaderboard's default sort is return vs holding (annualized), not a blended score.
5. Candidate positions are stored in the ordinary position tables under non-watched owners, relying on the existing "unwatched owner data is hidden from views" behaviour. If that behaviour is ever changed, scoring would leak into the owner's totals: a test pins it (section 12).
6. Scan window default 30 days, cooldown 6 hours, scoring cache 6 hours, top 40 wallets scored, max 50 followed.
7. Typical-range statistics use the top 10 ranked wallets; below 3 qualifying wallets nothing is shown.

## 12. Tests (xUnit, no live network)

- **Index building** from a fake `IPositionProvider` / fake logs (pattern of `SyncServiceTests`, temp SQLite): pool filter (a transaction touching two pools only indexes the target), token id to pool mapping, staked holder is the staker, minted-and-staked-in-one-tx, burned position, incremental scan resumes from `ScannedThroughBlock`, coverage maths.
- **WalletScorer** with fixtures: a steady profitable wallet, a single lucky position (stays unranked), a JIT bot (hidden), a contract wallet (hidden), annualization "n/a" under 7 days, thresholds at and either side of each limit, staked wallet reports rewards and no fees.
- **TypicalRange**: medians and percentile band, one-sided share, rebalance interval, "not enough data" below 3 wallets.
- **Isolation**: followed wallets and candidate wallets never appear in totals, Positions, Earnings, Borrow or range alerts of the owner's wallets (query-level tests).
- **Sync schedule**: a followed wallet is skipped inside 24 h, an alerts-enabled wallet is not, the cap is enforced.
- **Move alerts**: opened, closed, rebalanced, first sync silent, under-minimum ignored, de-duplication, delivery failure does not repeat.
- **Cooldown and cost guard**: a second scan inside 6 hours is refused; the log-request guard stops a runaway scan with a readable message.
- **Verification by self-scoring** (manual, step S2): score the owner's own wallet as a candidate and compare its numbers with the Positions and P&L pages; they must match.

## 13. Build order (stop and ask the user to verify after each)

- **S0 Spike, no UI**: run the scan against one busy pool and one small pool on the real RPC. Report requests made, receipts fetched, time taken, positions found and coverage. Go or no-go on the window default and the cost guards before building anything else.
- **S1 Pool LP index**: provider method, `PoolScan` and `PoolPositionRef`, scan endpoints, coverage. This also unlocks `LP_CONCENTRATION` in the risk screen.
- **S2 Scoring and leaderboard API**: scorer, thresholds, flags, endpoint, domain tests; verify by scoring the owner's own wallet.
- **S3 Leaderboard UI**: pool panel section, leaderboard page, typical range and **Use median width**.
- **S4 Follow and wallet page**: the `IsFollowed` migration, isolation from totals, daily sync schedule, Followed section, wallet page.
- **S5 Move alerts**: per-wallet alert toggle, hourly sync for those wallets, Telegram messages.

## 14. Open questions and risks

- **Per-transaction resolution cost** is the main unknown. A busy pool has many Mint/Burn transactions in 30 days, each needing a receipt. S0 measures it; the fallback is a shorter default window or scanning only positions above a size, never silently truncating without saying so in the coverage line.
- **Sync entry point**: scoring needs to sync known token ids for an owner without `DiscoverAsync`. The existing `SyncRequest` can narrow to one position but still starts from discovery, so a small new entry point on `SyncService` is needed. Check how `SyncService` and `SyncCoordinator` split before adding it.
- **Position managers on Base**: the Uniswap v3 and Slipstream manager addresses come from the existing `Evm` configuration; Uniswap v3 forks or other managers are out of scope.
- **Slipstream gauge ownership**: the holder of a staked NFT is the gauge; if the stake history cannot be read for some position the holder is recorded as "gauge" and the wallet is unknown, and the position is excluded from rankings (counted in coverage).
- **Survivorship**: only wallets with activity in the window are seen; the leaderboard says "wallets active in the last N days".
- **Privacy**: addresses and labels of followed wallets stay in the local database and backups; they are not sent to Telegram beyond a shortened address and the label in alert messages the owner has chosen to enable.
