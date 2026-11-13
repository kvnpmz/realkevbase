# RealKevbase project manual

RealKevbase is the personal brand storefront; SaveSimple is its printable-PDF
collection — $5 guides with instant download — sold alongside a few merch items through
one Stripe cart. A static SPA talks to a small JSON API, and the guides themselves are
rendered by `zz_tp/`, a vendored PDF generator with its own manual (`zz_tp/AGENTS.md`).

| Path | Role |
| --- | --- |
| `src/realkevbase-web/` | React 19 + Vite + MUI SPA → Cloudflare Pages |
| `src/RealKevbase.Api/` | .NET 10 minimal API: catalog, images, checkout, webhook |
| `src/RealKevbase.Core/` | Models and services: catalog seed, Stripe, storage, email |
| `src/RealKevbase.Api/Data/projects.db` | Shipped listing database (read-only, has titles) |
| `zz_tp/` | PDF generator (gitignored): `output/<listing>/` holds PDFs and renders |
| `scripts/` | Deploy, catalog sync, thumbnails, storage auth |

## How the shop works

- `GET /api/products` is fetched once (`useCatalog`, wired in `App.jsx`) and shared;
  cards link to `/guides/:slug`.
- `POST /api/checkout/sessions` prices the cart server-side. The Stripe webhook verifies
  the signature, returns at once and queues fulfillment; the worker mints 24-hour SAS
  links and emails them, and `/checkout/success` polls
  `GET /api/checkout/sessions/{id}/downloads`.
- Images proxy through `/api/products/{id}/thumbnail` and `/images/{slot}` (0–3).

## Rules

- Products are code: `Core/Services/InMemoryProductCatalog.cs` plus a row in the
  shipped database. Add or change one with `scripts/sync-catalog.py`.
- Copy is `zz_tp/documents/<listing>/listing.txt` (first pipe segment = name,
  `DESCRIPTION` = card copy). Edit the file, then sync.
- Identity is derived: `guides/<slug>.pdf` (folder name, `_` → `-`); id is
  `uuid5(NAMESPACE_URL, "realkevbase/listing/<slug>")`. A renamed blob moves the guide.
- Never copy `zz_tp/projects.db` over the shipped database — the generator's copy has
  titles on only 45 of its 116 rows, so it blanks most product names.
- No secrets in the repo (env or App Service), and the browser sees only `/api/...`;
  blob names and SAS links stay server-side.
- Public C# members keep XML docs; endpoints live in `Api/Endpoints/`, routes in `App.jsx`.

## Add or refresh a guide

The folder in `zz_tp/output/`, a row in the shipped database and a catalog entry — all
three, or the guide is invisible. The sync creates the last two for any folder holding a
PDF.

```bash
python3 scripts/sync-catalog.py            # preview: new listings, renames, warnings
python3 scripts/sync-catalog.py --apply    # row, upload, catalog entry, images
dotnet build src/RealKevbase.Api/RealKevbase.Api.csproj
./scripts/deploy.sh --yes
```

`--apply` uploads the PDF before committing the row, publishes the listing's renders,
and is safe to re-run. Flags: `--json`, `--no-upload`, `--no-images`, `--images`. Names
and descriptions come from `listing.txt`; category and card colour are the only
editorial values left — set them in `scripts/catalog_meta.json`. Nothing is deleted: a
vanished folder is reported, not unlisted. `deploy.sh` refuses while the sync reports
drift (`--skip-catalog` to bypass).

When the sync refuses

| Message | Fix |
| --- | --- |
| `two listings would share a product name` | rename one `listing.txt` title, or pin a `name` override in `catalog_meta.json` |
| `upload failed … nothing was written` | storage or `az` login problem; state is untouched, re-run |
| `thumbnail publishing failed` | rows and catalog are written; fix storage, re-run `--apply --images` |
| `catalog is out of date (N changes)` | run `sync-catalog.py --apply` |

## Verify

```bash
dotnet build src/RealKevbase.Api/RealKevbase.Api.csproj   # 0 warnings
cd src/realkevbase-web && npm run build                   # one chunk, ~530 KB
python3 scripts/sync-catalog.py                           # "catalog is up to date"
./scripts/deploy.sh verify                                # production smoke test
```

No test suite. A local API run needs `--no-launch-profile` and placeholder
`Stripe__SecretKey` / `AzureStorage__ConnectionString`, or the port comes from
`launchSettings.json`.

## Traps

- Catalog `max-age=60` + gzip (~53 KB → ~14 KB); images `max-age=86400` + ETag. Missing
  images and unknown products 404 from one blob download, not an `ExistsAsync` probe.
- CORS is an allow-list in `Cors/CorsPolicies.cs`. `nosniff`, `DENY`, no-referrer and
  HSTS on both halves; no CSP yet (MUI injects runtime styles).
- `zz_tp/` is gitignored and ~1 GB: never `git add` inside it, never run `tpr` (use
  `zz_tp/scripts/render-check.sh`).
- The SPA is one chunk on purpose; a second route-level MUI dependency doubles it.
- `Program.cs` is CRLF, so `edit` may fail on multi-line matches.
- Prices and delivery copy are derived (`formatMoney`, `DELIVERY_COPY`) — no hard-coded
  "$5" in components.
