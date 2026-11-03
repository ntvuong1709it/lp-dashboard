import type { Chain, Protocol, RangeDto } from "../api/types";
import { balancesHidden, maskedBalance } from "./privacy";

const usd = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
  maximumFractionDigits: 2,
});
const usdSmall = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
  maximumSignificantDigits: 3,
});

export function formatUsd(
  value: number | null | undefined,
  signed = false,
): string {
  if (value == null) return "—";
  if (balancesHidden()) return maskedBalance;
  const abs = Math.abs(value);
  const text = abs > 0 && abs < 0.01 ? usdSmall.format(abs) : usd.format(abs);
  const sign = value < 0 ? "-" : signed && value > 0 ? "+" : "";
  return `${sign}${text}`;
}

export function formatPercent(
  value: number | null | undefined,
  signed = false,
): string {
  if (value == null) return "—";
  const sign = signed && value > 0 ? "+" : "";
  return `${sign}${value.toFixed(2)}%`;
}

/**
 * A share of time: two decimals, but never "100.00%" unless it is exactly 100 (or "0.00%" unless exactly 0),
 * so a position that was out of range for a short while does not look perfect.
 */
export function formatShare(value: number): string {
  if (value >= 100) return "100.00%";
  if (value <= 0) return "0.00%";
  const text = value.toFixed(2);
  if (text === "100.00") return "99.99%";
  if (text === "0.00") return "0.01%";
  return `${text}%`;
}

export function formatAmount(value: number | null | undefined): string {
  if (value == null) return "—";
  if (balancesHidden()) return maskedBalance;
  if (value === 0) return "0";
  const abs = Math.abs(value);
  if (abs >= 1000)
    return value.toLocaleString("en-US", { maximumFractionDigits: 2 });
  if (abs >= 1)
    return value.toLocaleString("en-US", { maximumFractionDigits: 4 });
  return value.toLocaleString("en-US", { maximumSignificantDigits: 4 });
}

export function formatPrice(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) return "—";
  if (value === 0) return "0";
  const abs = Math.abs(value);
  if (abs >= 1e9 || abs < 1e-8) return value.toExponential(3);
  return value.toLocaleString("en-US", { maximumSignificantDigits: 6 });
}

export function formatAge(days: number | null | undefined): string {
  if (days == null) return "—";
  if (days < 1) return `${Math.max(1, Math.round(days * 24))}h`;
  if (days < 60) return `${Math.floor(days)}d ${Math.round((days % 1) * 24)}h`;
  return `${Math.floor(days)}d`;
}

/** "~" when part of a time-in-range figure is estimated rather than measured. */
export const estimateMark = (t: { estimatedHours: number }) =>
  t.estimatedHours > 0 ? "~" : "";

/** Explains where a time-in-range figure comes from. */
export const timeInRangeSource = (t: {
  measuredHours: number;
  estimatedHours: number;
}) =>
  t.estimatedHours > 0
    ? `${formatHours(t.measuredHours)} measured from snapshots + ${formatHours(t.estimatedHours)} estimated from historical token prices`
    : `${formatHours(t.measuredHours)} measured from snapshots`;

/** "12h", "3.5d" for a duration given in hours. */
export function formatHours(hours: number): string {
  if (hours < 48) return `${Math.max(1, Math.round(hours))}h`;
  return `${(hours / 24).toFixed(1)}d`;
}

export function formatDate(iso: string | null | undefined): string {
  if (!iso) return "—";
  return new Date(iso).toLocaleString(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  });
}

/** "just now", "5 min ago", "3 h ago", "2 d ago". */
export function formatAgo(iso: string | null | undefined, now = Date.now()): string {
  if (!iso) return "never";
  const seconds = Math.max(0, Math.round((now - new Date(iso).getTime()) / 1000));
  if (seconds < 60) return "just now";
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 48) return `${hours} h ago`;
  return `${Math.floor(hours / 24)} d ago`;
}

export function formatFee(fee: number | null): string {
  return fee == null
    ? ""
    : `${(fee / 10000).toFixed(fee % 100 === 0 ? 2 : 3)}%`;
}

export const shortAddress = (address: string) =>
  `${address.slice(0, 6)}…${address.slice(-4)}`;

const explorers: Record<Chain, string> = {
  Base: "https://basescan.org",
  Ethereum: "https://etherscan.io",
  Solana: "https://solscan.io",
  Robinhood: "",
};

export const txUrl = (chain: Chain, hash: string) =>
  `${explorers[chain]}/tx/${hash}`;
export const addressUrl = (chain: Chain, address: string) =>
  `${explorers[chain]}/address/${address}`;

/** Only Base is synced for EVM wallets today, so EVM wallets link to Basescan. */
export const walletUrl = (family: "Evm" | "Solana", address: string) =>
  family === "Solana"
    ? addressUrl("Solana", address)
    : addressUrl("Base", address);

const dexScreenerChains: Partial<Record<Chain, string>> = {
  Base: "base",
  Ethereum: "ethereum",
  Solana: "solana",
};

/** External pages for a pool: the Uniswap app (Uniswap v3 on Base) and DexScreener. */
export function poolLinks(
  chain: Chain,
  protocol: Protocol,
  pool: string,
): { label: string; url: string }[] {
  const links: { label: string; url: string }[] = [];
  if (protocol === "UniswapV3" && chain === "Base") {
    links.push({
      label: "Uniswap",
      url: `https://app.uniswap.org/explore/pools/base/${pool}`,
    });
  }
  const slug = dexScreenerChains[chain];
  if (slug) {
    links.push({
      label: "DexScreener",
      url: `https://dexscreener.com/${slug}/${pool}`,
    });
  }
  return links;
}

export const protocolLabels: Record<Protocol, string> = {
  UniswapV3: "Uniswap v3",
  AerodromeSlipstream: "Aerodrome CL",
  UniswapV4: "Uniswap v4",
  OrcaWhirlpool: "Orca",
  RaydiumClmm: "Raydium CLMM",
  MeteoraDlmm: "Meteora DLMM",
};

/** Flip a token0-in-token1 range to token1-in-token0. */
export function invertRange(range: RangeDto): RangeDto {
  const inv = (v: number | null) => (v ? 1 / v : null);
  const lower = inv(range.priceUpper) ?? 0;
  const upper = inv(range.priceLower) ?? 0;
  const current = inv(range.priceCurrent);
  return {
    priceLower: lower,
    priceUpper: upper,
    priceCurrent: current,
    inRange: range.inRange,
    distanceToLowerPercent: current
      ? ((current - lower) / current) * 100
      : null,
    distanceToUpperPercent: current
      ? ((upper - current) / current) * 100
      : null,
  };
}

export const pnlClass = (value: number | null | undefined) =>
  value == null || value === 0
    ? "text-slate-300"
    : value > 0
      ? "text-emerald-400"
      : "text-rose-400";
