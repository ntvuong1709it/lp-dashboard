import { useMemo } from "react";
import { Link } from "react-router-dom";
import { usePositionDrawer } from "../lib/positionDrawer";
import { usePositions, useSummary } from "../api/client";
import { PortfolioBreakEvenCard } from "../components/BreakEvenCallout";
import { PortfolioValueChart } from "../components/PortfolioValueChart";
import { ExposurePanel, RewardTokensPanel } from "../components/PortfolioInsights";
import { NeedsYouCard } from "../components/overview/NeedsYouCard";
import { UpdatedAgo } from "../components/UpdatedAgo";
import { READY_TO_CLAIM_ID, ReadyToClaimPanel } from "../components/Unclaimed";
import { BarRow, Empty, Panel, Skeleton, StatCard } from "../components/ui";
import type { PortfolioSummaryDto, PositionDto } from "../api/types";
import { UNCLAIMED_ALERT_USD, buildAlerts, pairLabel } from "../lib/attention";
import {
  formatPercent,
  formatPrice,
  formatUsd,
  pnlClass,
  protocolLabels,
} from "../lib/format";
import { useOwner } from "../lib/owner";
import { useHideBalances } from "../lib/privacy";

export function OverviewPage() {
  useHideBalances(); // re-render when the toggle changes; formatters read the flag
  const { owner } = useOwner();
  const drawer = usePositionDrawer();
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

  const byPosition = useMemo(
    () =>
      [...(open.data ?? [])]
        .sort((a, b) => b.metrics.currentValueUsd - a.metrics.currentValueUsd)
        .map((p) => ({
          id: p.id,
          label: `${pairLabel(p)} #${p.tokenId}`,
          value: p.metrics.currentValueUsd,
        })),
    [open.data],
  );
  const byProtocol = useMemo(() => {
    const totals = new Map<string, number>();
    for (const p of open.data ?? []) {
      const key = `${protocolLabels[p.protocol]} · ${p.chain}`;
      totals.set(key, (totals.get(key) ?? 0) + p.metrics.currentValueUsd);
    }
    return [...totals.entries()]
      .map(([label, value]) => ({ id: label, label, value }))
      .sort((a, b) => b.value - a.value);
  }, [open.data]);

  return (
    <div className="space-y-6">
      <div className="flex items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Overview</h1>
          <p className="text-sm text-muted">
            Everything you provide liquidity to, across wallets and protocols.
          </p>
        </div>
        <UpdatedAgo />
      </div>

      {/* What needs you comes first: on a phone it sits above the numbers, from lg up it sits beside them. */}
      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_21rem] lg:items-start">
        <div className="lg:order-2">
          <NeedsYouCard
            alerts={alerts}
            open={open.data ?? []}
            loading={open.isLoading}
            error={open.error}
          />
        </div>

        <div className="min-w-0 space-y-4 lg:order-1">
          <div className="grid grid-cols-1 gap-3 min-[480px]:grid-cols-2 md:grid-cols-3">
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
                s &&
                `${formatPercent(
                  s.depositedUsd > 0 ? (s.pnlUsd / s.depositedUsd) * 100 : null,
                  true,
                )} on ${formatUsd(s.depositedUsd)} deposited`
              }
            />
            <StatCard
              label="Fees earned"
              loading={summary.isLoading}
              value={formatUsd(s?.feesEarnedUsd)}
              hint={s && "claimed + unclaimed"}
            />
            <StatCard
              label="Impermanent loss"
              loading={summary.isLoading}
              value={formatUsd(s?.impermanentLossUsd, true)}
              valueClass={pnlClass(s?.impermanentLossUsd)}
              hint={
                s?.hasMissingPrices ? "some prices missing" : "excluding fees"
              }
            />
            <StatCard
              accent
              label="Unclaimed"
              loading={summary.isLoading || open.isLoading}
              value={formatUsd(s ? s.uncollectedFeesUsd + s.pendingRewardsUsd : 0)}
              hint={s && unclaimedNote(s, open.data ?? [])}
            />
            <PortfolioBreakEvenCard owner={owner} />
          </div>

          <PortfolioValueChart owner={owner} />
        </div>
      </div>

      <div id={READY_TO_CLAIM_ID} className="scroll-mt-40">
        <ReadyToClaimPanel open={open.data ?? []} loading={open.isLoading} />
      </div>

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        <AllocationPanel
          title="Allocation by position"
          loading={open.isLoading}
          rows={byPosition}
          onSelect={(id) => drawer.open(Number(id))}
        />
        <AllocationPanel
          title="Allocation by protocol"
          loading={open.isLoading}
          rows={byProtocol}
          note="Closed positions are excluded here and counted in all-time P/L."
        />
        <ExposurePanel />
      </div>

      <RewardTokensPanel />
    </div>
  );
}

function unclaimedNote(
  s: PortfolioSummaryDto,
  open: PositionDto[],
): string {
  const total = s.uncollectedFeesUsd + s.pendingRewardsUsd;
  const worthClaiming = open.filter(
    (p) =>
      p.metrics.uncollectedFeesUsd + p.metrics.pendingRewardsUsd >=
      UNCLAIMED_ALERT_USD,
  ).length;
  return (
    `${worthClaiming} of ${open.length} open above ${formatUsd(UNCLAIMED_ALERT_USD)}` +
    (s.feesEarnedUsd > 0
      ? ` · ${((total / s.feesEarnedUsd) * 100).toFixed(0)}% of fees earned`
      : "")
  );
}

function AllocationPanel({
  title,
  rows,
  loading,
  onSelect,
  note,
}: {
  title: string;
  rows: { id: string | number; label: string; value: number }[];
  loading: boolean;
  onSelect?: (id: string | number) => void;
  note?: string;
}) {
  const total = rows.reduce((sum, r) => sum + r.value, 0);
  const max = Math.max(...rows.map((r) => r.value), 0);
  return (
    <Panel title={title}>
      {loading ? (
        <Skeleton className="h-40 w-full" />
      ) : !rows.length ? (
        <Empty>
          No open positions.{" "}
          <Link className="text-accent underline" to="/addresses">
            Add a wallet
          </Link>
        </Empty>
      ) : (
        <div className="space-y-2.5">
          {rows.map((r) => (
            <BarRow
              key={r.id}
              label={r.label}
              fraction={max > 0 ? r.value / max : 0}
              value={formatUsd(r.value)}
              sub={total > 0 ? `${((r.value / total) * 100).toFixed(0)}%` : ""}
              onClick={onSelect && (() => onSelect(r.id))}
            />
          ))}
        </div>
      )}
      {note && <p className="mt-4 text-xs text-faint">{note}</p>}
    </Panel>
  );
}
