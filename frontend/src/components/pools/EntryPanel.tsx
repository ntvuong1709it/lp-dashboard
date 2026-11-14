import { useEffect, useMemo, useRef, useState } from "react";
import { useBorrowLoans, useEntryPlan, useWalletBalances } from "../../api/client";
import type { EntryPlanDto, EntryPlanRequest, PoolDto, PoolTokenDto } from "../../api/types";
import { formatAmount } from "../../lib/format";
import { amt, poolTokens, usd0, usd2, walletHoldings } from "../../lib/entry";
import { type Emissions } from "../../lib/pools";
import { useHideBalances } from "../../lib/privacy";
import { useOwner } from "../../lib/owner";
import { useDebounced } from "../../lib/useDebounced";
import { usePersistentState } from "../../lib/usePersistentState";
import { useWallets } from "../../lib/wallets";
import { CopyButton, ErrorText, Segmented } from "../ui";
import { NumberField, Tile, input } from "./entryBits";
import { HedgeBlock, type HedgeInputs } from "./HedgeBlock";
import { RedRiskBanner } from "./PoolRiskSection";

export type EntryMode = "budget" | "wallet";

const AUTO = "";

/** Parses a text field; null when it is empty or not a non-negative number. */
const num = (text: string) => {
  const v = Number(text.replace(",", "."));
  return text.trim() !== "" && Number.isFinite(v) && v >= 0 ? v : null;
};

/**
 * "Enter this range": what to hold to open the range chosen above and what that costs, plus a with-and-without comparison
 * of hedging it. The maths runs on the server (`POST /api/pools/{id}/entry-plan`); this is the form and the readout.
 * Nothing is sent or signed: the exact amounts are for typing into the DEX's own deposit screen.
 */
