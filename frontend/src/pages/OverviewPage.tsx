import { useMemo } from "react";
import { Link } from "react-router-dom";
import { usePositions, useSummary } from "../api/client";
import { PortfolioBreakEvenCard } from "../components/BreakEvenCallout";
import { PortfolioValueChart } from "../components/PortfolioValueChart";
import { ExposurePanel } from "../components/PortfolioInsights";
import { EarningsChartPanel } from "../components/earnings/EarningsChartPanel";
import { MarketLine } from "../components/market/MarketLine";
import { AttentionStrip } from "../components/overview/AttentionStrip";
import { PositionRangeCards } from "../components/overview/PositionRangeCards";
import { UpdatedAgo } from "../components/UpdatedAgo";
import { StatCard } from "../components/ui";
import type { PortfolioSummaryDto, PositionDto } from "../api/types";
import { UNCLAIMED_ALERT_USD, buildAlerts } from "../lib/attention";
import { formatPercent, formatPrice, formatUsd, pnlClass } from "../lib/format";
import { useOwner } from "../lib/owner";
import { useHideBalances } from "../lib/privacy";

export function OverviewPage() {
  useHideBalances(); // re-render when the toggle changes; formatters read the flag
  const { owner } = useOwner();
  const summary = useSummary(owner, null);
  const open = usePositions(owner, "Open");
  const s = summary.data;

  const alerts = useMemo(
    () =>
      buildAlerts(
        open.data ?? [],
        (n) => formatUsd(n),
        (n) => formatPrice(n),
      ),
    [open.data],
  );

  return (
    <div className="space-y-5">
      <div className="flex items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Overview</h1>
          <p className="text-sm text-muted">
            Everything you provide liquidity to, across wallets and protocols.
          </p>
        </div>
        <UpdatedAgo />
      </div>

      {/* What needs you first, then what is coming, then the numbers, the two charts, and every position with its range. */}
      <div className="space-y-3">
        <AttentionStrip
          alerts={alerts}
          open={open.data ?? []}
          loading={open.isLoading}
          error={open.error}
        />
        <MarketLine />
      </div>

      <div className="grid grid-cols-1 gap-3 min-[480px]:grid-cols-2 lg:grid-cols-4">
        <StatCard
          label="Portfolio value"
          loading={summary.isLoading}
          value={formatUsd(s?.currentValueUsd)}
          hint={s && `${s.openCount} open · ${s.closedCount} closed`}
        />
        <StatCard
          label="Total P/L (all time)"
          loading={summary.isLoading}
          value={formatUsd(s?.pnlUsd, true)}
          valueClass={pnlClass(s?.pnlUsd)}
          hint={
            s && (
              <span
                title={`${formatPercent(
                  s.depositedUsd > 0 ? (s.pnlUsd / s.depositedUsd) * 100 : null,
                  true,
                )} on ${formatUsd(s.depositedUsd)} deposited`}
              >
                Fees {formatUsd(s.feesEarnedUsd)} · impermanent loss {formatUsd(s.impermanentLossUsd, true)}
                {s.hasMissingPrices && " · some prices missing"}
              </span>
            )
          }
        />
        <PortfolioBreakEvenCard owner={owner} />
        <Link
          to="/fees#ready-to-claim"
          className="block min-w-0 rounded-lg focus-visible:outline-2 focus-visible:outline-accent"
          aria-label="Unclaimed fees and rewards. Open the claim list."
        >
          <StatCard
            accent
            label="Unclaimed"
            loading={summary.isLoading || open.isLoading}
            value={formatUsd(s ? s.uncollectedFeesUsd + s.pendingRewardsUsd : 0)}
            hint={s && <>{unclaimedNote(s, open.data ?? [])} · see claims</>}
          />
        </Link>
      </div>

      <div className="grid gap-4 xl:grid-cols-2">
        <EarningsChartPanel />
        <PortfolioValueChart owner={owner} />
      </div>

      <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)] lg:items-start">
        <PositionRangeCards open={open.data ?? []} loading={open.isLoading} error={open.error} />
        <ExposurePanel />
      </div>
    </div>
  );
}

function unclaimedNote(s: PortfolioSummaryDto, open: PositionDto[]): string {
  const total = s.uncollectedFeesUsd + s.pendingRewardsUsd;
  const worthClaiming = open.filter(
    (p) => p.metrics.uncollectedFeesUsd + p.metrics.pendingRewardsUsd >= UNCLAIMED_ALERT_USD,
  ).length;
  return (
    `${worthClaiming} of ${open.length} open above ${formatUsd(UNCLAIMED_ALERT_USD)}` +
    (s.feesEarnedUsd > 0 ? ` · ${((total / s.feesEarnedUsd) * 100).toFixed(0)}% of fees earned` : "")
  );
}
