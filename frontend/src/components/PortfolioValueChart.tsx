import { useMemo } from "react";
import { usePortfolioHistory } from "../api/client";
import { rangeLabel } from "../lib/chartAxis";
import { useHideBalances } from "../lib/privacy";
import { usePersistentState } from "../lib/usePersistentState";
import { HistoryChart } from "./HistoryChart";
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
  useHideBalances(); // the chart formats amounts, so re-render when the mask toggles
  const { data, error, isLoading } = usePortfolioHistory(owner);

  const [metric, setMetric] = usePersistentState<"value" | "pnl">("lpdash.overview.metric", "value");
  const [days, setDays] = usePersistentState<number>("lpdash.overview.range", 0);
  const points = useMemo(() => {
    const from = days ? Date.now() - days * DAY : 0;
    return (data ?? [])
      .map((p) => ({
        t: new Date(p.timestamp).getTime(),
        v: p.valueUsd,
        v2: p.valueUsd + p.unclaimedUsd,
        positions: p.positionCount,
        estimated: p.estimated,
      }))
      .filter((p) => p.t >= from);
  }, [data, days]);

  return (
    <Panel
      title={metric === "pnl" ? "Cumulative P/L" : "Portfolio value"}
      actions={
        <div className="flex flex-wrap gap-2">
          <Segmented
            label="Metric"
            options={[
              { label: "Value", value: "value" as const },
              { label: "P/L", value: "pnl" as const },
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
          Not enough history yet. Snapshots are taken hourly from the first sync, so the chart fills in over time.
        </Empty>
      ) : (
        <>
          <HistoryChart
            points={points}
            name="Positions"
            name2="Positions + unclaimed fees"
            rangeLabel={rangeLabel(days)}
          />
          <p className="mt-1 text-xs text-faint">
            The line moves when a position is opened or withdrawn. Fees already claimed are not included.
          </p>
        </>
      )}
    </Panel>
  );
}
