import { type ReactNode, useId, useState } from "react";
import { Area, AreaChart, Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { usePoolHistory } from "../../api/client";
import type { PoolDto, PoolHistoryPointDto, PoolHistorySeriesDto } from "../../api/types";
import { chart } from "../../lib/chartTheme";
import { weekdayDate } from "../../lib/dates";
import { protocolLabels } from "../../lib/format";
import { UNRELIABLE_VOLUME_NOTE, apr, feeLabel, hasUnreliableVolume, usd } from "../../lib/pools";
import { ErrorText, Skeleton } from "../ui";
import { Sparkline } from "./Sparkline";

type MetricKey = "apr" | "tvl" | "volume" | "fees" | "emissions";

interface Metric {
  key: MetricKey;
  label: string;
  kind: "area" | "bar";
  pick: (p: PoolHistoryPointDto) => number | null;
  format: (v: number | null) => string;
  headline: (h: PoolHistorySeriesDto, pool: PoolDto) => number | null;
  /** True when the number is derived from APR and TVL rather than reported by the source. */
  derived?: boolean;
}

const metrics: Metric[] = [
  { key: "apr", label: "Avg APR", kind: "area", pick: (p) => p.feeApr, format: apr, headline: (h) => h.apr30d },
  { key: "tvl", label: "TVL", kind: "area", pick: (p) => p.tvlUsd, format: usd, headline: (_, pool) => pool.tvlUsd },
  { key: "volume", label: "Volume 30d", kind: "bar", pick: (p) => p.volumeUsd, format: usd, headline: (h) => h.volume30dUsd, derived: true },
  { key: "fees", label: "LP fees 30d", kind: "bar", pick: (p) => p.feesUsd, format: usd, headline: (h) => h.fees30dUsd, derived: true },
  {
    key: "emissions",
    label: "Emissions APR",
    kind: "area",
    pick: (p) => p.rewardApr,
    format: apr,
    headline: (h) => {
      const rates = h.points.map((p) => p.rewardApr).filter((r): r is number => r != null);
      return rates.length ? rates.reduce((s, r) => s + r, 0) / rates.length : null;
    },
  },
];

const shortDate = (iso: string) =>
  new Date(`${iso}T00:00:00Z`).toLocaleDateString("en-US", { month: "short", day: "numeric", timeZone: "UTC" });

/**
 * What opens under a pool row: tiles for the pool's 30-day trends (click one for its chart), then the numbers that
 * do not change by the day. Fees and volume are worked out from TVL and the fee APR, and labelled as such.
 */
export function PoolDetail({ pool }: { pool: PoolDto }) {
  const { data, error, isLoading } = usePoolHistory(pool.id, pool.feePercent);
  const [selected, setSelected] = useState<MetricKey>("apr");

  const available = data ? metrics.filter((m) => m.key !== "emissions" || data.points.some((p) => p.rewardApr != null && p.rewardApr > 0)) : [];
  const metric = available.find((m) => m.key === selected) ?? available[0];

  return (
    <div className="space-y-4 p-4">
      {hasUnreliableVolume(pool) && (
        <p className="rounded-md border border-warn/40 bg-warn/10 px-3 py-2 text-xs text-warn">
          {UNRELIABLE_VOLUME_NOTE}
        </p>
      )}
      {error ? (
        <ErrorText error={error} />
      ) : isLoading || !data ? (
        <Skeleton className="h-64 w-full" />
      ) : data.points.length < 2 || !metric ? (
        <p className="py-6 text-center text-sm text-faint">This pool has no daily history yet.</p>
      ) : (
        <>
          {data.sourceTvlUsd != null && data.tvlUsd != null && (
            <p className="rounded-md border border-warn/40 bg-warn/10 px-3 py-2 text-xs text-warn">
              TVL is read from the pool on-chain: {usd(data.tvlUsd)}. The data source reports {usd(data.sourceTvlUsd)}, and the daily
              history below is the source's own, so recent days of TVL, APR, fees and volume are off by that gap.
            </p>
          )}
          <div role="group" aria-label="Metric" className="grid grid-cols-2 gap-2 sm:grid-cols-3 xl:grid-cols-5">
            {available.map((m) => (
              <MetricTile
                key={m.key}
                metric={m}
                pool={pool}
                history={data}
                active={m.key === metric.key}
                onSelect={() => setSelected(m.key)}
              />
            ))}
          </div>
          <MetricChart key={metric.key} metric={metric} pool={pool} history={data} />
        </>
      )}

      <div className="grid gap-px overflow-hidden rounded-lg border border-line bg-raised text-sm md:grid-cols-3">
        <StatGroup title="Liquidity">
          <Stat label="TVL" value={usd(pool.tvlUsd)} />
          <Stat label="Volume 24h" value={usd(pool.volume1dUsd)} />
          <Stat label="Volume / TVL" value={pool.volumeToTvl == null ? "—" : `${pool.volumeToTvl.toFixed(2)}×`} />
        </StatGroup>
        <StatGroup title="Fees & pool">
          <Stat label="Fee tier" value={feeLabel(pool.feePercent)} />
          <Stat label="Fees, last 7 days" value={usd(pool.fees7dUsd)} />
          <Stat label="Fee APR 24h / 7d" value={`${apr(pool.feeApr1d)} / ${apr(pool.feeApr7d)}`} />
        </StatGroup>
        <StatGroup title="Platform">
          <Stat label="Protocol" value={`${protocolLabels[pool.protocol]} on ${pool.chain}`} />
          <Stat label="Tick spacing" value={pool.tickSpacing == null ? "—" : String(pool.tickSpacing)} />
          <Stat label="Emissions APR" value={apr(pool.rewardApr)} />
        </StatGroup>
      </div>
    </div>
  );
}

function MetricTile({
  metric: m,
  pool,
  history,
  active,
  onSelect,
}: {
  metric: Metric;
  pool: PoolDto;
  history: PoolHistorySeriesDto;
  active: boolean;
  onSelect: () => void;
}) {
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={onSelect}
      className={`rounded-lg border p-3 text-left transition-colors ${active ? "border-accent-fill bg-accent/10" : "border-line bg-panel hover:bg-raised/60"}`}
    >
      <span className={`block text-xs ${active ? "text-accent" : "text-muted"}`}>{m.label}</span>
      <span className="num block text-lg font-semibold">{m.format(m.headline(history, pool))}</span>
      <span className={m.key === "apr" || m.key === "emissions" ? "text-up" : "text-accent"}>
        <Sparkline values={history.points.map(m.pick)} kind={m.kind === "bar" ? "bar" : "line"} />
      </span>
    </button>
  );
}

