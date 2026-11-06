import type { PoolDto, PositionDto, Protocol } from "../api/types";

/** Deposit size the estimates assume until the user types their own. */
export const DEFAULT_CAPITAL = 5000;

/* ---------- formatting ---------- */

const compactUsd = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
  notation: "compact",
  maximumFractionDigits: 1,
});
const cents = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", minimumFractionDigits: 2, maximumFractionDigits: 2 });
const dollars = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", maximumFractionDigits: 0 });

/** $1.2M style; "—" when there is no value. */
export const usd = (v: number | null | undefined) => (v == null ? "—" : compactUsd.format(v));
export const fullUsd = (v: number) => dollars.format(v);
export const usdCents = (v: number) => cents.format(v);
export const apr = (v: number | null | undefined) => (v == null ? "—" : `${v >= 1000 ? v.toFixed(0) : v.toFixed(1)}%`);
export const feeLabel = (f: number | null) => (f == null ? "—" : `${Number(f.toFixed(4))}%`);
export const priceLabel = (v: number) =>
  v >= 1000
    ? v.toLocaleString("en-US", { maximumFractionDigits: 0 })
    : v >= 1
      ? v.toFixed(2)
      : v >= 0.01
        ? v.toFixed(4)
        : v.toPrecision(3);

/** The pool's swap-fee rate: 7-day when it has volume for it, else 24h. */
export const poolRate = (p: PoolDto) => p.feeApr7d ?? p.feeApr1d;

/** Swap-fee rate plus the gauge's emission rate, the number a staker sees. */
export const totalRate = (p: PoolDto) => {
  const fee = poolRate(p);
  return fee == null && !p.rewardApr ? null : (fee ?? 0) + (p.rewardApr ?? 0);
};

/** "WETH-USDC" → ["WETH", "USDC"]. Symbols come from the analytics source as one string. */
export const tokenSymbols = (symbol: string): [string, string] => {
  const [a, b] = symbol.split("-");
  return [a ?? symbol, b ?? ""];
};

/* ---------- data quality ---------- */

/**
 * DefiLlama's volume and fee APR for Aerodrome Slipstream pools have been far below what Aerodrome, DexScreener and
 * GeckoTerminal show (about 30× for WETH-USDC CL50, and a 3.5% fee APR against Aerodrome's 301% for CL100), so earnings
 * and APRs for these pools are likely understated. There is no second source wired in, so the rows are flagged instead.
 */
export const hasUnreliableVolume = (p: PoolDto) => p.protocol === "AerodromeSlipstream";

export const UNRELIABLE_VOLUME_NOTE =
  "The data source's volume for Aerodrome pools is often far too low, so this pool's earnings and APRs, and the estimate for a range, are likely understated. Check the pool's APR on Aerodrome before relying on them.";

/* ---------- matching pools to positions ---------- */

/** Uniswap pools are told apart by token pair and fee, Slipstream pools by token pair and tick spacing. */
export function matchKey(protocol: Protocol, a: string, b: string, fee: number | null, tickSpacing: number | null) {
  const [lo, hi] = [a.toLowerCase(), b.toLowerCase()].sort();
  const tier = protocol === "AerodromeSlipstream" ? `ts${tickSpacing}` : `fee${fee}`;
  return `${protocol}|${lo}|${hi}|${tier}`;
}

export function poolKey(p: PoolDto) {
  return matchKey(p.protocol, p.token0, p.token1, p.feePercent == null ? null : Math.round(p.feePercent * 10000), p.tickSpacing);
}

/** Realised fee APR of the first open position in each pool, keyed like poolKey. */
export function realisedByPool(positions: PositionDto[] | undefined) {
  const map = new Map<string, number>();
  for (const p of positions ?? []) {
    if (!p.token0 || !p.token1 || p.metrics.feeAprPercent == null) continue;
    const key = matchKey(p.protocol, p.token0.address, p.token1.address, p.fee, p.tickSpacing);
    if (!map.has(key)) map.set(key, p.metrics.feeAprPercent);
  }
  return map;
}

/** What a staked position's gauge pays: reward tokens per day per dollar deposited, valued at today's token price. */
export interface Emissions {
  symbol: string;
  /** Reward tokens earned per day for each dollar deposited (claimed plus pending, over the position's age). */
  tokensPerDayPerUsd: number;
  priceUsd: number;
  apr: number;
}

/**
 * The first open staked position in each pool. The pool source has no emission rate for most pools, so the rate this
 * position actually earned, in tokens, is the best figure there is. Tokens, not dollars, so it can be valued at today's price.
 */
export function emissionsByPool(positions: PositionDto[] | undefined) {
  const map = new Map<string, Emissions>();
  for (const p of positions ?? []) {
    const m = p.metrics;
    const price = m.rewardPriceUsd;
    if (!p.isStaked || !p.token0 || !p.token1 || !m.ageDays || m.ageDays < 1 || m.depositedUsd <= 0 || !price) continue;
    const key = matchKey(p.protocol, p.token0.address, p.token1.address, p.fee, p.tickSpacing);
    if (map.has(key)) continue;
    const tokensPerDayPerUsd = (m.claimedRewardAmount + p.pendingReward) / m.ageDays / m.depositedUsd;
    map.set(key, { symbol: p.rewardTokenSymbol ?? "AERO", tokensPerDayPerUsd, priceUsd: price, apr: tokensPerDayPerUsd * price * 365 * 100 });
  }
  return map;
}