export function EntryPanel({
  pool,
  lowerPercent,
  upperPercent,
  capital,
  mode,
  onModeChange,
  onHeldUsd,
  feeApr,
  emissions,
  publishedEmissionsApr,
  basePlan,
}: {
  pool: PoolDto;
  lowerPercent: number;
  upperPercent: number;
  /** Deposit size from the "Your position" block's Capital field; used in "Capital" mode. */
  capital: number;
  mode: EntryMode;
  onModeChange: (mode: EntryMode) => void;
  /** Reports the USD value of the tokens entered in "Tokens I hold" mode, which the panel's Capital field shows read-only. */
  onHeldUsd: (usd: number) => void;
  /** Swap-fee APR from the Estimate tab, percent. */
  feeApr: number | null;
  emissions: Emissions | null;
  publishedEmissionsApr: number | null;
  /** The panel's own plan for this range (price and tokens), which knows the pool's token order and decimals before this form has an answer. */
  basePlan: EntryPlanDto | undefined;
}) {
  const hidden = useHideBalances();
  const [swapCost, setSwapCost] = usePersistentState<number>("lpdash.entry.swapCostPct", 0.1);
  const [gas, setGas] = usePersistentState<number>("lpdash.entry.gasUsd", 0.8);
  const [paysWith, setPaysWith] = useState<string>(AUTO);
  const [amounts, setAmounts] = useState<Record<string, string>>({});
  const [hedge, setHedge] = usePersistentState<HedgeInputs>("lpdash.entry.hedge.v1", {
    instrument: "perp",
    ratioPct: 100,
    perpRatePct: 8,
    borrowRatePct: 5,
    leverage: 3,
    openFeePct: 0.05,
  });
  const emissionsApr = publishedEmissionsApr || emissions?.apr || null;
  const [basis, setBasis] = useState<"fees" | "emissions">("fees");
  const [incomeText, setIncomeText] = useState<string>(feeApr == null ? "" : feeApr.toFixed(1));
  const incomeTouched = useRef(false);

  // Follow the estimate's APR until the user types their own, and reset it when the basis changes.
  const defaultIncome = basis === "fees" ? feeApr : emissionsApr;
  useEffect(() => {
    if (!incomeTouched.current && defaultIncome != null) setIncomeText(defaultIncome.toFixed(1));
  }, [defaultIncome]);
  const changeBasis = (b: "fees" | "emissions") => {
    setBasis(b);
    incomeTouched.current = false;
    const next = b === "fees" ? feeApr : emissionsApr;
    if (next != null) setIncomeText(next.toFixed(1));
  };

  // Amounts are keyed by token address, so they stay with their token if the order differs between the list and the chain.
  const listTokens = useMemo(() => poolTokens(pool, undefined), [pool]);
  const holdings = useMemo(
    () => listTokens.map((t) => ({ token: t.address, amount: num(amounts[t.address] ?? "") ?? 0 })),
    [listTokens, amounts],
  );
  const holdingsOk = holdings.some((h) => h.amount > 0);
  const budget = num(String(capital));

  const body = useMemo<EntryPlanRequest | null>(() => {
    const income = num(incomeText);
    const ok = mode === "budget" ? budget != null && budget > 0 : holdingsOk;
    if (!ok || income == null) return null;
    return {
      lowerPercent,
      upperPercent,
      ...(mode === "budget" ? { budgetUsd: budget!, ...(paysWith !== AUTO ? { paysWith } : {}) } : { holdings: holdings.filter((h) => h.amount > 0) }),
      swapCostPct: swapCost,
      gasUsd: gas,
      incomeAprPct: income,
      incomeBasis: basis,
      hedge: {
        instrument: hedge.instrument,
        ratio: hedge.ratioPct / 100,
        ratePct: hedge.instrument === "perp" ? hedge.perpRatePct : hedge.borrowRatePct,
        leverage: hedge.leverage,
        openFeePct: hedge.openFeePct,
      },
    };
  }, [mode, budget, holdingsOk, holdings, paysWith, lowerPercent, upperPercent, swapCost, gas, incomeText, basis, hedge]);

  // Debounce the serialised request so typing does not ask the server on every key.
  const key = useDebounced(body == null ? "" : JSON.stringify(body));
  const debounced = useMemo<EntryPlanRequest | null>(() => (key ? (JSON.parse(key) as EntryPlanRequest) : null), [key]);
  const { data, error, isFetching } = useEntryPlan(pool.id, debounced);
  const plan = data && data.pool.poolId === pool.id ? data : undefined;
  const known = plan ?? (basePlan?.pool.poolId === pool.id ? basePlan : undefined);
  const [t0, t1] = poolTokens(pool, known);

  // The Capital field shows what the entered tokens are worth, using the plan's USD prices.
  const heldUsd = known ? (num(amounts[t0.address] ?? "") ?? 0) * known.pool.usd0 + (num(amounts[t1.address] ?? "") ?? 0) * known.pool.usd1 : 0;
  useEffect(() => {
    if (mode === "wallet") onHeldUsd(heldUsd);
  }, [mode, heldUsd, onHeldUsd]);

  return (
    <section className="rounded-lg border border-dashed border-accent/70 p-3" aria-label="Enter this range">
      <div className="mb-2 flex items-center justify-between gap-2">
        <h3 className="text-sm font-semibold">Enter this range</h3>
        {isFetching && <span className="text-xs text-faint">Updating…</span>}
      </div>

      <div className="mb-3 empty:hidden">
        <RedRiskBanner poolId={pool.id} />
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <Segmented<EntryMode>
          label="Starting point"
          value={mode}
          onChange={onModeChange}
          options={[
            { label: "Capital", value: "budget" },
            { label: "Tokens I hold", value: "wallet" },
          ]}
        />
        {mode === "budget" && (
          <label className="flex items-center gap-2 text-xs text-muted">
            Paying with
            <select value={paysWith} onChange={(e) => setPaysWith(e.target.value)} className={`${input} py-1`}>
              <option value={AUTO}>Stable token (if any)</option>
              <option value={t1.address}>All {t1.symbol}</option>
              <option value={t0.address}>All {t0.symbol}</option>
              <option value="split">50 / 50</option>
            </select>
          </label>
        )}
      </div>

      {mode === "wallet" && (
        <WalletInputs tokens={known ? [t0, t1] : null} amounts={amounts} onAmounts={setAmounts} hidden={hidden} />
      )}

      <div className="mt-2 flex flex-wrap items-end gap-4">
        <NumberField label="Swap fee + slippage (%)" value={swapCost} onChange={setSwapCost} step={0.01} width="w-24" />
        <NumberField label="Gas: swap + mint (USD)" value={gas} onChange={setGas} step={0.1} width="w-24" />
      </div>

      {error ? (
        <div className="mt-3">
          <ErrorText error={error} />
        </div>
      ) : !plan ? (
        <p className="mt-3 text-sm text-faint">
          {body == null ? (mode === "wallet" ? "Enter the amounts you hold." : "Enter a capital.") : "Working out the swap…"}
        </p>
      ) : (
        <PlanReadout plan={plan} t0={t0} t1={t1} />
      )}

      {plan && (
        <HedgeBlock
          plan={plan}
          t0={t0}
          t1={t1}
          inputs={hedge}
          onInputs={setHedge}
          basis={basis}
          onBasis={changeBasis}
          incomeText={incomeText}
          onIncome={(v) => {
            incomeTouched.current = true;
            setIncomeText(v);
          }}
          borrowSuggestion={<BorrowRateSuggestion t0={t0} t1={t1} legs={plan.hedge?.legs ?? "None"} onUse={(rate) => setHedge({ ...hedge, borrowRatePct: rate })} />}
        />
      )}

      <p className="mt-3 text-xs text-faint">Estimates, not quotes. Price impact is an input, not read from the pool's depth. Read-only: nothing is sent or signed.</p>
    </section>
  );
}



