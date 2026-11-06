import { usePerformance } from "../api/client";
import type { HodlBreakEvenDto, PositionDto } from "../api/types";
import { breakEvenHeadline, breakEvenText } from "../lib/performance";
import { useHideBalances } from "../lib/privacy";

/** Green when ahead, amber while the gap is closing, red when it is not closing at all. */
function tone(b: HodlBreakEvenDto) {
  if (b.gapUsd >= 0) {
    return "border-emerald-500/60 bg-emerald-500/10 text-emerald-300";
  }
  return b.daysToBreakEven == null
    ? "border-rose-500/60 bg-rose-500/10 text-rose-300"
    : "border-amber-500/60 bg-amber-500/10 text-amber-300";
}

/** Where a position or the whole portfolio stands against simply holding the same tokens, and how long catching up would take. */
export function BreakEvenCallout({
  breakEven,
  label,
  note,
}: {
  breakEven: HodlBreakEvenDto;
  label: string;
  note?: string;
}) {
  return (
    <div
      className={`rounded-lg border-l-4 border px-4 py-3 ${tone(breakEven)}`}
      role="status"
    >
      <div className="text-xs font-semibold uppercase tracking-[.05em] opacity-80">
        {label}
      </div>
      <div className="mt-0.5 text-lg font-semibold num">
        {breakEvenHeadline(breakEven)}
      </div>
      <div className="mt-1 text-xs text-slate-300">
        {breakEvenText(breakEven)}
        {note && <span className="text-slate-400"> {note}</span>}
      </div>
    </div>
  );
}

/** Callout for one open position; renders nothing while loading, for closed positions, or when there is no projection. */
export function PositionBreakEven({ position: p }: { position: PositionDto }) {
  useHideBalances();
  const { data } = usePerformance(p.owner);
  const b = data?.positions.find((r) => r.positionId === p.id)?.breakEven;
  if (p.status !== "Open" || !b) return null;
  return <BreakEvenCallout breakEven={b} label="Against holding" />;
}

/** Callout for the selected wallets: every position's gap (closed ones included) against what the open ones earn per day. */
export function PortfolioBreakEven({ owner }: { owner: string | null }) {
  useHideBalances();
  const { data } = usePerformance(owner);
  if (!data?.portfolio) return null;
  return (
    <BreakEvenCallout
      breakEven={data.portfolio}
      label="LP vs just holding, all positions"
      note="Closed positions count towards the gap but no longer earn."
    />
  );
}