/* ---------- asset classes ---------- */

type Family = "usd" | "eth" | "btc";

const FAMILIES: Record<Family, ReadonlySet<string>> = {
  usd: new Set([
    "USDC", "USDT", "DAI", "USDBC", "USDE", "SUSDE", "USDS", "SUSDS", "EURC", "CRVUSD", "LUSD", "FRAX", "GHO", "USD+",
    "DOLA", "AUSD", "USR", "USDZ", "PYUSD", "USDL", "FDUSD", "TUSD", "USDD", "AXLUSDC", "USDT0", "USDM",
  ]),
  eth: new Set([
    "WETH", "ETH", "CBETH", "WSTETH", "STETH", "WEETH", "EETH", "RETH", "EZETH", "WRSETH", "OETH", "SFRXETH", "METH",
    "RSETH", "SUPEROETHB", "WSUPEROETHB", "AXLETH",
  ]),
  btc: new Set(["WBTC", "CBBTC", "TBTC", "LBTC", "EBTC", "BTC.B", "SOLVBTC", "UNIBTC", "CBBTC.E", "BTCB"]),
};

function familyOf(symbol: string): Family | null {
  const s = symbol.trim().toUpperCase();
  for (const f of Object.keys(FAMILIES) as Family[]) if (FAMILIES[f].has(s)) return f;
  return null;
}

/** Both tokens are dollar stablecoins. */
export const isStablePool = (p: PoolDto) => {
  const [a, b] = tokenSymbols(p.symbol);
  return familyOf(a) === "usd" && familyOf(b) === "usd";
};

/** Both tokens track the same asset (stablecoins, ETH and its liquid staking wrappers, BTC wrappers), so the price ratio barely moves. */
export const isCorrelatedPool = (p: PoolDto) => {
  const [a, b] = tokenSymbols(p.symbol);
  const fa = familyOf(a);
  return fa != null && fa === familyOf(b);
};

export const hasExtraRewards = (p: PoolDto) => (p.rewardApr ?? 0) > 0;

/* ---------- filtering and sorting ---------- */

export interface PoolFilters {
  query: string;
  protocol: Protocol | null;
  stable: boolean;
  extraRewards: boolean;
  correlated: boolean;
  /** Minimum total APR (swap fees plus emissions), percent. */
  minApr: number;
  /** Maximum fee tier, percent. */
  maxFee: number | null;
}

export const DEFAULT_FILTERS: PoolFilters = {
  query: "",
  protocol: null,
  stable: false,
  extraRewards: false,
  correlated: false,
  minApr: 0,
  maxFee: null,
};

/** How many filters differ from the defaults, for the "Filters" badge. */
export function activeFilterCount(f: PoolFilters) {
  return (
    Number(f.query.trim() !== "") +
    Number(f.protocol != null) +
    Number(f.stable) +
    Number(f.extraRewards) +
    Number(f.correlated) +
    Number(f.minApr > 0) +
    Number(f.maxFee != null)
  );
}

export function matchesFilters(p: PoolDto, f: PoolFilters) {
  const q = f.query.trim().toLowerCase();
  if (q && !`${p.symbol} ${p.token0} ${p.token1}`.toLowerCase().includes(q)) return false;
  if (f.protocol && p.protocol !== f.protocol) return false;
  if (f.stable && !isStablePool(p)) return false;
  if (f.extraRewards && !hasExtraRewards(p)) return false;
  if (f.correlated && !isCorrelatedPool(p)) return false;
  if (f.minApr > 0 && (totalRate(p) ?? -1) < f.minApr) return false;
  if (f.maxFee != null && (p.feePercent == null || p.feePercent > f.maxFee)) return false;
  return true;
}

export type SortKey = "symbol" | "tvlUsd" | "fees7dUsd" | "apr";

export interface Sort {
  key: SortKey;
  dir: 1 | -1;
}

export const DEFAULT_SORT: Sort = { key: "fees7dUsd", dir: -1 };

export const sortLabels: Record<SortKey, string> = {
  symbol: "Name",
  tvlUsd: "TVL",
  fees7dUsd: "7d Earnings",
  apr: "Avg APY",
};

function sortValue(p: PoolDto, key: SortKey): string | number {
  if (key === "symbol") return `${p.symbol} ${p.protocol}`;
  // Pools with no 7-day volume rank by their 24h rate instead of sinking to the bottom.
  if (key === "apr") return totalRate(p) ?? -1;
  return p[key] ?? -1;
}

/** Pools you hold stay on top whatever the sort, so they are easy to find. */
export function sortPools(pools: PoolDto[], sort: Sort) {
  return [...pools].sort((a, b) => {
    if (a.held !== b.held) return a.held ? -1 : 1;
    const x = sortValue(a, sort.key);
    const y = sortValue(b, sort.key);
    return (x < y ? -1 : x > y ? 1 : 0) * sort.dir;
  });
}
