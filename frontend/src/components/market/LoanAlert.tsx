import { Link } from "react-router-dom";
import type { MarketWatchDto } from "../../api/types";
import { countdownLabel, nextHighEvents } from "../../lib/market";

/** Said at the top only when the loan is thin or tight; a comfortable loan needs no banner. */
export function LoanAlert({ data, now }: { data: MarketWatchDto; now: number }) {
  const stress = data.loan?.stress;
  if (!stress || stress.buffer === "Comfortable") return null;
  const tight = stress.buffer === "Tight";
  const next = nextHighEvents(data.upcoming)[0];
  return (
    <div
      role="status"
      className={`flex flex-wrap items-baseline gap-x-3 gap-y-1 rounded-lg border px-4 py-2.5 text-sm ${
        tight ? "border-down-fill/50 bg-down-fill/10" : "border-warn-fill/50 bg-warn-fill/10"
      }`}
    >
      <strong className="font-display font-semibold">Your loan is {stress.buffer.toLowerCase()}.</strong>
      <span>
        A {Math.round(stress.dropToLiquidationPercent)}% price fall reaches liquidation
        {next ? `, and ${next.name} is in ${countdownLabel(new Date(next.at).getTime(), now)}` : ""}.
      </span>
      <Link to="/borrow" className="ml-auto whitespace-nowrap font-medium text-accent hover:underline">
        Open Borrow
      </Link>
    </div>
  );
}
