# RealKevbase project manual

RealKevbase is the personal brand store; SaveSimple is its printable-PDF collection ($5 with instant download) sold with a few merch items through one Stripe cart. React 19 + Vite + MUI SPA in `src/realkevbase-web` (Cloudflare Pages), .NET 10 API in `src/RealKevbase.Api` + `src/RealKevbase.Core`, PDFs rendered by `zz_tp/` (gitignored, own manual: `zz_tp/AGENTS.md`).

## Rules

- Products are code (`Core/Services/InMemoryProductCatalog.cs` plus a row in `src/RealKevbase.Api/Data/projects.db`). Change them only with `scripts/sync-catalog.py`.
- Shop copy is `zz_tp/documents/<listing>/listing.txt` (first pipe segment = name, `DESCRIPTION` = card copy); edit it, then sync.
- Never copy `zz_tp/projects.db` over the shipped database — it has titles on only 45 of 116 rows and would blank most names.
- No secrets in the repo; the browser sees only `/api/...` (blob names and SAS links stay server-side). Public C# members keep XML docs.
- Ids are derived from the folder name (`guides/<slug>.pdf`, `uuid5(...)`), so renaming a blob moves the guide. Endpoints live in `Api/Endpoints/`, routes in `App.jsx`.

## Ship a guide

A guide is live only when its `zz_tp/output/<listing>/` folder, a database row and a catalog entry all exist. The sync creates the last two, uploads before committing, publishes renders, and is safe to re-run.
```bash
python3 scripts/sync-catalog.py            # preview
python3 scripts/sync-catalog.py --apply    # register, upload, publish images
dotnet build src/RealKevbase.Api/RealKevbase.Api.csproj
./scripts/deploy.sh --yes                  # refuses while the sync reports drift
```

Category and card colour are the only editorial values left (`scripts/catalog_meta.json`); nothing is deleted, and `--json`, `--no-upload`, `--no-images`, `--images` adjust an apply. If image publishing fails, rows and catalog are already written — re-run `--apply --images`.

## Verify

```bash
dotnet build src/RealKevbase.Api/RealKevbase.Api.csproj   # 0 warnings
cd src/realkevbase-web && npm run build                   # one chunk, ~530 KB
python3 scripts/sync-catalog.py                           # "catalog is up to date"
./scripts/deploy.sh verify                                # production smoke test
```

## Traps

- Catalog `max-age=60` + gzip (~53 KB → ~14 KB); images `max-age=86400` + ETag; a missing image 404s from one blob download, not an `ExistsAsync` probe. CORS is an allow-list in `Cors/CorsPolicies.cs`; no CSP yet (MUI injects runtime styles).
- `zz_tp/` is gitignored and ~1 GB: never `git add` inside it, never run `tpr` (use `zz_tp/scripts/render-check.sh`).
- The SPA is one chunk on purpose; a second route-level MUI dependency doubles it.
- `Program.cs` is CRLF, so `edit` may fail on multi-line matches; prices and delivery copy are derived (`formatMoney`, `DELIVERY_COPY`).
