import type {
  MarketBufferLevel,
  MarketEventDto,
  MarketStressStatus,
  MarketTag,
  MarketWatchDto,
} from "../api/types";

const HOUR = 3_600_000;
const DAY = 24 * HOUR;

/** "1d 23h", "5h 12m", "42m"; "now" once the time has come. */
export function countdownLabel(targetMs: number, nowMs: number): string {
  const diff = targetMs - nowMs;
  if (diff <= 0) return "now";
  const days = Math.floor(diff / DAY);
  const hours = Math.floor((diff % DAY) / HOUR);
  const minutes = Math.floor((diff % HOUR) / 60_000);
  if (days > 0) return `${days}d ${hours}h`;
  if (hours > 0) return `${hours}h ${minutes}m`;
  return `${Math.max(1, minutes)}m`;
}

/** The countdown as number and unit pairs, so the page can set the units smaller than the numbers. */
export function countdownParts(targetMs: number, nowMs: number): { value: number; unit: string }[] {
  const diff = Math.max(0, targetMs - nowMs);
  const days = Math.floor(diff / DAY);
  const hours = Math.floor((diff % DAY) / HOUR);
  const minutes = Math.floor((diff % HOUR) / 60_000);
  if (days > 0) return [{ value: days, unit: "d" }, { value: hours, unit: "h" }];
  if (hours > 0) return [{ value: hours, unit: "h" }, { value: minutes, unit: "m" }];
  return [{ value: Math.max(1, minutes), unit: "m" }];
}

// ---- Words and colours ---------------------------------------------------------------------------------------------

export const TAG_CLASS: Record<MarketTag, string> = {
  RiskOn: "bg-up-fill/15 text-up",
  RiskOff: "bg-down-fill/15 text-down",
  Neutral: "bg-raised text-muted",
};

export const TAG_TOOLTIP =
  "Actual against forecast. Risk-on means markets usually read it as good for crypto, risk-off as bad. It describes the result, not what happens next.";

const INFLATION = /\b(CPI|PCE|PPI|Producer Price)/i;
const RATE_DECISION = /Fed Interest Rate Decision/i;

/** What the tag says in plain words: how the result compared with the forecast. Null when the event has no tag. */
export function tagLabel(e: Pick<MarketEventDto, "tag" | "name">): string | null {
  if (!e.tag) return null;
  const inflation = INFLATION.test(e.name) && !RATE_DECISION.test(e.name);
  const [more, less, same] = inflation
    ? ["Hotter than forecast", "Cooler than forecast", "In line with forecast"]
    : RATE_DECISION.test(e.name)
      ? ["Higher than forecast", "Lower than forecast", "As expected"]
      : ["Above forecast", "Below forecast", "In line with forecast"];
  return e.tag === "RiskOff" ? more : e.tag === "RiskOn" ? less : same;
}

export const BUFFER_LABEL: Record<MarketBufferLevel, string> = { Comfortable: "Comfortable", Thin: "Thin", Tight: "Tight" };

export const STRESS_LABEL: Record<MarketStressStatus, string> = {
  Ok: "OK",
  Watch: "Getting close",
  MarginCall: "Margin call",
  Liquidation: "Liquidation",
};

export const STRESS_CLASS: Record<MarketStressStatus, string> = {
  Ok: "bg-up-fill/15 text-up",
  Watch: "bg-warn-fill/15 text-warn",
  MarginCall: "bg-down-fill/15 text-down",
  Liquidation: "bg-down-fill/15 text-down",
};

/** The headline word for the loan at a given fall. */
export const STRESS_HEADLINE: Record<MarketStressStatus, string> = {
  Ok: "Safe",
  Watch: "Getting close",
  MarginCall: "Margin call",
  Liquidation: "Liquidated",
};

