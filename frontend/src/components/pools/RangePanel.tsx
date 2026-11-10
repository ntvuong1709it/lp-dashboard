import { type ReactNode, useEffect, useMemo, useState } from "react";
import { useEntryPlan, usePoolEstimate } from "../../api/client";
import type { EntryPlanDto, EntryPlanRequest, PoolDto } from "../../api/types";
import { MAX_LOWER, MAX_UPPER, MIN_LOWER, MIN_UPPER, clampLower, clampUpper, equivalentHalfWidth, qp } from "../../lib/entry";
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
import { useDebounced } from "../../lib/useDebounced";
import { ErrorText, Segmented } from "../ui";
import { type EntryMode, EntryPanel } from "./EntryPanel";
import { TokenPair } from "./TokenPair";

const MIN_WIDTH = 1;
const MAX_WIDTH = 50;
const STEP = 0.5;
const PRESETS = [
  { label: "Tight", width: 2 },
  { label: "Standard", width: 10 },
  { label: "Wide", width: 25 },
];
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
  const [lower, setLower] = useState(clampLower(start));
  const [upper, setUpper] = useState(clampUpper(start));
  const [capital, setCapital] = useState(DEFAULT_CAPITAL);
  const [mode, setMode] = useState<EntryMode>("budget");
  const [heldUsd, setHeldUsd] = useState(0);
  const [days, setDays] = useState(30);
  const setBoth = (w: number) => {
    setLower(clampLower(w));
    setUpper(clampUpper(w));
  };
  // The APR estimate is for a symmetric range; a skewed one is asked for as the symmetric range with the same price ratio.
  const width = Math.round(equivalentHalfWidth(lower, upper) * 100) / 100;
  const symmetric = lower === upper;
  const dWidth = useDebounced(width);
  const dCapital = useDebounced(mode === "wallet" ? Math.round(heldUsd) : capital);
  const cap = Math.max(100, dCapital || 0);
  const { data, error, isFetching } = usePoolEstimate(pool.id, dWidth, cap, days, pool.feePercent, knownFees);
  const fresh = data && data.poolId === pool.id ? data : null;
  const scaleMax = data ? Math.max(data.aprHigh * 1.15, 1) : 1;
  const activePreset = PRESETS.find((p) => p.width === lower && p.width === upper);

  // The pool's price and the snapped range come from the chain via the entry-plan endpoint (the same one the form below uses).
  const rangeKey = useDebounced(
    JSON.stringify({ lowerPercent: lower, upperPercent: upper, budgetUsd: Math.max(100, capital || 0), swapCostPct: 0.1, gasUsd: 0, incomeAprPct: 0, incomeBasis: "fees" }),
  );
  const rangeBody = useMemo(() => JSON.parse(rangeKey) as EntryPlanRequest, [rangeKey]);
  const rangeQuery = useEntryPlan(pool.id, rangeBody);
  const rangePlan = rangeQuery.data && rangeQuery.data.pool.poolId === pool.id ? rangeQuery.data : undefined;
  const [sym0, sym1] = rangePlan ? [rangePlan.pool.token0.symbol, rangePlan.pool.token1.symbol] : [base, quote];
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
        </div>
      </div>

      <div className="space-y-4 p-4">
        <CurrentPrice plan={rangePlan} error={rangeQuery.error} sym0={sym0} sym1={sym1} now={now} loading={rangeQuery.isFetching && !rangePlan} />

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
              onClick={() => (mine != null && !(lower === mine && upper === mine) ? setBoth(mine) : document.getElementById("est-width")?.focus())}
              title={mine != null && (!activePreset ? lower === mine && upper === mine : true) ? "My range" : "Custom"}
              hint={!activePreset ? (symmetric ? `±${lower}%` : `-${lower} / +${upper}%`) : mine != null ? `±${mine}%` : "drag or type"}
            />
          </div>
        </div>

        <dl className="grid grid-cols-3 gap-2 text-xs text-slate-400">
          <div>
            <dt>Width</dt>
            <dd className="num text-sm text-slate-50">{(lower + upper).toFixed(1)}%</dd>
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
            <span className="num text-sm text-slate-50">{symmetric ? `±${lower}%` : `custom, -${lower}% / +${upper}%`}</span>
          </span>
          <input
            id="est-width"
            type="range"
            min={MIN_WIDTH}
            max={MAX_WIDTH}
            step={STEP}
            value={sliderValue(lower, upper)}
            onChange={(e) => setBoth(Number(e.target.value))}
            className="accent-sky-400"
          />
        </label>

        <div className="grid grid-cols-2 gap-2">
          <BoundCard
            id="est-width"
            label="Lower bound"
            price={rangePlan ? `${qp(rangePlan.range.priceLower)} ${sym1}` : "—"}
            sign="−"
            value={lower}
            onValue={(v) => setLower(clampLower(v))}
            min={MIN_LOWER}
            max={MAX_LOWER}
            snapped={rangePlan ? `snapped to ${rangePlan.range.lowerPercent.toFixed(2)}%` : null}
            onLess={() => setLower(clampLower(lower - STEP))}
            onMore={() => setLower(clampLower(lower + STEP))}
            lessLabel="Move the lower bound closer to the price"
            moreLabel="Move the lower bound further from the price"
          />
          <BoundCard
            label="Upper bound"
            price={rangePlan ? `${qp(rangePlan.range.priceUpper)} ${sym1}` : "—"}
            sign="+"
            value={upper}
            onValue={(v) => setUpper(clampUpper(v))}
            min={MIN_UPPER}
            max={MAX_UPPER}
            snapped={rangePlan ? `snapped to +${rangePlan.range.upperPercent.toFixed(2)}%` : null}
            onLess={() => setUpper(clampUpper(upper - STEP))}
            onMore={() => setUpper(clampUpper(upper + STEP))}
            lessLabel="Move the upper bound closer to the price"
            moreLabel="Move the upper bound further from the price"
          />
        </div>
        {rangePlan && <RangeBar plan={rangePlan} sym1={sym1} />}
        <p className="-mt-2 text-xs text-slate-500">
          {sym1} per {sym0}. Each bound moves on its own; the presets and the slider move both. Percentages are from today's price.
          {rangePlan && (
            <>
              {" "}
              Snapped to the pool's {rangePlan.pool.tickSpacing}-tick spacing: {rangePlan.range.tickWidth.toLocaleString("en-US")} ticks wide. Requested -{lower}% / +{upper}%, actual {rangePlan.range.lowerPercent.toFixed(2)}% / +{rangePlan.range.upperPercent.toFixed(2)}%. The amounts below use the snapped bounds.
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
  price,
  sign,
  value,
  onValue,
  min,
  max,
  snapped,
  onLess,
  onMore,
  lessLabel,
  moreLabel,
}: {
  id?: string;
  label: string;
  price: string;
  sign: "−" | "+";
  value: number;
  onValue: (v: number) => void;
  min: number;
  max: number;
  snapped: string | null;
  onLess: () => void;
  onMore: () => void;
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
        <span className="num text-slate-50">{price}</span>
      </div>
      <div className="mt-1 flex items-center justify-between">
        <button type="button" onClick={onLess} aria-label={lessLabel} className={btn}>
          −
        </button>
        <span className="flex items-center gap-0.5 text-base font-medium text-sky-400">
          {sign}
          <input
            id={id}
            type="number"
            inputMode="decimal"
            min={min}
            max={max}
            step={STEP}
            value={draft ?? value}
            onChange={(e) => setDraft(e.target.value)}
            onBlur={commit}
            onKeyDown={(e) => e.key === "Enter" && commit()}
            aria-label={`${label}, percent ${sign === "−" ? "below" : "above"} the current price`}
            className="num w-16 rounded-md border border-slate-700 bg-slate-900 px-1 py-0.5 text-center text-base text-sky-400"
          />
          %
        </span>
        <button type="button" onClick={onMore} aria-label={moreLabel} className={btn}>
          +
        </button>
      </div>
      {snapped && <div className="mt-1 text-center text-xs text-slate-400">{snapped}</div>}
    </div>
  );
}

