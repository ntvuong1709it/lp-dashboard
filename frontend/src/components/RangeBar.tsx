import type { RangeDto } from "../api/types";
import { formatPrice } from "../lib/format";

/** Horizontal bar showing the position range with a marker at the current price. */
export function RangeBar({
  range,
  showLabels = false,
  entry,
}: {
  range: RangeDto;
  showLabels?: boolean;
  /** Price when the position was opened, drawn as a dot on the bar. */
  entry?: number | null;
}) {
  const { priceLower: lower, priceUpper: upper, priceCurrent: current } = range;
  const span = Math.max(upper - lower, Math.abs(upper) * 1e-9);
  const min = Math.min(lower, current ?? lower, entry ?? lower) - span * 0.25;
  const max = Math.max(upper, current ?? upper, entry ?? upper) + span * 0.25;
  const pct = (v: number) => ((v - min) / (max - min)) * 100;

  const color =
    range.inRange == null
      ? "bg-muted"
      : range.inRange
        ? "bg-up-fill/70"
        : "bg-down-fill/70";

  return (
    <div className={`w-full ${showLabels ? "min-w-48" : "min-w-32"}`}>
      <div className="relative h-2 rounded bg-raised">
        <div
          className={`absolute h-2 rounded ${color}`}
          style={{
            left: `${pct(lower)}%`,
            width: `${pct(upper) - pct(lower)}%`,
          }}
        />
        {entry != null && (
          <div
            className="absolute -top-0.5 h-3 w-3 -translate-x-1/2 rounded-full border-2 border-[var(--marker)] bg-accent-fill"
            title={`Entry ${formatPrice(entry)}`}
            style={{ left: `${Math.min(100, Math.max(0, pct(entry)))}%` }}
          />
        )}
        {current != null && (
          <div
            className="absolute -top-1 h-4 w-0.5 bg-[var(--marker)]"
            style={{ left: `${Math.min(100, Math.max(0, pct(current)))}%` }}
          />
        )}
      </div>
      {showLabels && (
        <div className="mt-1 flex justify-between gap-2 whitespace-nowrap text-[11px] text-muted num">
          <span>{formatPrice(lower)}</span>
          <span className="text-fg-2">{formatPrice(current)}</span>
          <span>{formatPrice(upper)}</span>
        </div>
      )}
    </div>
  );
}

export function RangeBadge({
  inRange,
  closed,
  nearEdge,
}: {
  inRange: boolean | null;
  closed?: boolean;
  nearEdge?: boolean;
}) {
  if (closed)
    return (
      <span className="rounded bg-line-strong px-1.5 py-0.5 text-[11px] text-fg-2">
        Closed
      </span>
    );
  if (inRange == null) return null;
  if (inRange && nearEdge)
    return (
      <span className="rounded bg-warn-fill/15 px-1.5 py-0.5 text-[11px] text-warn">
        Near edge
      </span>
    );
  return inRange ? (
    <span className="rounded bg-up-fill/15 px-1.5 py-0.5 text-[11px] text-up">
      In range
    </span>
  ) : (
    <span className="rounded bg-down-fill/15 px-1.5 py-0.5 text-[11px] text-down">
      Out of range
    </span>
  );
}
