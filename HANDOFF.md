# HANDOFF — realkevbase.com

Status snapshot: **2026-11-08**. Repo root: `~/realkevbase-platform`, branch `main`, HEAD `23be2bc`.

---

## 1. What this project is

**realkevbase.com** is a two-host storefront that sells digital PDF guides for **$5 each** (65 of them) alongside a small line of physical merch. The storefront is a **React 19 + Material UI SPA** built with Vite and hosted on **Cloudflare Pages**; it talks to a **C# / .NET 10 Minimal API** on **Azure App Service (Linux, B1)** at `api.realkevbase.com`. Payments run through **Stripe Checkout** (hosted page): the SPA posts product ids to `POST /api/checkout/sessions`, the API validates them against an in-memory catalog and creates the session, and Stripe redirects the buyer back to the SPA with `?checkout=success&session_id=…`. Fulfilment is **asynchronous**: a signature-verified Stripe webhook enqueues the session id on a bounded `Channel`, a `BackgroundService` re-reads the paid session from Stripe, resolves the digital products, mints **24-hour Azure Blob Storage SAS links**, emails them via **Azure Communication Services Email**, and exposes the same links on the success page through `GET /api/checkout/sessions/{sessionId}/downloads`.

---

## 2. Current deployed state

### Frontend — Cloudflare Pages

| Item | Value |
|---|---|
| Pages project | `realkevbase-client` |
| Custom domains | `realkevbase.com`, `www.realkevbase.com` |
| Pages domain | `realkevbase-client.pages.dev` |
| Last deployment id | `3f5c0e16.realkevbase-client.pages.dev` |
| Live bundle | `/assets/index-Cy1YeSJw.js` (502 kB, 154 kB gzip) |
| Bundled API base | `https://api.realkevbase.com` (inlined at build time) |

✅ **Verified working**: `https://realkevbase.com/` → `200 text/html`; bundle inlined API base (`grep -c 'api.realkevbase.com'` → 1); 68 catalog cards render (65 PDF + 3 merch); success page renders two `Download PDF` anchors that serve `200 application/pdf`.

### Backend — Azure App Service

| Item | Value |
|---|---|
| Resource group | `realkevbase-rg` (Central US; App Service itself is in **West US 2**) |
| App Service | `realkevbase-platform` |
| Default hostname | `realkevbase-platform-gkhed8h4dsadeyga.westus2-01.azurewebsites.net` |
| Plan | `realkevbase-plan`, **B1**, Linux, .NET `DOTNETCORE|10.0` |
| Custom domain | `api.realkevbase.com` (Verified, SNI binding) |
| TLS | Azure managed certificate, thumbprint `55D28D01DACA6DDDAAE6004932070E39F33D56B6`, issuer GeoTrust TLS RSA CA G1, valid to **2027-05-08** |
| Deployed revision | `23be2bc` (zip deploy of `dotnet publish -c Release`) |
| Platform CORS | **empty** (deliberately — see §7) |
| `alwaysOn` | `true` |

✅ **Verified working**: `https://api.realkevbase.com/api/checkout/sessions/cs_test_x/downloads` → `404 application/problem+json` over valid TLS (`subject=CN=api.realkevbase.com`); unsigned `POST /api/checkout/webhook` → `400`; preflight from `https://realkevbase.com` → `204` with `Access-Control-Allow-Origin`; a live session POST from that origin → `200` + Stripe URL.

### Data / email resources

| Item | Value |
|---|---|
| Storage account | `realkevbasestorage` (Standard_LRS) |
| Container | `pdfs` |
| Blob layout | `guides/<hyphen-slug>.pdf` — 65 objects, one per catalog product |
| App Insights | `realkevbase-platform202611071211` (the component the app writes to — see §7 trap) |
| ACS resource | `realkevbase-acs` (location `global`, data location **United States**) |
| Email service | `realkevbase-email` |
| Email domain | `realkevbase.com`, `CustomerManaged` — **Domain / SPF / DKIM / DKIM2 = Verified**, DMARC = `NotStarted` |
| Linked to ACS | yes (`linkedDomains` contains the domain resource id) |
| Sender usernames | `DoNotReply` (provisioned with the domain), `noreply` (created — the configured sender) |

### Stripe

