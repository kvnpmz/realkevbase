# RealKevbase project manual

RealKevbase is the personal brand store; SaveSimple is its printable-PDF collection ($5 with instant download) sold with a few merch items through one Stripe cart. React 19 + Vite + MUI SPA in `src/realkevbase-web` (Cloudflare Pages), .NET 10 API in `src/RealKevbase.Api` + `src/RealKevbase.Core`, PDFs rendered by `zz_tp/` (gitignored, own manual: `zz_tp/AGENTS.md`).

## Rules

- **`zz_tp/` is an independent project, not part of this one.** It has its own manual (`zz_tp/AGENTS.md`), its own scripts and its own database. Add a file inside it when a task genuinely needs one; never overwrite, rewrite or delete anything already there — not a document, not a template, not `projects.db`, not a `.goosehints`. If a change inside `zz_tp/` is needed, stop and ask.
- Products are code (`Core/Services/InMemoryProductCatalog.cs` plus a row in `src/RealKevbase.Api/Data/projects.db`). Change them only with `scripts/sync-catalog.py`.
- The storefront's subject taxonomy is a **declared** shelf, not an inferred one: every document carries `category = "kidney"` in its own `zz_tp/documents/<listing>/config.tl`, next to `id`. A variant is created by copying its base config, so an edition inherits the declaration and a new colourway or translation needs no entry anywhere. `scripts/taxonomy.json` holds only the shelves' slugs and the sales families that roll up into each; `python3 scripts/check-taxonomy.py` asserts that every document declares a shelf the shop has, that every family rolls into exactly one, and that the chip list and the file name the same ten. `sync-catalog.py` reads the declaration and refuses to guess — the old prefix matching needed a new entry for every name shape (`fatty_liver_food_list` beside `fattyliver_foodlist`) and let a prefix `house` claim `warehouse_bulk_list`, while the four Etsy-era categories dumped everything unrecognised into `guides`.
- Shop copy is `zz_tp/documents/<listing>/listing.txt` (first pipe segment = name, `DESCRIPTION` = card copy); edit it, then sync.
- Never copy `zz_tp/projects.db` over the shipped database — it has titles on only 45 of 116 rows and would blank most names.
- No secrets in the repo; the browser sees only `/api/...` (blob names and SAS links stay server-side). Public C# members keep XML docs.
- Ids are derived from the folder name (`guides/<slug>.pdf`, `uuid5(...)`), so renaming a blob moves the guide. Endpoints live in `Api/Endpoints/`, routes in `App.tsx`.
- Gallery and paper sizes are server facts: `/api/products` returns `gallerySlots` and `pageSizes` from one cached blob listing, so the SPA never hard-codes "US Letter + A4" nor requests an unpublished render. A4 is claimed only when `guides/<slug>-a4.pdf` is published, and that edition is then delivered beside the Letter PDF (A4 is legacy: 57 listings, no new ones). Slots 4 and 5 are the shared how-to and disclaimer pages, stored once in `guides/images/shared/` and published by `scripts/publish-shared-images.py`.
- A paid order delivers the PDF plus the full-resolution page PNG that the cover is downscaled from: `guides/images/<slug>.png`, and `<slug>-a4.png` for legacy A4 editions. `scripts/build-thumbnails.py --upload` publishes both, copied byte-for-byte rather than re-encoded. The image is optional — a listing without one ships its PDF alone, and a missing image never fails an order.
- Gallery clips are public marketing media, not deliverables: `scripts/build-gallery-video.py` renders a 5-second silent ping-pong from the page PNG, and `--upload` publishes it to the `media` container as `guides/video/<slug>.mp4` (underscores to hyphens, matching the PDF). The container stays private — the account refuses anonymous blob reads — so `/api/products` returns a `videoUrl` signed with a cached one-year container SAS, and only when that blob is listed; a listing without a clip keeps its still page. Clips are cached five minutes, not a day, so a re-render goes live quickly. `scripts/sync-catalog.py --apply` renders and uploads a clip for every listing it registers, so adding a listing is one command; `--videos` re-renders all of them and `--no-videos` skips the step.
- Search metadata is generated, never hand-written: `npm run build` runs `scripts/generate-seo.mjs` after Vite, which writes `guides/<slug>.html`, `sitemap.xml`, `robots.txt` and the www→apex `_middleware.js` from the live catalog. Guide heads are the `<!-- seo:start -->` block in `index.html`; a second `<title>` or a hand-edited sitemap will be overwritten.
- Nine collection pages sit above the guides: `src/data/hubs.ts` defines them (slug, copy, and a listing-id prefix list), and `generate-seo.mjs` writes one static HTML page each — `dist/<slug>.html`, served at `/<slug>` — beside `/all-guides`. A guide joins the hub its prefix matches first, so a new colourway or translation is collected with no second list to keep in step. They exist for the internal links, not the bundle: every guide page names its own hub in the static footer and the footer names all nine, which is the only inbound link most guide pages have beyond `/all-guides`. The hubs are plain HTML with no React route on purpose; adding one would grow the single chunk for no crawl benefit.
- Colour and typeface editions are separate products, and their `listing.txt` is what keeps them separate: 18 base documents ship as 57 extra pages (`-pink`, `-plum`, `-green`, `-serif`, `-blue`), each with its own 30-word title, one-sentence description and 13 tags written around a different angle on the same sheet. Every guide self-canonicalises and every guide is in the sitemap. Never copy a base `listing.txt` into an edition folder: two pages with identical descriptions are what Google files under "Crawled – currently not indexed", and the shop then sells the same page five times. `scripts/listing-check.sh documents/<slug>` gates it (30-word title, one sentence, 13 tags). The Spanish `-es` editions are translations with their own copy, so they are paired with the English guide through `hreflang` (plus `lang="es"` on the page, since the shell is English). `robots.txt` also disallows `/api/`, whose JSON and 404ing artwork routes were being crawled as pages.
- The served HTML carries the pages, not just their heads: `/` ships the hero from `src/data/home.ts` (the same module `HomePage` renders) and each guide ships a static product block, so a crawler that never runs JavaScript still finds an `<h1>`; `src/main.tsx` deletes both seeds before the first render, so a reader never sees two. The seed copies the theme's metrics — MUI's `h5` is a flat `1.5rem`, an overline sits on a 2.66 line box, a container is 1200px *including* its padding — so a theme change means re-rendering the seed to match, or the page jumps when React takes over. A guide's cover preload, seeded `<img>`, inline record and gallery entry must all name the same **root-relative** `/art/<guid>.webp`: `scripts/generate-seo.mjs` copies each cover out of the API into `dist/art/` while it prerenders, because the API is a single B1 worker that spends over a second before the first byte of a 16 KB image, and a static file is the one thing Cloudflare serves from a shared tier to a datacentre that has never seen it. Proxying that read instead put the first visitor to a new location behind Azure, which is exactly what PageSpeed Insights measured: 3.6s LCP against 1.8s from a warm location. Everything else that shows artwork still goes through `/api/...` (`functions/api/[[path]].js`, edge-cached), and merch has no static copy at all, so it keeps that route. If the seed and the app ever disagree about a cover the browser downloads it twice, and the second copy decides Largest Contentful Paint.

