import type { ReactNode } from "react";
import type { PoolDto } from "../../api/types";
import { protocolLabels } from "../../lib/format";
import { type Sort, type SortKey, UNRELIABLE_VOLUME_NOTE, apr, feeLabel, hasUnreliableVolume, poolRate, usd } from "../../lib/pools";
import { TokenPair } from "./TokenPair";

const columns: { key: SortKey; label: string; align: "left" | "right" }[] = [
  { key: "symbol", label: "Asset", align: "left" },
  { key: "tvlUsd", label: "TVL", align: "right" },
  { key: "fees7dUsd", label: "7d Earnings", align: "right" },
  { key: "apr", label: "Avg APY", align: "right" },
];

/** The next row up or down that can take focus, skipping the expanded detail row between pools. */
function focusSibling(row: HTMLElement, dir: 1 | -1) {
  let el = (dir === 1 ? row.nextElementSibling : row.previousElementSibling) as HTMLElement | null;
  while (el && el.tabIndex < 0) el = (dir === 1 ? el.nextElementSibling : el.previousElementSibling) as HTMLElement | null;
  el?.focus();
}

/**
 * Sortable pool table. Selecting a row opens its detail underneath; `detail` renders that content for the expanded pool.
 * Pools you hold are marked and kept on top by the caller's sort.
 */
export function PoolTable({
  pools,
  sort,
  onSort,
  selectedId,
  expandedId,
  onToggle,
  detail,
}: {
  pools: PoolDto[];
  sort: Sort;
  onSort: (sort: Sort) => void;
  selectedId: string | null;
  expandedId: string | null;
  onToggle: (id: string) => void;
  detail: (pool: PoolDto) => ReactNode;
}) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[560px] text-sm">
        <thead>
          <tr className="border-b border-slate-800 text-xs text-slate-400">
            {columns.map((c) => {
              const active = sort.key === c.key;
              return (
                <th
                  key={c.key}
                  scope="col"
                  aria-sort={active ? (sort.dir < 0 ? "descending" : "ascending") : undefined}
                  className={`whitespace-nowrap px-3 py-2 font-medium ${c.align === "left" ? "pl-10 text-left" : "text-right"}`}
                >
                  <button
                    type="button"
                    onClick={() =>
                      onSort(
                        active
                          ? { key: c.key, dir: sort.dir === 1 ? -1 : 1 }
                          : { key: c.key, dir: c.key === "symbol" ? 1 : -1 },
                      )
                    }
                    className={active ? "font-semibold text-sky-400" : "hover:text-slate-200"}
                  >
                    {c.label}
                    {active && (sort.dir < 0 ? " ↓" : " ↑")}
                  </button>
                </th>
              );
            })}
          </tr>
        </thead>
        <tbody>
          {pools.map((p) => {
            const expanded = p.id === expandedId;
            const selected = p.id === selectedId;
            return (
              <PoolRows key={p.id} pool={p} expanded={expanded} selected={selected} onToggle={() => onToggle(p.id)}>
                {expanded && detail(p)}
              </PoolRows>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function PoolRows({
  pool: p,
  expanded,
  selected,
  onToggle,
  children,
}: {
  pool: PoolDto;
  expanded: boolean;
  selected: boolean;
  onToggle: () => void;
  children: ReactNode;
}) {
  const fee = poolRate(p);
  return (
    <>
      <tr
        tabIndex={0}
        aria-selected={selected}
        aria-expanded={expanded}
        onClick={onToggle}
        onKeyDown={(e) => {
          if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            onToggle();
          } else if (e.key === "ArrowDown" || e.key === "ArrowUp") {
            e.preventDefault();
            focusSibling(e.currentTarget, e.key === "ArrowDown" ? 1 : -1);
          }
        }}
        className={`cursor-pointer border-b border-slate-800 hover:bg-slate-800/50 ${selected ? "bg-sky-400/10 shadow-[inset_3px_0_0_var(--color-sky-400)]" : ""}`}
      >
        <td className="py-2.5 pl-3 pr-3">
          <div className="flex items-center gap-2.5">
            <span aria-hidden="true" className={`w-4 text-slate-500 transition-transform ${expanded ? "rotate-90" : ""}`}>
              ›
            </span>
            <TokenPair symbol={p.symbol} />
            <div className="min-w-0">
              <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                <span className="font-semibold">{p.symbol}</span>
                <span className="rounded-full border border-slate-700 px-1.5 text-[11px] text-slate-400">{feeLabel(p.feePercent)}</span>
                {p.held && (
                  <span className="rounded-full border border-emerald-400/60 px-1.5 text-[11px] text-emerald-400">You hold</span>
                )}
              </div>
              <div className="text-xs text-slate-500">
                {protocolLabels[p.protocol]}
                {p.tickSpacing != null && ` · spacing ${p.tickSpacing}`}
                {hasUnreliableVolume(p) && (
                  <span
                    title={UNRELIABLE_VOLUME_NOTE}
                    className="ml-2 rounded-full border border-amber-400/50 px-1.5 text-[11px] text-amber-400"
                  >
                    ⚠ volume likely low
                  </span>
                )}
              </div>
            </div>
          </div>
        </td>
        <td className="num px-3 py-2.5 text-right">{usd(p.tvlUsd)}</td>
        <td className="num px-3 py-2.5 text-right">{usd(p.fees7dUsd)}</td>
        <td className="num px-3 py-2.5 text-right">
          {apr(fee)}
          {p.rewardApr ? <div className="text-xs text-emerald-400">+ {apr(p.rewardApr)} emissions</div> : null}
        </td>
      </tr>
      {expanded && (
        <tr className="border-b border-slate-800 bg-slate-950/40">
          <td colSpan={columns.length} className="p-0">
            {children}
          </td>
        </tr>
      )}
    </>
  );
}
