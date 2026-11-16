import { Link } from "react-router-dom";
import { usePosition } from "../../api/client";
import type { PositionDto, RangeDto } from "../../api/types";
import { isNearEdge, pairLabel } from "../../lib/attention";
import { formatFee, formatPercent, formatPrice, formatUsd, invertRange, protocolLabels } from "../../lib/format";
import { usePositionDrawer } from "../../lib/positionDrawer";
import { useHideBalances } from "../../lib/privacy";
import { usePersistentState } from "../../lib/usePersistentState";
import { useWallets, type WalletInfo } from "../../lib/wallets";
import { RangeBadge, RangeBar } from "../RangeBar";
import { Empty, ErrorText, Panel, Skeleton } from "../ui";
import { WalletChip } from "../WalletChip";

type Status = "out" | "edge" | "in" | "unknown";

function statusOf(range: RangeDto | null): Status {
  if (!range || range.inRange == null) return "unknown";
  if (!range.inRange) return "out";
  return isNearEdge(range) ? "edge" : "in";
}

const STATUS_ORDER: Record<Status, number> = { out: 0, edge: 1, in: 2, unknown: 3 };

const cardBorder: Record<Status, string> = {
  out: "border-down-fill/60",
  edge: "border-warn-fill/60",
  in: "border-line",
  unknown: "border-line",
};

/** How far the price is from the range, in the direction the prices are shown. */
function distanceText(range: RangeDto): { text: string; className: string } | null {
  const { priceLower: lo, priceUpper: hi, priceCurrent: cur } = range;
  if (cur == null || !(lo > 0) || !(cur > 0)) return null;
  if (cur < lo) return { text: `${(((lo - cur) / lo) * 100).toFixed(1)}% below range`, className: "text-down" };
  if (cur > hi) return { text: `${(((cur - hi) / hi) * 100).toFixed(1)}% above range`, className: "text-down" };
  const toLower = (cur - lo) / cur;
  const toUpper = (hi - cur) / cur;
  const lower = toLower <= toUpper;
  const text = `${(Math.min(toLower, toUpper) * 100).toFixed(1)}% to ${lower ? "lower" : "upper"} edge`;
  return { text, className: isNearEdge(range) ? "text-warn" : "text-muted" };
}

/**
 * Every open position as a card: where the price sits in its range, where you entered, how far the price is from an edge,
 * and what it is worth. Out of range first, then near an edge, then the rest by value. Prices follow the direction chosen
 * on the Positions page.
 */
export function PositionRangeCards({
  open,
  loading,
  error,
}: {
  open: PositionDto[];
  loading: boolean;
  error: unknown;
}) {
  useHideBalances(); // values are masked when balances are hidden
  const walletOf = useWallets();
  const [inverted] = usePersistentState("lpdash.portfolio.inverted", false);

  const sorted = [...open].sort(
    (a, b) =>
      STATUS_ORDER[statusOf(a.range)] - STATUS_ORDER[statusOf(b.range)] ||
      b.metrics.currentValueUsd - a.metrics.currentValueUsd,
  );
  const showWallet = new Set(open.map((p) => p.owner.toLowerCase())).size > 1;

  return (
    <Panel
      title="Positions"
      actions={
        <span className="flex items-center gap-3 text-xs text-muted">
          <span className="hidden sm:inline">Worst first. White mark: price now. Blue dot: your entry.</span>
          <Link to="/positions" className="font-medium text-accent hover:underline">
            All positions
          </Link>
        </span>
      }
    >
      {error ? (
        <ErrorText error={error} />
      ) : loading ? (
        <Skeleton className="h-56 w-full" />
      ) : !sorted.length ? (
        <Empty>
          No open positions.{" "}
          <Link className="text-accent underline" to="/addresses">
            Add a wallet
          </Link>
        </Empty>
      ) : (
        <ul className="grid gap-3 min-[560px]:grid-cols-2">
          {sorted.map((p) => (
            <li key={p.id} className="min-w-0">
              <PositionCard p={p} inverted={inverted} wallet={showWallet ? walletOf(p.owner) : null} />
            </li>
          ))}
        </ul>
      )}
    </Panel>
  );
}

function PositionCard({ p, inverted, wallet }: { p: PositionDto; inverted: boolean; wallet: WalletInfo | null }) {
  const drawer = usePositionDrawer();
  const status = statusOf(p.range);
  const range = p.range && (inverted ? invertRange(p.range) : p.range);
  const [base, quote] = inverted ? [p.token1, p.token0] : [p.token0, p.token1];
  const dist = range && distanceText(range);

  // The pool price at the first deposit, in the same units as the range. It comes from the position's events, so the
  // card loads them (cached, and shared with the position drawer).
  const detail = usePosition(p.id);
  const first = detail.data?.events.find((e) => e.type === "IncreaseLiquidity" && e.price0Usd && e.price1Usd);
  const entry0 = first ? first.price0Usd! / first.price1Usd! : null;
  const entry = entry0 == null ? null : inverted ? 1 / entry0 : entry0;
  const current = range?.priceCurrent ?? null;
  const sinceEntry = entry != null && current != null && entry > 0 ? (current / entry - 1) * 100 : null;

  return (
    <button
      type="button"
      onClick={() => drawer.open(p.id)}
      className={`block h-full w-full rounded-lg border bg-surface p-3 text-left hover:bg-raised/40 ${cardBorder[status]}`}
    >
      <div className="flex items-center justify-between gap-2">
        <span className="min-w-0 truncate font-semibold">
          {inverted ? `${p.token1?.symbol ?? "?"}/${p.token0?.symbol ?? "?"}` : pairLabel(p)}{" "}
          <span className="text-xs font-normal text-faint">{formatFee(p.fee)}</span>
        </span>
        <RangeBadge inRange={p.range?.inRange ?? null} nearEdge={isNearEdge(p.range)} />
      </div>
      <div className="mt-0.5 flex flex-wrap items-center gap-x-2 text-xs text-faint">
        <span>
          {protocolLabels[p.protocol]} · {p.chain} · #{p.tokenId}
        </span>
        {wallet && <WalletChip wallet={wallet} />}
      </div>
      <div className="mt-3">
        {range ? (
          <>
            <RangeBar range={range} showLabels entry={entry} />
            <div className="mt-0.5 flex flex-wrap items-center justify-between gap-x-3 text-[11px] text-faint">
              <span>
                {quote?.symbol} per {base?.symbol}
              </span>
              {entry != null ? (
                <span className="inline-flex items-center gap-1.5">
                  <span
                    aria-hidden="true"
                    className="inline-block h-2.5 w-2.5 rounded-full border-2 border-[var(--marker)] bg-accent-fill"
                  />
                  Entry <span className="num font-medium text-fg-2">{formatPrice(entry)}</span>
                  {sinceEntry != null && (
                    <span className="num">
                      ({sinceEntry > 0 ? "+" : ""}
                      {sinceEntry.toFixed(1)}% since)
                    </span>
                  )}
                </span>
              ) : (
                detail.isLoading && <span>Entry…</span>
              )}
            </div>
          </>
        ) : (
          <div className="text-xs text-faint">No range for this position</div>
        )}
      </div>
      <div className="mt-2 flex items-baseline justify-between gap-2 text-xs">
        <span className="num font-medium text-fg">{formatUsd(p.metrics.currentValueUsd)}</span>
        {dist && <span className={dist.className}>{dist.text}</span>}
        <span className="num text-muted">
          {p.metrics.feeAprPercent != null ? `${formatPercent(p.metrics.feeAprPercent)} APR` : "no APR yet"}
        </span>
      </div>
    </button>
  );
}
