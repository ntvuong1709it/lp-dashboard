import { useState, type FormEvent } from "react";
import { useAddAddress, useHealth, useSyncStatus } from "../api/client";
import { AccountPanel } from "../components/AccountPanel";
import { ResyncControls } from "../components/ResyncControls";
import {
  CopyButton,
  Empty,
  ExternalLink,
  Panel,
  SyncErrorList,
} from "../components/ui";
import { ErrorText } from "../components/ui";
import { SyncProgressDetails } from "../components/SyncProgress";
import { formatDate, walletUrl } from "../lib/format";
import { useOwner } from "../lib/owner";

function LabelCell({ address, label }: { address: string; label: string }) {
  const { setLabel } = useOwner();
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(label);
  const [error, setError] = useState<unknown>(null);

  const save = async () => {
    try {
      await setLabel(address, value);
      setError(null);
      setEditing(false);
    } catch (e) {
      setError(e);
    }
  };

  if (!editing) {
    return (
      <span>
        {label}{" "}
        <button
          className="text-xs text-accent hover:underline"
          onClick={() => {
            setValue(label);
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
        className="w-40 rounded-md border border-control bg-surface px-2 py-1 text-sm"
        maxLength={100}
        value={value}
        onChange={(e) => setValue(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter") void save();
          if (e.key === "Escape") setEditing(false);
        }}
      />
      <button
        className="text-xs text-accent hover:underline"
        onClick={() => void save()}
      >
        Save
      </button>
      <button
        className="text-xs text-muted hover:underline"
        onClick={() => setEditing(false)}
      >
        Cancel
      </button>
      {error != null && <ErrorText error={error} />}
    </span>
  );
}

export function AddressesPage() {
  const { data: status } = useSyncStatus();
  const { data: health } = useHealth();
  const add = useAddAddress();
  const { addresses, selected, setOwner, removeAddress } = useOwner();
  const [address, setAddress] = useState("");
  const [label, setLabel] = useState("");
  const [removeError, setRemoveError] = useState<unknown>(null);

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
      {addresses.length === 0 && (
        <div className="rounded-lg border border-accent-fill/30 bg-accent-fill/10 p-4 text-sm text-fg-2">
          <div className="mb-1 font-medium text-fg">
            Enter a wallet address to get started
          </div>
          The dashboard is read-only: it reads positions from the chain for the
          address you give it. The wallets you add are saved to your account,
          so every device you sign in on shows the same list.
        </div>
      )}

      <Panel title="Follow a wallet (read-only)">
        <form onSubmit={submit} className="flex flex-wrap gap-2">
          <input
            className="min-w-96 flex-1 rounded-md border border-control bg-surface px-3 py-2 text-sm font-mono"
            placeholder="0x… (EVM) or Solana address"
            value={address}
            onChange={(e) => setAddress(e.target.value)}
            required
          />
          <input
            className="w-48 rounded-md border border-control bg-surface px-3 py-2 text-sm"
            placeholder="Label (optional)"
            maxLength={100}
            value={label}
            onChange={(e) => setLabel(e.target.value)}
          />
          <button
            className="rounded-md bg-accent-solid px-4 py-2 text-sm font-medium hover:bg-accent-fill disabled:opacity-50"
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
        <p className="mt-2 text-xs text-faint">
          Solana addresses are stored but not synced yet (EVM/Base only for
          now). The first sync of a wallet can take a while. Wallets and labels
          are saved to your account and shared by all your devices.
        </p>
      </Panel>

      <Panel title="Your wallets">
        {removeError != null && (
          <div className="mb-2">
            <ErrorText error={removeError} />
          </div>
        )}
        {!addresses.length ? (
          <Empty>No wallets yet.</Empty>
        ) : (
          <table className="w-full text-sm">
            <tbody className="divide-y divide-line">
              {addresses.map((a) => {
                const family = a.address.startsWith("0x") ? "Evm" : "Solana";
                return (
                  <tr key={a.address}>
                    <td className="py-2 font-mono text-xs">
                      {a.address}{" "}
                      <CopyButton text={a.address} title="Copy address" />{" "}
                      <ExternalLink href={walletUrl(family, a.address)}>
                        explorer
                      </ExternalLink>
                    </td>
                    <td className="py-2">
                      <LabelCell address={a.address} label={a.label} />
                    </td>
                    <td className="py-2 text-xs text-faint">{family}</td>
                    <td className="py-2 text-right">
                      <button
                        className="mr-4 text-xs text-accent hover:underline disabled:opacity-50"
                        disabled={selected === a.address}
                        onClick={() => setOwner(a.address)}
                      >
                        {selected === a.address ? "Showing" : "Show only"}
                      </button>
                      {family === "Evm" && (
                        <span className="mr-4">
                          <ResyncControls
                            what="wallet"
                            scope={{ owner: a.address }}
                          />
                        </span>
                      )}
                      <button
                        className="text-xs text-down hover:underline"
                        onClick={() => {
                          if (
                            !confirm(
                              `Remove ${a.address} from your wallets? It stops syncing on every device. Its stored data stays on the server, so adding it again brings everything back.`,
                            )
                          )
                            return;
                          setRemoveError(null);
                          removeAddress(a.address).catch(setRemoveError);
                        }}
                      >
                        Remove
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </Panel>

      <Panel title="Sync status">
        <dl className="grid grid-cols-2 gap-2 text-sm md:grid-cols-4">
          <div>
            <dt className="text-xs text-faint">State</dt>
            <dd>
              {status?.isRunning
                ? `Running · ${Math.round(status.percent)}%`
                : status?.isQueued
                  ? "Queued"
                  : "Idle"}
            </dd>
          </div>
          <div>
            <dt className="text-xs text-faint">Last started</dt>
            <dd>{formatDate(status?.lastStartedAt)}</dd>
          </div>
          <div>
            <dt className="text-xs text-faint">Last finished</dt>
            <dd>{formatDate(status?.lastFinishedAt)}</dd>
          </div>
          <div>
            <dt className="text-xs text-faint">Providers</dt>
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
            <div className="mb-2 text-sm font-medium text-warn">
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

      <AccountPanel />

      {health?.backupEnabled && (
        <Panel title="Backup">
          <p className="mb-3 text-sm text-muted">
            Download a copy of the database. Price history and position
            snapshots cannot be rebuilt from chain data, so keep a backup
            before upgrades.
          </p>
          <a
            href="/api/backup"
            download
            className="inline-block rounded-md border border-line-strong px-3 py-1.5 text-sm hover:bg-raised"
          >
            Download database backup
          </a>
        </Panel>
      )}
    </div>
  );
}
