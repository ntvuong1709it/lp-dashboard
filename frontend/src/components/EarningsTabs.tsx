import { NavLink } from "react-router-dom";

const tabClass = ({ isActive }: { isActive: boolean }) =>
  `border-r border-line-strong px-3 py-1 last:border-r-0 ${isActive ? "bg-raised font-semibold text-fg" : "text-muted hover:bg-raised/60"}`;

/** Switches between the Earnings summary and the event log; both live under the "Earnings" nav item. */
export function EarningsTabs() {
  return (
    <nav
      aria-label="Earnings views"
      className="inline-flex overflow-hidden rounded-md border border-line-strong text-sm"
    >
      <NavLink to="/fees" className={tabClass}>
        Earnings
      </NavLink>
      <NavLink to="/activity" className={tabClass}>
        Activity
      </NavLink>
    </nav>
  );
}
