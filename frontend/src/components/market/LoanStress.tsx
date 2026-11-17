import type { MarketLoanStressDto } from "../../api/types";
import { formatUsd } from "../../lib/format";
import { BUFFER_CLASS, BUFFER_LABEL, STRESS_CLASS, STRESS_LABEL, stressValue } from "../../lib/market";
import { useHideBalances } from "../../lib/privacy";
import { Chip } from "../borrow/bits";

/** Where the loan stands: its buffer, its level and how far the price can fall. */
export function LoanBufferBlock({ stress }: { stress: MarketLoanStressDto }) {
  useHideBalances(); // re-render when the toggle changes; formatters read the flag
  const ltv = stress.kind === "Ltv";
  return (
    <div>
      <div className="text-xs uppercase tracking-[.05em] text-muted">Your loan buffer</div>
      <div className="mt-1.5 flex flex-wrap items-center gap-2.5">
        <Chip className={BUFFER_CLASS[stress.buffer]}>{BUFFER_LABEL[stress.buffer]}</Chip>
        <span className="num">{ltv ? `LTV ${stressValue("Ltv", stress.value)}` : `Health factor ${stressValue("HealthFactor", stress.value)}`}</span>
      </div>
      <div className="mt-1.5 text-xs text-faint">
        {ltv && stress.liquidationLtvPercent != null && (
          <>
            {stress.marginCallLtvPercent != null && `Margin call at ${stress.marginCallLtvPercent}%, `}
            liquidation at {stress.liquidationLtvPercent}%.
            <br />
          </>
        )}
        <span className="num font-medium text-fg">{stress.dropToLiquidationPercent.toFixed(0)}%</span> price drop to liquidation.
        Debt {formatUsd(stress.debtUsd)} against {formatUsd(stress.collateralUsd)} collateral.
      </div>
    </div>
  );
}

/** What a 10, 20 and 30 percent fall does, and the collateral to add. The collateral is assumed to fall one for one. */
export function LoanStressTable({ stress }: { stress: MarketLoanStressDto }) {
  useHideBalances();
  const target =
    stress.kind === "Ltv" ? `Add to stay under ${stress.marginCallLtvPercent ?? stress.liquidationLtvPercent}%` : "Add to keep HF at 1.2";
  return (
    <table className="w-full text-xs">
      <thead>
        <tr className="text-left text-muted">
          <th className="pb-1 pr-2 font-medium">If prices fall</th>
          <th className="pb-1 pr-2 text-right font-medium">{stress.kind === "Ltv" ? "LTV" : "HF"}</th>
          <th className="pb-1 pr-2 font-medium">Status</th>
          <th className="pb-1 text-right font-medium">{target}</th>
        </tr>
      </thead>
      <tbody>
        {stress.rows.map((r) => (
          <tr key={r.fallPercent} className="border-t border-line">
            <td className="py-1.5 pr-2">−{r.fallPercent}%</td>
            <td className="num py-1.5 pr-2 text-right">{stressValue(stress.kind, r.value)}</td>
            <td className="py-1.5 pr-2">
              <Chip className={STRESS_CLASS[r.status]}>{STRESS_LABEL[r.status]}</Chip>
            </td>
            <td className="num py-1.5 text-right">{r.addCollateralUsd > 0 ? formatUsd(r.addCollateralUsd) : "nothing"}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
