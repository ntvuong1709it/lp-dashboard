# LP Position Dashboard — Plan & Handover

Read-only, local, single-user dashboard for concentrated-liquidity LP positions (in the spirit of Krystal / Revert).
Features: watch wallets → all positions, price range, age, P/L, impermanent loss, P/L vs HODL, weekly fee claims, gas included in P/L.

## How to run

Prereqs: .NET SDK 10, Node 22+. On Windows PowerShell use `npm.cmd` if script execution is restricted.

```powershell
# backend (http://localhost:5080)
cd backend
dotnet user-secrets set "Evm:Chains:0:RpcUrl" "https://base-mainnet.g.alchemy.com/v2/<ALCHEMY_KEY>" --project src/LpDashboard.Api
dotnet run --project src/LpDashboard.Api

# frontend (http://localhost:5173, proxies /api to :5080)
cd frontend
npm.cmd install
npm.cmd run dev
```

- Tests: `cd backend; dotnet test LpDashboard.slnx`
- New migration (SQLite): `dotnet ef migrations add <Name> --project src/LpDashboard.Infrastructure.Migrations.Sqlite --startup-project src/LpDashboard.Api` (dotnet-ef is a local tool: `dotnet tool restore` at repo root).
- SQLite file: `backend/src/LpDashboard.Api/lpdashboard.db` (relative paths resolve against the API content root). Delete it to resync from scratch.
- Without an RPC URL the API still runs; sync status reports "No chain providers configured".

## Decisions

