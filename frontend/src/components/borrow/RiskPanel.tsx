import type { LoanDto, RiskLevel } from "../../api/types";
import {
  HEALTH_WARN,
  LTV_WARN_SHARE,
  fixForHealthFactor,
  fixForLtv,
  hfDrop,
  ltvDrop,
} from "../../lib/borrow";
import { formatUsd } from "../../lib/format";
import { useOwner } from "../../lib/owner";
import { useWallets, type WalletInfo } from "../../lib/wallets";
import { Panel } from "../ui";
import { WalletChip } from "../WalletChip";
import { HealthGauge, LevelChip, LtvGauge } from "./bits";

const SEVERITY: Record<RiskLevel, number> = { Ok: 0, Warn: 1, Critical: 2 };

/** One gauge: a wallet's Aave account (all its debts share one health factor) or one manual loan. */
interface Entry {
  id: string;
  title: string;
  note: string;
  health: boolean;
  value: number;
  level: RiskLevel;
  liquidationLtv: number | null;
  marginCallLtv: number | null;
  collateralUsd: number | null;
  debtUsd: number;
  /** Percent the collateral can fall before liquidation. */
  drop: number | null;
}

interface WalletRisk {
  owner: string;
  entries: Entry[];
  unknown: LoanDto[];
  level: RiskLevel;
  /** Smallest cushion across the wallet's entries. */
  drop: number | null;
}

function entriesOf(loans: LoanDto[]): { entries: Entry[]; unknown: LoanDto[] } {
  const entries: Entry[] = [];
  const unknown: LoanDto[] = [];
  const aave = loans.filter((l) => l.kind === "aave" && l.risk.kind === "HealthFactor" && l.risk.value != null);

  if (aave.length > 0) {
    const r = aave[0].risk;
    const value = r.value!;
    entries.push({
      id: "aave",
      title: "Aave v3 health factor",
      note: `${aave.length} ${aave.length > 1 ? "loans" : "loan"}: ${aave.map((l) => l.label).join(", ")}`,
      health: true,
      value,
      level: r.level,
      liquidationLtv: null,
      marginCallLtv: null,
      collateralUsd: aave[0].collateralUsd,
      debtUsd: aave.reduce((sum, l) => sum + l.debtUsd, 0),
      drop: hfDrop(value),
    });
  }

  for (const l of loans) {
    if (aave.includes(l)) continue;
    const r = l.risk;
    if (r.kind === "None" || r.value == null) {
      unknown.push(l);
      continue;
    }
    entries.push({
      id: l.key,
      title: `${l.source} loan, manual entry`,
      note: l.label,
      health: r.kind === "HealthFactor",
      value: r.value,
      level: r.level,
      liquidationLtv: r.liquidationLtvPercent,
      marginCallLtv: r.marginCallLtvPercent,
      collateralUsd: l.collateralUsd,
      debtUsd: l.debtUsd,
      drop: r.kind === "HealthFactor" ? hfDrop(r.value) : r.liquidationLtvPercent != null ? ltvDrop(r.value, r.liquidationLtvPercent) : null,
    });
  }
  return { entries, unknown };
}

function byWallet(loans: LoanDto[], order: string[]): WalletRisk[] {
  const owners = [...new Set(loans.map((l) => l.owner))];
  const risks = owners.map<WalletRisk>((owner) => {
    const { entries, unknown } = entriesOf(loans.filter((l) => l.owner === owner));
    const drops = entries.map((e) => e.drop).filter((d): d is number => d != null);
    return {
      owner,
      entries,
      unknown,
      level: entries.reduce<RiskLevel>((worst, e) => (SEVERITY[e.level] > SEVERITY[worst] ? e.level : worst), "Ok"),
      drop: drops.length > 0 ? Math.min(...drops) : null,
    };
  });
  // Closest to liquidation first; wallets with no figure last, in the order they are followed.
  return risks.sort(
    (a, b) =>
      (a.drop ?? Infinity) - (b.drop ?? Infinity) ||
      order.indexOf(a.owner) - order.indexOf(b.owner),
  );
}

/** How close each wallet is to liquidation: Aave's health factor, or a manual loan's LTV against its limits. */
export function RiskPanel({ loans }: { loans: LoanDto[] }) {
  const { addresses } = useOwner();
  const walletOf = useWallets();
  const wallets = byWallet(loans, addresses.map((a) => a.address));
  const withFigures = wallets.filter((w) => w.entries.length > 0);
  const closest = withFigures[0];

  return (
    <Panel
      title="Liquidation risk"
      actions={<span className="text-xs text-slate-500">Closest first. Warn below health factor 1.5, critical below 1.2.</span>}
    >
      {withFigures.length === 0 ? (
        <p className="text-sm text-slate-500">
          No loan has risk data yet. For a manual loan, add its collateral and liquidation LTV.
        </p>
      ) : (
        <>
          {withFigures.length > 1 && closest.drop != null && <Closest wallet={closest} name={walletOf(closest.owner).name} others={withFigures.slice(1)} nameOf={(o) => walletOf(o).name} />}
          <div className="grid gap-3 md:grid-cols-[repeat(auto-fit,minmax(20rem,1fr))]">
            {wallets.map((w) => (
              <WalletCard key={w.owner} wallet={w} info={walletOf(w.owner)} />
            ))}
          </div>
          <p className="mt-3 text-xs text-slate-500">
            The cushion assumes only the collateral's price moves and the debt stays the same. A manual loan uses the collateral and
            limits you entered, so update them when they change.
          </p>
        </>
      )}
    </Panel>
  );
}

