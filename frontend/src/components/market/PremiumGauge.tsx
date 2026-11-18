import type { PremiumBandDto, PremiumZone } from "../../api/types";
import { PREMIUM_LABEL, PREMIUM_VAR, formatPremium } from "../../lib/premium";

const CX = 130;
const CY = 120;
const R = 100;
/** The gauge shows this far either side of zero, in percent. Beyond it the tick sits at the end. */
const SPAN = 0.3;
/** Space between two zones, in percent. */
const GAP = 0.005;

/** A point on the half circle: -SPAN is the left end, +SPAN the right end. */
function point(percent: number): [number, number] {
  const clamped = Math.max(-SPAN, Math.min(SPAN, percent));
  const a = ((180 - ((clamped + SPAN) / (2 * SPAN)) * 180) * Math.PI) / 180;
  return [CX + R * Math.cos(a), CY - R * Math.sin(a)];
}

function arc(from: number, to: number): string {
  const [x0, y0] = point(from);
  const [x1, y1] = point(to);
  return `M ${x0.toFixed(2)} ${y0.toFixed(2)} A ${R} ${R} 0 0 1 ${x1.toFixed(2)} ${y1.toFixed(2)}`;
}

/** US buying against selling as a half circle in five zones, selling on the left. The zone holding the value is lit; the white tick is the one the other gauge uses. */
export function PremiumGauge({ percent, zone, bands }: { percent: number; zone: PremiumZone; bands: PremiumBandDto[] }) {
  const [mx, my] = point(percent);
  return (
    <svg
      viewBox="0 0 260 142"
      className="block w-full max-w-72"
      role="img"
      aria-label={`US buying versus selling ${formatPremium(percent)}: ${PREMIUM_LABEL[zone].toLowerCase()}`}
    >
      {bands.map((b, i) => (
        <path
          key={b.zone}
          d={arc(b.from + (i === 0 ? 0 : GAP), b.to - (i === bands.length - 1 ? 0 : GAP))}
          fill="none"
          stroke={PREMIUM_VAR[b.zone]}
          strokeWidth={13}
          opacity={b.zone === zone ? 1 : 0.28}
        />
      ))}
      <circle cx={mx} cy={my} r={7.5} fill="var(--marker)" stroke="var(--panel)" strokeWidth={3} />
      <text x={CX} y={106} textAnchor="middle" className="num" style={{ fontSize: 38, fontWeight: 500, fill: "var(--fg)" }}>
        {formatPremium(percent)}
      </text>
      <text x={CX - R} y={138} textAnchor="middle" style={{ fontSize: 10.5, fill: "var(--faint)" }}>
        Selling
      </text>
      <text x={CX + R} y={138} textAnchor="middle" style={{ fontSize: 10.5, fill: "var(--faint)" }}>
        Buying
      </text>
    </svg>
  );
}
