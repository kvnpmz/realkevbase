#!/bin/sh
#
# ai-auth-check - normalize credential permissions and report status inside the container.
#
# Run after the host has pushed credentials:
#   ai-creds-push            # on the host
#   ai-auth-check            # in the container
#
# Exits 0 when az is authenticated and a Cloudflare token is present, 1 otherwise.
# Never prints token material: only the account, tenant, and where the token lives.

set -u

home="${HOME:-/root}"
azure_dir="$home/.azure"
wrangler_cfg="$home/.wrangler/config/default.toml"
status=0

if [ -d "$azure_dir" ]; then
    chmod 700 "$azure_dir" 2>/dev/null
    for file in "$azure_dir"/msal_token_cache.json "$azure_dir"/msal_token_cache.bin \
                "$azure_dir"/azureProfile.json "$azure_dir"/config; do
        [ -f "$file" ] && chmod 600 "$file" 2>/dev/null
    done
fi
[ -f "$wrangler_cfg" ] && chmod 600 "$wrangler_cfg" 2>/dev/null

if command -v az >/dev/null 2>&1; then
    if az account show --query "user.name" -o tsv >/dev/null 2>&1; then
        printf 'az:        %s\n' "$(az account show --query 'user.name' -o tsv)"
        printf 'tenant:    %s\n' "$(az account show --query 'tenantId' -o tsv)"
        printf 'subscription: %s\n' "$(az account show --query 'name' -o tsv)"
    else
        printf 'az:        NOT authenticated (run ai-creds-push on the host, or az login here)\n'
        status=1
    fi
else
    printf 'az:        not installed\n'
    status=1
fi

if [ -f "$wrangler_cfg" ]; then
    printf 'wrangler:  token present (%s)\n' "$wrangler_cfg"
elif [ -n "${CLOUDFLARE_API_TOKEN:-}" ]; then
    printf 'wrangler:  CLOUDFLARE_API_TOKEN is set\n'
else
    printf 'wrangler:  no token found (run npx wrangler login on the host, or export CLOUDFLARE_API_TOKEN)\n'
    status=1
fi

exit "$status"
