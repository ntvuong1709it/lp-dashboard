import type { ReactNode } from "react";

export function StatCard({
  label,
  value,
  hint,
  valueClass,
}: {
  label: string;
  value: ReactNode;
  hint?: ReactNode;
  valueClass?: string;
}) {
  return (
    <div className="rounded-lg border border-slate-800 bg-slate-900 p-4">
      <div className="text-xs uppercase tracking-wide text-slate-400">
        {label}
      </div>
      <div className={`mt-1 text-xl font-semibold num ${valueClass ?? ""}`}>
        {value}
      </div>
      {hint && <div className="mt-1 text-xs text-slate-500">{hint}</div>}
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