## Ship a guide

A guide is live only when its `zz_tp/output/<listing>/` folder, a database row and a catalog entry all exist. The sync creates the last two, uploads before committing, publishes renders, any legacy A4 edition and that listing's gallery clip, and is safe to re-run.
```bash
python3 scripts/sync-catalog.py            # preview
python3 scripts/sync-catalog.py --apply    # register, upload, publish images and clips
dotnet build src/RealKevbase.Api/RealKevbase.Api.csproj
./scripts/deploy.sh --yes                  # refuses while the sync reports drift
```

Category and card colour are the only editorial values left (`scripts/catalog_meta.json`); nothing is deleted, and `--json`, `--no-upload`, `--no-images`, `--images`, `--no-videos`, `--videos` adjust an apply. If image publishing fails, rows and catalog are already written — re-run `--apply --images`. A clip failure only warns: a listing without one shows its still cover, so the sync still completes.

## Verify

```bash
dotnet build src/RealKevbase.Api/RealKevbase.Api.csproj   # 0 warnings
cd src/realkevbase-web && npm run build                   # one chunk, ~530 KB
python3 scripts/sync-catalog.py                           # "catalog is up to date"
./scripts/deploy.sh verify                                # production smoke test
```

## Getting indexed

Bing-family engines (Bing, DuckDuckGo, Copilot, ChatGPT search, Yandex) are pushed rather than crawled: `python3 scripts/ping_indexnow.py --apply` posts every sitemap URL to IndexNow (dry run by default). Ownership is proven by `src/realkevbase-web/public/<key>.txt`, whose name is the key, so the key must be deployed before the first push and the script verifies it before sending. Re-run it after any deploy that adds guides. Google ignores IndexNow — it only has Search Console.

Social bios use short links handled by `src/realkevbase-web/functions/_middleware.js`: `realkevbase.com/ig` lands on the home page and `/ig/<slug>` on that guide, both carrying `utm_source=<platform>&utm_medium=social&utm_campaign=bio`. `tt`, `pin` and `yt` are mapped the same way. They are 302s on purpose so a cached 301 cannot strand anyone.

## Pinterest

Pins are generated from the catalog with `python3 scripts/pinterest_pins.py` (dry run by default; `--apply --limit N` creates). The token lives outside the repo — `scripts/pinterest_auth.py --show` — and `docs/pinterest.md` covers setup, de-duplication and the Trial-visibility caveat. Never commit app credentials or the token.

The card artwork behind those pins comes from `scripts/pin_cards.py`, which renders three 1000×1500 cards per listing into `zz_tp/output/<listing>/pins/`. Every card carries the SaveSimple logo bottom-left, level with the price pill: the asset is the company logo on a cream field, so the script keys its luma to transparency (cream out, shadow thresholded, one flat green mark) rather than compositing a pale rectangle onto a pastel. `--no-wordmark` renders bare cards, `--wordmark PATH` swaps the file, and each card's SHA-256 in `pins/manifest.json` is what stops a byte-identical repost counting as new.

## Traps

- Catalog `max-age=60` + gzip (~53 KB → ~14 KB); images `max-age=86400` + ETag; a missing image 404s from one blob download, not an `ExistsAsync` probe. CORS is an allow-list in `Cors/CorsPolicies.cs`; no CSP yet (MUI injects runtime styles).
- `zz_tp/` is gitignored and ~1 GB: never `git add` inside it, never run `tpr` (use `zz_tp/scripts/render-check.sh`).
- The SPA is one chunk on purpose; a second route-level MUI dependency doubles it.
- `Program.cs` is CRLF, so `edit` may fail on multi-line matches; prices and delivery copy are derived (`formatMoney`, `DELIVERY_COPY`).
