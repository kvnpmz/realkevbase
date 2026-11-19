---
name: storefront-ship-and-deploy
description: Use when adding a guide to SaveSimple, changing shop copy, deploying the API or SPA, or verifying what realkevbase.com actually serves after a deploy.
---

# Ship and verify a storefront change

Three change classes share one loop: **catalog** (a rendered guide), **copy** (`listing.txt`),
**web** (SPA, prerendered HTML, sitemap, JSON-LD). Each ends with a deploy and a check of the
live response — the deploy log is not evidence.

## 1. When to use / when not to use

- Use: a guide has been rendered in `zz_tp/output/` and needs to go on sale.
- Use: shop copy, a card colour or a category changes.
- Use: SPA, prerendered page, sitemap or structured-data changes.
- Use: you must prove what production serves right now.
- Not: pin queue or Pinterest work — `docs/pinterest.md` and the pin scripts.
- Not: drawing or rendering the PDF — that is `zz_tp/` and its own manual; that tree is an
  independent project, so add files there if a task needs them and never overwrite what is there.
- Not: page layout, type and fit — `zz_tp/docs/layout.md`.

## 2. Prerequisites

- `zz_tp/` beside the repo (gitignored, ~1 GB) holding the listing's `output/` and `documents/`
  folders. Its database is not the one the API ships.
- `dotnet` SDK, `node`/`npm`, `python3`.
- Credentials, by name only: Cloudflare for `scripts/deploy.sh`; storage for the
  `scripts/sync-catalog.py` uploads.
- `listing.txt`, when the listing has one, in TITLE / DESCRIPTION / TAGS order.
- A tree you can explain: run `git status --short` first and know what is uncommitted.

## 3. Steps

1. Classify the change: catalog, copy, or web. A new guide is catalog **and** copy.
2. Confirm the inputs: `ls zz_tp/output/<listing>/` and `zz_tp/documents/<listing>/listing.txt`.
   For a copy change, edit that `listing.txt` in place and keep the TITLE / DESCRIPTION / TAGS
   headings: the reader is heading-based, the order is for the humans who edit it next.
3. Preview the catalog: `python3 scripts/sync-catalog.py`.
4. Read the preview. The `+` lines must be the listings you expect; if `names to refresh` is not
   0, stop and fix the source of the drift before writing anything.
5. Apply: `python3 scripts/sync-catalog.py --apply` — registers rows, uploads PDFs, publishes
   covers and renders that listing's gallery clip.
6. If image publishing failed, re-run `python3 scripts/sync-catalog.py --apply --images`; the rows
   are already written at that point, so this is safe and is not a second registration.
7. Build: `dotnet build src/RealKevbase.Api/RealKevbase.Api.csproj` — expect 0 warnings.
8. Deploy: `./scripts/deploy.sh --yes` (api + web + verification). Use `--skip-catalog` only when
   the change is web-only and you have deliberately left the catalog alone; for a web-only build
   without deploying the API, run `cd src/realkevbase-web && npm run build` first.
9. New or renamed pages: `python3 scripts/ping_indexnow.py --apply`.
10. Verify against production — section 4, not the deploy output.
11. Commit source only, one commit per class of change: `InMemoryProductCatalog.cs` +
    `src/RealKevbase.Api/Data/projects.db` for catalog work, `src/realkevbase-web/**` for web.
    Nothing under `zz_tp/`, ever.

## 4. Verification

- `./scripts/deploy.sh --yes` ends with `All checks passed.` Intent: a single failed check still
  deploys, so read the narrow lines: `catalog shape (N products, M PDFs …)` must move by exactly
  what you registered, and `sitemap lists N URLs` by exactly the number of pages added.
- Fetch the page yourself: `curl -s -o /tmp/p.html -w "%{http_code}\n" https://realkevbase.com/guides/<slug>`
  then check `<title>` and the `Product` JSON-LD. Intent: **Pages answers an unknown path with the
  SPA shell and HTTP 200**, so the status code proves nothing; the title does.
- Slug is the listing id with underscores turned into hyphens — `halfhour-daily-planner`, never
  `half-hour-…`. Intent: a wrong slug returns 200 with the shell title, which reads like success.
- `python3 scripts/sync-catalog.py` → `catalog is up to date.` Intent: proves the committed catalog
  matches `zz_tp`, and therefore that the next deploy will not be refused.
- Bundle stays one chunk: ~531 kB raw, ~167 kB gzip. Intent: Vite's >500 kB warning is expected;
  a second chunk is not.
- `ping_indexnow.py --apply` → `pushed N URLs` where N equals the sitemap count, plus `HTTP 200`
  or `202`. Intent: a rejected push looks identical to a successful one except for the code.

## 5. Pitfalls

- Don't `--skip-catalog` to get past the guard when the catalog really is behind: the new guide
  pages ship as the SPA shell and the store lists nothing (silent, high).
- Never copy `zz_tp/projects.db` over `src/RealKevbase.Api/Data/projects.db`: it holds titles for
  only some rows and blanks the rest (silent corruption, high).
- Don't fabricate structured data to clear a Search Console note — `aggregateRating`, `review`,
  `shippingDetails` on a download. It buys a manual action later (silent, delayed, high).
- Don't `git add` inside `zz_tp/`: ~1 GB of renders and a gitignored tree (repo bloat, loud).
- Don't rename a listing folder to fix copy: the slug, blob path and id all derive from it, so
  existing links break (silent for search engines, loud for customers).
- Don't add a route-level MUI import to the SPA: it splits the bundle the perf budget assumes is
  one chunk (silent regression).
- Don't trust `edit` on `src/RealKevbase.Api/Program.cs`: it is CRLF, so multi-line matches fail
  (loud, easy to misread as the tool being broken).
- A check reporting `got 000` is a runner network blip, not a site fault — re-run the deploy
  before changing code (loud, misleading).
- Unverified: whether `--apply` rewrites the price of an existing entry. Price is written by
  `sync-catalog.py` (`DEFAULT_PRICE`); confirm that path before promising a price change.

## 6. Supporting files

- `AGENTS.md` — the manual: rules, the ship-a-guide flow, the verify list.
- `scripts/sync-catalog.py` — catalog preview/apply; `--json`, `--images`, `--no-images`,
  `--videos`, `--no-videos`, `--no-upload`. `--apply` is the whole add-a-listing flow.
- `scripts/deploy.sh` — build, deploy, and the PASS/FAIL checks; `verify` re-runs them only.
- `scripts/ping_indexnow.py` — sitemap → IndexNow, dry run by default.
- `src/realkevbase-web/scripts/generate-seo.mjs` — prerendered guides, sitemap, robots, JSON-LD.
- `zz_tp/docs/tooling.md`, `zz_tp/docs/listing-metadata.md`, `zz_tp/docs/variants.md` — the
  renderer's own per-job references; that tree is independent, so read it, never rewrite it.
- `zz_tp/documents/<listing>/listing.txt` — the shop copy the catalog reads.
