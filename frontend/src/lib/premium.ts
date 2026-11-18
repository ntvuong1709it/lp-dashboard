import type { PremiumDayDto, PremiumZone } from "../api/types";

export const PREMIUM_LABEL: Record<PremiumZone, string> = {
  SellingHard: "US selling hard",
  SellingABit: "US selling a bit",
  Flat: "Flat",
  BuyingABit: "US buying a bit",
  BuyingHard: "US buying hard",
};

/** The label without "US", for the Overview chip ("US flow +0.06%, buying a bit"). */
export const PREMIUM_SHORT: Record<PremiumZone, string> = {
  SellingHard: "selling hard",
  SellingABit: "selling a bit",
  Flat: "flat",
  BuyingABit: "buying a bit",
  BuyingHard: "buying hard",
};

/** Chip colours: flat is plain grey, selling uses the down colour and buying the up colour. It says who is paying more, not what to do. */
export const PREMIUM_CHIP: Record<PremiumZone, string> = {
  SellingHard: "bg-down-fill/15 text-down",
  SellingABit: "bg-down-fill/15 text-down",
  Flat: "bg-raised text-fg-2",
  BuyingABit: "bg-up-fill/15 text-up",
  BuyingHard: "bg-up-fill/15 text-up",
};

/** The zone's colour for SVG fills and strokes: the same five tokens the Fear & Greed zones use (tokens.css), down to up. */
export const PREMIUM_VAR: Record<PremiumZone, string> = {
  SellingHard: "var(--zone-1)",
  SellingABit: "var(--zone-2)",
  Flat: "var(--zone-3)",
  BuyingABit: "var(--zone-4)",
  BuyingHard: "var(--zone-5)",
};

/** "+0.06%", "−0.03%" (a true minus sign) or "0.00%". A sign only when the shown number is not zero. */
export function formatPremium(percent: number): string {
  const shown = Math.abs(percent).toFixed(2);
  if (Number(shown) === 0) return "0.00%";
  return `${percent < 0 ? "−" : "+"}${shown}%`;
}

/** The lowest and highest day (the earlier day when two tie). `days` must not be empty. */
export function premiumRange(days: PremiumDayDto[]): { low: PremiumDayDto; high: PremiumDayDto } {
  let low = days[0];
  let high = days[0];
  for (const d of days) {
    if (d.percent < low.percent) low = d;
    if (d.percent > high.percent) high = d;
  }
  return { low, high };
}

/** Half the height of the chart: at least 0.15, else the biggest absolute value rounded up to the next 0.05. */
export function premiumAxisMax(days: PremiumDayDto[]): number {
  const biggest = days.reduce((m, d) => Math.max(m, Math.abs(d.percent)), 0);
  const stepped = Math.ceil(biggest / 0.05 - 1e-9) * 0.05;
  return Number(Math.max(0.15, stepped).toFixed(2));
}
