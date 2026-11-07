import { useState } from "react";
import type { BorrowSummaryDto } from "../../api/types";
import type { Answer } from "../../lib/borrowAnswer";
import { formatPercent, formatUsd, pnlClass } from "../../lib/format";
import { Panel } from "../ui";

/** How far today's numbers can move before income stops covering interest. Uses today's pace. */
export function Room({ now, summary }: { now: Answer; summary: BorrowSummaryDto }) {
  const [rate, setRate] = useState<number | null>(null);
  const debt = now.debtUsd;
  const current = now.payPer100;
  const breakEven = debt > 0 ? ((now.incomePerDay * 365) / debt) * 100 : null;
  const run = summary.runRate;
  const lpShare = now.payPerDay > 0 ? now.lpPerDay / now.payPerDay : null;

  const r = rate ?? current;
  const interest = (debt * r) / 100 / 365;
  const left = now.incomePerDay - interest;
  const min = Math.max(0.5, Math.floor(current * 0.4));
  const max = Math.max(min + 1, Math.ceil(Math.max(current, breakEven ?? 0) * 1.4));

  return (
    <Panel
      title="How much room do you have?"
      actions={
        rate != null && (
          <button className="text-xs text-sky-400 hover:underline" onClick={() => setRate(null)}>
            Reset
          </button>
        )
      }
    >
      <div className="space-y-4">
        {breakEven != null && (
          <Meter
            label="Borrow rate can rise to"
            value={formatPercent(breakEven)}
            scale={Math.max(current, breakEven) * 1.15}
            fill={current}
            mark={breakEven}
            color="bg-rose-400"
            note={
              current > breakEven
                ? `Today ${formatPercent(current)} is already above that, so the loans cost more than you earn.`
                : `Today ${formatPercent(current)}. Above ${formatPercent(breakEven)} the loans cost more than you earn.`
            }
          />
        )}
        {run.requiredLpAprPercent != null && (
          <Meter
            label="LP alone would need"
            value={`${formatPercent(run.requiredLpAprPercent)} APR`}
            scale={Math.max(run.requiredLpAprPercent, run.currentLpAprPercent ?? 0) * 1.15}
            fill={run.currentLpAprPercent ?? 0}
            mark={run.requiredLpAprPercent}
            color="bg-sky-400"
            note={
              run.currentLpAprPercent == null
                ? "You have no open positions."
                : `It earns ${formatPercent(run.currentLpAprPercent)} now, so LP alone pays ${Math.round((lpShare ?? 0) * 100)}% of the interest.${
                    lpShare != null && lpShare < 1 && now.aavePerDay > 0 ? " Aave supply yield covers the gap." : ""
                  }`
            }
          />
        )}
      </div>

      {debt > 0 && (
        <div className="mt-4 space-y-2 rounded-md border border-dashed border-slate-700 px-3.5 py-3">
          <label htmlFor="room-rate" className="block text-xs text-slate-400">
            What if your average borrow rate becomes
          </label>
          <div className="num text-2xl font-medium">{formatPercent(r)}</div>
          <input
            id="room-rate"
            type="range"
            className="w-full accent-sky-400"
            min={min}
            max={max}
            step={0.05}
            value={Math.min(max, Math.max(min, r))}
            onChange={(e) => setRate(Number(e.target.value))}
          />
          <div className="flex flex-wrap gap-x-5 gap-y-1 text-sm">
            <span>
              Left over <b className={`num ${pnlClass(left)}`}>{formatUsd(left, true)}</b> a day
            </span>
            <span>
              Coverage <b className="num">{interest > 0 ? `${(now.incomePerDay / interest).toFixed(2)}×` : "—"}</b>
            </span>
          </div>
        </div>
      )}
    </Panel>
  );
}

function Meter({
  label,
  value,
  scale,
  fill,
  mark,
  color,
  note,
}: {
  label: string;
  value: string;
  scale: number;
  fill: number;
  mark: number;
  color: string;
  note: string;
}) {
  const at = (v: number) => `${Math.min(100, (v / scale) * 100)}%`;
  return (
    <div>
      <div className="flex justify-between gap-3 text-sm">
        <span>{label}</span>
        <b className="num font-medium">{value}</b>
      </div>
      <div className="relative mt-2 h-2 rounded-full bg-slate-800">
        <i className={`absolute inset-y-0 left-0 rounded-full ${color}`} style={{ width: at(fill) }} />
        <i className="absolute -bottom-1 -top-1 w-0.5 -translate-x-px bg-slate-50" style={{ left: at(mark) }} />
      </div>
      <div className="mt-1.5 text-xs text-slate-500">{note}</div>
    </div>
  );
}
