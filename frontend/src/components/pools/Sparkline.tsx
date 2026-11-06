/**
 * A tiny trend line or bar strip with no axes. Missing days (null) are skipped for lines and left as gaps for bars.
 * Colour comes from `currentColor`, so the parent sets it with a text class and it follows the theme.
 */
export function Sparkline({
  values,
  kind = "line",
  className = "h-7 w-full",
}: {
  values: (number | null)[];
  kind?: "line" | "bar";
  className?: string;
}) {
  const nums = values.filter((v): v is number => v != null);
  if (nums.length < 2) return <div className={className} aria-hidden="true" />;

  const W = 100;
  const H = 28;
  const lo = kind === "bar" ? 0 : Math.min(...nums);
  const hi = Math.max(...nums);
  const span = hi - lo || 1;
  const x = (i: number) => (values.length === 1 ? W / 2 : (i / (values.length - 1)) * W);
  const y = (v: number) => H - 2 - ((v - lo) / span) * (H - 4);

  return (
    <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" className={className} aria-hidden="true">
      {kind === "line" ? (
        <polyline
          fill="none"
          stroke="currentColor"
          strokeWidth={1.5}
          strokeLinejoin="round"
          strokeLinecap="round"
          vectorEffect="non-scaling-stroke"
          points={values.flatMap((v, i) => (v == null ? [] : [`${x(i).toFixed(2)},${y(v).toFixed(2)}`])).join(" ")}
        />
      ) : (
        values.map((v, i) =>
          v == null ? null : (
            <rect
              key={i}
              x={x(i) - W / values.length / 2 + 0.3}
              width={Math.max(W / values.length - 0.6, 0.5)}
              y={y(v)}
              height={Math.max(H - 2 - y(v), 0.5)}
              fill="currentColor"
            />
          ),
        )
      )}
    </svg>
  );
}
