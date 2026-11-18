import { Link } from "react-router-dom";
import { useMarketWatch } from "../../api/market";
import { countdownLabel, nextHighEvents, STRESS_CLASS, STRESS_LABEL } from "../../lib/market";
import { useOwner } from "../../lib/owner";
import { useHideBalances } from "../../lib/privacy";
import { useFallImpact } from "../../lib/stress";
import { useNow } from "../../lib/useNow";
import { formatUsd } from "../../lib/format";
import { Chip } from "../borrow/bits";
import { Skeleton } from "../ui";
import { FearGreedChip } from "./FearGreedChip";

/** The fall the Overview line answers for. The Market watch page lets you change it. */
const FALL = 20;

/**
 * One line on the Overview: the next big event, and whether a 20% fall would hurt the loan or stop positions earning. It
 * turns amber only when the loan is thin or the stressed loan is not OK.
 */
export function MarketLine() {
  useHideBalances(); // re-render when the toggle changes; formatters read the flag
  const { owner } = useOwner();
  const now = useNow(30_000);
  const watch = useMarketWatch(owner, 7);
  const impact = useFallImpact(owner, FALL);
  const data = watch.data;

  if (watch.isLoading) return <Skeleton className="h-11 w-full rounded-lg" />;
  if (watch.error || !data) {
    return <p className="rounded-lg border border-line bg-panel px-4 py-2.5 text-sm text-muted">Market watch is unavailable right now.</p>;
  }

  const next = nextHighEvents(data.upcoming)[0] ?? null;
  const stress = data.loan?.stress;
  const row = stress?.rows.find((r) => r.fallPercent === FALL);
  const risky = (stress != null && stress.buffer !== "Comfortable") || (row != null && row.status !== "Ok");

  return (
    <section
      aria-label="Market watch"
      className={`flex flex-wrap items-center gap-x-4 gap-y-2 rounded-lg border px-4 py-2.5 text-sm ${
        risky ? "border-warn-fill/50 bg-warn-fill/10" : "border-line bg-panel"
      }`}
    >
      <div className="flex min-w-0 flex-1 flex-wrap items-center gap-x-4 gap-y-1.5">
        {next ? (
          <span className="inline-flex items-center gap-2">
            <span className="num rounded-full bg-raised px-2 py-0.5 text-xs font-medium text-fg-2">
              {countdownLabel(new Date(next.at).getTime(), now)}
            </span>
            <span className="font-medium">{next.name}</span>
          </span>
        ) : (
          <span className="text-muted">No big events in the next 7 days.</span>
        )}
        {(row || (!impact.loading && impact.total > 0)) && <span className="text-muted">If prices fall {FALL}%:</span>}
        {row && (
          <span className="inline-flex items-center gap-2">
            <Chip className={STRESS_CLASS[row.status]}>Loan {row.status === "Ok" ? "safe" : STRESS_LABEL[row.status].toLowerCase()}</Chip>
            {row.addCollateralUsd > 0 && <span className="num text-muted">add {formatUsd(row.addCollateralUsd)}</span>}
          </span>
        )}
        {!impact.loading && impact.total > 0 && (
          <Chip className={impact.stops > 0 ? "bg-down-fill/15 text-down" : "bg-up-fill/15 text-up"}>
            {impact.stops > 0 ? `${impact.stops} of ${impact.total} stop earning` : "All keep earning"}
          </Chip>
        )}
        {data.fearGreed && <FearGreedChip fearGreed={data.fearGreed} />}
      </div>
      <Link to="/market" className="whitespace-nowrap font-medium text-accent hover:underline">
        Market watch
      </Link>
    </section>
  );
}
