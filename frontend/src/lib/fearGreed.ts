import type { FearGreedDayDto, FearGreedZone } from "../api/types";

export const ZONE_LABEL: Record<FearGreedZone, string> = {
  ExtremeFear: "Extreme fear",
  Fear: "Fear",
  Neutral: "Neutral",
  Greed: "Greed",
  ExtremeGreed: "Extreme greed",
};

/** Chip colours. A mood is not a verdict, so neutral is plain grey and only the two ends use the loud colours. */
export const ZONE_CHIP: Record<FearGreedZone, string> = {
  ExtremeFear: "bg-down-fill/15 text-down",
  Fear: "bg-warn-fill/15 text-warn",
  Neutral: "bg-raised text-fg-2",
  Greed: "bg-up-fill/15 text-up",
  ExtremeGreed: "bg-up-fill/15 text-up",
};

/** The zone's colour for SVG fills and strokes (tokens.css). */
export const ZONE_VAR: Record<FearGreedZone, string> = {
  ExtremeFear: "var(--zone-1)",
  Fear: "var(--zone-2)",
  Neutral: "var(--zone-3)",
  Greed: "var(--zone-4)",
  ExtremeGreed: "var(--zone-5)",
};

/** "Nov 18". The day is a UTC day, so it is read in UTC; the viewer's zone would shift it. */
export function fearGreedDay(day: string): string {
  return new Date(`${day}T00:00:00Z`).toLocaleDateString(undefined, { month: "short", day: "numeric", timeZone: "UTC" });
}

/** The lowest and highest reading (the earlier day when two tie). `days` must not be empty. */
export function fearGreedRange(days: FearGreedDayDto[]): { low: FearGreedDayDto; high: FearGreedDayDto } {
  let low = days[0];
  let high = days[0];
  for (const d of days) {
    if (d.value < low.value) low = d;
    if (d.value > high.value) high = d;
  }
  return { low, high };
}
