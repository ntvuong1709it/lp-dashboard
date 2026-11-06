import { useMemo } from "react";
import { usePositionDrawer } from "../lib/positionDrawer";
import {
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { useDailyEarnings, usePositions } from "../api/client";
import type { PositionDto } from "../api/types";
import { MonthlyPnlChart } from "../components/PnlHistoryChart";
import { UpdatedAgo } from "../components/UpdatedAgo";
import {
  BarRow,
  Empty,
  ErrorText,
  Panel,
  Skeleton,
  StatCard,
  Waterfall,
} from "../components/ui";
import { pairLabel } from "../lib/attention";
import { chart } from "../lib/chartTheme";
import { formatUsd, pnlClass } from "../lib/format";
import { useOwner } from "../lib/owner";
import { useHideBalances } from "../lib/privacy";

const sum = (positions: PositionDto[], pick: (p: PositionDto) => number) =>
  positions.reduce((total, p) => total + pick(p), 0);

export function PnlPage() {
  useHideBalances(); // re-render when the toggle changes; formatters read the flag
  const { owner } = useOwner();
  const { data, error, isLoading } = usePositions(owner, null);

  const t = useMemo(() => {
    const all = data ?? [];
    const open = all.filter((p) => p.status === "Open");
    const closed = all.filter((p) => p.status === "Closed");
    const pnl = sum(all, (p) => p.metrics.pnlUsd);
    const il = sum(all, (p) => p.metrics.impermanentLossUsd);
    const earned = sum(all, (p) => p.metrics.feesEarnedUsd);
    const rewards = sum(all, (p) => p.metrics.claimedRewardsUsd + p.metrics.pendingRewardsUsd);
    const fees = earned - rewards;
    const gas = sum(all, (p) => p.metrics.gasUsd);
    return {
      all,
      pnl,
      il,
      fees,
      rewards,
      gas,
      // Residual, so the breakdown always adds up to the P/L figure.
      priceChange: pnl - il - earned + gas,
      unrealized: sum(open, (p) => p.metrics.pnlUsd),
      realized: sum(closed, (p) => p.metrics.pnlUsd),
      vsHodl: sum(all, (p) => p.metrics.pnlVsHodlUsd),
    };
  }, [data]);

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Profit &amp; loss
          </h1>
          <p className="text-sm text-slate-400">
            Where your return comes from, and which positions drive it.
          </p>
        </div>
        <div className="flex items-center gap-3 text-sm text-slate-400">
          <UpdatedAgo />
        </div>
      </div>

      {error ? (
        <ErrorText error={error} />
      ) : (
        <>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <StatCard
              label="Net P/L"
              loading={isLoading}
              value={formatUsd(t.pnl, true)}
              valueClass={pnlClass(t.pnl)}
              hint="All positions"
            />
            <StatCard
              label="Unrealized"
              loading={isLoading}
              value={formatUsd(t.unrealized, true)}
              valueClass={pnlClass(t.unrealized)}
              hint="Open positions"
            />
            <StatCard
              label="Realized"
              loading={isLoading}
              value={formatUsd(t.realized, true)}
              valueClass={pnlClass(t.realized)}
              hint="Closed positions"
            />
            <StatCard
              label="LP vs just holding"
              loading={isLoading}
              value={formatUsd(t.vsHodl, true)}
              valueClass={pnlClass(t.vsHodl)}
              hint="Impermanent loss + fees − gas"
            />
          </div>

          <div className="grid gap-4 md:grid-cols-2">
            <Panel title="P/L breakdown">
              {isLoading ? (
                <Skeleton className="h-40 w-full" />
              ) : (
                <Waterfall
                  totalLabel="Net P/L"
                  format={(n) => formatUsd(n, true)}
                  pnlClass={pnlClass}
                  steps={[
                    { label: "Token price change", value: t.priceChange },
                    { label: "Impermanent loss", value: t.il },
                    { label: "Swap fees", value: t.fees },
                    { label: "Reward tokens", value: t.rewards },
                    { label: "Gas spent", value: -t.gas },
                  ]}
                  total={t.pnl}
                />
              )}
              <p className="mt-4 text-xs text-slate-500">
                Token price change is what the deposited tokens gained or lost
                if simply held. Impermanent loss is the gap between the pool
                position and holding. Swap fees and reward tokens (such as
                AERO) both include claimed and unclaimed amounts.
              </p>
            </Panel>
            <PnlByPosition positions={t.all} loading={isLoading} />
          </div>

          <Panel title="Monthly net P/L">
            <MonthlyPnlChart owner={owner} />
          </Panel>

          <MonthlyFees />

          <Panel title="How P/L is calculated">
            <p className="max-w-prose text-sm text-slate-400">
              Net P/L = current value + withdrawn + fees earned − deposits −
              gas. Deposits and withdrawals are valued in USD at the time of
              each transaction.
            </p>
          </Panel>
        </>
      )}
    </div>
  );
}

