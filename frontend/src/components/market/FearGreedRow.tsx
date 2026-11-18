import type { FearGreedDayDto, MarketWatchDto } from "../../api/types";
import { ZONE_CHIP, ZONE_LABEL, fearGreedDay } from "../../lib/fearGreed";
import { Chip } from "../borrow/bits";
import { Panel } from "../ui";
import { FearGreedChart } from "./FearGreedChart";
import { FearGreedGauge } from "./FearGreedGauge";

/**
 * The crypto Fear and Greed index: a gauge with how it compared yesterday, last week and last month, and the last 30 days.
 * It is a mood reading, not a forecast, and says so. With the source down it is one panel that says so.
 */
export function FearGreedRow({ data }: { data: MarketWatchDto }) {
  const fg = data.fearGreed;
  if (!fg) {
    return (
      <Panel title="Crypto fear & greed">
        <p className="py-2 text-sm text-muted">Fear and greed is unavailable right now.</p>
      </Panel>
    );
  }

  const compare: [string, FearGreedDayDto | null][] = [
    ["Yesterday", fg.yesterday],
    ["Last week", fg.lastWeek],
    ["Last month", fg.lastMonth],
  ];
  const stale = data.sources.fearGreed === "stale";

  return (
    <div className="grid gap-4 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)] lg:items-stretch">
      <Panel
        title="Crypto fear & greed"
        actions={stale ? <span className="text-xs text-warn">Out of date</span> : <span className="text-xs text-faint">Updated daily</span>}
      >
        <div className="flex flex-col items-center gap-1.5">
          <FearGreedGauge value={fg.today.value} zone={fg.today.zone} bands={fg.bands} />
          <Chip className={ZONE_CHIP[fg.today.zone]}>{ZONE_LABEL[fg.today.zone]}</Chip>
          <p className="text-xs text-faint">Reading of {fearGreedDay(fg.today.day)}</p>
        </div>
        <ul className="mt-4 divide-y divide-line border-t border-line text-sm">
          {compare
            .filter((c): c is [string, FearGreedDayDto] => c[1] != null)
            .map(([when, d]) => (
              <li key={when} className="flex items-center justify-between gap-3 py-2">
                <span className="text-muted">{when}</span>
                <span className="flex items-center gap-2.5">
                  <Chip className={ZONE_CHIP[d.zone]}>{ZONE_LABEL[d.zone]}</Chip>
                  <span className="num w-8 text-right font-medium">{d.value}</span>
                </span>
              </li>
            ))}
        </ul>
        <p className="mt-3 max-w-prose text-sm text-muted">
          How worried or excited the crypto market is, from 0 to 100. A mood reading, not a forecast.
        </p>
      </Panel>
      <Panel title="Last 30 days" actions={<span className="text-xs text-faint">Source: alternative.me</span>}>
        <FearGreedChart days={fg.days} bands={fg.bands} />
      </Panel>
    </div>
  );
}
