import type {
  DailyEarningsDto,
  PositionDto,
  PositionEarningsDto,
  WeeklyClaimsDto,
} from "../api/types";
import { UNCLAIMED_ALERT_USD } from "./attention";
import { formatAmount, formatUsd } from "./format";

/**
 * Calendar maths on the server's local dates ("YYYY-MM-DD", already in the user's time zone).
 * Dates are compared as strings and shifted through UTC so the browser zone and DST never move a day.
 */
const DAY_MS = 86_400_000;
const toMs = (d: string) => Date.parse(`${d}T00:00:00Z`);
export const addDays = (d: string, n: number) =>
  new Date(toMs(d) + n * DAY_MS).toISOString().slice(0, 10);
const monthStartOf = (d: string) => `${d.slice(0, 7)}-01`;
const weekStartOf = (d: string) => addDays(d, -((new Date(toMs(d)).getUTCDay() + 6) % 7));
const daysInMonth = (d: string) =>
  new Date(Date.UTC(Number(d.slice(0, 4)), Number(d.slice(5, 7)), 0)).getUTCDate();

export const shortDate = (d: string) =>
  new Date(toMs(d)).toLocaleDateString(undefined, { month: "short", day: "numeric", timeZone: "UTC" });

/** Local calendar day of an ISO timestamp, in the browser's zone (the zone the daily series is bucketed in). */
export const localDay = (iso: string) => new Date(iso).toLocaleDateString("en-CA");

export interface Window {
  total: number;
  fees: number;
  rewards: number;
  /** Days of the series inside the window (less than its length when tracking started later). */
  days: number;
}

/** Sums `from`..`to` inclusive. */
export function windowOf(days: DailyEarningsDto[], from: string, to: string): Window {
  const w: Window = { total: 0, fees: 0, rewards: 0, days: 0 };
  for (const d of days) {
    if (d.date < from || d.date > to) continue;
    w.total += d.totalUsd;
    w.fees += d.feesUsd;
    w.rewards += d.rewardsUsd;
    w.days += 1;
  }
  return w;
}

const pctChange = (current: number, prior: number | null) =>
  prior != null && prior > 0 ? ((current - prior) / prior) * 100 : null;

export interface Headline {
  key: string;
  label: string;
  aside: string;
  total: number;
  fees: number;
  rewards: number;
  /** Null when there is no comparable earlier period yet. */
  changePct: number | null;
  changeLabel: string;
  /** Extra reference line under the change, e.g. the average full week. */
  detail?: string;
  /** False when the period has no tracked days at all. */
  available: boolean;
}

export interface Headlines {
  today: string;
  cards: Headline[];
  /** Month-end estimate from the completed days of this month; null on the 1st. */
  projection: number | null;
  monthName: string;
}

