# LP Ledger

Read-only liquidity pool tracker: paste a wallet address, see positions and profit and loss. Aerodrome (Base) first, then Uniswap and Meteora. What is built and what is left: [STATUS.md](STATUS.md). Plan: [docs/PLAN.md](docs/PLAN.md).

- `src/LpLedger.Core` normalized models, concentrated-liquidity math, PnL, Aerodrome adapter (no I/O)
- `src/LpLedger.Infrastructure` Alchemy chain reader, DefiLlama prices, demo chain
- `src/LpLedger.Api` ASP.NET Core minimal API (`GET /api/wallets/{address}/positions`, OpenAPI at `/openapi/v1.json` in Development)
- `tests/LpLedger.Tests` xUnit
- `web/` React + Vite + TypeScript. `docs/prototype.html` is the approved static prototype
- `db/schema.sql` Postgres design. Scan history tables are created by `LedgerDb` (EF Core); snapshots and price cache are not wired yet

## Run (demo data, no keys needed)

    dotnet run --project src/LpLedger.Api        # http://localhost:5080
    cd web && npm install && npm run dev         # http://localhost:5173

Open the web app and paste any 42-character address. Without an RPC the API serves simulated positions and the UI says "Demo data".

## Run against real Base data

    Base__RpcUrl=<rpc with getLogs> dotnet run --project src/LpLedger.Api

Set `ConnectionStrings__Ledger=<npgsql connection string>` to keep scanned history in Postgres, so a restart or refresh reads only new blocks (tables are created on startup; without it history is kept in memory). Optional: `Aerodrome__PositionManagers__0` and up, `Cors__Origins__0`. The URL must be an Alchemy endpoint (the reader uses `alchemy_getAssetTransfers`). The free tier works but the first lookup of a busy wallet takes about a minute. See STATUS.md.

## Tests and types

    dotnet test
    cd web && npm run gen:api     # regenerate src/api/schema.d.ts from the running API's OpenAPI document
