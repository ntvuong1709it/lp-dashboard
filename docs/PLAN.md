# LP Ledger: implementation plan

Status: brainstorming. Nothing here is committed. Written 2026-11-02.

## 1. Goal

A website where I paste a wallet address and see my liquidity pool positions and my profit and loss. Read-only: no wallet connection, no signing, no funds at risk.

I use Uniswap, Aerodrome (Base) and Meteora (Solana). Aerodrome comes first.

## 2. Decisions so far

| Topic | Decision |
|---|---|
| UI | Approved. Static prototype exists (`web/index.html`, mock data). Pages: Overview, Positions (with detail panel), Profit & loss, Activity, Features. |
| Wallet access | Read-only address lookup. |
| Protocol order | Aerodrome (Base), then Uniswap, then Meteora. |
| Backend | Small backend with a database, so history, alerts and exports are possible later. |
| Hosting | Azure, about $50 budget. |
| Backend stack | .NET (ASP.NET Core). |
| Frontend stack | React. |

## 3. Architecture

- **Frontend:** the approved prototype, switched from mock data to the API, plus an address input.
- **API:** one endpoint per wallet returns positions, summary and warnings in one normalized shape.
- **Protocol adapters:** one per protocol (Aerodrome, Uniswap, Meteora). Each returns the same `Position` shape, so the UI never knows which protocol a position came from.
- **Database (Postgres):** immutable event history, daily snapshots, price cache, per-wallet scan progress.
- **Background job:** refreshes watched wallets and writes the daily snapshot (needed for the history chart).
- **Prices:** one price API for current and historical USD prices. DefiLlama's free coins API is the leading candidate.

### Stack

Backend (.NET, current LTS):
- ASP.NET Core minimal API for the wallet endpoint.
- EVM reads (Base now, Uniswap chains later): Nethereum for `getLogs`, contract calls and the simulated `collect`. Use `BigInteger` for uint256 values and `decimal` for USD amounts. Tick and price math can use `double`, which is precise enough for display.
- Meteora and the price API: plain `HttpClient` with typed clients and Polly-style retries (`Microsoft.Extensions.Http.Resilience`).
- Database: PostgreSQL with EF Core (Npgsql provider) and migrations.
- Background refresh and daily snapshots: `BackgroundService`, or a Container Apps job on a schedule.
- Tests: xUnit. PnL and tick math are pure functions, so they get unit tests with no network. Adapters get tests against a fake chain reader (same interface idea as `ChainReader`).
- Config and secrets: `IOptions` plus environment variables, with Container Apps secrets in Azure.

Frontend (React):
- React with TypeScript, built with Vite.
- TanStack Query for fetching and caching API data, React Router for the five pages.
- Charts: the prototype hand-draws SVG. Either port that or use a charting library (Recharts is the simple choice). Keep the same hover and range-selector behaviour.
- API contract: generate the TypeScript types from the .NET API's OpenAPI document, so the normalized position shape is defined once.
- Port the prototype's design tokens (colours, spacing, dark mode) into CSS variables first, then rebuild the pages as components.

### Normalized position (what the UI reads)

id, protocol, chain, pair, state (in range / out of range / closed), price range and current price, entry price, token amounts and USD value, rewards (kind plus unclaimed USD), PnL breakdown, opened and closed dates, event list.

## 4. PnL definitions

- Net PnL = current principal value + everything collected + unclaimed rewards − deposits − gas.
- Deposits and withdrawals are valued in USD at the time they happened.
- Breakdown shown in the UI (sums to net PnL):
  - Token price change = value of tokens if simply held, minus what was deposited.
  - Impermanent loss = pool position value minus hold value.
  - Fees (or emissions) = collected + unclaimed.
  - Gas.
- Hold comparison and ETH-denominated returns are phase 2.

## 5. Protocol notes

### Aerodrome (Base), build first

- Slipstream is a Uniswap v3 fork where positions are NFTs. Staking an NFT into a pool's gauge earns AERO emissions.
- **Staked positions earn AERO, not trading fees.** Trading fees go to veAERO voters. The UI needs a "fees vs AERO rewards" label per position.
- Finding positions: scan Transfer logs on the position manager for the wallet (received and sent). This catches NFTs now held by gauges.
- History: IncreaseLiquidity, DecreaseLiquidity and Collect logs per token ID give deposits, withdrawals and collected amounts.
- Custody check: wallet owns the NFT means unstaked. Gauge owns it, then `stakedContains(wallet, tokenId)` must be true.
- Current amounts: computed from liquidity, tick range and pool price.
- Pending fees (unstaked): simulate `collect` from the owner address. Pending emissions (staked): gauge `earned(wallet, tokenId)`.
- Needs a Base RPC with usable `getLogs` (Alchemy or similar free tier). The public RPC is too limited.
- Verify on Basescan before trusting: position manager address `0x827922686190790b37229fd06084350E74485b72`, event signatures, gauge function names.
- Known gaps: claimed AERO history (gauge ClaimRewards events), gas including Base L1 data fee, burned NFTs (pool unknown), Aerodrome v1 basic pools.

