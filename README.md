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

The database is `data/lpdashboard.db` in the repo folder (git-ignored; set `DATA_DIR` in `.env` to use another folder). Compose runs SQLite in DELETE journal mode because WAL does not work on Windows bind mounts. The app asks for a password (see "Login and wallets"); the port is still bound to `127.0.0.1`, so it is not offered to your LAN unless you change it.

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
| `Auth:ResetPassword` | empty | Forgot the password? Set any new value (e.g. `reset-1`) and restart: the setup page opens once and your data stays. See "Login and wallets". |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | off | Set to `true` behind a TLS-terminating proxy (Azure sets it via `deploy/azure.ps1`) so the login cookie gets `Secure` and failed-login limits use the real client IP. Leave it off when the app is reachable directly, because the headers are then spoofable. |
| `Cors:Origins` | `["http://localhost:5173"]` | Also the origins allowed to make changing requests (login, add wallet…). Add your dev origin if Vite runs on another port. |

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

## Enter a pool range (what to hold, and what hedging costs)

On **Pools**, the side panel's "Enter this range" section takes a pool, a range (each bound on its own) and either a USD budget or the tokens you hold, and says how much of each token to end up with, what to swap and what that costs. A hedge block compares the 30-day result with and without a short of the volatile token. The maths lives in `Domain/Metrics/EntryPlanner.cs` and `HedgePlanner.cs` (`POST /api/pools/{id}/entry-plan`); wallet balances for the prefill come from `GET /api/wallet/balances` (watched wallets only). Everything is an estimate and read-only: nothing is sent or signed. Design: [docs/enter-pool-spec.md](docs/enter-pool-spec.md).

## Login and wallets

The site has one account protected by one password (8 to 128 characters, no username). Log in once per device and the wallets, their labels and all stored data are the same everywhere; nothing needs retyping.

- **First run.** A new server shows "Set your password". Until it is set, every `/api` call except `/health` answers 401, so nothing is visible, **but whoever opens the page first can set the password**. Do it right after deploying, before sharing the URL.
- **Sessions.** A random token in an `HttpOnly`, `SameSite=Lax` cookie (`Secure` over HTTPS) that lasts up to a year and renews while you use it. Only its SHA-256 is stored, so a leaked database cannot be used to sign in. **Log out** ends this device; **Addresses → Account** changes the password (which signs out other devices) or signs out every device.
- **Brute force.** Five wrong passwords from one address pause login from it for 15 minutes; each wrong answer is also delayed.
- **Forgot the password.** Set `Auth__ResetPassword` to a value you have not used before (e.g. `reset-1`) and restart. All devices are signed out and the setup page opens once, still protected by nothing but who gets there first, so do it straight away. Your data is untouched. Leaving the setting in place does not reopen setup on later restarts; use a different value to reset again.
- **Wallets** are saved to the account (`GET/POST /api/addresses`, `PATCH`/`DELETE …/{address}`). Removing one stops syncing it; its stored data stays, so adding it again restores everything. Wallets that were kept in an older version's browser storage are uploaded, labels included, the first time you log in on that browser.
- API calls still name the wallet(s) they want (`?owner=0x…[,0x…]`). Once logged in you can look up any address; the password is the only gate.

Other things to know when hosting it publicly:

- `/api/backup` is disabled unless `Backup:Enabled` is true (it contains every wallet ever entered). It now needs a login too. The Docker compose file enables it because it only listens on localhost.
- The background sync keeps syncing every wallet in the account, and out-of-range alerts go to the one configured Telegram chat.
- Run a single instance; sync state, sessions' renewal and the login limiter are in memory or in one SQLite file.

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

- **Claim the site right after the first deploy.** It is protected by one password, but until you set it the setup page is open to anyone who finds the URL. Open `https://<name>.azurewebsites.net` straight after `deploy/azure.ps1` (and after the first CI deploy of a version that adds login) and set the password; see "Login and wallets". For more protection, App Service Authentication or an IP restriction can sit in front as well.
- The script sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, so the app sees HTTPS (the cookie is `Secure`) and the real client IP (the login limiter works per visitor).
- **SQLite on `/home`** (persistent, SMB-backed) with `DELETE` journaling. Keep the app at **one instance** (no scale-out); the background sync and the file are single-writer. For more than that, Postgres needs a migrations assembly first (not built).
- `Backup:Enabled` is `false` there. Secrets (RPC URL, Telegram) are app settings, not in the image or repo.
- Health probe: `/health`.

### Deploy on every push to main

The `deploy` job in `.github/workflows/ci.yml` runs after the backend and frontend jobs pass on a push to `main`. It builds the image on the runner, pushes it to the Azure Container Registry (tagged with the commit SHA and `latest`), points the web app at it with `azure/webapps-deploy`, and waits for `/health`. Run `deploy/azure.ps1` once first so the resources exist, then add to the repo's Actions settings:

- **Variable** `AZURE_APP_NAME`: the `-Name` you gave the script.
- **Secrets** `ACR_USERNAME` and `ACR_PASSWORD`: from `az acr credential show -n <name without dashes>acr`.
- **Secret** `AZURE_WEBAPP_PUBLISH_PROFILE`: the whole XML from `az webapp deployment list-publishing-profiles -g rg-<name> -n <name> --xml`.

These are long-lived credentials; rotate them if access to the repo changes. App settings (RPC URL, Telegram) are not touched by the pipeline; change them with the script or `az webapp config appsettings set`.
