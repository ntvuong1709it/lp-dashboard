import type { FearGreedBandDto, FearGreedZone } from "../../api/types";
import { ZONE_LABEL, ZONE_VAR } from "../../lib/fearGreed";

const CX = 130;
const CY = 120;
const R = 100;
/** Space between two zones, in index points. */
const GAP = 0.9;

/** A point on the half circle: 0 is the left end, 100 the right end. */
function point(value: number): [number, number] {
  const a = ((180 - value * 1.8) * Math.PI) / 180;
  return [CX + R * Math.cos(a), CY - R * Math.sin(a)];
}

function arc(from: number, to: number): string {
  const [x0, y0] = point(from);
  const [x1, y1] = point(to);
  return `M ${x0.toFixed(2)} ${y0.toFixed(2)} A ${R} ${R} 0 0 1 ${x1.toFixed(2)} ${y1.toFixed(2)}`;
}

/** The index as a half circle in five zones. The zone holding the value is lit; the white tick is the same one the range bars use. */
export function FearGreedGauge({ value, zone, bands }: { value: number; zone: FearGreedZone; bands: FearGreedBandDto[] }) {
  const [mx, my] = point(value);
  return (
    <svg
      viewBox="0 0 260 142"
      className="block w-full max-w-72"
      role="img"
      aria-label={`Fear and greed index ${value} out of 100: ${ZONE_LABEL[zone].toLowerCase()}`}
    >
      {bands.map((b, i) => {
        const last = i === bands.length - 1;
        // Each zone runs up to where the next begins; the last one to 100.
        const from = b.from + (i === 0 ? 0 : GAP);
        const to = (last ? 100 : bands[i + 1].from) - (last ? 0 : GAP);
        return (
          <path
            key={b.zone}
            d={arc(from, to)}
            fill="none"
            stroke={ZONE_VAR[b.zone]}
            strokeWidth={13}
            opacity={b.zone === zone ? 1 : 0.28}
          />
        );
      })}
      <circle cx={mx} cy={my} r={7.5} fill="var(--marker)" stroke="var(--panel)" strokeWidth={3} />
      <text x={CX} y={108} textAnchor="middle" className="num" style={{ fontSize: 54, fontWeight: 500, fill: "var(--fg)" }}>
        {value}
      </text>
      <text x={CX - R} y={138} textAnchor="middle" className="num" style={{ fontSize: 10.5, fill: "var(--faint)" }}>
        0
      </text>
      <text x={CX + R} y={138} textAnchor="middle" className="num" style={{ fontSize: 10.5, fill: "var(--faint)" }}>
        100
      </text>
    </svg>
  );
}
