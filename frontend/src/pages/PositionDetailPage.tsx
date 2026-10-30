import { useState, type ReactNode } from "react";
import { Link, useParams } from "react-router-dom";
import {
  Bar,
  BarChart,
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { usePosition, useSnapshots } from "../api/client";
import type {
  PositionDto,
  PositionEventDto,
  SnapshotPointDto,
} from "../api/types";
import { RangeBadge, RangeBar } from "../components/RangeBar";
import { Empty, ErrorText, Panel, StatCard } from "../components/ui";
import {
  addressUrl,
  formatAge,
  formatAmount,
  formatDate,
  formatFee,
  formatPercent,
  formatPrice,
  formatUsd,
  invertRange,
  pnlClass,
  protocolLabels,
  txUrl,
} from "../lib/format";

export function PositionDetailPage() {
  const id = Number(useParams().id);
  const { data, error, isLoading } = usePosition(id);

  if (error) return <ErrorText error={error} />;
  if (isLoading || !data) return <Empty>Loading…</Empty>;

  const p = data.position;
  const m = p.metrics;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-baseline gap-3">
        <Link to="/" className="text-sm text-slate-400 hover:text-white">
          ← Portfolio
        </Link>
        <h1 className="text-xl font-semibold">
          {p.token0?.symbol}/{p.token1?.symbol}{" "}
          <span className="text-sm text-slate-500">{formatFee(p.fee)}</span>
        </h1>
        <span className="text-sm text-slate-400">
          {protocolLabels[p.protocol]} · {p.chain} · #{p.tokenId}
        </span>
        {p.isStaked && <span className="text-sm text-sky-400">staked</span>}
        {p.poolAddress && (
          <a
            className="text-sm text-sky-400 hover:underline"
            href={addressUrl(p.chain, p.poolAddress)}
            target="_blank"
            rel="noreferrer"
          >
            pool
          </a>
        )}
      </div>

      <div className="grid grid-cols-2 gap-3 md:grid-cols-4 lg:grid-cols-7">
        <StatCard label="Value" value={formatUsd(m.currentValueUsd)} />
        <StatCard
          label="Fees earned"
          value={formatUsd(m.feesEarnedUsd)}
          hint={
            m.feeAprPercent != null
              ? `APR ${formatPercent(m.feeAprPercent)}`
              : undefined
          }
        />
        <StatCard
          label="P/L"
          value={formatUsd(m.pnlUsd, true)}
          valueClass={pnlClass(m.pnlUsd)}
          hint={formatPercent(m.pnlPercent, true)}
        />
        <StatCard
          label="P/L vs HODL"
          value={formatUsd(m.pnlVsHodlUsd, true)}
          valueClass={pnlClass(m.pnlVsHodlUsd)}
        />
        <StatCard
          label="Impermanent loss"
          value={formatUsd(m.impermanentLossUsd, true)}
          valueClass={pnlClass(m.impermanentLossUsd)}
        />
        <StatCard
          label="Age"
          value={formatAge(m.ageDays)}
          hint={`opened ${formatDate(p.openedAt)}`}
        />
        <StatCard label="Gas" value={formatUsd(m.gasUsd)} />
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <RangePanel position={p} />
        <BreakdownPanel position={p} events={data.events} />
      </div>

      <ValueChart id={id} />

      <EarningsPerDayPanel id={id} />

      <EventsPanel position={p} events={data.events} />
    </div>
  );
}

