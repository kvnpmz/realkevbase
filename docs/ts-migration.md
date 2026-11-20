# SPA: JavaScript → TypeScript

The storefront in `src/realkevbase-web/src` is TypeScript. Every file is `.ts`/`.tsx`,
`allowJs` is off, and `strict` is on. `npm run typecheck` (`tsc --noEmit`, TypeScript 7) is
the gate; `npm run build` is unchanged.

## Rules the migration held to

- No `any`, no `as` (only `as const` in `data/hubLinks.ts`), no `!`, no `@ts-ignore`, no
  `@ts-expect-error`. Server JSON is `unknown` and narrowed by `api/json.ts`.
- Diffs stayed behaviour-identical. Two runtime details did change, both deliberate and both
  covered below: the MUI override and the missing `#root` guard.

## What the compiler found

The JSDoc was mostly right, and wrong in three places that mattered:

1. `CatalogApiError` and `CheckoutApiError` were documented as carrying only `status` (and
   `cause` in the options bag) while the code set `status`, `code` and `details`. Both
   classes now declare what they actually hold.
2. `CatalogProduct` omitted `videoUrl`, which `normalizeProducts` had always returned — the
   gallery clip was typed out of existence.
3. `useCatalog`'s state was asserted with two casts (`/** @type {CatalogProduct[]} */ ([])`),
   which hid the fact that the empty-array case and the loaded case had the same type.

## Two changes to know about

- **`MuiButton` variant override.** MUI v9 rejects `styleOverrides.containedPrimary`, so the
  black hover on contained primary buttons is now a `variants` entry with
  `props: { variant: 'contained', color: 'primary' }`. Same selector, same colour.
- **`main.tsx`** throws when `#root` is missing instead of passing `null` to `createRoot`.
  `index.html` has always had it; this only turns a blank page into a message.

## Landmine that was already there

`scripts/generate-seo.mjs` imports the four `src/data/*` modules, so those specifiers now
name `.ts` — Node strips types but will not resolve `site.js` → `site.ts`, and it resolves
relative specifiers literally, so it will not resolve `./hubLinks` → `hubLinks.ts` either.
That second half is why `data/hubs.ts` and `data/categories.ts` carry a `.ts` extension on
their `./hubLinks` import while the rest of `src/` stays extensionless; those two files are
the ones the script reaches into, and the comment there says so. The floor is Node **22.18**
(the first release with type stripping on by default): `package.json` declares it in
`engines`, `.nvmrc` repeats it, and `scripts/deploy.sh` refuses an older runtime before it
builds — failing there beats failing after `vite build` has already written `dist/`. The
script itself still prerenders 187 URLs.

## Known gap

ESLint covers `.js`/`.mjs` only — the Cloudflare functions, `eslint.config.js` and
`scripts/generate-seo.mjs` — so `.ts`/`.tsx` is linted by nothing, including
`react-hooks/exhaustive-deps`. `typescript-eslint` 8.73 (the current release) peers on
`typescript <6.1.0`, so it cannot be installed beside TypeScript 7 without
`--legacy-peer-deps`, which is a deploy risk this repo should not take. Revisit when
typescript-eslint supports TS 7, or pin TypeScript to 6 and accept older type checking.

Two things were done instead, both verified against the current tree:

- `tsconfig.json` turns on `noUnusedLocals` and `noUnusedParameters`, which recovers the
  part of `no-unused-vars` that mattered. `npm run typecheck` is a real gate for the SPA.
- `eslint.config.js` now states the gap in the config itself and matches `.mjs`, so the
  build script is covered rather than silently skipped by a `*.{js,jsx}` glob. That change
  surfaced five latent errors in `generate-seo.mjs` (`process` with browser globals, and a
  rethrow that dropped its `cause`), which are fixed.

`scripts/deploy.sh` runs both gates, with a comment saying which covers what, because a
single "lint" step that only reads JavaScript reads like full coverage.

## Still worth doing

`noUncheckedIndexedAccess` and `exactOptionalPropertyTypes`. Both were left out to keep this
diff reviewable.

## Tooling the migration broke, now fixed

`scripts/repo-intel.lua` had `{ .js, .jsx, .mjs, .cjs }` in its language table, so after the
migration the whole SPA disappeared from the index: `overview` no longer listed a web
project and `symbol <Component>` matched nothing, even though the project hints tell an
agent to navigate with it. It now indexes `.ts`/`.tsx` (and `.mts`/`.cts`) through
`tree-sitter-typescript`, resolving `source.ts` or `source.tsx` per file because the two
parsers disagree about what `<T>(x)` means, and extracting the TS-only declarations
(`interface`, `type`, `enum`, `abstract class`). 73 files and 606 symbols, no parse errors.
`main.tsx` is recognised as the entry file.

## Smoke list (not yet run by a human)

Cart survives reload · add, remove, quantity · checkout redirect · success page downloads
(PDF and PNG) · a guide page with the A4 edition · one hub page.
