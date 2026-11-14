import { usePerformance } from "../api/client";
import type { HodlBreakEvenDto, PositionDto } from "../api/types";
import { formatUsd } from "../lib/format";
import { breakEvenHeadline, breakEvenHint, breakEvenText } from "../lib/performance";
import { useHideBalances } from "../lib/privacy";
import { StatCard } from "./ui";

/** Green when ahead, amber while the gap is closing, red when it is not closing at all. */
function tone(b: HodlBreakEvenDto) {
  if (b.gapUsd >= 0) {
    return "border-up-fill/60 bg-up-fill/10 text-up";
  }
  return b.daysToBreakEven == null
    ? "border-down-fill/60 bg-down-fill/10 text-down"
    : "border-warn-fill/60 bg-warn-fill/10 text-warn";
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
      <div className="mt-1 text-xs text-fg-2">
        {breakEvenText(breakEven)}
        {note && <span className="text-muted"> {note}</span>}
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

/** Stat card for the selected wallets: every position's gap (closed ones included) against simply holding, and how soon the open ones close it. */
export function PortfolioBreakEvenCard({ owner }: { owner: string | null }) {
  useHideBalances();
  const { data, isLoading } = usePerformance(owner);
  if (isLoading) return <StatCard label="LP vs holding" value="" loading />;
  const b = data?.portfolio;
  if (!b) return null;
  const tone = b.gapUsd >= 0 ? "text-up" : b.daysToBreakEven == null ? "text-down" : "text-warn";
  return (
    <StatCard
      label="LP vs holding"
      value={formatUsd(b.gapUsd, true)}
      valueClass={tone}
      hint={
        <span title={`${breakEvenText(b)} Closed positions count towards the gap but no longer earn.`}>
          {breakEvenHint(b)}
        </span>
      }
    />
  );
}