/** Amounts in the wallet, prefilled from a followed wallet's balances (read-only RPC) and always editable. */
function WalletInputs({
  tokens,
  amounts,
  onAmounts,
  hidden,
}: {
  /** Null until the pool's tokens (and so their decimals) are known. */
  tokens: [PoolTokenDto, PoolTokenDto] | null;
  amounts: Record<string, string>;
  onAmounts: (a: Record<string, string>) => void;
  hidden: boolean;
}) {
  const { addresses, selected } = useOwner();
  const wallets = useWallets();
  // With "All wallets" picked in the header, the user says which wallet to read; with one, that wallet is the default.
  const [picked, setPicked] = useState<string | null>(null);
  const wallet = picked ?? selected ?? (addresses.length === 1 ? addresses[0].address : null);
  const balances = useWalletBalances(hidden || tokens == null ? null : wallet, tokens?.map((t) => t.address) ?? []);
  const [note, setNote] = useState<string | null>(null);
  const filledFor = useRef<string | null>(null);

  useEffect(() => {
    if (!balances.data || !wallet || !tokens || filledFor.current === `${wallet}:${balances.dataUpdatedAt}`) return;
    filledFor.current = `${wallet}:${balances.dataUpdatedAt}`;
    const held = walletHoldings(balances.data, tokens);
    onAmounts(Object.fromEntries(held.map((h) => [h.token.address, String(Number(h.amount.toPrecision(8)))])));
    const native = held.find((h) => h.nativeAdded > 0);
    setNote(native ? `Includes ${formatAmount(native.nativeAdded)} native ETH counted as WETH, keeping 0.002 ETH for gas.` : null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [balances.data, wallet]);

  return (
    <div className="mt-2 space-y-2">
      <div className="grid grid-cols-2 gap-2">
        {(tokens ?? []).map((t) => (
          <label key={t.address} className="flex flex-col gap-1 text-xs text-muted">
            {t.symbol} in wallet
            <input
              type="number"
              min={0}
              step="any"
              value={amounts[t.address] ?? ""}
              onChange={(e) => onAmounts({ ...amounts, [t.address]: e.target.value })}
              className={`${input} w-full`}
            />
          </label>
        ))}
      </div>
      <div className="flex flex-wrap items-center gap-2 text-xs text-faint">
        {addresses.length === 0 ? (
          <span>Add a wallet on the Addresses page to prefill from it. Type the amounts meanwhile.</span>
        ) : (
          <label className="flex items-center gap-2">
            Prefill from
            <select
              value={wallet ?? ""}
              onChange={(e) => setPicked(e.target.value || null)}
              className={`${input} py-1 text-xs`}
            >
              {wallet == null && <option value="">Choose a wallet…</option>}
              {addresses.map((a) => (
                <option key={a.address} value={a.address}>
                  {wallets(a.address).name}
                </option>
              ))}
            </select>
          </label>
        )}
        {hidden && <span>Balances are hidden, so nothing is prefilled. Turn off privacy mode or type the amounts.</span>}
        {balances.isFetching && <span>Reading balances…</span>}
        {balances.error && <span className="text-warn">Could not read the wallet: {balances.error.message}</span>}
        {note && !hidden && <span>{note}</span>}
      </div>
    </div>
  );
}

function PlanReadout({ plan, t0, t1 }: { plan: EntryPlanDto; t0: PoolTokenDto; t1: PoolTokenDto }) {
  const e = plan.entry;
  const sold = e.side === "SellToken0" ? t0.symbol : t1.symbol;
  const got = e.side === "SellToken0" ? t1.symbol : t0.symbol;
  const hidden = useHideBalances();

  return (
    <div className="mt-3 space-y-2">
      <p className="text-sm">
        {e.side === "None" ? (
          "No swap needed: your tokens already match this range."
        ) : (
          <>
            Swap{" "}
            <b className="font-medium">
              {amt(e.swapAmountIn)} {sold}
            </b>{" "}
            for about{" "}
            <b className="font-medium">
              {amt(e.swapProceeds)} {got}
            </b>
            , then deposit.
          </>
        )}
      </p>
      {e.swapShareWarning && e.swapShareOfTvl != null && (
        <p className="text-xs text-warn">
          This swap is {(e.swapShareOfTvl * 100).toFixed(2)}% of the pool's TVL. Expect more price impact than the {plan.swapCostPct}% above assumes.
        </p>
      )}
      {!plan.priceInRange && (
        <p className="text-xs text-warn">
          The pool price is outside this range, so a deposit here is a single token and earns no fees until the price gets back in.
        </p>
      )}

      <div className="grid grid-cols-3 gap-2">
        <Tile label={`Need ${t0.symbol}`} value={amt(e.amount0)} sub={usd0(e.amount0Usd)} />
        <Tile label={`Need ${t1.symbol}`} value={amt(e.amount1)} sub={`${Math.round(e.token1Share * 100)}% of position, ${usd0(e.amount1Usd)}`} />
        <Tile label="Swap and gas cost" value={usd2(e.entryCostUsd)} sub={`${usd2(e.swapCostUsd)} swap + ${usd2(e.gasUsd)} gas`} />
      </div>
      <p className="text-xs text-faint">
        Required mix at this price: {Math.round(e.token0Share * 100)}% {t0.symbol} / {Math.round(e.token1Share * 100)}% {t1.symbol}. Position value after the swap: {usd0(e.valueUsd)}
        {!hidden && e.startValueUsd > 0 ? `, from ${usd0(e.startValueUsd)}` : ""}.
      </p>

      <div className="rounded-lg border border-line bg-surface px-3 py-2">
        <div className="mb-1 text-xs text-muted">Deposit this</div>
        <DepositRow token={t0} value={e.deposit0} />
        <DepositRow token={t1} value={e.deposit1} />
        <p className="mt-1 text-xs text-faint">
          Rounded down to token decimals: leftover about {usd2(e.dustUsd)}. Enter both amounts in the deposit screen; it may use slightly less of one token.
        </p>
      </div>
    </div>
  );
}

function DepositRow({ token, value }: { token: PoolTokenDto; value: number }) {
  const decimals = Math.min(token.decimals, 8);
  const text = value.toFixed(decimals);
  return (
    <div className="flex items-center justify-between py-0.5 text-sm">
      <span className="num">
        {text} {token.symbol}
      </span>
      <CopyButton text={text} title={`Copy the ${token.symbol} amount`} />
    </div>
  );
}



/** When the wallet has a loan in the token a borrow-and-sell hedge would borrow, offer its rate instead of a guess. */
function BorrowRateSuggestion({
  t0,
  t1,
  legs,
  onUse,
}: {
  t0: PoolTokenDto;
  t1: PoolTokenDto;
  legs: string;
  onUse: (ratePct: number) => void;
}) {
  const { owner } = useOwner();
  const loans = useBorrowLoans(owner, 30);
  const volatile = legs === "Token0" ? t0 : legs === "Token1" ? t1 : null;
  const loan = volatile ? loans.data?.find((l) => l.debtSymbol.toUpperCase() === volatile.symbol.toUpperCase() && l.debtUsd > 0) : undefined;
  if (!loan) return null;
  return (
    <button type="button" onClick={() => onUse(Number(loan.aprPercent.toFixed(2)))} className="text-xs text-accent hover:underline">
      Use my {loan.source} {loan.debtSymbol} loan rate ({loan.aprPercent.toFixed(2)}%)
    </button>
  );
}