/** `days` is newest first and runs up to today, as the API returns it. */
export function headlines(days: DailyEarningsDto[]): Headlines {
  const today = days[0].date;
  const yesterday = addDays(today, -1);

  const yest = windowOf(days, yesterday, yesterday);
  const prior7 = windowOf(days, addDays(today, -8), addDays(today, -2));

  const last30 = windowOf(days, addDays(today, -30), yesterday);
  const prior30 = windowOf(days, addDays(today, -60), addDays(today, -31));

  // This week runs Monday to today. It is compared with the same Mondays-to-yesterday of the last 4 weeks,
  // so a half-finished week is not set against whole ones; the average full week is shown beside it.
  const thisMonday = weekStartOf(today);
  const week = windowOf(days, thisMonday, today);
  const elapsed = Math.round((toMs(today) - toMs(thisMonday)) / DAY_MS); // completed days this week
  const weekDone = windowOf(days, thisMonday, yesterday);
  const priorWeeks = [1, 2, 3, 4].map((k) => {
    const from = addDays(thisMonday, -7 * k);
    return {
      full: windowOf(days, from, addDays(from, 6)),
      same: elapsed > 0 ? windowOf(days, from, addDays(from, elapsed - 1)) : null,
    };
  });
  const mean = (values: number[]) => (values.length ? values.reduce((s, v) => s + v, 0) / values.length : null);
  const fullWeeks = priorWeeks.filter((w) => w.full.days === 7).map((w) => w.full.total);
  const avgWeek = fullWeeks.length >= 2 ? mean(fullWeeks) : null;
  const sameDays = priorWeeks.filter((w) => w.same && w.same.days === elapsed).map((w) => w.same!.total);
  const weekPace = elapsed > 0 && weekDone.days === elapsed && sameDays.length >= 2 ? mean(sameDays) : null;

  const monthStart = monthStartOf(today);
  const month = windowOf(days, monthStart, today);
  const prevMonthStart = monthStartOf(addDays(monthStart, -1));
  const dayOfMonth = Number(today.slice(8, 10));
  const prevMonthEnd = addDays(monthStart, -1);
  const samePoint = addDays(prevMonthStart, dayOfMonth - 1);
  const monthPrior = windowOf(days, prevMonthStart, samePoint < prevMonthEnd ? samePoint : prevMonthEnd);

  const avg = (w: Window) => (w.days > 0 ? w.total / w.days : null);
  const completed = windowOf(days, monthStart, yesterday);
  const projection = completed.days > 0 ? (completed.total / completed.days) * daysInMonth(today) : null;

  const cards: Headline[] = [
    {
      key: "yesterday",
      label: "Yesterday",
      aside: shortDate(yesterday),
      total: yest.total,
      fees: yest.fees,
      rewards: yest.rewards,
      changePct: pctChange(yest.total, prior7.days >= 3 ? prior7.total / prior7.days : null),
      changeLabel: "vs prior 7-day avg",
      available: yest.days > 0,
    },
    {
      key: "avg",
      label: "Avg / day",
      aside: last30.days >= 30 ? "last 30 days" : `last ${last30.days} days`,
      total: avg(last30) ?? 0,
      fees: last30.days > 0 ? last30.fees / last30.days : 0,
      rewards: last30.days > 0 ? last30.rewards / last30.days : 0,
      changePct: pctChange(avg(last30) ?? 0, prior30.days >= 7 ? avg(prior30) : null),
      changeLabel: "vs previous 30 days",
      available: last30.days > 0,
    },
    {
      key: "week",
      label: "This week",
      aside: `${shortDate(thisMonday)} – today`,
      total: week.total,
      fees: week.fees,
      rewards: week.rewards,
      changePct: pctChange(weekDone.total, weekPace),
      changeLabel: "vs 4-week avg, same days",
      detail: avgWeek != null ? `Average week: ${formatUsd(avgWeek)}` : undefined,
      available: week.days > 0,
    },
    {
      key: "month",
      label: "This month",
      aside: `${shortDate(monthStart)} – today`,
      total: month.total,
      fees: month.fees,
      rewards: month.rewards,
      changePct: pctChange(month.total, monthPrior.days >= dayOfMonth ? monthPrior.total : null),
      changeLabel: "vs same days last month",
      available: month.days > 0,
    },
  ];

  return {
    today,
    cards,
    projection,
    monthName: new Date(toMs(monthStart)).toLocaleDateString(undefined, { month: "long", timeZone: "UTC" }),
  };
}

const median = (values: number[]) => {
  const s = [...values].sort((a, b) => a - b);
  const mid = s.length >> 1;
  return s.length % 2 ? s[mid] : (s[mid - 1] + s[mid]) / 2;
};

/** Completed days earning under half of the median of the 14 days before them (needs a week of history). */
export function lowDays(days: DailyEarningsDto[]): Map<string, { total: number; median: number }> {
  const completed = days.slice(1).reverse(); // oldest first, without today
  const low = new Map<string, { total: number; median: number }>();
  for (let i = 7; i < completed.length; i++) {
    const before = completed.slice(Math.max(0, i - 14), i).map((d) => d.totalUsd);
    const m = median(before);
    if (m > 0 && completed[i].totalUsd < m * 0.5) {
      low.set(completed[i].date, { total: completed[i].totalUsd, median: m });
    }
  }
  return low;
}

export type Grain = "day" | "week" | "month";

/** How many finished buckets the chart's average line spans, and what to call it. */
export const grainAverage: Record<Grain, { length: number; label: string }> = {
  day: { length: 7, label: "7-day average" },
  week: { length: 4, label: "4-week average" },
  month: { length: 3, label: "3-month average" },
};

export interface ChartPoint {
  /** First day of the bucket: the day itself, the week's Monday, or the 1st of the month. */
  date: string;
  /** Last day of the bucket, for tooltips. */
  end: string;
  fees: number;
  rewards: number;
  total: number;
  /** Average of the finished buckets ending here; null for the current bucket and the first few. */
  avg: number | null;
  /** The bucket that contains today, still filling up. */
  partial: boolean;
  low: boolean;
}

