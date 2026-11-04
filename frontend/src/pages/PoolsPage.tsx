import { useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { usePoolEstimate, usePools, usePositions } from "../api/client";
import type { PoolDto, PositionDto, Protocol } from "../api/types";
import {
  Empty,
  ErrorText,
  PageSkeleton,
  Panel,
  Segmented,
  StatCard,
} from "../components/ui";
import { protocolLabels } from "../lib/format";
import { useOwner } from "../lib/owner";
import { usePersistentState } from "../lib/usePersistentState";

type SortKey = "symbol" | "tvlUsd" | "volume1dUsd" | "volumeToTvl" | "feeApr1d" | "feeApr7d";

const PAGE_SIZE = 15;
const DEFAULT_CAPITAL = 5000;

const compactUsd = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
  notation: "compact",
  maximumFractionDigits: 1,
});
const fullUsd = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
  maximumFractionDigits: 0,
});

const usd = (v: number | null | undefined) => (v == null ? "—" : compactUsd.format(v));
const apr = (v: number | null | undefined) =>
  v == null ? "—" : `${v >= 1000 ? v.toFixed(0) : v.toFixed(1)}%`;
const feeLabel = (f: number | null) => (f == null ? "—" : `${Number(f.toFixed(4))}%`);
const poolRate = (p: PoolDto) => p.feeApr7d ?? p.feeApr1d;

const MIN_TVL_OPTIONS = [
  { label: "$1M", value: 1_000_000 },
  { label: "$5M", value: 5_000_000 },
  { label: "$20M", value: 20_000_000 },
];

const columns: { key: SortKey; label: string }[] = [
  { key: "symbol", label: "Pool" },
  { key: "tvlUsd", label: "TVL" },
  { key: "volume1dUsd", label: "Volume 24h" },
  { key: "volumeToTvl", label: "Volume / TVL" },
  { key: "feeApr1d", label: "Fee APR 24h" },
  { key: "feeApr7d", label: "Fee APR 7d" },
];

/** Uniswap pools are told apart by token pair and fee, Slipstream pools by token pair and tick spacing. */
function matchKey(protocol: Protocol, a: string, b: string, fee: number | null, tickSpacing: number | null) {
  const [lo, hi] = [a.toLowerCase(), b.toLowerCase()].sort();
  const tier = protocol === "AerodromeSlipstream" ? `ts${tickSpacing}` : `fee${fee}`;
  return `${protocol}|${lo}|${hi}|${tier}`;
}

function poolKey(p: PoolDto) {
  return matchKey(p.protocol, p.token0, p.token1, p.feePercent == null ? null : Math.round(p.feePercent * 10000), p.tickSpacing);
}

/** Realised fee APR of the first open position in each pool, keyed like poolKey. */
function realisedByPool(positions: PositionDto[] | undefined) {
  const map = new Map<string, number>();
  for (const p of positions ?? []) {
    if (!p.token0 || !p.token1 || p.metrics.feeAprPercent == null) continue;
    const key = matchKey(p.protocol, p.token0.address, p.token1.address, p.fee, p.tickSpacing);
    if (!map.has(key)) map.set(key, p.metrics.feeAprPercent);
  }
  return map;
}

function sortValue(p: PoolDto, key: SortKey): string | number {
  if (key === "symbol") return `${p.symbol} ${p.protocol}`;
  // Pools with no 7-day volume rank by their 24h rate instead of sinking to the bottom.
  if (key === "feeApr7d") return poolRate(p) ?? -1;
  return p[key] ?? -1;
}

