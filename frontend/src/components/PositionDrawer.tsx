import { useEffect, useRef } from "react";
import { PositionDetailPage } from "../pages/PositionDetailPage";
import { usePositionDrawer } from "../lib/positionDrawer";

/** Slide-in panel with the full position detail, so lists keep their place underneath. */
export function PositionDrawer() {
  const { openId, close } = usePositionDrawer();
  const ref = useRef<HTMLElement>(null);

  useEffect(() => {
    if (openId == null) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && close();
    document.addEventListener("keydown", onKey);
    ref.current?.focus();
    ref.current?.scrollTo(0, 0);
    return () => document.removeEventListener("keydown", onKey);
  }, [openId, close]);

  if (openId == null) return null;
  return (
    <>
      <div
        className="fixed inset-0 z-40 bg-black/45"
        aria-hidden="true"
        onClick={close}
      />
      <aside
        ref={ref}
        tabIndex={-1}
        role="dialog"
        aria-label="Position detail"
        className="fixed inset-y-0 right-0 z-50 w-full max-w-3xl overflow-y-auto border-l border-line bg-surface p-4 shadow-2xl outline-none"
      >
        <PositionDetailPage id={openId} onClose={close} />
      </aside>
    </>
  );
}
