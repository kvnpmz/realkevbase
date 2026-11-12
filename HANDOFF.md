# HANDOFF — realkevbase.com

Status snapshot: **2026-11-09**. Branch `main`, HEAD `fb61b4f`.
Repo root: `~/realkevbase-platform` on the owner's machine, `/workspace` in the build container.

**Deployed code revision: `15b6149`** (product thumbnails). Everything after that —
`1a19462`, `e10747f`, `203d76b`, `d92543e`, `ee7311a`, `a00ac08`, `fb61b4f` — changed only
`scripts/`, which is not shipped to production. No redeploy is owed for them.

---

## 1. What this project is

**realkevbase.com** sells **65 digital PDF guides at $5 each** and a small line of physical
merch. **RealKevbase** is the umbrella brand; **SaveSimple** is the product line, presented as
"the SaveSimple collection".

The storefront is a **React 19 + Material UI SPA** (Vite) on **Cloudflare Pages**. It talks to
a **C# / .NET 10 Minimal API** on **Azure App Service (Linux, B1)** at `api.realkevbase.com`.

The important architectural change since the last handoff: **the catalog is server-owned.**
The SPA no longer contains product data or storage paths. It fetches `GET /api/products`
(68 items: 65 PDFs + 3 merch). The 860-line
`src/realkevbase-web/src/data/products.js` was deleted, which removed every
`guides/*.pdf` blob path from the bundle.

Payments run through **Stripe Checkout** in **live mode**. Fulfilment is asynchronous: a
signature-verified webhook enqueues the session id on a bounded `Channel`, a
`BackgroundService` re-reads the paid session from Stripe, mints **24-hour Azure Blob SAS
links**, emails them via **Azure Communication Services**, and exposes the same links on the
success page via `GET /api/checkout/sessions/{sessionId}/downloads`.

**Product artwork** is now real: 65 page renders are served as card thumbnails through
`GET /api/products/{id}/thumbnail`, proxied from private storage (no SAS, no blob name in
any payload).

---

## 2. Current deployed state

### Frontend — Cloudflare Pages

| Item | Value |
|---|---|
| Pages project | `realkevbase-client` |
| Custom domains | `realkevbase.com`, `www.realkevbase.com` |
| Live bundle | `/assets/index-o6-a42vm.js` (482.6 kB, 151 kB gzip) |
| Bundled API base | `https://api.realkevbase.com` (inlined at build time) |
| Catalog source | `GET /api/products` (no local product data) |

✅ **Verified**: `https://realkevbase.com/` → `200`; live bundle matches the local build;
68 cards render with thumbnails at 390 px and 1280 px; category pills (All / Moving Out /
Healthy Living / Money & Budgeting / Guides / Merch) with 12-desktop / 6-mobile paging.

### Backend — Azure App Service

| Item | Value |
|---|---|
| Resource group | `realkevbase-rg`; App Service in **West US 2** |
| App Service | `realkevbase-platform` |
| Plan | `realkevbase-plan`, **B1**, Linux, capacity **1**, **no autoscale rules** |
| Custom domain | `api.realkevbase.com` (SNI, Azure managed certificate, valid to 2027-05-08) |
| `httpsOnly` | `true` |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

✅ **Verified**: 12-check smoke test passes (section 5). Catalog serves 68 products / 65 PDFs
with no storage details; all 65 thumbnails return `200 image/webp`; a live checkout session
returns `cs_live_…`.

⚠️ **No autoscaling.** That is deliberate and is why a traffic flood **cannot** run up the
compute bill — cost stays flat at roughly $13/month. Do not add scale-out rules without
understanding that they convert a flood into a bill. The only abuse-scaled cost is
**Application Insights ingestion** (no daily cap set).

### Data / storage / email

