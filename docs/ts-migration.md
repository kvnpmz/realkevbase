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
name `.ts` — Node 24 strips types but will not resolve `site.js` → `site.ts`. Inside `src/`,
imports are extensionless. The script itself still prerenders 187 URLs.

## Known gap

ESLint still covers only `.js`/`.jsx` (its config's `files` list), so `.tsx` is linted by
nothing — including `react-hooks/exhaustive-deps`. `typescript-eslint@8` peers on
`typescript <6.1`, so it cannot be installed beside TypeScript 7 without
`--legacy-peer-deps`, which is a deploy risk this repo should not take. Revisit when
typescript-eslint supports TS 7, or pin TypeScript to 6 and accept older type checking.

## Still worth doing

`noUncheckedIndexedAccess` and `exactOptionalPropertyTypes`. Both were left out to keep this
diff reviewable.

## Smoke list (not yet run by a human)

Cart survives reload · add, remove, quantity · checkout redirect · success page downloads
(PDF and PNG) · a guide page with the A4 edition · one hub page.
