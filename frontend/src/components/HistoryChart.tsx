import { useId } from "react";
import {
  Area,
  ComposedChart,
  CartesianGrid,
  Line,
  ReferenceArea,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { chart } from "../lib/chartTheme";
import { compactUsd, paddedScale, shortDay } from "../lib/chartAxis";
import { weekdayDateTime } from "../lib/dates";
import { formatPercent, formatUsd, pnlClass } from "../lib/format";

const YEAR = 365 * 86_400_000;

export interface HistoryPoint {
  /** Milliseconds since epoch. */
  t: number;
  v: number;
  /** A second, dashed line (for example value plus unclaimed fees). */
  v2?: number;
  positions?: number;
  estimated?: boolean;
}

/**
 * A value over time as a filled line: the headline number and its change over the range on top, then the chart with a
 * scale that fits the data. Used for the portfolio value and the cumulative P/L on the Overview page.
 */
export function HistoryChart({
  points,
  name,
  name2,
  signed = false,
  rangeLabel,
}: {
  points: HistoryPoint[];
  name: string;
  /** Label of the dashed line; when omitted `v2` is not drawn. */
  name2?: string;
  /** A profit and loss series: shows signs, keeps zero in view and draws the zero line. */
  signed?: boolean;
  /** "last 90 days", "all time": what the change under the headline is measured over. */
  rangeLabel: string;
}) {
  const gradient = useId();
  const first = points[0];
  const last = points[points.length - 1];
  const change = last.v - first.v;
  const changePct = !signed && first.v > 0 ? (change / first.v) * 100 : null;
  const showSecond = name2 != null && points.some((p) => p.v2 != null);

  const scale = paddedScale(
    points.flatMap((p) => (showSecond && p.v2 != null ? [p.v, p.v2] : [p.v])),
    signed,
  );
  const withYear = last.t - first.t > YEAR;
  const estimatedUntil = points.reduce((end, p) => (p.estimated ? p.t : end), 0);

  return (
    <div>
      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <span className={`text-2xl font-medium tracking-tight num ${signed ? pnlClass(last.v) : ""}`}>
          {formatUsd(last.v, signed)}
        </span>
        <span className={`text-sm num ${pnlClass(change)}`}>
          {change > 0 ? "▲" : change < 0 ? "▼" : ""} {formatUsd(Math.abs(change))}
          {changePct != null && ` (${formatPercent(Math.abs(changePct))})`}
        </span>
        <span className="text-xs text-faint">{rangeLabel}</span>
      </div>

      <div className="mt-3 h-56">
        <ResponsiveContainer>
          <ComposedChart data={points} margin={{ top: 6, right: 6, bottom: 0, left: 0 }}>
            <defs>
              <linearGradient id={gradient} x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor={chart.a} stopOpacity={0.32} />
                <stop offset="100%" stopColor={chart.a} stopOpacity={0.02} />
              </linearGradient>
            </defs>
            <CartesianGrid stroke={chart.grid} vertical={false} />
            <XAxis
              dataKey="t"
              type="number"
              scale="time"
              domain={["dataMin", "dataMax"]}
              tickFormatter={(t) => shortDay(t as number, withYear)}
              stroke={chart.axis}
              fontSize={11}
              tickLine={false}
              axisLine={false}
              minTickGap={48}
              tickMargin={8}
            />
            <YAxis
              domain={[scale.lo, scale.hi]}
              ticks={scale.ticks}
              tickFormatter={(v) => compactUsd(v as number)}
              stroke={chart.axis}
              fontSize={11}
              width={46}
              tickLine={false}
              axisLine={false}
            />
            {signed && <ReferenceLine y={0} stroke={chart.axis} strokeDasharray="3 3" />}
            {estimatedUntil > first.t && (
              <ReferenceArea
                x1={first.t}
                x2={estimatedUntil}
                fill={chart.axis}
                fillOpacity={0.1}
                ifOverflow="hidden"
                label={{ value: "Estimated", position: "insideTopLeft", fill: "var(--chart-axis)", fontSize: 11 }}
              />
            )}
            <Tooltip
              cursor={{ stroke: chart.axis, strokeDasharray: "3 3" }}
              content={<HistoryTip name={name} name2={showSecond ? name2 : undefined} signed={signed} />}
            />
            <Area
              type="monotone"
              dataKey="v"
              name={name}
              stroke={chart.a}
              strokeWidth={2}
              fill={`url(#${gradient})`}
              dot={false}
              activeDot={{ r: 4, strokeWidth: 2, stroke: "var(--panel)" }}
              isAnimationActive={false}
            />
            {showSecond && (
              <Line
                type="monotone"
                dataKey="v2"
                name={name2}
                stroke={chart.b}
                strokeWidth={1.5}
                strokeDasharray="4 3"
                dot={false}
                activeDot={{ r: 3, strokeWidth: 2, stroke: "var(--panel)" }}
                isAnimationActive={false}
              />
            )}
          </ComposedChart>
        </ResponsiveContainer>
      </div>

      <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted">
        <span>
          <span className="mr-1.5 inline-block h-0.5 w-3.5 align-middle" style={{ background: chart.a }} />
          {name}
        </span>
        {showSecond && (
          <span>
            <span
              className="mr-1.5 inline-block h-0 w-3.5 border-t-2 border-dashed align-middle"
              style={{ borderColor: chart.b }}
            />
            {name2}
          </span>
        )}
        {estimatedUntil > first.t && <span className="text-faint">Shaded: estimated from deposits and historical prices</span>}
      </div>
    </div>
  );
}

interface TipProps {
  active?: boolean;
  payload?: { payload: HistoryPoint }[];
  name: string;
  name2?: string;
  signed: boolean;
}

function HistoryTip({ active, payload, name, name2, signed }: TipProps) {
  const p = active ? payload?.[0]?.payload : undefined;
  if (!p) return null;
  return (
    <div
      className="rounded-md px-3 py-2 text-xs shadow-lg"
      style={{ background: chart.tooltip.background, border: chart.tooltip.border }}
    >
      <div className="font-medium text-fg-2">
        {weekdayDateTime(p.t)}
        {p.positions ? ` · ${p.positions} ${p.positions === 1 ? "position" : "positions"}` : ""}
        {p.estimated ? " · estimated" : ""}
      </div>
      <div className="mt-1 flex items-center justify-between gap-6">
        <span className="text-muted">{name}</span>
        <span className="num font-semibold text-fg">{formatUsd(p.v, signed)}</span>
      </div>
      {name2 && p.v2 != null && (
        <div className="flex items-center justify-between gap-6">
          <span className="text-muted">{name2}</span>
          <span className="num text-fg-2">{formatUsd(p.v2)}</span>
        </div>
      )}
    </div>
  );
}
