import type { RangeDto } from "../api/types";
import { formatPrice } from "../lib/format";

/**
 * The one range bar used on every page: a blue band for the range, a white tick where the price is now, the lower and
 * upper price under its ends, and optionally a dot for the entry price and a ring for a price you are testing.
 */
export function RangeTrack({
  lower,
  upper,
  current,
  entry,
  ring,
  scale,
  labels = true,
  ariaLabel,
  className = "",
}: {
  lower: number;
  upper: number;
  current?: number | null;
  /** Price when the position was opened, drawn as a dot on the bar. */
  entry?: number | null;
  /** A price to test (for example after a fall), drawn as a ring. `ringClass` colors its border. */
  ring?: { value: number; ringClass: string } | null;
  /** Fixed [min, max] to draw between, when several bars must share one scale. */
  scale?: [number, number];
  /** Show the lower and upper price under the ends of the band. */
  labels?: boolean;
  ariaLabel?: string;
  className?: string;
}) {
  let min: number;
  let max: number;
  if (scale) {
    [min, max] = scale;
  } else {
    const values = [lower, upper, current, entry, ring?.value].filter((v): v is number => v != null);
    const lo = Math.min(...values);
    const hi = Math.max(...values);
    const pad = Math.max(hi - lo, Math.abs(hi) * 1e-9) * 0.15;
    min = lo - pad;
    max = hi + pad;
  }
  const at = (x: number) => Math.max(0, Math.min(100, ((x - min) / (max - min)) * 100));
  const left = at(lower);

  return (
    <div className={`w-full ${labels ? "min-w-48" : "min-w-32"} ${className}`}>
      <div
        className="relative h-1.5 rounded-full bg-raised"
        role="img"
        aria-label={
          ariaLabel ??
          `Range ${formatPrice(lower)} to ${formatPrice(upper)}${current != null ? `. Price now ${formatPrice(current)}` : ""}.`
        }
      >
        <div className="absolute inset-y-0 rounded-full bg-accent-fill/55" style={{ left: `${left}%`, width: `${at(upper) - left}%` }} />
        {current != null && (
          <div className="absolute -top-1 h-3.5 w-0.5 rounded-sm bg-fg" style={{ left: `${at(current)}%` }} />
        )}
        {entry != null && (
          <div
            className="absolute -top-0.5 box-border h-2.5 w-2.5 -translate-x-1/2 rounded-full border-2 border-panel bg-accent"
            title={`Entry ${formatPrice(entry)}`}
            style={{ left: `${at(entry)}%` }}
          />
        )}
        {ring && (
          <div
            className={`absolute -top-1 box-border h-3.5 w-3.5 -translate-x-1/2 rounded-full border-2 bg-panel ${ring.ringClass}`}
            style={{ left: `${at(ring.value)}%` }}
          />
        )}
        {labels && (
          <>
            <span className="num absolute top-3 -translate-x-1/2 whitespace-nowrap text-[10.5px] text-faint" style={{ left: `${left}%` }}>
              {formatPrice(lower)}
            </span>
            <span className="num absolute top-3 -translate-x-1/2 whitespace-nowrap text-[10.5px] text-faint" style={{ left: `${at(upper)}%` }}>
              {formatPrice(upper)}
            </span>
          </>
        )}
      </div>
      {labels && <div className="h-5" aria-hidden />}
    </div>
  );
}

/** The range bar for a position's range. */
export function RangeBar({
  range,
  showLabels = false,
  entry,
}: {
  range: RangeDto;
  /** Show the lower and upper price under the ends of the bar. */
  showLabels?: boolean;
  entry?: number | null;
}) {
  return (
    <RangeTrack
      lower={range.priceLower}
      upper={range.priceUpper}
      current={range.priceCurrent}
      entry={entry}
      labels={showLabels}
    />
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
