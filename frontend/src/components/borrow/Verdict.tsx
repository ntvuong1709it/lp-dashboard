import type { BorrowSummaryDto } from "../../api/types";
import { statusOf } from "../../lib/borrow";
import type { Answer } from "../../lib/borrowAnswer";
import { formatPercent, formatUsd } from "../../lib/format";
import { StatusChip } from "./bits";

/** The one-line answer: does income beat interest, and what pays for it. */
export function Verdict({
  shown,
  now,
  past,
  summary,
}: {
  shown: Answer;
  now: Answer;
  past: Answer;
  summary: BorrowSummaryDto;
}) {
  const a = shown;
  const c = summary.coverage;

  if (a.ratio == null) {
    return (
      <section className="rounded-lg border border-slate-800 bg-slate-900 p-4">
        <div className="text-lg font-medium">No interest in this window</div>
        <p className="mt-1 text-sm text-slate-400">There is nothing to cover yet.</p>
      </section>
    );
  }

  const ahead = a.leftPerDay >= 0;
  const max = Math.max(1.5, a.ratio * 1.1);
  const at = (x: number) => `${Math.min(100, (x / max) * 100)}%`;
  const lpShare = a.lpPerDay / a.payPerDay;
  const aaveShare = a.aavePerDay / a.payPerDay;

  return (
    <section className="space-y-3.5 rounded-lg border border-slate-800 bg-slate-900 p-4" aria-label="Coverage verdict">
      <div className="flex flex-wrap items-center gap-3">
        <StatusChip status={statusOf(a.ratio)} />
        <h2 className="text-xl font-semibold tracking-tight">
          Income {ahead ? "beats" : "trails"} interest by{" "}
          <span className={`num ${ahead ? "text-emerald-400" : "text-rose-400"}`}>{formatUsd(Math.abs(a.leftPerDay))}</span> a day
        </h2>
      </div>
      <p className="text-sm text-slate-400">
        LP alone pays <b className="text-slate-200">{Math.round(lpShare * 100)}%</b> of the interest.
        {a.aavePerDay > 0 && (
          <>
            {" "}
            Aave supply yield on your collateral adds <b className="text-slate-200">{Math.round(aaveShare * 100)}%</b>, for{" "}
            <b className="num text-slate-200">{a.ratio.toFixed(2)}×</b> in total.
          </>
        )}
      </p>

      <div className="relative pt-6" role="img" aria-label={`Income is ${a.ratio.toFixed(2)} times the interest`}>
        <span className="absolute top-0 -translate-x-1/2 whitespace-nowrap text-xs" style={{ left: at(1) }}>
          Interest {formatUsd(a.payPerDay)} a day
        </span>
        <div className="flex h-5 overflow-hidden rounded-md bg-slate-800">
          <i style={{ width: at(lpShare), background: "var(--chart-a)" }} />
          <i style={{ width: at(aaveShare), background: "var(--chart-b)" }} />
        </div>
        <i className="absolute -bottom-1 top-5 w-0.5 -translate-x-px bg-slate-50" style={{ left: at(1) }} />
      </div>

      <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-slate-400">
        <span>
          <span className="mr-1.5 inline-block h-2 w-2 rounded-sm" style={{ background: "var(--chart-a)" }} />
          LP fees and rewards <b className="num font-medium text-slate-200">{formatUsd(a.lpPerDay)}</b>
        </span>
        {a.aavePerDay > 0 && (
          <span>
            <span className="mr-1.5 inline-block h-2 w-2 rounded-sm" style={{ background: "var(--chart-b)" }} />
            Aave supply yield <b className="num font-medium text-slate-200">{formatUsd(a.aavePerDay)}</b>
          </span>
        )}
      </div>

      <p className="border-t border-slate-800 pt-3 text-xs text-slate-400">
        {a.basis === "now" ? (
          <>
            Today's pace uses today's borrow rate ({formatPercent(now.payPer100)}) and your last 7 days of LP income.
            {past.ratio != null && (
              <>
                {" "}
                Over the past {summary.days} days it came to <b className="num text-slate-200">{past.ratio.toFixed(2)}×</b>.
              </>
            )}
          </>
        ) : (
          <>
            This is what happened over the past {summary.days} days: <b className="num text-slate-200">{formatUsd(c.lpIncomeUsd)}</b> earned against{" "}
            <b className="num text-slate-200">{formatUsd(c.interestUsd)}</b> of interest.
            {now.ratio != null && (
              <>
                {" "}
                At today's rate and pace the same loans are covered <b className="num text-slate-200">{now.ratio.toFixed(2)}×</b>.
              </>
            )}
          </>
        )}
      </p>
    </section>
  );
}
