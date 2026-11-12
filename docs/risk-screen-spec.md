# Risk screen: should I put money in this token or pool?

Status: built 2026-11-11 except step 5 (`LP_CONCENTRATION`, which waits for the pool LP index); see "Built, and where it differs from this spec" at the end. Spec written 2026-11-11. Produced from an interview with the owner. Decisions are recorded as decisions; guesses are under **Assumptions** and were not individually confirmed. Companion spec: [smart-lp-spec.md](smart-lp-spec.md) (it supplies the pool LP index this spec uses for one check).

Implementation notes for whoever builds it:
- Follow the repo conventions in `PLAN.md` / `README.md`: pure logic in the C# Domain layer with xUnit tests (no frontend test runner), HTTP and RPC behind interfaces in Application, adapters in Infrastructure, API in `Api/Controllers`, UI in `frontend/src/components/`.
- The user pushes and opens PRs themselves; commit only when asked. Build in the order under "Build order" and **stop after each step** so the user can verify.
- Never log or print the RPC URL or any API key. Never put wallet addresses or other user data in outbound requests: the external sources only ever receive token and pool addresses.
- The root `spec.md` is the built Borrow spec and stays untouched.

## 1. Problem

The owner provides concentrated liquidity on Base (Aerodrome Slipstream, Uniswap v3). The highest APRs are in the long tail, where the risk is the principal, not the APR: honeypots, mintable tokens, one wallet holding most of the supply, one LP able to pull most of a pool. Today nothing in the app says "don't LP here". The same checks are needed after entry, because risk shows up as a change (owner changed, liquidity pulled) before it shows up as a loss.

## 2. Goal and non-goals

Goal: a traffic-light verdict (green, yellow, red, or "not checked") per token and per pool, with the evidence behind it, visible where decisions are made (Pools, Positions, a standalone lookup) and re-checked automatically for what the owner holds, with Telegram alerts when it gets worse.

Non-goals (v1):
- Judging smart-contract code or audits. The screen reads flags and on-chain facts; it does not decompile contracts.
- Chains other than Base (the code keeps `Chain` in every key, so Ethereum can follow).
- Protocol risk of Aerodrome or Uniswap themselves.
- Detecting colluding wallets (clusters). One address is one holder.
- Executing anything. The app stays read-only. The screen informs; it never blocks the Enter planner.
- A numeric risk score. The verdict is the worst finding, and the findings are the product.
- "No flags" never means safe. The UI says so.

## 3. Decisions from the interview

| Topic | Decision |
| --- | --- |
| Spec location | `docs/risk-screen-spec.md` and `docs/smart-lp-spec.md`; root `spec.md` untouched. |
| Where the verdict shows | **All four**: Pools explorer rows and side panel; the owner's open positions (table and detail); a standalone token lookup; Telegram alerts on change. |
| Data sources | **GoPlus Security API, Basescan/Etherscan API, DexScreener/GeckoTerminal**, plus the owner's RPC node (needed for on-chain facts) and honeypot.is (see "Sources checked"). |
| Red findings | Honeypot or failing sell; owner can mint or blacklist; one wallet holds most of the supply; one wallet owns most of the pool. Everything else is yellow at most. |
| Refresh | **Lazy plus nightly for held and starred**: check a token when its pool is opened or it is looked up, cache ~24 h; re-check tokens the owner holds or has starred every night. Rows without a check show "not checked". |
| Telegram events | New or worse risk flag on a token held; pool liquidity drop. (Gauge killed was offered and **not** chosen: it is a yellow finding with no alert.) |

## 4. Sources checked (2026-11-11)

Probed from the build machine with a plain HTTP call, no key:

| Source | Result | Use |
| --- | --- | --- |
| GoPlus `GET https://api.gopluslabs.io/api/v1/token_security/8453?contract_addresses=…` | Works for Base without a key. Returns creator, creator share, holder count, top-10 holders with share and `is_contract`/`is_locked`, tax fields and the security flags. Published free limits (150 CU/min, 30K CU/day, batch calls paid) are from their site and were **not** verified. | Primary flag source. |
| honeypot.is `GET https://api.honeypot.is/v2/IsHoneypot?address=…&chainID=8453` | Works for Base without a key. Returns `honeypotResult`, simulated buy/sell tax and a `summary.risk`. | Sell simulation. Second opinion to GoPlus; either one saying honeypot makes it red. |
| DexScreener `GET https://api.dexscreener.com/latest/dex/pairs/base/{pool}` (and `/tokens/{address}`) | Works without a key. Returns pair age (`pairCreatedAt`), liquidity USD, volume and buy/sell counts per window. It has **no unique-trader count**. | Pool age, liquidity, wash-trading heuristic. |
| GeckoTerminal | Not reachable from the probe (network error). Unverified. | Optional fallback for DexScreener. Do not depend on it. |
| Etherscan v2 (`api.etherscan.io/v2/api?chainid=8453`) | Not reachable from the probe, so unverified. Etherscan has announced that its free tier no longer covers every chain; whether Base is still included must be checked on their supported-chains page. | Source-verified flag and contract creation date. **Optional**: needs a key (`Risk:Etherscan:ApiKey`). Without it, use GoPlus `is_open_source` and DexScreener pair age. |

