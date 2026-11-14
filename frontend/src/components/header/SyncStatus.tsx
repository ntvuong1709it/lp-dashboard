import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useSyncStatus, useTriggerSync } from "../../api/client";
import { useOwner } from "../../lib/owner";
import { UpdatedAgo } from "../UpdatedAgo";
import { ProgressBar, SyncProgressDetails, describeSync } from "../SyncProgress";
import { SyncErrorList } from "../ui";
import { SyncIcon } from "./icons";

/**
 * Sync state as a compact chip: a status dot, how fresh the data is (or progress while syncing), an error count when
 * something failed, and a refresh button. The chip opens the details popover; the refresh button starts a sync.
 */
export function SyncStatus() {
  const { data: status, error: statusError } = useSyncStatus();
  const trigger = useTriggerSync();
  const { allOwners } = useOwner();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  const errors = [
    ...(trigger.error ? [`Could not start sync: ${trigger.error.message}`] : []),
    ...(statusError ? [`Could not load sync status: ${statusError.message}`] : []),
    ...(status?.lastErrors ?? []),
  ];
  const running = !!status?.isRunning;
  const starting = !running && (!!status?.isQueued || trigger.isPending);
  const busy = running || starting;

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

  const dot = errors.length > 0 ? "bg-warn-fill" : busy ? "bg-accent-fill" : "bg-up-fill";

  return (
    // Static on phones so the popover can span the header; the chip anchors it on wider screens.
    <div ref={ref} className="static text-xs text-muted sm:relative">
      <div className="flex h-8 items-center rounded-full border border-line-strong pl-1 pr-0.5">
        <button
          type="button"
          className="flex h-6 min-w-0 items-center gap-2 rounded-full px-2 hover:bg-raised"
          aria-expanded={open}
          aria-haspopup="dialog"
          title={running ? describeSync(status) : "Show sync details"}
          onClick={() => setOpen((o) => !o)}
        >
          <span aria-hidden="true" className={`h-2 w-2 shrink-0 rounded-full ${dot} ${busy ? "animate-pulse" : ""}`} />
          {running ? (
            <span className="flex items-center gap-2 text-accent">
              <span className="sr-only sm:not-sr-only">Syncing</span>
              <span className="num font-medium">{Math.round(status?.percent ?? 0)}%</span>
              <ProgressBar percent={status?.percent ?? 0} hasErrors={errors.length > 0} className="hidden w-16 md:block" />
            </span>
          ) : starting ? (
            <span className="sr-only text-accent sm:not-sr-only">Starting sync…</span>
          ) : (
            <span className="sr-only sm:not-sr-only">
              <UpdatedAgo prefix="Synced" />
            </span>
          )}
          {errors.length > 0 && (
            <span className="rounded-full bg-warn-fill/15 px-1.5 font-medium text-warn ring-1 ring-warn-fill/40">
              {errors.length}
              <span className="sr-only sm:not-sr-only">{errors.length === 1 ? " error" : " errors"}</span>
            </span>
          )}
        </button>
        <button
          type="button"
          className="inline-flex h-6 w-6 items-center justify-center rounded-full text-fg-2 hover:bg-raised disabled:opacity-50"
          aria-label="Sync now"
          title="Sync now"
          disabled={busy || !allOwners}
          onClick={() => trigger.mutate()}
        >
          <SyncIcon spinning={busy} />
        </button>
      </div>

      {open && (
        <div
          role="dialog"
          aria-label="Sync details"
          className="absolute inset-x-3 top-full z-20 mt-1 rounded-lg border border-line-strong bg-panel shadow-xl sm:inset-x-auto sm:right-0 sm:mt-2 sm:w-lg"
        >
          <div className="flex items-center justify-between border-b border-line px-3 py-2">
            <span className={`text-sm font-medium ${errors.length > 0 ? "text-warn" : "text-fg-2"}`}>
              {running ? "Sync in progress" : errors.length > 0 ? "Sync problems" : "Sync"}
            </span>
            <button
              type="button"
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
            <Link to="/addresses" className="text-accent hover:underline" onClick={() => setOpen(false)}>
              View sync status
            </Link>
            <button
              type="button"
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
