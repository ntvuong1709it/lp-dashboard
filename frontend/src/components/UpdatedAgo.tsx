import { useEffect, useState } from "react";
import { useSyncStatus } from "../api/client";
import { formatAgo, formatDate } from "../lib/format";

/** Re-renders every `intervalMs` so relative times stay current. */
function useNow(intervalMs = 30_000) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(id);
  }, [intervalMs]);
  return now;
}

/** "Updated 5 min ago", based on when the last sync finished. */
export function UpdatedAgo({ prefix = "Updated" }: { prefix?: string }) {
  const { data: status } = useSyncStatus();
  const now = useNow();
  if (!status) return null;

  return (
    <span
      className="text-xs text-faint"
      title={status.lastFinishedAt ? formatDate(status.lastFinishedAt) : undefined}
    >
      {status.lastFinishedAt
        ? `${prefix} ${formatAgo(status.lastFinishedAt, now)}`
        : "Not synced yet"}
    </span>
  );
}
