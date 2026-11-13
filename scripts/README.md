# Storefront scripts

Operational notes for the scripts that keep the shop and the PDF generator in step.
The catalog is the product list the API serves and the SPA renders; a guide only
exists on the site when three things agree:

| Where | What it holds | Written by |
| --- | --- | --- |
| `zz_tp/output/<listing>/` | the rendered PDF and its image renders | the PDF generator |
| `src/RealKevbase.Api/Data/projects.db` | the listing row (title, template) | `sync-catalog.py` |
| `src/RealKevbase.Core/Services/InMemoryProductCatalog.cs` | the product the API serves | `sync-catalog.py` |
| `zz_tp/documents/<listing>/listing.txt` | the shop copy (title, tags, description) | whoever published the Etsy listing |

## Adding a finished guide

1. Render the PDF into `zz_tp/output/<listing>/` as usual, and make sure a
   `listing.txt` exists in `zz_tp/documents/<listing>/` (TITLE, TAGS, DESCRIPTION
   blocks — see any existing folder for the shape).
2. Preview the change:

   ```bash
   python3 scripts/sync-catalog.py
   ```

   It prints the new listings, any names or descriptions it would refresh, and
   warnings about folders or rows that exist on only one side.
3. Apply it:

   ```bash
   python3 scripts/sync-catalog.py --apply
   ```

   This uploads each new PDF, inserts the database row, appends the catalog entry,
   and publishes thumbnails and gallery renders for the new listings. It is
   idempotent — running it twice changes nothing the second time.
4. Build and ship:

   ```bash
   dotnet build src/RealKevbase.Api/RealKevbase.Api.csproj
   ./scripts/deploy.sh --yes
   ```

   `deploy.sh` runs the same sync check first and refuses to deploy while the
   catalog is out of date, so step 3 cannot be forgotten quietly.

Adding or editing the **title and description** means editing `listing.txt`, not the
C# file. Re-running the sync carries the change through both stores.

## What the sync will not decide

- **Category and card colour.** A title cannot tell you whether a list is
  healthy-living or guides. Set them in `scripts/catalog_meta.json`
  (`{"<listing_id>": {"category": "...", "accent": "..."}}`) or let it infer from the
  title. Categories: `moving-out`, `healthy-living`, `money-budgeting`, `guides`.
- **Removing a product.** Nothing is ever deleted; orphaned entries and rows are
  reported as warnings.
- **A missing `listing.txt`.** Those listings keep their current copy and are
  counted in the warnings.

## Flags worth knowing

```bash
python3 scripts/sync-catalog.py --json               # machine-readable diff
python3 scripts/sync-catalog.py --apply --no-upload  # PDFs already in storage
python3 scripts/sync-catalog.py --apply --no-images  # skip thumbnail publishing
python3 scripts/sync-catalog.py --apply --images     # also republish older listings
```

`scripts/add-listing.py <folder>` still exists for registering a single folder
interactively (it prints a paste-ready entry instead of editing the catalog). Prefer
`sync-catalog.py`; the two share `listing_files.py`, so they parse folders the same way.

## When something fails

| Symptom | Cause |
| --- | --- |
| `two listings would share a product name` | two `listing.txt` titles collide; rename one or set an override in `catalog_meta.json` |
| `upload failed … nothing was written` | blob storage unreachable or `az` not logged in; nothing was committed |
| `thumbnail publishing failed` | rows and catalog are written, images are not — fix storage and re-run `--apply --images` |
| `catalog is out of date (N changes)` from `deploy.sh` | run `python3 scripts/sync-catalog.py --apply` |
