# Pinterest pins

Scripts that publish the SaveSimple catalog to Pinterest as image pins, each linking to
its guide page. Nothing is stored in the repo: the app credentials live in environment
variables and the OAuth token lives outside the working tree.

```
scripts/pinterest_auth.py    one-time consent, token refresh, token status
scripts/pinterest_api.py     v5 client: boards, pins, retries, field limits
scripts/pinterest_pins.py    catalog -> pin plan, de-duplication, creation
```

## What a pin contains

| Field | Source | Limit |
|---|---|---|
| Title | `listing.txt` TITLE phrases (up to 3) + `— Printable PDF` | 100 chars |
| Description | `listing.txt` DESCRIPTION, then up to 6 TAGS, then the price line | 800 chars |
| Link | `https://realkevbase.com/guides/<slug>` + `utm_source=pinterest` tags | 2048 chars |
| Alt text | the first three TITLE phrases + `printable PDF first page preview` | 500 chars |
| Image | `zz_tp/output/<listing>/images/<render>.png` | 20 MB |

The words come from the shop copy, not the product name. `listing.txt` carries the TITLE
phrases a guide is sold on (median eight of them) and a curated TAGS line, and the catalog
name is only the first phrase with the format words trimmed off — pinning the name alone
would throw away most of the keywords the listing was written around.

`read_listing` parses the three blocks by heading, so the 49 files ordered
TITLE/TAGS/DESCRIPTION and the 46 ordered TITLE/DESCRIPTION/TAGS both read the same.
Editions without their own copy (`emergency_gobag_pink` renders from `emergency_gobag`)
inherit the parent's via `parent_slug`, and the 65 listings that predate `listing.txt` fall
back to the name and catalog description. Format words ("A4 Letter PDF", "Instant
Download") are dropped rather than counted as keywords. Today 95 of 160 pins carry full
keywords; the longest title is 100 characters and the longest description 451, so the
title budget is the only field that ever binds.

Long values are cut at the limit without hunting for a word boundary, exactly as
Pinterest would if the field overflowed. Phrases are packed whole rather than sliced, so
a title never ends mid-phrase.

The image is the full-page render the generator wrote, the same one the PDF was printed
from — not the site's 500 px WebP thumbnail. `scripts/listing_files.find_cover_image`
picks it by excluding `_a4`, `_ipad`, `_zoom` and numbered page artifacts, and by taking
the largest file if a folder holds several. The name can differ from the listing folder
(`bill_calendar_budget_green` renders `bill_calendar_budget.png`), which is why the
folder name is never used to guess it.

## Prerequisites (once)

1. Log into Pinterest as `savesimplestore` on a **business** account and create an app at
   <https://developers.pinterest.com/apps/>. Submit it for **Trial** access and wait for
   the approval email.
2. In **My apps → Manage → Configure → Redirect URIs**, add
   `http://localhost:8787/callback` exactly. Any mismatch is rejected during consent.

   The app form asks for a 512×512 icon: `docs/assets/pinterest-app-icon.png` (the
   storefront's S monogram, upscaled from the favicon; no Pinterest logo).
3. Export the credentials in the shell you run the scripts from — never in a file:

   ```bash
   export PINTEREST_APP_ID=...
   export PINTEREST_APP_SECRET=...
   ```

4. Approve the app once:

   ```bash
   python3 scripts/pinterest_auth.py
   ```

   It prints the consent URL, catches the redirect locally, and writes
   `$XDG_CONFIG_HOME/realkevbase/pinterest-token.json` (mode 0600). `--manual` prints the
   URL and asks you to paste the redirect back, for when a browser cannot reach the
   local port.

Access tokens last 30 days; the refresh token is continuous (60-day life, renewable
forever) for apps created after 2025-09-25. The client refreshes a day before expiry, and
again on a 401, rotating the stored refresh token each time.

## Everyday commands

```bash
# plan only: which listings, what the copy looks like, which are already pinned
python3 scripts/pinterest_pins.py

# same, machine-readable
python3 scripts/pinterest_pins.py --json

# one pin, then check it in a logged-out browser
python3 scripts/pinterest_pins.py --apply --only apartment-checklist

# a batch, paced; safe to re-run because de-duplication is by link
python3 scripts/pinterest_pins.py --apply --limit 25 --sleep 3

# everything still missing
python3 scripts/pinterest_pins.py --apply --all
```

`--dedupe=account` (the default) reads pins from every board, so a link you pinned by
hand is never duplicated. `--dedupe=board` only reads the target board and costs fewer
requests; `--dedupe=none` skips the check entirely. The local ledger
(`$XDG_STATE_HOME/realkevbase/pinterest-pins.json`) records each created pin id so an
interrupted run resumes where it stopped; `--force` ignores it.

The target board defaults to `Printables & Planners`. It must exist, or be created once
with `--create-board`.

## Images: bytes first, URL second

By default the PNG is sent inside the request (`media_source.source_type=image_base64`),
so no public hosting is involved. If Pinterest ever rejects inline bytes, add a public
route that streams the same file and switch modes:

```bash
python3 scripts/pinterest_pins.py --apply --limit 10 --image-source=url
```

That mode expects `GET /api/products/{id}/pin-image` on the API, serving
`guides/images/<stem>.png` as `image/png` with a long cache. It does not exist yet.

## Limits and the visibility caveat

- Trial access: 1,000 requests/day overall, 300 pin creations/day. A full catalog run is
  160 creates plus a few pages of de-duplication, so it fits — but spread it over days
  with `--limit` rather than posting 160 pins in one sitting.
- Trial access: **pins created through the API are visible only to the creator**. They
  become public when the app is upgraded to **Standard** access, which Pinterest reviews
  separately (expect it to ask for a stated use case and a privacy-policy URL). That URL
  is `https://realkevbase.com/privacy`, served from
  `src/realkevbase-web/public/privacy.html`; it is live on the storefront.
- Rate limits and outages are handled: `429` waits for `Retry-After`, `5xx` backs off,
  `401` refreshes once, and one failed pin does not stop the run. The exit code is 1 when
  any pin failed.

## Troubleshooting

| Symptom | Cause |
|---|---|
| `401` on every call | Token expired and no refresh token — run `pinterest_auth.py` again |
| `403 PINNER_DATA_ACCESS_DENIED` | Requesting a board or pin the app is not authorised for (app registered after 2026-09-14); use `--dedupe=account` on boards the account owns |
| `403` on create | App not approved for Trial, or token missing `pins:write` |
| `400` mentioning `media_source` | Pinterest rejected inline bytes; retry with `--image-source=url` once the route exists |
| `could not read existing pins` | Consent was given to a different account, or boards are private to another user |

Check the stored token at any time with:

```bash
python3 scripts/pinterest_auth.py --show
```
