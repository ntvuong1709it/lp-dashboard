import { useRecheckPool, usePoolRisk } from "../../api/risk";
import { RiskBadge } from "../risk/RiskBadge";
import type { PoolDto, PoolRiskDto } from "../../api/types";
import { formatAgo, formatDate } from "../../lib/format";
import { verdictDot, verdictLabel, verdictText } from "../../lib/risk";
import { useNow } from "../../lib/useNow";
import { FindingList, RecheckButton, RiskReport, SourceList } from "../risk/RiskReport";
import { ErrorText, Skeleton } from "../ui";

/**
 * "Risk check", above "Enter this range": opening a pool runs the lazy check (a recent one is served from the cache). It is
 * shown while it runs as a skeleton and never blocks the planner below; a Red verdict only adds a banner there.
 */
export function PoolRiskSection({ pool }: { pool: PoolDto }) {
  const query = usePoolRisk(pool.id);
  const recheck = useRecheckPool(pool.id);
  const now = useNow(30_000);
  const report: PoolRiskDto | undefined = recheck.data ?? query.data;

  return (
    <section className="rounded-lg border border-slate-800 bg-slate-950" aria-label="Risk check" aria-busy={query.isFetching && !report}>
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-800 px-4 py-2.5">
        <h3 className="text-sm font-medium text-slate-300">Risk check</h3>
        {report ? (
          <span className={`inline-flex items-center gap-1.5 text-sm font-semibold ${verdictText[report.level]}`}>
            <span aria-hidden="true" className={`inline-block h-2.5 w-2.5 rounded-full ${verdictDot[report.level]}`} />
            {verdictLabel(report.level, report.checksRun, report.checksTotal)}
          </span>
        ) : null}
      </div>

      <div className="space-y-3 p-4">
        {!report && query.isFetching && (
          <div className="space-y-2" aria-label="Checking this pool and its tokens">
            <Skeleton className="h-4 w-2/3" />
            <Skeleton className="h-10 w-full" />
            <Skeleton className="h-10 w-full" />
            <p className="text-xs text-slate-500">Checking the pool and both tokens. A first check takes a few seconds.</p>
          </div>
        )}

        {!report && query.error && (
          <div className="space-y-1">
            <ErrorText error={query.error} />
            <button type="button" onClick={() => void query.refetch()} className="text-xs text-sky-400 hover:underline">
              Try again
            </button>
          </div>
        )}

        {report && (
          <>
            <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-slate-500">
              <span title={formatDate(report.checkedAt)}>
                {report.token0.symbol ?? "token0"} + {report.token1.symbol ?? "token1"}, checked {formatAgo(report.checkedAt, now)}
              </span>
              <RecheckButton
                recheckAvailableAt={report.recheckAvailableAt}
                pending={recheck.isPending}
                onClick={() => recheck.mutate()}
                error={recheck.error}
              />
            </div>

            {report.level === "Partial" && (
              <p className="rounded-md border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-xs text-amber-300">
                {report.checksRun} of {report.checksTotal} checks ran, so this cannot be called green.
              </p>
            )}

            <div>
              <h4 className="mb-1 text-xs font-semibold uppercase tracking-wide text-slate-400">The pool</h4>
              <FindingList findings={report.findings.filter((f) => f.code !== "SOURCE_UNAVAILABLE")} />
              <div className="mt-2">
                <SourceList sources={report.sources} />
              </div>
            </div>

            {[report.token0, report.token1].map((t) => (
              <details key={t.address} className="group rounded-md border border-slate-800">
                <summary className="flex cursor-pointer list-none items-center justify-between gap-2 px-3 py-2 text-sm">
                  <span className="font-medium">{t.symbol ?? "Token"}</span>
                  <span className={`inline-flex items-center gap-1.5 text-xs font-semibold ${verdictText[t.level]}`}>
                    <span aria-hidden="true" className={`inline-block h-2 w-2 rounded-full ${verdictDot[t.level]}`} />
                    {verdictLabel(t.level, t.checksRun, t.checksTotal)}
                    <span aria-hidden="true" className="text-slate-500 transition-transform group-open:rotate-90">
                      ›
                    </span>
                  </span>
                </summary>
                <div className="border-t border-slate-800 p-3">
                  <RiskReport report={t} compact />
                </div>
              </details>
            ))}

            <p className="text-xs text-slate-500">No flags is not a guarantee of safety. The check informs; it never blocks the planner below.</p>
          </>
        )}
      </div>
    </section>
  );
}