function MetricChart({ metric: m, pool, history }: { metric: Metric; pool: PoolDto; history: PoolHistorySeriesDto }) {
  const gradient = useId();
  const data = history.points.map((p) => ({ date: p.date, v: m.pick(p) }));
  const days = data.filter((d) => d.v != null).length;
  const latest = [...data].reverse().find((d) => d.v != null);
  const color = m.key === "apr" || m.key === "emissions" ? chart.b : chart.a;
  const tip = { background: chart.tooltip.background, border: chart.tooltip.border };
  const axes = (
    <>
      <CartesianGrid stroke={chart.grid} vertical={false} strokeDasharray="3 3" />
      <XAxis dataKey="date" tickFormatter={shortDate} stroke={chart.axis} fontSize={11} minTickGap={32} />
      <YAxis orientation="right" stroke={chart.axis} fontSize={11} width={56} tickFormatter={(v) => m.format(v as number)} domain={m.kind === "bar" ? [0, "auto"] : ["auto", "auto"]} />
      <Tooltip
        contentStyle={tip}
        cursor={{ fill: chart.cursor, stroke: chart.axis }}
        labelFormatter={(d) => weekdayDate(String(d))}
        formatter={(v) => [m.format(v as number), m.label]}
      />
    </>
  );

  return (
    <div className="rounded-lg border border-line bg-panel p-3">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <div>
          <div className="num text-2xl font-semibold">{m.format(m.headline(history, pool))}</div>
          <div className="text-xs text-faint">
            {m.label}
            {latest && <> · latest {m.format(latest.v)} on {shortDate(latest.date)}</>}
          </div>
        </div>
        <span className="rounded-md border border-warn/40 bg-warn/10 px-2 py-0.5 text-xs text-warn">
          {days} of {history.points.length} days
        </span>
      </div>
      <div className="mt-2 h-52" aria-label={`${m.label}, last ${history.points.length} days`}>
        <ResponsiveContainer>
          {m.kind === "bar" ? (
            <BarChart data={data}>
              {axes}
              <Bar dataKey="v" name={m.label} fill={color} radius={[2, 2, 0, 0]} />
            </BarChart>
          ) : (
            <AreaChart data={data}>
              <defs>
                <linearGradient id={gradient} x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor={color} stopOpacity={0.35} />
                  <stop offset="100%" stopColor={color} stopOpacity={0} />
                </linearGradient>
              </defs>
              {axes}
              <Area dataKey="v" name={m.label} type="monotone" stroke={color} strokeWidth={2} fill={`url(#${gradient})`} connectNulls />
            </AreaChart>
          )}
        </ResponsiveContainer>
      </div>
      {m.derived && (
        <p className="mt-1 text-xs text-faint">
          Worked out from each day's TVL and fee APR at the {feeLabel(history.feePercent)} fee tier, not measured volume.
        </p>
      )}
    </div>
  );
}

function StatGroup({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="bg-panel p-3">
      <h3 className="mb-1 text-xs font-medium uppercase tracking-[.05em] text-muted">{title}</h3>
      <dl className="space-y-1">{children}</dl>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <dt className="text-muted">{label}</dt>
      <dd className="num text-right font-medium">{value}</dd>
    </div>
  );
}
