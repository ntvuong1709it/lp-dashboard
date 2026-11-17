import { formatFee, formatPrice } from "../../lib/format";
import type { FallImpact, FallStatus, PositionFall } from "../../lib/stress";
import { usePositionDrawer } from "../../lib/positionDrawer";
import { Chip } from "../borrow/bits";
import { Skeleton } from "../ui";

const CHIP: Record<FallStatus, { label: string; className: string } | null> = {
  ok: { label: "Keeps earning", className: "bg-up-fill/15 text-up" },
  edge: { label: "At the edge", className: "bg-warn-fill/15 text-warn" },
  out: { label: "Stops earning", className: "bg-down-fill/15 text-down" },
  "already-out": { label: "Already out", className: "bg-down-fill/15 text-down" },
  unaffected: null,
  "no-range": null,
};

const MARKER_RING: Record<FallStatus, string> = {
  ok: "border-up",
  edge: "border-warn",
  out: "border-down",
  "already-out": "border-down",
  unaffected: "border-muted",
  "no-range": "border-muted",
};

/** One position: its range, where the price is now, and where it lands after the fall. */
function FallRow({ f }: { f: PositionFall }) {
  const drawer = usePositionDrawer();
  const chip = CHIP[f.status];
  const v = f.view;
  // The same scale for every fall, so the bar does not jump when the fall changes.
  const min = v ? Math.min(v.lower, v.current * 0.7) * 0.97 : 0;
  const max = v ? v.upper * 1.03 : 1;
  const at = (x: number) => Math.max(0, Math.min(100, ((x - min) / (max - min)) * 100));

  return (
    <li className="min-w-0">
      <button
        type="button"
        onClick={() => drawer.open(f.position.id)}
        className="block w-full rounded-md py-0.5 text-left hover:bg-raised/40"
        title={v ? `Range ${formatPrice(v.lower)} – ${formatPrice(v.upper)} ${v.unit}` : undefined}
      >
        <span className="flex items-center justify-between gap-2">
          <span className="min-w-0 truncate text-sm font-semibold">
            {f.pair} <span className="text-xs font-normal text-faint">{formatFee(f.position.fee)}</span>
          </span>
          {chip && <Chip className={chip.className}>{chip.label}</Chip>}
        </span>
        {v && (
          <>
            <span
              className="relative mb-6 mt-3 block h-1.5 rounded-full bg-raised"
              role="img"
              aria-label={`Range ${formatPrice(v.lower)} to ${formatPrice(v.upper)} ${v.unit}. Price now ${formatPrice(v.current)}, after the fall ${formatPrice(v.after)}.`}
            >
              <span
                className="absolute inset-y-0 rounded-full bg-accent-fill/55"
                style={{ left: `${at(v.lower)}%`, width: `${at(v.upper) - at(v.lower)}%` }}
              />
              <span className="absolute -top-1 h-3.5 w-0.5 rounded-sm bg-fg" style={{ left: `${at(v.current)}%` }} />
              <span
                className={`absolute -top-1 box-border h-3.5 w-3.5 -translate-x-1/2 rounded-full border-2 bg-panel ${MARKER_RING[f.status]}`}
                style={{ left: `${at(v.after)}%` }}
              />
              <span
                className="num absolute top-3 -translate-x-1/2 whitespace-nowrap text-[10.5px] text-faint"
                style={{ left: `${at(v.lower)}%` }}
              >
                {formatPrice(v.lower)}
              </span>
              <span
                className="num absolute top-3 -translate-x-1/2 whitespace-nowrap text-[10.5px] text-faint"
                style={{ left: `${at(v.upper)}%` }}
              >
                {formatPrice(v.upper)}
              </span>
            </span>
          </>
        )}
      </button>
    </li>
  );
}

/** Every open position that a fall can push out of its range, with the ones that cannot be hurt summed up in a line. */
export function PositionsFall({ impact }: { impact: FallImpact }) {
  if (impact.loading) return <Skeleton className="h-32 w-full" />;
  if (impact.total === 0) {
    return <p className="text-sm text-muted">No open positions to test.</p>;
  }
  return (
    <div>
      <div className="mb-2 flex justify-between gap-2 text-xs font-medium text-muted">
        <span>Your positions</span>
        {impact.affected.length > 0 && <span className="font-normal text-faint">Ring: where the price lands</span>}
      </div>
      {impact.affected.length > 0 && (
        <ul className="space-y-3">
          {impact.affected.map((f) => (
            <FallRow key={f.position.id} f={f} />
          ))}
        </ul>
      )}
      {impact.unaffected.length > 0 && (
        <p className="mt-3 text-xs text-muted">
          {impact.unaffected.length} {impact.unaffected.length === 1 ? "position is" : "positions are"} not affected: stablecoin pairs and
          pairs where both tokens move together.
        </p>
      )}
    </div>
  );
}
