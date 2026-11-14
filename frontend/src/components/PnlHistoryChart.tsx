import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Line,
  LineChart,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { usePnlHistory } from "../api/client";
import { chart } from "../lib/chartTheme";
import { weekdayOf } from "../lib/dates";
import { formatUsd } from "../lib/format";
import { Empty, ErrorText, Skeleton } from "./ui";

const DAY = 86_400_000;
const tip = {
  background: chart.tooltip.background,
  border: chart.tooltip.border,
};

/** Cumulative P/L (value + fees + withdrawals − deposits − gas) over time, from reconstructed history. */
export function CumulativePnlChart({
  owner,
  days,
}: {
  owner: string | null;
  days: number;
}) {
  const { data, error, isLoading } = usePnlHistory(owner);
  const from = days ? Date.now() - days * DAY : 0;
  const points = (data?.points ?? [])
    .map((p) => ({ t: new Date(p.timestamp).getTime(), pnl: p.cumulativePnlUsd }))
    .filter((p) => p.t >= from);

  if (error) return <ErrorText error={error} />;
  if (isLoading) return <Skeleton className="h-64 w-full" />;
  if (points.length < 2) return <Empty>Not enough history yet.</Empty>;
  return (
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
          <ReferenceLine y={0} stroke={chart.axis} strokeDasharray="3 3" />
          <Tooltip
            contentStyle={tip}
            labelFormatter={(t) => weekdayOf(t as number)}
            formatter={(v) => formatUsd(v as number)}
          />
          <Line dataKey="pnl" name="Cumulative P/L" stroke={chart.a} dot={false} />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}

/** Net P/L per calendar month: the change in cumulative P/L over the month. */
export function MonthlyPnlChart({ owner }: { owner: string | null }) {
  const { data, error, isLoading } = usePnlHistory(owner);
  const months = data?.months ?? [];
  if (error) return <ErrorText error={error} />;
  if (isLoading) return <Skeleton className="h-64 w-full" />;
  if (!months.length) return <Empty>No history yet.</Empty>;
  return (
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
          <ReferenceLine y={0} stroke={chart.axis} />
          <Tooltip
            cursor={{ fill: chart.cursor }}
            contentStyle={tip}
            formatter={(v) => formatUsd(v as number, true)}
          />
          <Bar dataKey="netPnlUsd" name="Net P/L">
            {months.map((m) => (
              <Cell
                key={m.month}
                fill={m.netPnlUsd >= 0 ? "var(--up)" : "var(--down)"}
              />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
