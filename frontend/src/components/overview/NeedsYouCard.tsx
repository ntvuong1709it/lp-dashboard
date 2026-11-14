import { Link } from "react-router-dom";
import type { Alert, AlertKind } from "../../lib/attention";
import type { PositionDto } from "../../api/types";
import { usePositionDrawer } from "../../lib/positionDrawer";
import { useHideBalances } from "../../lib/privacy";
import { ClaimSummary } from "../Unclaimed";
import { ErrorText, Skeleton } from "../ui";

/** A text label per kind, each with its own colour, so a row is readable without decoding an icon. */
const alertStyle: Record<AlertKind, { label: string; className: string }> = {
  out: { label: "Out of range", className: "border-down-fill/50 bg-down-fill/10 text-down" },
  edge: { label: "Near edge", className: "border-warn-fill/50 bg-warn-fill/10 text-warn" },
  unclaimed: { label: "Unclaimed", className: "border-accent-fill/50 bg-accent-fill/10 text-accent" },
  prices: { label: "No price", className: "border-line-strong/50 bg-muted/10 text-fg-2" },
};

/** "All clear" with the reason when every open position is in range; otherwise just that nothing needs attention. */
function allClearText(open: PositionDto[]): string {
  if (open.length > 0 && open.every((p) => p.range?.inRange === true)) {
    return `All clear. ${open.length === 1 ? "Your open position is" : `All ${open.length} open positions are`} in range, none near an edge.`;
  }
  return "All clear. Nothing needs attention.";
}

/**
 * The first thing on the Overview page: what needs a decision (out of range, near an edge, unclaimed above the alert
 * level, missing prices) and what is waiting to be claimed, in one card.
 */
export function NeedsYouCard({
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
  const count = alerts.length;
  const noPositions = !loading && !error && open.length === 0;

  return (
    <section aria-labelledby="needs-you-title" className="min-w-0 rounded-lg border border-line bg-panel">
      <div className="flex items-center gap-2 border-b border-line px-4 py-2">
        <h2 id="needs-you-title" className="text-sm font-medium text-fg-2">
          Needs you
        </h2>
        {!loading && !error && !noPositions && (
          <span
            className={`rounded-full px-2 text-[11px] font-bold ring-1 ${count > 0 ? "bg-warn-fill/15 text-warn ring-warn-fill/40" : "bg-up-fill/15 text-up ring-up-fill/40"}`}
          >
            {count}
            <span className="sr-only"> {count === 1 ? "item needs attention" : "items need attention"}</span>
          </span>
        )}
      </div>
      <div className="min-h-44 divide-y divide-line px-4">
        {error ? (
          <div className="py-3">
            <ErrorText error={error} />
          </div>
        ) : loading ? (
          <div className="py-3">
            <Skeleton className="h-36 w-full" />
          </div>
        ) : (
          <>
            <div className="py-3">
              {noPositions ? (
                <p className="text-sm text-fg-2">
                  No open positions to watch yet.{" "}
                  <Link className="font-medium text-accent hover:underline" to="/addresses">
                    Add a wallet
                  </Link>
                </p>
              ) : count === 0 ? (
                <p className="flex items-start gap-2.5 text-sm text-fg-2">
                  <span
                    aria-hidden="true"
                    className="mt-0.5 grid h-4 w-4 flex-none place-items-center rounded-full bg-up-fill/20 text-[10px] font-bold text-up"
                  >
                    ✓
                  </span>
                  {allClearText(open)}
                </p>
              ) : (
                <ul className="max-h-72 space-y-3 overflow-y-auto">
                  {alerts.map((a) => (
                    <li key={`${a.kind}-${a.position.id}`} className="flex gap-3">
                      <span
                        className={`mt-0.5 h-fit w-24 flex-none rounded-full border px-2 py-0.5 text-center text-[11px] font-semibold ${alertStyle[a.kind].className}`}
                      >
                        {alertStyle[a.kind].label}
                      </span>
                      <div className="text-sm">
                        <div>{a.title}</div>
                        <div className="text-xs text-muted">
                          {a.detail}{" "}
                          <button
                            type="button"
                            className="font-medium text-accent hover:underline"
                            onClick={() => drawer.open(a.position.id)}
                          >
                            View position
                          </button>
                        </div>
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </div>
            <div className="py-3">
              <ClaimSummary open={open} />
            </div>
          </>
        )}
      </div>
    </section>
  );
}
