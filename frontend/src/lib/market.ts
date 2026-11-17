import type {
  MarketBufferLevel,
  MarketEventDto,
  MarketStressStatus,
  MarketTag,
  MarketWatchDto,
} from "../api/types";

const HOUR = 3_600_000;

/** "1d 23h", "5h 12m", "42m"; "now" once the time has come. */
export function countdownLabel(targetMs: number, nowMs: number): string {
  const diff = targetMs - nowMs;
  if (diff <= 0) return "now";
  const days = Math.floor(diff / (24 * HOUR));
  const hours = Math.floor((diff % (24 * HOUR)) / HOUR);
  const minutes = Math.floor((diff % HOUR) / 60_000);
  if (days > 0) return `${days}d ${hours}h`;
  if (hours > 0) return `${hours}h ${minutes}m`;
  return `${Math.max(1, minutes)}m`;
}

export const IMPACT_CLASS = {
  High: "bg-down-fill/15 text-down",
  Medium: "bg-warn-fill/15 text-warn",
} as const;

export const TAG_LABEL: Record<MarketTag, string> = { RiskOn: "Risk-on", RiskOff: "Risk-off", Neutral: "Neutral" };

export const TAG_CLASS: Record<MarketTag, string> = {
  RiskOn: "bg-up-fill/15 text-up",
  RiskOff: "bg-down-fill/15 text-down",
  Neutral: "bg-raised text-muted",
};

export const TAG_TOOLTIP = "From actual against forecast. Not a judgement of what happens next.";

export const BUFFER_LABEL: Record<MarketBufferLevel, string> = { Comfortable: "Comfortable", Thin: "Thin", Tight: "Tight" };

export const BUFFER_CLASS: Record<MarketBufferLevel, string> = {
  Comfortable: "bg-up-fill/15 text-up",
  Thin: "bg-warn-fill/15 text-warn",
  Tight: "bg-down-fill/15 text-down",
};

export const STRESS_LABEL: Record<MarketStressStatus, string> = {
  Ok: "OK",
  Watch: "Watch",
  MarginCall: "Margin call",
  Liquidation: "Liquidation",
};

export const STRESS_CLASS: Record<MarketStressStatus, string> = {
  Ok: "bg-up-fill/15 text-up",
  Watch: "bg-warn-fill/15 text-warn",
  MarginCall: "bg-down-fill/15 text-down",
  Liquidation: "bg-down-fill/15 text-down",
};

/** The clock time of a macro event in the viewer's zone; earnings have a slot ("Before open"), not a time. */
export function eventTimeLabel(e: MarketEventDto): string {
  return e.timing ?? new Date(e.at).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

const dayLabel = (iso: string) =>
  new Date(iso).toLocaleDateString(undefined, { weekday: "short", day: "numeric", month: "short" });

/** Groups already-sorted events by the viewer's calendar day, keeping their order. */
export function groupByDay(events: MarketEventDto[]): { day: string; events: MarketEventDto[] }[] {
  const groups: { day: string; events: MarketEventDto[] }[] = [];
  for (const e of events) {
    const day = dayLabel(e.at);
    const last = groups[groups.length - 1];
    if (last?.day === day) last.events.push(e);
    else groups.push({ day, events: [e] });
  }
  return groups;
}

/** "Previous 4.25% · forecast 4.00% · actual 4.25%", skipping what the source does not give. */
export function eventFigures(e: MarketEventDto): { label: string; value: string }[] {
  const out: { label: string; value: string }[] = [];
  if (e.category === "Earnings") {
    if (e.forecast) out.push({ label: "EPS forecast", value: e.forecast });
    return out;
  }
  if (e.previous) out.push({ label: "Previous", value: e.previous });
  if (e.forecast) out.push({ label: "Forecast", value: e.forecast });
  if (e.actual) out.push({ label: "Actual", value: e.actual });
  return out;
}

/** The value column of the stress table: an LTV percent or a health factor. */
export const stressValue = (kind: "Ltv" | "HealthFactor", value: number) =>
  kind === "Ltv" ? `${value.toFixed(0)}%` : value.toFixed(2);

export const nextHighEvents = (upcoming: MarketEventDto[]) => upcoming.filter((e) => e.impact === "High");

/** A high-impact event that came out in the last 12 hours, newest first: worth saying before the next one. */
export const recentHighResult = (happened: MarketEventDto[], nowMs: number) =>
  happened.find((e) => e.impact === "High" && nowMs - new Date(e.at).getTime() <= 12 * HOUR) ?? null;

export const reportedText = (e: MarketEventDto) =>
  e.category === "Earnings" ? `${e.name} reported.` : `${e.name} is out${e.tag ? `, tagged ${TAG_LABEL[e.tag].toLowerCase()}` : ""}.`;

export type BannerLevel = "info" | "warn" | "crit";

/** One sentence on what is next and what the loan can take. The colour follows the loan buffer, not the event. */
export function marketBanner(data: MarketWatchDto, nowMs: number): { level: BannerLevel; text: string } {
  const [next, after] = nextHighEvents(data.upcoming);
  const recent = recentHighResult(data.happened, nowMs);
  const parts: string[] = [];
  if (recent) parts.push(reportedText(recent));
  if (next) {
    parts.push(
      `${next.name} in ${countdownLabel(new Date(next.at).getTime(), nowMs)}${after ? `, then ${after.name}` : ""}.`,
    );
  } else if (!recent) {
    parts.push("No high-impact events in this window.");
  }

  let level: BannerLevel = "info";
  const loan = data.loan?.stress;
  if (loan) {
    const drop = Math.round(loan.dropToLiquidationPercent);
    if (loan.buffer === "Comfortable") {
      parts.push(`Your loan can absorb a ${drop}% drop.`);
    } else {
      level = loan.buffer === "Thin" ? "warn" : "crit";
      parts.push(`Your loan is ${loan.buffer.toLowerCase()}: a ${drop}% drop reaches liquidation.`);
    }
  }
  return { level, text: parts.join(" ") };
}
