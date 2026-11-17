import { useMemo } from "react";
import { Link } from "react-router-dom";
import { useMarketWatch } from "../api/market";
import { EventList } from "../components/market/EventList";
import { HeadlinesPanel } from "../components/market/HeadlinesPanel";
import { LoanBufferBlock, LoanStressTable } from "../components/market/LoanStress";
import { MarketBanner } from "../components/market/MarketBanner";
import { ErrorText, Panel, Segmented, Skeleton } from "../components/ui";
import { formatAgo } from "../lib/format";
import { useOwner } from "../lib/owner";
import { useHideBalances } from "../lib/privacy";
import { usePersistentState } from "../lib/usePersistentState";
import { useNow } from "../lib/useNow";

const IMPACT_OPTIONS = [
  { label: "All impact", value: "all" },
  { label: "High only", value: "high" },
];
const WINDOW_OPTIONS = [
  { label: "7 days", value: 7 },
  { label: "14 days", value: 14 },
];

/**
 * What is coming that can move stocks and crypto (scheduled macro events and big-company earnings), what just happened
 * with a risk-on / risk-off tag, the latest headlines, and what a price fall would do to the loan with the worst risk.
 * Tags only compare actual with forecast for inflation and rate decisions; nothing here says what will happen next.
 */
export function MarketPage() {
  useHideBalances(); // re-render when the toggle changes; formatters read the flag
  const { owner } = useOwner();
  const now = useNow(60_000);
  const [impact, setImpact] = usePersistentState<string>("lpdash.market.impact", "all");
  const [days, setDays] = usePersistentState<number>("lpdash.market.days", 14);
  const window: 7 | 14 = days === 7 ? 7 : 14;
  const watch = useMarketWatch(owner, window);
  const data = watch.data;

  const keep = (e: { impact: string }) => impact === "all" || e.impact === "High";
  const upcoming = useMemo(() => (data?.upcoming ?? []).filter(keep), [data, impact]); // eslint-disable-line react-hooks/exhaustive-deps
  const happened = useMemo(() => (data?.happened ?? []).filter(keep), [data, impact]); // eslint-disable-line react-hooks/exhaustive-deps

  const notices = data
    ? ([
        ["calendar", data.sources.calendar],
        ["earnings", data.sources.earnings],
        ["headlines", data.sources.headlines],
      ] as const).filter(([, status]) => status !== "ok")
    : [];

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Market watch</h1>
          <p className="text-sm text-muted">Events that can move stocks and crypto, and what they mean for your loan.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2.5">
          <Segmented label="Impact" options={IMPACT_OPTIONS} value={impact} onChange={setImpact} />
          <Segmented label="Window" options={WINDOW_OPTIONS} value={days} onChange={setDays} />
        </div>
      </div>

      {watch.error && <ErrorText error={watch.error} />}

      {watch.isLoading && (
        <div role="status" aria-label="Loading" className="space-y-4">
          <p className="text-sm text-muted">Loading the calendar. The first load takes several seconds.</p>
          <Skeleton className="h-10 w-full rounded-lg" />
          <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
            <Skeleton className="h-96 rounded-lg" />
            <Skeleton className="h-96 rounded-lg" />
          </div>
        </div>
      )}

      {data && (
        <>
          <MarketBanner data={data} now={now} />
          {notices.length > 0 && (
            <p className="text-xs text-warn">
              {notices
                .map(([name, status]) => `The ${name} is ${status === "stale" ? "out of date or partial" : "unavailable"}`)
                .join(". ")}
              . Updated {formatAgo(data.updatedAt, now)}.
            </p>
          )}

          <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)] lg:items-start">
            <div className="min-w-0 space-y-4">
              <Panel title="Upcoming" actions={<span className="text-xs text-faint">Times in your time zone</span>}>
                <EventList events={upcoming} empty="Nothing in this window." />
              </Panel>
              <Panel
                title="Already happened"
                actions={<span className="text-xs text-faint">Tags compare actual with forecast, for inflation and rate decisions</span>}
              >
                <EventList events={happened} empty="Nothing in the last few days." happened />
              </Panel>
            </div>

            <div className="min-w-0 space-y-4">
              <Panel
                title="Your loan"
                actions={
                  <Link to="/borrow" className="text-xs text-accent hover:underline">
                    Open Borrow
                  </Link>
                }
              >
                {data.loan ? (
                  <div className="space-y-4">
                    <LoanBufferBlock stress={data.loan.stress} />
                    <LoanStressTable stress={data.loan.stress} />
                    <p className="text-xs text-faint">
                      Assumes your collateral falls one for one with price, the worst case: LP collateral usually falls less.
                    </p>
                  </div>
                ) : (
                  <p className="text-sm text-muted">
                    No loan to stress.{" "}
                    <Link to="/borrow" className="text-accent hover:underline">
                      Set up loans on Borrow
                    </Link>{" "}
                    to see how a price fall would affect it.
                  </p>
                )}
              </Panel>
              <HeadlinesPanel headlines={data.headlines} now={now} unavailable={data.sources.headlines === "unavailable"} />
            </div>
          </div>
        </>
      )}
    </div>
  );
}
