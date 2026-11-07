import { useState } from "react";
import { useBorrowDaily, useBorrowLoans, useBorrowSummary, useManualLoans } from "../api/client";
import type { ManualLoanDto } from "../api/types";
import { BorrowBanners } from "../components/borrow/BorrowBanners";
import { BreakEven } from "../components/borrow/BreakEven";
import { ChangesCard } from "../components/borrow/ChangesCard";
import { CollateralSection } from "../components/borrow/CollateralSection";
import { CostTable } from "../components/borrow/CostTable";
import { IncomeChart } from "../components/borrow/IncomeChart";
import { LoansTable } from "../components/borrow/LoansTable";
import { ManualLoanForm } from "../components/borrow/ManualLoanForm";
import { RepayCard } from "../components/borrow/RepayCard";
import { RiskPanel } from "../components/borrow/RiskPanel";
import { SteadyCard } from "../components/borrow/SteadyCard";
import { SummaryTiles } from "../components/borrow/SummaryTiles";
import { WhatIf } from "../components/borrow/WhatIf";
import { UpdatedAgo } from "../components/UpdatedAgo";
import { ErrorText, PageSkeleton, Segmented } from "../components/ui";
import { DAY_OPTIONS } from "../lib/borrow";
import { useOwner } from "../lib/owner";
import { useHideBalances } from "../lib/privacy";
import { usePersistentState } from "../lib/usePersistentState";

type FormState = { loan: ManualLoanDto | null } | null;

export function BorrowPage() {
  useHideBalances(); // formatters read the flag, so re-render when it changes
  const { owner } = useOwner();
  const [days, setDays] = usePersistentState<number>("lpdash.borrow.days", 30);
  const [loan, setLoan] = useState("all");
  const [form, setForm] = useState<FormState>(null);

  const summary = useBorrowSummary(owner, days);
  const loans = useBorrowLoans(owner, days);
  const daily = useBorrowDaily(owner, days);
  const manual = useManualLoans(owner);

  const error = summary.error ?? loans.error ?? daily.error;
  const hasLoans = summary.data?.hasLoans ?? false;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Borrow</h1>
          <p className="text-sm text-slate-400">Does LP income pay the interest on your loans?</p>
        </div>
        <div className="flex items-center gap-3">
          {hasLoans && <Segmented label="Window" options={DAY_OPTIONS} value={days} onChange={setDays} />}
          <UpdatedAgo />
        </div>
      </div>

      {error ? (
        <ErrorText error={error} />
      ) : summary.isLoading || loans.isLoading ? (
        <PageSkeleton cards={4} />
      ) : !summary.data || !loans.data ? null : !hasLoans ? (
        <>
          <section className="rounded-lg border border-slate-800 bg-slate-900 px-4 py-10 text-center">
            <h2 className="text-lg font-semibold">No loans yet</h2>
            <p className="mx-auto mt-1.5 max-w-xl text-sm text-slate-400">
              Add a loan you borrowed against your LP tokens, such as one on Binance. The app then shows what it costs, whether LP income
              covers it, and how close you are to liquidation.
            </p>
            <button className="mt-4 rounded-md bg-sky-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-sky-500" onClick={() => setForm({ loan: null })}>
              Add manual loan
            </button>
          </section>
          {form && <ManualLoanForm key={form.loan?.id ?? "new"} loan={form.loan} onClose={() => setForm(null)} />}
        </>
      ) : (
        <>
          <BorrowBanners summary={summary.data} loans={loans.data} />
          <SummaryTiles summary={summary.data} />
          <CostTable summary={summary.data} loans={loans.data} />
          {form && <ManualLoanForm key={form.loan?.id ?? "new"} loan={form.loan} onClose={() => setForm(null)} />}
          <div className="grid gap-6 lg:grid-cols-[2fr_1fr]">
            <IncomeChart days={daily.data ?? []} />
            <BreakEven summary={summary.data} />
          </div>
          <LoansTable
            loans={loans.data}
            manual={manual.data ?? []}
            days={days}
            onAdd={() => setForm({ loan: null })}
            onEdit={(l) => setForm({ loan: l })}
          />
          <CollateralSection days={days} loan={loan} onLoan={setLoan} loans={loans.data} />
          <ChangesCard days={days} loan={loan} loans={loans.data} />
          <RiskPanel loans={loans.data} />
          <div className="grid gap-6 lg:grid-cols-2">
            <SteadyCard summary={summary.data} loans={loans.data} />
            <RepayCard summary={summary.data} />
          </div>
          <WhatIf summary={summary.data} loans={loans.data} />
        </>
      )}
    </div>
  );
}