| Item | Value |
|---|---|
| **Sandbox account (in use)** | `acct_1UYhPSPm53HdvLrD` — "realkevbase sandbox", test mode |
| Webhook endpoint | `we_1UZaFtPm53HdvLrDANzYFURI` → `https://api.realkevbase.com/api/checkout/webhook` |
| Endpoint events | `checkout.session.completed`, `checkout.session.async_payment_succeeded` |
| Endpoint API version | `2026-10-28.endive` (payload style *Snapshot*) — tolerated: `throwOnApiVersionMismatch: false` |
| **Live account (NOT configured)** | `acct_1UZEkkBM39vAXdSW` — no keys, no endpoint, no live products yet |

✅ **Verified working**: a real Stripe delivery returned `200`; a real paid two-PDF session was fulfilled (`Fulfilled 2 digital product(s)`) and emailed (`Emailed 2 PDF download link(s) … operation status Succeeded`).

⚠️ **Configured but NOT verified**: a purchase driven from the production domain through to an inbox. A real card payment has been completed against a session created by the API (earlier e2e), and each hop has been proven separately in production, but the single uninterrupted "buy on realkevbase.com → receive email → click link" run has **not** been executed yet.

❌ **Missing**: live-mode Stripe configuration; DMARC record; email inbox-placement confirmation; real merch photography.

---

## 3. Architecture map — the money path

```
browser (realkevbase.com, React SPA)
  │  user clicks Checkout in CartDrawer
  ▼
POST https://api.realkevbase.com/api/checkout/sessions      { items: [{ productId, quantity }] }
  │  Endpoints/CheckoutEndpoints.cs  → StripeCheckoutService.CreateSessionAsync
  │  catalog validation (unknown id → 400 Unknown products), cent-safe money math,
  │  line metadata product_id / fulfillment, currency usd, shipping for merch
  ▼
Stripe Checkout (hosted, checkout.stripe.com/c/pay/cs_test_…)
  │  buyer pays (card / delayed methods)
  ▼
redirect → https://realkevbase.com/?checkout=success&session_id=cs_…
  │                                    │
  │ (A) SUCCESS PAGE PATH              │ (B) WEBHOOK PATH
  ▼                                    ▼
GET /api/checkout/sessions/{id}/downloads   POST /api/checkout/webhook
  │  DigitalFulfillmentService.IssueAsync    │  EventUtility.ConstructEvent (HMAC-SHA256, t=,v1=, 300 s tolerance)
  │  re-reads session from Stripe,           │  filter: completed | async_payment_succeeded
  │  gates on payment_status == "paid",      │  gate: payment_status == "paid" (else 200 "deferred")
  │  resolves products, mints SAS            │  enqueue session id → bounded Channel (cap 100, Wait, SingleReader)
  ▼                                         ▼
 CheckoutFulfillmentWorker (BackgroundService, ReadAllAsync)
  │  DigitalFulfillmentService.FulfillAsync → IssueAsync → IPdfDeliveryService.DeliverAsync
  │  AzureBlobPdfStorageService.CreateDownloadUrlAsync  (BlobSasBuilder, Read, 24 h, 5 min backdate)
  ▼                                         ▼
 React success view renders buttons      Azure Communication Services Email
 (2048-character SAS URLs, target=_blank) → buyer's session.customer_details.email
```

**Commit SHAs making up the current state** (oldest → newest):

| SHA | What it adds |
|---|---|
| `a7248c9` | `POST /api/checkout/sessions` with catalog validation and mixed orders |
| `c604a83` | Storefront assembly, cart context, MUI polish |
| `14c39da` | Webhook signature verification + background queue/worker + SAS issuance |
| `43b0d3a` | `GET /api/checkout/sessions/{id}/downloads` (`IssueAsync` refactor) |
| `8bc9027` | 65-PDF catalog at $5 in **both** API and SPA, uploaded blobs |
| `a68621b` | SPA success view (`CheckoutSuccess.jsx`) |
| `c184983` | `.gitignore`: `publish/`, `TestResults/` |
| `a7a0584` | Fulfil on `checkout.session.async_payment_succeeded` too (delayed payment methods) |
| `23be2bc` | Email delivery through Azure Communication Services (**deployed backend**) |

Product ids are **deterministic UUIDv5** of `https://realkevbase.com/products/<slug>`, generated once and mirrored between `InMemoryProductCatalog.cs` and `src/realkevbase-web/src/data/products.js`, so storefront and API cannot drift.

---

## 4. Key config and where it lives

**Never paste values into a chat, a commit, a log or an issue.** Names only, below.

### User secrets (local development only)

