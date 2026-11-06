import type { HodlBreakEvenDto, PoolComparisonDto } from "../api/types";
import { formatPercent, formatUsd } from "./format";

const DAY_MS = 86_400_000;

function basisLabel(basis: string): string {
  switch (basis) {
    case "30d":
      return "the last 30 days' average";
    case "lifetime":
      return "its lifetime average";
    case "mixed":
      return "a mix of 30-day and lifetime averages";
    default:
      return basis;
  }
}

/** "about 12 days (around 18 Nov)"; very long horizons are not worth a date. */
function horizon(days: number, now: number): string {
  if (days < 1) return "under a day";
  if (days > 730) return "more than two years";
  const whole = Math.ceil(days);
  const date = new Date(now + whole * DAY_MS).toLocaleDateString(undefined, {
    day: "numeric",
    month: "short",
    year: whole > 300 ? "numeric" : undefined,
  });
  return `about ${whole} day${whole === 1 ? "" : "s"} (around ${date})`;
}

/** One sentence on where this stands against simply holding, and how long catching up would take. */
export function breakEvenText(b: HodlBreakEvenDto, now = Date.now()): string {
  if (b.gapUsd >= 0) {
    return `Ahead of just holding by ${formatUsd(b.gapUsd)}.`;
  }
  const behind = `Behind just holding by ${formatUsd(-b.gapUsd)}`;
  if (b.daysToBreakEven == null) {
    return `${behind}, and it is not earning, so the gap does not close at this rate.`;
  }
  return (
    `${behind}. At ${formatUsd(b.dailyEarningsUsd)}/day (${basisLabel(b.rateBasis)}) fees and rewards ` +
    `close that in ${horizon(b.daysToBreakEven, now)}, if prices stay where they are.`
  );
}

/** Headline for a callout: the standing and, when behind, how soon it closes. */
export function breakEvenHeadline(b: HodlBreakEvenDto): string {
  if (b.gapUsd >= 0) return `Ahead of holding by ${formatUsd(b.gapUsd)}`;
  const behind = `Behind holding by ${formatUsd(-b.gapUsd)}`;
  if (b.daysToBreakEven == null) return `${behind} · not closing at this rate`;
  if (b.daysToBreakEven < 1) return `${behind} · catches up within a day`;
  if (b.daysToBreakEven > 730) return `${behind} · over two years to catch up`;
  return `${behind} · catches up in ~${Math.ceil(b.daysToBreakEven)} days`;
}

/** Short form for a stat card hint. */
export function breakEvenHint(b: HodlBreakEvenDto): string {
  if (b.gapUsd >= 0) return "Ahead of holding";
  if (b.daysToBreakEven == null) return "Gap does not close at the current earning rate";
  if (b.daysToBreakEven < 1) return "Catches up within a day at the current rate";
  if (b.daysToBreakEven > 730) return "Catching up would take over two years";
  return `Catches up in ~${Math.ceil(b.daysToBreakEven)} days at ${formatUsd(b.dailyEarningsUsd)}/day`;
}

/** "1.4x the pool's 30d fee APR (28.1% vs 20.0%)". */
export function poolComparisonText(c: PoolComparisonDto): string {
  const what =
    c.incomeKind === "rewards"
      ? "emission rate"
      : c.poolBasis === "24h"
        ? "24h fee APR"
        : `${c.poolBasis} fee APR`;
  const mine = c.positionBasis === "lifetime" ? "lifetime" : c.positionBasis;
  return (
    `${c.ratio.toFixed(1)}× the pool's ${what} ` +
    `(${formatPercent(c.positionAprPercent)} over ${mine} vs ${formatPercent(c.poolAprPercent)})`
  );
}

/** Table cell text for catching up with holding: the standing, and how long closing the gap takes. */
export function catchUpCell(b: HodlBreakEvenDto | undefined): { text: string; className: string; title: string } {
  if (!b) {
    return { text: "—", className: "text-slate-500", title: "Closed positions no longer earn, so there is nothing to project" };
  }
  const title = breakEvenText(b);
  if (b.gapUsd >= 0) return { text: "Ahead", className: "text-emerald-400", title };
  if (b.daysToBreakEven == null) return { text: "Not closing", className: "text-rose-400", title };
  if (b.daysToBreakEven < 1) return { text: "< 1 day", className: "text-amber-400", title };
  if (b.daysToBreakEven > 730) return { text: "> 2 years", className: "text-amber-400", title };
  return { text: `~${Math.ceil(b.daysToBreakEven)} days`, className: "text-amber-400", title };
}
