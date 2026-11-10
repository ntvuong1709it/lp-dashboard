# Handover: improvement roadmap in progress

Read [PLAN.md](PLAN.md) (design, metric definitions, protocol notes) and [README.md](README.md) (run/config) first. This file records where the improvement work stands.

## Working agreement with the user

The user asked for an improvement roadmap (steps 1-6 plus a 3.5 QoL batch), **one step at a time: after finishing each step, stop and ask the user to verify; only continue on their go-ahead.** Be honest about what was and was not verified. Do not commit unless asked; the user asked for commits split into logical pieces.

## State of the repo

- Branch: `improvements/price-fallback-table-docker` (cut from `main`, **not pushed, not merged**).
- Two commits on it: `7d883ec` (price fallback, table, backup, sync tests) and `bf8bfa7` (CI, Docker, SPA hosting, README).
- Tests: 72 passing (`cd backend; dotnet test LpDashboard.slnx`). Frontend: `cd frontend; npm run build`.
- The GitHub Actions workflow (`.github/workflows/ci.yml`) has **never run**.

## Roadmap status

| # | Step | Status |
| - | ---- | ------ |
| 1 | Real-wallet verification + protocol checks | Done. User confirmed numbers match the Aerodrome UI (positions, ticks, pending AERO). Slipstream V3 `EmissionsClaimed` and old `ClaimRewards` signatures both produce claims. |
| 2 | Missing-price handling, sortable/filterable table, persisted UI state | Done and user-verified |
| 3 | CI, SyncService integration tests, README, Docker, backups | Implemented and committed. **User has not explicitly approved Step 3** (they were asked, then moved on to Docker data location and commits). Ask once before starting Step 4. |
| 3.5 | **Quality-of-life batch** | **Implemented, uncommitted, awaiting user verification** (all items except the optional dark/light theme and keyboard shortcuts). Backend 75 tests pass, frontend builds, new endpoints smoke-tested against a scratch DB; UI not clicked through by the assistant. Do Step 4 only after the user approves. |
| 4 | Time in range, portfolio value over time, CSV export | **Implemented, uncommitted, awaiting user verification.** 95 tests pass, frontend builds, endpoints checked against a copy of the user's real DB (history's last point equals the summary value; all four CSVs download). UI not clicked through. Time in range = measured (snapshots) + estimated (tick inferred from DefiLlama prices via `PositionHistory`, covers pre-sync history and gaps; shown with "~" and a tooltip). Estimates are only as good as the prices: spot checks against deposit events whose token mix proves the true pool price were 0.6-1.3% (60-130 ticks) off, which can flip the call near a range edge (e.g. a one-sided position showing ~10% instead of ~0%). Possible refinement: use event token mix (one-sided = out of range, both tokens = in range) as ground truth. The estimate samples HOURLY (finest the price cache supports); 3-11 h sampling missed short dips (#1167 showed 100%, hourly shows ~98.9%). DefiLlama's chart endpoint 400s above 500 points per request across all tokens (1 token <=500, 2 tokens <=250), so `PriceService.GetSeriesAsync` fetches in windows of 480/tokens; a unit test enforces that limit. Cold list load ~12 s on 17 positions (once; prices then cached in the DB), warm ~70 ms; the summary endpoint skips the estimate. `formatShare` never prints 100.00% unless exactly 100. |
| 5 | Out-of-range alerts (Telegram/Discord/email/browser) | Not started |
| 6 | New chains/protocols: Uniswap v4, Ethereum, Solana, Robinhood | Not started |
| 7 | Public hosting limits (deferred by the user, do later) | Not started. Background sync covers every wallet ever entered (RPC quota) and alerts for all go to one Telegram chat. Plan: per-wallet last-seen time (new migration), background-sync only wallets seen in the last N days, `Sync:PinnedAddresses` for the operator, cap on active wallets, manual-sync cooldown, per-IP rate limit on `POST /api/addresses` and `/api/sync`. |

### Step 3.5 plan: quality-of-life batch (recommended, not built)
Mostly small frontend work. Already done in Step 2: sortable/filterable table, persisted UI state.
- **Quick wins**: copy buttons and explorer links for addresses, tx hashes and token IDs; links to the pool on Uniswap/Aerodrome; edit a wallet's label (today only add/remove); "hide balances" privacy toggle; totals row on the positions table; skeleton loaders instead of "Loading..."; "updated X min ago" next to the data.
- **Sync control**: resync one wallet or one position; "rescan from block X" action for debugging.
- **Setup and health**: friendly setup screen when no RPC URL is configured (the API already reports "No chain providers configured"); `/health` endpoint; recent-errors panel.
- **Wallet management**: label editing (orphaned data is intentionally kept, see Open issues; no fix needed).
- **Lower value, only if the user wants them**: dark/light theme, keyboard shortcuts.

### Step 4 (built; the plan below is what was agreed)
1. **Time in range**: share of time a position was in range, computed from `PositionSnapshots` (hourly, only from first sync; show "n/a" where there are no snapshots rather than guessing). Per-position on table and detail page.
2. **Portfolio value over time**: new endpoint summing snapshots across positions (API only has per-position snapshots today) plus a chart on the Portfolio page. Same history limit.
3. **CSV export**: positions, events, weekly claims, daily earnings.
Approach: domain calculation with tests first, then API, then UI, then ask the user to check the numbers.

## Open issues and decisions

- **Orphaned data (decided: keep, by design)**: removing a watched address does NOT delete its positions/events/snapshots/cursors, so re-adding the same wallet reuses them without a resync (data is keyed by owner address string; sync resumes from the saved cursor). The user's DB has 46 such positions from a removed wallet (`0xdd91...`), hidden from views until it is re-added. Do not add delete-on-remove. The user pushes branches and opens PRs themselves.
- **Postgres migrations assembly** (`LpDashboard.Infrastructure.Migrations.Postgres`) is still not built.
- **Reward price fallback**: a reward claim with no price uses the next known price of the *same reward token* (estimated, flagged "est. reward price"); the user was asked to confirm this is acceptable (alternative: leave $0 with a warning) and did not object. Cause: DefiLlama has no history for the new AERO token (`0x3f5b...a4ee`) before about Oct 23, 2026.
- Not verified: `docker compose up` with a real `BASE_RPC_URL` was the user's own run (it worked for them); the compose env passthrough was never tested by the assistant.

## How the user runs it (important)

- The user runs the app through **Docker Compose** (Rancher Desktop): `docker compose up --build -d` -> http://localhost:5080. The container serves the built frontend.
- Database: `data/lpdashboard.db` in the repo folder (git-ignored), bind-mounted at `/data`. Compose sets `Database__SqliteJournalMode=DELETE` because **WAL breaks on Windows bind mounts** (a WAL-mode file on the mount gives "disk I/O error"; convert a file offline with `PRAGMA journal_mode=DELETE` first). `data/docker-volume-copy.db` is a leftover copy and can be deleted.
- RPC key lives in `.env` (git-ignored) as `BASE_RPC_URL=...`. **Never read, print or commit `.env` or any key.** The user pasted a key in a previous chat and was told to rotate it.
- Tool notes for this machine: Python is not installed (use PowerShell/Node for JSON). Files use CRLF in git (`core.autocrlf=true`); in edits via Perl/sed be careful, prefer the Edit tool. `dotnet build/test` into the repo's `bin/` fails while an API instance runs; use `--artifacts-path <scratch dir>`. To test frontend changes against a new backend without touching the user's instance, run the published API and Vite on other ports (e.g. 5081/5174) against a copy of the DB, then stop them.
- The user's wallet to test with: the one already added in their app (do not copy it into committed files).

## Where things are

- Metrics: `backend/src/LpDashboard.Domain/Metrics/PositionMetricsCalculator.cs`, `EarningsCalculator.cs`
- Sync: `backend/src/LpDashboard.Application/Sync/SyncService.cs`; tests in `backend/tests/LpDashboard.Domain.Tests/SyncServiceTests.cs` (fake provider + price source, temp SQLite; a pattern to reuse)
- Pricing: `backend/src/LpDashboard.Application/Pricing/PriceService.cs`
- Query/DTO layer: `backend/src/LpDashboard.Application/Queries/`
- Frontend table logic: `frontend/src/lib/positionTable.ts`; persistence hook `frontend/src/lib/usePersistentState.ts`; pages in `frontend/src/pages/`
