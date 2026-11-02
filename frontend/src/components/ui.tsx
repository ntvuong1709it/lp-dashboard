import { useState, type ReactNode } from "react";

export function Skeleton({ className = "" }: { className?: string }) {
  return (
    <div
      aria-hidden="true"
      className={`animate-pulse rounded bg-slate-800 ${className}`}
    />
  );
}

/** Placeholder rows for a table that is still loading. */
export function TableSkeleton({ rows = 6 }: { rows?: number }) {
  return (
    <div
      className="space-y-2 rounded-lg border border-slate-800 p-3"
      role="status"
      aria-label="Loading"
    >
      <Skeleton className="h-6 w-full bg-slate-800/80" />
      {Array.from({ length: rows }, (_, i) => (
        <Skeleton key={i} className="h-10 w-full bg-slate-800/50" />
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
    <div className="rounded-lg border border-slate-800 bg-slate-900 p-4">
      <div className="text-xs uppercase tracking-wide text-slate-400">
        {label}
      </div>
      {loading ? (
        <Skeleton className="mt-2 h-6 w-24" />
      ) : (
        <div className={`mt-1 text-xl font-semibold num ${valueClass ?? ""}`}>
          {value}
        </div>
      )}
      {hint && !loading && (
        <div className="mt-1 text-xs text-slate-500">{hint}</div>
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
    <section className="rounded-lg border border-slate-800 bg-slate-900">
      <div className="flex items-center justify-between border-b border-slate-800 px-4 py-2">
        <h2 className="text-sm font-medium text-slate-300">{title}</h2>
        {actions}
      </div>
      <div className="p-4">{children}</div>
    </section>
  );
}

export function Empty({ children }: { children: ReactNode }) {
  return (
    <div className="py-10 text-center text-sm text-slate-500">{children}</div>
  );
}

export function SyncErrorList({ errors }: { errors: string[] }) {
  return (
    <ul className="max-h-72 space-y-2 overflow-y-auto">
      {errors.map((e, i) => (
        <li
          key={i}
          className="whitespace-pre-wrap break-words rounded-md border border-amber-500/30 bg-amber-500/10 px-3 py-2 font-mono text-xs text-amber-200"
        >
          {e}
        </li>
      ))}
    </ul>
  );
}

export function ErrorText({ error }: { error: unknown }) {
  return (
    <div className="text-sm text-rose-400">
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
      className="text-sky-400 hover:underline"
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
      className="rounded px-1 text-xs text-slate-500 hover:bg-slate-800 hover:text-slate-200"
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
