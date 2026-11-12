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
#   -h, --help     show this help
#
# Environment overrides:
#   AZ_RG            resource group        (default: realkevbase-rg)
#   AZ_APP           app service name      (default: realkevbase-platform)
#   PAGES_PROJECT    pages project          (default: realkevbase-client)
#   PAGES_BRANCH     pages production branch (default: main)
#   API_BASE_URL     API origin, also baked into the bundle (default: https://api.realkevbase.com)
#   SITE_URL         static site origin     (default: https://realkevbase.com)
#   GA_MEASUREMENT_ID GA4 measurement id (G-...) baked into the bundle; optional
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
GA_MEASUREMENT_ID="${GA_MEASUREMENT_ID:-}"

# A healthy catalog has 68 products (65 PDFs + 3 merch). The floor only has to catch
# an empty or badly broken catalog, so adding products never breaks this script.
readonly MIN_PRODUCTS=60

ASSUME_YES=0
SKIP_VERIFY=0
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

  local web_dir="$REPO_ROOT/src/realkevbase-web"
  [ -d "$web_dir" ] || die "web project not found at $web_dir"

  # VITE_API_BASE_URL is inlined at build time. Without it the bundle falls back to
  # a same-origin API, which the static host answers with HTML, and the storefront
  # silently fails to load its catalog. Fail loudly instead.
  [ -n "$API_BASE_URL" ] || die "API_BASE_URL must be set so it can be baked into the bundle"

  ( cd "$web_dir" && npm run lint ) || die "eslint failed; fix before deploying"
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
pdfs = sum(1 for p in products if p.get("fulfillment") == "DigitalPdf")
problems = []

if count < minimum:
    problems.append(f"only {count} products (expected at least {minimum})")
if pdfs == 0:
    problems.append("no DigitalPdf products")
for probe in ("blob.core.windows.net", "sig=", "sv=", "PdfBlobName", "pdfBlobName"):
    if probe in raw:
        problems.append(f"storage detail leaked: {probe!r}")

if problems:
    print("; ".join(problems))
    sys.exit(1)

print(f"catalog shape ({count} products, {pdfs} PDFs) and no storage details")
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
  fi

  echo
  if [ "$FAILURES" -eq 0 ]; then
    printf '%sAll checks passed.%s\n' "$C_PASS" "$C_OFF"
    return 0
  fi
  printf '%s%d check(s) failed.%s\n' "$C_FAIL" "$FAILURES" "$C_OFF"
  return 1
}

# --- main -------------------------------------------------------------------

case "$TARGET" in
  verify) verify ;;
  api|web|all)
    ( cd "$REPO_ROOT" && git diff --quiet && git diff --cached --quiet ) \
      || printf 'warning: working tree has uncommitted changes\n'
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
