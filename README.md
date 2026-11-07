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

## Borrow (do LP earnings pay the interest?)

The **Borrow** page and the "Interest coverage" card on **Earnings** compare what your loans cost with what your LP positions earn (fees, rewards and Aave supply yield, accrued whether or not claimed), and track collateral against debt over time. Design and definitions are in [spec.md](spec.md); the mock is [docs/borrow-coverage-mock.html](docs/borrow-coverage-mock.html).

- **Aave v3 on Base** is read from chain for every followed wallet during a sync: health factor, collateral, debt, rates, and daily balances. It is on by default (`Evm:Chains:0:Lending:AaveV3`, which holds the market's PoolAddressesProvider).
- **Daily history needs an RPC that serves historical state** (an archive-capable plan, e.g. Alchemy). Without it the page still works from today forward and says so; days before today then show today's balances rather than invented movements. Each sync backfills up to 120 missing days, oldest first.
- **Other lenders (e.g. Binance)** are entered by hand under Borrow → Add manual loan: the debt and APR with a date for every change, optional collateral amounts and liquidation LTV. The app holds no exchange keys.
- Interest is simple APR; a week is 7 days and a month a twelfth of the year. Nothing is ever sent or executed: the page only shows numbers and suggestions.

## Wallets and privacy

There is no login. The list of wallets you follow (and their labels) is stored in your browser's localStorage, and every API call names the wallet(s) it wants (`?owner=0x…[,0x…]`, required). The server never lists the wallets it tracks, so a visitor to a deployed URL starts empty and must enter an address. "Forget" only removes it from your browser; its synced data stays in the database, so adding it again restores everything immediately.

This is not secrecy: positions are public on-chain data, and anyone who knows a wallet address can look it up, or fetch a position by id. Other things to know when hosting it publicly:

- `/api/backup` is disabled unless `Backup:Enabled` is true (it contains every wallet ever entered). The Docker compose file enables it because it only listens on localhost.
- The background sync keeps syncing every wallet ever entered, and out-of-range alerts go to the one configured Telegram chat for all of them.
- Run a single instance; sync state is in memory.

## Backups

The database holds price history and position snapshots that cannot be rebuilt from chain data. Use **Addresses → Download database backup** or `GET /api/backup` (SQLite only, needs `Backup:Enabled=true`). Deleting the database file forces a full resync.

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

## Deploying to Azure

`deploy/azure.ps1` builds the Docker image in Azure Container Registry and runs it on App Service (Linux, B1, one instance). It needs the Azure CLI and your RPC URL in the environment:

```powershell
az login
$env:BASE_RPC_URL = "https://base-mainnet.g.alchemy.com/v2/<key>"
./deploy/azure.ps1 -Name lpdash-yourname
```

Things to know before exposing it:

- **No authentication.** Anyone with the URL can use it and trigger syncs (RPC quota). Put App Service Authentication (Entra ID) in front, or restrict access by IP, unless you want it public; see "Wallets and privacy".
- **SQLite on `/home`** (persistent, SMB-backed) with `DELETE` journaling. Keep the app at **one instance** (no scale-out); the background sync and the file are single-writer. For more than that, Postgres needs a migrations assembly first (not built).
- `Backup:Enabled` is `false` there. Secrets (RPC URL, Telegram) are app settings, not in the image or repo.
- Health probe: `/health`.

### Deploy on every push to main

The `deploy` job in `.github/workflows/ci.yml` runs after the backend and frontend jobs pass on a push to `main`. It builds the image on the runner, pushes it to the Azure Container Registry (tagged with the commit SHA and `latest`), points the web app at it with `azure/webapps-deploy`, and waits for `/health`. Run `deploy/azure.ps1` once first so the resources exist, then add to the repo's Actions settings:

- **Variable** `AZURE_APP_NAME`: the `-Name` you gave the script.
- **Secrets** `ACR_USERNAME` and `ACR_PASSWORD`: from `az acr credential show -n <name without dashes>acr`.
- **Secret** `AZURE_WEBAPP_PUBLISH_PROFILE`: the whole XML from `az webapp deployment list-publishing-profiles -g rg-<name> -n <name> --xml`.

These are long-lived credentials; rotate them if access to the repo changes. App settings (RPC URL, Telegram) are not touched by the pipeline; change them with the script or `az webapp config appsettings set`.
