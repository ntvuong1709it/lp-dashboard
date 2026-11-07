import { useMemo } from "react";
import {
  useDailyEarnings,
  useEarningsByPosition,
  usePositions,
  useWeeklyClaims,
} from "../api/client";
import { CoverageCard } from "../components/borrow/CoverageCard";
import { ClaimHealth } from "../components/earnings/ClaimHealth";
import { ClaimLog } from "../components/earnings/ClaimLog";
import { EarningsAlerts } from "../components/earnings/EarningsAlerts";
import { EarningsChart } from "../components/earnings/EarningsChart";
import { HeadlineCards } from "../components/earnings/HeadlineCards";
import { PositionBreakdown } from "../components/earnings/PositionBreakdown";
import { TokenBreakdown } from "../components/earnings/TokenBreakdown";
import { EarningsTabs } from "../components/EarningsTabs";
import { UpdatedAgo } from "../components/UpdatedAgo";
import { Empty, ErrorText, PageSkeleton } from "../components/ui";
import {
  buildEarningsAlerts,
  claimFlags,
  claimHealth,
  claimTxs,
  headlines,
} from "../lib/earnings";
import { formatUsd } from "../lib/format";
import { useOwner } from "../lib/owner";
import { useHideBalances } from "../lib/privacy";

export function FeeClaimsPage() {
  useHideBalances(); // re-render when the toggle changes; formatters read the flag
  return (
    <div className="space-y-6">
      <EarningsTabs />
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Earnings</h1>
          <p className="text-sm text-slate-400">
            What your positions earn each day, claimed or not.
          </p>
        </div>
        <UpdatedAgo />
      </div>
      <CoverageCard />
      <EarningsOverview />
    </div>
  );
}

function EarningsOverview() {
  const { owner } = useOwner();
  const hidden = useHideBalances(); // alert text embeds formatted amounts, so rebuild it when the mask toggles
  const daily = useDailyEarnings(owner);
  const byPosition = useEarningsByPosition(owner);
  const open = usePositions(owner, "Open");
  const claims = useWeeklyClaims(owner);

  const days = daily.data;
  const txs = useMemo(() => claimTxs(claims.data ?? []), [claims.data, hidden]);
  const view = useMemo(() => {
    if (!days?.length) return null;
    const head = headlines(days);
    const flags = claimFlags(txs, formatUsd, head.today);
    return {
      head,
      flags,
      alerts: buildEarningsAlerts({
        days,
        byPosition: byPosition.data ?? [],
        positions: open.data ?? [],
        claims: txs,
        flags,
        usd: formatUsd,
      }),
      health: claimHealth(days, txs, open.data ?? []),
    };
  }, [days, byPosition.data, open.data, txs, hidden]);

  if (daily.error) return <ErrorText error={daily.error} />;
  if (daily.isLoading) return <PageSkeleton cards={4} />;
  if (!days?.length || !view) {
    return (
      <Empty>
        No earnings tracked yet. Daily earnings build up from hourly snapshots
        of open positions.
      </Empty>
    );
  }

  return (
    <div className="space-y-6">
      <EarningsAlerts alerts={view.alerts} />
      <HeadlineCards cards={view.head.cards} />
      <EarningsChart
        days={days}
        projection={view.head.projection}
        monthName={view.head.monthName}
      />
      <TokenBreakdown days={days} />
      <PositionBreakdown
        rows={byPosition.data ?? []}
        open={open.data ?? []}
        today={view.head.today}
        owner={owner}
      />
      <ClaimHealth stats={view.health} />
      <ClaimLog weeks={claims.data ?? []} flags={view.flags} />
    </div>
  );
}
