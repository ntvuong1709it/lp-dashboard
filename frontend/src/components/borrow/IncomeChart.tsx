import { useMemo } from "react";
import {
  Area,
  Bar,
  CartesianGrid,
  ComposedChart,
  Line,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import type { DailyCoverageDto } from "../../api/types";
import { shortDay } from "../../lib/borrow";
import { chart as theme } from "../../lib/chartTheme";
import { weekdayDate } from "../../lib/dates";
import { formatUsd } from "../../lib/format";
import { Panel } from "../ui";

const tip = { background: theme.tooltip.background, border: theme.tooltip.border };

/** Daily LP income (stacked) against daily interest, and the running net carry underneath. */
export function IncomeChart({ days }: { days: DailyCoverageDto[] }) {
  const data = useMemo(() => {
    let cumulative = 0;
    return days.map((d) => {
      cumulative += d.netUsd;
      return { ...d, cumulative };
    });
  }, [days]);

  return (
    <Panel
      title="Income against interest, per day"
      actions={
        <div className="flex flex-wrap gap-x-3 text-xs text-muted">
          <Key color="var(--chart-a)">Fees</Key>
          <Key color="var(--chart-d)">Rewards</Key>
          <Key color="var(--chart-b)">Aave supply yield</Key>
          <Key color="var(--down)">Interest</Key>
        </div>
      }
    >
      <div className="h-60">
        <ResponsiveContainer>
          <ComposedChart data={data}>
            <CartesianGrid stroke={theme.grid} vertical={false} />
            <XAxis dataKey="date" stroke={theme.axis} fontSize={11} tickFormatter={shortDay} minTickGap={28} />
            <YAxis stroke={theme.axis} fontSize={11} tickFormatter={(v) => formatUsd(v)} width={64} />
            <Tooltip
              cursor={{ fill: theme.cursor }}
              contentStyle={tip}
              labelFormatter={(d) => weekdayDate(String(d))}
              formatter={(v, name) => [formatUsd(v as number), name]}
            />
            <Bar dataKey="feesUsd" name="Swap fees" stackId="a" fill={theme.a} />
            <Bar dataKey="rewardsUsd" name="Rewards" stackId="a" fill={theme.d} />
            <Bar dataKey="supplyYieldUsd" name="Supply yield" stackId="a" fill={theme.b} />
            <Line dataKey="interestUsd" name="Interest" stroke="var(--down)" strokeWidth={2} dot={false} />
          </ComposedChart>
        </ResponsiveContainer>
      </div>
      <div className="mb-1 mt-3 flex items-baseline justify-between text-xs">
        <span className="uppercase tracking-[.05em] text-muted">Cumulative net carry</span>
        <span className="text-faint">after interest and gas</span>
      </div>
      <div className="h-28">
        <ResponsiveContainer>
          <ComposedChart data={data}>
            <CartesianGrid stroke={theme.grid} vertical={false} />
            <XAxis dataKey="date" stroke={theme.axis} fontSize={11} tickFormatter={shortDay} minTickGap={28} />
            <YAxis stroke={theme.axis} fontSize={11} tickFormatter={(v) => formatUsd(v)} width={64} />
            <ReferenceLine y={0} stroke={theme.axis} strokeDasharray="3 3" />
            <Tooltip
              contentStyle={tip}
              labelFormatter={(d) => weekdayDate(String(d))}
              formatter={(v) => [formatUsd(v as number, true), "Net carry"]}
            />
            <Area dataKey="cumulative" name="Net carry" stroke={theme.a} fill={theme.a} fillOpacity={0.15} strokeWidth={2} />
          </ComposedChart>
        </ResponsiveContainer>
      </div>
    </Panel>
  );
}

function Key({ color, children }: { color: string; children: string }) {
  return (
    <span>
      <span className="mr-1.5 inline-block h-2 w-2 rounded-sm" style={{ background: color }} />
      {children}
    </span>
  );
}
