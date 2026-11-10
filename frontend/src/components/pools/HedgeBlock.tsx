import { type ReactNode, useState } from "react";
import { CartesianGrid, Line, LineChart, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import type { EntryPlanDto, HedgePlanDto, HedgeScenarioDto, PoolTokenDto } from "../../api/types";
import { amt, gainClass, pct, qp, signedUsd0, usd0, usd2 } from "../../lib/entry";
import { chart } from "../../lib/chartTheme";
import { NumberField, Tile, input } from "./entryBits";

export interface HedgeInputs {
  instrument: "perp" | "borrow";
  /** Share of the position's delta that is shorted, percent. 0 means no hedge. */
  ratioPct: number;
  perpRatePct: number;
  borrowRatePct: number;
  leverage: number;
  /** Trading fee, percent of the notional, charged once to open and once to close. */
  openFeePct: number;
}

const NO_HEDGE = "var(--color-rose-400)";
const HEDGED = "var(--color-emerald-400)";
const TABLE_MOVES = [-20, -10, 0, 10, 20];

/**
 * The hedge, with and without: a short of the volatile token sized to the position, what carrying it costs, and the
 * 30-day result across price moves. All numbers come from the server's plan; the slider only picks a point in the series
 * it returned, so moving it asks for nothing.
 */
export function HedgeBlock({
  plan,
  t0,
  t1,
  inputs,
  onInputs,
  basis,
  onBasis,
  incomeText,
  onIncome,
  borrowSuggestion,
}: {
  plan: EntryPlanDto;
  t0: PoolTokenDto;
  t1: PoolTokenDto;
  inputs: HedgeInputs;
  onInputs: (i: HedgeInputs) => void;
  basis: "fees" | "emissions";
  onBasis: (b: "fees" | "emissions") => void;
  incomeText: string;
  onIncome: (v: string) => void;
  borrowSuggestion: ReactNode;
}) {
  const h = plan.hedge;
  const perp = inputs.instrument === "perp";
  const [move, setMove] = useState(-10);
  const set = (patch: Partial<HedgeInputs>) => onInputs({ ...inputs, ...patch });
  const symbols = [t0.symbol, t1.symbol];

  return (
    <div className="mt-3 border-t border-slate-800 pt-3">
      <div className="flex items-center justify-between gap-2">
        <h4 className="text-xs font-medium uppercase tracking-[.05em] text-slate-400">Hedge: with or without</h4>
        <span className="text-xs text-slate-500">Set the ratio to 0% for no hedge</span>
      </div>

      <div className="mt-2 flex flex-wrap items-end gap-4">
        <label className="flex flex-col gap-1 text-xs text-slate-400">
          Hedge with
          <select value={inputs.instrument} onChange={(e) => set({ instrument: e.target.value as HedgeInputs["instrument"] })} className={`${input} py-1`}>
            <option value="perp">Perp short</option>
            <option value="borrow">Borrow and sell</option>
          </select>
        </label>
        <label className="flex flex-col gap-1 text-xs text-slate-400">
          <span>
            Hedge ratio <span className="num text-slate-50">{inputs.ratioPct}%</span>
          </span>
          <input
            type="range"
            min={0}
            max={100}
            step={5}
            value={inputs.ratioPct}
            onChange={(e) => set({ ratioPct: Number(e.target.value) })}
            className="w-32 accent-sky-400"
            aria-label="Hedge ratio"
          />
        </label>
        <NumberField
          label={perp ? "Funding rate % / yr" : "Borrow rate % / yr"}
          value={perp ? inputs.perpRatePct : inputs.borrowRatePct}
          onChange={(v) => set(perp ? { perpRatePct: v } : { borrowRatePct: v })}
          step={0.5}
          width="w-20"
          min={-1000}
        />
        {perp && <NumberField label="Leverage" value={inputs.leverage} onChange={(v) => set({ leverage: v })} step={1} width="w-16" min={1} />}
        <NumberField label="Open / close fee %" value={inputs.openFeePct} onChange={(v) => set({ openFeePct: v })} step={0.01} width="w-20" />
      </div>
      <div className="mt-2 flex flex-wrap items-end gap-4">
        <label className="flex flex-col gap-1 text-xs text-slate-400">
          Income counted
          <select value={basis} onChange={(e) => onBasis(e.target.value as "fees" | "emissions")} className={`${input} py-1`}>
            <option value="fees">Swap fees (not staked)</option>
            <option value="emissions">Emissions (staked)</option>
          </select>
        </label>
        <label className="flex flex-col gap-1 text-xs text-slate-400">
          Income APR %
          <input type="number" step={1} min={0} value={incomeText} onChange={(e) => onIncome(e.target.value)} className={`${input} w-20`} />
        </label>
      </div>
      <p className="mt-1 text-xs text-slate-500">
        {perp
          ? "Positive funding: longs pay shorts, so your short earns it. Negative funding is a cost."
          : "Positive borrow rate: you pay interest on the borrowed token. Check the health factor on the Borrow page."}{" "}
        {!perp && borrowSuggestion}
      </p>

      {!h || h.legs === "None" ? (
        <p className="mt-2 text-xs text-slate-500">Both tokens in this pool are stable, so there is no price risk to hedge.</p>
      ) : (
        <HedgeDetail plan={plan} h={h} symbols={symbols} perp={perp} leverage={inputs.leverage} move={move} onMove={setMove} />
      )}
    </div>
  );
}

function HedgeDetail({
  plan,
  h,
  symbols,
  perp,
  leverage,
  move,
  onMove,
}: {
  plan: EntryPlanDto;
  h: HedgePlanDto;
  symbols: string[];
  perp: boolean;
  leverage: number;
  move: number;
  onMove: (m: number) => void;
}) {
  const earn = h.carryPerDayUsd >= 0;
  const range = plan.range;
  const volatileName = h.legs === "Token1" ? symbols[1] : symbols[0];

  return (
    <>
      <div className="mt-2 grid grid-cols-3 gap-2">
        <div className="rounded-lg bg-slate-800/60 px-2.5 py-2">
          <span className="block text-xs text-slate-400">Short now</span>
          {h.shorts.length === 0 ? (
            <b className="num block text-sm font-medium">none</b>
          ) : (
            h.shorts.map((s) => (
              <b key={s.token} className="num block text-sm font-medium">
                {amt(s.amount)} {symbols[s.token]}
              </b>
            ))
          )}
          <span className="num block text-xs text-slate-500">notional {usd0(h.notionalUsd)}</span>
        </div>
        <Tile
          label={perp ? "Funding per day" : "Borrow cost per day"}
          value={`${earn ? "+" : "-"}${usd2(Math.abs(h.carryPerDayUsd))}`}
          sub={`you ${earn ? "earn" : "pay"} ${usd0(Math.abs(h.carryPerDayUsd) * 30)} / 30 days`}
        />
        <Tile label="Net APR, hedged" value={pct(h.hedgedNetApr * 100)} sub={`no hedge: ${pct(h.unhedgedApr * 100)}${perp && h.marginUsd > 0 ? ` · on ${usd0(h.capitalUsedUsd)}` : ""}`} />
      </div>

      {perp && h.marginUsd > 0 && (
        <div className="mt-2 text-xs text-slate-400">
          Margin needed {usd0(h.marginUsd)} at {leverage}x.
          {h.liquidationPoolPrice != null && (
            <>
              {" "}
              Liquidation price about {qp(h.liquidationPoolPrice)} {symbols[1]} per {symbols[0]} ({h.legs === "Token1" ? "-" : "+"}
              {pct(h.liquidationMovePct ?? 0)} for {volatileName}). Range {h.legs === "Token1" ? "bottom" : "top"} {qp(h.legs === "Token1" ? range.priceLower : range.priceUpper)} (+{pct(h.upperBoundMovePct)} for {volatileName}).
            </>
          )}
          {h.liquidatesBeforeRangeTop && h.ratio > 0 && (
            <p className="mt-1 text-amber-400">Liquidated before the {h.legs === "Token1" ? "bottom" : "top"} of your range. Lower the leverage or add margin.</p>
          )}
        </div>
      )}

      {h.legs === "Both" && (
        <p className="mt-2 text-xs text-amber-400">
          This pool has no stable leg. The short for each token is listed above; the price-scenario comparison for volatile pairs comes later.
        </p>
      )}

      {h.shorts.length > 0 && <EdgeTable h={h} symbols={symbols} />}

      {h.scenarios.length > 0 && <Scenarios plan={plan} h={h} symbols={symbols} move={move} onMove={onMove} volatileName={volatileName} />}

      <p className="mt-2 text-xs text-slate-500">
        Fees are earned only while the price is in range (a straight-line path to the scenario price). A hedge sized today drifts as the price moves.
      </p>
    </>
  );
}

function EdgeTable({ h, symbols }: { h: HedgePlanDto; symbols: string[] }) {
  return (
    <>
      <table className="mt-3 w-full text-xs">
        <thead>
          <tr className="text-slate-400">
            <th className="py-1 text-left font-normal">Short size</th>
            <th className="py-1 text-right font-normal">Now</th>
            <th className="py-1 text-right font-normal">At lower bound</th>
            <th className="py-1 text-right font-normal">At upper bound</th>
          </tr>
        </thead>
        <tbody className="num">
          {h.shorts.map((s) => (
            <FragmentRows key={s.token} s={s} name={symbols[s.token]} />
          ))}
        </tbody>
      </table>
      <p className="mt-1 text-xs text-slate-500">
        Re-hedge when the price moves about 5%:{" "}
        {h.shorts.map((s, i) => (
          <span key={s.token}>
            {i > 0 && "; "}
            at -5% {s.rehedgeIfDown >= 0 ? "add" : "cut"} {amt(Math.abs(s.rehedgeIfDown))} {symbols[s.token]} of short, at +5% {s.rehedgeIfUp >= 0 ? "add" : "cut"} {amt(Math.abs(s.rehedgeIfUp))}
          </span>
        ))}
        . Bounds are the pool's lower and upper price.
      </p>
    </>
  );
}

function FragmentRows({ s, name }: { s: HedgePlanDto["shorts"][number]; name: string }) {
  return (
    <>
      <tr className="border-t border-slate-800">
        <td className="py-1 text-left">{name}</td>
        <td className="py-1 text-right">{amt(s.amount)}</td>
        <td className="py-1 text-right">{amt(s.atLowerBound)}</td>
        <td className="py-1 text-right">{amt(s.atUpperBound)}</td>
      </tr>
      <tr>
        <td className="pb-1 text-left text-slate-400">Notional</td>
        <td className="pb-1 text-right">{usd0(s.notionalUsd)}</td>
        <td className="pb-1 text-right">{usd0(s.notionalAtLowerBoundUsd)}</td>
        <td className="pb-1 text-right">{usd0(s.notionalAtUpperBoundUsd)}</td>
      </tr>
    </>
  );
}

function Scenarios({
  plan,
  h,
  symbols,
  move,
  onMove,
  volatileName,
}: {
  plan: EntryPlanDto;
  h: HedgePlanDto;
  symbols: string[];
  move: number;
  onMove: (m: number) => void;
  volatileName: string;
}) {
  const days = 30;
  const first = h.scenarios[0].movePct;
  const last = h.scenarios[h.scenarios.length - 1].movePct;
  const step = h.scenarios.length > 1 ? h.scenarios[1].movePct - first : 1;
  const at = (m: number): HedgeScenarioDto =>
    h.scenarios.reduce((best, s) => (Math.abs(s.movePct - m) < Math.abs(best.movePct - m) ? s : best), h.scenarios[0]);
  const q = at(move);
  const value = plan.entry.valueUsd;
  const diff = q.hedgedNetUsd - q.noHedgeNetUsd;
  const sign = (m: number) => (m > 0 ? "+" : "");
  const row = (label: ReactNode, none: ReactNode, hedged: ReactNode, total = false) => (
    <tr className={total ? "border-t border-slate-700 font-semibold" : ""}>
      <td className="py-0.5 text-left text-slate-400">{label}</td>
      <td className="num py-0.5 text-right">{none}</td>
      <td className="num py-0.5 text-right">{hedged}</td>
    </tr>
  );
  const money = (v: number) => <span className={gainClass(v)}>{signedUsd0(v)}</span>;
  const dash = <span className="text-slate-500">-</span>;

  return (
    <>
      <div className="mt-3 rounded-lg bg-slate-800/60 p-2.5">
        <div className="text-xs text-slate-400">
          Compare after {days} days if {volatileName} moves <span className="num text-slate-50">{sign(q.movePct)}{q.movePct}%</span> (pool price {qp(q.poolPrice)} {symbols[1]} per {symbols[0]})
        </div>
        <input
          type="range"
          min={first}
          max={last}
          step={step}
          value={move}
          onChange={(e) => onMove(Number(e.target.value))}
          className="w-full accent-sky-400"
          aria-label="Price move"
        />
        <table className="mt-1 w-full text-xs">
          <thead>
            <tr className="text-slate-400">
              <th />
              <th className="py-1 text-right font-normal">No hedge</th>
              <th className="py-1 text-right font-normal">With hedge</th>
            </tr>
          </thead>
          <tbody>
            {row(`Fees earned (${q.daysInRange.toFixed(0)} of ${days} days in range)`, money(q.feesUsd), money(q.feesUsd))}
            {row("Price effect on position", money(q.priceEffectUsd), money(q.priceEffectUsd))}
            {row("Hedge profit or loss", dash, money(q.hedgePnlUsd))}
            {row("Funding or borrow, and trading fees", dash, money(-q.hedgeCostUsd))}
            {row("Entry cost (swap and gas)", money(-q.entryCostUsd), money(-q.entryCostUsd))}
            {row("Net result", money(q.noHedgeNetUsd), money(q.hedgedNetUsd), true)}
            {row(
              "Net, % of capital used",
              <span>{pct((q.noHedgeNetUsd / (value || 1)) * 100)}</span>,
              <span>{pct((q.hedgedNetUsd / (h.capitalUsedUsd || 1)) * 100)}</span>,
            )}
          </tbody>
        </table>
        <p className="mt-2 text-sm">
          At {sign(q.movePct)}
          {q.movePct}% the hedge leaves you{" "}
          <b className={`font-medium ${diff >= 0 ? "text-emerald-400" : "text-rose-400"}`}>
            {usd0(Math.abs(diff))} {diff >= 0 ? "better off" : "worse off"}
          </b>
          . Across {first}% to +{last}%: no hedge ranges from {usd0(h.noHedgeMinUsd)} to {usd0(h.noHedgeMaxUsd)}, with hedge from {usd0(h.hedgedMinUsd)} to {usd0(h.hedgedMaxUsd)}.
        </p>
      </div>

      <div className="mt-3 text-xs text-slate-400">30-day result by price move: fees while in range, hedge and entry costs included</div>
      <HedgeChart h={h} />
      <div className="flex flex-wrap gap-3 text-xs text-slate-400">
        <span>
          <i className="mr-1 inline-block h-[3px] w-3 align-middle" style={{ background: NO_HEDGE }} />
          No hedge
        </span>
        <span>
          <i className="mr-1 inline-block h-[3px] w-3 align-middle" style={{ background: HEDGED }} />
          With hedge
        </span>
        <span>Dashed: range bounds</span>
      </div>

      <table className="mt-2 w-full text-xs">
        <thead>
          <tr className="text-slate-400">
            <th className="py-1 text-left font-normal">Move</th>
            <th className="py-1 text-right font-normal">No hedge</th>
            <th className="py-1 text-right font-normal">With hedge</th>
          </tr>
        </thead>
        <tbody>
          {TABLE_MOVES.map((m) => {
            const s = at(m);
            return (
              <tr key={m} className="border-t border-slate-800">
                <td className="num py-1 text-left">
                  {sign(m)}
                  {m}%
                </td>
                <td className="num py-1 text-right">{money(s.noHedgeNetUsd)}</td>
                <td className="num py-1 text-right">
                  {money(s.hedgedNetUsd)}
                  <br />
                  <span className="text-[11px]">{money(s.hedgedNetUsd - s.noHedgeNetUsd)}</span>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </>
  );
}

function HedgeChart({ h }: { h: HedgePlanDto }) {
  const data = h.scenarios.map((s) => ({ move: s.movePct, none: Math.round(s.noHedgeNetUsd), hedged: Math.round(s.hedgedNetUsd) }));
  const first = data[0].move;
  const last = data[data.length - 1].move;
  const bounds = [h.lowerBoundMovePct, h.upperBoundMovePct].filter((m) => m > first && m < last);
  return (
    <div className="h-44 w-full" role="img" aria-label="30-day result versus price move, with and without the hedge">
      <ResponsiveContainer width="100%" height="100%">
        <LineChart data={data} margin={{ top: 6, right: 8, bottom: 0, left: 0 }}>
          <CartesianGrid stroke={chart.grid} vertical={false} />
          <XAxis
            dataKey="move"
            type="number"
            domain={[first, last]}
            ticks={[-30, -15, 0, 15, 30].filter((t) => t >= first && t <= last)}
            tickFormatter={(v: number) => `${v > 0 ? "+" : ""}${v}%`}
            stroke={chart.axis}
            fontSize={11}
          />
          <YAxis stroke={chart.axis} fontSize={11} width={56} tickFormatter={(v: number) => usd0(v)} />
          <Tooltip
            contentStyle={{ background: chart.tooltip.background, border: chart.tooltip.border, fontSize: 12 }}
            formatter={(v, name) => [usd0(Number(v)), name === "none" ? "No hedge" : "With hedge"]}
            labelFormatter={(v) => `Move ${Number(v) > 0 ? "+" : ""}${v}%`}
          />
          <ReferenceLine y={0} stroke={chart.axis} strokeOpacity={0.5} />
          {bounds.map((m) => (
            <ReferenceLine key={m} x={m} stroke={chart.axis} strokeDasharray="3 3" />
          ))}
          <Line type="linear" dataKey="none" stroke={NO_HEDGE} strokeWidth={2} dot={false} isAnimationActive={false} />
          <Line type="linear" dataKey="hedged" stroke={HEDGED} strokeWidth={2} dot={false} isAnimationActive={false} />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}
