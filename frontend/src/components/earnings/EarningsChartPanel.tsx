import { useMemo } from "react";
import { useBorrowDaily, useDailyEarnings } from "../../api/client";
import { headlines } from "../../lib/earnings";
import { useOwner } from "../../lib/owner";
import { useHideBalances } from "../../lib/privacy";
import { Empty, ErrorText, Panel, Skeleton } from "../ui";
import { EarningsChart } from "./EarningsChart";

/**
 * The Earned per day/week/month chart with everything it needs fetched in place, so a page can drop it in without
 * knowing about the earnings endpoints. Used by the Overview page; the Earnings page feeds the same chart itself.
 */
export function EarningsChartPanel() {
  useHideBalances(); // the chart formats amounts, so re-render when the mask toggles
  const { owner } = useOwner();
  const daily = useDailyEarnings(owner);
  // The chart's "All" range can reach back a year; the endpoint caps at 365 days.
  const interest = useBorrowDaily(owner, 365);
  const days = daily.data;
  const view = useMemo(() => (days?.length ? headlines(days) : null), [days]);

  if (daily.error)
    return (
      <Panel title="Earned per day">
        <ErrorText error={daily.error} />
      </Panel>
    );
  if (daily.isLoading)
    return (
      <Panel title="Earned per day">
        <Skeleton className="h-64 w-full" />
      </Panel>
    );
  if (!days?.length || !view)
    return (
      <Panel title="Earned per day">
        <Empty>
          No earnings tracked yet. Daily earnings build up from hourly snapshots of open positions.
        </Empty>
      </Panel>
    );

  return (
    <EarningsChart
      days={days}
      interest={interest.data ?? []}
      projection={view.projection}
      monthName={view.monthName}
    />
  );
}