/** The pool's price right now, read from the chain, with the USD reference prices it is valued at. */
function CurrentPrice({
  plan,
  error,
  sym0,
  sym1,
  now,
  loading,
}: {
  plan: EntryPlanDto | undefined;
  error: unknown;
  sym0: string;
  sym1: string;
  now: number;
  loading: boolean;
}) {
  const money = (v: number) => `$${v.toLocaleString("en-US", { maximumFractionDigits: 2 })}`;
  return (
    <div className="rounded-lg border border-slate-800 bg-slate-950 px-3 py-2" aria-live="polite">
      <div className="text-xs text-slate-400">Current price</div>
      {plan ? (
        <>
          <div className="num text-2xl font-medium">
            {qp(plan.pool.poolPrice)} {sym1} per {sym0}
          </div>
          {plan.pool.poolPrice < 1 && (
            <div className="num text-xs text-slate-400">
              or {qp(1 / plan.pool.poolPrice)} {sym0} per {sym1}
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

/** Where the pool price sits between the snapped bounds, on a scale of 40% to 160% of the price. */
function RangeBar({ plan, sym1 }: { plan: EntryPlanDto; sym1: string }) {
  const p = plan.pool.poolPrice;
  const lo = p * 0.4;
  const hi = p * 1.6;
  const at = (v: number) => Math.min(100, Math.max(0, ((v - lo) / (hi - lo)) * 100));
  const left = at(plan.range.priceLower);
  return (
    <div className="-mt-2" aria-hidden="true">
      <div className="relative my-3 h-2.5 rounded bg-slate-800">
        <i className="absolute inset-y-0 rounded bg-sky-400/60" style={{ left: `${left}%`, width: `${at(plan.range.priceUpper) - left}%` }} />
        <u className="absolute -top-1 h-[18px] w-[3px] rounded bg-slate-50 no-underline" style={{ left: `${at(p)}%` }} />
      </div>
      <div className="num flex justify-between text-xs text-slate-400">
        <span>{qp(plan.range.priceLower)}</span>
        <span className="text-slate-50">
          now {qp(p)} {sym1}
        </span>
        <span>{qp(plan.range.priceUpper)}</span>
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
