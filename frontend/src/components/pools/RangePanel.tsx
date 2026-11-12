import { type ReactNode, useEffect, useMemo, useRef, useState } from "react";
import { useEntryPlan, usePoolEstimate } from "../../api/client";
import type { EntryPlanDto, EntryPlanRequest, PoolDto } from "../../api/types";
import { MAX_LOWER, MAX_UPPER, MIN_LOWER, MIN_UPPER, equivalentHalfWidth, qp } from "../../lib/entry";
import { formatAgo, protocolLabels } from "../../lib/format";
import {
  DEFAULT_CAPITAL,
  type Emissions,
  UNRELIABLE_VOLUME_NOTE,
  apr,
  feeLabel,
  fullUsd,
  hasUnreliableVolume,
  tokenSymbols,
  usd,
  usdCents,
} from "../../lib/pools";
import {
  type PriceView,
  type PriceViewKind,
  availableViews,
  fmtPct,
  fromViewPercents,
  makePriceView,
  priceInputValue,
  toViewPercents,
  viewLimits,
} from "../../lib/priceView";
import { useDebounced } from "../../lib/useDebounced";
import { usePersistentState } from "../../lib/usePersistentState";
import { ErrorText, Segmented } from "../ui";
import { type EntryMode, EntryPanel } from "./EntryPanel";
import { PoolRiskSection, PoolRiskBadge, RiskTabs, type PoolTab } from "./PoolRiskSection";
import { StarButton } from "./StarButton";
import { TokenPair } from "./TokenPair";

const MIN_WIDTH = 1;
const MAX_WIDTH = 50;
const STEP = 0.5;
const PRESETS = [
  { label: "Tight", width: 2 },
  { label: "Standard", width: 10 },
  { label: "Wide", width: 25 },
];
const near = (a: number, b: number) => Math.abs(a - b) < 0.05;
/** Pool-percent bounds are kept to five decimals so a typed price comes back as the same price. */
const round5 = (v: number) => Math.round(v * 1e5) / 1e5;
const clampPool = (v: number, min: number, max: number) => Math.min(max, Math.max(min, round5(v)));
const round1 = (v: number) => Math.round(v * 10) / 10;
const clampWidth = (w: number) => Math.min(MAX_WIDTH, Math.max(MIN_WIDTH, Math.round(w * 10) / 10));
const sliderValue = (lower: number, upper: number) => clampWidth(lower === upper ? lower : Math.round(equivalentHalfWidth(lower, upper) * 2) / 2);

const pill = "rounded-md border border-slate-700 bg-slate-950 px-2 py-0.5 text-xs text-slate-300";

/**
 * The range side of the explorer: pick a range around today's price (preset, slider, or each bound on its own) and see what a
 * deposit of that size could earn, what fees cover, how the range would have fared lately and where else the pair trades,
 * then what to hold to enter it and what hedging it would cost.
 */
