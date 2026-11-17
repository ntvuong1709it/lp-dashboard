import type { MarketWatchDto } from "../../api/types";
import { marketBanner, type BannerLevel } from "../../lib/market";

const STYLE: Record<BannerLevel, string> = {
  info: "border-accent-fill/50 bg-accent-fill/10",
  warn: "border-warn-fill/50 bg-warn-fill/10",
  crit: "border-down-fill/50 bg-down-fill/10",
};

/** One sentence on what is next and what the loan can take; its colour follows the loan buffer. */
export function MarketBanner({ data, now }: { data: MarketWatchDto; now: number }) {
  const { level, text } = marketBanner(data, now);
  return (
    <div role="status" className={`rounded-lg border px-4 py-2.5 text-sm ${STYLE[level]}`}>
      {text}
    </div>
  );
}
