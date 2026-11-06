import type { ReactNode } from "react";
import type { Chain, Protocol } from "../../api/types";
import { protocolLabels } from "../../lib/format";
import { DEFAULT_SORT, type PoolFilters, type Sort, type SortKey, activeFilterCount, sortLabels } from "../../lib/pools";

export const MIN_TVL_OPTIONS = [
  { label: "Any TVL", value: 0 },
  { label: "TVL ≥ $100k", value: 100_000 },
  { label: "TVL ≥ $1M", value: 1_000_000 },
  { label: "TVL ≥ $5M", value: 5_000_000 },
  { label: "TVL ≥ $20M", value: 20_000_000 },
];
const MIN_APR_OPTIONS = [0, 5, 10, 25, 50, 100];
const MAX_FEE_OPTIONS = [0.05, 0.3, 1, 5];
const CHAINS: Chain[] = ["Base", "Ethereum"];
const PROTOCOLS: Protocol[] = ["UniswapV3", "AerodromeSlipstream"];

const chip =
  "rounded-md border px-3 py-1.5 text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:outline-sky-400";
const chipIdle = "border-slate-700 bg-slate-950 text-slate-300 hover:bg-slate-800";
const chipOn = "border-sky-400 bg-sky-400/10 text-sky-400";

function Toggle({ on, onClick, children }: { on: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button type="button" aria-pressed={on} onClick={onClick} className={`${chip} ${on ? chipOn : chipIdle}`}>
      {children}
    </button>
  );
}

function Select({
  label,
  value,
  onChange,
  active,
  children,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  active: boolean;
  children: ReactNode;
}) {
  return (
    <select
      aria-label={label}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      className={`${chip} appearance-auto ${active ? chipOn : chipIdle}`}
    >
      {children}
    </select>
  );
}

/**
 * Chips over the pool table: chain, protocol, asset-type toggles, APR and fee limits, a TVL floor and the sort.
 * Chain and the TVL floor are asked of the server; the rest filter what it returned.
 */
export function PoolFilterBar({
  filters,
  onFilters,
  chain,
  onChain,
  minTvl,
  onMinTvl,
  sort,
  onSort,
  shown,
  total,
  onReset,
  onShowAll,
}: {
  filters: PoolFilters;
  onFilters: (patch: Partial<PoolFilters>) => void;
  chain: Chain;
  onChain: (chain: Chain) => void;
  minTvl: number;
  onMinTvl: (minTvl: number) => void;
  sort: Sort;
  onSort: (sort: Sort) => void;
  shown: number;
  total: number;
  onReset: () => void;
  onShowAll: () => void;
}) {
  const count = activeFilterCount(filters) + Number(chain !== "Base");

  return (
    <div className="space-y-2 border-b border-slate-800 p-3">
      <div className="flex flex-wrap items-center gap-2">
        <span className={`${chip} flex items-center gap-2 border-slate-700 bg-slate-950 text-slate-300`}>
          Filters
          {count > 0 && (
            <span className="rounded-full bg-sky-400 px-1.5 text-[11px] font-semibold text-slate-950">{count}</span>
          )}
        </span>
        <Select label="Chain" value={chain} onChange={(v) => onChain(v as Chain)} active={chain !== "Base"}>
          {CHAINS.map((c) => (
            <option key={c} value={c}>
              Chain: {c}
            </option>
          ))}
        </Select>
        <Select
          label="Protocol"
          value={filters.protocol ?? ""}
          onChange={(v) => onFilters({ protocol: (v || null) as Protocol | null })}
          active={filters.protocol != null}
        >
          <option value="">Protocol</option>
          {PROTOCOLS.map((p) => (
            <option key={p} value={p}>
              {protocolLabels[p]}
            </option>
          ))}
        </Select>
        <Toggle on={filters.stable} onClick={() => onFilters({ stable: !filters.stable })}>
          Stable pools
        </Toggle>
        <Toggle on={filters.extraRewards} onClick={() => onFilters({ extraRewards: !filters.extraRewards })}>
          Extra rewards
        </Toggle>
        <Toggle on={filters.correlated} onClick={() => onFilters({ correlated: !filters.correlated })}>
          Correlated
        </Toggle>
        <Select
          label="Minimum APR"
          value={String(filters.minApr)}
          onChange={(v) => onFilters({ minApr: Number(v) })}
          active={filters.minApr > 0}
        >
          {MIN_APR_OPTIONS.map((v) => (
            <option key={v} value={v}>
              {v === 0 ? "Min APR" : `APR ≥ ${v}%`}
            </option>
          ))}
        </Select>
        <Select
          label="Maximum fee tier"
          value={filters.maxFee == null ? "" : String(filters.maxFee)}
          onChange={(v) => onFilters({ maxFee: v === "" ? null : Number(v) })}
          active={filters.maxFee != null}
        >
          <option value="">Any fee</option>
          {MAX_FEE_OPTIONS.map((v) => (
            <option key={v} value={v}>
              Fee ≤ {v}%
            </option>
          ))}
        </Select>
        <Select label="Minimum TVL" value={String(minTvl)} onChange={(v) => onMinTvl(Number(v))} active={minTvl !== 0}>
          {MIN_TVL_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </Select>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 text-sm">
        <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
          <input
            type="search"
            aria-label="Search pools"
            value={filters.query}
            onChange={(e) => onFilters({ query: e.target.value })}
            placeholder="Search WETH, cbBTC, 0x…"
            className="w-48 rounded-md border border-slate-700 bg-slate-950 px-2.5 py-1.5 text-sm text-slate-50"
          />
          <span className="text-slate-400" aria-live="polite">
            <span className="num text-slate-50">{shown}</span> of <span className="num">{total}</span> pools
          </span>
          <button type="button" onClick={onReset} className="text-sky-400 hover:underline">
            Reset
          </button>
          <button type="button" onClick={onShowAll} className="text-sky-400 hover:underline">
            Show all
          </button>
        </div>
        <div className="flex items-center gap-2 text-slate-400">
          <label htmlFor="pool-sort">Sort:</label>
          <select
            id="pool-sort"
            value={sort.key}
            onChange={(e) => {
              const key = e.target.value as SortKey;
              onSort({ key, dir: key === "symbol" ? 1 : -1 });
            }}
            className={`${chip} ${chipIdle}`}
          >
            {(Object.keys(sortLabels) as SortKey[]).map((k) => (
              <option key={k} value={k}>
                {sortLabels[k]}
              </option>
            ))}
          </select>
          {(sort.key !== DEFAULT_SORT.key || sort.dir !== DEFAULT_SORT.dir) && (
            <button type="button" onClick={() => onSort(DEFAULT_SORT)} className="text-xs text-slate-500 hover:text-slate-300">
              default
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