Everything external sits behind an interface (`ITokenSecuritySource`, `IMarketFactsSource`, `IContractInfoSource`), so a dead or paid-only source degrades to "check unavailable" and never to a wrong verdict.

## 5. What gets checked

A finding has a stable `Code`, a severity (`Red`, `Yellow`, `Info`), a title, an evidence string (the numbers) and a source. All thresholds live in `RiskOptions` (config section `Risk`) so they can be tuned without code changes.

### Token findings

| Code | Severity | Rule (default) | Source |
| --- | --- | --- | --- |
| `HONEYPOT` | Red | honeypot.is says honeypot or the sell simulation fails, or GoPlus `is_honeypot = 1`, or sell tax > 10% | honeypot.is, GoPlus |
| `PRIVILEGED_MINT_OR_BLACKLIST` | Red when controlled by an EOA, Yellow when controlled by a contract | Token can mint (`is_mintable`) or blacklist/pause transfers, and the controlling address is not the zero address. Controller = `owner()`, or the EIP-1967 admin for a proxy. Controlled by code (`eth_getCode` non-empty) is assumed to be a multisig or timelock: Yellow. Renounced: no finding. | GoPlus, RPC |
| `TOP_HOLDER_CONCENTRATION` | Red | Largest holder above 30% of supply, or creator above 20%, after excluding: pool contracts, burn addresses (`0x0`, `0xdead`), the token itself, and holders GoPlus marks `is_locked`. Only the top 10 holders are available. | GoPlus |
| `TOP10_CONCENTRATION` | Yellow | Top 10 (same exclusions) above 50% | GoPlus |
| `SELL_TAX` / `BUY_TAX` | Yellow | Between 3% and 10% | honeypot.is, GoPlus |
| `UNVERIFIED_SOURCE` | Yellow | Contract source not verified | GoPlus (or Etherscan if keyed) |
| `UPGRADEABLE_PROXY` | Yellow | EIP-1967 implementation slot set. Evidence includes the admin address and whether it is a contract. | RPC |
| `OWNER_NOT_RENOUNCED` | Info | An owner exists but nothing above applies | RPC |
| `TOKEN_YOUNG` | Yellow | Token or its first pool is under 7 days old | DexScreener, Etherscan |
| `THIN_LIQUIDITY` | Yellow | Liquidity under $100k across the token's pools | DexScreener |
| `WASH_SUSPECT` | Yellow | 24 h volume above 10× liquidity with under 100 trades. A heuristic: DexScreener has no unique-trader count. | DexScreener |
| `PRICE_HISTORY_MISSING` | Yellow | DefiLlama has no price history for the token (already seen on the new AERO token: P&L and time in range are estimates) | DefiLlama (existing `PriceService`) |
| `STABLE_DEPEG` | Yellow | A token classified as a stablecoin (`AssetClassifier`) priced more than 1% from $1 | DefiLlama |
| `SOURCE_UNAVAILABLE` | Info | A source failed or was rate limited; names which checks did not run | any |

### Pool findings

| Code | Severity | Rule (default) | Source |
| --- | --- | --- | --- |
| `LP_CONCENTRATION` | Red at ≥ 50%, Yellow at ≥ 30% | One wallet's share of the pool's liquidity active at the current tick. Computed from the pool LP index (see smart-lp-spec.md). Shows "partial history" when the index does not reach the pool's first block. | Pool LP index |
| `POOL_YOUNG` | Yellow | Pool under 7 days old | DexScreener, factory logs |
| `EMISSION_DEPENDENT` | Yellow | More than 80% of the pool's APR is gauge emissions (existing `FeeApr7d` vs `RewardApr`) | Pool explorer |
| `GAUGE_KILLED` | Yellow | Aerodrome voter reports the pool's gauge not alive (`isAlive(gauge)`) | RPC |
| plus the worst of the pool's two tokens' findings | | | |

### Trusted tokens

