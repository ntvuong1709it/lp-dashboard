import type { ReactNode } from "react";
import type { CoverageStatus, RiskLevel } from "../../api/types";
import { LEVEL_CLASS, LEVEL_LABEL, STATUS_CLASS, STATUS_LABEL } from "../../lib/borrow";

export function Chip({ className, children }: { className: string; children: ReactNode }) {
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium ${className}`}>
      <span className="inline-block h-1.5 w-1.5 rounded-full bg-current" />
      {children}
    </span>
  );
}

export const StatusChip = ({ status }: { status: CoverageStatus }) => (
  <Chip className={STATUS_CLASS[status]}>{STATUS_LABEL[status]}</Chip>
);

export const LevelChip = ({ level, children }: { level: RiskLevel; children?: ReactNode }) => (
  <Chip className={LEVEL_CLASS[level]}>{children ?? LEVEL_LABEL[level]}</Chip>
);

/** Arrow plus amount, green when the change is in the safe direction. */
export function Delta({ value, text, goodUp }: { value: number; text: string; goodUp: boolean }) {
  const up = value >= 0;
  return (
    <span className={up === goodUp ? "text-up" : "text-down"}>
      {up ? "▲" : "▼"} {text}
    </span>
  );
}

export interface Zone {
  from: number;
  to: number;
  className: string;
}

/** A horizontal bar of coloured zones with a marker, e.g. health factor 1.0 to 2.5. */
export function Gauge({
  min,
  max,
  value,
  zones,
  ticks = [],
  label,
}: {
  min: number;
  max: number;
  value: number;
  zones: Zone[];
  ticks?: { value: number; text: string }[];
  label: string;
}) {
  const at = (v: number) => Math.max(0, Math.min(100, ((v - min) / (max - min)) * 100));
  return (
    <div className="relative my-5 h-2.5" role="img" aria-label={label}>
      <div className="absolute inset-0 overflow-hidden rounded-full bg-raised">
        {zones.map((z) => (
          <span
            key={`${z.from}-${z.to}`}
            className={`absolute inset-y-0 ${z.className}`}
            style={{ left: `${at(z.from)}%`, width: `${at(z.to) - at(z.from)}%` }}
          />
        ))}
      </div>
      <i
        className="absolute -top-1 h-4.5 w-0.75 -translate-x-1/2 rounded-sm bg-fg"
        style={{ left: `${at(value)}%` }}
      />
      {ticks.map((t) => (
        <span
          key={t.text}
          className="absolute top-4 -translate-x-1/2 whitespace-nowrap text-[10px] text-muted"
          style={{ left: `${at(t.value)}%` }}
        >
          {t.text}
        </span>
      ))}
    </div>
  );
}

export const HealthGauge = ({ value }: { value: number }) => (
  <Gauge
    min={1}
    max={2.5}
    value={value}
    label={`Health factor ${value.toFixed(2)}`}
    zones={[
      { from: 1, to: 1.2, className: "bg-down-fill/70" },
      { from: 1.2, to: 1.5, className: "bg-warn-fill/70" },
      { from: 1.5, to: 2.5, className: "bg-up-fill/70" },
    ]}
    ticks={[
      { value: 1.2, text: "1.2 critical" },
      { value: 1.5, text: "1.5 warn" },
    ]}
  />
);

export const LtvGauge = ({
  value,
  liquidation,
  marginCall,
}: {
  value: number;
  liquidation: number;
  marginCall: number | null;
}) => {
  const warn = liquidation * 0.85;
  const critical = liquidation * 0.95;
  const min = Math.max(0, Math.floor((Math.min(value, warn) - 25) / 5) * 5);
  const max = Math.min(100, Math.ceil((liquidation + 10) / 5) * 5);
  return (
    <Gauge
      min={min}
      max={max}
      value={Math.min(value, max)}
      label={`LTV ${value.toFixed(0)} percent`}
      zones={[
        { from: min, to: warn, className: "bg-up-fill/70" },
        { from: warn, to: critical, className: "bg-warn-fill/70" },
        { from: critical, to: max, className: "bg-down-fill/70" },
      ]}
      ticks={[
        { value: warn, text: `${warn.toFixed(0)} warn` },
        ...(marginCall != null ? [{ value: marginCall, text: `${marginCall} margin call` }] : []),
        { value: liquidation, text: `${liquidation} liquidation` },
      ]}
    />
  );
};
