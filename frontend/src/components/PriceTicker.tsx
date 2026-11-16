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
      <div className="py-1 text-xs text-muted" role="status">
        {isError ? "Prices unavailable" : "Loading prices…"}
      </div>
    );
  }

  return (
    <ul
      aria-label="Market prices"
      className="flex w-max items-stretch gap-2 sm:gap-3"
    >
      {data.prices.map((p) => (
        <li
          key={p.symbol}
          className="flex items-center gap-3 whitespace-nowrap rounded-lg border border-line-strong bg-panel py-1.5 pl-2 pr-3"
        >
          <span
            aria-hidden="true"
            className={`grid h-9 w-9 shrink-0 place-items-center rounded-full text-[11px] font-bold tracking-tight text-white ${badgeColor[p.symbol] ?? "bg-muted"}`}
          >
            {p.symbol.slice(0, 4)}
          </span>
          <span className="flex flex-col leading-tight">
            <span className="text-xs font-semibold tracking-wider text-fg-2">
              {p.symbol}
            </span>
            <span className="num text-xl font-semibold tracking-tight text-fg">
              {formatTickerPrice(p.priceUsd)}
            </span>
          </span>
          {p.change24hPercent != null && (
            <span
              className={`num rounded-md px-2 py-1 text-sm font-semibold ${changeClasses(p.change24hPercent)}`}
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
