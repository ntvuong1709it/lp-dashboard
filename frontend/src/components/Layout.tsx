import { useEffect, useRef, useState } from "react";
import { Link, NavLink, Navigate, Outlet, useLocation } from "react-router-dom";
import {
  useHealth,
  useSyncStatus,
  useTriggerSync,
} from "../api/client";
import { PositionDrawer } from "./PositionDrawer";
import { PriceTicker } from "./PriceTicker";
import { SetupScreen } from "./SetupScreen";
import { UpdatedAgo } from "./UpdatedAgo";
import { SyncErrorList } from "./ui";
import { ProgressBar, SyncProgressDetails, describeSync } from "./SyncProgress";
import { useAuth } from "../lib/auth";
import { shortAddress } from "../lib/format";
import { useOwner } from "../lib/owner";
import { setTheme, useTheme } from "../lib/theme";
import { setBalancesHidden, useHideBalances } from "../lib/privacy";

const navClass = ({ isActive }: { isActive: boolean }) =>
  `whitespace-nowrap rounded-md px-3 py-1.5 text-sm font-medium ${isActive ? "bg-raised text-fg shadow-[inset_0_-2px_0_var(--accent)]" : "text-muted hover:bg-raised/60 hover:text-fg"}`;

export function Layout() {
  // The pool explorer is a table next to a side panel, so it gets more of a wide screen than the other pages.
  const { pathname } = useLocation();
  const wide = pathname.startsWith("/pools");
  const width = wide ? "max-w-[110rem]" : "max-w-7xl";
  return (
    <div className="min-h-screen">
      <header className="sticky top-0 z-20 border-b border-line bg-panel">
        <div className={`mx-auto flex ${width} flex-wrap items-center gap-4 px-4 py-3`}>
          <span className="flex items-center gap-2 text-lg font-bold tracking-tight">
            <i className="inline-block h-5 w-5 rounded-[50%_50%_50%_4px] bg-accent-fill" />
            LP Dashboard
          </span>
          <nav
            aria-label="Sections"
            className="order-last flex w-full gap-1 overflow-x-auto md:order-none md:w-auto"
          >
            <NavLink to="/" end className={navClass}>
              Overview
            </NavLink>
            <NavLink
              to="/fees"
              className={({ isActive }) =>
                navClass({ isActive: isActive || pathname === "/activity" })
              }
            >
              Earnings
            </NavLink>
            <NavLink to="/borrow" className={navClass}>
              Borrow
            </NavLink>
            <NavLink to="/positions" className={navClass}>
              Positions
            </NavLink>
            <NavLink to="/pools" className={navClass}>
              Pools
            </NavLink>
            <NavLink to="/pnl" className={navClass}>
              Profit &amp; loss
            </NavLink>
            <NavLink to="/risk" className={navClass}>
              Risk
            </NavLink>
            <NavLink to="/addresses" className={navClass}>
              Addresses
            </NavLink>
          </nav>
          <div className="ml-auto flex items-center gap-3">
            <OwnerSelect />
            <HideBalancesToggle />
            <ThemeToggle />
            <SyncIndicator />
            <LogoutButton />
          </div>
        </div>
        <div className="border-t border-line">
          <div className={`mx-auto ${width} px-4 py-2`}>
            <PriceTicker />
          </div>
        </div>
      </header>
      <main className={`mx-auto ${width} px-4 py-6`}>
        <Content />
      </main>
      <PositionDrawer />
    </div>
  );
}

/** The routed page, or the setup screen when no chain provider is configured (wallets can still be managed). */
function Content() {
  const { data: health } = useHealth();
  const { pathname } = useLocation();
  const { addresses } = useOwner();
  const needsSetup = health?.providers.length === 0;

  // A brand-new account has no wallets yet, so it starts by entering one.
  if (addresses.length === 0 && pathname !== "/addresses")
    return <Navigate to="/addresses" replace />;
  if (needsSetup && pathname !== "/addresses") return <SetupScreen />;
  return <Outlet />;
}

function LogoutButton() {
  const { logout } = useAuth();
  return (
    <button
      className="rounded-md border border-line-strong px-2 py-1.5 text-sm text-fg-2 hover:bg-raised"
      title="Sign out of this device"
      onClick={() => void logout()}
    >
      Log out
    </button>
  );
}

function ThemeToggle() {
  const theme = useTheme();
  const next = theme === "dark" ? "light" : "dark";
  return (
    <button
      className="rounded-md border border-line-strong px-2 py-1.5 text-sm text-fg-2 hover:bg-raised"
      title={`Switch to ${next} theme`}
      aria-label={`Switch to ${next} theme`}
      onClick={() => setTheme(next)}
    >
      {theme === "dark" ? "Light" : "Dark"}
    </button>
  );
}

function HideBalancesToggle() {
  const hidden = useHideBalances();
  return (
    <button
      className={`rounded-md border px-2 py-1.5 text-sm hover:bg-raised ${hidden ? "border-accent-fill text-accent" : "border-line-strong text-fg-2"}`}
      aria-pressed={hidden}
      title={hidden ? "Show balances" : "Hide balances"}
      onClick={() => setBalancesHidden(!hidden)}
    >
      {hidden ? "Balances hidden" : "Hide balances"}
    </button>
  );
}

