import { useMemo } from "react";
import { Link } from "react-router-dom";
import { usePositionDrawer } from "../lib/positionDrawer";
import { usePositions, useSummary } from "../api/client";
import { PortfolioValueChart } from "../components/PortfolioValueChart";
import { ExposurePanel, RewardTokensPanel } from "../components/PortfolioInsights";
import { UpdatedAgo } from "../components/UpdatedAgo";
import { ReadyToClaimPanel, UnclaimedCard, rewardTotals } from "../components/Unclaimed";
import {
  BarRow,
  Empty,
  ErrorText,
  Panel,
  Skeleton,
  StatCard,
} from "../components/ui";
import type { PortfolioSummaryDto, PositionDto } from "../api/types";
import {
  UNCLAIMED_ALERT_USD,
  buildAlerts,
  type AlertKind,
} from "../lib/attention";
import {
  formatPercent,
  formatPrice,
  formatUsd,
  pnlClass,
  protocolLabels,
} from "../lib/format";
import { useOwner } from "../lib/owner";
import { useHideBalances } from "../lib/privacy";
import { pairLabel } from "../lib/attention";

/** A text label per kind, each with its own colour, so a row is readable without decoding an icon. */
const alertStyle: Record<AlertKind, { label: string; className: string }> = {
  out: { label: "Out of range", className: "border-rose-500/50 bg-rose-500/10 text-rose-300" },
  edge: { label: "Near edge", className: "border-orange-500/50 bg-orange-500/10 text-orange-300" },
  unclaimed: { label: "Unclaimed", className: "border-sky-500/50 bg-sky-500/10 text-sky-300" },
  prices: { label: "No price", className: "border-slate-500/50 bg-slate-500/10 text-slate-300" },
};

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
          <p className="text-sm text-slate-400">
            Everything you provide liquidity to, across wallets and protocols.
          </p>
        </div>
        <UpdatedAgo />
      </div>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-[repeat(4,minmax(0,1fr))_minmax(0,1.3fr)]">
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
        <UnclaimedCard
          fees={s?.uncollectedFeesUsd ?? 0}
          rewards={s?.pendingRewardsUsd ?? 0}
          rewardTokens={rewardTotals(open.data ?? [])}
          note={s && unclaimedNote(s, open.data ?? [])}
          loading={summary.isLoading || open.isLoading}
          className="sm:col-span-2 lg:col-span-1"
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(18rem,1fr)]">
        <ReadyToClaimPanel open={open.data ?? []} loading={open.isLoading} />
        <AttentionPanel
          loading={open.isLoading}
          error={open.error}
          alerts={alerts}
        />
      </div>

      <PortfolioValueChart owner={owner} />

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

function AttentionPanel({
  alerts,
  loading,
  error,
}: {
  alerts: ReturnType<typeof buildAlerts>;
  loading: boolean;
  error: unknown;
}) {
  const drawer = usePositionDrawer();
  return (
    <Panel
      title="Needs attention"
      actions={<span className="text-sm text-slate-400">{alerts.length}</span>}
    >
      {error ? (
        <ErrorText error={error} />
      ) : loading ? (
        <Skeleton className="h-40 w-full" />
      ) : !alerts.length ? (
        <p className="text-sm text-slate-400">Nothing needs attention.</p>
      ) : (
        <ul className="max-h-96 divide-y divide-slate-800 overflow-y-auto">
          {alerts.map((a) => (
            <li
              key={`${a.kind}-${a.position.id}`}
              className="flex gap-3 py-2.5 first:pt-0 last:pb-0"
            >
              <span
                className={`mt-0.5 h-fit w-24 flex-none rounded-full border px-2 py-0.5 text-center text-[11px] font-semibold ${alertStyle[a.kind].className}`}
              >
                {alertStyle[a.kind].label}
              </span>
              <div className="text-sm">
                <div>{a.title}</div>
                <div className="text-xs text-slate-400">
                  {a.detail}{" "}
                  <button
                    type="button"
                    className="font-medium text-sky-400 hover:underline"
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
    </Panel>
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
          <Link className="text-sky-400 underline" to="/addresses">
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
      {note && <p className="mt-4 text-xs text-slate-500">{note}</p>}
    </Panel>
  );
}