`Risk:TrustedTokens` is a config list of well-known Base tokens (WETH, USDC, cbBTC, AERO, …; the default list is set when building and reviewed with the owner). Tokens on it are exempt from the privilege rules above (USDC can blacklist by design) and show "trusted token (config list)" as an Info finding. Honeypot and concentration checks still run. Without this list nearly every major token would show red.

### Verdict

- **Red** if any Red finding. **Yellow** if any Yellow finding. **Green** if every required check ran and none fired. A required check is the GoPlus or honeypot.is call and the RPC facts.
- **Not checked** (grey) if no check exists, **Partial** (grey with a count) if required sources failed: "6 of 9 checks ran".
- A pool's verdict is the worst of its two token verdicts and its own pool findings.

## 6. Data model (EF Core, one migration; SQLite and Postgres)

- `TokenRiskCheck`: `Id`, `Chain`, `TokenAddress`, `CheckedAt`, `Level`, `FindingsJson`, `FactsJson` (raw numbers used, so a rule change can re-evaluate stored facts), `SourcesJson` (which sources answered). The latest row per token is the current verdict; keep the last 30 per token for change detection and the "recent changes" list.
- `PoolRiskCheck`: same shape, keyed by `Chain` + `PoolAddress`.
- `PoolLiquiditySnapshot`: `Chain`, `PoolAddress`, `At`, `Token0Raw`, `Token1Raw` (on-chain balances of the pool, from `IPositionProvider.GetTokenBalancesAsync`); kept 30 days. Raw amounts, not USD, so price moves do not look like liquidity moves.
- `StarredPool`: `Chain`, `PoolAddress`, `CreatedAt`. There is no star feature today; this adds a minimal one (a star toggle in the Pools table and panel) because "starred" is part of the nightly scope.
- `RiskAlertState`: `Chain`, `TokenAddress` or `PoolAddress`, `Code`, `FirstAlertedAt`, `ClearedAt`: the de-duplication record for alerts.

## 7. Backend design

- `Domain/Risk/`: `RiskFacts` (plain record of everything collected for a token or pool), `RiskFinding`, `RiskLevel`, `RiskEvaluator.Evaluate(facts, options)` (pure, no I/O) and `RiskChangeDetector.Compare(previous, current)` (pure). Everything testable.
- `Application/Risk/`: `RiskService` (get cached, refresh, collect facts from sources, persist, return DTO), `RiskFactCollector` (runs sources in parallel with a per-source timeout, tolerates failures), `RiskAlertService`, `RiskOptions`.
- `Infrastructure/Risk/`: `GoPlusSource`, `HoneypotIsSource`, `DexScreenerSource`, `EtherscanSource` (disabled without a key), RPC facts on `EvmRpcClient` (storage slot read, `eth_getCode`, `owner()` call). Each source: typed `HttpClient`, 10 s timeout, a token-bucket limiter (default 20 requests/min for GoPlus), 429 retry with backoff, never throws for provider failures (returns "unavailable").
- `Api/Background/RiskBackgroundService`: nightly at `Risk:NightlyUtcHour` (default 3) re-checks tokens of every open position of every non-followed watched wallet plus starred pools' tokens; every `Risk:LiquidityIntervalMinutes` (default 60) writes `PoolLiquiditySnapshot` for held and starred pools and evaluates the liquidity-drop alert. Both run after, and never inside, the position sync, so a slow API cannot delay a sync.
- Concurrency and quota: at most 2 token checks in flight; manual refresh of one token has a 5-minute cooldown; the lazy check on opening a pool is skipped when a result younger than `Risk:CacheHours` (24) exists.

## 8. API

- `GET /api/risk/tokens/{chain}/{address}?refresh=false`: `TokenRiskDto` (level, findings with evidence and source link, checkedAt, sources answered/failed, trusted flag). `refresh=true` forces a new check subject to the cooldown (429 with `Retry-After` otherwise).
- `GET /api/risk/pools/{poolId}?refresh=false`: `PoolRiskDto` (pool level, pool findings, both token verdicts). `poolId` is the id format `PoolDto` already uses.
- `GET /api/risk/recent`: last N changes (finding appeared, cleared, level changed) across held and starred tokens, for the Risk page.
- `PoolDto` gains `RiskLevel?` and `Starred`; the position DTOs gain `RiskLevel?`. These come from the **cache only**: listing pools or positions never triggers an external call.
- `PUT /api/pools/{poolId}/star`, `DELETE /api/pools/{poolId}/star`.
- Auth is the existing single-password session. The `owner` convention applies where wallets are involved (`GET /api/risk/held?owner=` lists verdicts for the tokens that owner holds).