| Item | Value |
|---|---|
| Storage account | `realkevbasestorage` (Standard_LRS), container `pdfs` |
| PDF blobs | `guides/<hyphen-slug>.pdf` — **65** |
| Thumbnail blobs | `guides/images/<hyphen-slug>.webp` — **65**, `image/webp`, ~42 kB each |
| Orphan blobs | none (verified) |
| Listing database | `src/RealKevbase.Api/Data/projects.db` — 69 rows, 65 unique titles |
| App Insights | app writes to `realkevbase-platform202611071211`; 5 components exist (see traps) |
| ACS | `realkevbase-acs`, email service `realkevbase-email`, sender `noreply@realkevbase.com` |
| Email domain | `realkevbase.com` `CustomerManaged` — Domain / SPF / DKIM / DKIM2 **Verified** |
| DMARC | **published**: `v=DMARC1; p=none; rua=mailto:you@realkevbase.com` — the `rua` is a placeholder and reports go nowhere (section 6) |

### Stripe — LIVE

| Item | Value |
|---|---|
| **Live account (in use)** | `acct_1UZEkkBM39vAXdSW` — charges, payouts and details all enabled, US, USD |
| Secret key | `sk_live_…` (107 chars) in App Service setting `Stripe__SecretKey` |
| Webhook secret | `whsec_…` (38 chars — dashboard endpoint secret, **not** the 70-char `stripe listen` one) |
| Webhook endpoint | `we_1UZjjeBM39vAXdSWD8mOFas9` → `https://api.realkevbase.com/api/checkout/webhook`, `livemode: true`, enabled |
| Endpoint events | `checkout.session.completed`, `checkout.session.async_payment_succeeded` |
| Endpoint API version | `2026-10-28.endive` — tolerated (`throwOnApiVersionMismatch: false`) |
| Sandbox (retired) | `acct_1UYhPSPm53HdvLrD`, endpoint `we_1UZaFtPm53HdvLrDANzYFURI` — no longer used |

✅ **Verified**: session creation returns `cs_live_…`; the session reports `livemode: true`,
`amount_total: 500 USD`; test cards are rejected by Stripe in live mode, so the
"4242 → free PDF" hole is closed.

❌ **Not yet done**: one real card purchase end to end on the live account (section 6, item 1).

---

## 3. Architecture map

### Catalog path (new)

```
zz_tp/output/<listing_id>/            ← generator output (gitignored, local only)
  ├── <template>.pdf                   (full page; _a4/_ipad/_zoom are variants)
  └── images/<template>.png            ← thumbnail source, 2550x3300

zz_tp/projects.db → copied by hand once → src/RealKevbase.Api/Data/projects.db
  Projects(id, folder_path, completed_date, overrides, output_name,
           is_editable, template, title)      ← 'title' added; 65 unique values

GET /api/products   → ProductResponse  (id, name, description, price, fulfillment,
                                        category, listingId, thumbnailUrl, accent)
                                        never PdfBlobName
GET /api/products/{id}/thumbnail → streams image/webp from blob, Cache-Control 86400, ETag

join key: Product.ListingId = blob filename with '-' → '_'
          e.g. guides/150first-foods.pdf → 150first_foods
A digital product is only offered when its listing row exists. If the DB is missing,
GetAll() returns null and every product is served (a storage problem must not empty the store).
```

### Money path (unchanged in shape, now live)

```
POST /api/checkout/sessions  { items: [{ productId, quantity }] }
  │  prices always come from IProductCatalog (client cannot influence price)
  │  duplicate lines aggregated, quantity capped at 20
  ▼
Stripe Checkout (hosted, live)
  ├── (A) redirect → realkevbase.com/?checkout=success&session_id=cs_live_…
  │        GET /api/checkout/sessions/{id}/downloads  (re-reads session, gates on "paid")
  └── (B) webhook → ConstructEvent (HMAC, 300 s tolerance) → enqueue → worker
           → SAS links (Read, 24 h) → ACS email
```

---

## 4. Key config and where it lives

**Never paste values into chat, commits, logs or issues.** Names only.

### Azure App Service settings (production)

| Name | Notes |
|---|---|
| `Stripe__SecretKey` | **live** `sk_live_…` (107 chars) |
| `Stripe__WebhookSecret` | **live dashboard endpoint** secret (38 chars) |
| `ConnectionStrings__AzureStorage` | blob connection string |
| `AzureEmail__ConnectionString` | ACS connection string |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `WEBSITES_PORT` | `8080` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | set by the App Insights extension |

