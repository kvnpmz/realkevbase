# SPA: JavaScript → TypeScript

The storefront in `src/realkevbase-web/src` is migrating to TypeScript. This file is the
running record: what the rules are, what is done, and what is left. Delete it when the
migration lands.

## Rules

- `strict: true` applies to every file that is TypeScript; `allowJs: true` and
  `checkJs: false` mean un-migrated `.js` files are simply not checked yet. No big-bang.
- No `any`, no `as` (except `as const`), no `!`, no `@ts-ignore`, no `@ts-expect-error`.
  Data of unknown shape is `unknown` until a validator narrows it.
- Diffs stay behaviour-identical: rename, annotate, correct a wrong annotation. No
  restructuring, no formatting churn, no `sx` edits.
- `npm run build` must pass at every commit; `deploy.sh verify` at the milestones marked
  below. `vite build` strips types without checking them, so a type error cannot reach
  production — only a behaviour change can.

## Order

| # | Step | Verify |
|---|------|--------|
| 0 | Toolchain: `typescript@7`, `tsconfig.json`, `npm run typecheck` | `tsc --noEmit` = 0 |
| 1 | `src/data/*` → `.ts` with `as const`; `scripts/generate-seo.mjs` imports `.ts` | `npm run build` regenerates every guide/hub page |
| 2 | `src/cart/*`, `src/hooks/*` | `tsc --noEmit`, deploy verify |
| 3 | `src/api/*`, `src/analytics/ga` | `tsc --noEmit` |
| 4 | `src/components/*`, `theme`, `App`, `main` | `tsc --noEmit`, deploy verify |
| 5 | `allowJs: false`, typecheck added to the deploy gate | full smoke list |

## Known friction

- `scripts/generate-seo.mjs` imports the four `src/data/*` modules by explicit `.js`
  path. Node 24 strips types but does not resolve `site.js` → `site.ts`, so those
  specifiers must name `.ts`. Inside `src/`, imports stay extensionless.
- MUI v9 no longer accepts `styleOverrides.containedPrimary` on `MuiButton`.
- `import.meta.env` needs `vite/client` (in `tsconfig`) plus declared `VITE_*` vars.

## Deliberately out of scope

`noUncheckedIndexedAccess` and `exactOptionalPropertyTypes` — worth doing once this lands,
but they would roughly double the diff.

## Smoke list (after step 5)

Cart survives reload · add, remove, quantity · checkout redirect · success page downloads
(PDF and PNG) · a guide page with the A4 edition · one hub page · `/sitemap.xml` unchanged.
