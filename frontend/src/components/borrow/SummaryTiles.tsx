import type { BorrowSummaryDto } from "../../api/types";
import { formatUsd, pnlClass } from "../../lib/format";
import { StatusChip } from "./bits";

export function SummaryTiles({ summary }: { summary: BorrowSummaryDto }) {
  const c = summary.coverage;
  const total = c.lpIncomeUsd || 1;
  const share = (v: number) => `${(v / total) * 100}%`;

  return (
    <div className="space-y-2">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <div className="rounded-lg border border-slate-800 bg-slate-900 p-4">
          <div className="text-xs uppercase tracking-[.05em] text-slate-400">Coverage · {summary.days}d</div>
          <div className="mt-1 flex flex-wrap items-center gap-3">
            <span className="text-3xl font-medium tracking-tight num">{c.ratio == null ? "—" : `${c.ratio.toFixed(2)}×`}</span>
            <StatusChip status={c.status} />
          </div>
          <div className="mt-1 text-xs text-slate-500">LP income ÷ borrow interest</div>
        </div>
        <div className="rounded-lg border border-slate-800 bg-slate-900 p-4">
          <div className="text-xs uppercase tracking-[.05em] text-slate-400">Net carry</div>
          <div className={`mt-1 text-2xl font-medium tracking-tight num ${pnlClass(c.netCarryUsd)}`}>{formatUsd(c.netCarryUsd, true)}</div>
          <div className="mt-1 text-xs text-slate-500">Income − interest − gas</div>
        </div>
        <div className="rounded-lg border border-slate-800 bg-slate-900 p-4">
          <div className="text-xs uppercase tracking-[.05em] text-slate-400">Interest accrued</div>
          <div className="mt-1 text-2xl font-medium tracking-tight num">{formatUsd(c.interestUsd)}</div>
          <div className="mt-1 text-xs text-slate-500">Claimed or not, as it builds up</div>
        </div>
        <div className="flex flex-col rounded-lg border border-slate-800 bg-slate-900 p-4">
          <div className="text-xs uppercase tracking-[.05em] text-slate-400">LP income</div>
          <div className="mt-1 text-2xl font-medium tracking-tight num">{formatUsd(c.lpIncomeUsd)}</div>
          <div className="mt-1 text-xs text-slate-500">
            Fees {formatUsd(c.lpFeesUsd)} · Rewards {formatUsd(c.lpRewardsUsd)}
            {c.supplyYieldUsd > 0 && <> · Supply {formatUsd(c.supplyYieldUsd)}</>}
          </div>
          <div className="mt-auto flex h-1.5 overflow-hidden rounded-full bg-slate-800 pt-0">
            <i style={{ width: share(c.lpFeesUsd), background: "var(--chart-a)" }} />
            <i style={{ width: share(c.lpRewardsUsd), background: "var(--chart-d)" }} />
            <i style={{ width: share(c.supplyYieldUsd), background: "var(--chart-b)" }} />
          </div>
        </div>
      </div>
      <p className="text-xs text-slate-500">
        Gas on LP claims and rebalances in this window: <span className="num">{formatUsd(c.gasUsd)}</span>. Unclaimed fees and pending
        rewards count; they are priced when they accrued.
      </p>
    </div>
  );
}
