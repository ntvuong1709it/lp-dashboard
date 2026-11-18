import { Area, AreaChart, ReferenceArea, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import type { FearGreedBandDto, FearGreedDayDto } from "../../api/types";
import { ZONE_CHIP, ZONE_LABEL, ZONE_VAR, fearGreedDay, fearGreedRange } from "../../lib/fearGreed";
import { chart } from "../../lib/chartTheme";
import { Chip } from "../borrow/bits";

const Y_TICKS = [0, 25, 50, 75, 100];

function Readout({ active, payload }: { active?: boolean; payload?: ReadonlyArray<{ payload?: FearGreedDayDto }> }) {
  const d = active ? payload?.[0]?.payload : undefined;
  if (!d) return null;
  return (
    <div className="flex items-center gap-2 rounded-md px-3 py-2 text-sm" style={{ background: chart.tooltip.background, border: chart.tooltip.border }}>
      <span className="text-muted">{fearGreedDay(d.day)}</span>
      <span className="num font-medium">{d.value}</span>
      <Chip className={ZONE_CHIP[d.zone]}>{ZONE_LABEL[d.zone]}</Chip>
    </div>
  );
}

/** The index over the last 30 days on a fixed 0 to 100 scale, over faint bands for the five zones. The last point is marked in its zone's colour. */
export function FearGreedChart({ days, bands }: { days: FearGreedDayDto[]; bands: FearGreedBandDto[] }) {
  const { low, high } = fearGreedRange(days);
  const ticks = [days[0].day, days[Math.floor((days.length - 1) / 2)].day, days[days.length - 1].day];

  return (
    <div>
      <div className="h-52">
        <ResponsiveContainer>
          <AreaChart data={days} margin={{ top: 6, right: 10, bottom: 0, left: 0 }}>
            {bands.map((b, i) => (
              <ReferenceArea
                key={b.zone}
                y1={b.from}
                y2={i === bands.length - 1 ? 100 : bands[i + 1].from}
                fill={ZONE_VAR[b.zone]}
                fillOpacity={0.1}
                stroke="none"
              />
            ))}
            <XAxis
              dataKey="day"
              ticks={ticks}
              interval={0}
              tickFormatter={fearGreedDay}
              stroke={chart.axis}
              fontSize={10.5}
              tickLine={false}
              padding={{ left: 8, right: 8 }}
            />
            <YAxis domain={[0, 100]} ticks={Y_TICKS} stroke={chart.axis} fontSize={10.5} width={30} tickLine={false} axisLine={false} />
            <Tooltip cursor={{ stroke: chart.cursor }} content={<Readout />} />
            <Area
              dataKey="value"
              type="linear"
              stroke={chart.a}
              strokeWidth={2}
              fill={chart.a}
              fillOpacity={0.12}
              isAnimationActive={false}
              dot={(p: { cx?: number; cy?: number; index?: number; payload?: FearGreedDayDto }) =>
                p.index === days.length - 1 && p.payload ? (
                  <circle key="last" cx={p.cx} cy={p.cy} r={5} fill={ZONE_VAR[p.payload.zone]} stroke="var(--panel)" strokeWidth={2.5} />
                ) : (
                  <g key={`dot-${p.index}`} />
                )
              }
              activeDot={{ r: 5, stroke: "var(--panel)", strokeWidth: 2.5 }}
            />
          </AreaChart>
        </ResponsiveContainer>
      </div>
      <p className="mt-2 flex flex-wrap gap-x-6 gap-y-1 text-sm text-muted">
        <span>
          Low <span className="num font-medium text-fg-2">{low.value}</span> on {fearGreedDay(low.day)}
        </span>
        <span>
          High <span className="num font-medium text-fg-2">{high.value}</span> on {fearGreedDay(high.day)}
        </span>
      </p>
    </div>
  );
}
