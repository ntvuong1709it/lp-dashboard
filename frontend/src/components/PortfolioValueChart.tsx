import { chart } from "../lib/chartTheme";
import { weekdayDateTime } from "../lib/dates";
import { useMemo } from "react";
import { usePersistentState } from "../lib/usePersistentState";
import {
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ReferenceArea,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { usePortfolioHistory } from "../api/client";
import { formatUsd } from "../lib/format";
import { CumulativePnlChart } from "./PnlHistoryChart";
import { Empty, ErrorText, Panel, Segmented, Skeleton } from "./ui";

const DAY = 86_400_000;
const ranges = [
  { label: "7D", value: 7 },
  { label: "30D", value: 30 },
  { label: "90D", value: 90 },
  { label: "All", value: 0 },
];

/** Total value of every position (any status) over time: hourly snapshots, plus reconstructed values before the first sync. */
export function PortfolioValueChart({ owner }: { owner: string | null }) {
  const { data, error, isLoading } = usePortfolioHistory(owner);

  const [metric, setMetric] = usePersistentState<"value" | "pnl">("lpdash.overview.metric", "value");
  const [days, setDays] = usePersistentState<number>("lpdash.overview.range", 0);
  const points = useMemo(
    () => {
      const from = days ? Date.now() - days * DAY : 0;
      return (data ?? []).filter((p) => new Date(p.timestamp).getTime() >= from).map((p) => ({
        t: new Date(p.timestamp).getTime(),
        value: p.valueUsd,
        withUnclaimed: p.valueUsd + p.unclaimedUsd,
        count: p.positionCount,
        estimated: p.estimated,
      }));
    },
    [data, days],
  );
  /** Span covered at least partly by reconstructed values; measured snapshots take over after it. */
  const estimatedUntil = useMemo(
    () => points.reduce((last, p) => (p.estimated ? p.t : last), 0),
    [points],
  );

  return (
    <Panel
      title={metric === "pnl" ? "Cumulative P/L" : "Portfolio value over time"}
      actions={
        <div className="flex gap-2">
          <Segmented
            label="Metric"
            options={[
              { label: "Value", value: "value" as const },
              { label: "Cumulative P/L", value: "pnl" as const },
            ]}
            value={metric}
            onChange={setMetric}
          />
          <Segmented label="Time range" options={ranges} value={days} onChange={setDays} />
        </div>
      }
    >
      {metric === "pnl" ? (
        <CumulativePnlChart owner={owner} days={days} />
      ) : error ? (
        <ErrorText error={error} />
      ) : isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : points.length < 2 ? (
        <Empty>
          Not enough history yet. Snapshots are taken hourly from the first
          sync, so the chart fills in over time.
        </Empty>
      ) : (
        <>
          <div className="h-64">
            <ResponsiveContainer>
              <LineChart data={points}>
                <CartesianGrid stroke={chart.grid} />
                <XAxis
                  dataKey="t"
                  type="number"
                  domain={["dataMin", "dataMax"]}
                  scale="time"
                  tickFormatter={(t) => new Date(t).toLocaleDateString()}
                  stroke={chart.axis}
                  fontSize={11}
                />
                <YAxis
                  stroke={chart.axis}
                  fontSize={11}
                  tickFormatter={(v) => formatUsd(v)}
                  width={80}
                />
                <Tooltip
                  contentStyle={{
                    background: chart.tooltip.background,
                    border: chart.tooltip.border,
                  }}
                  labelFormatter={(t, items) => {
                    const count = items?.[0]?.payload?.count as
                      | number
                      | undefined;
                    const estimated = items?.[0]?.payload?.estimated as boolean | undefined;
                    return `${weekdayDateTime(t as number)}${count ? ` · ${count} positions` : ""}${estimated ? " · estimated" : ""}`;
                  }}
                  formatter={(v) => formatUsd(v as number)}
                />
                <Legend />
                {estimatedUntil > points[0].t && (
                  <ReferenceArea
                    x1={points[0].t}
                    x2={estimatedUntil}
                    fill={chart.axis}
                    fillOpacity={0.12}
                    ifOverflow="hidden"
                  />
                )}
                <Line
                  type="stepAfter"
                  dataKey="value"
                  name="Positions"
                  stroke={chart.a}
                  dot={false}
                />
                <Line
                  type="stepAfter"
                  dataKey="withUnclaimed"
                  name="Positions + unclaimed fees"
                  stroke={chart.b}
                  dot={false}
                />
              </LineChart>
            </ResponsiveContainer>
          </div>
          <p className="mt-2 text-xs text-faint">
            {estimatedUntil > points[0].t
              ? "The shaded part is estimated: before a position's first sync its value is rebuilt from its deposits and historical token prices (hourly), so it can be off by a percent or so and leaves out unclaimed fees. "
              : ""}
            The line steps when a position is opened or withdrawn. Fees
            already claimed are not included.
          </p>
        </>
      )}
    </Panel>
  );
}
