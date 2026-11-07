import type { LoanCostDto, LoanDto, BorrowSummaryDto } from "../../api/types";
import { formatPercent, formatUsd } from "../../lib/format";
import { useWallets } from "../../lib/wallets";
import { Panel } from "../ui";

const cells = (c: LoanCostDto, className = "") => (
  <>
    {[c.perDayUsd, c.perWeekUsd, c.perMonthUsd, c.perYearUsd].map((v, i) => (
      <td key={i} className={`px-2.5 py-2 text-right num ${className}`}>
        {formatUsd(v)}
      </td>
    ))}
  </>
);

/** What each loan costs per day, week, month and year at today's debt and rate, grouped by wallet, beside LP income at the same pace. */
export function CostTable({ summary, loans }: { summary: BorrowSummaryDto; loans: LoanDto[] }) {
  const walletOf = useWallets();
  const walletName = (owner: string) => {
    const w = walletOf(owner);
    return w.label ? `${w.label} (${w.short})` : w.short;
  };
  const byKey = new Map(loans.map((l) => [l.key, l]));
  const { costs } = summary;

  return (
    <Panel title="What you pay">
      <div className="overflow-x-auto">
        <table className="w-full min-w-[44rem] text-sm">
          <thead>
            <tr className="text-left text-xs uppercase tracking-[.04em] text-slate-400">
              <th className="px-2.5 py-1.5 font-medium">Loan</th>
              <th className="px-2.5 py-1.5 text-right font-medium">Debt</th>
              <th className="px-2.5 py-1.5 text-right font-medium">APR</th>
              <th className="px-2.5 py-1.5 text-right font-medium">Per day</th>
              <th className="px-2.5 py-1.5 text-right font-medium">Per week</th>
              <th className="px-2.5 py-1.5 text-right font-medium">Per month</th>
              <th className="px-2.5 py-1.5 text-right font-medium">Per year</th>
            </tr>
          </thead>
          <tbody>
            {costs.wallets.map((w) => (
              <WalletRows key={w.owner} name={walletName(w.owner)} count={w.loanKeys.length} subtotal={w.subtotal}>
                {w.loanKeys.map((k) => byKey.get(k)).filter((l): l is LoanDto => l != null).map((l) => (
                  <tr key={l.key} className="border-t border-slate-800">
                    <td className="px-2.5 py-2">
                      <span className="mr-2 rounded border border-slate-700 px-1.5 text-[11px] text-slate-400">{l.source}</span>
                      {l.label}
                    </td>
                    <td className="px-2.5 py-2 text-right num">{formatUsd(l.debtUsd)}</td>
                    <td className="px-2.5 py-2 text-right num">{formatPercent(l.aprPercent)}</td>
                    {cells(l.cost)}
                  </tr>
                ))}
              </WalletRows>
            ))}
          </tbody>
          <tfoot>
            <tr className="border-t border-slate-700 font-semibold">
              <td className="px-2.5 py-2" colSpan={3}>You pay, all loans</td>
              {cells(costs.total)}
            </tr>
            <tr className="border-t border-slate-800">
              <td className="px-2.5 py-2" colSpan={3}>LP income at the last 7 days' pace</td>
              {cells(costs.incomeAtPace)}
            </tr>
            <tr className="border-t border-slate-700 font-semibold">
              <td className="px-2.5 py-2" colSpan={3}>Left over</td>
              {(["perDayUsd", "perWeekUsd", "perMonthUsd", "perYearUsd"] as const).map((k) => (
                <td key={k} className={`px-2.5 py-2 text-right num ${costs.leftOver[k] >= 0 ? "text-emerald-400" : "text-rose-400"}`}>
                  {formatUsd(costs.leftOver[k], true)}
                </td>
              ))}
            </tr>
          </tfoot>
        </table>
      </div>
      <p className="mt-2 text-xs text-slate-500">
        Interest at today's balances and rates; a week is 7 days and a month a twelfth of the year. Aave rates float, so these move.
        Income is LP fees, rewards and Aave supply yield, before gas.
      </p>
    </Panel>
  );
}

function WalletRows({
  name,
  count,
  subtotal,
  children,
}: {
  name: string;
  count: number;
  subtotal: LoanCostDto;
  children: React.ReactNode;
}) {
  return (
    <>
      <tr className="bg-slate-800/60">
        <td className="px-2.5 py-2" colSpan={3}>
          <b className="num">{name}</b> <span className="text-xs text-slate-400">{count} loan{count > 1 ? "s" : ""}</span>
        </td>
        {cells(subtotal, "font-semibold")}
      </tr>
      {children}
    </>
  );
}
