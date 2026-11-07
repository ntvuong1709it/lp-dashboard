import { useMemo, useState } from "react";
import { useBorrowDaily, useBorrowLoans, useBorrowSummary, useManualLoans } from "../api/client";
import type { ManualLoanDto } from "../api/types";
import { BorrowBanners } from "../components/borrow/BorrowBanners";
import { BorrowDetails, type DetailLayout, type DetailTab } from "../components/borrow/BorrowDetails";
import { ManualLoanForm } from "../components/borrow/ManualLoanForm";
import { MoneyTable } from "../components/borrow/MoneyTable";
import { RateCompare } from "../components/borrow/RateCompare";
import { Room } from "../components/borrow/Room";
import { Verdict } from "../components/borrow/Verdict";
import { UpdatedAgo } from "../components/UpdatedAgo";
import { ErrorText, PageSkeleton, Segmented } from "../components/ui";
import { DAY_OPTIONS } from "../lib/borrow";
import { answerFor, type Basis } from "../lib/borrowAnswer";
import { useOwner } from "../lib/owner";
import { useHideBalances } from "../lib/privacy";
import { usePersistentState } from "../lib/usePersistentState";

type FormState = { loan: ManualLoanDto | null } | null;

export function BorrowPage() {
  useHideBalances(); // formatters read the flag, so re-render when it changes
  const { owner } = useOwner();
  const [days, setDays] = usePersistentState<number>("lpdash.borrow.days", 30);
  const [basis, setBasis] = usePersistentState<Basis>("lpdash.borrow.basis", "now");
  const [tab, setTab] = usePersistentState<DetailTab>("lpdash.borrow.tab", "trend");
  const [layout, setLayout] = usePersistentState<DetailLayout>("lpdash.borrow.layout", "tabs");
  const [loan, setLoan] = useState("all");
  const [form, setForm] = useState<FormState>(null);

  const summary = useBorrowSummary(owner, days);
  const loans = useBorrowLoans(owner, days);
  const daily = useBorrowDaily(owner, days);
  const manual = useManualLoans(owner);

  const error = summary.error ?? loans.error ?? daily.error;
  const hasLoans = summary.data?.hasLoans ?? false;

  const answers = useMemo(
    () =>
      summary.data && loans.data
        ? { now: answerFor(summary.data, loans.data, "now"), past: answerFor(summary.data, loans.data, "past") }
        : null,
    [summary.data, loans.data],
  );

  const openForm = (l: ManualLoanDto | null) => {
    setTab("loans");
    setForm({ loan: l });
  };

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Borrow</h1>
          <p className="text-sm text-slate-400">Does LP income pay the interest on your loans?</p>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          {hasLoans && (
            <>
              <Segmented
                label="Basis"
                options={[
                  { label: "Today's pace", value: "now" as Basis },
                  { label: `Past ${days}d`, value: "past" as Basis },
                ]}
                value={basis}
                onChange={setBasis}
              />
              <Segmented label="Window" options={DAY_OPTIONS} value={days} onChange={setDays} />
            </>
          )}
          <UpdatedAgo />
        </div>
      </div>

      {error ? (
        <ErrorText error={error} />
      ) : summary.isLoading || loans.isLoading ? (
        <PageSkeleton cards={4} />
      ) : !summary.data || !loans.data || !answers ? null : !hasLoans ? (
        <>
          <section className="rounded-lg border border-slate-800 bg-slate-900 px-4 py-10 text-center">
            <h2 className="text-lg font-semibold">No loans yet</h2>
            <p className="mx-auto mt-1.5 max-w-xl text-sm text-slate-400">
              Add a loan you borrowed against your LP tokens, such as one on Binance. The app then shows what it costs, whether LP income
              covers it, and how close you are to liquidation.
            </p>
            <button className="mt-4 rounded-md bg-sky-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-sky-500" onClick={() => openForm(null)}>
              Add manual loan
            </button>
          </section>
          {form && <ManualLoanForm key={form.loan?.id ?? "new"} loan={form.loan} onClose={() => setForm(null)} />}
        </>
      ) : (
        <>
          <BorrowBanners summary={summary.data} loans={loans.data} />
          <Verdict shown={answers[basis]} now={answers.now} past={answers.past} summary={summary.data} />
          <MoneyTable a={answers[basis]} days={summary.data.days} />
          <div className="grid gap-6 lg:grid-cols-2">
            <RateCompare a={answers[basis]} summary={summary.data} />
            <Room now={answers.now} summary={summary.data} />
          </div>
          {form && <ManualLoanForm key={form.loan?.id ?? "new"} loan={form.loan} onClose={() => setForm(null)} />}
          <BorrowDetails
            tab={tab}
            onTab={setTab}
            layout={layout}
            onLayout={setLayout}
            days={days}
            loan={loan}
            onLoan={setLoan}
            summary={summary.data}
            loans={loans.data}
            manual={manual.data ?? []}
            daily={daily.data ?? []}
            onAdd={() => openForm(null)}
            onEdit={(l) => openForm(l)}
          />
        </>
      )}
    </div>
  );
}