### Uniswap

- Data via subgraphs on The Graph. Hosted subgraphs are deprecated, so this needs a Graph API key. The free tier was 100,000 queries a month when last checked.
- Subgraph gives deposits and collected fees per position. Unclaimed fees and current amounts still need on-chain reads.
- Open question: which chains, and v3 or v4 (v4 now moves more Ethereum volume than v3).

### Meteora (Solana)

- Meteora's DLMM Data API (`dlmm.datapi.meteora.ag`) already returns open and closed positions with PnL in USD and SOL, plus portfolio totals. No key.
- Likely the cheapest adapter. Main work is mapping its fields onto the normalized shape.
- DLMM uses bins, not ticks, so "price range" needs its own mapping for the range bar.

## 6. Database tables (sketch)

- wallets
- position_events (wallet, protocol, chain, token id, kind, timestamp, block, tx hash, log index, amounts, USD, gas USD). Unique on tx hash plus log index.
- daily_snapshots (wallet, day, value, unclaimed, net PnL)
- position_snapshots (wallet, position id, day, value, in range)
- price_cache (chain, token, hour, USD)
- sync_state (wallet, protocol, last block scanned)

## 7. Build order

1. Solution skeleton (API project, core library, test project), normalized position type, PnL math with tests. Math and PnL are pure functions, so they can be tested without network.
2. Aerodrome adapter, run against a wallet I know, compared line by line with the Aerodrome UI.
3. API endpoint, then the React app scaffolded from the prototype's design and wired to it (address input, loading and error states).
4. Postgres: store events, scan only new blocks on refresh.
5. Claimed AERO and gas tracking. Required before PnL for staked positions is trustworthy.
6. Daily snapshot job and the real history chart.
7. Meteora adapter.
8. Uniswap adapter.
9. Later: out-of-range alerts, CSV export for tax, ETH-denominated returns, multiple wallets, pool explorer.

## 8. Azure deployment

- API: Azure Container Apps running the .NET container (multi-stage Dockerfile on the official ASP.NET runtime image), min replicas 0, max 1. Expect a few seconds of cold start on the first request after idle.
- Database: Azure Database for PostgreSQL Flexible Server, Burstable B1ms. Main cost, roughly $12 to $15 a month unless the free 12-month allowance applies.
- Frontend: the Vite build of the React app on Azure Static Web Apps, free plan. Needs CORS on the API for the Static Web Apps domain.
- Set a budget alert (about $40) before creating anything.
- Check what the $50 subscription is. The standard free account's $200 credit expires after 30 days.
- Secrets (RPC key, Graph key) in Container Apps secrets, never in the repo.

## 9. Open questions

- Which Uniswap chains and versions do I use?
- Do I want ETH-denominated returns in the first version?
- How should claimed AERO be valued: at claim time (cost basis style) or at today's price?
- Do I want staked-position rewards shown separately from trading fees on the Overview totals?
- Hosting region and whether any custom domain is wanted.

## 10. Risks

- Event signatures and addresses came from documentation, not a live network. First task is a reality check against a known wallet.
- Price API gaps for small or new tokens. Need a fallback or an "unpriced" state in the UI.
- RPC `getLogs` limits and cost on a full history scan. Mitigation: store scan progress, scan once.
- PnL will be wrong, not just incomplete, if claimed rewards or gas are missing. Show a visible "incomplete" marker until they are tracked.

## 11. Existing spike

A first pass exists in `lp-ledger.zip`: the web prototype, a TypeScript Aerodrome adapter with 11 passing tests (math, PnL, adapter against a simulated chain), a Postgres schema and Azure steps. It has never run against real Base data.

With the move to .NET, the backend code there is a reference to port, not something to reuse. What carries over unchanged: the prototype (`web/index.html`) as the design source for the React app, `db/schema.sql` as the starting table design, and the tests as a list of cases to reproduce (math edge cases, PnL breakdown summing to net, staked vs unstaked, burned and foreign NFTs, closed positions).
