# Frontend Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the slate-remap theme hack with semantic colour tokens that pass AA contrast in dark and light, fix keyboard gaps, then calm the header and give the Overview page a "what needs me" focal strip.

**Architecture:** One new stylesheet `frontend/src/tokens.css` defines every colour once per theme (`:root` = dark, `:root[data-theme="light"]` = light) and exposes them to Tailwind 4 through `@theme inline`, so utilities like `bg-panel`, `text-muted`, `border-line` exist. A one-off script rewrites the raw palette classes to those utilities. A Node script checks token contrast. The header and Overview are mocked as artifacts first and built only after the user picks.

**Tech Stack:** React 19, Tailwind 4 (`@tailwindcss/vite`), recharts 3, react-router 7, Vite 7, TypeScript 5.9. No frontend test framework; the gates are `npm run build`, the contrast script, greps, and browser checks.

**Spec:** `docs/superpowers/specs/2026-11-14-frontend-polish-design.md`

## Global Constraints

- Scope is `frontend/` only (plus docs). No backend, API, data or number-format changes; no new runtime dependencies; icons are inline SVG.
- Keep the name "LP Dashboard", the fonts (IBM Plex Sans/Mono, Bricolage Grotesque) and the sky-blue accent. No rebrand.
- Theme switching stays `data-theme` on `:root` via `src/lib/theme.ts`; keep the persisted choice and the dark default.
- Chart variable names `--chart-*`, `--wallet-*`, `--marker`, `--band-*` keep working (charts and Borrow read them).
- All text tokens meet 4.5:1 against `surface` and `panel` in both themes; fills that carry meaning meet 3:1.
- Dark must not look like a different app: dark token values start from today's values; change only what fails contrast, by the smallest amount.
- Do not reduce data density (no padding inflation).
- Never use ports 5080 or 5173 (the user's own backend). Never open the user's real database; work on a copy. Stop servers you start. Use ports 5190 (API) and 5191 (web).
- `npm run build` (in `frontend/`) must pass after every task.
- Any new or changed header/Overview design is shown to the user as an artifact (light and dark, real look) and built only after they pick.

## Review Focus

1. No `data-theme` attribute, or localStorage blocked: app renders dark and fully readable (Task 1, Task 3).
2. Opacity-modified token utilities (`bg-down-fill/10`, `border-accent-fill/50`) render as translucent tints in both themes (Task 2).
3. Chart tooltips, axes, gridlines and reference dots are readable in light on every chart page: Overview, Earnings, P&L, Borrow, Pools (Task 3).
4. Header at 320px width and with a very long wallet label: no horizontal page scroll, every control reachable (Task 5).
5. Overview with zero positions, while loading, and with sync errors: the top strip shows a sensible state, never blank or jumping (Task 6).

## Verification setup (used by every task)

Run once, in the scratchpad directory, never touching the user's database:

- Copy `backend/src/LpDashboard.Api/lpdashboard.db` to `<scratch>/data/test.db`.
- `dotnet build backend/src/LpDashboard.Api -o <scratch>/api-out`, then `dotnet <scratch>/api-out/LpDashboard.Api.dll --urls http://localhost:5190 --ConnectionStrings:Default="Data Source=<scratch>/data/test.db" --Auth:ResetPassword=<fresh unique value>` (this reopens the setup page for the copy only).
- In `frontend/`: `VITE_API_PROXY=http://localhost:5190 npx vite --port 5191 --strictPort`.
- Open `http://localhost:5191` in the browser pane, set a throwaway test password, and (if the copy has no wallets) add the test wallet from the project memory note on the Addresses page.
- Pages to check: `/`, `/fees`, `/borrow`, `/positions`, `/pools`, `/pnl`, `/addresses`, plus the position drawer. Check each in dark and light (toggle in header) at 1440px wide; header and Overview work also at 375px.
- Before Task 2 starts, take a screenshot of every page above in both themes; these are the "before" for comparison.

## File Structure

- Create `frontend/src/tokens.css`: all colour variables and the `@theme inline` mapping. One responsibility: the palette.
- Create `frontend/scripts/check-contrast.mjs`: reads `tokens.css`, fails on contrast violations.
- Modify `frontend/src/index.css`: imports `tokens.css`; loses the slate remap and the colour variable blocks.
- Modify nearly every file under `frontend/src/components`, `pages`, `lib` (class renames only in Task 2).
- Task 5 and 6 may create small components (e.g. `frontend/src/components/header/*`, `frontend/src/components/overview/AttentionStrip.tsx`) once a mockup is picked.

---

### Task 1: Token file and contrast script

**Files:**
- Create: `frontend/src/tokens.css`, `frontend/scripts/check-contrast.mjs`
- Modify: `frontend/src/index.css` (add `@import "./tokens.css";` after the tailwind import; leave everything else for now)

**Interfaces:**
- Produces, as Tailwind colour utilities (each `bg-`, `text-`, `border-`, `divide-`, `ring-` capable, opacity modifiers allowed): `surface`, `panel`, `raised`, `line`, `line-strong`, `control`, `fg`, `fg-2`, `muted`, `faint`, `accent`, `accent-fill`, `accent-solid`, `up`, `up-fill`, `down`, `down-fill`, `warn`, `warn-fill`.
- Meaning: `surface` page background; `panel` cards; `raised` hover and bar tracks; `line` hairlines and card borders; `line-strong` decorative stronger borders; `control` borders of input/select/textarea (3:1 on `panel`); `fg` strongest text; `fg-2` body; `muted` labels; `faint` hints (still AA); `<hue>` text-grade colour; `<hue>-fill` bars, dots, badges; `accent-solid` background of solid buttons with white text (white on it ≥4.5:1).
- Starting values, dark (today's): surface `#020617`, panel `#0f172a`, raised `#1e293b`, line `#1e293b`, line-strong `#334155`, control `#64748b`, fg `#f8fafc`, fg-2 `#cbd5e1`, muted `#94a3b8`, faint `#64748b`, accent `#38bdf8`, accent-fill `#0ea5e9`, accent-solid `#0369a1`, up `#34d399`, up-fill `#10b981`, down `#fb7185`, down-fill `#f43f5e`, warn `#fbbf24`, warn-fill `#f59e0b`.
- Starting values, light (today's remap results): surface `#f4f6f8`, panel `#ffffff`, raised `#e2e7ec`, line `#e2e7ec`, line-strong `#cfd6de`, control `#8d98a6`, fg `#0f1720`, fg-2 `#465363`, muted `#5b6675`, faint `#6b7686`, accent `#2a78d6`, accent-fill `#2a78d6`, accent-solid `#1d5fb0`, up `#0f7d57`, up-fill `#1baf7a`, down `#c23b36`, down-fill `#c23b36`, warn `#b36b00`, warn-fill `#d98a00`.
- Also move the existing `--chart-*`, `--wallet-*`, `--marker`, `--band-*` variable values into `tokens.css` unchanged (both themes), and add `color-scheme` per theme there.

- [ ] **Step 1: Write `check-contrast.mjs`.** It parses the two blocks of `tokens.css` with a regex on `--name: #hex;` lines, computes WCAG relative-luminance contrast, and exits non-zero printing every failing pair as `theme  fg-token on bg-token  ratio  needs`. Rules, per theme: text tokens (`fg`, `fg-2`, `muted`, `faint`, `accent`, `up`, `down`, `warn`) ≥ 4.5 on `surface` and on `panel`; `fg`, `fg-2`, `muted` also ≥ 4.5 on `raised`; `control` ≥ 3 on `panel`; each `*-fill` ≥ 3 on `panel`; `#ffffff` on `accent-solid` ≥ 4.5. Run with `node scripts/check-contrast.mjs` from `frontend/`.
- [ ] **Step 2: Create `tokens.css`** with the starting values above: `:root { --surface: …; }` for dark, `:root[data-theme="light"] { … }` for light, and `@theme inline { --color-surface: var(--surface); … }` for every token.
- [ ] **Step 3: Run `node scripts/check-contrast.mjs`.** Expected: FAIL, listing at least `faint` (about 3.7:1 on dark `panel`) and some light pairs.
- [ ] **Step 4: Adjust values until the script exits 0.** Change only failing tokens, lightening in dark and darkening in light by the smallest step that passes; keep `muted` and `faint` visibly different (faint must stay lighter than muted in light, darker in dark, by at least 0.6 contrast-ratio points of separation from `muted`).
- [ ] **Step 5: Verify:** `npm run build` passes; `node scripts/check-contrast.mjs` prints "all pairs pass" and exits 0.
- [ ] **Step 6: Commit** `feat(frontend): add semantic colour tokens and contrast check`.

---

### Task 2: Migrate colour classes to tokens

**Files:**
- Modify: every file under `frontend/src` (and `frontend/index.html` body class) that uses raw palette classes.
- Script (not committed): a one-off Node script in the scratchpad that rewrites class names.

**Interfaces:**
- Consumes: Task 1 tokens.
- Produces: no raw palette utilities (except `white`/`black`) left in `src`.

Mapping the script applies. The utility prefix is any of `bg|text|border|divide|ring|outline|fill|stroke|from|to|placeholder:text`, with any variant prefixes (`hover:`, `md:`, …) and any `/opacity` suffix preserved.

- slate, backgrounds: `950→surface`, `900→panel`, `800→raised`, `700→line-strong`, `600|500|400→muted`, `50→fg`.
- slate, borders/divide/ring/outline: `800→line`, `700|600|500|400→line-strong`, `900→panel`, `50→fg`.
- slate, text: `50|100→fg`, `200|300→fg-2`, `400→muted`, `500|600→faint`, `950→surface`.
- sky/emerald/rose/amber/orange text (`200|300|400`): sky→`accent`, emerald→`up`, rose→`down`, amber and orange→`warn`.
- those hues as `bg|border|ring|fill|stroke|divide` (`300|400|500`): `*-fill` of the same mapping (`sky→accent-fill`, `emerald→up-fill`, `rose→down-fill`, `amber|orange→warn-fill`).
- `bg-sky-600→bg-accent-solid`; `bg-rose-600→bg-down-fill`.
- Anything else (e.g. `sky-700|900|950`, `text-sky-950`, other shades) is a leftover: the script prints `file:line class`, and you fix each by hand to what it looks like today in dark and in light (usually `bg-accent-fill/15`-style tints).
- After the script, `<input>`, `<select>`, `<textarea>` use `border-control` instead of the mapped `border-line-strong`.
- Leave brand hex values in `PriceTicker.tsx` (`badgeColor`) alone: they are asset brand colours. Replace the `#fb7185` hex in `EarningsChart.tsx` (lines near 183 and 193) with `var(--color-down-fill)`. `body` class in `index.html` becomes `bg-surface text-fg`.

- [ ] **Step 1: Write and run the migration script**; commit nothing from it. Print the leftover list.
- [ ] **Step 2: Fix each leftover by hand**; apply `border-control` to form controls (find them with `grep -rn "<input\|<select\|<textarea" src`).
- [ ] **Step 3: Verify no raw palette classes remain:** `grep -rnE "\b(bg|text|border|divide|ring|outline|fill|stroke|from|to)-(slate|sky|emerald|rose|amber|orange|red|green|blue|violet|indigo|teal|cyan|pink|lime|yellow|purple)-[0-9]" frontend/src` prints nothing.
- [ ] **Step 4: Verify:** `npm run build` passes. In the browser, pick one element using an opacity token (e.g. Overview "Needs attention" tag, `bg-*-fill/10`) and read its computed `background-color` in both themes: it must be a semi-transparent colour (alpha < 1), not opaque and not transparent.
- [ ] **Step 5: Compare** every page, both themes, against the "before" screenshots: dark should look the same except slightly lighter hint text and lighter input borders; light may differ where colours were previously unmapped. Note any surprise.
- [ ] **Step 6: Commit** `refactor(frontend): use semantic colour tokens everywhere`.

---

### Task 3: Finish the light theme and remove the remap

**Files:**
- Modify: `frontend/src/index.css` (delete the `--color-slate-*` / accent override block and the duplicated colour variable blocks now in `tokens.css`), `frontend/src/lib/chartTheme.ts` only if variable names change, any component with a colour that still looks wrong.

**Interfaces:**
- Consumes: Tasks 1 and 2.
- Produces: `index.css` contains only the Tailwind import, `tokens.css` import, font `@theme`, base heading/body/`.num`/`:focus-visible` rules. `:focus-visible` outline uses `var(--color-accent)`.

- [ ] **Step 1: Delete the remap block and duplicated variable blocks from `index.css`.** `npm run build` must still pass.
- [ ] **Step 2: Walk every page in light** (all pages from the setup list, drawer open, a chart tooltip hovered on each chart page, a Borrow risk panel, the Pools range panel). Fix any colour that is unreadable, invisible, or inconsistent with dark; fix by token, not by new raw colour. Verify chart gridlines, axes, tooltips and reference dots are readable on Overview, Earnings, P&L, Borrow and Pools.
- [ ] **Step 3: Walk every page in dark** and compare to the "before" screenshots.
- [ ] **Step 4: Missing-theme check.** In devtools remove `data-theme` from `<html>` and clear `localStorage` key `lpdash.theme`: page renders dark and fully readable (tokens default to dark).
- [ ] **Step 5: Verify:** `npm run build` and `node scripts/check-contrast.mjs` both pass; `grep -n "color-slate\|color-sky\|color-emerald\|color-rose\|color-amber" frontend/src/index.css` prints nothing.
- [ ] **Step 6: Commit** `refactor(frontend): drop slate remap; light theme runs on tokens`.

---

### Task 4: Keyboard access and labels

**Files:**
- Modify: `frontend/src/components/ui.tsx` (`BarRow`), `frontend/src/pages/PositionsPage.tsx` (row at ~366; inputs/select at ~158–180), `frontend/src/components/earnings/ClaimLog.tsx` (row at ~53), `frontend/src/components/Layout.tsx` (`OwnerSelect`), `frontend/src/components/PositionDrawer.tsx`, any other unlabelled control found by the sweep.

**Interfaces:**
- Consumes: Task 3 (`:focus-visible` ring on tokens).
- Produces: `BarRow` keeps its props; when `onClick` is set it renders interactive markup (a `<button type="button">` row, or `role="button"` + `tabIndex={0}` + Enter/Space handler) and shows the focus ring.

- [ ] **Step 1: Sweep.** `grep -rnE "<(div|tr|li|span|td|p)\b[^>]*onClick" frontend/src` (multiline) and list each hit; `PoolTable.tsx` already has `tabIndex`, `aria-expanded` and a handler, check it handles Enter/Space too. The backdrop in `PositionDrawer.tsx` is mouse-only by design if Escape closes the drawer: confirm Escape works.
- [ ] **Step 2: Make `BarRow` operable by keyboard** as described under Interfaces.
- [ ] **Step 3: Make the Positions table row and ClaimLog row operable by keyboard:** focusable, Enter and Space activate, `aria-expanded` on ClaimLog rows, visible focus ring.
- [ ] **Step 4: Label controls:** `OwnerSelect` gets `aria-label="Wallet"`; label every `<input>` and `<select>` on Positions, Pools filter bar, Addresses and the Borrow manual-loan form via `aria-label` or `<label>`; icon-only or symbol buttons ("×") already have `aria-label` or get one.
- [ ] **Step 5: Keyboard walk in the browser** (Tab, Shift+Tab, Enter, Space, Escape): reach the wallet select, an Overview allocation row, a Positions row (Enter opens the drawer, Escape closes it, focus returns to the row), a ClaimLog row (Enter expands). Every stop shows a visible ring in both themes.
- [ ] **Step 6: Verify** `npm run build` passes. **Commit** `fix(frontend): keyboard access and control labels`.

---

### Task 5: Header

**Files:**
- Modify: `frontend/src/components/Layout.tsx`; may create `frontend/src/components/header/` pieces; may create a mock file in the scratchpad for the artifact.

**Interfaces:**
- Consumes: tokens, `useTheme`/`setTheme`, `useHideBalances`/`setBalancesHidden`, `useAuth().logout`, `SyncIndicator` behaviour (details popover, errors, retry) which must be preserved.
- Produces: a header where nav and wallet select dominate; theme and hide-balances are compact icon buttons with `aria-label`, tooltip and `aria-pressed` where toggles; sync is a compact status chip opening the existing details popover; log out is reachable but not in the main row. The sticky header (including the ticker row) is no taller than today at 1440px.

- [ ] **Step 1: Mock 2 or 3 header options as one artifact** in the app's real look (tokens from `tokens.css`, IBM Plex, light and dark side by side, desktop and 375px widths). Each option states where log out goes and how the ticker row is treated. Use the Artifact tool, following the project memory note on UI proposals.
- [ ] **Step 2: Stop and ask the user to pick** an option (or a mix). Do not build before they answer.
- [ ] **Step 3: Implement the chosen design** under the Interfaces constraints; reuse existing handlers and `SyncProgressDetails`; no new dependencies.
- [ ] **Step 4: Verify in the browser:** both themes; 1440px, 768px, 375px and 320px (no horizontal page scroll at any width); a wallet with a 60-character label does not break the header (truncate with ellipsis, full text in `title`); sync running, sync errors and idle states all show; keyboard walk reaches every control with a visible ring; the sticky header height at 1440px is ≤ today's (measure with devtools before and after).
- [ ] **Step 5: Verify** `npm run build` passes. **Commit** `feat(frontend): calmer header`.

---

### Task 6: Overview top strip

**Files:**
- Modify: `frontend/src/pages/OverviewPage.tsx`; may create `frontend/src/components/overview/AttentionStrip.tsx`; reuses `buildAlerts` (`src/lib/attention.ts`), `ReadyToClaimPanel` and `UnclaimedCard` (`src/components/Unclaimed.tsx`).

**Interfaces:**
- Consumes: tokens; existing `AttentionPanel`, `ReadyToClaimPanel`, `StatCard` content (stat card contents do not change).
- Produces: an Overview that opens with a block answering "does anything need me?", built from existing alert and ready-to-claim data; no section removed unless the user says so.

- [ ] **Step 1: Mock 2 or 3 Overview layouts as one artifact**, light and dark, with realistic data (an out-of-range position, a near-edge one, an unclaimed amount) and the empty state ("nothing needs attention"), desktop and 375px. Include where the chart sits.
- [ ] **Step 2: Stop and ask the user to pick.** Do not build before they answer.
- [ ] **Step 3: Implement the chosen layout** under the Interfaces constraints. Loading state uses `Skeleton` with the same height as the loaded strip's minimum so the page does not jump.
- [ ] **Step 4: Verify in the browser:** both themes; with positions needing attention; with none (strip shows an explicit all-clear state); while loading (throttle or reload); with sync errors present; a wallet with zero open positions (empty state links to Addresses); 375px width has no horizontal page scroll.
- [ ] **Step 5: Verify** `npm run build` passes. **Commit** `feat(frontend): Overview opens with what needs attention`.

---

## Wrap-up

- [ ] Final pass: `npm run build`, `node scripts/check-contrast.mjs`, the no-raw-palette grep, a both-theme walk of all pages. Stop the API and web servers started on 5190/5191 and delete the scratchpad DB copy. Report anything not checked.
