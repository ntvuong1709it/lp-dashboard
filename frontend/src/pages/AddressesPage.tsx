import { useState, type FormEvent } from "react";
import {
  useAddAddress,
  useAddresses,
  useRemoveAddress,
  useSyncStatus,
  useUpdateAddressLabel,
} from "../api/client";
import { ResyncControls } from "../components/ResyncControls";
import {
  CopyButton,
  Empty,
  ErrorText,
  ExternalLink,
  Panel,
  SyncErrorList,
  TableSkeleton,
} from "../components/ui";
import { SyncProgressDetails } from "../components/SyncProgress";
import { formatDate, walletUrl } from "../lib/format";
import { useOwner } from "../lib/owner";

function LabelCell({ id, label }: { id: number; label: string | null }) {
  const update = useUpdateAddressLabel();
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(label ?? "");

  const save = () => {
    update.mutate(
      { id, label: value },
      { onSuccess: () => setEditing(false) },
    );
  };

  if (!editing) {
    return (
      <span>
        {label}{" "}
        <button
          className="text-xs text-sky-400 hover:underline"
          onClick={() => {
            setValue(label ?? "");
            setEditing(true);
          }}
        >
          {label ? "Edit" : "Add label"}
        </button>
      </span>
    );
  }

  return (
    <span className="inline-flex items-center gap-2">
      <input
        autoFocus
        className="w-40 rounded-md border border-slate-700 bg-slate-950 px-2 py-1 text-sm"
        maxLength={100}
        value={value}
        onChange={(e) => setValue(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter") save();
          if (e.key === "Escape") setEditing(false);
        }}
      />
      <button
        className="text-xs text-sky-400 hover:underline disabled:opacity-50"
        disabled={update.isPending}
        onClick={save}
      >
        Save
      </button>
      <button
        className="text-xs text-slate-400 hover:underline"
        onClick={() => setEditing(false)}
      >
        Cancel
      </button>
      {update.error && <ErrorText error={update.error} />}
    </span>
  );
}