export function PoolsPage() {
  const { owner } = useOwner();
  const [minTvl, setMinTvl] = usePersistentState<number>("lpdash.pools.minTvl.v2", 5_000_000);
  const { data, error, isLoading } = usePools(owner, minTvl);
  const [params, setParams] = useSearchParams();
  const positions = usePositions(owner, "Open");
  const realised = useMemo(() => realisedByPool(positions.data), [positions.data]);

  const [query, setQuery] = useState("");
  const [protocol, setProtocol] = useState<Protocol | null>(null);
  const [fee, setFee] = useState<string>("");
  const [sort, setSort] = useState<{ key: SortKey; dir: 1 | -1 }>({ key: "feeApr7d", dir: -1 });
  const [show, setShow] = useState(PAGE_SIZE);
  const [sheetOpen, setSheetOpen] = useState(true);

  const feeTiers = useMemo(
    () => [...new Set((data?.pools ?? []).map((p) => p.feePercent).filter((f): f is number => f != null))].sort((a, b) => a - b),
    [data],
  );

  const rows = useMemo(() => {
    const q = query.trim().toLowerCase();
    return (data?.pools ?? [])
      .filter(
        (p) =>
          (!q || `${p.symbol} ${p.token0} ${p.token1}`.toLowerCase().includes(q)) &&
          (!protocol || p.protocol === protocol) &&
          (!fee || String(p.feePercent) === fee),
      )
      .sort((a, b) => {
        // Pools you hold stay on top whatever the sort, so they are easy to find.
        if (a.held !== b.held) return a.held ? -1 : 1;
        const x = sortValue(a, sort.key);
        const y = sortValue(b, sort.key);
        return (x < y ? -1 : x > y ? 1 : 0) * sort.dir;
      });
  }, [data, query, protocol, fee, sort]);

  if (error) return <ErrorText error={error} />;
  if (isLoading || !data) return <PageSkeleton cards={4} />;

  const yours = data.pools.filter((p) => p.held);
  const selectedParam = params.get("pool");
  const selected = data.pools.find((p) => p.id === selectedParam) ?? rows[0] ?? null;
  const select = (id: string) => {
    setParams({ pool: id }, { replace: true });
    setSheetOpen(true);
  };

  const selectedIndex = rows.findIndex((p) => p.id === selected?.id);
  const shown = rows.slice(0, Math.max(show, selectedIndex + 1));
  const tvl = rows.reduce((s, p) => s + p.tvlUsd, 0);
  const volume = rows.reduce((s, p) => s + (p.volume1dUsd ?? 0), 0);
  const best = rows.reduce<PoolDto | null>(
    (b, p) => ((poolRate(p) ?? -1) > (b ? (poolRate(b) ?? -1) : -2) ? p : b),
    null,
  );

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Pools</h1>
        <p className="text-sm text-slate-400">
          Browse Base pools by fee APR, volume and TVL, then test a price range to see what a deposit could roughly earn.
          Data from DefiLlama, refreshed every 15 minutes
          {data.fetchedAt && <> (last fetched {new Date(data.fetchedAt).toLocaleTimeString()})</>}.
        </p>
      </div>

      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <StatCard label="Pools shown" value={rows.length} hint={`above ${usd(minTvl)} TVL`} />
        <StatCard label="Combined TVL" value={usd(tvl)} />
        <StatCard label="Volume 24h" value={usd(volume)} />
        <StatCard
          label="Highest fee APR"
          value={apr(best ? poolRate(best) : null)}
          hint={best ? `${best.symbol} ${feeLabel(best.feePercent)}` : undefined}
        />
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

      <div className="grid items-start gap-4 lg:grid-cols-[minmax(0,1.5fr)_minmax(320px,1fr)]">
        <Panel
          title="Pools"
          actions={
            <span className="text-xs text-slate-500">
              {shown.length} of {rows.length}
            </span>
          }
        >
          <div className="-mx-4 -mt-4 mb-0 flex flex-wrap items-end gap-x-4 gap-y-3 border-b border-slate-800 px-4 py-3 text-sm">
            <label className="flex flex-col gap-1 text-xs text-slate-400" htmlFor="pool-search">
              Search
              <input
                id="pool-search"
                type="search"
                value={query}
                onChange={(e) => {
                  setQuery(e.target.value);
                  setShow(PAGE_SIZE);
                }}
                placeholder="WETH, cbBTC, 0x…"
                className="w-40 rounded-md border border-slate-700 bg-slate-950 px-2.5 py-1.5 text-sm text-slate-50"
              />
            </label>
            <div className="flex flex-col gap-1 text-xs text-slate-400">
              Protocol
              <Segmented<Protocol | null>
                label="Protocol"
                value={protocol}
                onChange={(v) => {
                  setProtocol(v);
                  setShow(PAGE_SIZE);
                }}
                options={[
                  { label: "All", value: null },
                  { label: protocolLabels.UniswapV3, value: "UniswapV3" },
                  { label: protocolLabels.AerodromeSlipstream, value: "AerodromeSlipstream" },
                ]}
              />
            </div>
            <div className="flex flex-col gap-1 text-xs text-slate-400">
              Min TVL
              <Segmented<number>
                label="Minimum TVL"
                value={minTvl}
                onChange={(v) => {
                  setMinTvl(v);
                  setShow(PAGE_SIZE);
                }}
                options={MIN_TVL_OPTIONS}
              />
            </div>
            <label className="flex flex-col gap-1 text-xs text-slate-400" htmlFor="pool-fee">
              Fee tier
              <select
                id="pool-fee"
                value={fee}
                onChange={(e) => {
                  setFee(e.target.value);
                  setShow(PAGE_SIZE);
                }}
                className="rounded-md border border-slate-700 bg-slate-950 px-2.5 py-1.5 text-sm text-slate-50"
              >
                <option value="">All</option>
                {feeTiers.map((f) => (
                  <option key={f} value={String(f)}>
                    {feeLabel(f)}
                  </option>
                ))}
              </select>
            </label>
          </div>

          {rows.length === 0 ? (
            <Empty>No pool matches these filters. Lower the minimum TVL or clear the fee tier.</Empty>
          ) : (
            <>
              <div className="-mx-4 max-h-[440px] overflow-auto">
                <table className="w-full min-w-[640px] text-sm">
                  <thead>
                    <tr className="text-xs text-slate-400">
                      {columns.map((c) => {
                        const active = sort.key === c.key;
                        return (
                          <th
                            key={c.key}
                            scope="col"
                            aria-sort={active ? (sort.dir < 0 ? "descending" : "ascending") : undefined}
                            className={`sticky top-0 z-10 whitespace-nowrap bg-slate-900 px-3 py-2 font-medium shadow-[0_1px_0_var(--color-slate-800)] ${c.key === "symbol" ? "text-left" : "text-right"}`}
                          >
                            <button
                              type="button"
                              onClick={() =>
                                setSort(
                                  active
                                    ? { key: c.key, dir: sort.dir === 1 ? -1 : 1 }
                                    : { key: c.key, dir: c.key === "symbol" ? 1 : -1 },
                                )
                              }
                              className={active ? "font-semibold text-slate-50" : "hover:text-slate-200"}
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
                    {shown.map((p) => (
                      <PoolRow key={p.id} pool={p} selected={p.id === selected?.id} onSelect={() => select(p.id)} />
                    ))}
                  </tbody>
                </table>
              </div>
              <div className="-mx-4 -mb-4 flex justify-center border-t border-slate-800 py-2.5 text-xs text-slate-500">
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
        </Panel>

        <aside
          aria-label="Estimate"
          className={`lg:sticky lg:top-20 max-lg:fixed max-lg:inset-x-0 max-lg:bottom-0 max-lg:z-30 max-lg:max-h-[55vh] max-lg:overflow-auto max-lg:rounded-t-xl max-lg:shadow-[0_-8px_24px_rgba(0,0,0,.45)] ${selected && sheetOpen ? "" : "max-lg:hidden"}`}
        >
          {selected ? (
            <Estimator key={selected.id} pool={selected} onClose={() => setSheetOpen(false)} />
          ) : (
            <Empty>Select a pool to estimate the fee APR of a price range.</Empty>
          )}
        </aside>
      </div>
    </div>
  );
}

function YourPoolCard({
  pool: p,
  realisedApr,
  active,
  onSelect,
}: {
  pool: PoolDto;
  realisedApr: number | null;
  active: boolean;
  onSelect: () => void;
}) {
  const mine = p.myHalfWidthPercent;
  const { data } = usePoolEstimate(p.id, mine ?? 10, DEFAULT_CAPITAL, 30, p.feePercent);
  return (
    <button
      type="button"
      onClick={onSelect}
      aria-pressed={active}
      className={`flex flex-col gap-2 rounded-lg border border-l-[3px] border-slate-800 border-l-emerald-400 p-3 text-left hover:border-sky-400 ${active ? "border-sky-400 bg-sky-400/10" : "bg-slate-900"}`}
    >
      <span className="flex items-baseline justify-between gap-2 font-semibold">
        <span>
          {p.symbol}{" "}
          <span className="rounded-full border border-slate-700 px-1.5 text-[11px] font-normal text-slate-400">
            {feeLabel(p.feePercent)}
          </span>
        </span>
        <span className="text-xs font-normal text-slate-500">{protocolLabels[p.protocol]}</span>
      </span>
      <span className="grid grid-cols-3 gap-2 text-xs text-slate-400">
        <span>
          Pool rate
          <b className="num block text-sm font-medium text-slate-50">{apr(poolRate(p))}</b>
        </span>
        <span>
          Your range ±{mine?.toFixed(0)}% (model)
          <b className="num block text-sm font-medium text-slate-50">{data ? apr(data.apr) : "…"}</b>
        </span>
        <span>
          Your realised APR
          <b className="num block text-sm font-medium text-slate-50">{apr(realisedApr)}</b>
        </span>
      </span>
    </button>
  );
}

function PoolRow({ pool: p, selected, onSelect }: { pool: PoolDto; selected: boolean; onSelect: () => void }) {
  return (
    <tr
      tabIndex={0}
      aria-selected={selected}
      onClick={onSelect}
      onKeyDown={(e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          onSelect();
        } else if (e.key === "ArrowDown" || e.key === "ArrowUp") {
          e.preventDefault();
          const next =
            e.key === "ArrowDown" ? e.currentTarget.nextElementSibling : e.currentTarget.previousElementSibling;
          (next as HTMLElement | null)?.focus();
        }
      }}
      className={`cursor-pointer border-b border-slate-800 last:border-b-0 hover:bg-slate-800/50 ${selected ? "bg-sky-400/10 shadow-[inset_3px_0_0_var(--color-sky-400)]" : ""}`}
    >
      <td className="px-3 py-2.5">
        <div className="flex items-center gap-2">
          <span className="font-semibold">{p.symbol}</span>
          <span className="rounded-full border border-slate-700 px-1.5 text-[11px] text-slate-400">{feeLabel(p.feePercent)}</span>
          {p.held && (
            <span className="rounded-full border border-emerald-400/60 px-1.5 text-[11px] text-emerald-400">You hold</span>
          )}
        </div>
        <div className="text-xs text-slate-500">
          {protocolLabels[p.protocol]}
          {p.tickSpacing != null && ` · spacing ${p.tickSpacing}`}
        </div>
      </td>
      <td className="num px-3 py-2.5 text-right">{usd(p.tvlUsd)}</td>
      <td className="num px-3 py-2.5 text-right">{usd(p.volume1dUsd)}</td>
      <td className="num px-3 py-2.5 text-right">{p.volumeToTvl == null ? "—" : `${p.volumeToTvl.toFixed(2)}×`}</td>
      <td className="num px-3 py-2.5 text-right">{apr(p.feeApr1d)}</td>
      <td className="num px-3 py-2.5 text-right">
        {apr(p.feeApr7d)}
        {p.rewardApr ? <div className="text-xs text-slate-500">+ {apr(p.rewardApr)} emissions</div> : null}
      </td>
    </tr>
  );
}

function useDebounced<T>(value: T, ms = 250): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), ms);
    return () => clearTimeout(t);
  }, [value, ms]);
  return debounced;
}

