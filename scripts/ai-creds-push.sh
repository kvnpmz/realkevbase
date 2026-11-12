#!/usr/bin/env bash
#
# ai-creds-push - copy host Azure CLI and Wrangler credentials into a running container.
#
# Run this on the HOST, then run `ai-auth-check` inside the container.
#
#   ./scripts/ai-creds-push.sh                          # container: main-box-platform
#   AI_CONTAINER=my-box ./scripts/ai-creds-push.sh
#   AI_SKIP_WRANGLER=1 ./scripts/ai-creds-push.sh       # Azure only
#
# Install once for convenience:
#   install -m 755 scripts/ai-creds-push.sh ~/bin/ai-creds-push
#
# What it moves (never into the repo):
#   ~/.azure/msal_token_cache.json  access + refresh tokens
#   ~/.azure/azureProfile.json      subscription/tenant selection
#   ~/.azure/config                 CLI defaults (optional)
#   ~/.wrangler/config/default.toml Cloudflare OAuth token (or the XDG location)
#
# The token files are live credentials. They are copied into the container's HOME only,
# permissions are tightened to 600, and no secret is ever printed.

set -euo pipefail

CONTAINER="${AI_CONTAINER:-main-box-platform}"
HELPER_SRC="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/ai-auth-check.sh"

die() { printf 'error: %s\n' "$1" >&2; exit 1; }
info() { printf '==> %s\n' "$1"; }
warn() { printf 'warning: %s\n' "$1" >&2; }

command -v docker >/dev/null 2>&1 || die "docker not found on the host"
docker inspect "$CONTAINER" >/dev/null 2>&1 || die "container '$CONTAINER' is not running"

DEST_HOME="$(docker exec "$CONTAINER" sh -c 'printf %s "${HOME:-/root}"')"
[ -n "$DEST_HOME" ] || DEST_HOME=/root
info "container=$CONTAINER home=$DEST_HOME"

docker exec "$CONTAINER" sh -c 'mkdir -p "${HOME:-/root}/.azure"'

copied=0
for file in msal_token_cache.json msal_token_cache.bin azureProfile.json config; do
    if [ -f "$HOME/.azure/$file" ]; then
        docker cp "$HOME/.azure/$file" "$CONTAINER:$DEST_HOME/.azure/$file" >/dev/null
        copied=$((copied + 1))
    fi
done

[ "$copied" -gt 0 ] || die "no Azure credential files in $HOME/.azure - run 'az login' on the host first"
if [ ! -f "$HOME/.azure/msal_token_cache.json" ]; then
    warn "only msal_token_cache.bin found; az inside Linux containers reads .json and may reject it"
fi

if [ "${AI_SKIP_WRANGLER:-0}" != "1" ]; then
    wrangler_src=""
    for candidate in \
        "$HOME/.wrangler/config/default.toml" \
        "$HOME/.config/.wrangler/config/default.toml"; do
        if [ -f "$candidate" ]; then
            wrangler_src="$candidate"
            break
        fi
    done

    if [ -n "$wrangler_src" ]; then
        docker exec "$CONTAINER" sh -c 'mkdir -p "${HOME:-/root}/.wrangler/config"'
        docker cp "$wrangler_src" "$CONTAINER:$DEST_HOME/.wrangler/config/default.toml" >/dev/null
        info "copied wrangler token from $wrangler_src"
    else
        warn "no wrangler token on the host - run 'npx wrangler login' if the container needs it"
    fi
fi

# Tighten permissions inside the container; ownership is already the container user's.
docker exec "$CONTAINER" sh -c '
    cd "${HOME:-/root}" || exit 0
    [ -d .azure ] && chmod 700 .azure
    [ -f .azure/msal_token_cache.json ] && chmod 600 .azure/msal_token_cache.json
    [ -f .azure/msal_token_cache.bin ] && chmod 600 .azure/msal_token_cache.bin
    [ -f .azure/azureProfile.json ] && chmod 600 .azure/azureProfile.json
    [ -f .azure/config ] && chmod 600 .azure/config
    [ -f .wrangler/config/default.toml ] && chmod 600 .wrangler/config/default.toml
    exit 0
'

# Install the in-container verification helper so the next step is one word.
if [ -f "$HELPER_SRC" ]; then
    docker cp "$HELPER_SRC" "$CONTAINER:/usr/local/bin/ai-auth-check" >/dev/null
    docker exec "$CONTAINER" chmod 755 /usr/local/bin/ai-auth-check
    info "installed /usr/local/bin/ai-auth-check"
else
    warn "helper not found next to this script; skipped installing ai-auth-check"
fi

info "verifying inside the container"
docker exec "$CONTAINER" ai-auth-check \
    || warn "credentials were copied but the container cannot use them yet; run ai-auth-check there for detail"
