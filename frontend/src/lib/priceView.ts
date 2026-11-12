import { MAX_LOWER, MAX_UPPER, qp } from "./entry";

/**
 * How the range panel shows prices. The pool's own price is token1 per token0, which for a pair like USDC/cbBTC is
 * 0.00001356 cbBTC per USDC. "usd" shows the dollar price of one token instead; "pool" and "inverse" are the two ratios.
 */
export type PriceViewKind = "usd" | "pool" | "inverse";

export interface PriceView {
  kind: PriceViewKind;
  /** Displayed price falls when the pool price rises (the view quotes token0 per token1, or dollars per token1). */
  flip: boolean;
  /** The displayed price today. */
  now: number;
  /** Short label for the toggle and the price captions, e.g. "$ per cbBTC". */
  label: string;
  /** Unit written after a price, e.g. "cbBTC per USDC"; empty for dollars. */
  unit: string;
  /** Displayed price for a pool price (token1 per token0). */
  fromPool: (poolPrice: number) => number;
  /** Pool price for a displayed price. */
  toPool: (displayed: number) => number;
  fmt: (v: number) => string;
}

const nearOne = (v: number) => Math.abs(v - 1) < 0.03;

export interface PricedPool {
  sym0: string;
  sym1: string;
  poolPrice: number;
  usd0: number;
  usd1: number;
}

/** The views that make sense for this pair, in the order they are offered; the first is the default. */
export function availableViews(p: PricedPool): PriceViewKind[] {
  const stable0 = nearOne(p.usd0);
  const stable1 = nearOne(p.usd1);
  const out: PriceViewKind[] = [];
  const hasUsd = p.usd0 > 0 && p.usd1 > 0 && !(stable0 && stable1);
  if (hasUsd) out.push("usd");
  // A ratio that is already in dollars (WETH/USDC: USDC per WETH) would only repeat the dollar view.
  const usdOnToken1 = hasUsd && stable0 && !stable1;
  const redundantPool = hasUsd && stable1 && !stable0;
  const redundantInverse = hasUsd && usdOnToken1;
  if (!redundantPool) out.push("pool");
  if (!redundantInverse) out.push("inverse");
  return out;
}

export function makePriceView(kind: PriceViewKind, p: PricedPool): PriceView {
  const stable0 = nearOne(p.usd0);
  const stable1 = nearOne(p.usd1);
  if (kind === "usd") {
    // Dollars per the volatile token; for two volatile tokens, per token0.
    const onToken1 = stable0 && !stable1;
    const sym = onToken1 ? p.sym1 : p.sym0;
    const now = onToken1 ? p.usd1 : p.usd0;
    return build("usd", onToken1, now, `$ per ${sym}`, "", p.poolPrice, usdFmt);
  }
  if (kind === "inverse") return build("inverse", true, 1 / p.poolPrice, `${p.sym0} per ${p.sym1}`, `${p.sym0} per ${p.sym1}`, p.poolPrice, qp);
  return build("pool", false, p.poolPrice, `${p.sym1} per ${p.sym0}`, `${p.sym1} per ${p.sym0}`, p.poolPrice, qp);
}

function build(kind: PriceViewKind, flip: boolean, now: number, label: string, unit: string, poolNow: number, fmt: (v: number) => string): PriceView {
  return {
    kind,
    flip,
    now,
    label,
    unit,
    fromPool: (pp) => (flip ? (now * poolNow) / pp : (now * pp) / poolNow),
    toPool: (d) => (flip ? (now * poolNow) / d : (d * poolNow) / now),
    fmt,
  };
}

const usdFmt = (v: number) => (v >= 1 ? `$${v.toLocaleString("en-US", { minimumFractionDigits: v >= 1000 ? 0 : 2, maximumFractionDigits: v >= 1000 ? 0 : 2 })}` : `$${qp(v)}`);

/** A price the way it goes into the text field: six significant digits, no thousands separators. */
export const priceInputValue = (v: number) => Number(v.toPrecision(6));

/** Percent strings: at most two decimals, no trailing zeros. */
export const fmtPct = (v: number) => String(Number(v.toFixed(2)));

/** Pool-percent bounds (below / above the pool price) seen from a view: flipped views swap the two and re-express them. */
export function toViewPercents(lower: number, upper: number, flip: boolean) {
  return flip ? { lower: (upper / (100 + upper)) * 100, upper: (lower / (100 - lower)) * 100 } : { lower, upper };
}

/** Inverse of {@link toViewPercents}. */
export function fromViewPercents(lower: number, upper: number, flip: boolean) {
  const lo = Math.min(lower, 99.9);
  return flip ? { lower: (upper / (100 + upper)) * 100, upper: (lo / (100 - lo)) * 100 } : { lower, upper };
}

/** Largest bound (percent) a view can ask for, given the limits on the pool-percent bounds. */
export function viewLimits(flip: boolean) {
  return flip
    ? { lower: (MAX_UPPER / (100 + MAX_UPPER)) * 100, upper: (MAX_LOWER / (100 - MAX_LOWER)) * 100 }
    : { lower: MAX_LOWER, upper: MAX_UPPER };
}