`UserSecretsId` = `abdf585f-250d-4b93-9e62-18dd07061bed`, file `~/.microsoft/usersecrets/abdf585f-250d-4b93-9e62-18dd07061bed/secrets.json`.

| Key | Purpose |
|---|---|
| `Stripe:SecretKey` | sandbox test secret key (`sk_test_…`) |
| `Stripe:WebhookSecret` | signing secret of `we_1UZaFtPm53HdvLrDANzYFURI` (38 chars) |
| `AzureStorage:ConnectionString` | blob account connection string |
| `AzureEmail:ConnectionString` | ACS connection string (165 chars, endpoint `https://realkevbase-acs.unitedstates.communication.azure.com/`) |

Note: the file is **UTF-8 with BOM** and keys are a **flat** `"Section:Name"` map — read it with `encoding='utf-8-sig'`.

### Azure App Service settings (production)

| Name | Notes |
|---|---|
| `Stripe__SecretKey` | sandbox test key |
| `Stripe__WebhookSecret` | must equal the *dashboard endpoint* secret, not the `stripe listen` secret |
| `ConnectionStrings__AzureStorage` | blob connection string (companion key `AzureStorage__ConnectionString` is also accepted by the app) |
| `AzureEmail__ConnectionString` | ACS connection string |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `WEBSITES_PORT` | `8080` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | set by the App Insights extension |

Not set, deliberately: `Stripe__ApiBase` (unset ⇒ real Stripe), `Stripe:ApiKey` (older name).

`AzureEmail:SenderAddress` = `noreply@realkevbase.com` lives in **`src/RealKevbase.Api/appsettings.json`** (not a secret). `appsettings.Production.json` only overrides the Stripe success/cancel URLs to `https://realkevbase.com/?checkout=success&session_id={CHECKOUT_SESSION_ID}` and `…?checkout=cancelled`.

### Cloudflare DNS (zone `realkevbase.com`, NS `tiffany.ns.cloudflare.com` / `donovan.ns.cloudflare.com`)

| Record | Purpose | Proxy |
|---|---|---|
| `api` CNAME → `realkevbase-platform-gkhed8h4dsadeyga.westus2-01.azurewebsites.net` | App Service | **DNS only (grey)** — required for the managed cert |
| `asuid.api` TXT → App Service `customDomainVerificationId` | domain ownership | n/a |
| apex + `www` → Pages | storefront | proxied |
| `@` TXT `ms-domain-verification=…` | ACS domain verification | n/a |
| `@` TXT `v=spf1 include:spf.protection.outlook.com -all` | SPF (**only one SPF record at apex**) | n/a |
| `selector1-azurecomm-prod-net._domainkey` CNAME → `….azurecomm.net` | DKIM | **DNS only** |
| `selector2-azurecomm-prod-net._domainkey` CNAME → `….azurecomm.net` | DKIM2 | **DNS only** |
| `_dmarc` TXT | DMARC — **not currently published**, see §6 | n/a |

---

## 5. What is verified, and how to re-run each check

All commands assume the Azure CLI, `stripe` CLI (sandbox), and `npx --no-install wrangler` are authenticated.

**a) Webhook accepts a real Stripe delivery and fulfils**

```bash
stripe trigger checkout.session.completed
sleep 75
az monitor app-insights query -g realkevbase-rg -a realkevbase-platform202611071211 \
  --analytics-query "requests | where timestamp > ago(30m) | where name contains 'webhook' | project timestamp, resultCode, success | order by timestamp desc | take 5" -o json
```

Expected: `HTTP 200 success=True`. Template sessions carry no catalog products, so the trace then says `contains no digital products` — that is correct, not a bug. For a *real* order, resend a paid event (note the flag, see §7):

```bash
stripe events resend <evt_id> --webhook-endpoint we_1UZaFtPm53HdvLrDANzYFURI
```

Expected traces: `Queued Stripe session cs_…` → `Emailed N PDF download link(s) … (operation status Succeeded)` → `Fulfilled N digital product(s)`.

**b) Success page serves real PDFs** (no Playwright installed; drive the cached Chromium directly)

```bash
SESS=cs_test_b1B53gEvyYwxmrM2hRdpFYYiUhKNN0abbuxnsBj9csZAWNXA8IYYU7w9cM
~/.cache/ms-playwright/chromium-1248/chrome-linux64/chrome --headless=new --no-sandbox \
  --disable-gpu --virtual-time-budget=15000 --dump-dom \
  "https://realkevbase.com/?checkout=success&session_id=$SESS" > /tmp/dom.html
grep -o 'href="[^"]*blob\.core\.windows\.net[^"]*"' /tmp/dom.html   # expect 2 anchors
```

