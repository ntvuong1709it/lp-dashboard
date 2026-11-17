import { Link } from "react-router-dom";
import type { MarketLoanStressDto, MarketStressRowDto, MarketStressStatus } from "../../api/types";
import { formatUsd } from "../../lib/format";
import { STRESS_CLASS, STRESS_HEADLINE, STRESS_LABEL, stressValue } from "../../lib/market";
import { useHideBalances } from "../../lib/privacy";
import { Chip } from "../borrow/bits";

const BAR_ZONES = "linear-gradient(to right, var(--up-fill) var(--ok), var(--warn-fill) var(--ok) var(--margin), var(--down-fill) var(--margin))";

/** How far the loan could go, on a bar from 0 to 100% loan-to-value, with where it is now and where it lands after the fall. */
function LtvBar({ stress, row }: { stress: MarketLoanStressDto; row: MarketStressRowDto }) {
  const liq = stress.liquidationLtvPercent ?? 100;
  const call = stress.marginCallLtvPercent ?? liq;
  const clamp = (v: number) => Math.max(0, Math.min(100, v));
  const now = clamp(stress.value);
  const after = clamp(row.value);
  return (
    <div className="mb-4 mt-7">
      <div
        className="relative mb-8 h-2.5 rounded-full"
        style={{ background: BAR_ZONES, ["--ok" as string]: `${call}%`, ["--margin" as string]: `${liq}%` }}
        role="img"
        aria-label={`Loan to value ${stress.value.toFixed(0)} percent now, ${row.value.toFixed(0)} percent after the fall. Margin call at ${call} percent, liquidation at ${liq} percent.`}
      >
        <Marker at={now} label={`now ${stress.value.toFixed(0)}%`} above />
        <Marker at={after} label={`${row.value.toFixed(0)}%${row.value > 100 ? "+" : ""}`} dashed />
      </div>
      <p className="text-xs text-faint">
        <i className="mr-1 inline-block h-2 w-2 rounded-sm bg-warn-fill" />
        {stress.marginCallLtvPercent != null ? `Margin call at ${stress.marginCallLtvPercent}%` : "Margin call"}
        <i className="ml-3 mr-1 inline-block h-2 w-2 rounded-sm bg-down-fill" />
        Liquidation at {liq}%
      </p>
    </div>
  );
}

function Marker({ at, label, above, dashed }: { at: number; label: string; above?: boolean; dashed?: boolean }) {
  return (
    <span
      className={`absolute -top-1.5 h-5.5 w-0 border-l-2 ${dashed ? "border-dashed" : ""} border-fg`}
      style={{ left: `${at}%` }}
    >
      <span
        className={`num absolute left-1/2 -translate-x-1/2 whitespace-nowrap text-[11px] ${above ? "-top-4 text-fg-2" : "top-6.5 text-fg"}`}
      >
        {label}
      </span>
    </span>
  );
}

/** What a fall does to the loan with the worst risk: a plain verdict, where the loan lands and what to add. */
export function LoanFall({ stress, fallPercent }: { stress: MarketLoanStressDto; fallPercent: number }) {
  useHideBalances(); // re-render when the toggle changes; formatters read the flag
  const row = stress.rows.find((r) => r.fallPercent === fallPercent) ?? stress.rows[0];
  if (!row) return null;
  const ltv = stress.kind === "Ltv";
  const status: MarketStressStatus = row.status;
  const addLabel = ltv ? `To stay under the ${stress.marginCallLtvPercent ?? stress.liquidationLtvPercent}% margin call` : "To keep health factor at 1.2";

  return (
    <div>
      <div className="mb-2 text-xs font-medium text-muted">Your loan</div>
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-display text-lg font-semibold">{STRESS_HEADLINE[status]}</span>
        <Chip className={STRESS_CLASS[status]}>{STRESS_LABEL[status]}</Chip>
      </div>
      {ltv ? (
        <LtvBar stress={stress} row={row} />
      ) : (
        <p className="num my-3 text-sm text-fg-2">
          Health factor {stress.value.toFixed(2)} → {row.value.toFixed(2)}
        </p>
      )}
      <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-sm">
        <dt className="text-muted">Debt</dt>
        <dd className="num text-right">{formatUsd(stress.debtUsd)}</dd>
        <dt className="text-muted">Collateral after</dt>
        <dd className="num text-right">{formatUsd(stress.collateralUsd * (1 - fallPercent / 100))}</dd>
        <dt className="text-muted">{addLabel}</dt>
        <dd className="num text-right">{row.addCollateralUsd > 0 ? `add ${formatUsd(row.addCollateralUsd)}` : "nothing to add"}</dd>
      </dl>
    </div>
  );
}

/** Shown in place of the loan block when there is no loan to stress. */
export function NoLoan() {
  return (
    <div>
      <div className="mb-2 text-xs font-medium text-muted">Your loan</div>
      <p className="text-sm text-muted">
        No loan to stress.{" "}
        <Link to="/borrow" className="text-accent hover:underline">
          Set up loans on Borrow
        </Link>{" "}
        to see what a fall would cost.
      </p>
    </div>
  );
}

/** Used when the loan is a health-factor loan, which has no LTV bar. */
export const loanValueText = (stress: MarketLoanStressDto) =>
  stress.kind === "Ltv" ? `LTV ${stressValue("Ltv", stress.value)}` : `health factor ${stressValue("HealthFactor", stress.value)}`;
