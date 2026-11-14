import type { ReactNode } from "react";

export type PositionFact = { label: string; value: string };

/**
 * "Your position": the range and capital the Estimate and Enter tabs both read. Open it shows the editor (the children);
 * collapsed it shows the facts on one line. Neither the section nor the editor is ever unmounted, so a half-typed field survives.
 */
export function PositionBlock({
  open,
  onToggle,
  facts,
  hidden,
  children,
}: {
  open: boolean;
  onToggle: () => void;
  /** Shown only while collapsed. */
  facts: PositionFact[];
  hidden?: boolean;
  children: ReactNode;
}) {
  return (
    <section className="rounded-lg border border-line bg-surface p-3" aria-labelledby="position-heading" hidden={hidden}>
      <div className="flex items-center justify-between gap-2">
        <h3 id="position-heading" className="text-sm font-medium text-fg-2">
          Your position
        </h3>
        <button
          type="button"
          aria-expanded={open}
          aria-controls="position-body"
          onClick={onToggle}
          className="rounded-md border border-line-strong px-2 py-0.5 text-xs text-accent hover:bg-raised"
        >
          {open ? "Collapse" : "Edit"}
        </button>
      </div>
      {/* The editor stays mounted while collapsed, so aria-controls always points at a real element and a half-typed field is kept. */}
      <div id="position-body" className="mt-3 space-y-4" hidden={!open}>
        {children}
      </div>
      {!open && (
        <dl className="mt-2 flex flex-wrap gap-x-5 gap-y-1">
          {facts.map((f) => (
            <div key={f.label}>
              <dt className="text-xs text-muted">{f.label}</dt>
              <dd className="num text-sm text-fg">{f.value}</dd>
            </div>
          ))}
        </dl>
      )}
    </section>
  );
}
