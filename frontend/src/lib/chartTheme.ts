/** Shared recharts styling; colours come from CSS variables so charts follow the light/dark theme. */
export const chart = {
  grid: "var(--chart-grid)",
  axis: "var(--chart-axis)",
  cursor: "var(--chart-cursor)",
  a: "var(--chart-a)",
  b: "var(--chart-b)",
  c: "var(--chart-c)",
  /** Violet; the Earnings page uses it for reward tokens. */
  d: "var(--chart-d)",
  tooltip: {
    background: "var(--chart-tip-bg)",
    border: "1px solid var(--chart-tip-border)",
  },
};
