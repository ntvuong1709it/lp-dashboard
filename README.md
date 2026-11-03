# LP Dashboard

Read-only, local, single-user dashboard for concentrated-liquidity LP positions (Uniswap v3 and Aerodrome Slipstream on Base). Watch a wallet and see every position with its price range, age, fees, P/L, impermanent loss, P/L vs HODL and weekly fee claims. Data comes straight from the chain over JSON-RPC; prices come from DefiLlama.

Design decisions, metric definitions, protocol edge cases and the roadmap are in [PLAN.md](PLAN.md).

## Quick start

Prerequisites: .NET SDK 10, Node 22+, and an Alchemy (or compatible) Base RPC URL.

```bash
# backend -> http://localhost:5080
cd backend
dotnet user-secrets set "Evm:Chains:0:RpcUrl" "https://base-mainnet.g.alchemy.com/v2/<KEY>" --project src/LpDashboard.Api
dotnet run --project src/LpDashboard.Api

# frontend -> http://localhost:5173 (proxies /api to :5080)
cd frontend
npm install
npm run dev
```

Open the app, go to **Addresses**, add a wallet and wait for the first sync. On Windows PowerShell use `npm.cmd` if script execution is restricted.

### Docker (single container)

The image builds the frontend and serves it from the API. Put the RPC URL in a git-ignored `.env` file:

```
BASE_RPC_URL=https://base-mainnet.g.alchemy.com/v2/<KEY>
```

```bash
docker compose up --build   # http://localhost:5080
```

The database is `data/lpdashboard.db` in the repo folder (git-ignored; set `DATA_DIR` in `.env` to use another folder). Compose runs SQLite in DELETE journal mode because WAL does not work on Windows bind mounts. The port is bound to `127.0.0.1` only because the app has no authentication.

## Configuration

Defaults are in `backend/src/LpDashboard.Api/appsettings.json`; override with user-secrets or environment variables (`Evm__Chains__0__RpcUrl`).

| Setting | Default | Notes |
| --- | --- | --- |
| `Evm:Chains:0:RpcUrl` | empty | Required to sync. Never logged. |
| `Sync:IntervalMinutes` | 15 | Background sync period. |
| `Sync:SnapshotIntervalMinutes` | 60 | Minimum spacing of position snapshots. |
| `Alerts:Telegram:BotToken` / `ChatId` | empty | Enables Telegram alerts (below). Never logged. |
| `Database:Provider` | `Sqlite` | `Postgres` is wired but has no migrations yet. |
| `ConnectionStrings:Default` | `Data Source=lpdashboard.db` | Relative paths resolve against the API content root. |

Without an RPC URL the API still runs; the sync status reports "No chain providers configured".

## Alerts

When a sync sees an open position leave its range, or return to it, a Telegram message is sent (one per change, after the new state is saved). The first sync of a position never alerts.

1. Create a bot with [@BotFather](https://t.me/BotFather) and copy its token.
2. Send your bot any message, then open `https://api.telegram.org/bot<token>/getUpdates` and copy `message.chat.id`.
3. Put `TELEGRAM_BOT_TOKEN` and `TELEGRAM_CHAT_ID` in `.env` (Docker) or set `Alerts__Telegram__BotToken` / `Alerts__Telegram__ChatId`.
4. Check it with `curl -X POST http://localhost:5080/api/alerts/test`; `GET /api/alerts/status` reports whether a channel is configured.

Alerts are only as timely as the sync (`Sync:IntervalMinutes`, 15 by default).

## Backups

The database holds price history and position snapshots that cannot be rebuilt from chain data. Use **Addresses → Download database backup** or `GET /api/backup` (SQLite only). Deleting the database file forces a full resync.

## Development

```bash
cd backend && dotnet test LpDashboard.slnx     # domain math + SyncService integration tests
cd frontend && npm run build                   # typecheck + production build
```

CI (`.github/workflows/ci.yml`) runs both on every push and pull request.

New SQLite migration (the EF tool is a local tool; run `dotnet tool restore` once):

```bash
cd backend
dotnet ef migrations add <Name> --project src/LpDashboard.Infrastructure.Migrations.Sqlite --startup-project src/LpDashboard.Api
```

## Layout

```
backend/
  src/LpDashboard.Domain/          entities, position math, metrics
  src/LpDashboard.Application/     sync, pricing, queries, abstractions
  src/LpDashboard.Infrastructure/  EF Core, EVM RPC + Uniswap v3-style provider, DefiLlama
  src/LpDashboard.Api/             controllers, background sync, SPA hosting
  tests/LpDashboard.Domain.Tests/
frontend/                          React 19, Vite, TanStack Query, Recharts, Tailwind
```