| Topic           | Decision                                                                                                                                                                                                             |
| --------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Stack           | .NET 10 Web API (Clean Architecture: Domain / Application / Infrastructure / Api) + React 19 / Vite 7 / TS / TanStack Query / Recharts / Tailwind 4                                                                  |
| Chains roadmap  | Base (done) → Ethereum (config only) → Solana → Robinhood Chain                                                                                                                                                      |
| Protocols       | Uniswap v3 + Aerodrome Slipstream (done, one adapter) → Uniswap v4 → Orca / Raydium CLMM / Meteora DLMM                                                                                                              |
| Source of truth | On-chain via JSON-RPC (Alchemy). No subgraphs, no Revert/Krystal APIs                                                                                                                                                |
| Discovery       | `alchemy_getAssetTransfers` (erc721, to/from owner) per position manager; fallback: Transfer log scan from `StartBlock` (`UseAlchemyTransfers: false`)                                                               |
| History         | `eth_getLogs` on the position manager filtered by tokenId topics (OR-list, one scan for all positions); adaptive block range (uses Alchemy's suggested range from the error message, otherwise halves)               |
| Prices          | DefiLlama coins API (free, no key). Historical bucketed hourly and cached in `TokenPrices`; current cached in memory 60s                                                                                             |
| DB              | EF Core, `Database:Provider` = `Sqlite` (default) or `Postgres`; one migrations assembly per provider; repositories + unit of work in Application; enums/BigInteger stored as strings; DateTimes forced to UTC       |
| Sync            | `SyncBackgroundService` every `Sync:IntervalMinutes` (15) + on startup + `POST /api/sync`; one DI scope per (owner, chain, protocol); cursor per (chain, protocol, owner) only advances when all positions succeeded |
| Snapshots       | One per open position per `Sync:SnapshotIntervalMinutes` (60). No historical backfill                                                                                                                                |
| Security        | localhost only, no auth; RPC URL (contains key) only in user-secrets and never logged; address input validated (EVM regex / Solana base58); CORS limited to Vite origin                                              |
| Fee-claim weeks | ISO weeks (Monday start) in the browser's time zone (`timeZone` query param, IANA id)                                                                                                                                |

## Metric definitions (Domain/Metrics/PositionMetricsCalculator.cs)

- Deposited = Σ IncreaseLiquidity amounts × prices at the time
- Withdrawn = Σ DecreaseLiquidity principal × prices at the time
- Collect split (CollectSplitter): DecreaseLiquidity credits tokensOwed; each Collect consumes owed principal first, the remainder is fees. Uncollected fees = static `collect()` result − principal still owed
- FeesEarned = claimed fees (at claim time) + unclaimed fees (now) + claimed rewards + pending rewards
- PnL = Current + Withdrawn + FeesEarned − Deposited − Gas
- HODL = deposited token amounts × reference prices (current for open, last event prices for closed)
- IL = (current + withdrawn − deposited) token amounts × reference prices (fees excluded)
- PnL vs HODL = Current + Withdrawn + FeesEarned − Gas − HODL
- Fee APR = FeesEarned ÷ time-weighted average capital ÷ days × 365
- Gas = receipt `gasUsed × effectiveGasPrice + l1Fee` (OP-stack), only for txs sent by the owner, split evenly across positions touched by the tx

## Protocol notes / edge cases handled

- Burned NFTs: `positions()` reverts → pool + ticks are recovered from the pool `Mint` log preceding `IncreaseLiquidity` in a known tx receipt
- Aerodrome Slipstream has **4 deployments on Base** (each NPM + factory configured in `appsettings.json`): initial `0x8279…5b72`, gauge-caps `0xa990…D55F`, gauges-v3 `0xe1f8…8b53` (source: github.com/aerodrome-finance/slipstream README), and Slipstream V3 `0x4857…c60b` (factory `0x9cb2…7a76`)
- Staked Slipstream NFTs are held by a CL gauge (detected via `gauge.nft() == NPM`); owner stays the user; pending = `gauge.earned(owner, tokenId)`; claims = gauge `ClaimRewards(from, amount)` logs (no tokenId → split evenly across the owner's positions in that gauge); gauge-recipient `Collect`s are ignored; staked positions have no unclaimed swap fees
- Slipstream V3 differs: `IncreaseLiquidity` has extra `tickLower, tickUpper`; gauges have no `rewardToken()` (emission token = deployment `GaugeRewardToken`, the new AERO `0x3f5b…a4ee`), pending = `earned(owner, uint256[])`, claims = `EmissionsClaimed(account, tokenId, recipient, amount)` / `EmissionsDeferred(account, tokenId, amount)` (deferred counts as claimed)
- NFTs that pass through an intermediary within one tx (Universal Router staking/unstaking, migrators that burn the old NFT) are resolved to the effective counterparty via the tx receipt
- Positions transferred away to a non-gauge address are no longer discovered

## Status

### Done

- [x] Phase 0 — scaffold (solution, projects, Vite app, config, user-secrets, local dotnet-ef tool)
- [x] Phase 1 — domain entities, EF Core context, repositories/UoW, provider switch, initial SQLite migration
- [x] Phase 2 — `UniswapV3StyleProvider` (Uniswap v3 + Aerodrome Slipstream, multi-deployment), `EvmRpcClient` (batching, retry on 429/5xx, adaptive getLogs, receipts with l1Fee, Alchemy NFT transfers), minimal ABI helper (BouncyCastle Keccak)
- [x] Phase 3 — DefiLlama price source + DB-cached `PriceService`
- [x] Phase 4 — `PositionMath`, `CollectSplitter`, `PositionMetricsCalculator` with xUnit tests (40 passing)
- [x] Phase 5 — `SyncService`, `SyncCoordinator`, background service, status endpoint
- [x] Phase 6 — API: addresses CRUD, positions list/detail/snapshots, portfolio summary, weekly fee claims, sync trigger/status (ProblemDetails, OpenAPI at `/openapi/v1.json` in Development)
- [x] Phase 7 — Frontend: Portfolio (summary cards, positions table with range bars, invert toggle, open/closed/all), Position detail (range, P/L breakdown, value vs HODL chart, event history with Basescan links), Fee claims (weekly stacked bar chart + expandable weekly table), Addresses (add/remove, sync status)

### Next

- [ ] Verify against a real Base wallet: position count, ticks, unclaimed fees vs Uniswap/Aerodrome UI; P/L vs Revert/Krystal
- [ ] Verify the Slipstream gauges-v3 `ClaimRewards` event signature (early-unstake penalty changes may have altered events)
- [ ] Postgres migrations assembly (`LpDashboard.Infrastructure.Migrations.Postgres`) + docker-compose
- [ ] Uniswap v4 on Base (PositionManager + StateView; `ModifyLiquidity` events; discovery via Transfer)
- [ ] Ethereum: add a second entry under `Evm:Chains` (config only)
- [ ] Solana (Helius): Orca Whirlpools, Raydium CLMM (position NFT → position PDA), Meteora DLMM (program accounts by owner); history via parsed txs. Consider a Node sidecar if Borsh decoding in .NET is too costly
- [ ] Robinhood Chain once DEXes are known
- [ ] Nice-to-have: daily snapshot backfill (archive RPC), CSV export, per-position notes

## Layout

```
backend/
  LpDashboard.slnx
  src/LpDashboard.Domain/            Entities, Calculations/PositionMath+TokenAmount, Metrics/*
  src/LpDashboard.Application/       Abstractions (repos, IPositionProvider, IPriceSource), Sync/*, Pricing/PriceService, Queries/*, Addresses/AddressParser
  src/LpDashboard.Infrastructure/    Persistence/* (AppDbContext, repositories), Evm/* (Abi, EvmRpcClient, UniswapV3StyleProvider, options), Pricing/DefiLlamaPriceSource, DependencyInjection
  src/LpDashboard.Infrastructure.Migrations.Sqlite/
  src/LpDashboard.Api/               Program.cs, Controllers/*, Background/SyncBackgroundService, appsettings.json
  tests/LpDashboard.Domain.Tests/    math, splitter, metrics, ABI, address parsing
frontend/
  src/api (types + TanStack Query hooks), src/lib (format, owner context), src/components, src/pages
```

## API

| Method   | Path                                             | Notes                                                             |
| -------- | ------------------------------------------------ | ----------------------------------------------------------------- |
| GET/POST | `/api/addresses`                                 | POST `{address,label}`; 400 invalid, 409 duplicate; triggers sync |
| DELETE   | `/api/addresses/{id}`                            |                                                                   |
| GET      | `/api/positions?owner=&status=&protocol=&chain=` | defaults to all watched wallets                                   |
| GET      | `/api/positions/{id}`                            | position + events                                                 |
| GET      | `/api/positions/{id}/snapshots?from=&to=`        | value, value+fees, HODL series                                    |
| GET      | `/api/portfolio/summary?owner=&status=`          | totals                                                            |
| GET      | `/api/fees/claims?owner=&from=&to=&timeZone=`    | weekly buckets                                                    |
| POST     | `/api/sync`                                      | 202, async                                                        |
| GET      | `/api/sync/status`                               | running, last run, providers, errors                              |