Non-secret config in `src/RealKevbase.Api/appsettings.json`: `AzureEmail:SenderAddress`,
`Listings:DatabasePath` (`Data/projects.db`), Stripe currency / shipping countries.
`appsettings.Production.json` overrides only the Stripe success/cancel URLs to
`https://realkevbase.com/…`.

### Local tooling

| Thing | Where | Notes |
|---|---|---|
| `.venv/` | repo root | Pillow for the thumbnail script. **Gitignored, never commit.** |
| `scripts/requirements.txt` | repo | `Pillow>=13,<14` |
| Storage credentials | resolved at run time | `scripts/storage_auth.py` reads the App Service setting via `az` |
| `zz_tp/` | repo root | 672 MB / 3,467 files of generator workspace. **Gitignored.** |

### Cloudflare DNS

| Record | Purpose | Proxy |
|---|---|---|
| `api` CNAME → `realkevbase-platform-….azurewebsites.net` | App Service | **DNS only (grey)** — required for the managed certificate |
| apex + `www` → Pages | storefront | proxied |
| `_dmarc` TXT | `v=DMARC1; p=none; rua=mailto:you@realkevbase.com` | n/a |

---

## 5. Verification: one command

```bash
./scripts/deploy.sh verify          # read-only, safe to run against production any time
```

Twelve assertions, exit non-zero on any failure:

```
paywall: unauthenticated link endpoint is gone (404)
scaffold endpoint /weatherforecast is gone (404)
catalog responds (200)
catalog shape (68 products, 65 PDFs) and no storage details
security headers present
Server header suppressed
quantity cap rejects 21 (400)
product artwork serves (200)
thumbnailUrl exposes no storage details
storefront responds (200)
live bundle is current (assets/index-…js)
bundle asset is fetchable (200)
```

This exists because a successful build cannot see the class of bug that mattered most: the
removed `/api/checkout/downloads/pdf` endpoint passed `dotnet build`, `eslint` and a green
deploy while handing out free PDFs. If a check fails, do not ship.

---

## 6. Known open items, prioritized

1. **One real card purchase on the live account.** Create a session, pay $5 with a real card,
   confirm the webhook emails the link and the PDF downloads, then refund. Everything is
   proven hop by hop and a live session has been created, but the uninterrupted
   "buy → email → download" run has never been executed on the live key.
2. **No test-mode guard.** Every protection above rests on `Stripe__SecretKey` staying
   `sk_live_`. One `az` command reverts it and nothing in the code objects — free PDFs return
   silently and the store still looks healthy. ~30 lines: return 503 on checkout and skip
   fulfilment when the key starts with `sk_test_`. Highest value per line of anything left.
3. **Merch takes money and ships nothing.** Tee $24, Hoodie $59, Desk Mat $34 are purchasable
   live with no fulfilment process and no photography. A single real order costs a refund
   plus a possible chargeback fee. Hide the three entries or build the process.
4. **`api.realkevbase.com` is not behind Cloudflare** (DNS-only, resolves to
   `20.115.232.11`). No WAF, no rate limiting, no DDoS absorption. This is an availability
   risk, not a billing one. ⚠️ Proxying is **not** a simple toggle: the App Service managed
   certificate is issued by DNS validation, so moving to the orange cloud needs a certificate
   on the origin (or Cloudflare-only TLS) plus a re-verified custom domain.
5. **Application Insights has no daily cap**, and there are **five** duplicate components from
   repeated create attempts (`realkevbase-api`, `realkevbase-api20261106188`,
   `realkevbase-api202611061812`, `realkevbase-platform`, `realkevbase-platform202611071211`).
   Only the last is wired. Cap the workspace and delete the rest.
6. **Secrets are plaintext app settings**, not Key Vault. The ACS connection string is an
   account key — broader than "send email"; a managed identity with a sender role would remove
   it entirely.
7. **Durability.** Catalog is `InMemoryProductCatalog`; the fulfilment queue is a bounded
   `Channel<string>` (100, `FullMode.Wait`, no persistence). A restart drops queued sessions;
   a full queue returns `503` so Stripe retries. Fine at this volume.
8. **No webhook idempotency.** A Stripe retry re-sends the download email. Re-signing is
   idempotent so nothing breaks, but customers may get two emails.
