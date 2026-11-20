#!/usr/bin/env bash
#
# deploy.sh - build, deploy and verify the RealKevbase storefront.
#
#   ./scripts/deploy.sh              # API + web, then verify
#   ./scripts/deploy.sh api          # API only, then verify
#   ./scripts/deploy.sh web          # web only, then verify
#   ./scripts/deploy.sh verify       # no deploy, just the smoke test
#
# Flags:
#   --yes          skip the production confirmation prompt (required when stdin is not a TTY)
#   --skip-verify  deploy without running the smoke test
#   --skip-catalog deploy without checking that the catalog matches zz_tp
#   -h, --help     show this help
#
# Environment overrides:
#   AZ_RG            resource group        (default: realkevbase-rg)
#   AZ_APP           app service name      (default: realkevbase-platform)
#   PAGES_PROJECT    pages project          (default: realkevbase-client)
#   PAGES_BRANCH     pages production branch (default: main)
#   API_BASE_URL     API origin, also baked into the bundle (default: https://api.realkevbase.com)
#   SITE_URL         static site origin     (default: https://realkevbase.com)
#   GA_MEASUREMENT_ID GA4 measurement id (G-...) baked into the bundle (default: the
#                     storefront's own property; set it empty to ship without analytics)
#
# The API is deployed before the web bundle on purpose: a new frontend calling an
# endpoint that the old API does not serve yet shows an error in the storefront,
# while a new API behind an old frontend is harmless.
#
# Requires: dotnet, node/npm, az (logged in), npx wrangler (authenticated).

set -euo pipefail

readonly REPO_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"

AZ_RG="${AZ_RG:-realkevbase-rg}"
AZ_APP="${AZ_APP:-realkevbase-platform}"
PAGES_PROJECT="${PAGES_PROJECT:-realkevbase-client}"
PAGES_BRANCH="${PAGES_BRANCH:-main}"
API_BASE_URL="${API_BASE_URL:-https://api.realkevbase.com}"
SITE_URL="${SITE_URL:-https://realkevbase.com}"
# The measurement id is a public identifier (it sits in the HTML of every tagged site), so the
# storefront's own property is the default: a deploy must not silently ship a storefront that
# reports nothing, which is what an optional empty default did. `${VAR-default}` rather than
# `${VAR:-default}` so that setting the variable empty really does disable analytics.
GA_MEASUREMENT_ID="${GA_MEASUREMENT_ID-G-6FS9CWY5LB}"

# A healthy catalog has 114 products (111 PDFs + 3 merch). The floor only has to catch
# an empty or badly broken catalog, so adding products never breaks this script.
readonly MIN_PRODUCTS=60

ASSUME_YES=0
SKIP_VERIFY=0
SKIP_CATALOG=0
FAILURES=0
TARGET="all"

if [ -t 1 ]; then
  C_PASS=$'\033[32m'; C_FAIL=$'\033[31m'; C_INFO=$'\033[36m'; C_OFF=$'\033[0m'
else
  C_PASS=''; C_FAIL=''; C_INFO=''; C_OFF=''
fi

info() { printf '%s==>%s %s\n' "$C_INFO" "$C_OFF" "$1"; }
pass() { printf '  %sPASS%s %s\n' "$C_PASS" "$C_OFF" "$1"; }
fail() { printf '  %sFAIL%s %s\n' "$C_FAIL" "$C_OFF" "$1"; FAILURES=$((FAILURES + 1)); }
die()  { printf '%sERROR:%s %s\n' "$C_FAIL" "$C_OFF" "$1" >&2; exit 1; }

usage() { awk 'NR > 2 && /^#/ { sub(/^# ?/, ""); print; next } NR > 2 { exit }' "${BASH_SOURCE[0]}"; }

# --- argument parsing -------------------------------------------------------

while [ $# -gt 0 ]; do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    --yes|-y) ASSUME_YES=1 ;;
    --skip-verify) SKIP_VERIFY=1 ;;
    --skip-catalog) SKIP_CATALOG=1 ;;
    api|web|all|verify) TARGET="$1" ;;
    *) die "unknown argument '$1' (try --help)" ;;
  esac
  shift