function OwnerSelect() {
  const { selected, setOwner, addresses } = useOwner();

  return (
    <select
      className="rounded-md border border-control bg-panel px-2 py-1.5 text-sm"
      value={selected ?? ""}
      onChange={(e) => setOwner(e.target.value || null)}
    >
      <option value="">All wallets</option>
      {addresses.map((a) => (
        <option key={a.address} value={a.address}>
          {a.label
            ? `${a.label} (${shortAddress(a.address)})`
            : shortAddress(a.address)}
        </option>
      ))}
    </select>
  );
}

function SyncIndicator() {
  const { data: status, error: statusError } = useSyncStatus();
  const trigger = useTriggerSync();
  const { allOwners } = useOwner();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  const errors = [
    ...(trigger.error
      ? [`Could not start sync: ${trigger.error.message}`]
      : []),
    ...(statusError
      ? [`Could not load sync status: ${statusError.message}`]
      : []),
    ...(status?.lastErrors ?? []),
  ];
  const busy = status?.isRunning || status?.isQueued || trigger.isPending;
  const running = !!status?.isRunning;

  useEffect(() => {
    if (!open) return;
    const onClick = (e: MouseEvent) => {
      if (!ref.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && setOpen(false);
    document.addEventListener("mousedown", onClick);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onClick);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  return (
    <div
      ref={ref}
      className="relative flex items-center gap-2 text-xs text-muted"
    >
      <button
        className="flex min-w-0 flex-col items-start gap-1 rounded-md px-2 py-1 text-left hover:bg-raised"
        aria-expanded={open}
        title={running ? describeSync(status) : "Show sync details"}
        onClick={() => setOpen((o) => !o)}
      >
        {running ? (
          <>
            <span className="flex items-center gap-1.5 text-accent">
              <span className="inline-block h-3 w-3 animate-spin rounded-full border-2 border-accent-fill border-t-transparent" />
              Syncing{" "}
              <span className="num font-medium">
                {Math.round(status?.percent ?? 0)}%
              </span>
              <ProgressBar
                percent={status?.percent ?? 0}
                hasErrors={errors.length > 0}
                className="w-24"
              />
            </span>
            <span className="max-w-72 truncate text-[11px] text-muted">
              {describeSync(status)}
            </span>
          </>
        ) : status?.isQueued || trigger.isPending ? (
          <span className="flex items-center gap-1.5 text-accent">
            <span className="inline-block h-3 w-3 animate-spin rounded-full border-2 border-accent-fill border-t-transparent" />
            Starting sync…
          </span>
        ) : (
          <UpdatedAgo prefix="Synced" />
        )}
      </button>
      {errors.length > 0 && (
        <button
          className="rounded-full bg-warn-fill/15 px-2 py-0.5 font-medium text-warn ring-1 ring-warn-fill/40 hover:bg-warn-fill/25"
          aria-expanded={open}
          onClick={() => setOpen((o) => !o)}
        >
          {errors.length} {errors.length === 1 ? "error" : "errors"} ▾
        </button>
      )}
      <button
        className="rounded-md border border-line-strong px-2 py-1 text-fg-2 hover:bg-raised disabled:opacity-50"
        disabled={busy || !allOwners}
        onClick={() => trigger.mutate()}
      >
        Sync now
      </button>

      {open && (
        <div
          role="dialog"
          aria-label="Sync details"
          className="absolute right-0 top-full z-20 mt-2 w-lg max-w-[90vw] rounded-lg border border-line-strong bg-panel shadow-xl"
        >
          <div className="flex items-center justify-between border-b border-line px-3 py-2">
            <span
              className={`text-sm font-medium ${errors.length > 0 ? "text-warn" : "text-fg-2"}`}
            >
              {running
                ? "Sync in progress"
                : errors.length > 0
                  ? "Sync problems"
                  : "Sync"}
            </span>
            <button
              className="px-1 text-base leading-none text-muted hover:text-fg"
              aria-label="Close"
              onClick={() => setOpen(false)}
            >
              ×
            </button>
          </div>
          <div className="max-h-[70vh] space-y-3 overflow-y-auto p-3">
            {status && <SyncProgressDetails status={status} />}
            {errors.length > 0 && (
              <div>
                <div className="mb-2 text-xs font-medium text-warn">
                  {errors.length} {errors.length === 1 ? "error" : "errors"}
                  {running ? " so far" : ""}
                </div>
                <SyncErrorList errors={errors} />
              </div>
            )}
          </div>
          <div className="flex items-center justify-end gap-3 border-t border-line px-3 py-2">
            <Link
              to="/addresses"
              className="text-accent hover:underline"
              onClick={() => setOpen(false)}
            >
              View sync status
            </Link>
            <button
              className="rounded-md bg-accent-solid px-2 py-1 font-medium text-white hover:bg-accent-fill disabled:opacity-50"
              disabled={busy || !allOwners}
              onClick={() => trigger.mutate()}
            >
              {errors.length > 0 ? "Retry sync" : "Sync now"}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
