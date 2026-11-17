#!/usr/bin/env python3
"""Authorise the Pinterest app once, then keep its token fresh.

Pinterest only hands out tokens to a human who approves the app in a browser. This script
prints that consent URL, catches the redirect on a loopback server, and swaps the code
for a token. The token is stored outside the repository, so no secret can be committed:

    $XDG_CONFIG_HOME/realkevbase/pinterest-token.json   (mode 0600)

The redirect URI must be registered on the app and match exactly. Add
``http://localhost:8787/callback`` under My apps -> Configure -> Redirect URIs, or pass a
different one with ``--redirect-uri``.

Requires two environment variables, never stored in the repo:

    PINTEREST_APP_ID       App ID from developers.pinterest.com
    PINTEREST_APP_SECRET   App secret for the same app

Usage:
    python3 scripts/pinterest_auth.py                  # consent, then fetch the account
    python3 scripts/pinterest_auth.py --manual         # same, pasting the redirect by hand
    python3 scripts/pinterest_auth.py --show           # what is stored, and when it expires
    python3 scripts/pinterest_auth.py --refresh        # renew now instead of waiting
"""

from __future__ import annotations

import argparse
import os
import secrets
import sys
import time
import urllib.parse
import webbrowser

from http.server import BaseHTTPRequestHandler, HTTPServer

from pinterest_api import (
    Pinterest,
    PinterestError,
    SCOPE_STRING,
    TokenStore,
    build_authorize_url,
    default_token_path,
)

DEFAULT_PORT = 8787
DEFAULT_PATH = "/callback"
DEFAULT_REDIRECT_URI = f"http://localhost:{DEFAULT_PORT}{DEFAULT_PATH}"
CONSENT_TIMEOUT_SECONDS = 300


class _CallbackHandler(BaseHTTPRequestHandler):
    """Receives Pinterest's redirect once and stores its query string on the server."""

    def do_GET(self) -> None:  # noqa: N802 - name required by BaseHTTPRequestHandler
        parsed = urllib.parse.urlparse(self.path)
        if parsed.path != self.server.expected_path:  # type: ignore[attr-defined]
            self._respond(404, "Not the Pinterest callback path.")
            return

        query = urllib.parse.parse_qs(parsed.query)
        self.server.result = query  # type: ignore[attr-defined]
        failed = query.get("error")
        if failed:
            self._respond(400, f"Pinterest refused the request: {failed[0]}")
        else:
            self._respond(200, "Pinterest is connected. You can close this window.")

    def _respond(self, status: int, message: str) -> None:
        body = f"<!doctype html><meta charset='utf-8'><title>SaveSimple</title><p>{message}".encode()
        self.send_response(status)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *args: object) -> None:
        """Keep the loopback server quiet; the CLI prints what matters."""


def _wait_for_redirect(port: int, path: str, timeout: int, redirect_uri: str) -> dict[str, list[str]]:
    """Serve requests until the callback arrives, ignoring favicon-style noise."""
    server = HTTPServer(("127.0.0.1", port), _CallbackHandler)
    server.expected_path = path  # type: ignore[attr-defined]
    server.result = {}  # type: ignore[attr-defined]
    deadline = time.time() + timeout
    print(f"waiting for Pinterest to redirect to {redirect_uri} (up to {timeout}s)…")

    try:
        while time.time() < deadline:
            server.timeout = max(0.5, deadline - time.time())
            server.handle_request()
            result = server.result  # type: ignore[attr-defined]
            if result:
                return result
    finally:
        server.server_close()

    raise SystemExit("timed out waiting for the redirect; re-run or use --manual")


