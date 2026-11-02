# LP Ledger: build status

Updated 2026-11-02. Plan: [docs/PLAN.md](docs/PLAN.md). Legend: [x] done and verified, [~] built but not verified or partial, [ ] not started.

## Build order (PLAN section 7)

| # | Step | State | Notes |
|---|---|---|---|
| 1 | Solution skeleton, normalized `Position`, PnL math + tests | [x] | .NET 9 solution, 16 xUnit tests pass (math, PnL, adapter on a fake chain, ABI helpers, demo wallet) |
| 2 | Aerodrome adapter vs a known wallet | [~] | Runs on real Base data for one wallet (17 positions, about 52 s for the first lookup on Alchemy Free). Open position #1167 (value, range, pending AERO) confirmed against the Aero app by the owner on 2026-11-02. **Closed positions' deposits and PnL not yet checked** |
| 3 | API endpoint + React app wired to it | [x] | `GET /api/wallets/{address}/positions`, React/Vite app, address input, loading and error states. Checked in the browser with demo data only |
| 4 | Postgres: store events, scan only new blocks | [~] | Built and unit-tested (fake node, SQLite): scan cursors, NFT transfers, tx hashes, decoded receipts and block times are stored, and a refresh queries only blocks after the cursor. **Not run against real Postgres or Alchemy.** Tables come from `EnsureCreated`, no EF migrations yet. Daily snapshots and price cache are still unbuilt |
| 5 | Claimed AERO and gas tracking | [ ] | Until done, every position carries an "incomplete" marker and the UI shows a banner |
| 6 | Daily snapshot job and real history chart | [ ] | Overview "Portfolio history" is a placeholder |
| 7 | Meteora adapter | [ ] | |
| 8 | Uniswap adapter | [ ] | Open question: chains and v3 vs v4 |
| 9 | Later: alerts, CSV export, ETH returns, multi-wallet, pool explorer | [ ] | |

## Verify Aerodrome first (blocks trusting any number)

- [x] RPC: Alchemy Free caps `getLogs` at 10 blocks, so the reader does not scan logs. It uses Alchemy's transfer index to find NFTs and transaction hashes, then decodes events from receipts (best-effort, see Backend gaps)
- [x] Confirmed on-chain: position managers `0x4857…` (current), `0xe1f8…`, `0x8279…`; the 6-field `IncreaseLiquidity` on the current manager; gauge `stakedContains`; `earned(address,uint256[])`; `getPool`
- [ ] Run against a wallet you know and compare value, range, fees and PnL with the Aerodrome UI
- [ ] DefiLlama price coverage for the tokens involved (no fallback or "unpriced" state yet: a missing price currently drops that position with a warning)

## What the dashboard shows today

Runs on **demo data** (a simulated chain, not market data) when `Base:RpcUrl` is not configured. The banner says so.

| Page | State |
|---|---|
| Landing / address input | [x] |
| Overview: KPIs, alerts, allocation, staked vs unstaked, fees-vs-AERO label | [x] |
| Overview: portfolio history chart, 24h change, blended APR | [ ] needs step 6 |
| Positions table, sorting, status filter, detail drawer | [x] |
| Profit & loss: breakdown, realized/unrealized, by position | [x] |
| Profit & loss: monthly net PnL and monthly fees charts, ETH basis | [ ] needs steps 4 and 6, phase 2 |
| Activity (add / remove / collect) | [x] |
| Features | [x] static list |
| Dark mode, mobile layout | [~] ported from the prototype, mobile not checked |
| Out-of-range alert toggle | [ ] phase 2 |

## Backend gaps

- [ ] **History is best-effort on the free tier.** Events come from receipts of NFT transfers, ERC-20 moves of the pair's tokens to or from the wallet, and value-bearing sends. Missed: liquidity changes with no such footprint, and ETH (not WETH) paid or returned through the router. Fix: Alchemy Pay As You Go (wide `getLogs`) or the database step
- [ ] Closed positions that were once staked show as unstaked with trading fees, and their claimed AERO is missing
- [~] Decoded receipts are stored in Postgres when `ConnectionStrings__Ledger` is set (in memory otherwise, lost on restart). The first lookup of a busy wallet still takes about a minute; later ones read only new blocks
- [ ] Request pacing is fixed at 300 compute units per second (`AlchemyClient`); tune for the plan in use
- [ ] Claimed AERO history (gauge `ClaimRewards` events) and valuation choice (at claim time or today's price: open question)
- [ ] Gas per transaction including Base L1 data fee
- [ ] Burned NFTs (pool unknown, reported as a warning) and Aerodrome v1 basic pools
- [x] Resilience handler on the RPC client, with the circuit breaker relaxed for bursts of free-tier 429s
- [ ] Cancellation tokens are not threaded through `IChainReader`
- [ ] Per-wallet result cache is in memory for 60 seconds; the database replaces it in step 4

## Deployment (PLAN section 8)

- [~] `src/LpLedger.Api/Dockerfile` written, **not built or run**
- [ ] Azure: budget alert (about $40) first, Container Apps (min 0, max 1), Postgres B1ms, Static Web Apps. See `infra/AZURE.md` (still the spike's text; update for .NET)
- [ ] CORS origins for the Static Web Apps domain (`Cors:Origins`), `VITE_API_URL` for the frontend build
- [ ] Check what the $50 subscription actually is

## Open questions (PLAN section 9)

- Which Uniswap chains and versions?
- ETH-denominated returns in the first version?
- Claimed AERO valued at claim time or today's price?
- Staked rewards shown separately from trading fees on the Overview totals? (Currently shown separately in the "Staked vs unstaked" card, combined in the KPI.)
- Hosting region, custom domain?
