import type {
  ChangesDto,
  CoverageStatus,
  LoanCostDto,
  LoanDto,
  RiskLevel,
} from "../api/types";

export type Period = "d" | "w" | "m";

export const PERIODS: {
  value: Period;
  label: string;
  /** Used in sentences: "a day". */
  text: string;
  /** Decimals for dollar amounts; small daily figures need cents. */
  digits: number;
}[] = [
  { value: "d", label: "Day", text: "a day", digits: 2 },
  { value: "w", label: "Week", text: "a week", digits: 2 },
  { value: "m", label: "Month", text: "a month", digits: 0 },
];

export const periodInfo = (p: Period) => PERIODS.find((x) => x.value === p)!;

export const perPeriod = (cost: LoanCostDto, p: Period) =>
  p === "d" ? cost.perDayUsd : p === "w" ? cost.perWeekUsd : cost.perMonthUsd;

/** Formats a dollar amount with the period's precision. */
export const usdFor = (format: (v: number, signed?: boolean) => string, p: Period) => (v: number, signed = false) =>
  p === "m" ? format(Math.round(v), signed) : format(v, signed);

export const DAY_OPTIONS = [
  { label: "7d", value: 7 },
  { label: "30d", value: 30 },
  { label: "90d", value: 90 },
];

export const STATUS_LABEL: Record<CoverageStatus, string> = {
  Covered: "Covered",
  Thin: "Thin",
  Shortfall: "Shortfall",
  NoDebt: "No interest",
};

export const STATUS_CLASS: Record<CoverageStatus, string> = {
  Covered: "bg-up-fill/15 text-up",
  Thin: "bg-warn-fill/15 text-warn",
  Shortfall: "bg-down-fill/15 text-down",
  NoDebt: "bg-raised text-muted",
};

export const LEVEL_LABEL: Record<RiskLevel, string> = {
  Ok: "Healthy",
  Warn: "Warning",
  Critical: "Critical",
};

export const LEVEL_CLASS: Record<RiskLevel, string> = {
  Ok: "bg-up-fill/15 text-up",
  Warn: "bg-warn-fill/15 text-warn",
  Critical: "bg-down-fill/15 text-down",
};

/** Same cut-offs as the server (BorrowCoverageCalculator). */
export const HEALTH_WARN = 1.5;
export const HEALTH_CRITICAL = 1.2;
export const LTV_WARN_SHARE = 0.15;
export const LTV_CRITICAL_SHARE = 0.05;

export function statusOf(ratio: number | null): CoverageStatus {
  return ratio == null ? "NoDebt" : ratio >= 1.25 ? "Covered" : ratio >= 1 ? "Thin" : "Shortfall";
}

export function healthLevel(hf: number): RiskLevel {
  return hf < HEALTH_CRITICAL ? "Critical" : hf < HEALTH_WARN ? "Warn" : "Ok";
}

export function ltvLevel(ltv: number, liquidation: number): RiskLevel {
  return ltv >= liquidation * (1 - LTV_CRITICAL_SHARE)
    ? "Critical"
    : ltv >= liquidation * (1 - LTV_WARN_SHARE)
      ? "Warn"
      : "Ok";
}

export const ltvDrop = (ltv: number, liquidation: number) =>
  ltv <= 0 ? 100 : Math.max(0, (1 - ltv / liquidation) * 100);

export const hfDrop = (hf: number) => (hf <= 1 ? 0 : (1 - 1 / hf) * 100);

/** How much to repay, or how much collateral to add, to reach a target. Null when it cannot be worked out. */
export interface Fix {
  repayUsd: number;
  addCollateralUsd: number;
}

/** Health factor = collateral * threshold / debt; the threshold follows from today's figures. */
export function fixForHealthFactor(
  collateralUsd: number,
  debtUsd: number,
  healthFactor: number,
  target: number,
): Fix | null {
  if (collateralUsd <= 0 || debtUsd <= 0 || healthFactor <= 0 || target <= 0) return null;
  if (healthFactor >= target) return { repayUsd: 0, addCollateralUsd: 0 };
  const threshold = (healthFactor * debtUsd) / collateralUsd;
  return {
    repayUsd: debtUsd - (collateralUsd * threshold) / target,
    addCollateralUsd: (target * debtUsd) / threshold - collateralUsd,
  };
}

export function fixForLtv(collateralUsd: number, debtUsd: number, targetPercent: number): Fix | null {
  if (collateralUsd <= 0 || debtUsd <= 0 || targetPercent <= 0) return null;
  const target = targetPercent / 100;
  if (debtUsd / collateralUsd <= target) return { repayUsd: 0, addCollateralUsd: 0 };
  return { repayUsd: debtUsd - target * collateralUsd, addCollateralUsd: debtUsd / target - collateralUsd };
}

/** What the loan's own risk figure says, as one line for badges. */
export function riskText(loan: Pick<LoanDto, "risk">): string | null {
  const r = loan.risk;
  if (r.kind === "None" || r.value == null) return null;
  return r.kind === "HealthFactor" ? `HF ${r.value.toFixed(2)}` : `LTV ${r.value.toFixed(0)}%`;
}

/** The change that moved collateral ÷ debt most, as a name and a signed dollar effect on safety. */
export function biggestDriver(c: ChangesDto): { name: string; usd: number } | null {
  const items = [
    { name: "price moves in your collateral", usd: c.priceMovesUsd },
    { name: "collateral you added or removed", usd: c.depositsUsd },
    { name: "supply yield", usd: c.supplyYieldUsd },
    { name: "interest on your debt", usd: -c.interestUsd },
    { name: "borrowing or repaying", usd: -c.borrowedUsd },
    { name: "price moves in your debt asset", usd: -c.debtPriceMovesUsd },
  ];
  items.sort((a, b) => Math.abs(b.usd) - Math.abs(a.usd));
  return items[0] && Math.abs(items[0].usd) >= 0.5 ? items[0] : null;
}

/** What-if: scale borrow rates and LP income; a collateral price move changes health factor and LTV only. */
export function whatIf(
  base: { interestPerDay: number; incomePerDay: number },
  rateFactor: number,
  incomeFactor: number,
) {
  const interest = base.interestPerDay * rateFactor;
  const income = base.incomePerDay * incomeFactor;
  return { interest, income, ratio: interest > 0 ? income / interest : null, netPerDay: income - interest };
}

export const hfAfterMove = (hf: number, move: number) => hf * (1 + move);
export const ltvAfterMove = (ltv: number, move: number) => ltv / (1 + move);

/** Sentence for the date axis, matching the Earnings charts. */
export const shortDay = (iso: string) =>
  new Date(`${iso}T00:00:00Z`).toLocaleDateString(undefined, { month: "short", day: "numeric", timeZone: "UTC" });

export const todayLocal = () => new Date().toLocaleDateString("en-CA");

/** The wallets that have at least one loan, in the order they are followed. */
export const walletsWithLoans = (owners: string[], loans: { owner: string }[]): string[] => {
  const have = new Set(loans.map((l) => l.owner));
  return owners.filter((o) => have.has(o));
};