export function RangePanel({
  pool,
  onClose,
  onSelectPool,
  initialWidth,
  knownFees,
  myEmissions,
}: {
  knownFees: Record<string, number>;
  myEmissions: Emissions | null;
  pool: PoolDto;
  onClose: () => void;
  onSelectPool: (id: string, width?: number, pool?: PoolDto) => void;
  initialWidth?: number;
}) {
  const [base, quote] = tokenSymbols(pool.symbol);
  const mine = pool.myHalfWidthPercent == null ? null : Math.round(pool.myHalfWidthPercent * 10) / 10;
  const start = initialWidth ?? mine ?? 10;
  // The two bounds are independent: percent below and percent above today's price. Presets and the slider set both.
  const [lower, setLower] = useState(clampPool(start, MIN_LOWER, MAX_LOWER));
  const [upper, setUpper] = useState(clampPool(start, MIN_UPPER, MAX_UPPER));
  const [capital, setCapital] = useState(DEFAULT_CAPITAL);
  const [mode, setMode] = useState<EntryMode>("budget");
  const [heldUsd, setHeldUsd] = useState(0);
  const [days, setDays] = useState(30);
  const [tab, setTab] = useState<PoolTab>("estimate");
  const [viewKind, setViewKind] = usePersistentState<PriceViewKind>("lpdash.range.priceView", "usd");
  const [inputBy, setInputBy] = usePersistentState<"percent" | "price">("lpdash.range.inputBy", "percent");
  // The APR estimate is for a symmetric range; a skewed one is asked for as the symmetric range with the same price ratio.
  const width = Math.round(equivalentHalfWidth(lower, upper) * 100) / 100;
  const symmetric = lower === upper;
  const dWidth = useDebounced(width);
  const dCapital = useDebounced(mode === "wallet" ? Math.round(heldUsd) : capital);
  const cap = Math.max(100, dCapital || 0);
  const { data, error, isFetching } = usePoolEstimate(pool.id, dWidth, cap, days, pool.feePercent, knownFees);
  const fresh = data && data.poolId === pool.id ? data : null;
  const scaleMax = data ? Math.max(data.aprHigh * 1.15, 1) : 1;

  // The pool's price and the snapped range come from the chain via the entry-plan endpoint (the same one the form below uses).
  const rangeKey = useDebounced(
    JSON.stringify({ lowerPercent: lower, upperPercent: upper, budgetUsd: Math.max(100, capital || 0), swapCostPct: 0.1, gasUsd: 0, incomeAprPct: 0, incomeBasis: "fees" }),
  );
  const rangeBody = useMemo(() => JSON.parse(rangeKey) as EntryPlanRequest, [rangeKey]);
  const rangeQuery = useEntryPlan(pool.id, rangeBody);
  const rangePlan = rangeQuery.data && rangeQuery.data.pool.poolId === pool.id ? rangeQuery.data : undefined;
  const [sym0, sym1] = rangePlan ? [rangePlan.pool.token0.symbol, rangePlan.pool.token1.symbol] : [base, quote];
  // How prices are shown: dollars per token, or one of the two ratios. Bounds are entered in the same view.
  const priced = rangePlan
    ? { sym0, sym1, poolPrice: rangePlan.pool.poolPrice, usd0: rangePlan.pool.usd0, usd1: rangePlan.pool.usd1 }
    : null;
  const views = priced ? availableViews(priced) : [];
  const kind = views.includes(viewKind) ? viewKind : views[0];
  const view = priced && kind ? makePriceView(kind, priced) : null;
  const flip = view?.flip ?? false;
  const priceMode = inputBy === "price" && view != null;
  // The bounds as the view sees them: percent below and above the displayed price.
  const seen = toViewPercents(lower, upper, flip);
  const limits = viewLimits(flip);
  const setSeen = (vl: number, vu: number, asFlipped = flip) => {
    const p = fromViewPercents(vl, vu, asFlipped);
    setLower(clampPool(p.lower, MIN_LOWER, MAX_LOWER));
    setUpper(clampPool(p.upper, MIN_UPPER, MAX_UPPER));
  };
  // A range that is the same distance either side of the price (a preset) stays that way when the view turns the pair around.
  const changeView = (k: PriceViewKind) => {
    if (priced && near(seen.lower, seen.upper)) {
      const next = makePriceView(k, priced);
      if (next.flip !== flip) setSeen(seen.lower, seen.upper, next.flip);
    }
    setViewKind(k);
  };
  // Whether the range the panel opened with was meant to be symmetric in the view, which is only known once the pool is read.
  const seeded = useRef(false);
  useEffect(() => {
    if (!view || seeded.current) return;
    seeded.current = true;
    if (view.flip && mine == null) setSeen(start, start, true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [view]);
  const setBoth = (w: number) => setSeen(w, w);
  const setSeenLower = (v: number) => setSeen(Math.min(limits.lower, Math.max(MIN_LOWER, v)), seen.upper);
  const setSeenUpper = (v: number) => setSeen(seen.lower, Math.min(limits.upper, Math.max(MIN_UPPER, v)));
  const activePreset = PRESETS.find((p) => near(p.width, seen.lower) && near(p.width, seen.upper));
  const seenSymmetric = near(seen.lower, seen.upper);
  const isMine = mine != null && near(lower, mine) && near(upper, mine);
  // Snapped bounds, as the view sees them.
  const snappedSeen = rangePlan ? toViewPercents(rangePlan.range.lowerPercent, rangePlan.range.upperPercent, flip) : null;
  const snappedPrices = rangePlan && view ? [view.fromPool(rangePlan.range.priceLower), view.fromPool(rangePlan.range.priceUpper)].sort((a, b) => a - b) : null;
  // Re-render now and then so "read 2 min ago" stays true.
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 30_000);
    return () => clearInterval(t);
  }, []);

  return (
    <section className="rounded-lg border border-slate-800 bg-slate-900" aria-label={`Range for ${pool.symbol}`}>
      <div className="space-y-2 border-b border-slate-800 px-4 py-3">
        <div className="flex items-center justify-between gap-2">
          <h2 className="flex items-center gap-2 text-lg font-semibold">
            <TokenPair symbol={pool.symbol} size={26} />
            {pool.symbol}
            <StarButton pool={pool} />
          </h2>
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
        </div>
        <div className="flex flex-wrap gap-1.5">
          <span className={pill}>{protocolLabels[pool.protocol]}</span>
          <span className={pill}>{pool.chain}</span>
          <span className={pill}>{feeLabel(pool.feePercent)}</span>
          {pool.tickSpacing != null && <span className={pill}>{pool.tickSpacing} ticks</span>}
          {pool.held && <span className="rounded-md border border-emerald-400/60 px-2 py-0.5 text-xs text-emerald-400">You hold</span>}
          <PoolRiskBadge pool={pool} onClick={() => setTab("risk")} />
        </div>
      </div>

      <div className="space-y-4 p-4">
        <CurrentPrice
          plan={rangePlan}
          error={rangeQuery.error}
          sym0={sym0}
          sym1={sym1}
          now={now}
          loading={rangeQuery.isFetching && !rangePlan}
          view={view}
          views={views.map((k) => ({ kind: k, label: priced ? makePriceView(k, priced).label : k }))}
          onView={changeView}
        />

        <RiskTabs tab={tab} onTab={setTab} pool={pool} />

        <div role="tabpanel" id="pool-tab-estimate" aria-labelledby="pool-tab-estimate-tab" hidden={tab !== "estimate"} className="space-y-4">
          <div>
            <div className="mb-2 flex items-baseline justify-between">
              <h3 className="text-sm font-medium text-slate-300">Position range</h3>
              <span className="text-xs text-slate-500">around today's price</span>
            </div>
            <div role="group" aria-label="Range presets" className="grid grid-cols-4 gap-1.5">
              {PRESETS.map((p) => (
                <PresetButton key={p.label} active={activePreset?.label === p.label} onClick={() => setBoth(p.width)} title={p.label} hint={`±${p.width}%`} />
              ))}
              <PresetButton
                active={!activePreset}
                dashed={!!activePreset}
                onClick={() => {
                  if (mine != null && !isMine) {
                    setLower(clampPool(mine, MIN_LOWER, MAX_LOWER));
                    setUpper(clampPool(mine, MIN_UPPER, MAX_UPPER));
                  } else document.getElementById("est-width")?.focus();
                }}
                title={mine != null && (!activePreset ? isMine : true) ? "My range" : "Custom"}
                hint={!activePreset ? (seenSymmetric ? `±${fmtPct(seen.lower)}%` : `-${fmtPct(seen.lower)} / +${fmtPct(seen.upper)}%`) : mine != null ? `±${mine}%` : "drag or type"}
              />
            </div>
          </div>

          <dl className="grid grid-cols-3 gap-2 text-xs text-slate-400">
            <div>
              <dt>Width</dt>
              <dd className="num text-sm text-slate-50">{(seen.lower + seen.upper).toFixed(1)}%</dd>
            </div>
            <div>
              <dt>APR</dt>
              <dd className="num text-sm text-slate-50">{fresh ? apr(fresh.apr) : "…"}</dd>
            </div>
            <div>
              <dt>Fees / day</dt>
              <dd className="num text-sm text-slate-50">{fresh ? usdCents(fresh.feesUsd / days) : "…"}</dd>
            </div>
          </dl>

          <label className="flex flex-col gap-1 text-xs text-slate-400" htmlFor="est-width">
            <span>
              Range around the current price:{" "}
              <span className="num text-sm text-slate-50">
                {seenSymmetric ? `±${fmtPct(seen.lower)}%` : `custom, -${fmtPct(seen.lower)}% / +${fmtPct(seen.upper)}%`}
              </span>
            </span>
            <input
              id="est-width"
              type="range"
              min={MIN_WIDTH}
              max={MAX_WIDTH}
              step={STEP}
              value={sliderValue(seen.lower, seen.upper)}
              onChange={(e) => setBoth(Number(e.target.value))}
              className="accent-sky-400"
            />
          </label>

          <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-slate-400">
            <span>Enter the bounds as</span>
            <Segmented<"percent" | "price">
              label="Enter the bounds as"
              value={priceMode ? "price" : "percent"}
              onChange={setInputBy}
              options={[
                { label: "% from price", value: "percent" },
                { label: view ? (view.kind === "usd" ? "Price ($)" : "Price") : "Price", value: "price" },
              ]}
            />
          </div>
          <div className="grid grid-cols-2 gap-2">
            <BoundCard
              id="est-width"
              label="Lower bound"
              other={
                priceMode
                  ? `−${fmtPct(seen.lower)}%`
                  : view && snappedPrices
                    ? view.fmt(snappedPrices[0])
                    : "—"
              }
              prefix={priceMode ? (view?.kind === "usd" ? "$" : "") : "−"}
              suffix={priceMode ? "" : "%"}
              value={priceMode && view ? priceInputValue(view.now * (1 - seen.lower / 100)) : Number(seen.lower.toFixed(2))}
              onValue={(v) => setSeenLower(priceMode && view ? (1 - v / view.now) * 100 : v)}
              min={priceMode ? undefined : MIN_LOWER}
              max={priceMode ? undefined : Number(limits.lower.toFixed(2))}
              step={priceMode ? "any" : STEP}
              snapped={snappedSeen ? `snapped to ${fmtPct(snappedSeen.lower)}%${priceMode && snappedPrices && view ? ` (${view.fmt(snappedPrices[0])})` : ""}` : null}
              onLess={() => setSeenLower(round1(seen.lower - STEP))}
              onMore={() => setSeenLower(round1(seen.lower + STEP))}
              ariaLabel={priceMode ? `Lower bound, price in ${view?.label}` : "Lower bound, percent below the current price"}
              lessLabel="Move the lower bound closer to the price"
              moreLabel="Move the lower bound further from the price"
            />
            <BoundCard
              label="Upper bound"
              other={
                priceMode
                  ? `+${fmtPct(seen.upper)}%`
                  : view && snappedPrices
                    ? view.fmt(snappedPrices[1])
                    : "—"
              }
              prefix={priceMode ? (view?.kind === "usd" ? "$" : "") : "+"}
              suffix={priceMode ? "" : "%"}
              value={priceMode && view ? priceInputValue(view.now * (1 + seen.upper / 100)) : Number(seen.upper.toFixed(2))}
              onValue={(v) => setSeenUpper(priceMode && view ? (v / view.now - 1) * 100 : v)}
              min={priceMode ? undefined : MIN_UPPER}
              max={priceMode ? undefined : Number(limits.upper.toFixed(2))}
              step={priceMode ? "any" : STEP}
              snapped={snappedSeen ? `snapped to +${fmtPct(snappedSeen.upper)}%${priceMode && snappedPrices && view ? ` (${view.fmt(snappedPrices[1])})` : ""}` : null}
              onLess={() => setSeenUpper(round1(seen.upper - STEP))}
              onMore={() => setSeenUpper(round1(seen.upper + STEP))}
              ariaLabel={priceMode ? `Upper bound, price in ${view?.label}` : "Upper bound, percent above the current price"}
              lessLabel="Move the upper bound closer to the price"
              moreLabel="Move the upper bound further from the price"
            />
          </div>
          {view && snappedPrices && <RangeBar view={view} low={snappedPrices[0]} high={snappedPrices[1]} />}
          <p className="-mt-2 text-xs text-slate-500">
            {view ? view.label : `${sym1} per ${sym0}`}. Each bound moves on its own; the presets and the slider move both. Percentages are from today's price.
            {rangePlan && snappedSeen && (
              <>
                {" "}
                Snapped to the pool's {rangePlan.pool.tickSpacing}-tick spacing: {rangePlan.range.tickWidth.toLocaleString("en-US")} ticks wide. Requested -{fmtPct(seen.lower)}% / +{fmtPct(seen.upper)}%, actual -{fmtPct(snappedSeen.lower)}% / +{fmtPct(snappedSeen.upper)}%. The amounts below use the snapped bounds.
              </>
            )}
          </p>

          <div className="flex flex-wrap items-end gap-4">
            <label className="flex flex-col gap-1 text-xs text-slate-400" htmlFor="est-capital">
              Capital (USD)
              <input
                id="est-capital"
                type="number"
                min={100}
                step={500}
                value={mode === "wallet" ? Math.round(heldUsd) : capital}
                disabled={mode === "wallet"}
                onChange={(e) => setCapital(Number(e.target.value))}
                className="num w-28 rounded-md border border-slate-700 bg-slate-950 px-2.5 py-1.5 text-sm text-slate-50 disabled:opacity-60"
              />
            </label>
            {mode === "wallet" && <span className="pb-2 text-xs text-slate-500">Taken from the tokens you hold, below.</span>}
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

          {hasUnreliableVolume(pool) && (
            <p className="rounded-md border border-amber-400/40 bg-amber-400/10 px-3 py-2 text-xs text-amber-400">
              {UNRELIABLE_VOLUME_NOTE}
            </p>
          )}

          <div className="space-y-3 rounded-lg border border-slate-800 bg-slate-950 p-4">
            {error ? (
              <ErrorText error={error} />
            ) : !fresh ? (
              <p className="text-sm text-slate-500">Estimating…</p>
            ) : (
              <>
                <div>
                  <div className="flex items-baseline justify-between gap-3">
                    <div className="text-xs text-slate-400">Estimated fee APR ({fresh.basis} volume)</div>
                    <div className="text-xs text-slate-500">Fees only, from recent volume; not a forecast</div>
                  </div>
                  <div className="num text-3xl font-medium text-sky-400" aria-live="polite">
                    {apr(fresh.apr)}
                  </div>
                  <div className="relative my-1.5 h-1.5 rounded bg-slate-800" aria-hidden="true">
                    <i
                      className="absolute inset-y-0 rounded bg-sky-400/40"
                      style={{ left: `${(fresh.aprLow / scaleMax) * 100}%`, width: `${((fresh.aprHigh - fresh.aprLow) / scaleMax) * 100}%` }}
                    />
                    <b
                      className="absolute -top-[3px] h-3.5 w-[3px] rounded bg-sky-400"
                      style={{ left: `calc(${(fresh.apr / scaleMax) * 100}% - 1px)` }}
                    />
                  </div>
                  <div className="num text-xs text-slate-500">
                    Range across 24h, 7d and 30d rates: {apr(fresh.aprLow)} to {apr(fresh.aprHigh)}
                  </div>
                </div>

                <Earnings
                  capital={cap}
                  days={days}
                  feesUsd={fresh.feesUsd}
                  publishedEmissionsApr={fresh.rewardApr}
                  myEmissions={myEmissions}
                />

                {fresh.depositSharePercent > 5 && (
                  <p className="text-xs text-amber-400">
                    About {fresh.depositSharePercent.toFixed(0)}% of the liquidity at this range: it would dilute its own fees. Treat the
                    estimate as high.
                  </p>
                )}
              </>
            )}
          </div>

          {fresh && (
            <div className="divide-y divide-slate-800 rounded-lg border border-slate-800 bg-slate-950 px-4">
              <Disclosure title="What fees cover" summary={`${days}d fees ≈ ${fresh.feesPercentOfCapital.toFixed(1)}% of capital`}>
                <p className="text-sm">
                  {days} days of fees is about {fresh.feesPercentOfCapital.toFixed(1)}% of your capital. They cover a price{" "}
                  <b className="font-medium">
                    {fresh.breakEvenDownPercent == null ? "fall of any size" : `fall of about ${fresh.breakEvenDownPercent.toFixed(1)}%`}
                  </b>{" "}
                  or{" "}
                  <b className="font-medium">
                    {fresh.breakEvenUpPercent == null ? "rise of any size" : `rise of about ${fresh.breakEvenUpPercent.toFixed(1)}%`}
                  </b>{" "}
                  before impermanent loss cancels them.
                </p>
              </Disclosure>

              <Disclosure title={`Last ${fresh.history.days} days`} summary={`in range ${fresh.history.daysInRange} of ${fresh.history.days} days`}>
                <p className="text-sm">
                  Opened {fresh.history.days} days ago, this range would have been in range{" "}
                  <b className="font-medium">
                    {fresh.history.daysInRange} of {fresh.history.days} days
                  </b>
                  {fresh.history.firstExitDay == null ? " and never left it." : ` and first left it on day ${fresh.history.firstExitDay}.`}
                </p>
                <p className="text-xs text-slate-500">
                  Price moved between {fresh.history.lowPercent.toFixed(1)}% and +{fresh.history.highPercent.toFixed(1)}% (daily closes,
                  so brief moves are missed).
                </p>
              </Disclosure>

              {fresh.alternatives.length > 1 && (
                <Disclosure
                  title={`${base}/${quote} in other pools`}
                  summary={`${fresh.alternatives.length - 1} other, best ${apr(Math.max(...fresh.alternatives.map((a) => a.apr)))}`}
                >
                  <p className="text-xs text-slate-500">Same range in each pool.</p>
                  <ul className="divide-y divide-slate-800 text-sm">
                    {fresh.alternatives.map((a) => (
                      <li key={a.id}>
                        <button
                          type="button"
                          disabled={a.isSelected}
                          onClick={() =>
                            onSelectPool(a.id, Math.round(width * 10) / 10, {
                              ...pool,
                              id: a.id,
                              protocol: a.protocol,
                              feePercent: a.feePercent,
                              tickSpacing: a.tickSpacing,
                              tvlUsd: a.tvlUsd,
                              volume1dUsd: null,
                              volume7dAvgUsd: null,
                              fees7dUsd: null,
                              volumeToTvl: null,
                              feeApr1d: null,
                              feeApr7d: null,
                              rewardApr: null,
                              held: false,
                              myHalfWidthPercent: null,
                            })
                          }
                          className={`flex w-full items-center justify-between gap-3 py-1.5 text-left ${a.isSelected ? "font-semibold text-slate-50" : "text-slate-300 hover:text-sky-400"}`}
                        >
                          <span>
                            {protocolLabels[a.protocol]} {feeLabel(a.feePercent)}
                            <span className="ml-2 text-xs font-normal text-slate-500">{usd(a.tvlUsd)} TVL</span>
                          </span>
                          <span className="num">{apr(a.apr)}</span>
                        </button>
                      </li>
                    ))}
                  </ul>
                </Disclosure>
              )}

              <Disclosure title="How this is estimated">
                <p className="text-xs text-slate-500">
                  The pool's {fresh.basis} fee APR is scaled by how much more liquidity per dollar your range gives than a typical LP's
                  (concentration ×{fresh.concentration.toFixed(1)} versus full range), and by how long the price is expected to stay
                  inside it ({(fresh.timeInRange * 100).toFixed(0)}% of the {days} days). A typical LP range is assumed to be ±
                  {fresh.referenceHalfWidthPercent.toFixed(1)}%, taken from the pool's 30-day price volatility of{" "}
                  {(fresh.volatility * 100).toFixed(0)}% a year. The APR is fees only; the break-even is how impermanent loss compares.
                  {!symmetric && " For a range with two different bounds, the estimate uses the symmetric range with the same price ratio."}
                </p>
              </Disclosure>
            </div>
          )}
        </div>

        <div role="tabpanel" id="pool-tab-enter" aria-labelledby="pool-tab-enter-tab" hidden={tab !== "enter"}>
          <EntryPanel
            pool={pool}
            lowerPercent={lower}
            upperPercent={upper}
            capital={capital}
            mode={mode}
            onModeChange={setMode}
            onHeldUsd={setHeldUsd}
            feeApr={fresh?.apr ?? null}
            emissions={myEmissions}
            publishedEmissionsApr={fresh?.rewardApr ? Number(fresh.rewardApr) : null}
            basePlan={rangePlan}
          />
        </div>

        <div role="tabpanel" id="pool-tab-risk" aria-labelledby="pool-tab-risk-tab" hidden={tab !== "risk"}>
          <PoolRiskSection pool={pool} />
        </div>
      </div>
    </section>
  );
}