/** Oldest-first series for the chart, summed per day, week (Monday start) or month; `range` buckets, 0 means everything. */
export function chartSeries(days: DailyEarningsDto[], grain: Grain, range: number): ChartPoint[] {
  const oldest = [...days].reverse();
  const low = grain === "day" ? lowDays(days) : new Map<string, unknown>();
  const startOf = (d: string) => (grain === "day" ? d : grain === "week" ? weekStartOf(d) : monthStartOf(d));
  const endOf = (start: string) =>
    grain === "day" ? start : grain === "week" ? addDays(start, 6) : addDays(monthStartOf(addDays(start, 32)), -1);

  const buckets: ChartPoint[] = [];
  for (const d of oldest) {
    const start = startOf(d.date);
    let b = buckets[buckets.length - 1];
    if (!b || b.date !== start) {
      b = { date: start, end: endOf(start), fees: 0, rewards: 0, total: 0, avg: null, partial: false, low: low.has(start) };
      buckets.push(b);
    }
    b.fees += d.feesUsd;
    b.rewards += d.rewardsUsd;
    b.total += d.totalUsd;
  }
  if (buckets.length) buckets[buckets.length - 1].partial = true; // the bucket holding today

  const { length } = grainAverage[grain];
  buckets.forEach((b, i) => {
    if (!b.partial && i >= length - 1) {
      b.avg = buckets.slice(i - length + 1, i + 1).reduce((s, x) => s + x.total, 0) / length;
    }
  });
  return range > 0 ? buckets.slice(-range) : buckets;
}

export interface TokenAmount {
  amount: number;
  usd: number;
}

export interface TokenRow {
  key: string;
  symbol: string;
  kind: "fee" | "reward";
  yesterday: TokenAmount;
  last7: TokenAmount;
  month: TokenAmount;
  /** Share of this month's earnings, 0..1. */
  share: number;
}

/** Earnings per token over yesterday, the 7 days before today, and this month. */
export function tokenRows(days: DailyEarningsDto[]): TokenRow[] {
  const today = days[0].date;
  const yesterday = addDays(today, -1);
  const from7 = addDays(today, -7);
  const monthStart = monthStartOf(today);
  const rows = new Map<string, TokenRow>();

  for (const d of days) {
    const inMonth = d.date >= monthStart;
    const in7 = d.date >= from7 && d.date <= yesterday;
    const isYesterday = d.date === yesterday;
    if (!inMonth && !in7) continue;

    for (const t of d.tokens ?? []) {
      const key = `${t.kind}:${(t.address ?? t.symbol).toLowerCase()}`;
      let row = rows.get(key);
      if (!row) {
        row = {
          key,
          symbol: t.symbol,
          kind: t.kind,
          yesterday: { amount: 0, usd: 0 },
          last7: { amount: 0, usd: 0 },
          month: { amount: 0, usd: 0 },
          share: 0,
        };
        rows.set(key, row);
      }
      const add = (a: TokenAmount) => {
        a.amount += t.amount;
        a.usd += t.usd;
      };
      if (isYesterday) add(row.yesterday);
      if (in7) add(row.last7);
      if (inMonth) add(row.month);
    }
  }

  const list = [...rows.values()];
  const monthTotal = list.reduce((s, r) => s + r.month.usd, 0);
  for (const r of list) r.share = monthTotal > 0 ? r.month.usd / monthTotal : 0;
  return list.sort((a, b) => b.month.usd - a.month.usd);
}

/** One claim transaction: a staked claim can emit several events, so they are folded by hash. */
export interface ClaimTx {
  txHash: string;
  timestamp: string;
  usd: number;
  gasUsd: number;
  /** Amounts (e.g. "41.03 AERO") that arrived without a price. Dust is left out. */
  unpriced: string[];
  /** What each position received in this transaction, to judge a claim against that position's own history. */
  parts: { positionId: number; label: string; usd: number }[];
}

