import { useMemo, useState } from "react";
import { useMarketWatch } from "../api/market";
import type { MarketEventDto } from "../api/types";
import { CalendarStrip } from "../components/market/CalendarStrip";
import { EventList } from "../components/market/EventList";
import { FallPanel } from "../components/market/FallPanel";
import { HeadlinesPanel } from "../components/market/HeadlinesPanel";
import { LoanAlert } from "../components/market/LoanAlert";
import { NextUp } from "../components/market/NextUp";
import { ErrorText, Panel, Segmented, Skeleton } from "../components/ui";
import { formatAgo } from "../lib/format";
import { dayKey, dayLabel, nextHighEvents } from "../lib/market";
import { useOwner } from "../lib/owner";
import { useHideBalances } from "../lib/privacy";
import { usePersistentState } from "../lib/usePersistentState";
import { useNow } from "../lib/useNow";

const IMPACT_OPTIONS = [
  { label: "Big ones", value: "high" },
  { label: "All events", value: "all" },
];

/**
 * What is coming that can move stocks and crypto, and what a fall would do to the loan and to every position. The next big
 * event and the "if prices fall" test come first; the calendar and the event list are for looking further. Tags only compare
 * actual with forecast for inflation and rate decisions; nothing here says what will happen next.
 */
export function MarketPage() {
  useHideBalances(); // re-render when the toggle changes; formatters read the flag
  const { owner } = useOwner();
  const now = useNow(60_000);
  const [impact, setImpact] = usePersistentState<string>("lpdash.market.impact", "high");
  const [days, setDays] = usePersistentState<number>("lpdash.market.days", 14);
  const [fall, setFall] = usePersistentState<number>("lpdash.market.fall", 20);
  const [day, setDay] = useState<string | null>(null);
  const windowDays: 7 | 14 = days === 7 ? 7 : 14;
  const watch = useMarketWatch(owner, windowDays);
  const data = watch.data;

  const todayKey = dayKey(new Date(now));
  const big = (e: MarketEventDto) => impact === "all" || e.impact === "High";
  const inDay = (e: MarketEventDto) => day == null || dayKey(new Date(e.at)) === day;
  const show = (e: MarketEventDto) => inDay(e) && (day != null || big(e));

  const view = useMemo(() => {
    const upcomingAll = data?.upcoming ?? [];
    const happenedAll = data?.happened ?? [];
    const upcoming = upcomingAll.filter(show);
    const happened = happenedAll.filter(show);
    return {
      all: [...happenedAll, ...upcomingAll],
      upcoming,
      happened,
      hiddenSmaller: day == null && impact === "high" ? upcomingAll.filter((e) => e.impact !== "High").length : 0,
      openId: nextHighEvents(upcomingAll)[0]?.id ?? null,
    };
  }, [data, impact, day]); // eslint-disable-line react-hooks/exhaustive-deps

  const showPast = view.happened.length > 0 && (day == null || day <= todayKey);

  const notices = data
    ? ([
        ["calendar", data.sources.calendar],
        ["earnings", data.sources.earnings],
        ["headlines", data.sources.headlines],
      ] as const).filter(([, status]) => status !== "ok")
    : [];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-x-4 gap-y-2">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Market watch</h1>
          <p className="max-w-prose text-sm text-muted">
            What is coming that can move prices, and what a fall would do to your loan and your positions.
          </p>
        </div>
        {data && (
          <div className="flex items-center gap-3 text-xs text-faint">
            <span>Updated {formatAgo(data.updatedAt, now)}</span>
            <button
              type="button"
              className="rounded-md border border-line-strong px-2.5 py-1 text-fg-2 hover:bg-raised disabled:opacity-60"
              disabled={watch.isFetching}
              onClick={() => void watch.refetch()}
            >
              {watch.isFetching ? "Refreshing…" : "Refresh"}
            </button>
          </div>
        )}
      </div>

      {watch.error && <ErrorText error={watch.error} />}

      {watch.isLoading && (
        <div role="status" aria-label="Loading" className="space-y-4">
          <p className="text-sm text-muted">Loading the calendar. The first load takes several seconds.</p>
          <div className="grid gap-4 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)]">
            <Skeleton className="h-72 rounded-lg" />
            <Skeleton className="h-72 rounded-lg" />
          </div>
          <Skeleton className="h-40 w-full rounded-lg" />
        </div>
      )}

      {data && (
        <>
          <LoanAlert data={data} now={now} />
          {notices.length > 0 && (
            <p className="text-xs text-warn">
              {notices
                .map(([name, status]) => `The ${name} is ${status === "stale" ? "out of date or partial" : "unavailable"}`)
                .join(". ")}
              .
            </p>
          )}

          <div className="grid gap-4 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)] lg:items-stretch">
            <NextUp data={data} now={now} />
            <FallPanel data={data} fall={fall} onFall={setFall} />
          </div>

          <CalendarStrip
            events={view.all}
            now={now}
            windowDays={windowDays}
            onWindow={setDays}
            day={day}
            onDay={setDay}
          />

          <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)] lg:items-start">
            <Panel
              title={day ? `Events on ${dayLabel(new Date(`${day}T12:00:00`))}` : "Coming up"}
              actions={
                <span className="flex flex-wrap items-center gap-3">
                  <span className="text-xs text-faint">Times in your time zone</span>
                  <Segmented label="Impact" options={IMPACT_OPTIONS} value={impact} onChange={setImpact} />
                </span>
              }
            >
              <EventList
                events={view.upcoming}
                empty={day ? "Nothing scheduled on this day." : "No big events in this window."}
                openId={view.openId}
                today={todayKey}
              />
              {view.hiddenSmaller > 0 && (
                <button type="button" className="mt-3 text-sm text-accent hover:underline" onClick={() => setImpact("all")}>
                  Show {view.hiddenSmaller} smaller {view.hiddenSmaller === 1 ? "event" : "events"}
                </button>
              )}
              {showPast && (
                <details className="group mt-5 border-t border-line pt-3" open={day != null}>
                  <summary className="cursor-pointer list-none text-sm text-accent hover:underline [&::-webkit-details-marker]:hidden">
                    Already out ({view.happened.length})
                  </summary>
                  <div className="mt-3">
                    <EventList events={view.happened} empty="" happened today={todayKey} />
                  </div>
                </details>
              )}
            </Panel>
            <HeadlinesPanel headlines={data.headlines} now={now} unavailable={data.sources.headlines === "unavailable"} />
          </div>
        </>
      )}
    </div>
  );
}
