import { useState, type FormEvent } from "react";
import { useSearchParams } from "react-router-dom";
import { useHeldRisk, useRiskRecent, useTokenRisk } from "../api/risk";
import type { RiskChangeDto, RiskHeldRowDto } from "../api/types";
import { RiskBadge } from "../components/risk/RiskBadge";
import { RiskReport } from "../components/risk/RiskReport";
import { Empty, ErrorText, Panel, Skeleton, TableSkeleton } from "../components/ui";
import { formatAgo, formatDate, shortAddress } from "../lib/format";
import { useOwner } from "../lib/owner";
import { parseAddress } from "../lib/risk";
import { useNow } from "../lib/useNow";

/**
 * "Should I put money in this token?": paste any Base token address for its report, and see the tokens of the pools you
 * hold or follow with their verdicts, plus what changed lately. Held and followed tokens are re-checked every night.
 */
export function RiskPage() {
  const { owner } = useOwner();
  const [params, setParams] = useSearchParams();
  const address = parseAddress(params.get("token") ?? "");
  const [text, setText] = useState(params.get("token") ?? "");
  const [inputError, setInputError] = useState<string | null>(null);

  const lookup = useTokenRisk("Base", address);
  const held = useHeldRisk(owner);
  const recent = useRiskRecent(owner);

  const open = (token: string) => {
    setText(token);
    setInputError(null);
    setParams({ token }, { replace: true });
  };

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const parsed = parseAddress(text);
    if (!parsed) {
      setInputError("Enter a token contract address: 0x followed by 40 hex characters.");
      return;
    }
    open(parsed);
  };

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Risk</h1>
        <p className="text-sm text-slate-400">
          Should I put money in this token or pool? Flags and facts from GoPlus, honeypot.is, DexScreener and the chain itself, with the
          evidence behind each. No flags is not a guarantee of safety. Tokens you hold or follow are re-checked every night.
        </p>
      </div>

      <Panel title="Look up a token on Base">
        <form onSubmit={submit} className="flex flex-wrap items-start gap-2">
          <div className="min-w-[18rem] flex-1">
            <label htmlFor="risk-token" className="sr-only">
              Token contract address
            </label>
            <input
              id="risk-token"
              value={text}
              onChange={(e) => {
                setText(e.target.value);
                setInputError(null);
              }}
              placeholder="Paste a token address, 0x…"
              spellCheck={false}
              autoComplete="off"
              aria-invalid={inputError != null}
              aria-describedby={inputError ? "risk-token-error" : undefined}
              className="w-full rounded-md border border-slate-700 bg-slate-950 px-3 py-2 font-mono text-sm text-slate-50"
            />
            {inputError && (
              <p id="risk-token-error" className="mt-1 text-xs text-rose-400">
                {inputError}
              </p>
            )}
          </div>
          <button type="submit" className="rounded-md bg-sky-500 px-4 py-2 text-sm font-medium text-slate-950 hover:bg-sky-400">
            Check
          </button>
        </form>

        {address && (
          <div className="mt-4 border-t border-slate-800 pt-4">
            {lookup.isLoading || lookup.isFetching && !lookup.data ? (
              <div className="space-y-2" aria-busy="true">
                <Skeleton className="h-5 w-1/3" />
                <Skeleton className="h-12 w-full" />
                <Skeleton className="h-12 w-full" />
                <p className="text-xs text-slate-500">Asking the sources. A first check takes a few seconds.</p>
              </div>
            ) : lookup.error ? (
              <ErrorText error={lookup.error} />
            ) : lookup.data ? (
              <RiskReport report={lookup.data} />
            ) : null}
          </div>
        )}
      </Panel>

      <Panel title="Tokens you hold or follow">
        {held.error ? (
          <ErrorText error={held.error} />
        ) : held.isLoading ? (
          <TableSkeleton rows={4} />
        ) : !held.data || held.data.length === 0 ? (
          <Empty>
            Nothing here yet. Tokens of your open positions, and of pools you follow with the star on the Pools page, are listed with
            their verdicts.
          </Empty>
        ) : (
          <HeldTable rows={held.data} onOpen={open} openAddress={address} />
        )}
      </Panel>

      <Panel title="Recent changes">
        {recent.error ? (
          <ErrorText error={recent.error} />
        ) : recent.isLoading ? (
          <Skeleton className="h-16 w-full" />
        ) : !recent.data || recent.data.length === 0 ? (
          <Empty>No changes yet. A finding that appears or clears, or a verdict that moves, shows up here after the next check.</Empty>
        ) : (
          <ChangeList changes={recent.data} />
        )}
      </Panel>
    </div>
  );
}

function HeldTable({ rows, onOpen, openAddress }: { rows: RiskHeldRowDto[]; onOpen: (address: string) => void; openAddress: string | null }) {
  const now = useNow(30_000);
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[520px] text-sm">
        <thead className="text-left text-xs uppercase text-slate-400">
          <tr>
            <th className="px-3 py-2">Token</th>
            <th className="px-3 py-2">Why</th>
            <th className="px-3 py-2">Verdict</th>
            <th className="px-3 py-2">Checked</th>
            <th className="px-3 py-2" />
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-800">
          {rows.map((r) => (
            <tr key={r.address} className={`hover:bg-slate-800/50 ${openAddress === r.address ? "bg-sky-400/10" : ""}`}>
              <td className="px-3 py-2">
                <div className="font-medium">{r.symbol}</div>
                <div className="font-mono text-xs text-slate-500">{shortAddress(r.address)}</div>
              </td>
              <td className="px-3 py-2 text-xs text-slate-400">{r.reasons.map((x) => (x === "held" ? "You hold it" : "You follow its pool")).join(" · ")}</td>
              <td className="px-3 py-2">
                <RiskBadge summary={r.risk} />
              </td>
              <td className="px-3 py-2 text-xs text-slate-500" title={r.risk?.checkedAt ? formatDate(r.risk.checkedAt) : undefined}>
                {r.risk?.checkedAt ? formatAgo(r.risk.checkedAt, now) : "never"}
              </td>
              <td className="px-3 py-2 text-right">
                <button type="button" onClick={() => onOpen(r.address)} className="text-xs text-sky-400 hover:underline">
                  {r.risk ? "Open report" : "Check now"}
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ChangeList({ changes }: { changes: RiskChangeDto[] }) {
  return (
    <ul className="divide-y divide-slate-800">
      {changes.map((c, i) => (
        <li key={`${c.subject}-${c.at}-${i}`} className="flex gap-3 py-2 text-sm">
          <span aria-hidden="true" className={`mt-1.5 h-2 w-2 shrink-0 rounded-full ${c.isWorse ? "bg-rose-500" : "bg-emerald-400"}`} />
          <div className="min-w-0">
            <div className="break-words">
              <span className="font-medium">{c.symbol ?? shortAddress(c.subject)}</span>
              <span className="text-slate-500"> · {c.kind === "Pool" ? "pool" : "token"}</span>
              <span className="sr-only">{c.isWorse ? ", got worse" : ", improved"}</span>
            </div>
            <div className="break-words text-slate-400">{c.description}</div>
            <div className="text-xs text-slate-500">{formatDate(c.at)}</div>
          </div>
        </li>
      ))}
    </ul>
  );
}
