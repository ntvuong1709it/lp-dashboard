import type { Chain, Protocol, RangeDto } from "../api/types";

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

export function formatAmount(value: number | null | undefined): string {
  if (value == null) return "—";
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

export function formatDate(iso: string | null | undefined): string {
  if (!iso) return "—";
  return new Date(iso).toLocaleString(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  });
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
