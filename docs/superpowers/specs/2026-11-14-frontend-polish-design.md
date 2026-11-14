# Frontend polish pass: design

Status: draft for approval. Date: 2026-11-14. Scope: `frontend/` only.

## Intent and why

The user asked for a general polish pass on the frontend ("what should I improve", answered "general polish"; no specific annoyance named). A read of the source found these problems:

- The light theme is a hack. `frontend/src/index.css` swaps Tailwind's slate scale (so `slate-50` is dark in light mode) and overrides only some accent shades (`emerald-400`, `rose-400`, `sky-300/400/700/900/950`, `amber-200/300/400`). Components using other shades (`rose-300`, `orange-300`, `emerald-300`, `*-500/10` tints) were never mapped, so they likely have poor contrast in light mode. This is from reading the code; nobody has looked at it rendered.
- Muted text is faint. `text-slate-400` (183 uses) and `text-slate-500` (172 uses) are used for labels and hints, often at `text-xs`/`text-[11px]`. `slate-500` on `slate-900` is about 3.8:1, under the 4.5:1 AA threshold for small text.
- The header is crowded. One sticky block carries the logo, 7 nav links, wallet select, hide-balances, theme toggle, sync indicator (progress bar, error pill, "Sync now"), log out, and a price-ticker row. Every control is a bordered text button of equal weight.
- There are no semantic colour tokens. Components use raw `slate-*` classes (about 11 slate shades, 8 accent families in use), so any palette change means editing many files.
- Some interactions are not keyboard-reachable: `BarRow` (`ui.tsx`) puts `onClick` on a plain `div`, and the wallet `<select>` in `Layout.tsx` has no label.
- The Overview page is a tall stack of about 8 sections with no single "what needs me today" focal point.

Intent: make the app look and read better without changing what it is: a dense, dark-first LP tracking tool for one user. Fix the real defects (theme, contrast, keyboard access) first, then use the clean base for the two most visible surfaces (header, Overview).

## Goals

1. A real two-theme colour system: semantic tokens defined once per theme, replacing the slate-scale remap.
2. Light theme looks as deliberate as dark. Every colour a component uses has a light value.
3. All text meets WCAG AA (4.5:1 for normal text, 3:1 for large text and UI borders that carry meaning) on its actual background, in both themes. Checked by a script, not by eye.
4. Everything clickable is reachable and operable by keyboard; form controls have accessible names.
5. A calmer header: primary navigation and wallet selection stand out, utilities (theme, hide balances, sync, log out) are visually secondary.
6. An Overview page with a clear top-of-page answer to "does anything need me?".

## Non-goals

- No new pages, features, API or backend changes, and no data or number formatting changes.
- No chart library change. Chart colours may move to the new tokens, but chart behaviour stays.
- No rebrand: the name "LP Dashboard", fonts (IBM Plex, Bricolage Grotesque) and the sky-blue accent stay.
- No new runtime dependencies. Icons, if used, are small inline SVGs.
- No redesign of Borrow, Pools, Positions, P&L, Earnings or Addresses layouts. They get the new tokens and the keyboard fixes only. Layout changes there are a later project.

## Anti-goals (failure even if it "works")

- The dark theme must not look like a different app. Same hues and overall feel; changes are in contrast and consistency, not a restyle.
- No loss of data density. This is a tool for scanning numbers; padding must not balloon.
- No light-mode regressions elsewhere: after slice 1 no page may contain a colour that has no light value.
- No visual change to financial colour semantics: green is up/good, red is down/bad, amber is a warning, and these stay distinguishable (not by colour alone where text can say it).

## Constraints

