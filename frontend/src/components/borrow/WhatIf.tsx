import { useState } from "react";
import type { BorrowSummaryDto, LoanDto } from "../../api/types";
import { hfAfterMove, healthLevel, ltvAfterMove, ltvLevel, statusOf, whatIf } from "../../lib/borrow";
import { formatUsd, pnlClass } from "../../lib/format";
import { Panel } from "../ui";
import { LevelChip, StatusChip } from "./bits";

const DEFAULTS = { rate: 100, income: 100, move: 0 };

/** Sliders over today's numbers. Nothing is saved; a price move changes health factor and LTV, not LP income. */
export function WhatIf({ summary, loans }: { summary: BorrowSummaryDto; loans: LoanDto[] }) {
  const [v, setV] = useState(DEFAULTS);
  const r = whatIf(
    { interestPerDay: summary.runRate.interestPerDayUsd, incomePerDay: summary.runRate.incomePerDayUsd },
    v.rate / 100,
    v.income / 100,
  );
  const move = v.move / 100;

  return (
    <Panel
      title="What if"
      actions={
        <button className="text-xs text-sky-400 hover:underline" onClick={() => setV(DEFAULTS)}>
          Reset
        </button>
      }
    >
      <div className="grid gap-5 lg:grid-cols-2">
        <div className="space-y-4">
          <Slider id="wi-rate" label="Borrow rates" value={`${v.rate}% of today`} min={50} max={200} step={5} current={v.rate} onChange={(n) => setV({ ...v, rate: n })} />
          <Slider id="wi-income" label="LP income" value={`${v.income}% of the 7d average`} min={20} max={200} step={5} current={v.income} onChange={(n) => setV({ ...v, income: n })} />
          <Slider id="wi-move" label="Collateral price" value={`${v.move > 0 ? "+" : ""}${v.move}%`} min={-60} max={30} step={1} current={v.move} onChange={(n) => setV({ ...v, move: n })} />
        </div>
        <div className="grid grid-cols-2 gap-3 self-start">
          <Out label="Coverage">
            <span className="num">{r.ratio == null ? "—" : `${r.ratio.toFixed(2)}×`}</span> <StatusChip status={statusOf(r.ratio)} />
          </Out>
          <Out label="Net carry per day" hint="before gas">
            <span className={`num ${pnlClass(r.netPerDay)}`}>{formatUsd(r.netPerDay, true)}</span>
          </Out>
          {loans
            .filter((l) => l.risk.kind !== "None" && l.risk.value != null)
            .map((l) => {
              const hf = l.risk.kind === "HealthFactor";
              const value = hf ? hfAfterMove(l.risk.value!, move) : ltvAfterMove(l.risk.value!, move);
              const liquidation = l.risk.liquidationLtvPercent ?? 80;
              const gone = hf ? value <= 1 : value >= liquidation;
              const level = hf ? healthLevel(value) : ltvLevel(value, liquidation);
              return (
                <Out key={l.key} label={hf ? "Aave health factor" : `${l.label} LTV`}>
                  <span className="num">{hf ? value.toFixed(2) : `${value.toFixed(0)}%`}</span>{" "}
                  <LevelChip level={gone ? "Critical" : level}>{gone ? "Liquidated" : undefined}</LevelChip>
                </Out>
              );
            })}
        </div>
      </div>
      <p className="mt-3 text-xs text-slate-500">Scenario only. A price move changes health factor and LTV, not LP income.</p>
    </Panel>
  );
}

function Slider({
  id,
  label,
  value,
  min,
  max,
  step,
  current,
  onChange,
}: {
  id: string;
  label: string;
  value: string;
  min: number;
  max: number;
  step: number;
  current: number;
  onChange: (n: number) => void;
}) {
  return (
    <div>
      <label htmlFor={id} className="mb-1 flex justify-between text-sm">
        <span>{label}</span>
        <b className="num">{value}</b>
      </label>
      <input id={id} type="range" className="w-full accent-sky-400" min={min} max={max} step={step} value={current} onChange={(e) => onChange(Number(e.target.value))} />
    </div>
  );
}

function Out({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  return (
    <div className="rounded-md border border-slate-800 bg-slate-800/40 px-3 py-2.5">
      <div className="text-xs uppercase tracking-[.05em] text-slate-400">{label}</div>
      <div className="mt-0.5 text-lg font-medium">{children}</div>
      {hint && <div className="text-xs text-slate-500">{hint}</div>}
    </div>
  );
}
