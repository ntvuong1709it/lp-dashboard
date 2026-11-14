import { Link } from "react-router-dom";
import { useBorrowSummary } from "../../api/client";
import { DAY_OPTIONS, hfDrop, ltvDrop } from "../../lib/borrow";
import { formatUsd } from "../../lib/format";
import { useOwner } from "../../lib/owner";
import { usePersistentState } from "../../lib/usePersistentState";
import { Skeleton, Segmented } from "../ui";
import { Delta, LevelChip, StatusChip } from "./bits";

/** The Earnings page's answer to "do my LP earnings pay the interest?". Hidden while there is nothing to show. */
export function CoverageCard() {
  const { owner } = useOwner();
  const [days, setDays] = usePersistentState<number>("lpdash.borrow.days", 30);
  const summary = useBorrowSummary(owner, days);

  if (summary.isLoading) return <Skeleton className="h-28 w-full rounded-lg" />;
  const s = summary.data;
  if (summary.error || !s) return null;

  if (!s.hasLoans) {
    return (
      <section className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-dashed border-line-strong bg-panel p-4">
        <p className="text-sm text-muted">
          Borrowing against your LP tokens? Add a loan to see whether LP income covers the interest.
        </p>
        <Link to="/borrow" className="rounded-md bg-accent-solid px-3 py-1.5 text-sm font-medium text-white hover:bg-accent-fill">
          Set up loans
        </Link>
      </section>
    );
  }

  const c = s.coverage;
  const perDay = s.runRate.interestPerDayUsd;
  const max = Math.max(c.lpIncomeUsd, c.interestUsd, 0.01);
  const w = (v: number) => `${Math.max(0, (v / max) * 100)}%`;
  const ratioChange =
    s.trend.ratioNow != null && s.trend.ratioStart != null ? s.trend.ratioNow - s.trend.ratioStart : null;
  const risk = s.worstRisk;
  const drop =
    risk?.kind === "Ltv" && risk.value != null && risk.liquidationLtvPercent != null
      ? ltvDrop(risk.value, risk.liquidationLtvPercent)
      : risk?.kind === "HealthFactor" && risk.value != null
        ? hfDrop(risk.value)
        : null;

  return (
    <section className="rounded-lg border border-line bg-panel p-4" aria-label="Interest coverage">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-sm font-medium text-fg-2">Interest coverage</h2>
        <Segmented label="Window" options={DAY_OPTIONS} value={days} onChange={setDays} />
      </div>
      <div className="grid gap-5 md:grid-cols-[1.1fr_1.4fr_1fr] md:items-center">
        <div>
          <div className="text-xs uppercase tracking-[.05em] text-muted">Last {days} days</div>
          <div className="mt-1 flex flex-wrap items-center gap-3">
            <span className="text-4xl font-medium tracking-tight num">
              {c.ratio == null ? "—" : `${c.ratio.toFixed(2)}×`}
            </span>
            <StatusChip status={c.status} />
          </div>
          <div className="mt-1.5 text-xs text-muted">
            <span className={`num ${c.netCarryUsd >= 0 ? "text-up" : "text-down"}`}>
              {formatUsd(c.netCarryUsd, true)}
            </span>{" "}
            net carry after gas
          </div>
          <div className="mt-0.5 text-xs text-muted">
            Interest costs <span className="num">{formatUsd(perDay)}</span> a day,{" "}
            <span className="num">{formatUsd((perDay * 365) / 12)}</span> a month
          </div>
        </div>

        <div className="space-y-2.5 text-xs text-muted">
          <div className="grid grid-cols-[4.5rem_1fr_5rem] items-center gap-2">
            <span>LP income</span>
            <span className="flex h-3 overflow-hidden rounded bg-raised">
              <i style={{ width: w(c.lpFeesUsd), background: "var(--chart-a)" }} />
              <i style={{ width: w(c.lpRewardsUsd), background: "var(--chart-d)" }} />
              <i style={{ width: w(c.supplyYieldUsd), background: "var(--chart-b)" }} />
            </span>
            <span className="text-right text-fg num">{formatUsd(c.lpIncomeUsd)}</span>
          </div>
          <div className="grid grid-cols-[4.5rem_1fr_5rem] items-center gap-2">
            <span>Interest</span>
            <span className="flex h-3 overflow-hidden rounded bg-raised">
              <i className="bg-down-fill" style={{ width: w(c.interestUsd) }} />
            </span>
            <span className="text-right text-fg num">{formatUsd(c.interestUsd)}</span>
          </div>
        </div>

        <div className="flex flex-col items-start gap-1.5 text-xs text-muted">
          {risk && risk.kind !== "None" && (
            <>
              <LevelChip level={risk.level} />
              <span>
                {s.worstRiskLabel}: {risk.kind === "Ltv" ? `LTV ${risk.value?.toFixed(0)}%` : `health factor ${risk.value?.toFixed(2)}`}
                {drop != null && `, ${drop.toFixed(0)}% price drop to liquidation`}
              </span>
            </>
          )}
          {s.trend.ratioNow != null && (
            <span>
              Collateral ÷ debt <span className="num text-fg">{s.trend.ratioNow.toFixed(2)}×</span>{" "}
              {ratioChange != null && (
                <Delta value={ratioChange} text={`${Math.abs(ratioChange).toFixed(2)} in ${days}d`} goodUp />
              )}
            </span>
          )}
          <Link to="/borrow" className="text-accent hover:underline">
            Open Borrow
          </Link>
        </div>
      </div>
    </section>
  );
}