function RangePanel({ position: p }: { position: PositionDto }) {
  const [inverted, setInverted] = useState(false);
  if (!p.range)
    return (
      <Panel title="Price range">
        <Empty>No range data</Empty>
      </Panel>
    );

  const range = inverted ? invertRange(p.range) : p.range;
  const [base, quote] = inverted ? [p.token1, p.token0] : [p.token0, p.token1];

  return (
    <Panel
      title="Price range"
      actions={
        <button
          className="text-xs text-sky-400 hover:underline"
          onClick={() => setInverted(!inverted)}
        >
          Show {quote?.symbol} price
        </button>
      }
    >
      <div className="mb-3 flex items-center justify-between text-sm">
        <span className="text-slate-400">
          {quote?.symbol} per {base?.symbol}
        </span>
        <RangeBadge inRange={range.inRange} closed={p.status === "Closed"} />
      </div>
      <RangeBar range={range} />
      <dl className="mt-4 grid grid-cols-3 gap-2 text-sm num">
        <div>
          <dt className="text-xs text-slate-500">Min</dt>
          <dd>{formatPrice(range.priceLower)}</dd>
        </div>
        <div>
          <dt className="text-xs text-slate-500">Current</dt>
          <dd>{formatPrice(range.priceCurrent)}</dd>
        </div>
        <div>
          <dt className="text-xs text-slate-500">Max</dt>
          <dd>{formatPrice(range.priceUpper)}</dd>
        </div>
        <div>
          <dt className="text-xs text-slate-500">To min</dt>
          <dd>{formatPercent(range.distanceToLowerPercent)}</dd>
        </div>
        <div>
          <dt className="text-xs text-slate-500">Ticks</dt>
          <dd>
            {p.tickLower} → {p.tickUpper}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-slate-500">To max</dt>
          <dd>{formatPercent(range.distanceToUpperPercent)}</dd>
        </div>
      </dl>
    </Panel>
  );
}

/** Index of the token whose price is worth showing: the non-stable side, else token0. */
function pricedTokenIndex(events: PositionEventDto[]): 0 | 1 {
  const isStable = (price: number | null) =>
    price != null && Math.abs(price - 1) < 0.03;
  const e = events.find((x) => x.price0Usd != null && x.price1Usd != null);
  return e && isStable(e.price0Usd) && !isStable(e.price1Usd) ? 1 : 0;
}

/** USD-value-weighted average token price over events of one type. */
function averagePrice(
  events: PositionEventDto[],
  type: PositionEventDto["type"],
  index: 0 | 1,
): { price: number; count: number } | null {
  let weighted = 0;
  let weights = 0;
  let count = 0;
  for (const e of events) {
    const price = index === 0 ? e.price0Usd : e.price1Usd;
    if (e.type !== type || price == null) continue;
    const weight = e.valueUsd && e.valueUsd > 0 ? e.valueUsd : 1;
    weighted += price * weight;
    weights += weight;
    count++;
  }
  return count ? { price: weighted / weights, count } : null;
}

function PriceNote({
  symbol,
  at,
  baseline,
}: {
  symbol: string;
  at: { price: number; count: number } | null;
  baseline?: number;
}) {
  if (!at) return null;
  const change = baseline ? ((at.price - baseline) / baseline) * 100 : null;
  return (
    <div className="text-[11px] text-slate-500">
      {symbol} {at.count > 1 ? "avg " : "@ "}
      <span className="num text-slate-300">{formatUsd(at.price)}</span>
      {change != null && (
        <span className={`num ${pnlClass(change)}`}>
          {" "}
          ({formatPercent(change, true)})
        </span>
      )}
    </div>
  );
}

