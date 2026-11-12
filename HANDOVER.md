# Handover: where the project stands

Last refreshed: 2026-11-11, on `main` at `6b48ca0`. Read [README.md](README.md) (run, config, login, deploy) and [PLAN.md](PLAN.md) (design, metric definitions, protocol notes) first. Note that PLAN.md's "Status" section is from the first week and is out of date; this file is the current status. The README's `Sync:IntervalMinutes` default of 15 is also stale: `appsettings.json` has 60.

## Working agreement with the user

- **One step at a time.** After each step, stop, say what was verified and what was not, and wait for the user's go-ahead. Be honest about what was and was not checked (for example "UI not clicked through").
- **Do not commit unless asked.** The user pushes branches and opens PRs themselves.
- **Never read, print or commit `.env`, API keys or RPC URLs.** Do not copy the user's wallet address into committed files. Outbound requests to outside services may carry token and pool addresses only, never wallet addresses or other user data.
- New features are specified in `docs/` first (interview, then spec), then built in the spec's "Build order".

## State of the repo

- Branch: `main`, in sync with `origin/main`. Earlier PRs (#1 to #5) are merged. The branches `improvements/price-fallback-table-docker` and `deploy/azure-script` (local and remote) and the remote `features-page-status` are merged and can be deleted. The remotes `version-2` and `feature/unclaimed-ui` were not looked at.
- Uncommitted: the removal of the Smart-LP watcher and of the risk screen's `LP_CONCENTRATION` check (see "Dropped" below).
- Backend tests: 556 passing after the risk screen (`cd backend; dotnet test LpDashboard.slnx`, run 2026-11-11; 380 before it). Frontend: `cd frontend; npm run build` was not re-run for this refresh.
- CI (`.github/workflows/ci.yml`) runs both and deploys to Azure on every push to `main`. This refresh did not check its run history.

## What is built

| Area | Status |
| --- | --- |
| Positions, P&L, fees, rewards, IL, P/L vs HODL, unclaimed fees and rewards | Built. Verified against the Aerodrome UI on a real wallet (positions, ticks, pending AERO). Uniswap v3 and all four Aerodrome Slipstream deployments on Base. |
| Slipstream staking edge cases | Built, including positions minted and staked in one transaction (`MediatedStakeTests`, `StakePeriodTests`). |
| Time in range, portfolio value over time, reconstructed history before first sync | Built. Estimates use hourly DefiLlama prices and are shown with "~". |
| Pages | Overview/Earnings (with Activity), Borrow, Positions, Pools, P&L, Addresses. Features page was removed. |
| Pools explorer | Built: history, on-chain TVL, trailing and in-range APR, pool comparison, HODL break-even, gauge emissions, data flags. |
| Enter pool planner (swap amounts and hedge sizing) | Built 2026-11-10; spec [docs/enter-pool-spec.md](docs/enter-pool-spec.md). **Not yet verified by the user** against a real wallet and the Aerodrome deposit screen. |
| Risk screen | Built 2026-11-11 (steps 1 to 4 of [docs/risk-screen-spec.md](docs/risk-screen-spec.md), section 15 lists where it differs): token and pool verdicts with evidence (GoPlus, honeypot.is, DexScreener, node reads; Basescan optional), Risk column and "Risk check" section on Pools, Risk column and panel on Positions, star toggle, `/risk` page with lookup, nightly re-check, hourly pool-balance snapshots, Telegram alerts. Migration `AddRiskScreen` (four new tables). **Not yet verified by the user** against tokens they know; UI checked on a scratch instance with the Pools and Risk pages only (no positions in it, so the Positions column and drawer were not clicked through). |
| Borrow tracking | Built: manual loans plus Aave v3 on Base, coverage verdict, collateral trend. Spec: root [spec.md](spec.md). Daily Aave history needs an archive-capable RPC. |
| Alerts | Telegram only, for a position leaving or returning to its range (includes range and price). Other channels (Discord, email, browser) were never built. |
| Login and wallets | Single password, server-side wallet list, forwarded-header support for Azure. See README "Login and wallets". |
| Hosting | Docker (the user's local setup), plus Azure App Service with CI deploy on push to `main`. |
| Price ticker | BTC, ETH, BNB, AERO in the header. |
| CSV export | Built, then removed on 2026-11-04 (commit `23c4260`). |

## Not built, and specs waiting

| Item | Status |
| --- | --- |
| Rebalance simulator | Design only: [docs/rebalance-simulator.md](docs/rebalance-simulator.md). Its swap solver is meant to reuse `EntryPlanner`. |
| Clickable mocks | `docs/borrow-*.html`, `docs/enter-pool-mock.html`. The risk mock was published as a private Claude artifact and is not in the repo. |
| New chains and protocols | Not started. The `Chain` and `Protocol` enums list Ethereum, Solana, Robinhood, Uniswap v4, Orca, Raydium and Meteora, but only Base with Uniswap v3 and Aerodrome Slipstream has a provider. |
| Postgres | Wired but no migrations assembly (`LpDashboard.Infrastructure.Migrations.Postgres` does not exist). SQLite is the only supported database. |
| Public hosting limits | Partly open. Login exists, but the background sync still covers **every wallet ever entered** (RPC quota) and all alerts go to one Telegram chat. Plan: per-wallet last-seen time (new migration), sync only wallets seen in the last N days, `Sync:PinnedAddresses` for the operator, a cap on active wallets, a manual-sync cooldown, and per-IP rate limits on `POST /api/addresses` and `POST /api/sync`. |

## Dropped

- **Smart-LP watcher and the risk screen's `LP_CONCENTRATION` check (dropped 2026-11-12).** The watcher's discovery was a pool-level `eth_getLogs` scan over a 30-day window. The S0 spike showed it cannot run on the user's RPC plan: Alchemy Free allows 10 blocks per `eth_getLogs`, and Base blocks are now about 0.2 s (1.296M blocks measured as 3 days), so 30 days is about 13M blocks, i.e. about 1.3M requests against the app's guard of 200. Nothing was measured beyond that, and the user chose to remove the feature as too RPC-hungry. Its spec was deleted (recoverable from git history: `docs/smart-lp-spec.md`); the check was removed from `Domain/Risk` with its tests. Do not assume 2 s blocks anywhere (`BlockLocator` uses timestamps). If this is ever revisited it needs an RPC plan with a large `eth_getLogs` range first, and a sync entry point that takes known token ids instead of calling `DiscoverAsync`.

## Open issues and decisions

- **Orphaned data (decided: keep, by design).** Removing a watched address does NOT delete its positions, events, snapshots or cursors, so re-adding the wallet reuses them. Data is keyed by owner address and hidden from views while the address is not watched. The user's database has 46 such positions from a removed wallet. Do not add delete-on-remove.
- **Reward price fallback.** A reward claim with no price uses the next known price of the same reward token (estimated, flagged "est. reward price"). The user was asked, and did not object. Cause: DefiLlama has no history for the new AERO token (`0x3f5b…a4ee`) before about Oct 23, 2026.
- **Time-in-range estimates** are as good as the prices: spot checks against deposit events were 0.6 to 1.3% (60 to 130 ticks) off, which can flip the call near a range edge. Possible refinement: use the event token mix (one-sided means out of range) as ground truth.
- **DefiLlama chart endpoint** returns 400 above 500 points per request across all tokens, so `PriceService.GetSeriesAsync` fetches in windows of 480 divided by the number of tokens. A unit test enforces it.
- **Not verified by the assistant:** `docker compose up` with a real `BASE_RPC_URL` was the user's own run (it worked for them); the compose env passthrough was never tested here. A past month of Aave daily history was not compared with Aave's interface.
- **Compiler warning** to clean up when passing by: `PositionQueryService` has an unread constructor parameter `addresses`.

## How the user runs it (important)

- Through **Docker Compose** (Rancher Desktop): `docker compose up --build -d`, then http://localhost:5080. The container serves the built frontend.
- Database: `data/lpdashboard.db` in the repo folder (git-ignored), bind-mounted at `/data`. Compose sets `Database__SqliteJournalMode=DELETE` because **WAL breaks on Windows bind mounts** ("disk I/O error"; convert a file offline with `PRAGMA journal_mode=DELETE` first). `data/docker-volume-copy.db` is a leftover and can be deleted.
- The RPC key lives in `.env` (git-ignored) as `BASE_RPC_URL=...`. **Never read, print or commit `.env` or any key.** A key was pasted in an earlier chat and the user was told to rotate it.
- The user's wallet to test with is the one already added in their app (do not copy it into committed files). To test against real data without touching their instance, run the published API and Vite on other ports (for example 5081 and 5174) against a **copy** of the database, then stop them.

## Tool notes for this machine

- Python is not installed; use PowerShell or Node for JSON and scripts.
- Files use CRLF in git (`core.autocrlf=true`); prefer the Edit tool over sed or perl.
- `dotnet build` and `dotnet test` into the repo's `bin/` fail while an API instance runs; pass `--artifacts-path <scratch dir>`.
- On Windows PowerShell use `npm.cmd` if script execution is restricted.
- New SQLite migration: `cd backend; dotnet ef migrations add <Name> --project src/LpDashboard.Infrastructure.Migrations.Sqlite --startup-project src/LpDashboard.Api` (run `dotnet tool restore` once). **Two features adding migrations on parallel branches will conflict in the model snapshot**: merge one before creating the next.

## Where things are

- Metrics: `backend/src/LpDashboard.Domain/Metrics/` (`PositionMetricsCalculator`, `EarningsCalculator`, `TimeInRangeCalculator`, `PerformanceCalculator`, `EntryPlanner`, `HedgePlanner`, `BorrowCoverageCalculator`, `RangeTransitionDetector`, and more); position maths in `Domain/Calculations/PositionMath.cs`.
- Sync: `Application/Sync/SyncService.cs`, `SyncCoordinator.cs`, `Api/Background/SyncBackgroundService.cs`. Tests in `backend/tests/LpDashboard.Domain.Tests/SyncServiceTests.cs` (fake provider and price source, temp SQLite; the pattern to reuse).
- Chain access: `Infrastructure/Evm/` (`UniswapV3StyleProvider`, `EvmRpcClient` with batching, pacing and adaptive `eth_getLogs`, `AaveV3Provider`, `BlockLocator`); the provider contract is `Application/Abstractions/IPositionProvider.cs`.
- Pricing: `Application/Pricing/PriceService.cs`, `Infrastructure/Pricing/DefiLlamaPriceSource.cs`. Pool data: `Application/Pools/`, `Infrastructure/Pools/DefiLlamaPoolStatsSource.cs`.
- Alerts: `Application/Abstractions/IAlertNotifier.cs`, `Infrastructure/TelegramAlertNotifier.cs`; range alerts are sent from `SyncService` after the new state is saved.
- Query and DTO layer: `Application/Queries/`. API: `Api/Controllers/`. Wallet scoping: `Api/OwnerParam.cs` (every call names its owners).
- Frontend: pages in `frontend/src/pages/`, components in `frontend/src/components/` (`pools/`, `borrow/`, `earnings/`), logic in `frontend/src/lib/`, API hooks in `frontend/src/api/client.ts`.
- Specs: root `spec.md` (Borrow, built), `docs/enter-pool-spec.md` (built), `docs/risk-screen-spec.md` (built, minus the dropped `LP_CONCENTRATION` check), `docs/rebalance-simulator.md` (design only).