function PresetButton({
  active,
  dashed,
  onClick,
  title,
  hint,
}: {
  active: boolean;
  dashed?: boolean;
  onClick: () => void;
  title: string;
  hint: string;
}) {
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={onClick}
      className={`flex flex-col items-center rounded-md border px-1 py-2 text-center ${
        active
          ? "border-sky-400 bg-sky-400/10 text-sky-400"
          : `${dashed ? "border-dashed" : ""} border-slate-700 text-slate-300 hover:bg-slate-800`
      }`}
    >
      <span className="text-sm font-medium">{title}</span>
      <span className="num text-[11px] text-slate-500">{hint}</span>
    </button>
  );
}

function BoundCard({
  id,
  label,
  other,
  prefix,
  suffix,
  value,
  onValue,
  min,
  max,
  step,
  snapped,
  onLess,
  onMore,
  ariaLabel,
  lessLabel,
  moreLabel,
}: {
  id?: string;
  label: string;
  /** The same bound the other way round: the price when the field is in percent, the percent when it is a price. */
  other: string;
  prefix: string;
  suffix: string;
  value: number;
  onValue: (v: number) => void;
  min?: number;
  max?: number;
  step: number | "any";
  snapped: string | null;
  onLess: () => void;
  onMore: () => void;
  ariaLabel: string;
  lessLabel: string;
  moreLabel: string;
}) {
  const btn = "flex h-9 w-9 items-center justify-center rounded-md text-lg text-slate-300 hover:bg-slate-800";
  // The text is the user's draft; it is applied (and clamped) when they leave the field or press Enter.
  const [draft, setDraft] = useState<string | null>(null);
  const commit = () => {
    const v = Number(draft);
    if (draft != null && draft.trim() !== "" && Number.isFinite(v)) onValue(v);
    setDraft(null);
  };
  return (
    <div className="rounded-lg border border-slate-800 bg-slate-950 p-2">
      <div className="flex items-baseline justify-between gap-2 px-1 text-xs text-slate-400">
        <span>{label}</span>
        <span className="num text-slate-50">{other}</span>
      </div>
      <div className="mt-1 flex items-center justify-between">
        <button type="button" onClick={onLess} aria-label={lessLabel} className={btn}>
          −
        </button>
        <span className="flex items-center gap-0.5 text-base font-medium text-sky-400">
          {prefix}
          <input
            id={id}
            type="number"
            inputMode="decimal"
            min={min}
            max={max}
            step={step}
            value={draft ?? value}
            onChange={(e) => setDraft(e.target.value)}
            onBlur={commit}
            onKeyDown={(e) => e.key === "Enter" && commit()}
            aria-label={ariaLabel}
            className={`num ${suffix ? "w-16" : "w-28"} rounded-md border border-slate-700 bg-slate-900 px-1 py-0.5 text-center text-base text-sky-400`}
          />
          {suffix}
        </span>
        <button type="button" onClick={onMore} aria-label={moreLabel} className={btn}>
          +
        </button>
      </div>
      {snapped && <div className="mt-1 text-center text-xs text-slate-400">{snapped}</div>}
    </div>
  );
}