function BreakdownPanel({
  position: p,
  events,
}: {
  position: PositionDto;
  events: PositionEventDto[];
}) {
  const m = p.metrics;
  const s0 = p.token0?.symbol ?? "token0";
  const s1 = p.token1?.symbol ?? "token1";
  const priced = pricedTokenIndex(events);
  const pricedSymbol = priced === 0 ? s0 : s1;
  const depositPrice = averagePrice(events, "IncreaseLiquidity", priced);
  const withdrawPrice = averagePrice(events, "DecreaseLiquidity", priced);

  const rows: [string, string, ReactNode?][] = [
    [
      "Deposited",
      formatUsd(m.depositedUsd),
      <>
        {formatAmount(m.deposited0)} {s0} + {formatAmount(m.deposited1)} {s1}
        <PriceNote symbol={pricedSymbol} at={depositPrice} />
      </>,
    ],
    [
      "Withdrawn",
      formatUsd(m.withdrawnUsd),
      <>
        {formatAmount(m.withdrawn0)} {s0} + {formatAmount(m.withdrawn1)} {s1}
        <PriceNote
          symbol={pricedSymbol}
          at={withdrawPrice}
          baseline={depositPrice?.price}
        />
      </>,
    ],
    [
      "Current",
      formatUsd(m.currentValueUsd),
      `${formatAmount(p.amount0)} ${s0} + ${formatAmount(p.amount1)} ${s1}`,
    ],
    [
      "Fees claimed",
      formatUsd(m.collectedFeesUsd),
      `${formatAmount(m.collectedFee0)} ${s0} + ${formatAmount(m.collectedFee1)} ${s1}`,
    ],
    [
      "Fees unclaimed",
      formatUsd(m.uncollectedFeesUsd),
      `${formatAmount(p.uncollected0)} ${s0} + ${formatAmount(p.uncollected1)} ${s1}`,
    ],
    [
      "Rewards",
      formatUsd(m.claimedRewardsUsd + m.pendingRewardsUsd),
      p.rewardTokenSymbol
        ? `${formatAmount(p.pendingReward)} ${p.rewardTokenSymbol} pending`
        : undefined,
    ],
    ["Gas", formatUsd(-m.gasUsd)],
    [
      "HODL value",
      formatUsd(m.hodlValueUsd),
      "deposited tokens at current/close prices",
    ],
  ];

  return (
    <Panel title="P/L breakdown">
      <table className="w-full text-sm">
        <tbody className="divide-y divide-slate-800">
          {rows.map(([label, usd, detail]) => (
            <tr key={label}>
              <td className="py-1.5 text-slate-400">{label}</td>
              <td className="py-1.5 text-xs text-slate-500">{detail}</td>
              <td className="py-1.5 text-right num">{usd}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {m.hasMissingPrices && (
        <p className="mt-2 text-xs text-amber-400">
          Some historical prices are missing; USD figures may be understated.
        </p>
      )}
    </Panel>
  );
}

function ValueChart({ id }: { id: number }) {
  const { data } = useSnapshots(id);
  const points = (data ?? []).map((s: SnapshotPointDto) => ({
    ...s,
    t: new Date(s.timestamp).getTime(),
  }));

  return (
    <Panel title="Value over time">
      {points.length < 2 ? (
        <Empty>Not enough price history for this position yet.</Empty>
      ) : (
        <div className="h-72">
          <ResponsiveContainer>
            <LineChart data={points}>
              <CartesianGrid stroke="#1e293b" />
              <XAxis
                dataKey="t"
                type="number"
                domain={["dataMin", "dataMax"]}
                scale="time"
                tickFormatter={(t) => new Date(t).toLocaleDateString()}
                stroke="#64748b"
                fontSize={11}
              />
              <YAxis
                stroke="#64748b"
                fontSize={11}
                tickFormatter={(v) => formatUsd(v)}
                width={80}
              />
              <Tooltip
                contentStyle={{
                  background: "#0f172a",
                  border: "1px solid #334155",
                }}
                labelFormatter={(t) => new Date(t as number).toLocaleString()}
                formatter={(v) => formatUsd(v as number)}
              />
              <Legend />
              <Line
                type="monotone"
                dataKey="valueUsd"
                name="Position"
                stroke="#38bdf8"
                dot={false}
              />
              <Line
                type="monotone"
                dataKey="valueWithFeesUsd"
                name="Position + fees"
                stroke="#34d399"
                dot={false}
              />
              <Line
                type="monotone"
                dataKey="hodlValueUsd"
                name="HODL"
                stroke="#f59e0b"
                dot={false}
                strokeDasharray="4 4"
              />
            </LineChart>
          </ResponsiveContainer>
        </div>
      )}
    </Panel>
  );
}

/** Daily change in cumulative fees + rewards (claimed and unclaimed), bucketed by local day. */
function dailyEarnings(points: SnapshotPointDto[]) {
  const lastByDay = new Map<string, number>();
  for (const s of points) {
    const day = new Date(s.timestamp).toLocaleDateString("en-CA");
    lastByDay.set(day, s.valueWithFeesUsd - s.valueUsd);
  }
  let prev = 0;
  return [...lastByDay].map(([day, cumulative]) => {
    const earned = cumulative - prev;
    prev = cumulative;
    return { day, earned };
  });
}

function EarningsPerDayPanel({ id }: { id: number }) {
  const { data } = useSnapshots(id);
  const days = dailyEarnings(data ?? []);
  const total = days.reduce((sum, d) => sum + d.earned, 0);
  const avg = days.length ? total / days.length : 0;

  return (
    <Panel
      title="Earnings per day"
      actions={
        days.length > 0 && (
          <span className="text-xs text-slate-400">
            avg <span className="num text-slate-200">{formatUsd(avg)}</span>
            /day
          </span>
        )
      }
    >
      {days.length === 0 ? (
        <Empty>No earnings data yet.</Empty>
      ) : (
        <div className="h-56">
          <ResponsiveContainer>
            <BarChart data={days}>
              <CartesianGrid stroke="#1e293b" vertical={false} />
              <XAxis dataKey="day" stroke="#64748b" fontSize={11} />
              <YAxis
                stroke="#64748b"
                fontSize={11}
                tickFormatter={(v) => formatUsd(v)}
                width={80}
              />
              <Tooltip
                cursor={{ fill: "#1e293b" }}
                contentStyle={{
                  background: "#0f172a",
                  border: "1px solid #334155",
                }}
                formatter={(v) => formatUsd(v as number)}
              />
              <Bar dataKey="earned" name="Earned" fill="#34d399" />
            </BarChart>
          </ResponsiveContainer>
        </div>
      )}
    </Panel>
  );
}

const eventLabels: Record<PositionEventDto["type"], string> = {
  IncreaseLiquidity: "Deposit",
  DecreaseLiquidity: "Withdraw",
  Collect: "Collect",
  RewardClaim: "Reward claim",
};

function EventsPanel({
  position: p,
  events,
}: {
  position: PositionDto;
  events: PositionEventDto[];
}) {
  const s0 = p.token0?.symbol;
  const s1 = p.token1?.symbol;

  return (
    <Panel title="History">
      {events.length === 0 ? (
        <Empty>No events.</Empty>
      ) : (
        <table className="w-full text-sm">
          <thead className="text-left text-xs uppercase text-slate-500">
            <tr>
              <th className="py-1">Time</th>
              <th className="py-1">Type</th>
              <th className="py-1">Amounts</th>
              <th className="py-1 text-right">USD (at time)</th>
              <th className="py-1 text-right">Gas</th>
              <th className="py-1 text-right">Tx</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-800">
            {events.map((e) => (
              <tr key={e.id}>
                <td className="py-1.5 text-slate-400">
                  {formatDate(e.timestamp)}
                </td>
                <td className="py-1.5">{eventLabels[e.type]}</td>
                <td className="py-1.5 num text-slate-300">
                  {e.type === "RewardClaim" ? (
                    `${formatAmount(e.rewardAmount)} ${e.rewardTokenSymbol ?? ""}`
                  ) : e.type === "Collect" ? (
                    <>
                      fees {formatAmount(e.fee0)} {s0} + {formatAmount(e.fee1)}{" "}
                      {s1}
                      {(e.principal0 > 0 || e.principal1 > 0) && (
                        <span className="text-slate-500">
                          {" "}
                          · principal {formatAmount(e.principal0)} {s0} +{" "}
                          {formatAmount(e.principal1)} {s1}
                        </span>
                      )}
                    </>
                  ) : (
                    `${formatAmount(e.amount0)} ${s0} + ${formatAmount(e.amount1)} ${s1}`
                  )}
                </td>
                <td className="py-1.5 text-right num">
                  {formatUsd(e.valueUsd)}
                </td>
                <td className="py-1.5 text-right num text-slate-400">
                  {e.gasUsd ? formatUsd(e.gasUsd) : "—"}
                </td>
                <td className="py-1.5 text-right">
                  <a
                    className="text-sky-400 hover:underline"
                    href={txUrl(p.chain, e.txHash)}
                    target="_blank"
                    rel="noreferrer"
                  >
                    {e.txHash.slice(0, 10)}…
                  </a>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Panel>
  );
}
