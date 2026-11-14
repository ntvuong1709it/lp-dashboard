import type { ReactNode } from "react";
import { useBorrowChanges, useBorrowChangesByWallet } from "../../api/client";
import type { ChangesDto, LoanDto } from "../../api/types";
import { biggestDriver, shortDay, walletsWithLoans } from "../../lib/borrow";
import { formatUsd } from "../../lib/format";
import { useOwner } from "../../lib/owner";
import { useWallets } from "../../lib/wallets";
import { ErrorText, Panel, Skeleton } from "../ui";
import { WalletChip } from "../WalletChip";

function Row({ label, children, strong, top }: { label: string; children: ReactNode; strong?: boolean; top?: boolean }) {
  return (
    <div className={`flex justify-between gap-3 py-1 text-sm ${strong ? "font-semibold" : ""} ${top ? "mt-1 border-t border-line pt-2" : ""}`}>
      <span className={strong ? "" : "text-muted"}>{label}</span>
      <span className="num">{children}</span>
    </div>
  );
}

const good = (v: number) => <span className={v >= 0 ? "text-up" : "text-down"}>{formatUsd(v, true)}</span>;
/** Debt rising is bad, falling is good; sub-dollar moves are noise. */
const bad = (v: number) => <span className={v > 0.5 ? "text-down" : v < -0.5 ? "text-up" : ""}>{formatUsd(v, true)}</span>;

/** End value with its % change against the start. For debt, rising is bad, so the colours flip. */
function nowWithPct(start: number, end: number, debt?: boolean) {
  if (start <= 0) return <>{formatUsd(end)}</>;
  const pct = ((end - start) / start) * 100;
  const flat = Math.abs(pct) < 0.05;
  const goodMove = debt ? pct < 0 : pct > 0;
  const color = flat ? "text-muted" : goodMove ? "text-up" : "text-down";
  return (
    <>
      {formatUsd(end)}{" "}
      <span className={`text-xs font-normal ${color}`}>
        {flat ? "0.0%" : `${pct > 0 ? "▲ +" : "▼ −"}${Math.abs(pct).toFixed(1)}%`}
      </span>
    </>
  );
}

/**
 * What moved collateral and debt over the window, in dollars: market, your own transactions, or interest.
 * With several wallets followed it shows one column per wallet beside the combined one.
 */
export function ChangesCard({ days, loan, loans }: { days: number; loan: string; loans: LoanDto[] }) {
  const { addresses, selected } = useOwner();
  const wallets = selected ? [] : walletsWithLoans(addresses.map((a) => a.address), loans);
  return wallets.length > 1 ? (
    <ByWallet days={days} loan={loan} wallets={wallets} />
  ) : (
    <Combined days={days} loan={loan} />
  );
}

function Combined({ days, loan }: { days: number; loan: string }) {
  const { owner } = useOwner();
  const changes = useBorrowChanges(owner, days, loan);
  const c = changes.data;

  return (
    <Panel title={`What changed in ${days} days`}>
      {changes.error ? (
        <ErrorText error={changes.error} />
      ) : !c ? (
        <Skeleton className="h-40 w-full" />
      ) : (
        <>
          <div className="grid gap-5 sm:grid-cols-2">
            <div>
              <Band tone="col">Collateral</Band>
              <Row label={`Start, ${shortDay(c.from)}`} strong>{formatUsd(c.collateralStartUsd)}</Row>
              <Row label="Price moves">{good(c.priceMovesUsd)}</Row>
              <Row label="Deposits and withdrawals">{good(c.depositsUsd)}</Row>
              {c.supplyYieldUsd !== 0 && <Row label="Aave supply yield">{good(c.supplyYieldUsd)}</Row>}
              <Row label="Now" strong top>{nowWithPct(c.collateralStartUsd, c.collateralEndUsd)}</Row>
            </div>
            <div>
              <Band tone="debt">Debt</Band>
              <Row label={`Start, ${shortDay(c.from)}`} strong>{formatUsd(c.debtStartUsd)}</Row>
              <Row label="Interest added">{bad(c.interestUsd)}</Row>
              <Row label="Borrowed or repaid">{bad(c.borrowedUsd)}</Row>
              {Math.abs(c.debtPriceMovesUsd) >= 0.5 && <Row label="Debt asset price">{bad(c.debtPriceMovesUsd)}</Row>}
              <Row label="Now" strong top>{nowWithPct(c.debtStartUsd, c.debtEndUsd, true)}</Row>
            </div>
          </div>
          <Summary c={c} />
        </>
      )}
    </Panel>
  );
}

