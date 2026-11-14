// Checks the colour tokens in src/tokens.css against the contrast rules in
// docs/superpowers/specs/2026-11-14-frontend-polish-design.md. Run from frontend/: node scripts/check-contrast.mjs
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const css = readFileSync(
  fileURLToPath(new URL("../src/tokens.css", import.meta.url)),
  "utf8",
);

const blocks = {
  dark: css.match(/:root\s*\{([^}]*)\}/)?.[1],
  light: css.match(/:root\[data-theme="light"\]\s*\{([^}]*)\}/)?.[1],
};

function parse(block) {
  const vars = {};
  for (const m of block.matchAll(/--([a-z0-9-]+)\s*:\s*(#[0-9a-fA-F]{6})\s*;/g))
    vars[m[1]] = m[2];
  return vars;
}

const channel = (v) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4);
function luminance(hex) {
  const n = parseInt(hex.slice(1), 16);
  const [r, g, b] = [(n >> 16) & 255, (n >> 8) & 255, n & 255].map((c) => channel(c / 255));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}
const ratio = (a, b) => {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
};

const TEXT = ["fg", "fg-2", "muted", "faint", "accent", "up", "down", "warn"];
const FILLS = ["accent-fill", "up-fill", "down-fill", "warn-fill"];
const MIN_SEPARATION = 0.6; // muted must beat faint on panel by this much, so they stay distinguishable

const failures = [];
let checked = 0;
for (const theme of ["dark", "light"]) {
  if (!blocks[theme]) {
    failures.push(`${theme}: token block not found in src/tokens.css`);
    continue;
  }
  const t = parse(blocks[theme]);
  const need = (fg, bg, min) => {
    checked++;
    if (!t[fg] || !t[bg]) {
      failures.push(`${theme}  ${fg} on ${bg}  missing token  needs ${min}`);
      return;
    }
    const r = ratio(t[fg], t[bg]);
    if (r < min) failures.push(`${theme}  ${fg} on ${bg}  ${r.toFixed(2)}  needs ${min}`);
  };
  for (const fg of TEXT) for (const bg of ["surface", "panel"]) need(fg, bg, 4.5);
  for (const fg of ["fg", "fg-2", "muted"]) need(fg, "raised", 4.5);
  need("control", "panel", 3);
  need("control", "surface", 3);
  for (const fill of FILLS) need(fill, "panel", 3);
  // Badges and pills put a hue's text on a translucent tint of its own fill (bg-up-fill/20 and the like).
  for (const [text, fill] of [["accent", "accent-fill"], ["up", "up-fill"], ["down", "down-fill"], ["warn", "warn-fill"]]) {
    for (const bg of ["panel", "surface"]) {
      for (const alpha of [0.1, 0.15, 0.2]) {
        checked++;
        if (!t[text] || !t[fill] || !t[bg]) continue;
        const mix = (c, k) => Math.round(parseInt(t[fill].slice(k, k + 2), 16) * alpha + parseInt(t[bg].slice(k, k + 2), 16) * (1 - alpha));
        const tinted = "#" + [1, 3, 5].map((k) => mix(null, k).toString(16).padStart(2, "0")).join("");
        const r = ratio(t[text], tinted);
        if (r < 4.5) failures.push(`${theme}  ${text} on ${fill}/${Math.round(alpha * 100)} over ${bg}  ${r.toFixed(2)}  needs 4.5`);
      }
    }
  }
  if (t["accent-solid"]) {
    checked++;
    const r = ratio("#ffffff", t["accent-solid"]);
    if (r < 4.5) failures.push(`${theme}  white on accent-solid  ${r.toFixed(2)}  needs 4.5`);
  } else failures.push(`${theme}  accent-solid missing`);
  if (t.muted && t.faint && t.panel) {
    checked++;
    const gap = ratio(t.muted, t.panel) - ratio(t.faint, t.panel);
    if (gap < MIN_SEPARATION)
      failures.push(`${theme}  muted vs faint on panel  gap ${gap.toFixed(2)}  needs ${MIN_SEPARATION}`);
  }
}

if (failures.length) {
  console.error(failures.join("\n"));
  console.error(`\n${failures.length} of ${checked} checks failed`);
  process.exit(1);
}
console.log(`all pairs pass (${checked} checks)`);
