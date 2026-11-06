import { usePoolEstimate } from "../../api/client";
import type { PoolDto } from "../../api/types";
import { protocolLabels } from "../../lib/format";
import { DEFAULT_CAPITAL, apr, feeLabel, poolRate } from "../../lib/pools";
import { TokenPair } from "./TokenPair";

/** A pool you hold: what it paid, what the model says your range should earn, and what your position actually earned. */
export function YourPoolCard({
  pool: p,
  realisedApr,
  active,
  onSelect,
}: {
  pool: PoolDto;
  realisedApr: number | null;
  active: boolean;
  onSelect: () => void;
}) {
  const mine = p.myHalfWidthPercent;
  const { data } = usePoolEstimate(p.id, mine ?? 10, DEFAULT_CAPITAL, 30, p.feePercent);
  return (
    <button
      type="button"
      onClick={onSelect}
      aria-pressed={active}
      className={`flex flex-col gap-2 rounded-lg border border-l-[3px] border-slate-800 border-l-emerald-400 p-3 text-left hover:border-sky-400 ${active ? "border-sky-400 bg-sky-400/10" : "bg-slate-900"}`}
    >
      <span className="flex items-center justify-between gap-2 font-semibold">
        <span className="flex items-center gap-2">
          <TokenPair symbol={p.symbol} size={22} />
          <span>
            {p.symbol}{" "}
            <span className="rounded-full border border-slate-700 px-1.5 text-[11px] font-normal text-slate-400">
              {feeLabel(p.feePercent)}
            </span>
          </span>
        </span>
        <span className="text-xs font-normal text-slate-500">{protocolLabels[p.protocol]}</span>
      </span>
      <span className="grid grid-cols-3 gap-2 text-xs text-slate-400">
        <span>
          Pool rate
          <b className="num block text-sm font-medium text-slate-50">{apr(poolRate(p))}</b>
        </span>
        <span>
          Your range ±{mine?.toFixed(0)}% (model)
          <b className="num block text-sm font-medium text-slate-50">{data ? apr(data.apr) : "…"}</b>
        </span>
        <span>
          Your realised APR
          <b className="num block text-sm font-medium text-slate-50">{apr(realisedApr)}</b>
        </span>
      </span>
    </button>
  );
}