export function claimTxs(weeks: WeeklyClaimsDto[]): ClaimTx[] {
  const byTx = new Map<string, ClaimTx>();
  for (const w of weeks) {
    for (const i of w.items) {
      const tx = byTx.get(i.txHash) ?? {
        txHash: i.txHash,
        timestamp: i.timestamp,
        usd: 0,
        gasUsd: 0,
        unpriced: [],
        parts: [],
      };
      tx.usd += i.totalUsd;
      const part = tx.parts.find((p) => p.positionId === i.positionId);
      if (part) part.usd += i.totalUsd;
      else tx.parts.push({ positionId: i.positionId, label: `${i.pair} #${i.tokenId}`, usd: i.totalUsd });
      // The gas belongs to the transaction, not to each event in it.
      tx.gasUsd = Math.max(tx.gasUsd, i.gasUsd);
      if (i.hasMissingPrices) {
        // A few thousandths of a token left over from a collect is not worth chasing a price for.
        const amounts = [
          { amount: i.rewardAmount, symbol: i.rewardTokenSymbol },
          { amount: i.fee0, symbol: i.token0Symbol },
          { amount: i.fee1, symbol: i.token1Symbol },
        ];
        for (const a of amounts) {
          if (a.amount >= DUST_TOKEN_AMOUNT) tx.unpriced.push(`${formatAmount(a.amount)} ${a.symbol ?? ""}`.trim());
        }
      }
      byTx.set(i.txHash, tx);
    }
  }
  return [...byTx.values()].sort((a, b) => b.timestamp.localeCompare(a.timestamp));
}

/** Share of a claim's value that gas may take before the claim looks wasteful. */
const GAS_SHARE_LIMIT = 0.15;
/** A claim this many times the median claim of the same position is unusual. */
const LARGE_CLAIM_FACTOR = 3;
/** Claims a position needs before its own median means anything. */
const MIN_CLAIMS_FOR_TYPICAL = 5;
const CLAIM_WINDOW_DAYS = 30;
const DUST_CLAIM_USD = 1;
const DUST_TOKEN_AMOUNT = 0.001;

/** Claims in the last 30 days that look off, keyed by transaction hash. */
export function claimFlags(
  claims: ClaimTx[],
  usd: (n: number) => string,
  today: string,
): Map<string, string> {
  const flags = new Map<string, string>();
  // "Typical" is per position: a wallet with more capital claims more, and that is not strange.
  const history = new Map<number, number[]>();
  for (const c of claims) {
    // Leftover dust claims would drag the median to zero and make every normal claim look huge.
    for (const p of c.parts.filter((x) => x.usd >= DUST_CLAIM_USD)) {
      history.set(p.positionId, [...(history.get(p.positionId) ?? []), p.usd]);
    }
  }
  const typical = (positionId: number) => {
    const list = history.get(positionId) ?? [];
    return list.length >= MIN_CLAIMS_FOR_TYPICAL ? median(list) : null;
  };
  const since = addDays(today, -(CLAIM_WINDOW_DAYS - 1));
  for (const c of claims) {
    if (localDay(c.timestamp) < since) continue;
    if (c.unpriced.length) flags.set(c.txHash, `(${c.unpriced.join(" + ")}) has no price, so its value is missing`);
    // Dust claims (a few cents left over after closing a position) are not worth a warning.
    else if (c.usd < DUST_CLAIM_USD) continue;
    else if (c.gasUsd / c.usd > GAS_SHARE_LIMIT)
      flags.set(c.txHash, `paid ${usd(c.gasUsd)} gas on ${usd(c.usd)} (${Math.round((c.gasUsd / c.usd) * 100)}%)`);
    else {
      for (const p of c.parts) {
        const t = typical(p.positionId);
        if (t != null && t > 0 && p.usd >= DUST_CLAIM_USD && p.usd > t * LARGE_CLAIM_FACTOR) {
          flags.set(c.txHash, `is ${(p.usd / t).toFixed(1)}x a typical claim for ${p.label} (${usd(p.usd)})`);
          break;
        }
      }
    }
  }
  return flags;
}

export interface EarningsAlert {
  id: string;
  severity: "warn" | "info";
  label: string;
  text: string;
  positionId?: number;
}

const MIN_OPEN_DAYS = 3;

