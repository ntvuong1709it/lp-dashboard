import type { EntryPlanDto, PoolDto, PoolTokenDto, WalletBalancesDto } from "../api/types";
import { tokenSymbols } from "./pools";

/* Display helpers for the enter-pool planner. The maths (swap, amounts, hedge, scenarios) is on the server in
   Domain/Metrics/EntryPlanner.cs and HedgePlanner.cs; nothing here recomputes it. */

export const MIN_LOWER = 0.1;
export const MAX_LOWER = 99;
export const MIN_UPPER = 0.1;
export const MAX_UPPER = 500;

/** Native ETH kept back for gas when it is counted as WETH. */
export const GAS_RESERVE_ETH = 0.002;

export const clampLower = (v: number) => Math.min(MAX_LOWER, Math.max(MIN_LOWER, Math.round(v * 10) / 10));
export const clampUpper = (v: number) => Math.min(MAX_UPPER, Math.max(MIN_UPPER, Math.round(v * 10) / 10));

/**
 * Half-width (percent) of the symmetric range with the same upper/lower price ratio as ±lower/+upper, which is what the
 * APR estimate is asked for when the two bounds differ. Equal bounds give back that bound.
 */
export function equivalentHalfWidth(lower: number, upper: number) {
  const l = lower / 100;
  const h = upper / 100;
  return (100 * (h + l)) / (2 + h - l);
}

const nf = (v: number, min: number, max = min) =>
  v.toLocaleString("en-US", { minimumFractionDigits: min, maximumFractionDigits: max });

/** Token amount with more decimals the smaller it is. */
export const amt = (v: number) => nf(v, v >= 100 ? 2 : v >= 1 ? 4 : v >= 0.01 ? 6 : 8);
/** Price with more decimals the smaller it is. */
export const qp = (v: number) =>
  v >= 100 ? nf(v, 2) : v >= 1 ? nf(v, 4) : v >= 0.01 ? nf(v, 6) : v.toLocaleString("en-US", { maximumSignificantDigits: 4, maximumFractionDigits: 12 });
export const usd0 = (v: number) => `${v < 0 ? "-" : ""}$${nf(Math.abs(v), 0)}`;
export const usd2 = (v: number) => `${v < 0 ? "-" : ""}$${nf(Math.abs(v), 2)}`;
export const pct = (v: number, d = 1) => `${nf(v, d)}%`;
export const signedPct = (v: number, d = 2) => `${v > 0 ? "+" : v < 0 ? "-" : ""}${nf(Math.abs(v), d)}%`;
/** "+$310" / "-$285", rounded to whole dollars. */
export const signedUsd0 = (v: number) => {
  const r = Math.round(v);
  return `${r > 0 ? "+" : r < 0 ? "-" : ""}$${nf(Math.abs(r), 0)}`;
};
export const gainClass = (v: number) => (Math.round(v) > 0 ? "text-emerald-400" : Math.round(v) < 0 ? "text-rose-400" : "text-slate-300");

/** The pool's two tokens in the order the plan uses; before the first answer, the list's own order. */
export function poolTokens(pool: PoolDto, plan: EntryPlanDto | undefined): [PoolTokenDto, PoolTokenDto] {
  if (plan) return [plan.pool.token0, plan.pool.token1];
  const [a, b] = tokenSymbols(pool.symbol);
  return [
    { address: pool.token0.toLowerCase(), symbol: a, decimals: 18 },
    { address: pool.token1.toLowerCase(), symbol: b, decimals: 18 },
  ];
}

const toUnits = (raw: string | null | undefined, decimals: number) => {
  if (raw == null) return null;
  try {
    const whole = BigInt(raw);
    const base = 10n ** BigInt(decimals);
    return Number(whole / base) + Number(whole % base) / Number(base);
  } catch {
    return null;
  }
};

export interface WalletHolding {
  token: PoolTokenDto;
  amount: number;
  /** Native ETH added to a WETH balance, before the gas reserve is taken off. */
  nativeAdded: number;
}

/**
 * What a wallet can put into the pool, per token. Native ETH counts as WETH (minus a small reserve for gas), since the
 * pool's WETH is what a swap would produce from it. Tokens whose balance could not be read are left out.
 */
export function walletHoldings(balances: WalletBalancesDto, tokens: PoolTokenDto[]): WalletHolding[] {
  const native = toUnits(balances.nativeRaw, 18);
  return tokens.flatMap((token) => {
    const entry = balances.balances.find((b) => b.token.toLowerCase() === token.address.toLowerCase());
    const base = toUnits(entry?.raw, token.decimals);
    if (base == null) return [];
    const isWeth = token.symbol.toUpperCase() === "WETH";
    const extra = isWeth && native != null ? Math.max(0, native - GAS_RESERVE_ETH) : 0;
    return [{ token, amount: base + extra, nativeAdded: extra }];
  });
}