function Estimator({ pool, onClose }: { pool: PoolDto; onClose: () => void }) {
  const mine = pool.myHalfWidthPercent == null ? null : Math.round(pool.myHalfWidthPercent * 10) / 10;
  const [width, setWidth] = useState(mine ?? 10);
  const [capital, setCapital] = useState(DEFAULT_CAPITAL);
  const [days, setDays] = useState(30);
  const dWidth = useDebounced(width);
  const dCapital = useDebounced(capital);
  const cap = Math.max(100, dCapital || 0);
  const { data, error, isFetching } = usePoolEstimate(pool.id, dWidth, cap, days, pool.feePercent);
  const scaleMax = data ? Math.max(data.aprHigh * 1.15, 1) : 1;
  const presets = [2, 5, 10, 25, 50];

  return (
    <Panel
      title={`${pool.symbol} ${feeLabel(pool.feePercent)}${pool.held ? " · you hold" : ""}`}
      actions={
        <span className="flex items-center gap-3">
          {isFetching && <span className="text-xs text-slate-500">Updating…</span>}
          <button
            type="button"
            onClick={onClose}
            className="rounded-md border border-slate-700 px-2 py-0.5 text-xs text-slate-400 hover:bg-slate-800 lg:hidden"
          >
            Close
          </button>
        </span>
      }
    >
      <div className="space-y-4">
        <div className="text-xs text-slate-500">
          {protocolLabels[pool.protocol]} · Base · {usd(pool.tvlUsd)} TVL
        </div>
        <label className="flex flex-col gap-1 text-xs text-slate-400" htmlFor="est-width">
          <span>
            Range around the current price: <span className="num text-sm text-slate-50">±{width}%</span>
          </span>
          <input
            id="est-width"
            type="range"
            min={1}
            max={50}
            step={0.5}
            value={width}
            onChange={(e) => setWidth(Number(e.target.value))}
            className="accent-sky-400"
          />
        </label>
        <Segmented<number>
          label="Range presets"
          value={width}
          onChange={setWidth}
          options={[
            ...presets.map((v) => ({ label: `±${v}%`, value: v })),
            ...(mine != null && !presets.includes(mine) ? [{ label: `My range ±${mine}%`, value: mine }] : []),
          ]}
        />
        <div className="flex flex-wrap items-end gap-4">
          <label className="flex flex-col gap-1 text-xs text-slate-400" htmlFor="est-capital">
            Capital (USD)
            <input
              id="est-capital"
              type="number"
              min={100}
              step={500}
              value={capital}
              onChange={(e) => setCapital(Number(e.target.value))}
              className="num w-28 rounded-md border border-slate-700 bg-slate-950 px-2.5 py-1.5 text-sm text-slate-50"
            />
          </label>
          <div className="flex flex-col gap-1 text-xs text-slate-400">
            Hold for
            <Segmented<number>
              label="Holding period"
              value={days}
              onChange={setDays}
              options={[
                { label: "7d", value: 7 },
                { label: "30d", value: 30 },
                { label: "90d", value: 90 },
              ]}
            />
          </div>
        </div>

        <div className="space-y-3 rounded-lg border border-slate-800 bg-slate-950 p-4">
          {error ? (
            <ErrorText error={error} />
          ) : !data || data.poolId !== pool.id ? (
            <p className="text-sm text-slate-500">Estimating…</p>
          ) : (
            <>
              <div>
                <div className="text-xs text-slate-400">Estimated fee APR ({data.basis} volume)</div>
                <div className="num text-4xl font-medium text-sky-400" aria-live="polite">
                  {apr(data.apr)}
                </div>
                <div className="relative my-2 h-2 rounded bg-slate-800" aria-hidden="true">
                  <i
                    className="absolute inset-y-0 rounded bg-sky-400/40"
                    style={{ left: `${(data.aprLow / scaleMax) * 100}%`, width: `${((data.aprHigh - data.aprLow) / scaleMax) * 100}%` }}
                  />
                  <b
                    className="absolute -top-[3px] h-3.5 w-[3px] rounded bg-sky-400"
                    style={{ left: `calc(${(data.apr / scaleMax) * 100}% - 1px)` }}
                  />
                </div>
                <div className="num text-xs text-slate-500">
                  24h to 7d rates: {apr(data.aprLow)} to {apr(data.aprHigh)}
                </div>
              </div>
              <dl className="grid grid-cols-[1fr_auto] gap-x-4 gap-y-1 text-sm">
                <dt className="text-slate-400">
                  Fees on {fullUsd.format(cap)} over {days} days
                </dt>
                <dd className="num text-right">{fullUsd.format(data.feesUsd)}</dd>
                <dt className="text-slate-400">Concentration vs full range</dt>
                <dd className="num text-right">× {data.concentration.toFixed(1)}</dd>
                <dt className="text-slate-400">Expected time in range</dt>
                <dd className="num text-right">{(data.timeInRange * 100).toFixed(0)}%</dd>
                <dt className="text-slate-400">Price volatility (30d)</dt>
                <dd className="num text-right">{(data.volatility * 100).toFixed(0)}%</dd>
                <dt className="text-slate-400">Typical LP range assumed</dt>
                <dd className="num text-right">±{data.referenceHalfWidthPercent.toFixed(1)}%</dd>
                {data.rewardApr ? (
                  <>
                    <dt className="text-slate-400">Gauge emissions if staked</dt>
                    <dd className="num text-right">+ {apr(data.rewardApr)}</dd>
                  </>
                ) : null}
              </dl>
              {data.depositSharePercent > 5 && (
                <p className="text-xs text-amber-400">
                  About {data.depositSharePercent.toFixed(0)}% of the liquidity at this range: it would dilute its own
                  fees. Treat the estimate as high.
                </p>
              )}
            </>
          )}
        </div>
        <p className="text-xs text-slate-500">
          Fees only. Based on recent volume and 30 days of price movement. Impermanent loss is not subtracted and past
          volume is not a forecast.
        </p>
      </div>
    </Panel>
  );
}