done

# --- tool resolution --------------------------------------------------------
#
# Containers often have a broken /usr/bin/npm or keep the SDK outside PATH, so each
# tool is looked up on PATH first and then in the locations this project is built in.

have() { command -v "$1" >/dev/null 2>&1; }

resolve_dotnet() {
  if have dotnet && dotnet --version >/dev/null 2>&1; then return 0; fi
  local candidate
  for candidate in /opt/dotnet/dotnet "${HOME}/.dotnet/dotnet"; do
    if [ -x "$candidate" ]; then
      export PATH="$(dirname "$candidate"):$PATH"
      dotnet --version >/dev/null 2>&1 && return 0
    fi
  done
  return 1
}

resolve_node() {
  if have node && have npm && npm --version >/dev/null 2>&1; then return 0; fi
  local dir
  for dir in /tmp/node-*/bin "${HOME}"/.nvm/versions/node/*/bin /usr/local/bin; do
    if [ -x "$dir/npm" ] && [ -x "$dir/node" ] && "$dir/npm" --version >/dev/null 2>&1; then
      export PATH="$dir:$PATH"
      return 0
    fi
  done
  return 1
}

# --- helpers ----------------------------------------------------------------

confirm() {
  [ "$ASSUME_YES" -eq 1 ] && return 0
  [ -t 0 ] || die "refusing to deploy to production in a non-interactive shell; pass --yes"
  local answer
  printf 'Deploy to production? API=%s (%s)  web=%s -> %s [y/N] ' \
    "$AZ_APP" "$AZ_RG" "$PAGES_PROJECT" "$SITE_URL"
  read -r answer
  case "$answer" in y|Y|yes|YES) return 0 ;; *) die "aborted" ;; esac
}

check_status() { # label method want_code url [json_body]
  local label="$1" method="$2" want="$3" url="$4"
  local data="${5-}" got
  if [ "$method" = POST ]; then
    [ -n "$data" ] || data='{}'
    got=$(curl -s -o /dev/null -w '%{http_code}' --max-time 60 -X POST "$url" \
      -H 'Content-Type: application/json' -d "$data" 2>/dev/null) || got=000
  else
    got=$(curl -s -o /dev/null -w '%{http_code}' --max-time 60 "$url" 2>/dev/null) || got=000
  fi
  [ -n "$got" ] || got=000
  if [ "$got" = "$want" ]; then pass "$label (HTTP $got)"; else fail "$label (wanted $want, got $got)"; fi
}

wait_for_api() {
  info "waiting for $API_BASE_URL to answer"
  local attempt code
  for attempt in $(seq 1 40); do
    code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 15 "$API_BASE_URL/api/products" 2>/dev/null || echo 000)
    if [ "$code" = "200" ]; then
      printf '  up after ~%ss\n' "$((attempt * 5))"
      return 0
    fi
    sleep 5
  done
  return 1
}

# --- deploy: API ------------------------------------------------------------

deploy_api() {
  info "publishing API"
  resolve_dotnet || die "dotnet SDK not found (install it or set PATH)"
  have az || die "az CLI not found"

  local out="/tmp/realkevbase-api-publish" archive="/tmp/realkevbase-api.zip"
  rm -rf "$out" "$archive"

  ( cd "$REPO_ROOT" && dotnet publish src/RealKevbase.Api/RealKevbase.Api.csproj \
      -c Release -o "$out" ) || die "dotnet publish failed"

  # `zip` is not installed everywhere; Python's zipfile is always available.
  python3 - "$out" "$archive" <<'PY' || die "failed to create the deployment archive"
import os, shutil, sys
src, archive = sys.argv[1], sys.argv[2]
base = archive[:-4] if archive.endswith(".zip") else archive
shutil.make_archive(base, "zip", src)
if not os.path.exists(archive):
    sys.exit(1)
PY

  # The listings database ships as content; without it the API cannot tell which
  # PDFs have actually been published, so catalog filtering silently stops.
  python3 - "$archive" <<'PY' || die "Data/projects.db missing from the deployment archive"
import sys, zipfile
with zipfile.ZipFile(sys.argv[1]) as z:
    sys.exit(0 if any(n.endswith("Data/projects.db") for n in z.namelist()) else 1)
PY

  info "deploying API to $AZ_APP ($AZ_RG)"
  az webapp deploy -g "$AZ_RG" -n "$AZ_APP" --src-path "$archive" --type zip -o none \
    || die "az webapp deploy failed"

  wait_for_api || die "API did not become healthy; check the app logs"
}

# --- deploy: web ------------------------------------------------------------

deploy_web() {
  info "building web bundle (VITE_API_BASE_URL=$API_BASE_URL)"
  resolve_node || die "node/npm not found (install Node or set PATH)"

  # `scripts/generate-seo.mjs` imports the copy modules in `src/data/*.ts` directly, which
  # only works once Node strips types without a flag (>= 22.18). An older runtime fails
  # after `vite build` has already written dist/, leaving a half-prerendered site; catch it
  # here instead. `package.json` carries the same floor in `engines`.
  local node_major node_minor
  node_major="$(node -p 'process.versions.node.split(".")[0]' 2>/dev/null || echo 0)"
  node_minor="$(node -p 'process.versions.node.split(".")[1]' 2>/dev/null || echo 0)"
  if [ "$node_major" -lt 22 ] || { [ "$node_major" -eq 22 ] && [ "$node_minor" -lt 18 ]; }; then
    die "node $(node --version 2>/dev/null) is too old: the SEO step imports src/data/*.ts and needs node >= 22.18"
  fi

  local web_dir="$REPO_ROOT/src/realkevbase-web"
  [ -d "$web_dir" ] || die "web project not found at $web_dir"

  # VITE_API_BASE_URL is inlined at build time. Without it the bundle falls back to
  # a same-origin API, which the static host answers with HTML, and the storefront
  # silently fails to load its catalog. Fail loudly instead.
  [ -n "$API_BASE_URL" ] || die "API_BASE_URL must be set so it can be baked into the bundle"

  # Two gates, two scopes: eslint covers .js/.mjs (the Cloudflare functions and the build
  # scripts), while the SPA is TypeScript, where typescript-eslint cannot be installed
  # beside TypeScript 7 — tsc is the only checker for src/. Neither replaces the other.
  ( cd "$web_dir" && npm run lint ) || die "eslint failed; fix before deploying"
  ( cd "$web_dir" && npm run typecheck ) || die "tsc failed; fix the types before deploying"
  ( cd "$web_dir" && VITE_API_BASE_URL="$API_BASE_URL" \
      VITE_GA_ID="$GA_MEASUREMENT_ID" \
      npm run build ) || die "vite build failed"

  # Analytics is optional; when an id is supplied, prove it reached the bundle rather than
  # silently shipping a storefront that reports nothing.
  if [ -n "$GA_MEASUREMENT_ID" ]; then
    grep -rq "$GA_MEASUREMENT_ID" "$web_dir/dist/assets" \
      || die "GA_MEASUREMENT_ID=$GA_MEASUREMENT_ID was not baked into the bundle"
  fi

  if grep -rq 'downloads/pdf' "$web_dir/dist/assets" 2>/dev/null; then
    die "the built bundle still references the removed /downloads/pdf endpoint"
  fi

  info "deploying web bundle to Pages project $PAGES_PROJECT"
  ( cd "$web_dir" && npx wrangler pages deploy dist \
      --project-name "$PAGES_PROJECT" --branch "$PAGES_BRANCH" --commit-dirty=true ) \
    || die "wrangler pages deploy failed"
}

# --- verify -----------------------------------------------------------------

verify() {
  FAILURES=0
  info "verifying $API_BASE_URL"

  # 1. The paywall invariant. This endpoint minted free download links and must never
  #    come back; a successful deploy cannot detect its return on its own.
  check_status "paywall: unauthenticated link endpoint is gone" \
    POST 404 "$API_BASE_URL/api/checkout/downloads/pdf" \
    '{"productId":"00000000-0000-0000-0000-000000000000"}'

  # 2. Template scaffolding must not be reachable.
  check_status "scaffold endpoint /weatherforecast is gone" GET 404 "$API_BASE_URL/weatherforecast"

  # 3. Catalog still serves.
  check_status "catalog responds" GET 200 "$API_BASE_URL/api/products"

  # 4. Catalog shape, and no storage details leaking into the payload.
  local body
  body=$(mktemp)
  if curl -s --max-time 60 "$API_BASE_URL/api/products" -o "$body"; then
    local catalog_detail
    if catalog_detail=$(python3 - "$body" "$MIN_PRODUCTS" <<'PY'
import json, sys

path, minimum = sys.argv[1], int(sys.argv[2])
raw = open(path, encoding="utf-8").read()

try:
    products = json.loads(raw)
except Exception as exc:
    print(f"payload is not valid JSON ({exc})")
    sys.exit(1)

count = len(products)
digital = [p for p in products if p.get("fulfillment") == "DigitalPdf"]
problems = []

if count < minimum:
    problems.append(f"only {count} products (expected at least {minimum})")
if not digital:
    problems.append("no DigitalPdf products")
for probe in ("blob.core.windows.net", "sig=", "sv=", "PdfBlobName", "pdfBlobName"):
    if probe in raw:
        problems.append(f"storage detail leaked: {probe!r}")

# Paper sizes must come from the API, never from storefront copy. A null list is honest
# (storage could not be listed), but a wrong shape means the deployed build is stale.
for product in digital:
    sizes = product.get("pageSizes")
    if not isinstance(sizes, list) or sizes[:1] != ["US Letter"]:
        problems.append(f"{product.get('name')!r} does not report page sizes like ['US Letter']")
        break

# The A4 preview must never outlive the edition it illustrates: a listing may have the
# render without the PDF (it is then withheld), but never the other way round.
a4 = [p for p in digital if "A4" in (p.get("pageSizes") or [])]
for product in digital:
    if 3 in (product.get("gallerySlots") or []) and product not in a4:
        problems.append(f"{product.get('name')!r} serves an A4 preview without the edition")
        break

if problems:
    print("; ".join(problems))
    sys.exit(1)

listed = sum(1 for p in digital if isinstance(p.get("gallerySlots"), list))
print(f"catalog shape ({count} products, {len(digital)} PDFs, {listed} with a gallery manifest, "
      f"{len(a4)} A4 editions), page sizes, and no storage details")
PY
    ); then
      pass "$catalog_detail"
    else
      fail "catalog: $catalog_detail"
    fi
  else
    fail "could not fetch the catalog"
  fi
  rm -f "$body"

  # 5. Baseline headers.
  local headers header_code
  headers=$(mktemp)
  header_code=$(curl -s -o /dev/null -D "$headers" -w '%{http_code}' --max-time 60 \
    "$API_BASE_URL/api/products" 2>/dev/null) || header_code=000
  if [ "$header_code" != "200" ]; then
    fail "security headers: could not read a response (HTTP $header_code)"
  else
    local missing=""
    for header in x-content-type-options x-frame-options referrer-policy strict-transport-security; do
      grep -qi "^${header}:" "$headers" || missing="$missing $header"
    done
    if [ -z "$missing" ]; then
      pass "security headers present"
    else
      fail "security headers missing:$missing"
    fi
    if grep -qi '^server:' "$headers"; then
      fail "Server header advertised ($(grep -i '^server:' "$headers" | tr -d '\r'))"
    else
      pass "Server header suppressed"
    fi
  fi
  rm -f "$headers"

  # 6. Quantity cap rejects absurd carts without calling Stripe.
  check_status "quantity cap rejects 21" \
    POST 400 "$API_BASE_URL/api/checkout/sessions" \
    '{"items":[{"productId":"00000000-0000-0000-0000-000000000000","quantity":21}]}'

  # 7. Product artwork serves, and stays free of storage details.
  local thumb
  thumb=$(curl -s --max-time 60 "$API_BASE_URL/api/products" | python3 -c "
import json, sys
try:
    products = json.load(sys.stdin)
except Exception:
    print('')
    raise SystemExit(0)
urls = [p.get('thumbnailUrl') for p in products if isinstance(p, dict) and p.get('thumbnailUrl')]
print(urls[0] if urls else '')
")
  if [ -z "$thumb" ]; then
    fail "no product exposes a thumbnailUrl"
  else
    check_status "product artwork serves" GET 200 "$API_BASE_URL$thumb"
    if printf '%s' "$thumb" | grep -qiE 'blob\.core|sig=|sv='; then
      fail "thumbnailUrl leaks storage details: $thumb"
    else
      pass "thumbnailUrl exposes no storage details"
    fi
  fi

  # 8. The site serves the bundle that was just built.
  check_status "storefront responds" GET 200 "$SITE_URL/"
  local live_bundle built_bundle
  live_bundle=$(curl -s --max-time 60 "$SITE_URL/" | grep -oE 'assets/index-[A-Za-z0-9_-]+\.js' | head -1 || true)
  built_bundle=""
  if [ -d "$REPO_ROOT/src/realkevbase-web/dist/assets" ]; then
    built_bundle=$(cd "$REPO_ROOT/src/realkevbase-web/dist" && ls assets/index-*.js 2>/dev/null | head -1 || true)
  fi
  if [ -z "$live_bundle" ]; then
    fail "could not find a bundle reference in the live index.html"
  elif [ -n "$built_bundle" ] && [ "$live_bundle" != "$built_bundle" ]; then
    fail "live bundle ($live_bundle) is not the one just built ($built_bundle)"
  else
    pass "live bundle is current ($live_bundle)"
    check_status "bundle asset is fetchable" GET 200 "$SITE_URL/$live_bundle"

    # 8b. Analytics has to be in the bundle that is actually live. The measurement id comes
    #     from the deploy environment, and until now a deploy that forgot it shipped a
    #     storefront that reported nothing while every other check passed.
    if [ -n "$GA_MEASUREMENT_ID" ]; then
      if curl -sf --max-time 60 "$SITE_URL/$live_bundle" | grep -q "$GA_MEASUREMENT_ID"; then
        pass "GA4 $GA_MEASUREMENT_ID is in the live bundle"
      else
        fail "GA4 $GA_MEASUREMENT_ID is missing from the live bundle"
      fi
    fi
  fi

  # 9. Client-side routes must serve the app, not the host's 404. This is the one way a
  #    shared guide link can break while the storefront itself still looks healthy.
  check_status "guide deep link serves the app" \
    GET 200 "$SITE_URL/guides/apartment-checklist"

  # 10. Search metadata is generated at build time, and a 200 cannot prove it exists: the
  #     SPA fallback answers every path, so a skipped prerender would still look healthy
  #     while every guide shared one generic title and one canonical URL.
  local seo_dir
  seo_dir=$(mktemp -d)

  if curl -sf --max-time 60 "$SITE_URL/guides/apartment-checklist" -o "$seo_dir/guide.html"; then
    if grep -q '<title>SaveSimple by RealKevbase — checklists' "$seo_dir/guide.html"; then
      fail "guide page serves the generic shell title (prerender did not run)"
    elif grep -q "rel=\"canonical\" href=\"$SITE_URL/guides/apartment-checklist\"" "$seo_dir/guide.html" \
      && grep -q '"@type": "Product"' "$seo_dir/guide.html"; then
      pass "guide page carries its own title, canonical URL and Product JSON-LD"
    else
      fail "guide page is missing its canonical link or Product JSON-LD"
    fi
  else
    fail "could not fetch a prerendered guide page"
  fi

  if curl -sf --max-time 60 "$SITE_URL/robots.txt" -o "$seo_dir/robots.txt" \
    && grep -qi '^Sitemap:' "$seo_dir/robots.txt" \
    && ! grep -qi '<html' "$seo_dir/robots.txt"; then
    pass "robots.txt is plain text and names the sitemap"
  else
    fail "robots.txt is missing, HTML, or does not name the sitemap"
  fi

  if curl -sf --max-time 60 "$SITE_URL/sitemap.xml" -o "$seo_dir/sitemap.xml" \
    && grep -q '<urlset' "$seo_dir/sitemap.xml"; then
    local sitemap_urls
    sitemap_urls=$(grep -c '<loc>' "$seo_dir/sitemap.xml" || true)
    if [ "${sitemap_urls:-0}" -ge "$MIN_PRODUCTS" ]; then
      pass "sitemap lists $sitemap_urls URLs"
    else
      fail "sitemap lists only ${sitemap_urls:-0} URLs"
    fi
  else
    fail "sitemap.xml is missing or not XML"
  fi

  if curl -sf --max-time 60 "$SITE_URL/" -o "$seo_dir/home.html" \
    && grep -q "rel=\"canonical\" href=\"$SITE_URL/\"" "$seo_dir/home.html"; then
    pass "home page declares $SITE_URL as canonical"
  else
    fail "home page has no canonical link for $SITE_URL"
  fi

  # 11. www is a second custom domain on the same Pages project. Without the redirect,
  #     Google keeps indexing both hosts (and their http variants) as separate sites.
  if [[ "$SITE_URL" == https://* ]]; then
    local www_url www_code www_target
    www_url="https://www.${SITE_URL#https://}"
    www_code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 60 "$www_url/" || echo 000)
    www_target=$(curl -s -o /dev/null -w '%{redirect_url}' --max-time 60 "$www_url/" || true)
    if [ "$www_code" = "301" ] && [ "$www_target" = "$SITE_URL/" ]; then
      pass "www host redirects to the apex (301)"
    else
      fail "www host does not redirect to the apex (HTTP ${www_code:-000}, target '${www_target:-none}')"
    fi
  fi

  rm -rf "$seo_dir"

  echo
  if [ "$FAILURES" -eq 0 ]; then
    printf '%sAll checks passed.%s\n' "$C_PASS" "$C_OFF"
    return 0
  fi
  printf '%s%d check(s) failed.%s\n' "$C_FAIL" "$FAILURES" "$C_OFF"
  return 1
}

# --- preflight: catalog ------------------------------------------------------
#
# A folder in zz_tp/output that is not in the database and the catalog is invisible on
# the site, and adding it by hand is where listings get missed. The sync script knows
# how to register one, so refuse to ship a catalog that is out of date instead of
# discovering it after the deploy.

preflight_catalog() {
  info "checking the catalog matches zz_tp"
  local report pending
  report=$(mktemp)
  if ! python3 "$REPO_ROOT/scripts/sync-catalog.py" --json >"$report"; then
    rm -f "$report"
    die "catalog sync check failed; run python3 scripts/sync-catalog.py for details"
  fi

  pending=$(python3 - "$report" <<'PY'
import json
import sys

plan = json.load(open(sys.argv[1], encoding="utf-8"))
changes = len(plan["new"]) + len(plan["renamed"]) + len(plan["redescribed"])
if changes:
    names = [f"register {item['slug']}" for item in plan["new"][:5]]
    names += [f"rename {item['slug']}" for item in plan["renamed"][:5]]
    print(f"{changes}|{'; '.join(names)}")
PY
)
  rm -f "$report"

  if [ -n "$pending" ]; then
    die "catalog is out of date (${pending%%|*} change(s): ${pending#*|}); run python3 scripts/sync-catalog.py --apply"
  fi
  pass "catalog matches zz_tp"
}

# --- main -------------------------------------------------------------------

case "$TARGET" in
  verify) verify ;;
  api|web|all)
    ( cd "$REPO_ROOT" && git diff --quiet && git diff --cached --quiet ) \
      || printf 'warning: working tree has uncommitted changes\n'
    if [ "$SKIP_CATALOG" -eq 1 ]; then info "skipping the catalog check"; else preflight_catalog; fi
    confirm
    [ "$TARGET" = "web" ] || deploy_api
    [ "$TARGET" = "api" ] || deploy_web
    if [ "$SKIP_VERIFY" -eq 1 ]; then
      info "skipping verification"
    else
      verify
    fi
    ;;
esac
