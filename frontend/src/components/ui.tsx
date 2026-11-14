import { useState, type ReactNode } from "react";

export function Skeleton({ className = "" }: { className?: string }) {
  return (
    <div
      aria-hidden="true"
      className={`animate-pulse rounded bg-raised ${className}`}
    />
  );
}

/** Placeholder rows for a table that is still loading. */
export function TableSkeleton({ rows = 6 }: { rows?: number }) {
  return (
    <div
      className="space-y-2 rounded-lg border border-line p-3"
      role="status"
      aria-label="Loading"
    >
      <Skeleton className="h-6 w-full bg-raised/80" />
      {Array.from({ length: rows }, (_, i) => (
        <Skeleton key={i} className="h-10 w-full bg-raised/50" />
      ))}
    </div>
  );
}

/** Placeholder for a page of stat cards followed by panels. */
export function PageSkeleton({ cards = 6 }: { cards?: number }) {
  return (
    <div className="space-y-6" role="status" aria-label="Loading">
      <div className="grid grid-cols-2 gap-3 md:grid-cols-3 lg:grid-cols-6">
        {Array.from({ length: cards }, (_, i) => (
          <Skeleton key={i} className="h-20 rounded-lg" />
        ))}
      </div>
      <Skeleton className="h-64 w-full rounded-lg" />
    </div>
  );
}

export function StatCard({
  label,
  value,
  hint,
  valueClass,
  loading,
}: {
  label: string;
  value: ReactNode;
  hint?: ReactNode;
  valueClass?: string;
  loading?: boolean;
}) {
  return (
    <div className="rounded-lg border border-line bg-panel p-4">
      <div className="text-xs uppercase tracking-[.05em] text-muted">
        {label}
      </div>
      {loading ? (
        <Skeleton className="mt-2 h-6 w-24" />
      ) : (
        <div className={`mt-1 text-2xl font-medium tracking-tight num ${valueClass ?? ""}`}>
          {value}
        </div>
      )}
      {hint && !loading && (
        <div className="mt-1 text-xs text-faint">{hint}</div>
      )}
    </div>
  );
}

export function Panel({
  title,
  children,
  actions,
}: {
  title: string;
  children: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <section className="rounded-lg border border-line bg-panel">
      <div className="flex items-center justify-between border-b border-line px-4 py-2">
        <h2 className="text-sm font-medium text-fg-2">{title}</h2>
        {actions}
      </div>
      <div className="p-4">{children}</div>
    </section>
  );
}

export function Empty({ children }: { children: ReactNode }) {
  return (
    <div className="py-10 text-center text-sm text-faint">{children}</div>
  );
}

export function SyncErrorList({ errors }: { errors: string[] }) {
  return (
    <ul className="max-h-72 space-y-2 overflow-y-auto">
      {errors.map((e, i) => (
        <li
          key={i}
          className="whitespace-pre-wrap break-words rounded-md border border-warn-fill/30 bg-warn-fill/10 px-3 py-2 font-mono text-xs text-warn"
        >
          {e}
        </li>
      ))}
    </ul>
  );
}

export function ErrorText({ error }: { error: unknown }) {
  return (
    <div className="text-sm text-down">
      {error instanceof Error ? error.message : "Something went wrong"}
    </div>
  );
}

/** Link that opens in a new tab and never triggers a parent row click. */
export function ExternalLink({
  href,
  children,
  title,
}: {
  href: string;
  children: ReactNode;
  title?: string;
}) {
  return (
    <a
      className="text-accent hover:underline"
      href={href}
      target="_blank"
      rel="noreferrer"
      title={title}
      onClick={(e) => e.stopPropagation()}
    >
      {children}
    </a>
  );
}

function copyText(text: string): Promise<void> {
  if (navigator.clipboard?.writeText) return navigator.clipboard.writeText(text);
  // Clipboard API needs a secure context; fall back for plain-http access.
  return new Promise((resolve, reject) => {
    const area = document.createElement("textarea");
    area.value = text;
    area.style.position = "fixed";
    area.style.opacity = "0";
    document.body.appendChild(area);
    area.select();
    const ok = document.execCommand("copy");
    area.remove();
    if (ok) resolve();
    else reject(new Error("copy failed"));
  });
}

