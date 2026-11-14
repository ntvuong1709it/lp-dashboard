import { usePriceTicker } from "../api/client";
import { formatPercent } from "../lib/format";

const wholeDollars = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });
const cents = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const subDollar = new Intl.NumberFormat("en-US", { minimumFractionDigits: 4, maximumFractionDigits: 4 });

const formatTickerPrice = (value: number) =>
  `$${value >= 1000 ? wholeDollars.format(value) : value >= 1 ? cents.format(value) : subDollar.format(value)}`;

/** Badge colours only tell the assets apart at a glance. */
const badgeColor: Record<string, string> = {
  BTC: "bg-[#f7931a]",
  ETH: "bg-[#627eea]",
  BNB: "bg-[#c98f00]",
  AERO: "bg-[#2a78d6]",
};

function changeClasses(change: number) {
  if (change > 0) return "bg-up/15 text-up";
  if (change < 0) return "bg-down/15 text-down";
  return "bg-raised text-fg-2";
}

/** Spot prices of BTC, ETH, BNB and AERO. Market prices are not balances, so the hide-balances mask does not apply. */
export function PriceTicker() {
  const { data, isError } = usePriceTicker();

  // Keep showing the last prices if a refresh fails; only say so when there is nothing to show.
  if (!data) {
    return (
      <div className="py-1.5 text-xs text-muted" role="status">
        {isError ? "Prices unavailable" : "Loading prices…"}
      </div>
    );
  }

  return (
    <ul
      aria-label="Market prices"
      className="grid grid-cols-2 gap-x-3 gap-y-2 sm:flex sm:flex-wrap sm:items-center sm:gap-x-6"
    >
      {data.prices.map((p) => (
        <li key={p.symbol} className="flex flex-wrap items-center gap-x-2.5 gap-y-1 sm:flex-nowrap sm:whitespace-nowrap">
          <span
            aria-hidden="true"
            className={`grid h-[26px] w-[26px] shrink-0 place-items-center rounded-full text-[9px] font-bold tracking-tighter text-white ${badgeColor[p.symbol] ?? "bg-muted"}`}
          >
            {p.symbol}
          </span>
          <span className="flex flex-col leading-tight">
            <span className="text-[11px] font-semibold tracking-wider text-muted">
              {p.symbol}
            </span>
            <span className="num text-base font-semibold text-fg">
              {formatTickerPrice(p.priceUsd)}
            </span>
          </span>
          {p.change24hPercent != null && (
            <span
              className={`num rounded-full px-2 py-0.5 text-xs font-semibold ${changeClasses(p.change24hPercent)}`}
              title="Change over the last 24 hours"
            >
              {p.change24hPercent > 0 ? "▲" : p.change24hPercent < 0 ? "▼" : ""}{" "}
              {formatPercent(p.change24hPercent, true)}
            </span>
          )}
        </li>
      ))}
    </ul>
  );
}
