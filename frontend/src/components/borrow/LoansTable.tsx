import type { LoanDto, ManualLoanDto } from "../../api/types";
import { useDeleteManualLoan } from "../../api/client";
import { riskText } from "../../lib/borrow";
import { formatPercent, formatUsd } from "../../lib/format";
import { useState } from "react";
import { Panel } from "../ui";
import { Delta, LevelChip } from "./bits";

/** Every open loan with what it has cost in the window and its risk; manual loans can be edited here. */
export function LoansTable({
  loans,
  manual,
  days,
  onAdd,
  onEdit,
}: {
  loans: LoanDto[];
  manual: ManualLoanDto[];
  days: number;
  onAdd: () => void;
  onEdit: (loan: ManualLoanDto) => void;
}) {
  const remove = useDeleteManualLoan();
  const [confirming, setConfirming] = useState<number | null>(null);

  return (
    <Panel
      title="Loans"
      actions={
        <button className="rounded-md border border-line-strong px-2.5 py-1 text-sm hover:bg-raised" onClick={onAdd}>
          Add manual loan
        </button>
      }
    >
      <div className="overflow-x-auto">
        <table className="w-full min-w-[46rem] text-sm">
          <thead>
            <tr className="text-left text-xs uppercase tracking-[.04em] text-muted">
              <th className="px-2.5 py-1.5 font-medium">Source</th>
              <th className="px-2.5 py-1.5 font-medium">Loan</th>
              <th className="px-2.5 py-1.5 text-right font-medium">Debt</th>
              <th className="px-2.5 py-1.5 text-right font-medium">APR</th>
              <th className="px-2.5 py-1.5 text-right font-medium">Interest {days}d</th>
              <th className="px-2.5 py-1.5 text-right font-medium">Collateral ÷ debt</th>
              <th className="px-2.5 py-1.5 font-medium">Risk</th>
              <th className="px-2.5 py-1.5" />
            </tr>
          </thead>
          <tbody>
            {loans.map((l) => {
              const edit = l.manualId == null ? undefined : manual.find((m) => m.id === l.manualId);
              return (
                <tr key={l.key} className="border-t border-line">
                  <td className="px-2.5 py-2.5">
                    <span className={`rounded border px-1.5 text-[11px] ${l.kind === "manual" ? "border-warn-fill/60 text-warn" : "border-line-strong text-muted"}`}>
                      {l.source}
                    </span>
                  </td>
                  <td className="px-2.5 py-2.5 font-medium">{l.label}</td>
                  <td className="px-2.5 py-2.5 text-right num">{formatUsd(l.debtUsd)}</td>
                  <td className="px-2.5 py-2.5 text-right num">{formatPercent(l.aprPercent)}</td>
                  <td className="px-2.5 py-2.5 text-right num">{formatUsd(l.interestInWindowUsd)}</td>
                  <td className="px-2.5 py-2.5 text-right num">
                    {l.collateralRatio == null ? (
                      <span className="text-faint">no collateral data</span>
                    ) : (
                      <>
                        {l.collateralRatio.toFixed(2)}×{" "}
                        {l.collateralRatioChange != null && (
                          <Delta value={l.collateralRatioChange} text={Math.abs(l.collateralRatioChange).toFixed(2)} goodUp />
                        )}
                      </>
                    )}
                  </td>
                  <td className="px-2.5 py-2.5">
                    {riskText(l) ? <LevelChip level={l.risk.level}>{riskText(l)}</LevelChip> : <span className="text-faint">—</span>}
                  </td>
                  <td className="px-2.5 py-2.5">
                    {edit && (
                      <div className="flex justify-end gap-1.5">
                        <button className="rounded px-2 py-0.5 text-xs text-fg-2 hover:bg-raised" onClick={() => onEdit(edit)}>
                          Edit
                        </button>
                        {confirming === edit.id ? (
                          <button
                            className="rounded bg-down-fill px-2 py-0.5 text-xs font-medium text-white hover:bg-down-fill disabled:opacity-50"
                            disabled={remove.isPending}
                            onClick={() => remove.mutate({ id: edit.id, owner: edit.owner }, { onSettled: () => setConfirming(null) })}
                          >
                            Delete for good
                          </button>
                        ) : (
                          <button className="rounded px-2 py-0.5 text-xs text-fg-2 hover:bg-raised" onClick={() => setConfirming(edit.id)}>
                            Delete
                          </button>
                        )}
                      </div>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </Panel>
  );
}
