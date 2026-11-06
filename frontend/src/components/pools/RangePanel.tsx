import { type ReactNode, useState } from "react";
import { usePoolEstimate } from "../../api/client";
import type { PoolDto } from "../../api/types";
import { protocolLabels } from "../../lib/format";
import {
  DEFAULT_CAPITAL,
  type Emissions,
  UNRELIABLE_VOLUME_NOTE,
  apr,
  feeLabel,
  fullUsd,
  hasUnreliableVolume,
  priceLabel,
  tokenSymbols,
  usd,
  usdCents,
} from "../../lib/pools";
import { useDebounced } from "../../lib/useDebounced";
import { ErrorText, Segmented } from "../ui";
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

const pill = "rounded-md border border-slate-700 bg-slate-950 px-2 py-0.5 text-xs text-slate-300";

/**
 * The range side of the explorer: pick a width around today's price (preset, slider or bound steppers) and see what a
 * deposit of that size could earn, what fees cover, how the range would have fared lately and where else the pair trades.
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
  const [width, setWidth] = useState(initialWidth ?? mine ?? 10);
  const [capital, setCapital] = useState(DEFAULT_CAPITAL);
  const [days, setDays] = useState(30);
  const dWidth = useDebounced(width);
  const dCapital = useDebounced(capital);
  const cap = Math.max(100, dCapital || 0);
  const { data, error, isFetching } = usePoolEstimate(pool.id, dWidth, cap, days, pool.feePercent, knownFees);
  const fresh = data && data.poolId === pool.id ? data : null;
  const scaleMax = data ? Math.max(data.aprHigh * 1.15, 1) : 1;
  const activePreset = PRESETS.find((p) => p.width === width);

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
        <div>
          <div className="mb-2 flex items-baseline justify-between">
            <h3 className="text-sm font-medium text-slate-300">Position range</h3>
            <span className="text-xs text-slate-500">around today's price</span>
          </div>
          <div role="group" aria-label="Range presets" className="grid grid-cols-4 gap-1.5">
            {PRESETS.map((p) => (
              <PresetButton key={p.label} active={activePreset?.label === p.label} onClick={() => setWidth(p.width)} title={p.label} hint={`±${p.width}%`} />
            ))}
            <PresetButton
              active={!activePreset}
              dashed={!!activePreset}
              onClick={() => (mine != null && mine !== width ? setWidth(mine) : document.getElementById("est-width")?.focus())}
              title={mine != null && (!activePreset ? mine === width : true) ? "My range" : "Custom"}
              hint={!activePreset ? `±${width}%` : mine != null ? `±${mine}%` : "drag or type"}
            />
          </div>
        </div>

        <dl className="grid grid-cols-3 gap-2 text-xs text-slate-400">
          <div>
            <dt>Width</dt>
            <dd className="num text-sm text-slate-50">{(width * 2).toFixed(1)}%</dd>
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
            Range around the current price: <span className="num text-sm text-slate-50">±{width}%</span>
          </span>
          <input
            id="est-width"
            type="range"
            min={MIN_WIDTH}
            max={MAX_WIDTH}
            step={STEP}
            value={width}
            onChange={(e) => setWidth(Number(e.target.value))}
            className="accent-sky-400"
          />
        </label>

        <div className="grid grid-cols-2 gap-2">
          <BoundCard
            label="Lower bound"
            price={fresh ? `${priceLabel(fresh.priceLower)} ${quote}` : "—"}
            percent={`−${width}%`}
            onLess={() => setWidth(clampWidth(width + STEP))}
            onMore={() => setWidth(clampWidth(width - STEP))}
            lessLabel="Move the lower bound further from the price"
            moreLabel="Move the lower bound closer to the price"
          />
          <BoundCard
            label="Upper bound"
            price={fresh ? `${priceLabel(fresh.priceUpper)} ${quote}` : "—"}
            percent={`+${width}%`}
            onLess={() => setWidth(clampWidth(width - STEP))}
            onMore={() => setWidth(clampWidth(width + STEP))}
            lessLabel="Move the upper bound closer to the price"
            moreLabel="Move the upper bound further from the price"
          />
        </div>
        <p className="-mt-2 text-xs text-slate-500">
          {quote} per {base}. The two bounds move together: the range stays centred on today's price.
        </p>

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
                <div className="text-xs text-slate-400">Estimated fee APR ({fresh.basis} volume)</div>
                <div className="num text-4xl font-medium text-sky-400" aria-live="polite">
                  {apr(fresh.apr)}
                </div>
                <div className="relative my-2 h-2 rounded bg-slate-800" aria-hidden="true">
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

              <Section title="What fees cover">
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
              </Section>

              <Section title={`Last ${fresh.history.days} days`}>
                <p className="text-sm">
                  Opened {fresh.history.days} days ago, this range would have been in range{" "}
                  <b className="font-medium">
                    {fresh.history.daysInRange} of {fresh.history.days} days
                  </b>
                  {fresh.history.firstExitDay == null ? " and never left it." : ` and first left it on day ${fresh.history.firstExitDay}.`}
                </p>
                <p className="text-xs text-slate-500">
                  Price moved between {fresh.history.lowPercent.toFixed(1)}% and +{fresh.history.highPercent.toFixed(1)}% (daily
                  closes, so brief moves are missed).
                </p>
              </Section>

              {fresh.alternatives.length > 1 && (
                <Section title={`${base}/${quote} in other pools, same range`}>
                  <ul className="divide-y divide-slate-800 text-sm">
                    {fresh.alternatives.map((a) => (
                      <li key={a.id}>
                        <button
                          type="button"
                          disabled={a.isSelected}
                          onClick={() =>
                            onSelectPool(a.id, width, {
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
                </Section>
              )}

              {fresh.depositSharePercent > 5 && (
                <p className="text-xs text-amber-400">
                  About {fresh.depositSharePercent.toFixed(0)}% of the liquidity at this range: it would dilute its own fees. Treat the
                  estimate as high.
                </p>
              )}

              <details className="text-xs text-slate-500">
                <summary className="cursor-pointer text-slate-400">How this is estimated</summary>
                <p className="mt-1">
                  The pool's {fresh.basis} fee APR is scaled by how much more liquidity per dollar your range gives than a typical
                  LP's (concentration ×{fresh.concentration.toFixed(1)} versus full range), and by how long the price is expected to
                  stay inside it ({(fresh.timeInRange * 100).toFixed(0)}% of the {days} days). A typical LP range is assumed to be ±
                  {fresh.referenceHalfWidthPercent.toFixed(1)}%, taken from the pool's 30-day price volatility of{" "}
                  {(fresh.volatility * 100).toFixed(0)}% a year.
                </p>
              </details>
            </>
          )}
        </div>
        <p className="text-xs text-slate-500">
          The APR is fees only; the break-even above is how impermanent loss compares. Based on recent volume, so it is not a
          forecast.
        </p>
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
  label,
  price,
  percent,
  onLess,
  onMore,
  lessLabel,
  moreLabel,
}: {
  label: string;
  price: string;
  percent: string;
  onLess: () => void;
  onMore: () => void;
  lessLabel: string;
  moreLabel: string;
}) {
  const btn = "flex h-9 w-9 items-center justify-center rounded-md text-lg text-slate-300 hover:bg-slate-800";
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
        <span className="num text-base font-medium text-sky-400">{percent}</span>
        <button type="button" onClick={onMore} aria-label={moreLabel} className={btn}>
          +
        </button>
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

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="space-y-1 border-t border-slate-800 pt-3">
      <h3 className="text-xs font-medium uppercase tracking-[.05em] text-slate-400">{title}</h3>
      {children}
    </div>
  );
}