function PnlByPosition({
  positions,
  loading,
}: {
  positions: PositionDto[];
  loading: boolean;
}) {
  const drawer = usePositionDrawer();
  const sorted = [...positions].sort((a, b) => b.metrics.pnlUsd - a.metrics.pnlUsd);
  const max = Math.max(...sorted.map((p) => Math.abs(p.metrics.pnlUsd)), 1);
  return (
    <Panel title="P/L by position">
      {loading ? (
        <Skeleton className="h-40 w-full" />
      ) : !sorted.length ? (
        <Empty>No positions yet.</Empty>
      ) : (
        <div className="max-h-96 space-y-2.5 overflow-y-auto">
          {sorted.map((p) => (
            <BarRow
              key={p.id}
              label={`${pairLabel(p)} #${p.tokenId}`}
              fraction={Math.abs(p.metrics.pnlUsd) / max}
              barClass={p.metrics.pnlUsd >= 0 ? "bg-emerald-500/80" : "bg-rose-500/80"}
              value={formatUsd(p.metrics.pnlUsd, true)}
              valueClass={pnlClass(p.metrics.pnlUsd)}
              onClick={() => drawer.open(p.id)}
            />
          ))}
        </div>
      )}
    </Panel>
  );
}

/** Daily earnings (fees + rewards, claimed or not) rolled up by calendar month. */
function MonthlyFees() {
  const { owner } = useOwner();
  const { data, error, isLoading } = useDailyEarnings(owner);
  const months = useMemo(() => {
    const totals = new Map<string, { fees: number; rewards: number }>();
    for (const d of data ?? []) {
      const key = d.date.slice(0, 7);
      const m = totals.get(key) ?? { fees: 0, rewards: 0 };
      m.fees += d.feesUsd;
      m.rewards += d.rewardsUsd;
      totals.set(key, m);
    }
    return [...totals.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([month, v]) => ({ month, ...v }));
  }, [data]);

  return (
    <Panel title="Earned per month (fees and rewards, incl. unclaimed)">
      {error ? (
        <ErrorText error={error} />
      ) : isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : !months.length ? (
        <Empty>No earnings tracked yet.</Empty>
      ) : (
        <div className="h-64">
          <ResponsiveContainer>
            <BarChart data={months}>
              <CartesianGrid stroke={chart.grid} vertical={false} />
              <XAxis dataKey="month" stroke={chart.axis} fontSize={11} />
              <YAxis
                stroke={chart.axis}
                fontSize={11}
                tickFormatter={(v) => formatUsd(v)}
                width={80}
              />
              <Tooltip
                cursor={{ fill: chart.cursor }}
                contentStyle={{
                  background: chart.tooltip.background,
                  border: chart.tooltip.border,
                }}
                formatter={(v) => formatUsd(v as number)}
              />
              <Bar dataKey="fees" name="Swap fees" stackId="a" fill={chart.b} />
              <Bar dataKey="rewards" name="Rewards" stackId="a" fill={chart.a} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      )}
    </Panel>
  );
}
