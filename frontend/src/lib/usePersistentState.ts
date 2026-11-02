import { useCallback, useState } from "react";

/** useState backed by localStorage (JSON). Falls back to the initial value if storage is unavailable or corrupt. */
export function usePersistentState<T>(key: string, initial: T) {
  const [value, setValue] = useState<T>(() => {
    try {
      const raw = localStorage.getItem(key);
      return raw === null ? initial : (JSON.parse(raw) as T);
    } catch {
      return initial;
    }
  });

  const set = useCallback(
    (next: T) => {
      setValue(next);
      try {
        localStorage.setItem(key, JSON.stringify(next));
      } catch {
        // storage full or blocked; keep in-memory state only
      }
    },
    [key],
  );

  return [value, set] as const;
}
