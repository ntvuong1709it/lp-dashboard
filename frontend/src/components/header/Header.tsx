import { useEffect, useRef, useState } from "react";
import { Link, NavLink, useLocation } from "react-router-dom";
import { PriceTicker } from "../PriceTicker";
import { SyncStatus } from "./SyncStatus";
import {
  ChevronIcon,
  EyeIcon,
  EyeOffIcon,
  MoonIcon,
  SunIcon,
  UserIcon,
} from "./icons";
import { useAuth } from "../../lib/auth";
import { shortAddress } from "../../lib/format";
import { useOwner } from "../../lib/owner";
import { setBalancesHidden, useHideBalances } from "../../lib/privacy";
import { setTheme, useTheme } from "../../lib/theme";

const navClass = ({ isActive }: { isActive: boolean }) =>
  `whitespace-nowrap rounded-md px-3 py-1.5 text-sm font-medium ${isActive ? "bg-raised text-fg shadow-[inset_0_-2px_0_var(--accent)]" : "text-muted hover:bg-raised/60 hover:text-fg"}`;

const iconButton =
  "inline-flex h-8 w-8 items-center justify-center rounded-md hover:bg-raised";

/**
 * Two pinned rows: navigation with the wallet picker and account menu on top, and a status bar underneath that holds the
 * prices and the page-wide state (sync, hide balances, theme). The links sit beside the logo from 1200px up; below that
 * they take their own row.
 */
export function Header({ width }: { width: string }) {
  const { pathname } = useLocation();
  return (
    <header className="sticky top-0 z-20 border-b border-line bg-panel">
      <div className={`mx-auto flex ${width} flex-wrap items-center gap-x-4 gap-y-1 px-4 py-2.5 min-[1200px]:py-3`}>
        <span className="flex items-center gap-2 text-lg font-bold tracking-tight">
          <i className="inline-block h-5 w-5 rounded-[50%_50%_50%_4px] bg-accent-fill" />
          <span className="sr-only sm:not-sr-only">LP Dashboard</span>
        </span>
        <nav
          aria-label="Sections"
          className="order-last -mx-1 flex w-full gap-1 overflow-x-auto px-1 pb-1 min-[1200px]:order-none min-[1200px]:mx-0 min-[1200px]:w-auto min-[1200px]:px-0 min-[1200px]:pb-0"
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
        <div className="ml-auto flex min-w-0 flex-1 items-center justify-end gap-2 sm:flex-none">
          <OwnerSelect />
          <AccountMenu />
        </div>
      </div>
      <div className="border-t border-line bg-surface">
        <div className={`mx-auto flex ${width} items-center gap-2 px-4`}>
          <div className="min-w-0 flex-1 overflow-x-auto py-1.5 [scrollbar-width:none]">
            <PriceTicker />
          </div>
          <div className="flex shrink-0 items-center gap-1 py-1">
            <SyncStatus />
            <HideBalancesToggle />
            <ThemeToggle />
          </div>
        </div>
      </div>
    </header>
  );
}

function ThemeToggle() {
  const theme = useTheme();
  const next = theme === "dark" ? "light" : "dark";
  return (
    <button
      type="button"
      className={`${iconButton} text-fg-2`}
      title={`Switch to ${next} theme`}
      aria-label={`Switch to ${next} theme`}
      onClick={() => setTheme(next)}
    >
      {theme === "dark" ? <SunIcon /> : <MoonIcon />}
    </button>
  );
}

function HideBalancesToggle() {
  const hidden = useHideBalances();
  return (
    <button
      type="button"
      className={`${iconButton} ${hidden ? "text-accent shadow-[inset_0_0_0_1px_var(--accent-fill)]" : "text-fg-2"}`}
      aria-pressed={hidden}
      aria-label="Hide balances"
      title={hidden ? "Show balances" : "Hide balances"}
      onClick={() => setBalancesHidden(!hidden)}
    >
      {hidden ? <EyeOffIcon /> : <EyeIcon />}
    </button>
  );
}

function OwnerSelect() {
  const { selected, setOwner, addresses } = useOwner();
  const labelOf = (a: (typeof addresses)[number]) =>
    a.label ? `${a.label} (${shortAddress(a.address)})` : shortAddress(a.address);
  const current = addresses.find((a) => a.address === selected);

  return (
    <select
      aria-label="Wallet"
      title={current ? labelOf(current) : "All wallets"}
      className="h-9 min-w-0 flex-1 truncate rounded-md border border-control bg-panel px-2 text-sm sm:w-56 sm:flex-none"
      value={selected ?? ""}
      onChange={(e) => setOwner(e.target.value || null)}
    >
      <option value="">All wallets</option>
      {addresses.map((a) => (
        <option key={a.address} value={a.address}>
          {labelOf(a)}
        </option>
      ))}
    </select>
  );
}

/** Account and Log out live here so the main row stays about navigation and the wallet. */
function AccountMenu() {
  const { logout } = useAuth();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const button = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    const onClick = (e: MouseEvent) => {
      if (!ref.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      setOpen(false);
      button.current?.focus();
    };
    document.addEventListener("mousedown", onClick);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onClick);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  const item =
    "flex w-full items-center rounded-md px-2.5 py-2 text-left text-sm text-fg-2 hover:bg-raised hover:text-fg";

  return (
    <div ref={ref} className="relative shrink-0">
      <button
        ref={button}
        type="button"
        className="inline-flex h-9 items-center gap-1 rounded-md border border-line-strong px-2 text-fg-2 hover:bg-raised"
        aria-label="Account"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
      >
        <UserIcon />
        <ChevronIcon />
      </button>
      {open && (
        <div
          className="absolute right-0 top-full z-20 mt-2 w-56 rounded-lg border border-line-strong bg-panel p-1.5 shadow-xl"
        >
          <div className="px-2.5 py-1.5 text-xs text-faint">Signed in on this device</div>
          <Link
            to="/addresses"
            className={item}
            onClick={() => setOpen(false)}
          >
            Account and password
          </Link>
          <div className="my-1 border-t border-line" />
          <button
            type="button"
            className={item}
            onClick={() => void logout()}
          >
            Log out
          </button>
        </div>
      )}
    </div>
  );
}