export function buildEarningsAlerts(input: {
  days: DailyEarningsDto[];
  byPosition: PositionEarningsDto[];
  positions: PositionDto[];
  claims: ClaimTx[];
  flags: Map<string, string>;
  usd: (n: number) => string;
}): EarningsAlert[] {
  const { days, byPosition, positions, claims, flags, usd } = input;
  const today = days[0].date;
  const alerts: EarningsAlert[] = [];
  const open = positions.filter((p) => p.status === "Open");
  const byId = new Map(open.map((p) => [p.id, p]));

  // Open positions that produced nothing for the last two completed days.
  for (const e of byPosition) {
    const p = byId.get(e.positionId);
    if (!p) continue;
    const age = p.openedAt ? (Date.parse(`${today}T00:00:00Z`) - Date.parse(p.openedAt)) / DAY_MS : Infinity;
    if (age < MIN_OPEN_DAYS) continue;
    if (e.lastEarnedDate != null && e.lastEarnedDate >= addDays(today, -2)) continue;
    const why = p.range?.inRange === false ? " It is out of range." : "";
    alerts.push({
      id: `idle-${e.positionId}`,
      severity: "warn",
      label: "Not earning",
      positionId: e.positionId,
      text: `${e.pair} #${e.tokenId} has earned nothing${e.lastEarnedDate ? ` since ${shortDate(e.lastEarnedDate)}` : " yet"}.${why}`,
    });
  }

  // The most recent unusually low day within the last week.
  const weekAgo = addDays(today, -7);
  const lows = [...lowDays(days)].filter(([date]) => date >= weekAgo).sort((a, b) => b[0].localeCompare(a[0]));
  if (lows.length) {
    const [date, v] = lows[0];
    alerts.push({
      id: "low-day",
      severity: "warn",
      label: "Low day",
      text: `${shortDate(date)} earned ${usd(v.total)}, ${Math.round((1 - v.total / v.median) * 100)}% below the median of the 14 days before it (${usd(v.median)}).`,
    });
  }

  if (days.slice(1, 8).some((d) => d.hasMissingPrices)) {
    alerts.push({
      id: "missing-prices",
      severity: "warn",
      label: "Missing price",
      text: "Some earnings in the last 7 days have no price yet, so those days read lower than they were.",
    });
  }

  // Strange claims; several unpriced ones become one alert, since they usually share a cause.
  const unpriced = claims.filter((c) => c.unpriced.length > 0 && flags.has(c.txHash));
  const groupUnpriced = unpriced.length > 1;
  if (groupUnpriced) {
    alerts.push({
      id: "claims-unpriced",
      severity: "warn",
      label: "Check claim",
      text: `${unpriced.length} claims in the last 30 days have no price, so their value is missing.`,
    });
  }
  for (const c of claims) {
    const why = flags.get(c.txHash);
    if (why && !(groupUnpriced && c.unpriced.length > 0)) {
      alerts.push({
        id: `claim-${c.txHash}`,
        severity: "warn",
        label: "Check claim",
        text: `The claim on ${shortDate(localDay(c.timestamp))} ${why}.`,
      });
    }
  }

  // Money waiting to be claimed.
  const waiting = open.reduce((s, p) => s + p.metrics.uncollectedFeesUsd + p.metrics.pendingRewardsUsd, 0);
  if (waiting >= UNCLAIMED_ALERT_USD) {
    const gas = claims.map((c) => c.gasUsd).filter((g) => g > 0);
    const typicalGas = gas.length ? median(gas) : null;
    const gasText = typicalGas != null ? ` A claim costs about ${usd(typicalGas)} in gas (${((typicalGas / waiting) * 100).toFixed(1)}%).` : "";
    alerts.push({
      id: "waiting",
      severity: "info",
      label: "Tip",
      text: `${usd(waiting)} is waiting to be claimed.${gasText}`,
    });
  }

  return alerts;
}

export interface ClaimHealthStats {
  earned: number;
  claimed: number;
  claimCount: number;
  waiting: number;
  waitingByToken: { symbol: string; amount: number }[];
  gas: number;
}

/** Earned vs claimed over the last 30 days, plus what is still waiting now. */
export function claimHealth(days: DailyEarningsDto[], claims: ClaimTx[], positions: PositionDto[]): ClaimHealthStats {
  const today = days[0].date;
  const from = addDays(today, -(CLAIM_WINDOW_DAYS - 1));
  const earned = windowOf(days, from, today).total;
  const recent = claims.filter((c) => localDay(c.timestamp) >= from);
  const open = positions.filter((p) => p.status === "Open");

  const tokens = new Map<string, number>();
  for (const p of open) {
    if (p.pendingReward > 0) {
      const s = p.rewardTokenSymbol ?? "reward";
      tokens.set(s, (tokens.get(s) ?? 0) + p.pendingReward);
    }
  }

  return {
    earned,
    claimed: recent.reduce((s, c) => s + c.usd, 0),
    claimCount: recent.length,
    waiting: open.reduce((s, p) => s + p.metrics.uncollectedFeesUsd + p.metrics.pendingRewardsUsd, 0),
    waitingByToken: [...tokens].map(([symbol, amount]) => ({ symbol, amount })),
    gas: recent.reduce((s, c) => s + c.gasUsd, 0),
  };
}
