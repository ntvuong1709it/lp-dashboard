import { useStarPool } from "../../api/risk";
import type { PoolDto } from "../../api/types";

/**
 * Follows a pool. Followed pools have their tokens re-checked every night, and are listed on the Risk page.
 * Never triggers the row's click, so it can sit inside a table row.
 */
export function StarButton({ pool, className = "" }: { pool: PoolDto; className?: string }) {
  const star = useStarPool();
  const starred = star.isPending ? !pool.starred : pool.starred;
  return (
    <button
      type="button"
      aria-pressed={starred}
      aria-label={starred ? `Stop following ${pool.symbol}` : `Follow ${pool.symbol}`}
      title={starred ? "Following: re-checked every night. Click to stop." : "Follow this pool: its tokens are re-checked every night"}
      disabled={star.isPending}
      onClick={(e) => {
        e.stopPropagation();
        star.mutate({ poolId: pool.id, starred: !pool.starred });
      }}
      onKeyDown={(e) => e.stopPropagation()}
      className={`rounded px-1 text-base leading-none hover:bg-slate-800 focus-visible:outline-2 focus-visible:outline-sky-400 ${starred ? "text-amber-400" : "text-slate-600 hover:text-slate-300"} ${className}`}
    >
      <span aria-hidden="true">{starred ? "★" : "☆"}</span>
    </button>
  );
}
