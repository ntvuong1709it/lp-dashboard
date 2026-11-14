import { useMemo } from "react";
import {
  Bar,
  CartesianGrid,
  Cell,
  ComposedChart,
  Line,
  ReferenceDot,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import type { DailyEarningsDto } from "../../api/types";
import { chart as theme } from "../../lib/chartTheme";
import { weekdayDate } from "../../lib/dates";
import { chartSeries, grainAverage, shortDate, type ChartPoint, type Grain } from "../../lib/earnings";
import { formatUsd } from "../../lib/format";
import { usePersistentState } from "../../lib/usePersistentState";
import { Panel, Segmented } from "../ui";

const GRAINS: { label: string; value: Grain }[] = [
  { label: "Day", value: "day" },
  { label: "Week", value: "week" },
  { label: "Month", value: "month" },
];

/** Ranges are counted in buckets of the chosen grain; 0 shows everything. */
const RANGES: Record<Grain, { label: string; value: number }[]> = {
  day: [
    { label: "7D", value: 7 },
    { label: "30D", value: 30 },
    { label: "90D", value: 90 },
    { label: "All", value: 0 },
  ],
  week: [
    { label: "8W", value: 8 },
    { label: "12W", value: 12 },
    { label: "26W", value: 26 },
    { label: "All", value: 0 },
  ],
  month: [
    { label: "6M", value: 6 },
    { label: "12M", value: 12 },
    { label: "All", value: 0 },
  ],
};

const TITLES: Record<Grain, string> = {
  day: "Earned per day",
  week: "Earned per week",
  month: "Earned per month",
};

const monthLabel = (d: string, long = false) =>
  new Date(`${d}T00:00:00Z`).toLocaleDateString(undefined, {
    month: long ? "long" : "short",
    year: long || d.slice(5, 7) === "01" ? "numeric" : undefined,
    timeZone: "UTC",
  });

/** What the axis and tooltip call a bucket. */
function tickLabel(grain: Grain, date: string) {
  return grain === "month" ? monthLabel(date) : shortDate(date);
}

/**
 * How much of today has passed in the browser's zone (the zone the daily series is bucketed in).
 * Measured from local midnight, so a DST day correctly counts 23 or 25 hours.
 */
function todayHours(now = new Date()) {
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const end = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1);
  const hours = (ms: number) => ms / 3_600_000;
  const total = hours(end.getTime() - start.getTime());
  const elapsed = Math.min(total, hours(now.getTime() - start.getTime()));
  return { elapsed, left: total - elapsed, total };
}

const oneDecimal = (n: number) => (Math.round(n * 10) / 10).toString();

/** "14.2/24 h, 9.8 h left" */
function soFarText() {
  const { elapsed, left, total } = todayHours();
  return `${oneDecimal(elapsed)}/${oneDecimal(total)} h, ${oneDecimal(left)} h left`;
}

function tipLabel(grain: Grain, p: ChartPoint) {
  const text =
    grain === "day"
      ? weekdayDate(p.date)
      : grain === "week"
        ? `Week of ${shortDate(p.date)} – ${shortDate(p.end)}`
        : monthLabel(p.date, true);
  if (!p.partial) return text;
  return grain === "day" ? `${text} (${soFarText()})` : `${text} (so far)`;
}

function Key({ color, children }: { color: string; children: string }) {
  return (
    <span>
      <span className="mr-1.5 inline-block h-2 w-2 rounded-sm" style={{ background: color }} />
      {children}
    </span>
  );
}

export function EarningsChart({
  days,
  projection,
  monthName,
}: {
  days: DailyEarningsDto[];
  projection: number | null;
  monthName: string;
}) {
  const [grain, setGrain] = usePersistentState<Grain>("lpdash.earnings.grain", "day");
  // One remembered range per grain; the day key is the one used before the switch existed.
  const [dayRange, setDayRange] = usePersistentState<number>("lpdash.earnings.range", 30);
  const [weekRange, setWeekRange] = usePersistentState<number>("lpdash.earnings.range.week", 12);
  const [monthRange, setMonthRange] = usePersistentState<number>("lpdash.earnings.range.month", 12);
  const range = grain === "day" ? dayRange : grain === "week" ? weekRange : monthRange;
  const setRange = grain === "day" ? setDayRange : grain === "week" ? setWeekRange : setMonthRange;

  const data = useMemo(() => chartSeries(days, grain, range), [days, grain, range]);
  const average = grainAverage[grain];

  return (
    <Panel
      title={TITLES[grain]}
      actions={
        <div className="flex flex-wrap items-center justify-end gap-2">
          <Segmented label="Group by" options={GRAINS} value={grain} onChange={setGrain} />
          <Segmented label="Range" options={RANGES[grain]} value={range} onChange={setRange} />
        </div>
      }
    >
      <div className="h-64">
        <ResponsiveContainer>
          <ComposedChart data={data}>
            <CartesianGrid stroke={theme.grid} vertical={false} />
            <XAxis
              dataKey="date"
              stroke={theme.axis}
              fontSize={11}
              tickFormatter={(d) => tickLabel(grain, String(d))}
              minTickGap={24}
            />
            <YAxis stroke={theme.axis} fontSize={11} tickFormatter={(v) => formatUsd(v)} width={64} />
            <Tooltip
              cursor={{ fill: theme.cursor }}
              contentStyle={{ background: theme.tooltip.background, border: theme.tooltip.border }}
              labelFormatter={(_, items) => {
                const p = items?.[0]?.payload as ChartPoint | undefined;
                return p ? tipLabel(grain, p) : "";
              }}
              formatter={(v, name) => [formatUsd(v as number), name]}
            />
            <Bar dataKey="fees" name="Swap fees" stackId="a" fill={theme.a}>
              {data.map((d) => (
                <Cell key={d.date} fillOpacity={d.partial ? 0.45 : 1} />
              ))}
            </Bar>
            <Bar dataKey="rewards" name="Rewards" stackId="a" fill={theme.d}>
              {data.map((d) => (
                <Cell key={d.date} fillOpacity={d.partial ? 0.45 : 1} />
              ))}
            </Bar>
            <Line
              dataKey="avg"
              name={average.label}
              stroke="var(--fg-2)"
              strokeDasharray="4 3"
              strokeWidth={1.5}
              dot={false}
              activeDot={false}
              connectNulls={false}
              isAnimationActive={false}
            />
            {data
              .filter((d) => d.low)
              .map((d) => (
                <ReferenceDot key={d.date} x={d.date} y={d.total} r={3.5} fill="var(--down)" stroke="none" />
              ))}
          </ComposedChart>
        </ResponsiveContainer>
      </div>
      <div className="mt-2 flex flex-wrap items-center justify-between gap-x-4 gap-y-1 text-xs text-muted">
        <div className="flex flex-wrap gap-x-4 gap-y-1">
          <Key color="var(--chart-a)">Swap fees</Key>
          <Key color="var(--chart-d)">Rewards</Key>
          <span>- - {average.label}</span>
          {grain === "day" && <Key color="var(--down)">Unusually low day</Key>}
          {grain === "day" && <span>Faded bar: today ({soFarText()})</span>}
          {grain !== "day" && <span>Faded bar: {grain} in progress</span>}
        </div>
        {projection != null && (
          <span>
            On pace for {formatUsd(projection)} in {monthName} (from completed days)
          </span>
        )}
      </div>
    </Panel>
  );
}
