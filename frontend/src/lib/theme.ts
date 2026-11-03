import { useSyncExternalStore } from "react";

export type Theme = "dark" | "light";

const STORAGE_KEY = "lpdash.theme";

function read(): Theme {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === "dark" || stored === "light") return stored;
  } catch {
    // storage blocked; fall through to the system preference
  }
  return matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark";
}

let theme = read();
const listeners = new Set<() => void>();

const apply = () => {
  document.documentElement.dataset.theme = theme;
};
apply();

export function setTheme(value: Theme) {
  theme = value;
  apply();
  try {
    localStorage.setItem(STORAGE_KEY, value);
  } catch {
    // storage blocked; keep in-memory state only
  }
  listeners.forEach((l) => l());
}

const subscribe = (listener: () => void) => {
  listeners.add(listener);
  return () => listeners.delete(listener);
};

export const useTheme = () => useSyncExternalStore(subscribe, () => theme);