/** The pool's price right now, read from the chain, in the view the user picked, with the USD reference prices it is valued at. */
function CurrentPrice({
  plan,
  error,
  sym0,
  sym1,
  now,
  loading,
  view,
  views,
  onView,
}: {
  plan: EntryPlanDto | undefined;
  error: unknown;
  sym0: string;
  sym1: string;
  now: number;
  loading: boolean;
  view: PriceView | null;
  views: { kind: PriceViewKind; label: string }[];
  onView: (k: PriceViewKind) => void;
}) {
  const money = (v: number) => `$${v.toLocaleString("en-US", { maximumFractionDigits: 2 })}`;
  return (
    <div className="rounded-lg border border-slate-800 bg-slate-950 px-3 py-2" aria-live="polite">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="text-xs text-slate-400">Current price</div>
        {views.length > 1 && (
          <Segmented<PriceViewKind>
            label="Price shown as"
            value={view?.kind ?? views[0].kind}
            onChange={onView}
            options={views.map((v) => ({ label: v.label, value: v.kind }))}
          />
        )}
      </div>
      {plan && view ? (
        <>
          <div className="num text-2xl font-medium">
            {view.fmt(view.now)}
            {view.unit && ` ${view.unit}`}
            {view.kind === "usd" && <span className="ml-2 text-base font-normal text-slate-400">{view.label.replace("$ ", "")}</span>}
          </div>
          {view.kind === "usd" ? (
            <div className="num text-xs text-slate-400">
              Pool ratio {qp(plan.pool.poolPrice)} {sym1} per {sym0}, or {qp(1 / plan.pool.poolPrice)} {sym0} per {sym1}
            </div>
          ) : (
            <div className="num text-xs text-slate-400">
              or {qp(view.flip ? plan.pool.poolPrice : 1 / plan.pool.poolPrice)} {view.flip ? `${sym1} per ${sym0}` : `${sym0} per ${sym1}`}
            </div>
          )}
          <div className="text-xs text-slate-500">
            Read from the pool {formatAgo(plan.pool.readAt, now)}. USD reference {money(plan.pool.usd0)} for {sym0}
            {plan.pool.usd1 !== 1 && `, ${money(plan.pool.usd1)} for ${sym1}`} (DefiLlama, {formatAgo(plan.pool.pricesAsOf, now)}). The plan uses the pool price for the ratio and the USD reference for dollar values.
          </div>
        </>
      ) : error ? (
        <ErrorText error={error} />
      ) : (
        <div className="text-sm text-slate-500">{loading ? "Reading the pool…" : "—"}</div>
      )}
    </div>
  );
}

