import { useMemo } from "react";
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
import { Empty, ErrorText, Panel, Skeleton } from "./ui";

/** Total value of every position (any status) over time, from hourly snapshots. */
export function PortfolioValueChart({ owner }: { owner: string | null }) {
  const { data, error, isLoading } = usePortfolioHistory(owner);

  const points = useMemo(
    () =>
      (data ?? []).map((p) => ({
        t: new Date(p.timestamp).getTime(),
        value: p.valueUsd,
        withUnclaimed: p.valueUsd + p.unclaimedUsd,
        count: p.positionCount,
      })),
    [data],
  );

  return (
    <Panel title="Portfolio value over time">
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
                <CartesianGrid stroke="#1e293b" />
                <XAxis
                  dataKey="t"
                  type="number"
                  domain={["dataMin", "dataMax"]}
                  scale="time"
                  tickFormatter={(t) => new Date(t).toLocaleDateString()}
                  stroke="#64748b"
                  fontSize={11}
                />
                <YAxis
                  stroke="#64748b"
                  fontSize={11}
                  tickFormatter={(v) => formatUsd(v)}
                  width={80}
                />
                <Tooltip
                  contentStyle={{
                    background: "#0f172a",
                    border: "1px solid #334155",
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
                  stroke="#38bdf8"
                  dot={false}
                />
                <Line
                  type="stepAfter"
                  dataKey="withUnclaimed"
                  name="Positions + unclaimed fees"
                  stroke="#34d399"
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