## 9. Alerts (Telegram through `IAlertNotifier`)

- **Risk got worse on a held token**: level increased, or any of `HONEYPOT`, `PRIVILEGED_MINT_OR_BLACKLIST`, `UPGRADEABLE_PROXY` appeared, or the controller or implementation address changed, or `TOP_HOLDER_CONCENTRATION` newly fired or the top holder's share rose by more than 10 points. Evidence in the message (before and after). The first check of a token never alerts (same rule as range alerts).
- **Pool liquidity drop**: for a held pool, the pool's token balances valued at *current* prices are down more than 30% against the snapshot ~24 h earlier, or `LP_CONCENTRATION` newly reached Red. One alert per pool per 24 h; re-armed when it clears.
- De-duplication: one alert per (token or pool, code) until it clears (`RiskAlertState`). Sent only after the new check is persisted, so a failed run cannot repeat an alert (same pattern as `SyncService.SendAlertsAsync`). Delivery failures are logged, never fatal.

## 10. Frontend

- `components/risk/RiskBadge.tsx`: dot and word (Green, Yellow, Red, "Not checked", "Partial 6/9"); tooltip lists the top findings. Colour is never the only signal.
- `components/risk/RiskReport.tsx`: findings grouped Red, Yellow, Info with evidence text, source name and link (honeypot.is page, DexScreener page, Basescan address), `checkedAt` ("checked 3 h ago", via `UpdatedAgo`) and a Recheck button (shows the cooldown). Footer: "No flags is not a guarantee of safety."
- **Pools**: a Risk column (sortable, filterable by level) and a star column; the side panel gets a "Risk check" section **above** "Enter this range", which runs the lazy check on open and shows a skeleton while it runs. The check never blocks the Enter planner; a red verdict shows a banner on the planner only.
- **Positions** table and detail: a Risk column and a section in the detail drawer.
- **Risk page** (`/risk`, nav link after P&L): a lookup box (paste a token address, press Enter, get `RiskReport`), a table of held and starred tokens with their verdicts, and the "recent changes" list. Lookup of an unknown token works for any ERC-20 on Base.
- Respect the existing "hide balances" toggle where USD values appear; follow the existing light/dark theme tokens.

## 11. Assumptions I made (not confirmed one by one)

1. Thresholds in section 5 (30% / 20% top holder, 50% LP, 10% tax, $100k liquidity, 7 days) are starting points; they are config.
2. EOA-controlled mint/blacklist is Red and contract-controlled is Yellow, with a config trusted-token list. This rule is mine; the interview only said "owner can mint or blacklist".
3. `TOP_HOLDER_CONCENTRATION` uses only GoPlus's top-10 list and excludes pools and locked holders; holders it fails to tag as pools can cause false reds on new tokens, which is why the evidence names the holder address.
4. Nightly time 03:00 UTC, liquidity snapshot hourly, 24 h cache, 30-day snapshot retention.
5. `LP_CONCENTRATION` comes from the pool LP index, whose depth is the scan window defined in smart-lp-spec.md. When the index is partial the finding says so rather than reading "all clear".
6. Etherscan is optional because its Base coverage on a free key is unconfirmed.
7. Followed wallets (other people) are excluded from the "tokens I hold" set.

## 12. Tests (xUnit, no live network)

- `RiskEvaluatorTests`: one case per finding code at and either side of its threshold; trusted-token exemption; verdict combination; partial sources produce "Partial", never "Green".
- Source adapters parse **recorded JSON fixtures** (a trimmed real GoPlus, honeypot.is and DexScreener response each, plus a malformed one and an empty one); no test calls the network.
- `RiskChangeDetectorTests`: appeared, worsened, cleared, first check, controller change.
- `RiskAlertServiceTests`: de-duplication, first check silent, re-arm after clear, delivery failure does not repeat (pattern of `SyncServiceTests`: fake provider, temp SQLite).
- `PoolLiquidityTests`: price moves alone do not trigger the drop alert; a 31% raw drop does.
- Service-level: cache hit does no HTTP; cooldown returns 429; a source timeout degrades to Partial.

## 13. Build order (stop and ask the user to verify after each)

