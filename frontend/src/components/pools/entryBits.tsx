import { useEffect, useState } from "react";

/** Shared look of the planner's text and number inputs. */
export const input = "num rounded-md border border-slate-700 bg-slate-950 px-2.5 py-1.5 text-sm text-slate-50";

export function NumberField({
  label,
  value,
  onChange,
  step,
  width,
  min = 0,
}: {
  label: string;
  value: number;
  onChange: (v: number) => void;
  step: number;
  width: string;
  min?: number;
}) {
  const [text, setText] = useState(String(value));
  useEffect(() => setText((t) => (Number(t) === value ? t : String(value))), [value]);
  return (
    <label className="flex flex-col gap-1 text-xs text-slate-400">
      {label}
      <input
        type="number"
        inputMode="decimal"
        min={min}
        step={step}
        value={text}
        onChange={(e) => {
          setText(e.target.value);
          const v = Number(e.target.value);
          if (e.target.value.trim() !== "" && Number.isFinite(v) && v >= min) onChange(v);
        }}
        className={`${input} ${width}`}
      />
    </label>
  );
}


export function Tile({ label, value, sub }: { label: string; value: string; sub?: string }) {
  return (
    <div className="rounded-lg bg-slate-800/60 px-2.5 py-2">
      <span className="block text-xs text-slate-400">{label}</span>
      <b className="num block text-base font-medium">{value}</b>
      {sub && <span className="num block text-xs text-slate-500">{sub}</span>}
    </div>
  );
}

