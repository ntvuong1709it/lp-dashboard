import { balancesHidden, maskedBalance } from "./privacy";

/** "$48k", "$48.5k", "$1.2M", "-$300": short enough for an axis. Follows the hide-balances mask. */
export function compactUsd(value: number): string {
  if (balancesHidden()) return maskedBalance;
  const abs = Math.abs(value);
  const sign = value < 0 ? "-" : "";
  if (abs >= 1_000_000) return `${sign}$${(abs / 1_000_000).toFixed(1).replace(/\.0$/, "")}M`;
  if (abs >= 1000) return `${sign}$${(abs / 1000).toFixed(1).replace(/\.0$/, "")}k`;
  return `${sign}$${Math.round(abs)}`;
}

/** What a chart's change figure is measured over: "last 90 days", or "all time" for a range of 0. */
export const rangeLabel = (days: number) => (days ? `last ${days} days` : "all time");

/** "Nov 3", or "Nov 3, 2026" when the chart spans more than a year and the year helps. */
export function shortDay(ms: number, withYear = false): string {
  return new Date(ms).toLocaleDateString(undefined, {
    month: "short",
    day: "numeric",
    year: withYear ? "numeric" : undefined,
  });
}

/** A 1, 2, 2.5, 5 or 10 times a power of ten at or above `raw`, so axis ticks land on round numbers. */
function niceStep(raw: number): number {
  const power = Math.pow(10, Math.floor(Math.log10(raw)));
  const m = raw / power;
  return (m <= 1 ? 1 : m <= 2 ? 2 : m <= 2.5 ? 2.5 : m <= 5 ? 5 : 10) * power;
}

/**
 * Y scale for a line that should fill the chart instead of starting at zero: the data range plus a little air,
 * widened to round tick values. `includeZero` keeps the zero line in view (for profit and loss).
 */
export function paddedScale(values: number[], includeZero = false): { lo: number; hi: number; ticks: number[] } {
  let min = Math.min(...values);
  let max = Math.max(...values);
  if (includeZero) {
    min = Math.min(min, 0);
    max = Math.max(max, 0);
  }
  if (!Number.isFinite(min) || !Number.isFinite(max)) return { lo: 0, hi: 1, ticks: [0, 1] };
  const span = max - min || Math.abs(max) * 0.1 || 1;
  const step = niceStep((span * 1.2) / 4);
  const lo = Math.floor((min - span * 0.06) / step) * step;
  const hi = Math.ceil((max + span * 0.06) / step) * step;
  const ticks: number[] = [];
  for (let v = lo; v <= hi + step / 2; v += step) ticks.push(Math.round(v * 1e6) / 1e6);
  return { lo, hi, ticks };
}