Expected: two `Download PDF` anchors; following each yields `200 application/pdf` and `%PDF-` magic bytes (measured: `apartment-checklist.pdf` 60,596 B, `baby-registrylist.pdf` 26,142 B).

**c) API reachability, TLS and error contract**

```bash
curl -s -o /dev/null -w "%{http_code} %{content_type}\n" \
  https://api.realkevbase.com/api/checkout/sessions/cs_test_x/downloads      # 404 application/problem+json
curl -s -o /dev/null -w "%{http_code}\n" -X POST \
  https://api.realkevbase.com/api/checkout/webhook -d '{}' -H 'Content-Type: application/json'   # 400
echo | openssl s_client -connect api.realkevbase.com:443 -servername api.realkevbase.com 2>/dev/null \
  | openssl x509 -noout -subject -enddate                                    # CN=api.realkevbase.com
```

**d) CORS from the live origin**

```bash
curl -s -o /dev/null -D - -X OPTIONS https://api.realkevbase.com/api/checkout/sessions \
  -H "Origin: https://realkevbase.com" -H "Access-Control-Request-Method: POST" \
  -H "Access-Control-Request-Headers: content-type" | grep -i "^HTTP\|allow-origin"
```

Expected `204` + `Access-Control-Allow-Origin: https://realkevbase.com`. `*.pages.dev` previews are allowed; `http://localhost:5173` is allowed; everything else gets `204` **without** the ACAO header (browser blocks).

**e) Email path**

```bash
CS=$(az communication list-key --name realkevbase-acs --resource-group realkevbase-rg --query primaryConnectionString -o tsv)
az communication email send --connection-string "$CS" --sender noreply@realkevbase.com \
  --to <inbox> --subject "RealKevbase deliverability test" --text "test" --wait-until completed -o json
unset CS
```

Expected `{"error": null, "id": "<guid>", "status": "Succeeded"}`. A send has already returned `Succeeded` (operation `3809fcf5-2e74-4300-aa07-afc6e0a7748c`); **inbox placement for that message was never confirmed by the recipient**. To re-run the application path rather than the CLI, resend a paid event as in (a).

**f) ACS domain verification state**

```bash
az communication email domain show --domain-name realkevbase.com \
  --email-service-name realkevbase-email -g realkevbase-rg --query verificationStates -o json
dig +short TXT realkevbase.com; dig +short CNAME selector1-azurecomm-prod-net._domainkey.realkevbase.com
```

---

## 6. Known open items, prioritized

1. **End-to-end purchase on the production domain (highest value, costs $0).** Create a session with `POST /api/checkout/sessions`, pay it with `4242 4242 4242 4242`, put a real inbox in Stripe's email field, and confirm the webhook emails the links and the PDF downloads. Everything is proven hop-by-hop; this is the one uninterrupted run still owed. A session created on 2026-11-08 (`cs_test_a1aavUy6EMJJvgYDuAZjIKxD4EWnFHWSnDb1A50wJHdOKNuUPre20lBMgE`) was never paid and has expired.
2. **DMARC record not published.** `verificationStates.DMARC` stays `NotStarted` (ACS only accepts `--verification-type Domain|SPF|DKIM|DKIM2`, so DMARC is never initiated and has no error code). Authoritative NS returns nothing for `_dmarc.realkevbase.com` while apex TXT resolves — the classic Cloudflare mistake is entering the FQDN in the **Name** field, which publishes `_dmarc.realkevbase.com.realkevbase.com`. Enter `_dmarc` only, type TXT, value `v=DMARC1; p=none; rua=mailto:<address>`.
3. **Live-mode Stripe migration.** Account `acct_1UZEkkBM39vAXdSW` has nothing configured: needs live keys, a live webhook endpoint (`checkout.session.completed` + `checkout.session.async_payment_succeeded`) and its signing secret, live product/price review, `httpsOnly`, and a real end-to-end payment. Keep the sandbox path intact until live is proven.
4. **Merch is placeholder.** Three `PhysicalMerch` entries (RealKevbase Logo Tee $24, Hoodie $59, Desk Mat $34) have no photography and no shipping/fulfilment process behind them. Either shoot real photos or drop the entries; note `Stripe:ShippingCountries` (22 countries) and a $0 shipping rate exist and are exercised by mixed carts.
5. **Durability.** Catalog is `InMemoryProductCatalog` and the fulfilment queue is a bounded `Channel<string>` (capacity 100, `FullMode.Wait`, no persistence). A restart drops queued sessions, and a full queue makes the webhook return `503` so Stripe retries. Fine at launch volume; needs durable storage + idempotency before scale.
6. **ACS connection string is an account key** in app settings — broader than "send email". A managed identity with a sender role on `realkevbase-acs` would remove the secret from config entirely.
7. **No CI.** `.github/workflows/ci.yml` does not exist; deployments are manual. Backend `dotnet build` is currently 0 warnings / 0 errors; frontend `npm run build` passes with a 500 kB chunk advisory.
8. **Cosmetic/cleanup.** `app.MapGet("/weatherforecast")`, a stray `Class1.cs`, unused starter assets, and a 502 kB single-chunk bundle worth code-splitting. The cart is intentionally **not** cleared after payment (a receipt link reopened in the same browser must not wipe a new cart).

