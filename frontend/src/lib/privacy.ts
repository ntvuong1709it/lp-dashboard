import { useSyncExternalStore } from "react";

const STORAGE_KEY = "lpdash.hideBalances";
const MASK = "••••";

function read(): boolean {
  try {
    return localStorage.getItem(STORAGE_KEY) === "true";
  } catch {
    return false;
  }
}

let hidden = read();
const listeners = new Set<() => void>();

/** Current value for non-React callers (the formatters). */
export const balancesHidden = () => hidden;

/** Replacement text for dollar values and token amounts while balances are hidden. */
export const maskedBalance = MASK;

export function setBalancesHidden(value: boolean) {
  hidden = value;
  try {
    localStorage.setItem(STORAGE_KEY, String(value));
  } catch {
    // storage blocked; keep in-memory state only
  }
  listeners.forEach((l) => l());
}

const subscribe = (listener: () => void) => {
  listeners.add(listener);
  return () => listeners.delete(listener);
};

/** Subscribe a component so it re-renders when the toggle changes. */
export const useHideBalances = () =>
  useSyncExternalStore(subscribe, balancesHidden);