function Summary({ c }: { c: ChangesDto }) {
  const driver = biggestDriver(c);
  if (c.ratioStart == null || c.ratioEnd == null) return null;
  const moved = c.ratioEnd < c.ratioStart ? "fell" : "rose";
  return (
    <>
      <p className={`mt-4 px-2.5 py-2 text-sm ${TONES.ratio.band} ${TONES.ratio.bar}`}>
        <b className="mr-1.5 text-[15px]">Collateral ÷ debt</b> {moved} from <b className="num">{c.ratioStart.toFixed(2)}×</b> to <b className="num">{c.ratioEnd.toFixed(2)}×</b>.
        {driver && (
          <>
            {" "}Biggest driver: <b>{driver.name}</b> (
            <span className={`num ${driver.usd >= 0 ? "text-up" : "text-down"}`}>{formatUsd(driver.usd, true)}</span>).
          </>
        )}
      </p>
      <p className="mt-1 text-xs text-faint">
        Worked out day by day, so each deposit counts at the price of its day and the parts add up to the total. A price move in the debt asset shows only when it is over 50 cents.
      </p>
    </>
  );
}

// ---- several wallets ---------------------------------------------------------------------------------------------

interface Column {
  key: string;
  head: ReactNode;
  sub: string;
  data: ChangesDto | undefined;
  combined?: boolean;
}