- Stack: React 19, Tailwind 4 (`@tailwindcss/vite`, tokens via `@theme` in `index.css`), recharts 3, react-router 7. No test framework in `frontend/`; the gate is `npm run build` (`tsc --noEmit && vite build`) plus visual checks.
- The theme is switched by `data-theme` on `:root` (`src/lib/theme.ts`); keep that mechanism and persisted choice.
- Charts read `--chart-*` and `--wallet-*` CSS variables; keep those names working (or migrate them in the same change).
- When verifying in a browser, never use ports 5080/5173 (the user's own backend uses them); use others, build the API to a separate output folder, and stop servers afterwards. Test wallet and setup are in the project memory notes.
- Any new or changed UI design (header, Overview) is first shown to the user as an artifact mockup, light and dark, in the app's real look, and only built after they pick.

## Approaches considered

1. **Foundation first, then the visible parts (chosen).** Tokens, themes and contrast first; then keyboard fixes; then header; then Overview. Costs a mechanical migration across about 50 files up front; benefit is that the header and Overview are built once on a sound base and the real defects are gone early.
2. **Visible first.** Redo header and Overview, fix theme gaps as they appear. Faster to see, but the rest of the app keeps the broken theme and things get restyled twice.
3. **Targeted patches only.** Extend the existing overrides, bump a few text colours, add keyboard fixes. Cheapest and lowest risk, but it deepens the hack and changes nothing about how the header or Overview feel.

The user picked 1.

## The design

### Slice 1: tokens, themes, contrast

- Define semantic tokens in `index.css`, each with a dark and a light value (a `:root` block and a `:root[data-theme="light"]` block). Tailwind 4 `@theme` entries reference the variables so utilities like `bg-surface`, `text-muted`, `border-line` exist.
- Token set (names are a starting point, builder may adjust):
  - Surfaces: `surface` (page, today `slate-950`), `panel` (cards, today `slate-900`), `raised` (hover/track, today `slate-800`).
  - Lines: `line` (today `slate-800`), `line-strong` (today `slate-700`).
  - Text: `fg` (strongest), `fg-2` (body), `muted`, `faint`.
  - Accents, each with `text`, `fill` and `tint` (translucent background) variants: `accent` (sky), `up` (emerald), `down` (rose), `warn` (amber). `orange` is only needed for "Near edge"; fold it into `warn` or give it a token, builder's call.
- Migrate all `slate-*`, `sky-*`, `emerald-*`, `rose-*`, `amber-*`, `orange-*` colour classes in `frontend/src` (and the 6 hex literals in `.ts/.tsx`) to the tokens, with a script plus a manual review of the leftovers. After this, nothing references raw palette classes, and the slate-remap block in `index.css` is deleted.
- Contrast rule: every text token meets 4.5:1 against both `panel` and `surface` in both themes; `line-strong` and accent fills meet 3:1 where they carry meaning (input borders, range bars). `muted` and `faint` must still differ visibly. A small script (in `frontend/scripts/` or equivalent) computes the ratios from the token values and fails if any pair is under its threshold; run it in the slice's verification, not necessarily in CI.
- Dark token values start from today's slate/sky/emerald/rose/amber values; only the text tokens that fail the contrast rule (mainly `faint`, today `slate-500`) are lightened, by the smallest amount that passes. This is the design's call.
- Light theme keeps the current light look in spirit (white panels, cool grey page, same accent hues darkened) but is corrected, not redesigned. This is the design's call.

### Slice 2: keyboard and labels

- `BarRow` with `onClick` becomes a real button (or gets `role="button"`, `tabIndex`, Enter/Space handling); same for any other clickable non-interactive element found by grep for `onClick` on `div`/`tr`/`li`/`span`.
- Label the wallet select (`aria-label` is enough) and sweep other unlabelled inputs/selects/icon buttons.
- Check focus visibility on all token surfaces (the global `:focus-visible` ring must be visible on both themes).

### Slice 3: header

Direction (a proposal to be mocked, not decided): keep logo and 7 nav links and the wallet select as the primary row. Theme and hide-balances become compact icon buttons with tooltips and `aria-pressed`; sync becomes a compact status chip that still opens the existing details popover; "Log out" moves out of the main row (account menu, or into the existing Addresses → Account section). The price ticker stays but must not make the sticky header taller than today. On narrow screens the header collapses sensibly (today the nav wraps to its own row).
Output of this slice: an artifact with 2 or 3 header options in light and dark; the user picks; then implementation.

### Slice 4: Overview

Direction (a proposal to be mocked, not decided): a top strip that answers "does anything need me?" by promoting "Needs attention" and "Ready to claim" above or beside the stat cards, so the page opens with what to do; stat cards keep their content; the allocation and exposure panels stay below; the chart position is a mockup question. No section is removed unless the user says so.
Output of this slice: artifact mockup in light and dark; the user picks; then implementation.

### Verification (every slice)

- `npm run build` in `frontend/` passes.
- Slice 1 only: the contrast script passes; no raw palette classes remain (grep).
- Look at every page in both themes in a browser on non-default ports and compare with before. Slices 3 and 4 also check a narrow (phone) width.
- Report anything not checked.

## Left to the builder

- Exact token names and values (subject to the contrast rule), how to script the class migration, where the contrast script lives.
- How to treat the 6 hex literals and chart variables.
- Icon drawing for slice 3, once a mockup is picked.
- The order of work inside a slice.

## Open decisions (resolved at mockup time, by the user)

- Header option: where log out goes, ticker treatment, mobile layout.
- Overview option: how the top strip is arranged.
