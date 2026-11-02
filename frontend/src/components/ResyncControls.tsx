import { useState } from "react";
import { useSyncStatus, useTriggerSync } from "../api/client";
import type { SyncRequestBody } from "../api/types";
import { ErrorText } from "./ui";

/**
 * Re-sync just one wallet or position, or rescan its events from a given block.
 * A rescan only fills gaps (already stored events are not duplicated), so it is for debugging missing events.
 */
export function ResyncControls({
  scope,
  what,
}: {
  scope: SyncRequestBody;
  /** Shown in the button, e.g. "wallet" or "position". */
  what: string;
}) {
  const trigger = useTriggerSync();
  const { data: status } = useSyncStatus();
  const [rescanOpen, setRescanOpen] = useState(false);
  const [block, setBlock] = useState("");

  const busy = status?.isRunning || status?.isQueued || trigger.isPending;
  const blockNumber = /^\d+$/.test(block.trim()) ? Number(block.trim()) : null;

  return (
    <span className="inline-flex flex-wrap items-center gap-2">
      <button
        className="rounded-md border border-slate-700 px-2 py-1 text-xs text-slate-200 hover:bg-slate-800 disabled:opacity-50"
        disabled={busy}
        onClick={() => trigger.mutate(scope)}
      >
        Resync {what}
      </button>
      {rescanOpen ? (
        <>
          <input
            autoFocus
            inputMode="numeric"
            className="w-28 rounded-md border border-slate-700 bg-slate-950 px-2 py-1 text-xs"
            placeholder="from block"
            value={block}
            onChange={(e) => setBlock(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Escape") setRescanOpen(false);
            }}
          />
          <button
            className="rounded-md bg-sky-600 px-2 py-1 text-xs font-medium hover:bg-sky-500 disabled:opacity-50"
            disabled={busy || blockNumber === null}
            onClick={() =>
              trigger.mutate(
                { ...scope, fromBlock: blockNumber ?? undefined },
                { onSuccess: () => setRescanOpen(false) },
              )
            }
          >
            Rescan
          </button>
          <button
            className="text-xs text-slate-400 hover:underline"
            onClick={() => setRescanOpen(false)}
          >
            Cancel
          </button>
        </>
      ) : (
        <button
          className="text-xs text-slate-400 hover:text-slate-200 hover:underline disabled:opacity-50"
          disabled={busy}
          onClick={() => setRescanOpen(true)}
        >
          Rescan from block…
        </button>
      )}
      {trigger.error && <ErrorText error={trigger.error} />}
    </span>
  );
}