export function CopyButton({
  text,
  title = "Copy",
}: {
  text: string;
  title?: string;
}) {
  const [state, setState] = useState<"idle" | "copied" | "failed">("idle");

  return (
    <button
      type="button"
      title={title}
      className="rounded px-1 text-xs text-faint hover:bg-raised hover:text-fg-2"
      onClick={(e) => {
        e.stopPropagation();
        copyText(text).then(
          () => setState("copied"),
          () => setState("failed"),
        );
        setTimeout(() => setState("idle"), 1200);
      }}
    >
      {state === "copied" ? "copied" : state === "failed" ? "failed" : "copy"}
    </button>
  );
}

/** One labelled horizontal bar: label, proportional track, value. */
export function BarRow({
  label,
  fraction,
  value,
  valueClass = "",
  barClass = "bg-accent-fill",
  onClick,
  sub,
}: {
  label: ReactNode;
  /** 0..1 share of the track to fill. */
  fraction: number;
  value: ReactNode;
  valueClass?: string;
  barClass?: string;
  onClick?: () => void;
  sub?: ReactNode;
}) {
  return (
    <div
      className={`grid grid-cols-[8rem_1fr_6.5rem] items-center gap-3 text-sm ${onClick ? "cursor-pointer hover:bg-raised/50" : ""}`}
      onClick={onClick}
    >
      <span className="truncate" title={typeof label === "string" ? label : undefined}>
        {label}
      </span>
      <div className="h-2 overflow-hidden rounded bg-raised">
        <div
          className={`h-full rounded ${barClass}`}
          style={{ width: `${Math.max(0, Math.min(1, fraction)) * 100}%` }}
        />
      </div>
      <span className={`text-right num ${valueClass}`}>
        {value}
        {sub && <span className="ml-1 text-xs text-faint">{sub}</span>}
      </span>
    </div>
  );
}

/** Compact segmented control (single choice). */
export function Segmented<T extends string | number | null>({
  options,
  value,
  onChange,
  label,
}: {
  options: { label: string; value: T }[];
  value: T;
  onChange: (value: T) => void;
  label: string;
}) {
  return (
    <div
      role="group"
      aria-label={label}
      className="inline-flex overflow-hidden rounded-md border border-line-strong text-sm"
    >
      {options.map((o) => (
        <button
          key={String(o.value)}
          type="button"
          aria-pressed={o.value === value}
          onClick={() => onChange(o.value)}
          className={`border-r border-line-strong px-2.5 py-1 last:border-r-0 ${o.value === value ? "bg-raised font-semibold text-fg" : "text-muted hover:bg-raised/60"}`}
        >
          {o.label}
        </button>
      ))}
    </div>
  );
}

/** Diverging bars (gains right, losses left of a centre line) that add up to a total. */
export function Waterfall({
  steps,
  total,
  totalLabel,
  format,
  pnlClass,
}: {
  steps: { label: string; value: number }[];
  total: number;
  totalLabel: string;
  format: (n: number) => string;
  pnlClass: (n: number) => string;
}) {
  const max = Math.max(...steps.map((s) => Math.abs(s.value)), Math.abs(total), 1);
  const row = (label: string, value: number, bold = false) => (
    <div
      key={label}
      className="grid grid-cols-[9rem_1fr_6.5rem] items-center gap-3 text-sm"
    >
      <span className={bold ? "font-semibold" : "text-muted"}>{label}</span>
      <div className="relative h-3">
        <div className="absolute inset-y-0 left-1/2 w-px bg-line-strong" />
        <div
          className={`absolute inset-y-0.5 rounded-sm ${value >= 0 ? "bg-up-fill/80" : "bg-down-fill/80"}`}
          style={{
            width: `${(Math.abs(value) / max) * 50}%`,
            [value >= 0 ? "left" : "right"]: "50%",
          }}
        />
      </div>
      <span
        className={`text-right num ${pnlClass(value)} ${bold ? "font-semibold" : ""}`}
      >
        {format(value)}
      </span>
    </div>
  );
  return (
    <div className="space-y-2">
      {steps.map((s) => row(s.label, s.value))}
      <div className="border-t border-line pt-2">{row(totalLabel, total, true)}</div>
    </div>
  );
}

/** Small label/value tile used in the position drawer summary. */
export function MiniStat({
  label,
  value,
  valueClass = "",
}: {
  label: string;
  value: ReactNode;
  valueClass?: string;
}) {
  return (
    <div className="rounded-lg border border-line bg-panel px-3 py-2.5">
      <span className="block text-xs text-muted">{label}</span>
      <b className={`text-base font-medium num ${valueClass}`}>{value}</b>
    </div>
  );
}