9. **No CI.** Deployments are manual via `scripts/deploy.sh`. The smoke test is the safety net.
10. **DMARC `rua` is a placeholder** (`you@realkevbase.com`), so aggregate reports are
    discarded. Set a real mailbox.
11. **Descriptions and prices live in `InMemoryProductCatalog.cs`**, not the database. Copy or
    price edits need a redeploy; only listings, titles and thumbnails are data-driven.
12. **Cosmetic.** Single 482 kB chunk worth code-splitting. The cart is intentionally **not**
    cleared after payment (a reopened receipt must not wipe a new cart).

---

## 7. Traps and gotchas

**Data and catalog**

- **Never copy `zz_tp/projects.db` over `src/RealKevbase.Api/Data/projects.db`.** The source
  has 7 columns; the shipped one has 8 because `title` was added and backfilled with 65 unique
  values. A plain copy silently blanks every product name. `scripts/add-listing.py` inserts and
  refuses to run if the `title` column is absent.
- **Product GUIDs cannot be regenerated.** They are UUIDv5-looking but from a namespace that
  is not reproducible; treat them as opaque constants. A new GUID for an existing product
  breaks live carts and bookmarks. New listings get a fresh `uuid4`, which is fine.
- **`ListingId` is derived, not stored.** `PdfBlobName` with `'-'`→`'_'`. A product whose
  derived id is not in `Projects` is filtered out of `/api/products` **with no error**.
- **The generator names renders after the template, not the folder.** `150first_foods/`
  contains `baby_foods.pdf`; both `baby_registrylist*` variants render
  `baby_registrylist.png`. Thumbnail upload renames to the PDF's unique slug.
- **Skip the variants and the placeholders.** `images/` also holds `_a4`, `_ipad`, `_zoom`
  and numbered page artifacts (`…-1.png`), and one folder has a 1×1 PNG.
- **`zz_tp/` is 672 MB** across 3,467 files and is gitignored. Never `git add` it.

**Deploy**

- **`VITE_API_BASE_URL` is inlined at build time.** Build without it and the bundle falls back
  to same-origin, so the SPA calls `realkevbase.com/api/products`, gets HTML back, and the
  catalog silently fails. `deploy.sh` sets it and hard-fails if unset.
- **Deploy the API before the frontend.** A new SPA against an old API shows an error banner;
  the reverse is harmless.
- **`--branch main`** on the Pages deploy is what aliases to `realkevbase.com`. Without it you
  get a preview URL and the live site does not change.
- **Cold start after deploy is ~10–35 s.** Poll until `200`; do not conclude the app is broken.
- **Commit before deploying.** `projects.db` is a tracked binary; `deploy.sh` publishes from
  the working tree, so an uncommitted edit goes live while `origin` lags.

**Stripe**

- **`stripe listen` secret ≠ dashboard endpoint secret** (70 vs 38 chars). Deploying the
  listener secret yields `The expected signature was not found`.
- **Sandboxes are separate accounts.** An endpoint in one account never receives another
  account's events.
- **Signature failures land in `exceptions`,** not `traces` — query
  `exceptions | where type == 'Stripe.StripeException'`.
- **Test cards are rejected in live mode**, which is the whole point; do not "fix" a declined
  4242.

**Tooling**

- **`az` argument ordering matters.** Splicing `--connection-string` into the middle of an
  `az storage` command makes the CLI read the value as a subcommand name — which also **prints
  the secret into the error output**. Append arguments at the end.
- **`az storage --auth-mode login` does not work here**: the signed-in identity lacks
  *Storage Blob Data Contributor*. `scripts/storage_auth.py` reads the connection string from
  App Service settings instead.
- **Pillow needs `.venv`.** System Python is externally managed (PEP 668); `pip install` outside
  a venv is refused. `add-listing.py` is stdlib-only and runs under plain `python3`.
- **App Insights component matters.** Five exist; the app writes to
  `realkevbase-platform202611071211`. Querying another returns 0 rows and looks like
  "nothing is logged".
- **No `zip` binary in the container** — build archives with Python's `zipfile`
  (`deploy.sh` does).

---

## 8. What NOT to do

- **Do not bind the apex to App Service** — it belongs to Cloudflare Pages.
- **Do not paste secret values** into chat, logs, commits or screenshots; refer to them by
  name. When a value must move, pipe it inside one command and print only a length or a hash.
