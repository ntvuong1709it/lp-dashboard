import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useState,
  type ReactNode,
} from "react";

const ADDRESSES_KEY = "lpdash.addresses";
const SELECTED_KEY = "lpdash.owner";

export interface LocalAddress {
  address: string;
  label: string;
}

interface OwnerContextValue {
  /** Wallets this browser follows. The server never lists wallets, so this is the only list. */
  addresses: LocalAddress[];
  /** The wallet picked in the header; null means all of `addresses`. */
  selected: string | null;
  /** `owner` query value for the API: the selected wallet, else every followed wallet comma-separated; null with none. */
  owner: string | null;
  /** Every followed wallet, comma-separated (for sync status and "sync all"); null with none. */
  allOwners: string | null;
  setOwner: (owner: string | null) => void;
  addAddress: (address: string, label?: string) => void;
  removeAddress: (address: string) => void;
  setLabel: (address: string, label: string) => void;
}

const OwnerContext = createContext<OwnerContextValue>({
  addresses: [],
  selected: null,
  owner: null,
  allOwners: null,
  setOwner: () => {},
  addAddress: () => {},
  removeAddress: () => {},
  setLabel: () => {},
});

/** The server lowercases EVM addresses; do the same so one wallet is never stored twice. */
export const normalizeAddress = (address: string) => {
  const value = address.trim();
  return /^0x[0-9a-fA-F]{40}$/.test(value) ? value.toLowerCase() : value;
};

function loadAddresses(): LocalAddress[] {
  try {
    const parsed: unknown = JSON.parse(
      localStorage.getItem(ADDRESSES_KEY) ?? "[]",
    );
    if (!Array.isArray(parsed)) return [];
    return parsed.flatMap((a) =>
      a && typeof a.address === "string"
        ? [{ address: a.address, label: typeof a.label === "string" ? a.label : "" }]
        : [],
    );
  } catch {
    return [];
  }
}

function save(key: string, value: string | null) {
  try {
    if (value) localStorage.setItem(key, value);
    else localStorage.removeItem(key);
  } catch {
    // Storage can be blocked (private mode); the list then lasts for this tab only.
  }
}

export function OwnerProvider({ children }: { children: ReactNode }) {
  const [addresses, setAddresses] = useState<LocalAddress[]>(loadAddresses);
  const [selectedRaw, setSelected] = useState<string | null>(() => {
    try {
      return localStorage.getItem(SELECTED_KEY);
    } catch {
      return null;
    }
  });

  const selected =
    selectedRaw && addresses.some((a) => a.address === selectedRaw)
      ? selectedRaw
      : null;

  const update = useCallback((next: LocalAddress[]) => {
    setAddresses(next);
    save(ADDRESSES_KEY, next.length ? JSON.stringify(next) : null);
  }, []);

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
        save(SELECTED_KEY, owner);
        setSelected(owner);
      },
      addAddress: (address, label = "") => {
        const normalized = normalizeAddress(address);
        if (addresses.some((a) => a.address === normalized)) return;
        update([...addresses, { address: normalized, label: label.trim() }]);
      },
      removeAddress: (address) =>
        update(addresses.filter((a) => a.address !== address)),
      setLabel: (address, label) =>
        update(
          addresses.map((a) =>
            a.address === address ? { ...a, label: label.trim() } : a,
          ),
        ),
    };
  }, [addresses, selected, update]);

  return (
    <OwnerContext.Provider value={value}>{children}</OwnerContext.Provider>
  );
}

export const useOwner = () => useContext(OwnerContext);
