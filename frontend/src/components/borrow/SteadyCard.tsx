import { useState } from "react";
import type { BorrowSummaryDto, LoanDto } from "../../api/types";
import { fixForHealthFactor, fixForLtv, perPeriod, periodInfo, PERIODS, type Fix, type Period } from "../../lib/borrow";
import { formatUsd } from "../../lib/format";
import { usePersistentState } from "../../lib/usePersistentState";
import { Panel, Segmented } from "../ui";

/** Suggestions from the numbers: what interest adds to debt, what holds the ratio steady, and a target calculator. */
export function SteadyCard({ summary, loans }: { summary: BorrowSummaryDto; loans: LoanDto[] }) {
  const [period, setPeriod] = usePersistentState<Period>("lpdash.borrow.period", "m");
  const info = periodInfo(period);
  const costs = summary.costs;
  const cost = perPeriod(costs.total, period);
  const income = perPeriod(costs.incomeAtPace, period);
  const left = income - cost;
  const debt = summary.trend.debtUsd;
  const ratio = summary.trend.ratioNow;

  return (
    <Panel
      title="Keep it steady"
      actions={
        <Segmented label="Period" options={PERIODS.map((p) => ({ label: p.label, value: p.value }))} value={period} onChange={setPeriod} />
      }
    >
      <div className="space-y-2.5 text-sm">
        <p>
          Interest adds about <b className="num">{formatUsd(cost)}</b> {info.text} to your debt
          {debt > 0 && ` (${((cost / debt) * 100).toFixed(period === "m" ? 2 : 3)}% of ${formatUsd(debt)})`}. Paying it keeps debt flat.{" "}
          {left >= 0 ? (
            <>
              LP income runs at <b className="num">{formatUsd(income)}</b> {info.text}, so it covers that with{" "}
              <b className="num text-emerald-400">{formatUsd(left)}</b> to spare.
            </>
          ) : (
            <>
              LP income runs at <b className="num">{formatUsd(income)}</b> {info.text}, which covers only {cost > 0 ? Math.round((income / cost) * 100) : 0}% of it.
            </>
          )}
        </p>
        {ratio != null && (
          <p>
            If prices stay flat, your collateral ÷ debt only slips because interest piles up. To hold it where it is, repay about{" "}
            <b className="num">{formatUsd(cost)}</b> {info.text}, or add about <b className="num">{formatUsd(cost * ratio)}</b> of collateral.
          </p>
        )}
      </div>

      <div className="mt-4 text-xs uppercase tracking-[.05em] text-slate-400">Target calculator</div>
      <div className="divide-y divide-slate-800">
        {loans
          .filter((l) => l.risk.kind !== "None" && l.risk.value != null && l.collateralUsd != null)
          .map((l) => (
            <Target key={l.key} loan={l} leftPerMonth={costs.leftOver.perMonthUsd} />
          ))}
      </div>
    </Panel>
  );
}

function Target({ loan, leftPerMonth }: { loan: LoanDto; leftPerMonth: number }) {
  const hf = loan.risk.kind === "HealthFactor";
  const [target, setTarget] = usePersistentState<number>(hf ? "lpdash.borrow.target.hf" : "lpdash.borrow.target.ltv", hf ? 2 : 60);
  const [text, setText] = useState(String(target));
  const value = loan.risk.value!;

  const fix: Fix | null = hf
    ? fixForHealthFactor(loan.collateralUsd!, loan.debtUsd, value, target)
    : fixForLtv(loan.collateralUsd!, loan.debtUsd, target);
  const met = fix != null && fix.repayUsd === 0 && fix.addCollateralUsd === 0;

  return (
    <div className="grid grid-cols-[1fr_6rem] items-end gap-x-3 gap-y-1.5 py-3">
      <div>
        <b>{loan.label}</b>
        <div className="text-xs text-slate-400">
          {hf ? `health factor now ${value.toFixed(2)}` : `LTV now ${value.toFixed(0)}%`}
        </div>
      </div>
      <div>
        <label className="mb-0.5 block text-xs text-slate-400" htmlFor={`target-${loan.key}`}>
          Target {hf ? "" : "LTV %"}
        </label>
        <input
          id={`target-${loan.key}`}
          type="number"
          step={hf ? "0.1" : "1"}
          min={hf ? 1.1 : 20}
          max={hf ? 5 : 79}
          value={text}
          className="w-full rounded-md border border-slate-700 bg-slate-800 px-2 py-1 text-sm"
          onChange={(e) => {
            setText(e.target.value);
            const n = Number(e.target.value);
            if (n > 0) setTarget(n);
          }}
        />
      </div>
      <div className="col-span-2 text-sm">
        {fix == null ? (
          "Not enough data to work this out."
        ) : met ? (
          hf ? "Already at or above this target." : "Already at or below this target."
        ) : (
          <>
            Repay <b className="num">{formatUsd(fix.repayUsd)}</b> or add <b className="num">{formatUsd(fix.addCollateralUsd)}</b> of collateral.{" "}
            {leftPerMonth > 0
              ? `That is about ${(fix.repayUsd / leftPerMonth).toFixed(1)} months of left-over LP income.`
              : "LP income is not leaving a surplus right now."}
          </>
        )}
      </div>
    </div>
  );
}