- **Do not delete or recreate `POST /api/checkout/downloads/pdf`.** It minted a 24-hour SAS URL
  from a public product id with no payment check — a full paywall bypass. The only legitimate
  path is `GET /api/checkout/sessions/{id}/downloads`, which re-reads the session and gates on
  `payment_status == "paid"`. `deploy.sh verify` asserts the removed endpoint stays 404.
- **Do not expose `PdfBlobName`, blob paths or SAS URLs in any API response.** `ProductResponse`
  is a field allowlist; keep it that way.
- **Do not add autoscale** without deciding that a flood should become a bill.
- **Do not regenerate existing product GUIDs.**
- **Do not copy the source SQLite file over the shipped one.**
- **Do not clear the buyer's cart after payment.**
- **Do not widen CORS back to any `*.pages.dev`** — it is now scoped to
  `realkevbase-client.pages.dev`; strangers can publish a Pages site in seconds.
- **Do not trust `ForwardedHeaders` for anything security-relevant.** `KnownProxies` and
  `KnownIPNetworks` are cleared, so `X-Forwarded-For` is client-controlled. Fix that before
  adding any IP-based rate limiting.

---

## 9. Common operations

### Add a PDF listing

```bash
az login                                        # once per machine/session; no exports needed

python3 scripts/add-listing.py new_thing             # preview, writes nothing
python3 scripts/add-listing.py new_thing --apply     # insert row + upload PDF

# paste the printed C# block into InMemoryProductCatalog.cs and fill in:
#   Description, Category (moving-out|healthy-living|money-budgeting|guides), Accent
# Price is already 5.0m.

.venv/bin/python scripts/build-thumbnails.py --upload
git add -A && git commit -m "feat(catalog): add new thing"
./scripts/deploy.sh --yes
```

The folder must be `zz_tp/output/<listing_id>/` containing a full-page PDF and `images/`.
Duplicate titles are auto-numbered ("Foo", "Foo 2"); a duplicate `listing_id` is refused.
Uploads resolve credentials from App Service settings via `az` — nothing is exported or stored.

### Deploy and verify

```bash
./scripts/deploy.sh            # API + web, then verify
./scripts/deploy.sh api        # one target, then verify
./scripts/deploy.sh verify     # assertions only, no deploy
```

### Rotate the Stripe webhook secret

```bash
read -s -p "live webhook secret: " WH; echo
az webapp config appsettings set -g realkevbase-rg -n realkevbase-platform \
  --settings "Stripe__WebhookSecret=$WH" -o none
az webapp restart -g realkevbase-rg -n realkevbase-platform
unset WH
```

Expect 38 characters. Verify by creating a live session (not by trusting a dashboard `200`).

### Local development

```bash
dotnet run --project src/RealKevbase.Api --urls http://localhost:5283
cd src/realkevbase-web && VITE_API_BASE_URL=http://localhost:5283 npm run dev
```

CORS allows `http://localhost:5173` only, so keep Vite's default port. User secrets load in
`Development` only; `Production` locally needs the env vars injected.

---

## 10. Next session

**Frontend UI polish — scope not yet decided.** Constraints and useful context:

- **The visual design is approved and frozen.** Do not change the theme palette
  (`#14161A` ink, `#2F6BFF` accent, `#FBFAF7` background, `#E7E4DC` divider), the typography
  clamps, or the MUI component overrides in `src/realkevbase-web/src/theme.js`.
- Copy is settled: "the SaveSimple collection", hero "Organization, made effortless.",
  overline "Browse the collection". **RealKevbase stays capitalised everywhere.**
- Layout facts: cards show a 172 px artwork strip (thumbnail `object-fit: contain` over the
  accent gradient, badge chip at z-index 2); category pills scroll horizontally on mobile;
  paging is 12 desktop / 6 mobile with a "Show more" button and an aria-live counter.
- Merch cards fall back to an icon because there is no photography — that is intentional
  until real photos exist.
- Free wins if you want them: code-split the single 482 kB chunk; the cart is deliberately not
  cleared after payment.
- `./scripts/deploy.sh verify` must still pass after any UI change; run it before declaring done.