1. **Domain**: `RiskFacts`, `RiskEvaluator`, `RiskChangeDetector`, `RiskOptions` with tests. Check: the findings table above matches the code.
2. **Sources and service**: GoPlus, honeypot.is, DexScreener, RPC facts, persistence and migration, `GET /api/risk/tokens/...`. Check against 3 to 5 tokens the owner knows (a blue chip, AERO, a long-tail token, a known bad one); report which checks ran.
3. **UI**: badge, report, Pools column and panel section, star toggle, Positions column, Risk page with lookup.
4. **Monitoring**: nightly job, hourly liquidity snapshots, change detection, Telegram alerts. Check with a forced re-check and a synthetic change in a scratch DB.
5. **Pool LP concentration** (`LP_CONCENTRATION`): lands after the pool LP index from smart-lp-spec.md step S1 exists; until then the finding reads "not checked".

## 14. Open questions and risks

- GoPlus Base coverage worked in the probe, but the free limits and batch rules were not verified; check the response headers on first use. If the limit bites, the nightly job spreads checks across the night.
- Etherscan's free tier and Base: confirm before relying on it.
- Whether the owner wants `PRICE_HISTORY_MISSING` and `STABLE_DEPEG` shown on tokens that already appear in the P&L pages (they overlap with existing warnings).
- A verdict is only as good as the sources; two services reading the same contract can agree and both be wrong on a brand new scam. The report says which sources answered.

## 15. Built, and where it differs from this spec (2026-11-11)

Steps 1 to 4 are built; step 5 (`LP_CONCENTRATION`) reads "not checked" until the pool LP index (smart-lp-spec.md, S1) exists. Differences and findings from building:

- **Where things live.** `RiskOptions` (thresholds, trusted tokens) is in `Domain/Risk/` so the evaluator stays pure; the operational settings (cache, cooldown, schedule, rate limits, Etherscan key) are `Application/Risk/RiskSettings.cs`. Both bind to the `Risk` section.
- **One migration, four tables** (`AddRiskScreen`, new tables only): `RiskChecks` (one table for token and pool checks, with `Kind`, `IsLatest`, `ChangesJson` and, for pools, the explorer id), `StarredPools`, `PoolLiquiditySnapshots`, `RiskAlertStates`. Stars are keyed by the pool explorer's id, because that id has no contract address; the address is filled in the first time the pool is resolved on-chain.
- **Required sources** are GoPlus, honeypot.is and the RPC reads; any of them missing makes a clean result "Partial", never Green. DexScreener, prices and Etherscan are optional (their absence is an Info finding).
- **GoPlus partial answers.** For some tokens (seen on a Uniswap v4 token) GoPlus answers `code: 3` with a result that has no `is_honeypot` flag. The facts are kept, the check counts as not run ("partial data"). GoPlus's `dex[].pool_manager` (the Uniswap v4 PoolManager) counts as a pool, so it is not reported as a whale.
- **Trusted tokens and contract holders.** A contract holding more than the top-holder limit of a *trusted* token is Yellow, not Red (AERO: 49.9% sits in an untagged contract, probably the vote escrow). Anything else, and every non-trusted token, follows the spec.
- **Gaps in the spec, decided here:** mint/blacklist/pause with a controller that cannot be identified (role-based access, or the RPC read failed) is Yellow; a buy tax above 10% is Yellow (only a sell tax above 10% is a honeypot); EIP-7702 delegated accounts count as externally owned.
- **Lists show the cache only.** A pool row is the worst of its two tokens' cached verdicts (and of the pool's own check once it was opened); with one token never checked, a clean result reads "Not checked" and only a Red or Yellow shows.
- **Alerts** are keyed per (subject, code); a severity raise, a new controller or implementation address and a new level each count as news. The alert record is written before the message is sent, so neither a failed run nor a failed delivery repeats an alert. Only held tokens alert; starred ones are checked and recorded.
- **Nightly scope** is every open position of every watched wallet plus starred pools. The data has no "own vs followed wallet" distinction, so assumption 7 is not implemented. A catch-up run two minutes after startup re-checks only tokens older than the cache window, so a container that was off at 03:00 UTC still catches up. A starred pool's own findings are refreshed nightly; a held pool's when it is opened.
- **Source limits found on first use (2026-11-11).** GoPlus sends no rate-limit headers (only Cloudflare ones), so the 20 requests/minute limiter is a configured guess. honeypot.is sends `x-ratelimit-limit: 50` (window unknown); DexScreener sends none. Etherscan v2 could not be reached from the build machine, so its parser is tested on synthetic fixtures and whether a free key covers Base is still unconfirmed.
- **Fixtures** are in `backend/tests/LpDashboard.Domain.Tests/Fixtures/risk/`: `*_aero`, `*_weth` and `goplus_v4_partial` are trimmed real answers; `*_synthetic` are hand-made variants.
- **The default trusted list** (WETH, USDC, USDbC, DAI, cbBTC, cbETH, wstETH, both AERO) is mine and should be reviewed.
