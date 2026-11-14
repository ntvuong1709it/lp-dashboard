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
      className={`flex flex-col gap-2 rounded-lg border border-l-[3px] border-line border-l-up-fill p-3 text-left hover:border-accent-fill ${active ? "border-accent-fill bg-accent/10" : "bg-panel"}`}
    >
      <span className="flex items-center justify-between gap-2 font-semibold">
        <span className="flex items-center gap-2">
          <TokenPair symbol={p.symbol} size={22} />
          <span>
            {p.symbol}{" "}
            <span className="rounded-full border border-line-strong px-1.5 text-[11px] font-normal text-muted">
              {feeLabel(p.feePercent)}
            </span>
          </span>
        </span>
        <span className="text-xs font-normal text-faint">{protocolLabels[p.protocol]}</span>
      </span>
      <span className="grid grid-cols-3 gap-2 text-xs text-muted">
        <span>
          Pool rate
          <b className="num block text-sm font-medium text-fg">{apr(poolRate(p))}</b>
        </span>
        <span>
          Your range ±{mine?.toFixed(0)}% (model)
          <b className="num block text-sm font-medium text-fg">{data ? apr(data.apr) : "…"}</b>
        </span>
        <span>
          Your realised APR
          <b className="num block text-sm font-medium text-fg">{apr(realisedApr)}</b>
        </span>
      </span>
    </button>
  );
}