/** The clock time of a macro event in the viewer's zone; earnings have a slot ("Before open"), not a time. */
export function eventTimeLabel(e: MarketEventDto): string {
  return e.timing ?? new Date(e.at).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

/** "Mon 17 Nov". */
export const dayLabel = (d: Date) => d.toLocaleDateString(undefined, { weekday: "short", day: "numeric", month: "short" });

/** "Previous 4.25% · forecast 4.00% · actual 4.25%", skipping what the source does not give. */
export function eventFigures(e: MarketEventDto): { label: string; value: string }[] {
  const out: { label: string; value: string }[] = [];
  if (e.category === "Earnings") {
    if (e.forecast) out.push({ label: "EPS forecast", value: e.forecast });
    return out;
  }
  if (e.previous) out.push({ label: "Before", value: e.previous });
  if (e.forecast) out.push({ label: "Forecast", value: e.forecast });
  if (e.actual) out.push({ label: "Result", value: e.actual });
  return out;
}

// ---- Why it matters and short names ----------------------------------------------------------------------------------

const WHY: [RegExp, string][] = [
  [RATE_DECISION, "The Fed's decision on interest rates. A result above or below forecast changes what every market expects next."],
  [/FOMC Minutes/i, "Notes from the last Fed meeting, published three weeks later. They show how split the Fed is on cutting rates."],
  [/FOMC Press Conference/i, "The Fed chair explaining the rate decision. Wording that sounds more cautious than expected has moved markets before."],
  [/Fed Chair/i, "The Fed chair speaking. A hint about rate cuts, or a change in tone, has moved markets before."],
  [/Core CPI|CPI/i, "Consumer price inflation. Hotter than forecast means fewer rate cuts are expected, which has tended to weigh on crypto."],
  [/Core PCE|PCE/i, "The inflation gauge the Fed watches most. Above forecast means rate cuts look less likely."],
  [/PPI|Producer Price/i, "Prices businesses pay at the factory gate. It feeds into consumer inflation a few weeks later."],
  [/Nonfarm Payrolls/i, "New jobs last month. Very strong or very weak numbers both change what the Fed is expected to do."],
  [/Unemployment Rate/i, "Share of people without a job. A sharp rise points to a weaker economy."],
  [/GDP/i, "The size of the economy. A big surprise or revision changes what the Fed is expected to do."],
  [/Retail Sales/i, "How much people spent in shops. Stronger than forecast can keep interest rates high for longer."],
  [/Jobless Claims/i, "New unemployment filings last week. A steady rise points to a weaker job market."],
  [/JOLTS/i, "Job openings. Fewer openings point to a cooling job market."],
  [/ISM/i, "A survey of company managers. Above 50 means the sector is growing, below 50 means it is shrinking."],
  [/Consumer Confidence|Michigan/i, "A survey of how households feel about the economy. It hints at how much they will spend."],
];

/** A plain sentence on why the event matters; null when there is nothing useful to say. */
export function whyItMatters(e: MarketEventDto): string | null {
  if (e.category === "Earnings") {
    return "A large company reporting. A big miss or a weak outlook can pull stocks down, and crypto has often followed the same day.";
  }
  return WHY.find(([re]) => re.test(e.name))?.[1] ?? null;
}

const SHORT: [RegExp, string][] = [
  [RATE_DECISION, "Fed rate"],
  [/FOMC Minutes/i, "Minutes"],
  [/FOMC Press Conference/i, "Fed presser"],
  [/Fed Chair/i, "Fed chair"],
  [/Core CPI/i, "Core CPI"],
  [/CPI/i, "CPI"],
  [/Core PCE/i, "Core PCE"],
  [/PCE/i, "PCE"],
  [/PPI|Producer Price/i, "PPI"],
  [/Nonfarm Payrolls/i, "Jobs"],
  [/Unemployment Rate/i, "Jobs rate"],
  [/GDP/i, "GDP"],
  [/Retail Sales/i, "Retail"],
  [/Jobless Claims/i, "Claims"],
  [/JOLTS/i, "JOLTS"],
  [/ISM/i, "ISM"],
  [/Consumer Confidence/i, "Confidence"],
  [/Michigan/i, "Michigan"],
];

/** A name of a few letters for the calendar strip. */
export function shortName(e: MarketEventDto): string {
  if (e.category === "Earnings") {
    // The event id ends with the ticker ("earn:2026-11-19:NVDA"), which reads better than a cut-off company name.
    const symbol = e.id.split(":")[2];
    return symbol || e.name.replace(/ earnings$/i, "").split(/[\s,]+/)[0] || "Earnings";
  }
  return SHORT.find(([re]) => re.test(e.name))?.[1] ?? e.name.split(" ")[0];
}

// ---- Days and the calendar strip -------------------------------------------------------------------------------------

/** The viewer's calendar day of a moment, as 2026-11-17. */
export function dayKey(d: Date): string {
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${d.getFullYear()}-${m}-${day}`;
}

export interface StripDay {
  key: string;
  date: Date;
  weekday: string;
  dayOfMonth: number;
  weekend: boolean;
  past: boolean;
  today: boolean;
  events: MarketEventDto[];
}

/** Three days back, today and the window ahead, each with its events (big ones first). */
export function stripDays(events: MarketEventDto[], windowDays: number, nowMs: number): StripDay[] {
  const today = new Date(nowMs);
  today.setHours(12, 0, 0, 0);
  const todayKey = dayKey(today);
  const byDay = new Map<string, MarketEventDto[]>();
  for (const e of events) {
    const k = dayKey(new Date(e.at));
    byDay.set(k, [...(byDay.get(k) ?? []), e]);
  }
  const out: StripDay[] = [];
  for (let i = -3; i <= windowDays; i++) {
    const date = new Date(today);
    date.setDate(today.getDate() + i);
    const key = dayKey(date);
    const dow = date.getDay();
    out.push({
      key,
      date,
      weekday: date.toLocaleDateString(undefined, { weekday: "short" }),
      dayOfMonth: date.getDate(),
      weekend: dow === 0 || dow === 6,
      past: i < 0,
      today: key === todayKey,
      events: [...(byDay.get(key) ?? [])].sort((a, b) => Number(b.impact === "High") - Number(a.impact === "High")),
    });
  }
  return out;
}

/** Groups already-sorted events by the viewer's calendar day, keeping their order. */
export function groupByDay(events: MarketEventDto[]): { key: string; date: Date; events: MarketEventDto[] }[] {
  const groups: { key: string; date: Date; events: MarketEventDto[] }[] = [];
  for (const e of events) {
    const date = new Date(e.at);
    const key = dayKey(date);
    const last = groups[groups.length - 1];
    if (last?.key === key) last.events.push(e);
    else groups.push({ key, date, events: [e] });
  }
  return groups;
}

// ---- Picking the next events -------------------------------------------------------------------------------------------

export const nextHighEvents = (upcoming: MarketEventDto[]) => upcoming.filter((e) => e.impact === "High");

/** A high-impact event that came out in the last 12 hours, newest first: worth saying before the next one. */
export const recentHighResult = (happened: MarketEventDto[], nowMs: number) =>
  happened.find((e) => e.impact === "High" && nowMs - new Date(e.at).getTime() <= 12 * HOUR) ?? null;

/** The value column of the loan: an LTV percent or a health factor. */
export const stressValue = (kind: "Ltv" | "HealthFactor", value: number) =>
  kind === "Ltv" ? `${value.toFixed(0)}%` : value.toFixed(2);

/** True when the loan buffer is not comfortable, so the page should say so at the top. */
export const loanNeedsWords = (data: MarketWatchDto): boolean =>
  data.loan != null && data.loan.stress.buffer !== "Comfortable";
