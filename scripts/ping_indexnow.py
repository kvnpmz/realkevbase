#!/usr/bin/env python3
"""Push the published sitemap to IndexNow so Bing, DuckDuckGo and Copilot fetch it now.

IndexNow is a bare protocol: no account and no token. Ownership is proven by a key file
served from the site root, so the key here lives next to the rest of the static assets and
ships with the normal web deploy. The key file has to be live before the first push, which
is why this script refuses to run against a key it cannot fetch back.

Google ignores IndexNow entirely — it has its own push channel in Search Console — so this
only shortens the wait on Bing-family engines, which index new domains far sooner.

Usage:
    python3 scripts/ping_indexnow.py                # show the URLs that would be pushed
    python3 scripts/ping_indexnow.py --apply        # push them
    python3 scripts/ping_indexnow.py --apply --sitemap https://realkevbase.com/sitemap.xml

Environment: none.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PUBLIC_DIR = ROOT / "src" / "realkevbase-web" / "public"
DEFAULT_SITE = "https://realkevbase.com"
ENDPOINT = "https://api.indexnow.org/indexnow"
KEY_PATTERN = re.compile(r"^[0-9a-f]{32}$")
LOC_PATTERN = re.compile(r"<loc>\s*([^<\s]+)\s*</loc>")
TIMEOUT = 30


def find_key(public_dir: Path = PUBLIC_DIR) -> tuple[str, Path]:
    """Returns the IndexNow key and the file that publishes it.

    The key file is the only 32-hex-character `.txt` in the static assets, so the key is
    discovered rather than duplicated in this script where it could drift from the file.
    """
    if not public_dir.is_dir():
        raise SystemExit(f"error: {public_dir} is missing")
    candidates = [path for path in public_dir.glob("*.txt") if KEY_PATTERN.match(path.stem)]
    if not candidates:
        raise SystemExit(
            f"error: no IndexNow key file in {public_dir}\n"
            "create one with: python3 -c 'import secrets; print(secrets.token_hex(16))'"
        )
    if len(candidates) > 1:
        names = ", ".join(sorted(path.name for path in candidates))
        raise SystemExit(f"error: more than one IndexNow key file: {names}")
    key_file = candidates[0]
    key = key_file.stem
    if key_file.read_text(encoding="utf-8").strip() != key:
        raise SystemExit(f"error: {key_file.name} must contain only the key {key}")
    return key, key_file


def fetch(url: str) -> str:
    """Fetches a URL as text, raising a readable error instead of a traceback."""
    request = urllib.request.Request(url, headers={"User-Agent": "realkevbase-indexnow/1.0"})
    with urllib.request.urlopen(request, timeout=TIMEOUT) as response:
        charset = response.headers.get_content_charset() or "utf-8"
        return response.read().decode(charset, "replace")


def sitemap_urls(sitemap: str) -> list[str]:
    """Extracts every <loc> from a sitemap, so the pushed list matches what Google reads."""
    urls = LOC_PATTERN.findall(fetch(sitemap))
    if not urls:
        raise SystemExit(f"error: no <loc> entries in {sitemap}")
    return urls


def check_key_live(site: str, key: str) -> None:
    """Confirms the key file is published, because an unverified key rejects the whole push."""
    url = f"{site}/{key}.txt"
    try:
        live = fetch(url).strip()
    except urllib.error.HTTPError as error:
        raise SystemExit(
            f"error: {url} returned HTTP {error.code}\n"
            "the key file has to be live before any URL is accepted — deploy the web bundle first"
        ) from None
    except urllib.error.URLError as error:
        raise SystemExit(f"error: cannot reach {url}: {error.reason}") from None
    if live != key:
        raise SystemExit(f"error: {url} does not contain the key {key}")


def submit(site: str, key: str, urls: list[str]) -> int:
    """Posts the URL list to IndexNow and returns the HTTP status it answered with."""
    payload = json.dumps(
        {
            "host": site.split("://", 1)[-1],
            "key": key,
            "keyLocation": f"{site}/{key}.txt",
            "urlList": urls,
        }
    ).encode("utf-8")
    request = urllib.request.Request(
        ENDPOINT,
        data=payload,
        headers={"Content-Type": "application/json; charset=utf-8"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=TIMEOUT) as response:
            return response.status
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", "replace").strip()
        raise SystemExit(f"error: IndexNow returned HTTP {error.code}: {detail}") from None


def main(argv: list[str] | None = None) -> int:
    """Entry point for the command line interface."""
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--site", default=DEFAULT_SITE, help=f"site origin (default {DEFAULT_SITE})")
    parser.add_argument(
        "--sitemap",
        default="",
        help="sitemap URL to read (default <site>/sitemap.xml)",
    )
    parser.add_argument("--apply", action="store_true", help="actually push (default is a dry run)")
    args = parser.parse_args(argv)

    site = args.site.rstrip("/")
    sitemap = args.sitemap or f"{site}/sitemap.xml"
    key, key_file = find_key()
    urls = sitemap_urls(sitemap)

    print(f"key      {key}  ({key_file.name})")
    print(f"sitemap  {sitemap}")
    print(f"urls     {len(urls)}")

    if not args.apply:
        for url in urls[:5]:
            print(f"  {url}")
        if len(urls) > 5:
            print(f"  ... and {len(urls) - 5} more")
        print("\ndry run — re-run with --apply to push")
        return 0

    check_key_live(site, key)
    status = submit(site, key, urls)
    print(f"\npushed {len(urls)} URLs — IndexNow answered HTTP {status}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
