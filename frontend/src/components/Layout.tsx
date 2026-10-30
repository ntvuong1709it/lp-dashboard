import { useEffect, useRef, useState } from "react";
import { Link, NavLink, Outlet } from "react-router-dom";
import { useAddresses, useSyncStatus, useTriggerSync } from "../api/client";
import { SyncErrorList } from "./ui";
import { ProgressBar, SyncProgressDetails, describeSync } from "./SyncProgress";
import { formatDate, shortAddress } from "../lib/format";
import { useOwner } from "../lib/owner";

const navClass = ({ isActive }: { isActive: boolean }) =>
  `px-3 py-1.5 rounded-md text-sm ${isActive ? "bg-slate-800 text-white" : "text-slate-400 hover:text-white"}`;

export function Layout() {
  return (
    <div className="min-h-screen">
      <header className="border-b border-slate-800 bg-slate-900/60">
        <div className="mx-auto flex max-w-7xl flex-wrap items-center gap-4 px-4 py-3">
          <span className="font-semibold tracking-tight">LP Dashboard</span>
          <nav className="flex gap-1">
            <NavLink to="/" end className={navClass}>
              Portfolio
            </NavLink>
            <NavLink to="/fees" className={navClass}>
              Fee claims
            </NavLink>
            <NavLink to="/addresses" className={navClass}>
              Addresses
            </NavLink>
          </nav>
          <div className="ml-auto flex items-center gap-3">
            <OwnerSelect />
            <SyncIndicator />
          </div>
        </div>
      </header>
      <main className="mx-auto max-w-7xl px-4 py-6">
        <Outlet />
      </main>
    </div>
  );
}

function OwnerSelect() {
  const { owner, setOwner } = useOwner();
  const { data: addresses } = useAddresses();

  return (
    <select
      className="rounded-md border border-slate-700 bg-slate-900 px-2 py-1.5 text-sm"
      value={owner ?? ""}
      onChange={(e) => setOwner(e.target.value || null)}
    >
      <option value="">All wallets</option>
      {addresses?.map((a) => (
        <option key={a.id} value={a.address}>
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
      className="relative flex items-center gap-2 text-xs text-slate-400"
    >
      <button
        className="flex min-w-0 flex-col items-start gap-1 rounded-md px-2 py-1 text-left hover:bg-slate-800"
        aria-expanded={open}
        title={running ? describeSync(status) : "Show sync details"}
        onClick={() => setOpen((o) => !o)}
      >
        {running ? (
          <>
            <span className="flex items-center gap-1.5 text-sky-300">
              <span className="inline-block h-3 w-3 animate-spin rounded-full border-2 border-sky-400 border-t-transparent" />
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
            <span className="max-w-72 truncate text-[11px] text-slate-400">
              {describeSync(status)}
            </span>
          </>
        ) : status?.isQueued || trigger.isPending ? (
          <span className="flex items-center gap-1.5 text-sky-300">
            <span className="inline-block h-3 w-3 animate-spin rounded-full border-2 border-sky-400 border-t-transparent" />
            Starting sync…
          </span>
        ) : (
          <span>Synced {formatDate(status?.lastFinishedAt)}</span>
        )}
      </button>
      {errors.length > 0 && (
        <button
          className="rounded-full bg-amber-500/15 px-2 py-0.5 font-medium text-amber-300 ring-1 ring-amber-500/40 hover:bg-amber-500/25"
          aria-expanded={open}
          onClick={() => setOpen((o) => !o)}
        >
          {errors.length} {errors.length === 1 ? "error" : "errors"} ▾
        </button>
      )}
      <button
        className="rounded-md border border-slate-700 px-2 py-1 text-slate-200 hover:bg-slate-800 disabled:opacity-50"
        disabled={busy}
        onClick={() => trigger.mutate()}
      >
        Sync now
      </button>

      {open && (
        <div
          role="dialog"
          aria-label="Sync details"
          className="absolute right-0 top-full z-20 mt-2 w-lg max-w-[90vw] rounded-lg border border-slate-700 bg-slate-900 shadow-xl"
        >
          <div className="flex items-center justify-between border-b border-slate-800 px-3 py-2">
            <span
              className={`text-sm font-medium ${errors.length > 0 ? "text-amber-300" : "text-slate-200"}`}
            >
              {running
                ? "Sync in progress"
                : errors.length > 0
                  ? "Sync problems"
                  : "Sync"}
            </span>
            <button
              className="px-1 text-base leading-none text-slate-400 hover:text-white"
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
                <div className="mb-2 text-xs font-medium text-amber-300">
                  {errors.length} {errors.length === 1 ? "error" : "errors"}
                  {running ? " so far" : ""}
                </div>
                <SyncErrorList errors={errors} />
              </div>
            )}
          </div>
          <div className="flex items-center justify-end gap-3 border-t border-slate-800 px-3 py-2">
            <Link
              to="/addresses"
              className="text-sky-400 hover:underline"
              onClick={() => setOpen(false)}
            >
              View sync status
            </Link>
            <button
              className="rounded-md bg-sky-600 px-2 py-1 font-medium text-white hover:bg-sky-500 disabled:opacity-50"
              disabled={busy}
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
