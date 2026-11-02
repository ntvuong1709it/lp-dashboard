import type { ReactNode } from 'react';
import type { Position } from '../api/client';
import { cls, sg, usd } from '../format';
import { pillOf } from '../derive';

export function PageHead({ title, sub, right }: { title: string; sub: string; right?: ReactNode }) {
  return (
    <div className="page-head">
      <div><h1>{title}</h1><p>{sub}</p></div>
      {right}
    </div>
  );
}

export function Kpi({ label, value, tone, sub, subTone }: { label: string; value: string; tone?: string; sub: ReactNode; subTone?: string }) {
  return (
    <div className="card kpi">
      <div className="label">{label}</div>
      <div className={`value ${tone ?? ''}`}>{value}</div>
      <div className={`sub ${subTone ?? 'muted'}`}>{sub}</div>
    </div>
  );
}

export function Seg<T extends string>({ value, options, onChange }: { value: T; options: [T, string][]; onChange: (v: T) => void }) {
  return (
    <div className="seg">
      {options.map(([v, label]) => (
        <button key={v} aria-pressed={v === value} onClick={() => onChange(v)}>{label}</button>
      ))}
    </div>
  );
}

export function Pill({ p }: { p: Position }) {
  const { cls: c, text } = pillOf(p);
  return <span className={`pill ${c}`}>{text}</span>;
}

export function RangeBar({ p, big }: { p: Position; big?: boolean }) {
  const lo = p.priceLower - (p.priceUpper - p.priceLower) * 0.35;
  const hi = p.priceUpper + (p.priceUpper - p.priceLower) * 0.35;
  const f = (v: number) => Math.max(0, Math.min(100, ((v - lo) / (hi - lo)) * 100));
  return (
    <span className={`range ${big ? 'big' : ''}`}>
      <span className="fill" style={{ left: `${f(p.priceLower)}%`, width: `${f(p.priceUpper) - f(p.priceLower)}%` }} />
      {p.state !== 'closed' && <span className="mk" style={{ left: `${f(p.priceCurrent)}%` }} />}
    </span>
  );
}

export function Bars({ rows, total, color }: { rows: [string, number][]; total: number; color?: (v: number) => string }) {
  const max = rows[0]?.[1] || 1;
  return (
    <div className="rows">
      {rows.map(([name, v]) => (
        <div className="bar-row" key={name}>
          <span>{name}</span>
          <div className="track"><i style={{ width: `${(Math.abs(v) / Math.abs(max)) * 100}%`, background: color?.(v) }} /></div>
          <span className="num" style={{ textAlign: 'right' }}>
            {usd(v)} <span className="muted">{total ? ((v / total) * 100).toFixed(0) : 0}%</span>
          </span>
        </div>
      ))}
    </div>
  );
}

/** Horizontal rows scaled to one axis; the steps sum to the total. */
export function Waterfall({ steps, total }: { steps: { label: string; v: number }[]; total: number }) {
  const max = Math.max(...steps.map((s) => Math.abs(s.v)), Math.abs(total), 1);
  const row = (label: string, v: number, bold?: boolean) => {
    const w = (Math.abs(v) / max) * 50;
    return (
      <div className="wf-row" key={label}>
        <span className={bold ? '' : 'muted'}>{label}</span>
        <svg viewBox="0 0 100 18" preserveAspectRatio="none" aria-hidden="true">
          <line x1="50" x2="50" y1="0" y2="18" stroke="var(--line)" vectorEffect="non-scaling-stroke" />
          <rect x={v >= 0 ? 50 : 50 - w} y="3" width={Math.max(w, 0.6)} height="12" rx="1.5" fill={`var(${v >= 0 ? '--good' : '--bad'})`} opacity={bold ? 1 : 0.8} />
        </svg>
        <span className={`num ${cls(v)}`} style={{ textAlign: 'right', fontWeight: bold ? 600 : undefined }}>{sg(v)}</span>
      </div>
    );
  };
  return (
    <div className="wf">
      {steps.map((s) => row(s.label, s.v))}
      <div style={{ borderTop: '1px solid var(--line)', margin: '2px 0' }} />
      {row('Net PnL', total, true)}
    </div>
  );
}
