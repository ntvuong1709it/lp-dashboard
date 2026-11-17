import type { BorrowSummaryDto, LoanDto } from "../api/types";

/** "now": today's borrow rate and the last 7 days of LP income. "past": what really happened over the window. */
export type Basis = "now" | "past";

export const PERIOD_COLUMNS = [
  { key: "d", label: "Per day", factor: 1 },
  { key: "w", label: "Per week", factor: 7 },
  { key: "m", label: "Per month", factor: 365 / 12 },
  { key: "y", label: "Per year", factor: 365 },
] as const;

/** Everything the answer zone shows, as per-day dollar amounts plus the same per $100 of debt a year. */
export interface Answer {
  basis: Basis;
  payPerDay: number;
  /** Aave supply yield. It comes with the loan, so it is not something the owner works for. */
  aavePerDay: number;
  /** LP fees and rewards: the variable part. */
  lpPerDay: number;
  incomePerDay: number;
  /** What LP has to bring in on top of the Aave yield to cover the interest (never below zero). */
  needPerDay: number;
  leftPerDay: number;
  ratio: number | null;
  debtUsd: number;
  /** Dollars of interest per $100 of debt per year, i.e. the borrow rate. */
  payPer100: number;
  lpPer100: number;
  aavePer100: number;
}

/** Interest per day for one group of loans, e.g. "Manual · Binance" or "Aave". */
export interface PayPart {
  key: string;
  label: string;
  perDay: number;
}

/** Splits what you pay into Aave and each manual source, so manual loans can be read on their own. */
export function payPartsFor(summary: BorrowSummaryDto, loans: LoanDto[], basis: Basis): PayPart[] {
  const days = Math.max(1, summary.days);
  const parts = new Map<string, PayPart>();
  for (const l of loans) {
    const key = l.kind === "manual" ? `manual:${l.source}` : "aave";
    const label = l.kind === "manual" ? `Manual · ${l.source}` : "Aave";
    const perDay = basis === "now" ? l.cost.perDayUsd : l.interestInWindowUsd / days;
    const part = parts.get(key);
    if (part) part.perDay += perDay;
    else parts.set(key, { key, label, perDay });
  }
  return [...parts.values()];
}

export function answerFor(summary: BorrowSummaryDto, loans: LoanDto[], basis: Basis): Answer {
  const c = summary.coverage;
  const days = Math.max(1, summary.days);
  const pay = basis === "now" ? summary.costs.total.perDayUsd : c.interestUsd / days;
  const aave = basis === "now" ? summary.costs.supplyYieldAtRate.perDayUsd : c.supplyYieldUsd / days;
  const lp = basis === "now" ? summary.costs.lpIncomeAtPace.perDayUsd : (c.lpFeesUsd + c.lpRewardsUsd) / days;
  const debt = loans.reduce((sum, l) => sum + l.debtUsd, 0);
  const per100 = (perDay: number) => (debt > 0 ? ((perDay * 365) / debt) * 100 : 0);

  return {
    basis,
    payPerDay: pay,
    aavePerDay: aave,
    lpPerDay: lp,
    incomePerDay: lp + aave,
    needPerDay: Math.max(0, pay - aave),
    leftPerDay: lp + aave - pay,
    ratio: pay > 0 ? (lp + aave) / pay : null,
    debtUsd: debt,
    payPer100: per100(pay),
    lpPer100: per100(lp),
    aavePer100: per100(aave),
  };
}
