import type { RangeDto } from "../api/types";
import { formatPrice } from "../lib/format";

/** Horizontal bar showing the position range with a marker at the current price. */
export function RangeBar({
  range,
  showLabels = false,
}: {
  range: RangeDto;
  showLabels?: boolean;
}) {
  const { priceLower: lower, priceUpper: upper, priceCurrent: current } = range;
  const span = Math.max(upper - lower, Math.abs(upper) * 1e-9);
  const min = Math.min(lower, current ?? lower) - span * 0.25;
  const max = Math.max(upper, current ?? upper) + span * 0.25;
  const pct = (v: number) => ((v - min) / (max - min)) * 100;

  const color =
    range.inRange == null
      ? "bg-slate-500"
      : range.inRange
        ? "bg-emerald-500/70"
        : "bg-rose-500/70";

  return (
    <div className="w-full min-w-32">
      <div className="relative h-2 rounded bg-slate-800">
        <div
          className={`absolute h-2 rounded ${color}`}
          style={{
            left: `${pct(lower)}%`,
            width: `${pct(upper) - pct(lower)}%`,
          }}
        />
        {current != null && (
          <div
            className="absolute -top-1 h-4 w-0.5 bg-white"
            style={{ left: `${Math.min(100, Math.max(0, pct(current)))}%` }}
          />
        )}
      </div>
      {showLabels && (
        <div className="mt-1 flex justify-between text-[11px] text-slate-400 num">
          <span>{formatPrice(lower)}</span>
          <span className="text-slate-200">{formatPrice(current)}</span>
          <span>{formatPrice(upper)}</span>
        </div>
      )}
    </div>
  );
}

export function RangeBadge({
  inRange,
  closed,
}: {
  inRange: boolean | null;
  closed?: boolean;
}) {
  if (closed)
    return (
      <span className="rounded bg-slate-700 px-1.5 py-0.5 text-[11px] text-slate-300">
        Closed
      </span>
    );
  if (inRange == null) return null;
  return inRange ? (
    <span className="rounded bg-emerald-500/15 px-1.5 py-0.5 text-[11px] text-emerald-400">
      In range
    </span>
  ) : (
    <span className="rounded bg-rose-500/15 px-1.5 py-0.5 text-[11px] text-rose-400">
      Out of range
    </span>
  );
}
