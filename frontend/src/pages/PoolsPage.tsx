import { useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { usePools, usePositions } from "../api/client";
import type { Chain, PoolDto } from "../api/types";
import { PoolDetail } from "../components/pools/PoolDetail";
import { PoolFilterBar } from "../components/pools/PoolFilterBar";
import { PoolTable } from "../components/pools/PoolTable";
import { RangePanel } from "../components/pools/RangePanel";
import { YourPoolCard } from "../components/pools/YourPoolCard";
import { Empty, ErrorText, Skeleton } from "../components/ui";
import { useOwner } from "../lib/owner";
import {
  DEFAULT_FILTERS,
  DEFAULT_SORT,
  type PoolFilters,
  type Sort,
  emissionsByPool,
  matchesFilters,
  poolKey,
  realisedByPool,
  sortPools,
} from "../lib/pools";
import { usePersistentState } from "../lib/usePersistentState";

const PAGE_SIZE = 15;
const DEFAULT_MIN_TVL = 5_000_000;

/**
 * Pool explorer: chips and a sortable table on the left, where a row opens its 30-day trends underneath, and on the
 * right the range panel for the selected pool. The selected pool lives in `?pool=` so it can be linked to.
 */
export function PoolsPage() {
  const { owner } = useOwner();
  const [minTvl, setMinTvl] = usePersistentState<number>("lpdash.pools.minTvl.v2", DEFAULT_MIN_TVL);
  const [chain, setChain] = usePersistentState<Chain>("lpdash.pools.chain", "Base");
  const { data, error, isLoading } = usePools(owner, minTvl, chain);
  const [params, setParams] = useSearchParams();
  const positions = usePositions(owner, "Open");
  const realised = useMemo(() => realisedByPool(positions.data), [positions.data]);
  const emissions = useMemo(() => emissionsByPool(positions.data), [positions.data]);

  const [filters, setFilters] = useState<PoolFilters>(DEFAULT_FILTERS);
  const [sort, setSort] = useState<Sort>(DEFAULT_SORT);
  const [show, setShow] = useState(PAGE_SIZE);
  const [sheetOpen, setSheetOpen] = useState(true);
  const [expanded, setExpanded] = useState<string | null>(() => params.get("pool"));
  const [carried, setCarried] = useState<{ id: string; width: number; pool?: PoolDto } | null>(null);

  const pools = data?.pools;
  const rows = useMemo(() => sortPools((pools ?? []).filter((p) => matchesFilters(p, filters)), sort), [pools, filters, sort]);

  const patchFilters = (patch: Partial<PoolFilters>) => {
    setFilters((f) => ({ ...f, ...patch }));
    setShow(PAGE_SIZE);
  };
  const reset = () => {
    setFilters(DEFAULT_FILTERS);
    setSort(DEFAULT_SORT);
    setChain("Base");
    setMinTvl(DEFAULT_MIN_TVL);
    setShow(PAGE_SIZE);
  };
  const showAll = () => {
    setFilters(DEFAULT_FILTERS);
    setMinTvl(0);
    setShow(Number.MAX_SAFE_INTEGER);
  };

  if (error) return <ErrorText error={error} />;

  const yours = (pools ?? []).filter((p) => p.held);
  const selectedParam = params.get("pool");
  const selected =
    pools?.find((p) => p.id === selectedParam) ??
    (carried?.pool && carried.id === selectedParam ? carried.pool : null) ??
    rows[0] ??
    null;
  const select = (id: string, width?: number, pool?: PoolDto) => {
    // Picking an alternative from the range panel keeps the range being compared; picking a row starts from that pool's own.
    // The alternative may be under the table's TVL filter, so its stub is kept to open it anyway.
    setCarried(width == null ? null : { id, width, pool });
    setParams({ pool: id }, { replace: true });
    setSheetOpen(true);
    setExpanded(id);
  };
  const toggle = (id: string) => (expanded === id ? setExpanded(null) : select(id));

  const selectedIndex = rows.findIndex((p) => p.id === selected?.id);
  const shown = rows.slice(0, Math.max(show, selectedIndex + 1));

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Pools</h1>
        <p className="text-sm text-slate-400">
          Browse {chain} pools by earnings, APR and TVL, open one for its 30-day trends, then test a price range to see what a
          deposit could roughly earn. Data from DefiLlama, refreshed every 15 minutes
          {data?.fetchedAt && <> (last fetched {new Date(data.fetchedAt).toLocaleTimeString()})</>}.
        </p>
      </div>

      {yours.length > 0 && (
        <section aria-labelledby="your-pools">
          <h2 id="your-pools" className="mb-2 text-sm font-medium text-slate-300">
            Your pools{" "}
            <span className="font-normal text-slate-500">
              · what the pool paid, what the model says your range should earn, and what your position has earned
            </span>
          </h2>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {yours.map((p) => (
              <YourPoolCard
                key={p.id}
                pool={p}
                realisedApr={realised.get(poolKey(p)) ?? null}
                active={p.id === selected?.id}
                onSelect={() => select(p.id)}
              />
            ))}
          </div>
        </section>
      )}

      <div className="grid items-start gap-4 lg:grid-cols-[minmax(0,1.5fr)_minmax(340px,1fr)] xl:grid-cols-[minmax(0,1fr)_minmax(420px,500px)]">
        <section aria-label="Pools" className="min-w-0 rounded-lg border border-slate-800 bg-slate-900">
          <PoolFilterBar
            filters={filters}
            onFilters={patchFilters}
            chain={chain}
            onChain={(c) => {
              setChain(c);
              setShow(PAGE_SIZE);
            }}
            minTvl={minTvl}
            onMinTvl={(v) => {
              setMinTvl(v);
              setShow(PAGE_SIZE);
            }}
            sort={sort}
            onSort={setSort}
            shown={rows.length}
            total={data?.total ?? 0}
            onReset={reset}
            onShowAll={showAll}
          />

          {isLoading || !data ? (
            <div className="space-y-2 p-4">
              {Array.from({ length: 6 }, (_, i) => (
                <Skeleton key={i} className="h-12 w-full" />
              ))}
            </div>
          ) : rows.length === 0 ? (
            <Empty>No pool matches these filters. Reset them, or show all pools.</Empty>
          ) : (
            <>
              <PoolTable
                pools={shown}
                sort={sort}
                onSort={setSort}
                selectedId={selected?.id ?? null}
                expandedId={expanded}
                onToggle={toggle}
                detail={(p) => <PoolDetail pool={p} />}
              />
              <div className="flex justify-center py-2.5 text-xs text-slate-500">
                {rows.length > shown.length ? (
                  <button
                    type="button"
                    onClick={() => setShow(shown.length + PAGE_SIZE)}
                    className="rounded-full border border-slate-700 px-3 py-1 text-slate-300 hover:bg-slate-800"
                  >
                    Show {Math.min(PAGE_SIZE, rows.length - shown.length)} more ({rows.length - shown.length} left)
                  </button>
                ) : (
                  "End of list"
                )}
              </div>
            </>
          )}
        </section>

        <aside
          aria-label="Range"
          className={`lg:sticky lg:top-20 lg:max-h-[calc(100vh-6rem)] lg:overflow-auto max-lg:fixed max-lg:inset-x-0 max-lg:bottom-0 max-lg:z-30 max-lg:max-h-[55vh] max-lg:overflow-auto max-lg:rounded-t-xl max-lg:shadow-[0_-8px_24px_rgba(0,0,0,.45)] ${selected && sheetOpen ? "" : "max-lg:hidden"}`}
        >
          {selected && pools ? (
            <RangePanel
              key={selected.id}
              pool={selected}
              initialWidth={carried?.id === selected.id ? carried.width : undefined}
              myEmissions={emissions.get(poolKey(selected)) ?? null}
              knownFees={Object.fromEntries(pools.filter((p) => p.held && p.feePercent != null).map((p) => [p.id, p.feePercent as number]))}
              onClose={() => setSheetOpen(false)}
              onSelectPool={select}
            />
          ) : (
            <Empty>Select a pool to estimate the fee APR of a price range.</Empty>
          )}
        </aside>
      </div>
    </div>
  );
}