---

## 7. Traps and gotchas

- **App Service platform-level CORS shadows Kestrel.** If `az webapp cors show` lists origins, the platform answers preflights first and the app's predicate never runs — which produced a `400 The origin … is not allowed` for a `*.pages.dev` preview while the same code returned `204` locally. Platform CORS must stay empty (`az webapp cors remove … --allowed-origins …`, then restart; allow ~2 min).
- **`stripe listen` secret ≠ dashboard endpoint secret.** The CLI's secret is 70 characters (`whsec_` + 64 hex) and only signs events forwarded to the local listener; a dashboard endpoint's secret is 38 characters. Deploying the listener secret to production yields `StripeException: The expected signature was not found in the Stripe-Signature header`.
- **Test mode in the main account ≠ a sandbox account.** Stripe sandboxes are separate accounts with their own keys, products and endpoints. An endpoint in one account never receives events from another. Confirm which account a key belongs to with `curl https://api.stripe.com/v1/account -u "$KEY:"` (prints the `acct_…` id; never echo the key itself).
- **`stripe events resend` needs `--webhook-endpoint`.** Without it the command reports success and delivers nothing.
- **Signature failures are not in `traces`.** `logger.LogWarning(exception, …)` is emitted as `ExceptionTelemetry`, so query `exceptions | where type == 'Stripe.StripeException'`, not `traces | where message contains 'signature'`.
- **The apex is Cloudflare Pages, never App Service.** `api.realkevbase.com` is the only App Service hostname; the `api` CNAME must stay **DNS only** or the managed certificate cannot be issued.
- **`AzureEmail:SenderAddress` must exist as an ACS SenderUsername.** A `CustomerManaged` domain rejects any local part that was not created as a resource: `Invalid email sender username: 'noreply'`. Provisioned default is `DoNotReply`; `noreply` was created explicitly.
- **DKIM CNAMEs must be grey-clouded** in Cloudflare, and the apex must carry **exactly one** SPF TXT record; merging is required if another provider is added later.
- **`az webapp config ssl show|list` is broken in CLI 2.92** (`--name is only supported for Flex Consumption function apps`). Read the certificate with `az resource show --ids …/Microsoft.Web/certificates/<name>` and bind via the portal or `az webapp config ssl bind --certificate-thumbprint … --ssl-type SNI`.
- **Always On does not make restarts instant.** `alwaysOn: true` prevents idle unload, but a deploy/restart still cold-starts the container (~35 s measured). Retried requests during that window return `000`/timeout — poll until `404`, don't conclude the app is broken.
- **No `zip` binary on this machine.** Build deploy archives with Python's `zipfile`; `rm -rf` routes through a trash helper that errors on missing paths (wrap in `( … 2>/dev/null || true)`).
- **Wrangler's OAuth token has `zone (read)` but no DNS write** — every Cloudflare DNS change is a dashboard action.
- **App Insights component matters.** Several components exist in the resource group; the app writes to `realkevbase-platform202611071211` (match on `APPLICATIONINSIGHTS_CONNECTION_STRING`'s instrumentation key). Querying another component returns 0 rows and looks like "nothing is logged".
- **Vite inlines `VITE_API_BASE_URL` at build time.** A build without it produces a bundle with no API origin at all, and checkout then posts to the Pages domain. Always rebuild with the variable set.
- **User secrets load only in `Development`.** Running with `ASPNETCORE_ENVIRONMENT=Production` locally requires injected env vars (`Stripe__SecretKey`, `Stripe__WebhookSecret`, `AzureStorage__ConnectionString`, `AzureEmail__ConnectionString`).

---

## 8. What NOT to do

- **Do not bind the apex (`realkevbase.com`) to App Service.** It belongs to Cloudflare Pages; binding it breaks the storefront and the managed certificate.
- **Do not paste secret values into chat, logs, commits, issues or screenshots.** Refer to them by name and location. When a value must move between systems, pipe it inside a single shell command (`WH=$(…) && az … --settings "Stripe__WebhookSecret=$WH"`) and print only lengths, prefixes or hashes.
- **Do not recreate the Stripe webhook in the wrong account, and do not create it via the API.** It is a dashboard action in `acct_1UYhPSPm53HdvLrD` (sandbox) — verify with `stripe webhook_endpoints list` before assuming a secret will work.
- **Do not deploy without a rollback artifact.** App Service zip deploys have no git rollback: save the current `wwwroot` first (`https://realkevbase-platform-gkhed8h4dsadeyga.westus2-01.azurewebsites.net/api/zip/site/wwwroot`, authenticated with the publishing credentials) plus a dump of app settings, and note the current Pages deployment id for a one-click rollback in the Cloudflare dashboard.
- **Do not set `Stripe__ApiBase` in production.** Unset means the real Stripe API.
- **Do not reuse the `stripe listen` secret in production**, and do not "verify" a webhook by trusting a dashboard `200` alone — the handler returns `200` for any parseable event, so confirm with `requests` + `traces`/`exceptions`.
- **Do not add a second SPF record at the apex, and do not proxy the DKIM CNAMEs.**
- **Do not clear the buyer's cart after a successful payment** — a reopened receipt link would wipe a fresh cart.

---

## 9. Common operations

**Local API** (user secrets apply, port from `src/RealKevbase.Api/Properties/launchSettings.json`):

```bash
dotnet run --project src/RealKevbase.Api --urls http://localhost:5283
```

**Local SPA** — CORS allows only `http://localhost:5173`, so keep Vite's default port:

```bash
cd src/realkevbase-web && VITE_API_BASE_URL=http://localhost:5283 npm run dev
```

**Deploy backend**

```bash
cd ~/realkevbase-platform
SHA=$(git rev-parse --short HEAD)
dotnet publish src/RealKevbase.Api -c Release -o src/RealKevbase.Api/publish
python3 - "$SHA" <<'PY'
import sys, os, zipfile
sha = sys.argv[1]
os.chdir('src/RealKevbase.Api/publish')
with zipfile.ZipFile(f'/tmp/deploy-{sha}.zip', 'w', zipfile.ZIP_DEFLATED) as z:
    for root, _, files in os.walk('.'):
        for f in files:
            p = os.path.join(root, f)
            z.write(p, os.path.relpath(p, '.'))
PY
az webapp deploy -g realkevbase-rg -n realkevbase-platform \
  --src-path "/tmp/deploy-$SHA.zip" --type zip --timeout 600
# then poll until the app answers (restart costs ~35 s)
curl -s -o /dev/null -w "%{http_code}\n" https://api.realkevbase.com/api/checkout/sessions/cs_test_x/downloads
```

**Deploy frontend**

```bash
cd src/realkevbase-web
VITE_API_BASE_URL=https://api.realkevbase.com npm run build
npx --no-install wrangler pages deploy dist --project-name realkevbase-client --branch main
# verify the origin made it into the live bundle
curl -s https://realkevbase.com/ | grep -o 'assets/index-[A-Za-z0-9_-]*\.js'
```

**Rotate the webhook secret safely**

```bash
SID=abdf585f-250d-4b93-9e62-18dd07061bed
WH=$(python3 -c "import json,sys;print(json.load(open(sys.argv[1],encoding='utf-8-sig'))['Stripe:WebhookSecret'])" \
     "$HOME/.microsoft/usersecrets/$SID/secrets.json")
test ${#WH} -eq 38 || { echo "not a dashboard endpoint secret"; exit 1; }
az webapp config appsettings set -g realkevbase-rg -n realkevbase-platform \
  --settings "Stripe__WebhookSecret=$WH" -o none
az webapp restart -g realkevbase-rg -n realkevbase-platform
# prove the hand-off without printing the value
printf '%s' "$(az webapp config appsettings list -g realkevbase-rg -n realkevbase-platform \
  --query "[?name=='Stripe__WebhookSecret'].value" -o tsv)" | sha256sum
unset WH
```