function ByWallet({ days, loan, wallets }: { days: number; loan: string; wallets: string[] }) {
  const { owner } = useOwner();
  const all = useBorrowChanges(owner, days, loan);
  const per = useBorrowChangesByWallet(wallets, days, loan);
  const walletOf = useWallets();

  const error = all.error ?? per.find((q) => q.error)?.error;
  const columns: Column[] = [
    { key: "all", head: "All wallets", sub: "combined", data: all.data, combined: true },
    ...wallets.map((w, i) => ({
      key: w,
      head: <WalletChip wallet={walletOf(w)} />,
      sub: walletOf(w).short,
      data: per[i]?.data,
    })),
  ];
  const ready = columns.every((c) => c.data);
  const any = (f: (c: ChangesDto) => number, min = 0) => columns.some((c) => c.data && Math.abs(f(c.data)) > min);
  const from = all.data?.from;

  return (
    <Panel title={`What changed in ${days} days`}>
      {error ? (
        <ErrorText error={error} />
      ) : !ready ? (
        <Skeleton className="h-56 w-full" />
      ) : (
        <>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[34rem] border-collapse text-sm">
              <thead>
                <tr className="border-b border-line-strong align-bottom">
                  <th />
                  {columns.map((c) => (
                    <th key={c.key} className={`px-2.5 pb-2 text-right font-normal ${c.combined ? "bg-raised/60" : ""}`}>
                      <span className="block whitespace-nowrap font-semibold">{c.head}</span>
                      <span className="num text-[11px] text-faint">{c.sub}</span>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                <Section title="Collateral" tone="col" columns={columns} />
                <Line label={`Start${from ? `, ${shortDay(from)}`: ""}`} columns={columns} value={(c) => formatUsd(c.collateralStartUsd)} />
                <Line label="Price moves" columns={columns} value={(c) => good(c.priceMovesUsd)} />
                <Line label="Deposits and withdrawals" columns={columns} value={(c) => good(c.depositsUsd)} />
                {any((c) => c.supplyYieldUsd) && <Line label="Aave supply yield" columns={columns} value={(c) => good(c.supplyYieldUsd)} />}
                <Line label="Now" columns={columns} value={(c) => nowWithPct(c.collateralStartUsd, c.collateralEndUsd)} total tone="col" />

                <Section title="Debt" tone="debt" columns={columns} />
                <Line label={`Start${from ? `, ${shortDay(from)}`: ""}`} columns={columns} value={(c) => formatUsd(c.debtStartUsd)} />
                <Line label="Interest added" columns={columns} value={(c) => bad(c.interestUsd)} />
                <Line label="Borrowed or repaid" columns={columns} value={(c) => bad(c.borrowedUsd)} />
                {any((c) => c.debtPriceMovesUsd, 0.5) && <Line label="Debt asset price" columns={columns} value={(c) => bad(c.debtPriceMovesUsd)} />}
                <Line label="Now" columns={columns} value={(c) => nowWithPct(c.debtStartUsd, c.debtEndUsd, true)} total tone="debt" />

                <Section title="Collateral ÷ debt" tone="ratio" columns={columns} />
                <Line label="Start → now" columns={columns} value={ratioCell} total tone="ratio" />
                <Line label="Biggest driver" columns={columns} value={driverCell} wrap />
              </tbody>
            </table>
          </div>

          <p className="mt-3 text-xs text-faint">
            Each column is worked out day by day, so each deposit counts at the price of its day and the parts add up to its total.
            Only wallets with a loan are listed. A price move in the debt asset shows only when it is over 50 cents.
          </p>
        </>
      )}
    </Panel>
  );
}

type Tone = "col" | "debt" | "ratio";

/** Written out in full so Tailwind sees every class. `band` is the heading row, `tint` the "Now" row under it. */
const TONES: Record<Tone, { band: string; bar: string; tint: string }> = {
  col: { band: "bg-(--band-col-bg) text-(--band-col)", bar: "shadow-[inset_3px_0_0_var(--band-col-bar)]", tint: "bg-(--band-col-bg)" },
  debt: { band: "bg-(--band-debt-bg) text-(--band-debt)", bar: "shadow-[inset_3px_0_0_var(--band-debt-bar)]", tint: "bg-(--band-debt-bg)" },
  ratio: { band: "bg-(--band-ratio-bg) text-(--band-ratio)", bar: "shadow-[inset_3px_0_0_var(--band-ratio-bar)]", tint: "bg-(--band-ratio-bg)" },
};

/** The same band as a block heading, for the single-wallet layout. */
function Band({ tone, children }: { tone: Tone; children: ReactNode }) {
  const t = TONES[tone];
  return <div className={`mb-1 px-2.5 py-2 text-[15px] font-semibold ${t.band} ${t.bar}`}>{children}</div>;
}

/** A tinted heading band for Collateral, Debt or Collateral ÷ debt, with a gap above it. */
function Section({ title, tone, columns }: { title: string; tone: Tone; columns: Column[] }) {
  const t = TONES[tone];
  return (
    <>
      <tr aria-hidden>
        <td className="h-3" />
        {columns.map((c) => (
          <td key={c.key} className={c.combined ? "bg-raised/60" : ""} />
        ))}
      </tr>
      <tr>
        <th scope="rowgroup" className={`px-2.5 py-2 text-left text-[15px] font-semibold ${t.band} ${t.bar}`}>{title}</th>
        {columns.map((c) => (
          <td key={c.key} className={t.band} />
        ))}
      </tr>
    </>
  );
}

function Line({
  label,
  columns,
  value,
  total,
  tone,
  wrap,
}: {
  label: string;
  columns: Column[];
  value: (c: ChangesDto) => ReactNode;
  total?: boolean;
  tone?: Tone;
  wrap?: boolean;
}) {
  const t = tone ? TONES[tone] : null;
  const bg = (c?: Column) => (t ? t.tint : c?.combined ? "bg-raised/60" : "");
  return (
    <tr className={total ? `font-semibold ${t ? "text-[15px]" : "border-t border-line-strong"}` : ""}>
      <td className={`whitespace-nowrap px-2.5 py-1.5 ${total ? "" : "text-muted"} ${bg()} ${t?.bar ?? ""}`}>{label}</td>
      {columns.map((c) => (
        <td
          key={c.key}
          className={`px-2.5 py-1.5 text-right ${wrap ? "align-top" : "num whitespace-nowrap"} ${bg(c)}`}
        >
          {c.data ? value(c.data) : null}
        </td>
      ))}
    </tr>
  );
}

function ratioCell(c: ChangesDto) {
  if (c.ratioStart == null || c.ratioEnd == null) return <span className="text-faint">–</span>;
  const up = c.ratioEnd >= c.ratioStart;
  return (
    <span className={up ? "text-up" : "text-down"}>
      {c.ratioStart.toFixed(2)}× → {c.ratioEnd.toFixed(2)}× {up ? "▲" : "▼"}
    </span>
  );
}

/** What moved the ratio most, in the same column as the numbers above it. */
function driverCell(c: ChangesDto) {
  const driver = c.ratioStart == null || c.ratioEnd == null ? null : biggestDriver(c);
  if (!driver) return <span className="text-faint">–</span>;
  return (
    <>
      <span className="block text-fg-2">{driver.name}</span>
      <span className={`num ${driver.usd >= 0 ? "text-up" : "text-down"}`}>{formatUsd(driver.usd, true)}</span>
    </>
  );
}
