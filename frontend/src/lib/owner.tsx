import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ApiError, request } from "../api/http";
import type { WatchedAddress } from "../api/types";

/** Wallets used to live only in the browser under this key; they are moved to the account on the next login. */
const LEGACY_ADDRESSES_KEY = "lpdash.addresses";
/** Which wallet the header shows stays per-device. */
const SELECTED_KEY = "lpdash.owner";
const ADDRESSES_KEY = ["addresses"] as const;

export interface LocalAddress {
  address: string;
  label: string;
}

interface OwnerContextValue {
  /** The wallets saved to the account, oldest first; every device shows the same list. */
  addresses: LocalAddress[];
  /** The wallet picked in the header; null means all of `addresses`. */
  selected: string | null;
  /** `owner` query value for the API: the selected wallet, else every followed wallet comma-separated; null with none. */
  owner: string | null;
  /** Every followed wallet, comma-separated (for sync status and "sync all"); null with none. */
  allOwners: string | null;
  setOwner: (owner: string | null) => void;
  /** Stops following a wallet. Its stored data stays on the server, so adding it again brings it back. */
  removeAddress: (address: string) => Promise<void>;
  setLabel: (address: string, label: string) => Promise<void>;
}

const OwnerContext = createContext<OwnerContextValue>({
  addresses: [],
  selected: null,
  owner: null,
  allOwners: null,
  setOwner: () => {},
  removeAddress: async () => {},
  setLabel: async () => {},
});

/** The server lowercases EVM addresses; do the same so one wallet is never listed twice. */
export const normalizeAddress = (address: string) => {
  const value = address.trim();
  return /^0x[0-9a-fA-F]{40}$/.test(value) ? value.toLowerCase() : value;
};

function readLegacyAddresses(): LocalAddress[] {
  try {
    const parsed: unknown = JSON.parse(
      localStorage.getItem(LEGACY_ADDRESSES_KEY) ?? "[]",
    );
    if (!Array.isArray(parsed)) return [];
    const seen = new Set<string>();
    return parsed.flatMap((a) => {
      if (!a || typeof a.address !== "string") return [];
      const address = normalizeAddress(a.address);
      if (!address || seen.has(address)) return [];
      seen.add(address);
      return [{ address, label: typeof a.label === "string" ? a.label : "" }];
    });
  } catch {
    return [];
  }
}

function saveSelected(value: string | null) {
  try {
    if (value) localStorage.setItem(SELECTED_KEY, value);
    else localStorage.removeItem(SELECTED_KEY);
  } catch {
    // Storage can be blocked (private mode); the choice then lasts for this tab only.
  }
}

/**
 * Uploads wallets (and labels) found in this browser's old local list to the account, then clears the local copy.
 * A label already saved on the server wins. Returns how many wallets were uploaded. If the server could not be reached
 * the local list is kept so the next login tries again; addresses the server rejects as invalid are skipped.
 */
async function importLegacyAddresses(): Promise<number> {
  const legacy = readLegacyAddresses();
  let uploaded = 0;
  for (const wallet of legacy) {
    try {
      await request<WatchedAddress>("/api/addresses", {
        method: "POST",
        body: JSON.stringify(wallet),
      });
      uploaded++;
    } catch (e) {
      if (e instanceof ApiError && e.status === 400) continue;
      return uploaded;
    }
  }
  try {
    localStorage.removeItem(LEGACY_ADDRESSES_KEY);
  } catch {
    // Nothing more to do if storage is blocked.
  }
  return uploaded;
}

export function OwnerProvider({ children }: { children: ReactNode }) {
  const client = useQueryClient();
  const list = useQuery({
    queryKey: ADDRESSES_KEY,
    queryFn: () => request<WatchedAddress[]>("/api/addresses"),
    staleTime: 30_000,
  });
  const [selectedRaw, setSelected] = useState<string | null>(() => {
    try {
      return localStorage.getItem(SELECTED_KEY);
    } catch {
      return null;
    }
  });
  const [moved, setMoved] = useState(0);
  const importStarted = useRef(false);

  // Once the account's list has loaded, move any wallets this browser still holds into it (once per page load).
  useEffect(() => {
    if (!list.isSuccess || importStarted.current) return;
    importStarted.current = true;
    void importLegacyAddresses().then((count) => {
      if (count === 0) return;
      setMoved(count);
      void client.invalidateQueries({ queryKey: ADDRESSES_KEY });
    });
  }, [list.isSuccess, client]);

  useEffect(() => {
    if (moved === 0) return;
    const timer = setTimeout(() => setMoved(0), 10_000);
    return () => clearTimeout(timer);
  }, [moved]);

  const addresses = useMemo<LocalAddress[]>(
    () => (list.data ?? []).map((a) => ({ address: a.address, label: a.label })),
    [list.data],
  );

  const selected =
    selectedRaw && addresses.some((a) => a.address === selectedRaw)
      ? selectedRaw
      : null;

  const value = useMemo<OwnerContextValue>(() => {
    const allOwners = addresses.length
      ? addresses.map((a) => a.address).join(",")
      : null;
    return {
      addresses,
      selected,
      owner: selected ?? allOwners,
      allOwners,
      setOwner: (owner) => {
        saveSelected(owner);
        setSelected(owner);
      },
      removeAddress: async (address) => {
        await request<void>(`/api/addresses/${encodeURIComponent(address)}`, {
          method: "DELETE",
        });
        client.setQueryData<WatchedAddress[]>(ADDRESSES_KEY, (old) =>
          old?.filter((a) => a.address !== address),
        );
      },
      setLabel: async (address, label) => {
        const saved = await request<WatchedAddress>(
          `/api/addresses/${encodeURIComponent(address)}`,
          { method: "PATCH", body: JSON.stringify({ label: label.trim() }) },
        );
        client.setQueryData<WatchedAddress[]>(ADDRESSES_KEY, (old) =>
          old?.map((a) => (a.address === saved.address ? saved : a)),
        );
      },
    };
  }, [addresses, selected, client]);

  if (list.isPending) {
    return (
      <div className="p-10 text-center text-sm text-faint">
        Loading your wallets…
      </div>
    );
  }

  if (list.isError) {
    return (
      <div className="mx-auto max-w-md space-y-3 p-10 text-center text-sm">
        <div className="text-down">
          Could not load your wallets: {list.error.message}
        </div>
        <button
          className="rounded-md border border-line-strong px-3 py-1.5 text-fg-2 hover:bg-raised"
          onClick={() => void list.refetch()}
        >
          Try again
        </button>
      </div>
    );
  }

  return (
    <OwnerContext.Provider value={value}>
      {children}
      {moved > 0 && (
        <div
          role="status"
          className="fixed bottom-4 right-4 z-30 flex max-w-sm items-start gap-3 rounded-lg border border-up-fill/40 bg-panel px-4 py-3 text-sm text-up shadow-xl"
        >
          <span>
            Moved {moved} {moved === 1 ? "wallet" : "wallets"} and{" "}
            {moved === 1 ? "its label" : "their labels"} from this browser to
            your account.
          </span>
          <button
            className="text-muted hover:text-fg"
            aria-label="Dismiss"
            onClick={() => setMoved(0)}
          >
            ×
          </button>
        </div>
      )}
    </OwnerContext.Provider>
  );
}

export const useOwner = () => useContext(OwnerContext);
