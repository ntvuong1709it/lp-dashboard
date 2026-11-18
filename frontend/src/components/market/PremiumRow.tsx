import type { MarketWatchDto, PremiumReadingDto } from "../../api/types";
import { PREMIUM_CHIP, PREMIUM_LABEL, formatPremium } from "../../lib/premium";
import { Chip } from "../borrow/bits";
import { Panel } from "../ui";
import { PremiumChart } from "./PremiumChart";
import { PremiumGauge } from "./PremiumGauge";

/**
 * US buying against selling: Coinbase's Bitcoin price against Binance's, both in dollars. A gauge with the last 24 hours and how
 * it compared before, and 30 daily bars. It is a clue about who is paying more, not a forecast, and says so. With the source
 * down it is one panel that says so.
 */
export function PremiumRow({ data }: { data: MarketWatchDto }) {
  const p = data.premium;
  if (!p || p.days.length === 0) {
    return (
      <Panel title="US buying vs selling">
        <p className="py-2 text-sm text-muted">The Bitcoin premium is unavailable right now.</p>
      </Panel>
    );
  }

  const compare: [string, PremiumReadingDto | null][] = [
    ["Yesterday", p.yesterday],
    ["7-day average", p.week],
    ["30-day average", p.month],
  ];
  const stale = data.sources.premium === "stale";

  return (
    <div className="grid gap-4 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)] lg:items-stretch">
      <Panel
        title="US buying vs selling"
        actions={stale ? <span className="text-xs text-warn">Out of date</span> : <span className="text-xs text-faint">Updated hourly</span>}
      >
        <div className="flex flex-col items-center gap-1.5">
          <PremiumGauge percent={p.today.percent} zone={p.today.zone} bands={p.bands} />
          <Chip className={PREMIUM_CHIP[p.today.zone]}>{PREMIUM_LABEL[p.today.zone]}</Chip>
          <p className="text-xs text-faint">Average of the last 24 hours</p>
        </div>
        <ul className="mt-4 divide-y divide-line border-t border-line text-sm">
          {compare
            .filter((c): c is [string, PremiumReadingDto] => c[1] != null)
            .map(([when, r]) => (
              <li key={when} className="flex items-center justify-between gap-3 py-2">
                <span className="text-muted">{when}</span>
                <span className="flex items-center gap-2.5">
                  <Chip className={PREMIUM_CHIP[r.zone]}>{PREMIUM_LABEL[r.zone]}</Chip>
                  <span className="num w-16 text-right font-medium">{formatPremium(r.percent)}</span>
                </span>
              </li>
            ))}
        </ul>
        <p className="mt-3 max-w-prose text-sm text-muted">
          Coinbase's Bitcoin price against Binance's, both in dollars. Above zero, US buyers are paying more. It is a clue about who is
          buying, not a count of trades and not a forecast.
        </p>
      </Panel>
      <Panel title="Last 30 days" actions={<span className="text-xs text-faint">Daily average · Coinbase vs Binance</span>}>
        <PremiumChart days={p.days} bands={p.bands} />
      </Panel>
    </div>
  );
}