/** Where the price sits between the snapped bounds, on a scale of 40% to 160% of the price, in the displayed units. */
function RangeBar({ view, low, high }: { view: PriceView; low: number; high: number }) {
  const p = view.now;
  const lo = p * 0.4;
  const hi = p * 1.6;
  const at = (v: number) => Math.min(100, Math.max(0, ((v - lo) / (hi - lo)) * 100));
  const left = at(low);
  return (
    <div className="-mt-2" aria-hidden="true">
      <div className="relative my-3 h-2.5 rounded bg-slate-800">
        <i className="absolute inset-y-0 rounded bg-sky-400/60" style={{ left: `${left}%`, width: `${at(high) - left}%` }} />
        <u className="absolute -top-1 h-[18px] w-[3px] rounded bg-slate-50 no-underline" style={{ left: `${at(p)}%` }} />
      </div>
      <div className="num flex justify-between text-xs text-slate-400">
        <span>{view.fmt(low)}</span>
        <span className="text-slate-50">
          now {view.fmt(p)}
          {view.unit && ` ${view.unit}`}
        </span>
        <span>{view.fmt(high)}</span>
      </div>
    </div>
  );
}

/** Swap fees on the capital over the holding period and, for a gauge pool, the emissions a staked position earns instead. */
function Earnings({
  capital,
  days,
  feesUsd,
  publishedEmissionsApr,
  myEmissions,
}: {
  capital: number;
  days: number;
  feesUsd: number;
  publishedEmissionsApr: number | null;
  myEmissions: Emissions | null;
}) {
  const emissionsApr = publishedEmissionsApr || myEmissions?.apr;
  const own = publishedEmissionsApr ? null : myEmissions;
  const emissionsUsd = emissionsApr ? (capital * emissionsApr * days) / 36500 : null;
  const tokensTotal = own ? capital * own.tokensPerDayPerUsd * days : null;
  return (
    <>
      <dl className="grid grid-cols-[1fr_auto] gap-x-4 gap-y-1 text-sm">
        <dt className="text-slate-400">
          {emissionsApr ? "Not staked: swap fees" : "Swap fees"} on {fullUsd(capital)} over {days} days
        </dt>
        <dd className="num text-right">
          {fullUsd(feesUsd)}
          <span className="block text-xs text-slate-500">{usdCents(feesUsd / days)} / day</span>
        </dd>
        {emissionsApr && emissionsUsd != null ? (
          <>
            <dt className="text-slate-400">
              Staked: {own?.symbol ?? "AERO"} emissions, {apr(emissionsApr)} over {days} days
              {own && tokensTotal != null && (
                <span className="block text-xs text-slate-500">
                  about {tokensTotal.toFixed(1)} {own.symbol} at today's ${own.priceUsd.toFixed(3)}
                </span>
              )}
            </dt>
            <dd className="num text-right">
              {fullUsd(emissionsUsd)}
              <span className="block text-xs text-slate-500">{usdCents(emissionsUsd / days)} / day</span>
            </dd>
          </>
        ) : null}
      </dl>
      {emissionsApr ? (
        <p className="text-xs text-slate-500">
          Pick one: a staked position earns AERO and its swap fees go to the gauge, while an unstaked one keeps the swap fees and earns no AERO.
          {publishedEmissionsApr
            ? " The emission rate is the pool's published one."
            : ` The token amount is what your position has earned so far per dollar deposited, valued at today's price, so the dollars move with the ${own?.symbol ?? "AERO"} price. It is not a forecast: the gauge's share of emissions changes every week.`}
        </p>
      ) : null}
    </>
  );
}

/** A collapsed row: the title and a one-line summary stay visible, the detail opens on click. */
function Disclosure({ title, summary, children }: { title: string; summary?: string; children: ReactNode }) {
  return (
    <details className="group py-2">
      <summary className="flex cursor-pointer list-none items-baseline justify-between gap-3 text-sm [&::-webkit-details-marker]:hidden">
        <span className="flex items-baseline gap-2">
          <span className="text-xs text-slate-500 transition-transform group-open:rotate-90" aria-hidden="true">
            ▸
          </span>
          <span className="text-slate-300">{title}</span>
        </span>
        {summary && <span className="num text-xs text-slate-500">{summary}</span>}
      </summary>
      <div className="space-y-1 pb-1 pl-5 pt-2">{children}</div>
    </details>
  );
}
