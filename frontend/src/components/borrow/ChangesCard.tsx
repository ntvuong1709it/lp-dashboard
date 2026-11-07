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
    <div className={`flex justify-between gap-3 py-1 text-sm ${strong ? "font-semibold" : ""} ${top ? "mt-1 border-t border-slate-800 pt-2" : ""}`}>
      <span className={strong ? "" : "text-slate-400"}>{label}</span>
      <span className="num">{children}</span>
    </div>
  );
}

const good = (v: number) => <span className={v >= 0 ? "text-emerald-400" : "text-rose-400"}>{formatUsd(v, true)}</span>;
/** Debt rising is bad, falling is good; sub-dollar moves are noise. */
const bad = (v: number) => <span className={v > 0.5 ? "text-rose-400" : v < -0.5 ? "text-emerald-400" : ""}>{formatUsd(v, true)}</span>;

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
              <div className="mb-1 text-xs uppercase tracking-[.05em] text-slate-400">Collateral</div>
              <Row label={`Start, ${shortDay(c.from)}`} strong>{formatUsd(c.collateralStartUsd)}</Row>
              <Row label="Price moves">{good(c.priceMovesUsd)}</Row>
              <Row label="Deposits and withdrawals">{good(c.depositsUsd)}</Row>
              {c.supplyYieldUsd !== 0 && <Row label="Aave supply yield">{good(c.supplyYieldUsd)}</Row>}
              <Row label="Now" strong top>{formatUsd(c.collateralEndUsd)}</Row>
            </div>
            <div>
              <div className="mb-1 text-xs uppercase tracking-[.05em] text-slate-400">Debt</div>
              <Row label={`Start, ${shortDay(c.from)}`} strong>{formatUsd(c.debtStartUsd)}</Row>
              <Row label="Interest added">{bad(c.interestUsd)}</Row>
              <Row label="Borrowed or repaid">{bad(c.borrowedUsd)}</Row>
              {Math.abs(c.debtPriceMovesUsd) >= 0.5 && <Row label="Debt asset price">{bad(c.debtPriceMovesUsd)}</Row>}
              <Row label="Now" strong top>{formatUsd(c.debtEndUsd)}</Row>
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
      <p className="mt-3 text-sm">
        Collateral ÷ debt {moved} from <b className="num">{c.ratioStart.toFixed(2)}×</b> to <b className="num">{c.ratioEnd.toFixed(2)}×</b>.
        {driver && (
          <>
            {" "}Biggest driver: <b>{driver.name}</b> (
            <span className={`num ${driver.usd >= 0 ? "text-emerald-400" : "text-rose-400"}`}>{formatUsd(driver.usd, true)}</span>).
          </>
        )}
      </p>
      <p className="mt-1 text-xs text-slate-500">
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
                <tr className="border-b border-slate-700 align-bottom">
                  <th />
                  {columns.map((c) => (
                    <th key={c.key} className={`px-2.5 pb-2 text-right font-normal ${c.combined ? "bg-slate-800/60" : ""}`}>
                      <span className="block whitespace-nowrap font-semibold">{c.head}</span>
                      <span className="num text-[11px] text-slate-500">{c.sub}</span>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                <Section title="Collateral" columns={columns} />
                <Line label={`Start${from ? `, ${shortDay(from)}`: ""}`} columns={columns} value={(c) => formatUsd(c.collateralStartUsd)} />
                <Line label="Price moves" columns={columns} value={(c) => good(c.priceMovesUsd)} />
                <Line label="Deposits and withdrawals" columns={columns} value={(c) => good(c.depositsUsd)} />
                {any((c) => c.supplyYieldUsd) && <Line label="Aave supply yield" columns={columns} value={(c) => good(c.supplyYieldUsd)} />}
                <Line label="Now" columns={columns} value={(c) => formatUsd(c.collateralEndUsd)} total />

                <Section title="Debt" columns={columns} />
                <Line label={`Start${from ? `, ${shortDay(from)}`: ""}`} columns={columns} value={(c) => formatUsd(c.debtStartUsd)} />
                <Line label="Interest added" columns={columns} value={(c) => bad(c.interestUsd)} />
                <Line label="Borrowed or repaid" columns={columns} value={(c) => bad(c.borrowedUsd)} />
                {any((c) => c.debtPriceMovesUsd, 0.5) && <Line label="Debt asset price" columns={columns} value={(c) => bad(c.debtPriceMovesUsd)} />}
                <Line label="Now" columns={columns} value={(c) => formatUsd(c.debtEndUsd)} total />

                <Section title="Collateral ÷ debt" columns={columns} />
                <Line label="Start → now" columns={columns} value={ratioCell} total />
              </tbody>
            </table>
          </div>

          <div className="mt-4 grid gap-2.5 sm:grid-cols-2 lg:grid-cols-3">
            {columns.map((c) => (
              <div key={c.key} className="min-w-0 rounded-md bg-slate-800/60 px-3 py-2.5 text-sm">
                <span className="mb-1 block font-semibold">{c.head}</span>
                <Takeaway c={c.data!} />
              </div>
            ))}
          </div>
          <p className="mt-3 text-xs text-slate-500">
            Each column is worked out day by day, so each deposit counts at the price of its day and the parts add up to its total.
            Only wallets with a loan are listed. A price move in the debt asset shows only when it is over 50 cents.
          </p>
        </>
      )}
    </Panel>
  );
}

function Section({ title, columns }: { title: string; columns: Column[] }) {
  return (
    <tr>
      <td className="pt-4 text-[11px] uppercase tracking-[.05em] text-slate-400">{title}</td>
      {columns.map((c) => (
        <td key={c.key} className={c.combined ? "bg-slate-800/60" : ""} />
      ))}
    </tr>
  );
}

function Line({
  label,
  columns,
  value,
  total,
}: {
  label: string;
  columns: Column[];
  value: (c: ChangesDto) => ReactNode;
  total?: boolean;
}) {
  return (
    <tr className={total ? "border-t border-slate-700 font-semibold" : ""}>
      <td className={`whitespace-nowrap px-2.5 py-1.5 ${total ? "" : "text-slate-400"}`}>{label}</td>
      {columns.map((c) => (
        <td key={c.key} className={`whitespace-nowrap px-2.5 py-1.5 text-right num ${c.combined ? "bg-slate-800/60" : ""}`}>
          {c.data ? value(c.data) : null}
        </td>
      ))}
    </tr>
  );
}

function ratioCell(c: ChangesDto) {
  if (c.ratioStart == null || c.ratioEnd == null) return <span className="text-slate-500">–</span>;
  const up = c.ratioEnd >= c.ratioStart;
  return (
    <span className={up ? "text-emerald-400" : "text-rose-400"}>
      {c.ratioStart.toFixed(2)}× → {c.ratioEnd.toFixed(2)}× {up ? "▲" : "▼"}
    </span>
  );
}

function Takeaway({ c }: { c: ChangesDto }) {
  if (c.ratioStart == null || c.ratioEnd == null) return <span className="text-slate-500">No debt to compare.</span>;
  const driver = biggestDriver(c);
  return (
    <span>
      Ratio {c.ratioEnd < c.ratioStart ? "fell" : "rose"} from <b className="num">{c.ratioStart.toFixed(2)}×</b> to{" "}
      <b className="num">{c.ratioEnd.toFixed(2)}×</b>.
      {driver && (
        <>
          {" "}Biggest driver: {driver.name} (
          <span className={`num ${driver.usd >= 0 ? "text-emerald-400" : "text-rose-400"}`}>{formatUsd(driver.usd, true)}</span>).
        </>
      )}
    </span>
  );
}