export function AddressesPage() {
  const { data: addresses, error, isLoading } = useAddresses();
  const { data: status } = useSyncStatus();
  const add = useAddAddress();
  const remove = useRemoveAddress();
  const { owner, setOwner } = useOwner();
  const [address, setAddress] = useState("");
  const [label, setLabel] = useState("");

  const submit = (e: FormEvent) => {
    e.preventDefault();
    add.mutate(
      { address: address.trim(), label: label.trim() || undefined },
      {
        onSuccess: () => {
          setAddress("");
          setLabel("");
        },
      },
    );
  };

  return (
    <div className="space-y-6">
      <Panel title="Watch a wallet (read-only)">
        <form onSubmit={submit} className="flex flex-wrap gap-2">
          <input
            className="min-w-96 flex-1 rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-sm font-mono"
            placeholder="0x… (EVM) or Solana address"
            value={address}
            onChange={(e) => setAddress(e.target.value)}
            required
          />
          <input
            className="w-48 rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-sm"
            placeholder="Label (optional)"
            maxLength={100}
            value={label}
            onChange={(e) => setLabel(e.target.value)}
          />
          <button
            className="rounded-md bg-sky-600 px-4 py-2 text-sm font-medium hover:bg-sky-500 disabled:opacity-50"
            disabled={add.isPending}
          >
            Add
          </button>
        </form>
        {add.error && (
          <div className="mt-2">
            <ErrorText error={add.error} />
          </div>
        )}
        <p className="mt-2 text-xs text-slate-500">
          Solana addresses are stored but not synced yet (EVM/Base only for
          now). The first sync of a wallet can take a while.
        </p>
      </Panel>

      <Panel title="Watched wallets">
        {error ? (
          <ErrorText error={error} />
        ) : isLoading ? (
          <TableSkeleton rows={2} />
        ) : !addresses?.length ? (
          <Empty>No wallets yet.</Empty>
        ) : (
          <table className="w-full text-sm">
            <tbody className="divide-y divide-slate-800">
              {addresses.map((a) => (
                <tr key={a.id}>
                  <td className="py-2 font-mono text-xs">
                    {a.address}{" "}
                    <CopyButton text={a.address} title="Copy address" />{" "}
                    <ExternalLink href={walletUrl(a.chainFamily, a.address)}>
                      explorer
                    </ExternalLink>
                  </td>
                  <td className="py-2">
                    <LabelCell id={a.id} label={a.label} />
                  </td>
                  <td className="py-2 text-xs text-slate-500">
                    {a.chainFamily}
                  </td>
                  <td className="py-2 text-xs text-slate-500">
                    added {formatDate(a.createdAt)}
                  </td>
                  <td className="py-2 text-right">
                    {a.chainFamily === "Evm" && (
                      <span className="mr-4">
                        <ResyncControls
                          what="wallet"
                          scope={{ owner: a.address }}
                        />
                      </span>
                    )}
                    <button
                      className="text-xs text-rose-400 hover:underline disabled:opacity-50"
                      disabled={remove.isPending}
                      onClick={() => {
                        if (!confirm(`Stop watching ${a.address}?`)) return;
                        remove.mutate(a.id, {
                          onSuccess: () =>
                            owner === a.address && setOwner(null),
                        });
                      }}
                    >
                      Remove
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Panel>

      <Panel title="Sync status">
        <dl className="grid grid-cols-2 gap-2 text-sm md:grid-cols-4">
          <div>
            <dt className="text-xs text-slate-500">State</dt>
            <dd>
              {status?.isRunning
                ? `Running · ${Math.round(status.percent)}%`
                : status?.isQueued
                  ? "Queued"
                  : "Idle"}
            </dd>
          </div>
          <div>
            <dt className="text-xs text-slate-500">Last started</dt>
            <dd>{formatDate(status?.lastStartedAt)}</dd>
          </div>
          <div>
            <dt className="text-xs text-slate-500">Last finished</dt>
            <dd>{formatDate(status?.lastFinishedAt)}</dd>
          </div>
          <div>
            <dt className="text-xs text-slate-500">Providers</dt>
            <dd>{status?.configuredProviders.join(", ") || "none"}</dd>
          </div>
        </dl>
        {status && (status.isRunning || status.tasks.length > 0) && (
          <div className="mt-4">
            <SyncProgressDetails status={status} />
          </div>
        )}
        {!!status?.lastErrors.length && (
          <div className="mt-4">
            <div className="mb-2 text-sm font-medium text-amber-300">
              {status.isRunning
                ? "Current sync has"
                : "Last sync finished with"}{" "}
              {status.lastErrors.length}{" "}
              {status.lastErrors.length === 1 ? "error" : "errors"}
              {status.isRunning ? " so far" : ""}
            </div>
            <SyncErrorList errors={status.lastErrors} />
          </div>
        )}
      </Panel>

      <Panel title="Recent errors">
        {status?.recentErrors.length ? (
          <ul className="max-h-72 space-y-2 overflow-y-auto">
            {[...status.recentErrors].reverse().map((e, i) => (
              <li
                key={i}
                className="rounded-md border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-xs text-amber-200"
              >
                <div className="mb-1 text-[11px] text-amber-300/70">
                  {formatDate(e.at)}
                </div>
                <div className="whitespace-pre-wrap break-words font-mono">
                  {e.message}
                </div>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-slate-500">
            No sync errors since the app started. (Kept in memory, so this
            clears on restart.)
          </p>
        )}
      </Panel>

      <Panel title="Backup">
        <p className="mb-3 text-sm text-slate-400">
          Download a copy of the database. Price history and position snapshots
          cannot be rebuilt from chain data, so keep a backup before upgrades.
        </p>
        <a
          href="/api/backup"
          download
          className="inline-block rounded-md border border-slate-700 px-3 py-1.5 text-sm hover:bg-slate-800"
        >
          Download database backup
        </a>
      </Panel>
    </div>
  );
}
