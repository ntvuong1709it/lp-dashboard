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
        className="rounded-md border border-line-strong px-2 py-1 text-xs text-fg-2 hover:bg-raised disabled:opacity-50"
        disabled={busy}
        onClick={() => trigger.mutate(scope)}
      >
        Resync {what}
      </button>
      {rescanOpen ? (
        <>
          <input
            autoFocus
            aria-label="Rescan from block"
            inputMode="numeric"
            className="w-28 rounded-md border border-control bg-surface px-2 py-1 text-xs"
            placeholder="from block"
            value={block}
            onChange={(e) => setBlock(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Escape") setRescanOpen(false);
            }}
          />
          <button
            className="rounded-md bg-accent-solid px-2 py-1 text-xs font-medium hover:bg-accent-fill disabled:opacity-50"
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
            className="text-xs text-muted hover:underline"
            onClick={() => setRescanOpen(false)}
          >
            Cancel
          </button>
        </>
      ) : (
        <button
          className="text-xs text-muted hover:text-fg-2 hover:underline disabled:opacity-50"
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
