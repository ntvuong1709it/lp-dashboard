import type { BorrowSummaryDto } from "../../api/types";
import type { Answer } from "../../lib/borrowAnswer";
import { formatPercent, formatUsd } from "../../lib/format";
import { Panel } from "../ui";

const d2 = (v: number) => `$${v.toFixed(2)}`;

/** Borrow rate against yield rate, in plain dollars: per $100 borrowed, over one year. */
export function RateCompare({ a, summary }: { a: Answer; summary: BorrowSummaryDto }) {
  const earn = a.lpPer100 + a.aavePer100;
  const keep = earn - a.payPer100;
  const max = Math.max(a.payPer100, earn, 0.01) * 1.1;
  const at = (v: number) => `${(v / max) * 100}%`;
  const share = (v: number) => (earn > 0 ? `${(v / earn) * 100}%` : "0%");
  const r = summary.runRate;
  const lpIsSmall = r.currentLpAprPercent != null && r.openPositionValueUsd > 0 && r.openPositionValueUsd < a.debtUsd;

  return (
    <Panel title="Borrow rate vs yield rate">
      <p className="mb-3 text-sm text-muted">
        For every <b className="text-fg-2">$100</b> you borrow, over one year:
      </p>
      <div className="space-y-3">
        <div className="grid grid-cols-[4rem_1fr] items-center gap-2.5">
          <span className="text-sm font-medium">You pay</span>
          <div className="relative h-9 rounded-md bg-raised">
            <div className="absolute inset-y-0 left-0 flex items-center justify-center overflow-hidden rounded-md bg-down-fill" style={{ width: at(a.payPer100) }}>
              <span className="num whitespace-nowrap px-1.5 text-xs font-medium text-surface">{d2(a.payPer100)}</span>
            </div>
          </div>
        </div>
        <div className="grid grid-cols-[4rem_1fr] items-center gap-2.5">
          <span className="text-sm font-medium">You earn</span>
          <div className="relative h-9 rounded-md bg-raised">
            <div className="absolute inset-y-0 left-0 flex overflow-hidden rounded-md" style={{ width: at(earn) }}>
              <span
                className="flex items-center justify-center overflow-hidden"
                style={{ width: share(a.lpPer100), background: "var(--chart-a)" }}
              >
                <span className="num whitespace-nowrap px-1.5 text-xs font-medium text-surface">LP {d2(a.lpPer100)}</span>
              </span>
              {a.aavePer100 > 0 && (
                <span
                  className="flex items-center justify-end overflow-hidden"
                  style={{ width: share(a.aavePer100), background: "var(--chart-b)" }}
                >
                  <span className="num relative z-10 whitespace-nowrap px-2 text-xs font-medium text-surface">Aave {d2(a.aavePer100)}</span>
                </span>
              )}
            </div>
            <i className="absolute -bottom-1.5 -top-1.5 border-l-2 border-dashed border-fg" style={{ left: at(a.payPer100) }} title="What you pay" />
          </div>
        </div>
        <p className="ml-[4.65rem] text-xs text-faint">Dashed line = what you pay. Everything earned past it is yours to keep.</p>
      </div>

      <div className="mt-3 flex flex-wrap items-baseline gap-x-3 rounded-md bg-raised/50 px-3.5 py-3">
        <span className={`num text-3xl font-semibold tracking-tight ${keep >= 0 ? "text-up" : "text-down"}`}>
          {keep >= 0 ? "+" : "-"}
          {d2(Math.abs(keep))}
        </span>
        <span className="text-sm text-muted">
          {keep >= 0 ? "kept" : "short"} per $100 a year, about {formatUsd(Math.abs(a.leftPerDay) * 365)} a year on your debt
        </span>
      </div>

      {lpIsSmall && a.basis === "now" && (
        <p className="mt-3 text-xs text-faint">
          Why LP adds only {d2(a.lpPer100)}: it earns about {formatPercent(r.currentLpAprPercent)} a year, but on {formatUsd(r.openPositionValueUsd)}, which is{" "}
          {Math.round((r.openPositionValueUsd / a.debtUsd) * 100)}% of your {formatUsd(a.debtUsd)} debt.
          {a.aavePer100 > 0 && " The rest comes from the supply yield on your Aave collateral."}
        </p>
      )}
    </Panel>
  );
}
