import { useMemo } from "react";
import { shortAddress } from "./format";
import { useOwner } from "./owner";

export interface WalletInfo {
  address: string;
  /** The name given on the Addresses page; empty when there is none. */
  label: string;
  /** What to show as the wallet's name: the label, else the short address. */
  name: string;
  short: string;
  /** CSS colour for this wallet (a `--wallet-N` variable); fixed by the order wallets were added. */
  color: string;
}

const HUES = 6;

/** Looks up a followed wallet's name and colour by address; unknown addresses still get a stable fallback. */
export function useWallets() {
  const { addresses } = useOwner();
  return useMemo(() => {
    const byAddress = new Map<string, WalletInfo>();
    addresses.forEach((a, i) => {
      byAddress.set(a.address.toLowerCase(), {
        address: a.address,
        label: a.label,
        name: a.label || shortAddress(a.address),
        short: shortAddress(a.address),
        color: `var(--wallet-${(i % HUES) + 1})`,
      });
    });
    return (address: string): WalletInfo =>
      byAddress.get(address.toLowerCase()) ?? {
        address,
        label: "",
        name: shortAddress(address),
        short: shortAddress(address),
        color: "var(--color-slate-400)",
      };
  }, [addresses]);
}

export interface WalletGroup<T> {
  wallet: WalletInfo;
  items: T[];
}

/** Splits rows by wallet, keeping each wallet's rows in their given order; groups are ordered by `weight`, largest first. */
export function groupByWallet<T>(
  rows: T[],
  ownerOf: (row: T) => string,
  walletOf: (address: string) => WalletInfo,
  weight: (row: T) => number,
): WalletGroup<T>[] {
  const groups = new Map<string, WalletGroup<T>>();
  for (const row of rows) {
    const key = ownerOf(row).toLowerCase();
    let g = groups.get(key);
    if (!g) {
      g = { wallet: walletOf(ownerOf(row)), items: [] };
      groups.set(key, g);
    }
    g.items.push(row);
  }
  const total = (g: WalletGroup<T>) => g.items.reduce((s, r) => s + weight(r), 0);
  return [...groups.values()].sort((a, b) => total(b) - total(a));
}
