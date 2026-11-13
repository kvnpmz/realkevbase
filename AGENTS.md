# RealKevbase project manual

The SaveSimple storefront: printable PDF guides ($5, instant download) and a few merch
items sold through one Stripe cart. A static SPA talks to a small JSON API; the guides
themselves are rendered by `zz_tp/`, the vendored PDF generator, which has its own
manual at `zz_tp/AGENTS.md`.

## Where things live

| Path | Role |
| --- | --- |
| `src/realkevbase-web/` | React 19 + Vite + MUI SPA, deployed to Cloudflare Pages |
| `src/RealKevbase.Api/` | .NET 10 minimal API: catalog, images, checkout, webhook |
| `src/RealKevbase.Core/` | Models and services shared by the API (catalog seed, Stripe, blob storage) |
| `src/RealKevbase.Api/Data/projects.db` | Shipped listing database, read-only, carries titles |
| `zz_tp/` | PDF generator (gitignored); `output/<listing>/` holds rendered PDFs and images |
| `scripts/` | Deploy, catalog sync, thumbnails, storage auth — see `scripts/README.md` |

## How the shop works

1. The SPA loads `GET /api/products` once (`useCatalog`, wired in `App.jsx`) and shares
   the catalog with every route. Cards link to `/guides/:slug`.
2. `POST /api/checkout/sessions` prices the cart server-side from the catalog — the
   browser never sends prices.
3. `POST /api/checkout/webhook` verifies the Stripe signature, returns immediately and
   queues fulfillment. The worker generates 24-hour SAS links and emails them.
4. `/checkout/success` polls `GET /api/checkout/sessions/{id}/downloads` for the links.
5. Images are proxied: `GET /api/products/{id}/thumbnail` and
   `/api/products/{id}/images/{slot}` (slots 0–3: cover, zoom, iPad, A4).

## Hard rules

- **Products are code.** They live in `Core/Services/InMemoryProductCatalog.cs`; the
  database row and the catalog entry must both exist for a guide to appear. Use
  `scripts/sync-catalog.py`, never hand-edit one side.
- **Shop copy is `zz_tp/documents/<listing>/listing.txt`.** Names and descriptions on
  the site come from it; edit the file and re-run the sync, not the C#.
- **Product identity is derived.** Blob `guides/<slug>.pdf`, where slug is the folder
  name with `_` → `-`; id is `uuid5(NAMESPACE_URL, "realkevbase/listing/<slug>")`;
  `Product.ListingId` is the blob stem reversed. Changing a blob name changes the URL
  of the guide page and the product.
- **The shipped database is master.** `zz_tp/projects.db` has no `title` column;
  copying it over `src/RealKevbase.Api/Data/projects.db` blanks every product name.
- **No secrets in the repo.** Stripe, blob storage and Azure Email come from
  environment variables or App Service settings; `scripts/storage_auth.py` fetches the
  storage connection string at run time.
- **Blob names never reach the browser.** Everything the SPA shows is served through
  `/api/...`; keep it that way.
- **Public members get XML documentation**, and changes stay inside the task's scope.

## Common tasks

**Add or refresh a guide**

```bash
python3 scripts/sync-catalog.py            # preview the diff
python3 scripts/sync-catalog.py --apply    # row, upload, catalog entry, images
dotnet build src/RealKevbase.Api/RealKevbase.Api.csproj
./scripts/deploy.sh --yes
```

`deploy.sh` runs the same check and refuses to deploy while the catalog is out of date.

**Change a name or description** — edit `listing.txt`, then `sync-catalog.py --apply`.
Category and card colour are the only editorial values the sync cannot derive: set
them in `scripts/catalog_meta.json`.

**Add an API endpoint** — put it in `src/RealKevbase.Api/Endpoints/` on the existing
route group, return `TypedResults`, and set `Cache-Control` when the response is
cacheable.

**Add a SPA route** — add a `<Route>` in `App.jsx`; pages read the catalog they are
given rather than fetching again. Deep links work because `public/_redirects` falls
back to `index.html`.

## Verify

There is no test suite. What counts as proof:

```bash
dotnet build src/RealKevbase.Api/RealKevbase.Api.csproj   # 0 warnings
cd src/realkevbase-web && npm run build                   # single chunk, ~530 KB
python3 scripts/sync-catalog.py                           # "catalog is up to date"
./scripts/deploy.sh verify                                # against production
```

For API behaviour locally, start it with placeholder credentials
(`Stripe__SecretKey=sk_test_dummy`, a fake `AzureStorage__ConnectionString`) and
`--no-launch-profile`, otherwise `launchSettings.json` overrides the port.

## Performance and security invariants

- Catalog: `Cache-Control: public, max-age=60` plus response compression (~53 KB,
  ~14 KB gzipped). Images: `max-age=86400` with ETag revalidation.
- Missing images and unknown products answer `404` from a single blob download
  (`RequestFailedException` on 404), not an `ExistsAsync` probe first.
- CORS is an allow-list in `Cors/CorsPolicies.cs`; add a new origin there, not with
  `AllowAnyOrigin`.
- Security headers on both halves: `nosniff`, `DENY` framing, no-referrer, HSTS. The
  static bundle also sets `Permissions-Policy`; no CSP yet (MUI injects runtime styles).
- Response bodies never include storage paths, connection strings or SAS URLs except
  the customer's own 24-hour download links.

## Traps

- `zz_tp/` is gitignored and ~1 GB; never `git add` anything inside it, and never run
  `tpr` (use `zz_tp/scripts/render-check.sh` for layouts).
- The SPA ships as one chunk on purpose: adding a second route-level dependency that
  duplicates MUI will double the bundle.
- `Program.cs` uses CRLF endings; `edit` may fail on multi-line matches there.
- Currency, prices and delivery promises are derived (`formatMoney`, `DELIVERY_COPY`);
  do not hard-code "$5" in components.
- `scripts/repo-intel.lua overview|symbol|deps|update` indexes C#, JS/JSX and Python;
  use it before reading whole files.
