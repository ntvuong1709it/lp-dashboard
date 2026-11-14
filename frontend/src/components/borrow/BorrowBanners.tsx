import type { BorrowSummaryDto, LoanDto } from "../../api/types";
import { hfDrop, ltvDrop } from "../../lib/borrow";
import { formatUsd } from "../../lib/format";

type Level = "critical" | "warn" | "info";

const STYLE: Record<Level, string> = {
  critical: "border-down-fill/50 bg-down-fill/10",
  warn: "border-warn-fill/50 bg-warn-fill/10",
  info: "border-accent-fill/50 bg-accent-fill/10",
};
const MARK: Record<Level, string> = { critical: "text-down", warn: "text-warn", info: "text-accent" };

/** What needs attention, in order of severity. Built here, not on the server, so hidden balances stay hidden. */
export function BorrowBanners({ summary, loans }: { summary: BorrowSummaryDto; loans: LoanDto[] }) {
  const items: { level: Level; text: React.ReactNode }[] = [];
  const c = summary.coverage;

  for (const loan of loans) {
    const r = loan.risk;
    if (r.kind === "None" || r.value == null || r.level === "Ok") continue;
    const level: Level = r.level === "Critical" ? "critical" : "warn";
    items.push(
      r.kind === "Ltv" && r.liquidationLtvPercent != null
        ? {
            level,
            text: (
              <>
                <b>{loan.label} is at {r.value.toFixed(0)}% LTV.</b> Liquidation at {r.liquidationLtvPercent}%
                {r.marginCallLtvPercent != null && `, margin call at ${r.marginCallLtvPercent}%`}. A{" "}
                {ltvDrop(r.value, r.liquidationLtvPercent).toFixed(0)}% price drop in your collateral liquidates it.
              </>
            ),
          }
        : {
            level,
            text: (
              <>
                <b>{loan.label} has a health factor of {r.value.toFixed(2)}.</b> Collateral can fall about{" "}
                {hfDrop(r.value).toFixed(0)}% before liquidation.
              </>
            ),
          },
    );
  }

  if (c.status === "Shortfall") {
    items.push({
      level: "critical",
      text: (
        <>
          <b>LP income does not cover interest.</b> Over {summary.days} days you earned {formatUsd(c.lpIncomeUsd)} against{" "}
          {formatUsd(c.interestUsd)} of interest.
        </>
      ),
    });
  } else if (c.status === "Thin" && c.ratio != null) {
    items.push({
      level: "warn",
      text: (
        <>
          <b>Coverage is thin at {c.ratio.toFixed(2)}×.</b> A small drop in LP income or rise in borrow rates puts you below 1.0×.
        </>
      ),
    });
  }

  if (summary.dataQuality.hasMissingPrices) {
    items.push({
      level: "info",
      text: <>No price found for {summary.dataQuality.missingPriceKeys.join(", ")}. Affected figures show as zero.</>,
    });
  }

  for (const note of summary.dataQuality.historyNotes) {
    items.push({ level: "info", text: <>Aave history: {note}</> });
  }

  items.sort((a, b) => ["critical", "warn", "info"].indexOf(a.level) - ["critical", "warn", "info"].indexOf(b.level));
  if (items.length === 0) return null;

  return (
    <div className="space-y-2">
      {items.map((it, i) => (
        <div key={i} className={`flex gap-3 rounded-lg border px-3.5 py-2.5 text-sm ${STYLE[it.level]}`} role={it.level === "critical" ? "alert" : undefined}>
          <span className={`w-4 shrink-0 text-center font-bold ${MARK[it.level]}`}>{it.level === "info" ? "i" : "!"}</span>
          <span>{it.text}</span>
        </div>
      ))}
    </div>
  );
}
