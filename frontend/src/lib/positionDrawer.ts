import { useCallback } from "react";
import { useSearchParams } from "react-router-dom";

const PARAM = "position";

/** The position detail drawer is driven by `?position=<id>`, so it opens over any page and survives a reload. */
export function usePositionDrawer() {
  const [params, setParams] = useSearchParams();
  const raw = Number(params.get(PARAM));
  const openId = Number.isInteger(raw) && raw > 0 ? raw : null;

  const open = useCallback(
    (id: number) =>
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          next.set(PARAM, String(id));
          return next;
        },
        { replace: false },
      ),
    [setParams],
  );
  const close = useCallback(
    () =>
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          next.delete(PARAM);
          return next;
        },
        { replace: true },
      ),
    [setParams],
  );

  return { openId, open, close };
}
