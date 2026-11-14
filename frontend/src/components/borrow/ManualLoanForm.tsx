import { useState } from "react";
import { useBorrowAssets, useSaveManualLoan } from "../../api/client";
import type { ManualLoanDto } from "../../api/types";
import { todayLocal } from "../../lib/borrow";
import { formatUsd, shortAddress } from "../../lib/format";
import { useOwner } from "../../lib/owner";
import { Panel } from "../ui";

interface TermRow {
  from: string;
  principal: string;
  apr: string;
}
interface CollateralRow {
  from: string;
  amount: string;
}

const input =
  "w-full rounded-md border border-control bg-raised px-2 py-1.5 text-sm text-fg focus:border-accent-fill";
const label = "mb-1 block text-xs text-muted";

/** Adds or edits a manual loan: its terms over time, its collateral over time and its liquidation limits. */
export function ManualLoanForm({ loan, onClose }: { loan: ManualLoanDto | null; onClose: () => void }) {
  const { addresses, selected } = useOwner();
  const assets = useBorrowAssets().data ?? [];
  const save = useSaveManualLoan();
  const today = todayLocal();

  const [name, setName] = useState(loan?.label ?? "Flexible loan");
  const [owner, setOwner] = useState(loan?.owner ?? selected ?? addresses[0]?.address ?? "");
  const [platform, setPlatform] = useState(loan?.platform ?? "Binance");
  const [asset, setAsset] = useState(loan?.assetSymbol ?? "USDT");
  const [assetKey, setAssetKey] = useState(loan?.assetPriceKey ?? "");
  const [closedOn, setClosedOn] = useState(loan?.closedOn ?? "");
  const [terms, setTerms] = useState<TermRow[]>(
    loan?.terms.map((t) => ({ from: t.effectiveFrom, principal: String(t.principal), apr: String(t.aprPercent) })) ?? [
      { from: today, principal: "", apr: "" },
    ],
  );
  const [collateralAsset, setCollateralAsset] = useState(loan?.collateralSymbol ?? "BTC");
  const [collateralKey, setCollateralKey] = useState(loan?.collateralPriceKey ?? "");
  const [margin, setMargin] = useState(loan?.marginCallLtvPercent == null ? "" : String(loan.marginCallLtvPercent));
  const [liquidation, setLiquidation] = useState(loan?.liquidationLtvPercent == null ? "" : String(loan.liquidationLtvPercent));
  const [collateral, setCollateral] = useState<CollateralRow[]>(
    loan?.collateral.map((c) => ({ from: c.effectiveFrom, amount: String(c.amount) })) ?? [{ from: today, amount: "" }],
  );

  const known = (s: string) => assets.some((a) => a.symbol === s);
  const last = terms[terms.length - 1];
  const perDay = last && Number(last.principal) > 0 ? (Number(last.principal) * Number(last.apr || 0)) / 100 / 365 : null;

  const setTerm = (i: number, patch: Partial<TermRow>) => setTerms(terms.map((t, j) => (j === i ? { ...t, ...patch } : t)));
  const setColl = (i: number, patch: Partial<CollateralRow>) => setCollateral(collateral.map((c, j) => (j === i ? { ...c, ...patch } : c)));

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    const num = (v: string) => (v.trim() === "" ? null : Number(v));
    const dto: ManualLoanDto = {
      id: loan?.id ?? 0,
      owner,
      label: name,
      platform,
      assetSymbol: asset,
      assetPriceKey: known(asset) ? null : assetKey || null,
      collateralSymbol: collateral.some((c) => c.amount.trim() !== "") ? collateralAsset : null,
      collateralPriceKey: known(collateralAsset) ? null : collateralKey || null,
      marginCallLtvPercent: num(margin),
      liquidationLtvPercent: num(liquidation),
      closedOn: closedOn || null,
      terms: terms.map((t) => ({ effectiveFrom: t.from, principal: Number(t.principal), aprPercent: Number(t.apr) })),
      collateral: collateral
        .filter((c) => c.amount.trim() !== "")
        .map((c) => ({ effectiveFrom: c.from, amount: Number(c.amount) })),
    };
    save.mutate(dto, { onSuccess: onClose });
  };

  return (
    <Panel title={loan ? "Edit manual loan" : "Add manual loan"} actions={<span className="text-xs text-faint">For debt the app cannot read from chain</span>}>
      <form className="space-y-4" onSubmit={submit}>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <div>
            <label className={label} htmlFor="ml-label">Label</label>
            <input id="ml-label" className={input} value={name} onChange={(e) => setName(e.target.value)} required />
          </div>
          <div>
            <label className={label} htmlFor="ml-wallet">Wallet</label>
            <select id="ml-wallet" className={input} value={owner} onChange={(e) => setOwner(e.target.value)} disabled={loan != null}>
              {addresses.map((a) => (
                <option key={a.address} value={a.address}>
                  {a.label ? `${a.label} (${shortAddress(a.address)})` : shortAddress(a.address)}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className={label} htmlFor="ml-platform">Platform</label>
            <input id="ml-platform" className={input} value={platform} onChange={(e) => setPlatform(e.target.value)} />
          </div>
          <div>
            <label className={label} htmlFor="ml-closed">Closed on (optional)</label>
            <input id="ml-closed" type="date" className={input} value={closedOn} onChange={(e) => setClosedOn(e.target.value)} />
          </div>
          <div>
            <label className={label} htmlFor="ml-asset">Borrowed asset</label>
            <select id="ml-asset" className={input} value={known(asset) ? asset : "__other"} onChange={(e) => setAsset(e.target.value === "__other" ? "" : e.target.value)}>
              {assets.map((a) => (
                <option key={a.symbol}>{a.symbol}</option>
              ))}
              <option value="__other">Other…</option>
            </select>
          </div>
          {!known(asset) && (
            <>
              <div>
                <label className={label} htmlFor="ml-asset-symbol">Asset symbol</label>
                <input id="ml-asset-symbol" className={input} value={asset} onChange={(e) => setAsset(e.target.value)} required />
              </div>
              <div>
                <label className={label} htmlFor="ml-asset-key">Price key</label>
                <input id="ml-asset-key" className={input} placeholder="coingecko:<id>" value={assetKey} onChange={(e) => setAssetKey(e.target.value)} required />
              </div>
            </>
          )}
        </div>

        <div>
          <div className="mb-1.5 text-xs uppercase tracking-[.05em] text-muted">Terms history</div>
          <div className="space-y-2">
            {terms.map((t, i) => (
              <div key={i} className="grid grid-cols-[1.2fr_1fr_.8fr_auto] items-end gap-2">
                <div>
                  <label className={label} htmlFor={`term-from-${i}`}>Effective from</label>
                  <input id={`term-from-${i}`} type="date" className={input} value={t.from} onChange={(e) => setTerm(i, { from: e.target.value })} required />
                </div>
                <div>
                  <label className={label} htmlFor={`term-principal-${i}`}>Total debt ({asset || "units"})</label>
                  <input id={`term-principal-${i}`} type="number" step="any" min="0" className={input} value={t.principal} onChange={(e) => setTerm(i, { principal: e.target.value })} required />
                </div>
                <div>
                  <label className={label} htmlFor={`term-apr-${i}`}>APR %</label>
                  <input id={`term-apr-${i}`} type="number" step="any" min="0" className={input} value={t.apr} onChange={(e) => setTerm(i, { apr: e.target.value })} required />
                </div>
                <button type="button" className="rounded px-2 py-1.5 text-xs text-fg-2 hover:bg-raised disabled:opacity-40" disabled={terms.length === 1} onClick={() => setTerms(terms.filter((_, j) => j !== i))}>
                  Remove
                </button>
              </div>
            ))}
          </div>
          <button
            type="button"
            className="mt-2 rounded-md border border-line-strong px-2.5 py-1 text-xs hover:bg-raised"
            onClick={() => setTerms([...terms, { from: today, principal: last?.principal ?? "", apr: last?.apr ?? "" }])}
          >
            Add rate change or top-up
          </button>
          <p className="mt-2 text-xs text-muted">
            {perDay != null ? (
              <>
                At the latest terms this accrues <b className="num">{formatUsd(perDay)}</b> a day (simple interest, {formatUsd(perDay * 30)} per 30 days).{" "}
              </>
            ) : null}
            Enter the total debt your lender shows each time you add a row, so the debt line matches theirs.
          </p>
        </div>

        <div>
          <div className="mb-1.5 text-xs uppercase tracking-[.05em] text-muted">Collateral and risk limits (optional)</div>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <div>
              <label className={label} htmlFor="ml-collateral">Collateral asset</label>
              <select id="ml-collateral" className={input} value={known(collateralAsset) ? collateralAsset : "__other"} onChange={(e) => setCollateralAsset(e.target.value === "__other" ? "" : e.target.value)}>
                {assets.map((a) => (
                  <option key={a.symbol}>{a.symbol}</option>
                ))}
                <option value="__other">Other…</option>
              </select>
            </div>
            {!known(collateralAsset) && (
              <div>
                <label className={label} htmlFor="ml-collateral-key">Price key</label>
                <input id="ml-collateral-key" className={input} placeholder="coingecko:<id>" value={collateralKey} onChange={(e) => setCollateralKey(e.target.value)} />
              </div>
            )}
            <div>
              <label className={label} htmlFor="ml-margin">Margin call LTV %</label>
              <input id="ml-margin" type="number" step="any" className={input} value={margin} onChange={(e) => setMargin(e.target.value)} />
            </div>
            <div>
              <label className={label} htmlFor="ml-liq">Liquidation LTV %</label>
              <input id="ml-liq" type="number" step="any" className={input} value={liquidation} onChange={(e) => setLiquidation(e.target.value)} />
            </div>
          </div>
          <div className="mt-3 space-y-2">
            {collateral.map((c, i) => (
              <div key={i} className="grid grid-cols-[1.2fr_1fr_auto] items-end gap-2 sm:max-w-xl">
                <div>
                  <label className={label} htmlFor={`coll-from-${i}`}>Effective from</label>
                  <input id={`coll-from-${i}`} type="date" className={input} value={c.from} onChange={(e) => setColl(i, { from: e.target.value })} required />
                </div>
                <div>
                  <label className={label} htmlFor={`coll-amount-${i}`}>Amount ({collateralAsset || "units"})</label>
                  <input id={`coll-amount-${i}`} type="number" step="any" min="0" className={input} value={c.amount} onChange={(e) => setColl(i, { amount: e.target.value })} />
                </div>
                <button type="button" className="rounded px-2 py-1.5 text-xs text-fg-2 hover:bg-raised disabled:opacity-40" disabled={collateral.length === 1} onClick={() => setCollateral(collateral.filter((_, j) => j !== i))}>
                  Remove
                </button>
              </div>
            ))}
          </div>
          <button type="button" className="mt-2 rounded-md border border-line-strong px-2.5 py-1 text-xs hover:bg-raised" onClick={() => setCollateral([...collateral, { from: today, amount: collateral[collateral.length - 1]?.amount ?? "" }])}>
            Add top-up or withdrawal
          </button>
          <p className="mt-2 text-xs text-muted">Add a row whenever your collateral changes. It lets the app chart collateral ÷ debt for days before today.</p>
        </div>

        {save.error && <div className="text-sm text-down" role="alert">{save.error.message}</div>}
        <div className="flex justify-end gap-2">
          <button type="button" className="rounded-md px-3 py-1.5 text-sm text-fg-2 hover:bg-raised" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="rounded-md bg-accent-solid px-3 py-1.5 text-sm font-medium text-white hover:bg-accent-fill disabled:opacity-50" disabled={save.isPending || !owner}>
            {save.isPending ? "Saving…" : "Save loan"}
          </button>
        </div>
      </form>
    </Panel>
  );
}
