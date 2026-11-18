import { Bar, BarChart, Cell, ReferenceArea, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import type { PremiumBandDto, PremiumDayDto } from "../../api/types";
import { chart } from "../../lib/chartTheme";
import { fearGreedDay } from "../../lib/fearGreed";
import { PREMIUM_CHIP, PREMIUM_LABEL, formatPremium, premiumAxisMax, premiumRange } from "../../lib/premium";
import { Chip } from "../borrow/bits";

function Readout({ active, payload }: { active?: boolean; payload?: ReadonlyArray<{ payload?: PremiumDayDto }> }) {
  const d = active ? payload?.[0]?.payload : undefined;
  if (!d) return null;
  return (
    <div className="flex items-center gap-2 rounded-md px-3 py-2 text-sm" style={{ background: chart.tooltip.background, border: chart.tooltip.border }}>
      <span className="text-muted">{fearGreedDay(d.day)}</span>
      <span className="num font-medium">{formatPremium(d.percent)}</span>
      <Chip className={PREMIUM_CHIP[d.zone]}>{PREMIUM_LABEL[d.zone]}</Chip>
    </div>
  );
}

/**
 * One bar per UTC day, green above zero (US buyers paying more) and red below. A faint band marks the flat zone; the last bar
 * is today so far, and is drawn at full strength.
 */
export function PremiumChart({ days, bands }: { days: PremiumDayDto[]; bands: PremiumBandDto[] }) {
  const { low, high } = premiumRange(days);
  const max = premiumAxisMax(days);
  const flat = bands.find((b) => b.zone === "Flat");
  const ticks = [days[0].day, days[Math.floor((days.length - 1) / 2)].day, days[days.length - 1].day];

  return (
    <div>
      <div className="h-52">
        <ResponsiveContainer>
          <BarChart data={days} margin={{ top: 6, right: 10, bottom: 0, left: 0 }}>
            {flat && (
              <ReferenceArea
                y1={flat.from}
                y2={flat.to}
                fill="var(--raised)"
                fillOpacity={0.55}
                stroke="none"
                label={{ value: "flat", position: "insideTopLeft", fill: chart.axis, fontSize: 9 }}
              />
            )}
            <ReferenceLine y={0} stroke="var(--line-strong)" />
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
            <YAxis
              domain={[-max, max]}
              ticks={[-max, 0, max]}
              tickFormatter={formatPremium}
              stroke={chart.axis}
              fontSize={10.5}
              width={48}
              tickLine={false}
              axisLine={false}
            />
            <Tooltip cursor={{ fill: chart.cursor, fillOpacity: 0.3 }} content={<Readout />} />
            <Bar dataKey="percent" isAnimationActive={false} radius={1.5}>
              {days.map((d, i) => (
                <Cell key={d.day} fill={d.percent >= 0 ? "var(--up-fill)" : "var(--down-fill)"} fillOpacity={i === days.length - 1 ? 1 : 0.62} />
              ))}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>
      <p className="mt-2 flex flex-wrap gap-x-6 gap-y-1 text-sm text-muted">
        <span>
          Lowest <span className="num font-medium text-fg-2">{formatPremium(low.percent)}</span> on {fearGreedDay(low.day)}
        </span>
        <span>
          Highest <span className="num font-medium text-fg-2">{formatPremium(high.percent)}</span> on {fearGreedDay(high.day)}
        </span>
      </p>
    </div>
  );
}