def _manual_code(redirect_uri: str) -> str:
    """Read the code out of a redirect URL the operator pastes back."""
    pasted = input("Paste the full URL Pinterest redirected you to: ").strip()
    if not pasted:
        raise SystemExit("nothing pasted")
    query = urllib.parse.parse_qs(urllib.parse.urlparse(pasted).query)
    if "error" in query:
        raise SystemExit(f"Pinterest refused the request: {query['error'][0]}")
    code = query.get("code", [""])[0]
    if not code:
        raise SystemExit(f"no ?code= in that URL; it must be the {redirect_uri} redirect")
    return code


def _authorise(args: argparse.Namespace) -> int:
    """Run the consent flow, exchange the code, and record which account it belongs to."""
    app_id = os.environ.get("PINTEREST_APP_ID")
    if not app_id or not os.environ.get("PINTEREST_APP_SECRET"):
        print(
            "set PINTEREST_APP_ID and PINTEREST_APP_SECRET first — they are on\n"
            "https://developers.pinterest.com/apps/ under My apps -> Manage.",
            file=sys.stderr,
        )
        return 2

    state = secrets.token_urlsafe(24)
    url = build_authorize_url(app_id, args.redirect_uri, state)
    print("open this URL and approve access:\n")
    print(f"  {url}\n")
    print(f"requested scopes: {SCOPE_STRING}")

    if not args.manual and not args.no_browser:
        webbrowser.open(url)

    if args.manual:
        code = _manual_code(args.redirect_uri)
    else:
        path = urllib.parse.urlparse(args.redirect_uri).path or DEFAULT_PATH
        result = _wait_for_redirect(args.port, path, CONSENT_TIMEOUT_SECONDS, args.redirect_uri)
        if result.get("state", [""])[0] != state:
            print("the redirect did not carry the state this run sent; nothing was stored.", file=sys.stderr)
            return 1
        code = result.get("code", [""])[0]
        if not code:
            print(f"no authorization code in the redirect: {result}", file=sys.stderr)
            return 1

    client = Pinterest(args.token_file)
    client.exchange_code(code, args.redirect_uri)
    account = client.get_account()
    username = account.get("username", "")
    client.record_account(username)

    print(f"\nconnected as {username or 'unknown account'}")
    _describe_token(client.store)
    return 0


def _describe_token(store: TokenStore) -> None:
    """Print when the stored token expires, without revealing the token itself."""
    token = store.load()
    if not token.get("access_token"):
        print(f"no token stored at {store.path}")
        return

    def moment(key: str) -> str:
        value = token.get(key)
        return time.strftime("%Y-%m-%d %H:%M", time.localtime(int(value))) if value else "unknown"

    print(f"token file:  {store.path}")
    print(f"account:     {token.get('account') or 'unknown'}")
    print(f"scopes:      {token.get('scope') or 'unknown'}")
    print(f"access:      expires {moment('expires_at')}")
    print(f"refresh:     expires {moment('refresh_expires_at')}")


def main(argv: list[str] | None = None) -> int:
    """Parse arguments and run the requested token action."""
    parser = argparse.ArgumentParser(
        description="Authorise the Pinterest app and manage the stored token.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument("--token-file", help=f"token location (default {default_token_path()})")
    parser.add_argument("--redirect-uri", default=DEFAULT_REDIRECT_URI, help="must match the app setting exactly")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT, help="local port for the redirect listener")
    parser.add_argument("--manual", action="store_true", help="paste the redirect URL instead of using a local server")
    parser.add_argument("--no-browser", action="store_true", help="print the URL without opening a browser")
    parser.add_argument("--show", action="store_true", help="report the stored token and exit")
    parser.add_argument("--refresh", action="store_true", help="renew the access token now")
    args = parser.parse_args(argv)

    store = TokenStore(args.token_file)

    if args.show:
        _describe_token(store)
        return 0

    if args.refresh:
        try:
            client = Pinterest(args.token_file)
            client.refresh()
        except PinterestError as error:
            print(error, file=sys.stderr)
            return 1
        _describe_token(store)
        return 0

    try:
        return _authorise(args)
    except PinterestError as error:
        print(f"authorisation failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