function Closest({
  wallet,
  name,
  others,
  nameOf,
}: {
  wallet: WalletRisk;
  name: string;
  others: WalletRisk[];
  nameOf: (owner: string) => string;
}) {
  const alert = wallet.level !== "Ok";
  const tone =
    wallet.level === "Critical"
      ? "border-rose-500/60 bg-rose-500/10"
      : wallet.level === "Warn"
        ? "border-amber-500/60 bg-amber-500/10"
        : "border-slate-800 bg-slate-800/40";
  return (
    <div className={`mb-3 rounded-lg border px-3.5 py-2.5 text-sm ${tone}`}>
      <b>{name} is closest to liquidation.</b> Its collateral can fall about {wallet.drop!.toFixed(0)}% before it is liquidated
      {others.length > 0 && (
        <>
          {" "}
          ({others.filter((o) => o.drop != null).map((o, i) => `${i ? ", " : ""}${nameOf(o.owner)} about ${o.drop!.toFixed(0)}%`)}).
        </>
      )}
      {!alert && <span className="text-slate-400"> All wallets are above the warning level.</span>}
    </div>
  );
}

function WalletCard({ wallet, info }: { wallet: WalletRisk; info: WalletInfo }) {
  const { level, entries, unknown } = wallet;
  const ring = level === "Critical" ? "ring-1 ring-rose-500/70" : level === "Warn" ? "ring-1 ring-amber-500/70" : "";
  return (
    <article className={`min-w-0 rounded-lg border border-slate-800 bg-slate-900 p-3.5 ${ring}`} style={{ borderTop: `3px solid ${info.color}` }}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="inline-flex flex-wrap items-center gap-x-2">
          <WalletChip wallet={info} />
          {info.label && <span className="num text-[11px] text-slate-500">{info.short}</span>}
        </span>
        {entries.length > 0 && <LevelChip level={level} />}
      </div>
      {entries.map((e) => (
        <EntryBlock key={e.id} e={e} />
      ))}
      {entries.length === 0 && <p className="mt-3 text-sm text-slate-500">No risk figure yet.</p>}
      {unknown.map((l) => (
        <p key={l.key} className="mt-3 border-t border-slate-800 pt-3 text-xs text-slate-500">
          {l.label}: add its collateral and liquidation LTV to see its risk.
        </p>
      ))}
    </article>
  );
}

function EntryBlock({ e }: { e: Entry }) {
  const liquidationAt = e.collateralUsd != null && e.drop != null ? e.collateralUsd * (1 - e.drop / 100) : null;
  const cushionUsd = e.collateralUsd != null && liquidationAt != null ? e.collateralUsd - liquidationAt : null;
  const fix =
    e.level === "Ok" || e.collateralUsd == null
      ? null
      : e.health
        ? fixForHealthFactor(e.collateralUsd, e.debtUsd, e.value, HEALTH_WARN)
        : e.liquidationLtv != null
          ? fixForLtv(e.collateralUsd, e.debtUsd, e.liquidationLtv * (1 - LTV_WARN_SHARE))
          : null;
  const marginDrop = !e.health && e.marginCallLtv != null ? ltvDrop(e.value, e.marginCallLtv) : null;

  return (
    <div className="mt-3 border-t border-slate-800 pt-3 first-of-type:mt-3">
      <div className="flex flex-wrap items-baseline justify-between gap-x-2 text-xs text-slate-400">
        <span>{e.title}</span>
        <span>{e.health ? "liquidated at 1.00" : `liquidation LTV ${e.liquidationLtv ?? 80}%`}</span>
      </div>
      <div className="mt-1 flex flex-wrap items-baseline gap-x-2.5">
        <span className="text-3xl font-medium tracking-tight num">{e.health ? e.value.toFixed(2) : `${e.value.toFixed(0)}%`}</span>
        <span className="text-xs text-slate-400">{e.health ? e.note : `LTV · ${e.note}`}</span>
      </div>
      {e.health ? (
        <HealthGauge value={e.value} />
      ) : (
        <LtvGauge value={e.value} liquidation={e.liquidationLtv ?? 80} marginCall={e.marginCallLtv} />
      )}
      {e.collateralUsd != null && (
        <div className="grid grid-cols-3 gap-2">
          <Fact label="Collateral now" value={formatUsd(e.collateralUsd)} />
          <Fact label="Debt" value={formatUsd(e.debtUsd)} />
          <Fact label="Collateral at liquidation" value={liquidationAt != null ? formatUsd(liquidationAt) : "–"} tone="text-rose-400" />
        </div>
      )}
      {e.drop != null && (
        <p className="mt-2.5 rounded-md bg-slate-800/60 px-2.5 py-2 text-sm">
          <b>Cushion: about {e.drop.toFixed(0)}%</b>
          {cushionUsd != null && <> ({formatUsd(cushionUsd)})</>}
          {e.level === "Ok" ? " before liquidation." : ". If your collateral loses that much while the debt stays the same, it can be liquidated."}
          {marginDrop != null && <> A margin call comes first, at about {marginDrop.toFixed(0)}% lower.</>}
          {fix && fix.repayUsd > 0 && (
            <> Repaying about {formatUsd(fix.repayUsd)} would {e.health ? `bring the health factor to ${HEALTH_WARN.toFixed(2)}` : "move it out of the warning zone"}.</>
          )}
        </p>
      )}
    </div>
  );
}

function Fact({ label, value, tone = "" }: { label: string; value: string; tone?: string }) {
  return (
    <div className="min-w-0">
      <div className={`text-sm num ${tone}`}>{value}</div>
      <div className="text-[11px] text-slate-400">{label}</div>
    </div>
  );
}
