import { chart } from "../lib/chartTheme";
import { useMemo } from "react";
import { usePersistentState } from "../lib/usePersistentState";
import {
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { usePortfolioHistory } from "../api/client";
import { formatUsd } from "../lib/format";
import { Empty, ErrorText, Panel, Segmented, Skeleton } from "./ui";

const DAY = 86_400_000;
const ranges = [
  { label: "7D", value: 7 },
  { label: "30D", value: 30 },
  { label: "90D", value: 90 },
  { label: "All", value: 0 },
];

/** Total value of every position (any status) over time, from hourly snapshots. */
export function PortfolioValueChart({ owner }: { owner: string | null }) {
  const { data, error, isLoading } = usePortfolioHistory(owner);

  const [days, setDays] = usePersistentState<number>("lpdash.overview.range", 0);
  const points = useMemo(
    () => {
      const from = days ? Date.now() - days * DAY : 0;
      return (data ?? []).filter((p) => new Date(p.timestamp).getTime() >= from).map((p) => ({
        t: new Date(p.timestamp).getTime(),
        value: p.valueUsd,
        withUnclaimed: p.valueUsd + p.unclaimedUsd,
        count: p.positionCount,
      }));
    },
    [data, days],
  );

  return (
    <Panel
      title="Portfolio value over time"
      actions={<Segmented label="Time range" options={ranges} value={days} onChange={setDays} />}
    >
      {error ? (
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
                    return `${new Date(t as number).toLocaleString()}${count ? ` · ${count} positions` : ""}`;
                  }}
                  formatter={(v) => formatUsd(v as number)}
                />
                <Legend />
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
          <p className="mt-2 text-xs text-slate-500">
            History starts at each position&apos;s first sync (it is not
            reconstructed), so the line steps up when a position is first
            tracked and drops when one is withdrawn. Fees already claimed are
            not included.
          </p>
        </>
      )}
    </Panel>
  );
}
