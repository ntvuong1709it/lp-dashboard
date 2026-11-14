import type { BorrowSummaryDto } from "../../api/types";
import { formatPercent, formatUsd } from "../../lib/format";
import { Chip } from "./bits";
import { Panel } from "../ui";

/** A suggestion from the numbers; the app never moves money. */
export function RepayCard({ summary }: { summary: BorrowSummaryDto }) {
  const r = summary.repay;
  const days = summary.days;

  return (
    <Panel
      title="Repay or keep"
      actions={
        r.mode === "None" ? undefined : (
          <Chip className={r.mode === "Surplus" ? "bg-up-fill/15 text-up" : "bg-down-fill/15 text-down"}>
            {r.mode === "Surplus" ? "Surplus" : "Shortfall"}
          </Chip>
        )
      }
    >
      {r.mode === "Surplus" ? (
        <p className="text-sm">
          Net carry over {days} days is <b className="num text-up">{formatUsd(summary.coverage.netCarryUsd)}</b>. Using it to repay
          your highest-rate loan ({r.loanLabel}, {formatPercent(r.aprPercent)}) would save about{" "}
          <b className="num">{formatUsd(r.savingPerYearUsd)}</b> a year in interest.
        </p>
      ) : r.mode === "Shortfall" ? (
        <p className="text-sm">
          To reach 1.0× at current income, interest has to fall by about <b className="num text-down">{formatUsd(r.savingPerYearUsd)}</b> a
          year. Repaying about <b className="num">{formatUsd(r.amountUsd)}</b> of {r.loanLabel} ({formatPercent(r.aprPercent)}, your highest
          rate) does that.
        </p>
      ) : (
        <p className="text-sm text-faint">Nothing to suggest yet: no interest has accrued in this window.</p>
      )}
      <p className="mt-2 text-xs text-faint">A suggestion from the numbers above. The app never moves money.</p>
    </Panel>
  );
}
