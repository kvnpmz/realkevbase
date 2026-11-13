#!/usr/bin/env python3
"""Publish the two gallery pages every listing shares.

``zz_tp/assets/guide.png`` ("getting your files") and ``disclaimer.png`` ("instant
download, no physical product") are Letter-shaped pages that belong on every product
detail page. They are stored once under ``guides/images/shared/`` instead of being
copied beside all 111 listings, and the API maps gallery slots 4 and 5 onto them.

The script is incremental: it lists storage once and re-uploads only a page that is
missing or older than the asset it comes from.

Usage:
    python3 scripts/publish-shared-images.py            # build and upload changed pages
    python3 scripts/publish-shared-images.py --check    # report what would change
    python3 scripts/publish-shared-images.py --force    # rebuild and upload both
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile

from datetime import datetime

from storage_auth import storage_arguments

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(REPO_ROOT, "zz_tp", "assets")
CONTAINER = "pdfs"
#: Blob prefix the API derives for shared slots; must match
#: ``AzureBlobProductImageService.SharedFolderName``.
PREFIX = "guides/images/shared"

#: Asset file name to the published WebP name, in gallery slot order.
PAGES = (("guide.png", "guide.webp"), ("disclaimer.png", "disclaimer.webp"))

#: Match the gallery renders build-thumbnails.py publishes, so the pages look native.
WIDTH = 800
QUALITY = 82


def published_pages() -> dict[str, float] | None:
    """List published shared pages as name to last-modified timestamp."""
    auth, _ = storage_arguments()
    command = [
        "az", "storage", "blob", "list",
        "--container-name", CONTAINER,
        "--prefix", PREFIX + "/",
        "--query", "[].{name:name,modified:properties.lastModified}",
        "-o", "json",
    ]
    command += auth

    try:
        result = subprocess.run(command, capture_output=True, text=True, timeout=180)
    except (OSError, subprocess.SubprocessError):
        return None

    if result.returncode != 0:
        return None

    try:
        entries = json.loads(result.stdout or "[]")
    except json.JSONDecodeError:
        return None

    published = {}
    for entry in entries:
        name = str(entry.get("name", "")).rsplit("/", 1)[-1]
        try:
            published[name] = datetime.fromisoformat(str(entry.get("modified"))).timestamp()
        except ValueError:
            published[name] = 0.0
    return published


def build(staging: str) -> list[tuple[str, float]]:
    """Render each asset to a WebP, returning name and source modification time."""
    from PIL import Image

    built = []
    for asset, name in PAGES:
        source = os.path.join(ASSETS, asset)
        if not os.path.exists(source):
            raise FileNotFoundError(source)

        with Image.open(source) as image:
            width, height = image.size
            page = image.convert("RGB").resize(
                (WIDTH, round(height * WIDTH / width)), Image.LANCZOS
            )
        page.save(os.path.join(staging, name), "WEBP", quality=QUALITY, method=6)
        built.append((name, os.path.getmtime(source)))
    return built


def upload(name: str, path: str) -> bool:
    """Upload one page, reusing the cached-control the gallery renders already have."""
    auth, source_label = storage_arguments()
    command = [
        "az", "storage", "blob", "upload",
        "--container-name", CONTAINER,
        "--name", f"{PREFIX}/{name}",
        "--file", path,
        "--content-type", "image/webp",
        "--content-cache-control", "public, max-age=86400",
        "--overwrite",
    ]
    command += auth

    print(f"  uploading {PREFIX}/{name} ({source_label})")
    result = subprocess.run(command, capture_output=True, text=True, timeout=180)
    if result.returncode != 0:
        print(f"  upload failed: {result.stderr.strip()[:300]}", file=sys.stderr)
    return result.returncode == 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="report changes without uploading")
    parser.add_argument("--force", action="store_true", help="rebuild and upload both pages")
    args = parser.parse_args()

    try:
        import PIL  # noqa: F401  (checked here so --help works without the venv)
    except ImportError:
        print(
            "error: Pillow is required. Create the repo-local environment and install it:\n"
            "  python3 -m venv .venv && .venv/bin/pip install -r scripts/requirements.txt\n"
            "then run: .venv/bin/python scripts/publish-shared-images.py",
            file=sys.stderr,
        )
        return 1

    stored = None if args.force else published_pages()

    staging = tempfile.mkdtemp(prefix="realkevbase-shared-")
    try:
        try:
            built = build(staging)
        except FileNotFoundError as missing:
            print(f"error: missing asset {missing}", file=sys.stderr)
            return 1

        pending = []
        for name, modified in built:
            if stored is None or name not in stored or modified > stored[name]:
                pending.append((name, os.path.join(staging, name)))

        if not pending:
            print(f"storage already matches both shared pages ({len(built)} files); nothing to upload")
            return 0

        if args.check:
            for name, _ in pending:
                print(f"  would upload {PREFIX}/{name}")
            return 0

        for name, path in pending:
            if not upload(name, path):
                return 1

        print(f"\nshared pages published: {len(pending)} of {len(built)}")
        return 0
    finally:
        shutil.rmtree(staging, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
