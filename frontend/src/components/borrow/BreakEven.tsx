import type { BorrowSummaryDto } from "../../api/types";
import { formatPercent, formatUsd } from "../../lib/format";
import { Panel } from "../ui";

/** The income that covers today's interest, set against what LP income is doing now. */
export function BreakEven({ summary }: { summary: BorrowSummaryDto }) {
  const r = summary.runRate;
  const ok = r.incomePerDayUsd >= r.interestPerDayUsd;

  return (
    <Panel title="Break-even">
      <p className="mb-3 text-sm">
        You need <b className="num">{formatUsd(r.interestPerDayUsd)}</b> a day to cover interest. Over the last 7 days LP income averaged{" "}
        <b className={`num ${ok ? "text-emerald-400" : "text-rose-400"}`}>{formatUsd(r.incomePerDayUsd)}</b> a day.
      </p>
      <div className="grid grid-cols-2 gap-3">
        <div className="rounded-md border border-slate-800 bg-slate-800/40 px-3 py-2.5">
          <div className="text-xs uppercase tracking-[.05em] text-slate-400">Interest per day</div>
          <div className="mt-0.5 text-xl font-medium num">{formatUsd(r.interestPerDayUsd)}</div>
          <div className="text-xs text-slate-500">at today's debt and rates</div>
        </div>
        <div className="rounded-md border border-slate-800 bg-slate-800/40 px-3 py-2.5">
          <div className="text-xs uppercase tracking-[.05em] text-slate-400">LP APR needed</div>
          <div className="mt-0.5 text-xl font-medium num">
            {r.requiredLpAprPercent == null ? "—" : formatPercent(r.requiredLpAprPercent)}
          </div>
          <div className="text-xs text-slate-500">
            {r.currentLpAprPercent == null
              ? "no open positions"
              : `now ${formatPercent(r.currentLpAprPercent)} on ${formatUsd(r.openPositionValueUsd)}`}
          </div>
        </div>
      </div>
    </Panel>
  );
}
