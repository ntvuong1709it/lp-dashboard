import { Link } from "react-router-dom";
import type { PositionDto } from "../../api/types";
import { pairLabel, type Alert, type AlertKind } from "../../lib/attention";
import { formatUsd } from "../../lib/format";
import { usePositionDrawer } from "../../lib/positionDrawer";
import { useHideBalances } from "../../lib/privacy";
import { ErrorText, Skeleton } from "../ui";

/** One short text label per kind, each with its own colour, so a chip reads without decoding an icon. */
const chipStyle: Record<AlertKind, { label: string; className: string }> = {
  out: { label: "Out of range", className: "border-down-fill/50 bg-down-fill/10 text-down" },
  edge: { label: "Near edge", className: "border-warn-fill/50 bg-warn-fill/10 text-warn" },
  unclaimed: { label: "Claim now", className: "border-accent-fill/50 bg-accent-fill/10 text-accent" },
  prices: { label: "No price", className: "border-line-strong/50 bg-muted/10 text-fg-2" },
};

/**
 * The line at the top of the Overview page: which positions need a decision (out of range, near an edge, unclaimed above
 * the alert level, missing prices). Each chip opens its position. What is waiting to be claimed is on the Unclaimed card.
 */
export function AttentionStrip({
  alerts,
  open,
  loading,
  error,
}: {
  alerts: Alert[];
  open: PositionDto[];
  loading: boolean;
  error: unknown;
}) {
  useHideBalances(); // re-render when the toggle changes; formatters read the flag
  const drawer = usePositionDrawer();

  if (error) return <ErrorText error={error} />;
  if (loading) return <Skeleton className="h-12 w-full rounded-lg" />;
  if (!open.length) {
    return (
      <p className="rounded-lg border border-line bg-panel px-4 py-3 text-sm text-fg-2">
        No open positions to watch yet.{" "}
        <Link className="font-medium text-accent hover:underline" to="/addresses">
          Add a wallet
        </Link>
      </p>
    );
  }

  const count = alerts.length;
  const allInRange = open.every((p) => p.range?.inRange === true);

  return (
    <section
      aria-label="Needs you"
      className={`flex flex-wrap items-center gap-x-4 gap-y-2 rounded-lg border px-4 py-2.5 ${count > 0 ? "border-warn-fill/40 bg-warn-fill/5" : "border-up-fill/30 bg-up-fill/5"}`}
    >
      <div className="flex min-w-0 flex-1 flex-wrap items-center gap-2">
        <span
          className={`rounded-full px-2 text-[11px] font-bold ring-1 ${count > 0 ? "bg-warn-fill/15 text-warn ring-warn-fill/40" : "bg-up-fill/15 text-up ring-up-fill/40"}`}
        >
          {count}
          <span className="sr-only"> {count === 1 ? "item needs you" : "items need you"}</span>
        </span>
        {count === 0 ? (
          <span className="text-sm text-fg-2">
            All clear.{" "}
            {allInRange
              ? `${open.length === 1 ? "Your open position is" : `All ${open.length} open positions are`} in range, none near an edge.`
              : "Nothing needs attention."}
          </span>
        ) : (
          <ul className="flex min-w-0 flex-wrap items-center gap-2">
            {alerts.map((a) => (
              <li key={`${a.kind}-${a.position.id}`}>
                <button
                  type="button"
                  onClick={() => drawer.open(a.position.id)}
                  title={`${a.title} ${a.detail}`}
                  className="inline-flex items-center gap-1.5 rounded-full border border-line-strong bg-panel py-0.5 pl-0.5 pr-2.5 text-sm hover:bg-raised"
                >
                  <span
                    className={`rounded-full border px-2 py-px text-[11px] font-semibold ${chipStyle[a.kind].className}`}
                  >
                    {chipStyle[a.kind].label}
                  </span>
                  <span className="font-medium">
                    {pairLabel(a.position)} <span className="text-xs font-normal text-faint">#{a.position.tokenId}</span>
                  </span>
                  {a.kind === "unclaimed" && (
                    <span className="num text-xs text-accent">
                      {formatUsd(a.position.metrics.uncollectedFeesUsd + a.position.metrics.pendingRewardsUsd)}
                    </span>
                  )}
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}