/** Shown on the Enter planner only, when the pool's verdict is Red. Shares the Risk check section's query, so it costs nothing. */
export function RedRiskBanner({ poolId }: { poolId: string }) {
  const { data } = usePoolRisk(poolId);
  if (data?.level !== "Red") return null;
  return (
    <div role="alert" className="rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-sm text-rose-300">
      This pool's risk check is <b>Red</b>. Read it above before putting money in. The planner still works: the app never blocks it.
    </div>
  );
}

export type PoolTab = "estimate" | "enter" | "risk";

const TABS: { id: PoolTab; label: string }[] = [
  { id: "estimate", label: "Estimate" },
  { id: "enter", label: "Enter" },
  { id: "risk", label: "Risk" },
];

/** The pool's verdict in the panel header, so it stays visible whichever tab is open. Shares the Risk tab's query. */
export function PoolRiskBadge({ pool, onClick }: { pool: PoolDto; onClick: () => void }) {
  const { data, isFetching, error } = usePoolRisk(pool.id);
  const recheck = useRecheckPool(pool.id);
  const report = recheck.data ?? data;
  return (
    <button
      type="button"
      onClick={onClick}
      title="Open the risk check"
      className="rounded-md border border-slate-700 bg-slate-950 px-2 py-0.5 hover:bg-slate-800"
    >
      {report ? (
        <RiskBadge level={report.level} summary={{ level: report.level, checksRun: report.checksRun, checksTotal: report.checksTotal, checkedAt: report.checkedAt, top: [] }} />
      ) : error ? (
        <span className="text-xs text-slate-500">Risk unavailable</span>
      ) : (
        <span className="text-xs text-slate-500">{isFetching ? "Checking risk…" : "Risk"}</span>
      )}
    </button>
  );
}

/** Estimate / Enter / Risk. The Risk tab carries the verdict dot and word, so it is never hidden behind the tab. */
export function RiskTabs({ tab, onTab, pool }: { tab: PoolTab; onTab: (tab: PoolTab) => void; pool: PoolDto }) {
  const { data } = usePoolRisk(pool.id);
  const recheck = useRecheckPool(pool.id);
  const report = recheck.data ?? data;
  const move = (e: React.KeyboardEvent, i: number) => {
    const next = e.key === "ArrowRight" ? i + 1 : e.key === "ArrowLeft" ? i - 1 : null;
    if (next == null) return;
    e.preventDefault();
    const target = TABS[(next + TABS.length) % TABS.length].id;
    onTab(target);
    document.getElementById(`pool-tab-${target}-tab`)?.focus();
  };
  return (
    <div role="tablist" aria-label="Pool panel sections" className="flex gap-1 border-b border-slate-800">
      {TABS.map((t, i) => {
        const active = t.id === tab;
        return (
          <button
            key={t.id}
            id={`pool-tab-${t.id}-tab`}
            role="tab"
            type="button"
            aria-selected={active}
            aria-controls={`pool-tab-${t.id}`}
            tabIndex={active ? 0 : -1}
            onClick={() => onTab(t.id)}
            onKeyDown={(e) => move(e, i)}
            className={`-mb-px flex items-center gap-2 border-b-2 px-3 py-2 text-sm font-medium focus-visible:outline-2 focus-visible:outline-sky-400 ${active ? "border-sky-400 text-slate-50" : "border-transparent text-slate-400 hover:text-slate-200"}`}
          >
            {t.label}
            {t.id === "risk" && report && (
              <span className={`inline-flex items-center gap-1 text-xs ${verdictText[report.level]}`}>
                <span aria-hidden="true" className={`inline-block h-2 w-2 rounded-full ${verdictDot[report.level]}`} />
                {verdictLabel(report.level, report.checksRun, report.checksTotal)}
              </span>
            )}
          </button>
        );
      })}
    </div>
  );
}
