import { useEffect, useRef, useState } from "react";

export interface ExportItem {
  label: string;
  href: string;
}

/** Dropdown of CSV download links (plain links, so the browser handles the download). */
export function ExportMenu({ items }: { items: ExportItem[] }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onClick = (e: MouseEvent) => {
      if (!ref.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && setOpen(false);
    document.addEventListener("mousedown", onClick);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onClick);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  return (
    <div ref={ref} className="relative">
      <button
        className="rounded-md border border-slate-700 px-2 py-1.5 text-sm text-slate-200 hover:bg-slate-800"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
      >
        Export CSV ▾
      </button>
      {open && (
        <ul className="absolute right-0 z-20 mt-1 w-56 rounded-md border border-slate-700 bg-slate-900 py-1 text-sm shadow-xl">
          {items.map((item) => (
            <li key={item.href}>
              <a
                className="block px-3 py-1.5 text-slate-200 hover:bg-slate-800"
                href={item.href}
                download
                onClick={() => setOpen(false)}
              >
                {item.label}
              </a>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
