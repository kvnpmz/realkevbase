#!/bin/sh
#
# ai-auth-check - normalize credential permissions and report status inside the container.
#
# Run after the host has pushed credentials:
#   ai-creds-push            # on the host
#   ai-auth-check            # in the container
#   ai-auth-check --deep     # also validate the Cloudflare token over the network
#
# Exits 0 only when az can actually mint an access token and a Cloudflare token is present.
# `az account show` alone is not enough: the profile is cached, so it succeeds even after the
# token cache is stale. This script requests a real token to prove the copy worked.
#
# Never prints token material: only the account, tenant, and expiry.

set -u

home="${HOME:-/root}"
azure_dir="$home/.azure"
wrangler_cfg="$home/.wrangler/config/default.toml"
status=0
deep=0

[ "${1:-}" = "--deep" ] && deep=1

if [ -d "$azure_dir" ]; then
    chmod 700 "$azure_dir" 2>/dev/null
    for file in "$azure_dir"/msal_token_cache.json "$azure_dir"/msal_token_cache.bin \
                "$azure_dir"/azureProfile.json "$azure_dir"/config; do
        [ -f "$file" ] && chmod 600 "$file" 2>/dev/null
    done
fi
[ -f "$wrangler_cfg" ] && chmod 600 "$wrangler_cfg" 2>/dev/null

if ! command -v az >/dev/null 2>&1; then
    printf 'az:        not installed\n'
    status=1
elif ! az account show --query "user.name" -o tsv >/dev/null 2>&1; then
    printf 'az:        NOT authenticated (run ai-creds-push on the host, or az login here)\n'
    status=1
else
    printf 'az:        %s\n' "$(az account show --query 'user.name' -o tsv)"
    printf 'tenant:    %s\n' "$(az account show --query 'tenantId' -o tsv)"
    printf 'subscription: %s\n' "$(az account show --query 'name' -o tsv)"

    expiry="$(az account get-access-token --resource https://management.azure.com \
        --query expiresOn -o tsv 2>/dev/null)" || expiry=""
    if [ -n "$expiry" ]; then
        printf 'token:     valid until %s UTC\n' "$expiry"
    else
        printf 'token:     FAILED - credentials are stale; run az login on the host, then ai-creds-push\n'
        status=1
    fi
fi

if [ -f "$wrangler_cfg" ]; then
    printf 'wrangler:  token present (%s)\n' "$wrangler_cfg"
elif [ -n "${CLOUDFLARE_API_TOKEN:-}" ]; then
    printf 'wrangler:  CLOUDFLARE_API_TOKEN is set\n'
else
    printf 'wrangler:  no token found (run npx wrangler login on the host, or export CLOUDFLARE_API_TOKEN)\n'
    status=1
fi

if [ "$deep" -eq 1 ] && command -v npx >/dev/null 2>&1; then
    if npx --yes wrangler whoami >/dev/null 2>&1; then
        printf 'wrangler:  validated over the network (whoami ok)\n'
    else
        printf 'wrangler:  whoami FAILED - token is stale, run npx wrangler login on the host\n'
        status=1
    fi
fi

exit "$status"
