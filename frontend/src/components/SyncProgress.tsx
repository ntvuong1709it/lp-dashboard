import type {
  SyncStage,
  SyncStatus,
  SyncTaskState,
  SyncTaskStatus,
} from "../api/types";
import { formatDate, protocolLabels, shortAddress } from "../lib/format";

const stageLabels: Record<SyncStage, string> = {
  FetchingLatestBlock: "Fetching latest block",
  DiscoveringPositions: "Discovering positions",
  FetchingEvents: "Fetching position events",
  SyncingPositions: "Syncing positions",
  Saving: "Saving",
};

const stateStyles: Record<SyncTaskState, { label: string; className: string }> =
  {
    Pending: { label: "Waiting", className: "bg-slate-700/40 text-slate-400" },
    Running: { label: "Running", className: "bg-sky-500/15 text-sky-300" },
    Succeeded: { label: "Done", className: "bg-emerald-500/15 text-emerald-300" },
    CompletedWithErrors: {
      label: "Done with errors",
      className: "bg-amber-500/15 text-amber-300",
    },
    Failed: { label: "Failed", className: "bg-rose-500/15 text-rose-300" },
  };

const taskName = (t: SyncTaskStatus) =>
  `${t.chain} · ${protocolLabels[t.protocol] ?? t.protocol}`;

function stepText(t: SyncTaskStatus): string {
  if (!t.stage) return "";
  const label =
    t.stage === "SyncingPositions" && t.positionsTotal > 0
      ? `${stageLabels[t.stage]} ${Math.min(t.positionsDone + 1, t.positionsTotal)}/${t.positionsTotal}`
      : stageLabels[t.stage];
  if (!t.detail) return label;
  const count =
    t.detailTotal > 0 ? ` ${t.detailDone}/${t.detailTotal}` : "…";
  return `${label} — ${t.detail}${count}`;
}

/** One-line description of what the sync is doing right now. */
export function describeSync(status: SyncStatus | undefined): string {
  if (!status) return "Loading sync status…";
  if (!status.isRunning) {
    return status.isQueued ? "Sync queued…" : "Idle";
  }
  const index = status.tasks.findIndex((t) => t.state === "Running");
  if (index < 0) return "Preparing sync…";
  const task = status.tasks[index];
  return `${taskName(task)} · ${shortAddress(task.owner)}: ${stepText(task)} (task ${index + 1}/${status.tasks.length})`;
}

export function ProgressBar({
  percent,
  hasErrors = false,
  className = "",
}: {
  percent: number;
  hasErrors?: boolean;
  className?: string;
}) {
  const value = Math.max(0, Math.min(100, percent));
  return (
    <div
      role="progressbar"
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(value)}
      className={`h-1.5 overflow-hidden rounded-full bg-slate-800 ${className}`}
    >
      <div
        className={`h-full rounded-full transition-[width] duration-500 ${hasErrors ? "bg-amber-400" : "bg-sky-500"}`}
        style={{ width: `${value}%` }}
      />
    </div>
  );
}

/** Overall progress + per (wallet, chain/protocol) task breakdown. */
export function SyncProgressDetails({ status }: { status: SyncStatus }) {
  const active = status.isRunning;
  const errorCount = status.lastErrors.length;
  const showBar = active || (!status.isQueued && status.tasks.length > 0);

  return (
    <div className="space-y-3">
      <div>
        <div className="mb-1 flex items-baseline justify-between gap-3 text-xs">
          <span className="text-slate-300">
            {active
              ? describeSync(status)
              : status.isQueued
                ? "Sync queued…"
                : `Last sync finished ${formatDate(status.lastFinishedAt)}`}
          </span>
          {showBar && (
            <span className="num shrink-0 font-medium text-slate-200">
              {Math.round(active ? status.percent : 100)}%
            </span>
          )}
        </div>
        {showBar && (
          <ProgressBar
            percent={active ? status.percent : 100}
            hasErrors={errorCount > 0}
          />
        )}
      </div>

      {status.tasks.length > 0 && (
        <ul className="max-h-64 divide-y divide-slate-800 overflow-y-auto rounded-md border border-slate-800">
          {status.tasks.map((t, i) => {
            const style = stateStyles[t.state];
            return (
              <li key={i} className="space-y-1 px-3 py-2 text-xs">
                <div className="flex items-center gap-2">
                  <span
                    className={`rounded px-1.5 py-0.5 font-medium ${style.className}`}
                  >
                    {t.state === "Running" && (
                      <span className="mr-1 inline-block h-1.5 w-1.5 animate-pulse rounded-full bg-sky-300 align-middle" />
                    )}
                    {style.label}
                  </span>
                  <span className="text-slate-200">{taskName(t)}</span>
                  <span className="font-mono text-slate-500" title={t.owner}>
                    {shortAddress(t.owner)}
                  </span>
                  <span className="num ml-auto text-slate-400">
                    {Math.round(t.percent)}%
                  </span>
                </div>
                {t.state === "Running" && (
                  <>
                    <div className="text-slate-400">{stepText(t)}</div>
                    <ProgressBar
                      percent={t.percent}
                      hasErrors={t.errors.length > 0}
                    />
                  </>
                )}
                {(t.state === "Succeeded" ||
                  t.state === "CompletedWithErrors") && (
                  <div className="text-slate-500">
                    {t.positionsTotal}{" "}
                    {t.positionsTotal === 1 ? "position" : "positions"} processed
                  </div>
                )}
                {t.errors.length > 0 && (
                  <div className="text-amber-300">
                    {t.errors.length}{" "}
                    {t.errors.length === 1 ? "error" : "errors"} — see below
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
