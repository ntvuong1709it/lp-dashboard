import type { BorrowSummaryDto, DailyCoverageDto, LoanDto, ManualLoanDto } from "../../api/types";
import { Segmented } from "../ui";
import { ChangesCard } from "./ChangesCard";
import { CollateralSection } from "./CollateralSection";
import { CostTable } from "./CostTable";
import { IncomeChart } from "./IncomeChart";
import { LoansTable } from "./LoansTable";
import { RepayCard } from "./RepayCard";
import { RiskPanel } from "./RiskPanel";
import { SteadyCard } from "./SteadyCard";
import { WhatIf } from "./WhatIf";

export type DetailTab = "trend" | "loans" | "safety" | "plan";
export type DetailLayout = "tabs" | "scroll";

const TABS: { value: DetailTab; label: string }[] = [
  { value: "trend", label: "Trend" },
  { value: "loans", label: "Loans" },
  { value: "safety", label: "Collateral and safety" },
  { value: "plan", label: "Plan" },
];

interface Props {
  tab: DetailTab;
  onTab: (t: DetailTab) => void;
  layout: DetailLayout;
  onLayout: (l: DetailLayout) => void;
  days: number;
  loan: string;
  onLoan: (key: string) => void;
  summary: BorrowSummaryDto;
  loans: LoanDto[];
  manual: ManualLoanDto[];
  daily: DailyCoverageDto[];
  onAdd: () => void;
  onEdit: (loan: ManualLoanDto) => void;
}

/** The detail under the answer: trend, loans, collateral and safety, plan. As tabs, or one long scroll. */
export function BorrowDetails(p: Props) {
  const panels: Record<DetailTab, React.ReactNode> = {
    trend: <IncomeChart days={p.daily} />,
    loans: (
      <div className="space-y-6">
        {p.loans.length > 1 && <CostTable summary={p.summary} loans={p.loans} />}
        <LoansTable loans={p.loans} manual={p.manual} days={p.days} onAdd={p.onAdd} onEdit={p.onEdit} />
      </div>
    ),
    safety: (
      <div className="space-y-6">
        <CollateralSection days={p.days} loan={p.loan} onLoan={p.onLoan} loans={p.loans} />
        <ChangesCard days={p.days} loan={p.loan} loans={p.loans} />
        <RiskPanel loans={p.loans} />
      </div>
    ),
    plan: (
      <div className="space-y-6">
        <div className="grid gap-6 lg:grid-cols-2">
          <SteadyCard summary={p.summary} loans={p.loans} />
          <RepayCard summary={p.summary} />
        </div>
        <WhatIf summary={p.summary} loans={p.loans} />
      </div>
    ),
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-800">
        {p.layout === "tabs" ? (
          <div role="tablist" aria-label="Borrow details" className="-mb-px flex overflow-x-auto">
            {TABS.map((t) => (
              <button
                key={t.value}
                type="button"
                role="tab"
                id={`borrow-tab-${t.value}`}
                aria-selected={p.tab === t.value}
                aria-controls={`borrow-panel-${t.value}`}
                onClick={() => p.onTab(t.value)}
                className={`whitespace-nowrap border-b-2 px-3.5 py-2 text-sm ${
                  p.tab === t.value ? "border-sky-400 font-semibold text-slate-50" : "border-transparent text-slate-400 hover:text-slate-200"
                }`}
              >
                {t.label}
              </button>
            ))}
          </div>
        ) : (
          <h2 className="py-2 text-sm font-medium text-slate-300">Details</h2>
        )}
        <div className="pb-1.5">
          <Segmented
            label="Details layout"
            options={[
              { label: "Tabs", value: "tabs" as DetailLayout },
              { label: "One scroll", value: "scroll" as DetailLayout },
            ]}
            value={p.layout}
            onChange={p.onLayout}
          />
        </div>
      </div>

      {p.layout === "tabs" ? (
        <div role="tabpanel" id={`borrow-panel-${p.tab}`} aria-labelledby={`borrow-tab-${p.tab}`}>
          {panels[p.tab]}
        </div>
      ) : (
        <div className="space-y-8">
          {TABS.map((t) => (
            <section key={t.value} aria-label={t.label}>
              <h3 className="mb-3 text-lg font-semibold tracking-tight">{t.label}</h3>
              {panels[t.value]}
            </section>
          ))}
        </div>
      )}
    </div>
  );
}
