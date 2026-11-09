#!/usr/bin/env python3
"""Generate product thumbnails from the PDF generator's output folders.

Each output folder contains an ``images/`` directory holding four renders of the same
page: the full-page render (no suffix), plus ``_a4``, ``_ipad`` and ``_zoom`` variants.
Some folders also contain numbered page artifacts (``foo-0.png``, ``foo-1.png``).

The full-page render is the thumbnail source. Its file name follows the generator
*template*, not the folder, so it can differ from the folder name and can repeat across
folders (``baby_registrylist`` and ``baby_registrylist_lightblue`` both render
``baby_registrylist.png``). Thumbnails are therefore renamed on the way out, using the
PDF's own file name, which is unique per product.

Usage:
    python3 scripts/build-thumbnails.py                 # write to /tmp/realkevbase-thumbs
    python3 scripts/build-thumbnails.py --out build/thumbs
    python3 scripts/build-thumbnails.py --upload        # also push to Azure Blob Storage
    python3 scripts/build-thumbnails.py --check         # report coverage, write nothing

Requires Pillow. Create the repo-local environment once:

    python3 -m venv .venv
    .venv/bin/pip install -r scripts/requirements.txt

Then run it with that interpreter:

    .venv/bin/python scripts/build-thumbnails.py --check

The output file name must match what the API derives from the PDF blob name
(``guides/150first-foods.pdf`` -> ``guides/images/150first-foods.webp``), so uploading
is a matter of placing every ``*.webp`` into the ``guides/images`` prefix.
"""

from __future__ import annotations

import argparse
import os
import subprocess
import sys

DEFAULT_SOURCE = "zz_tp/output"
DEFAULT_OUT = "/tmp/realkevbase-thumbs"
DEFAULT_WIDTH = 500
DEFAULT_QUALITY = 82

VARIANT_SUFFIXES = ("_a4", "_ipad", "_zoom")


def is_variant(stem: str) -> bool:
    """True when a render is a device/zoom variant rather than the full page."""
    lowered = stem.lower()
    if any(lowered.endswith(suffix) for suffix in VARIANT_SUFFIXES):
        return True
    # Numbered page artifacts such as ``diverticulitis_foodchart-1``.
    tail = lowered.rsplit("-", 1)
    return len(tail) == 2 and tail[1].isdigit()


def find_base_image(images_dir: str) -> str | None:
    """Return the full-page render in an ``images/`` folder, or None."""
    if not os.path.isdir(images_dir):
        return None

    candidates = []
    for name in os.listdir(images_dir):
        if not name.lower().endswith(".png"):
            continue
        if is_variant(name[: -len(".png")]):
            continue
        path = os.path.join(images_dir, name)
        if os.path.getsize(path) < 4096:  # skip 1x1 placeholders
            continue
        candidates.append(path)

    if not candidates:
        return None
    # Deterministic when a folder holds more than one: the largest render wins.
    return max(candidates, key=os.path.getsize)


def blob_stem_by_listing(source: str) -> dict[str, str]:
    """Map listing id (folder name) to the PDF stem used in blob storage.

    The API derives the thumbnail name from the PDF blob name, where the generator
    wrote underscores as hyphens (``guides/150first-foods.pdf``). No published PDF
    name contains an underscore, so this is a faithful inverse.
    """
    mapping = {}
    for listing in sorted(os.listdir(source)):
        directory = os.path.join(source, listing)
        if not os.path.isdir(directory):
            continue
        mapping[listing] = listing.replace("_", "-")
    return mapping


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate product thumbnails.")
    parser.add_argument("--source", default=DEFAULT_SOURCE, help="generator output folder")
    parser.add_argument("--out", default=DEFAULT_OUT, help="destination for .webp files")
    parser.add_argument("--width", type=int, default=DEFAULT_WIDTH, help="thumbnail width in px")
    parser.add_argument("--quality", type=int, default=DEFAULT_QUALITY, help="WebP quality")
    parser.add_argument("--upload", action="store_true", help="upload to Azure Blob Storage")
    parser.add_argument("--container", default="pdfs", help="blob container (default: pdfs)")
    parser.add_argument("--prefix", default="guides/images", help="blob prefix for thumbnails")
    parser.add_argument("--check", action="store_true", help="report coverage without writing")
    args = parser.parse_args()

    if not os.path.isdir(args.source):
        print(f"error: source folder not found: {args.source}", file=sys.stderr)
        return 1

    try:
        from PIL import Image
    except ImportError:
        print(
            "error: Pillow is required. Create the repo-local environment and install it:\n"
            "  python3 -m venv .venv && .venv/bin/pip install -r scripts/requirements.txt\n"
            "then run: .venv/bin/python scripts/build-thumbnails.py",
            file=sys.stderr,
        )
        return 1

    stems = blob_stem_by_listing(args.source)
    if not args.check:
        os.makedirs(args.out, exist_ok=True)

    built, skipped, total_bytes = 0, [], 0

    for listing, stem in stems.items():
        images_dir = os.path.join(args.source, listing, "images")
        source_image = find_base_image(images_dir)
        if source_image is None:
            skipped.append(listing)
            continue

        target = os.path.join(args.out, f"{stem}.webp")
        if args.check:
            print(f"  {listing:42s} -> {stem}.webp  ({os.path.basename(source_image)})")
            built += 1
            continue

        with Image.open(source_image) as image:
            image = image.convert("RGB")
            width = min(args.width, image.width)
            height = round(image.height * width / image.width)
            image = image.resize((width, height), Image.LANCZOS)
            image.save(target, "WEBP", quality=args.quality, method=6)

        size = os.path.getsize(target)
        total_bytes += size
        built += 1
        print(f"  {stem}.webp  {width}x{height}  {size / 1024:.0f} KB")

    label = "would build" if args.check else "built"
    print(f"\n{label} {built} thumbnails", end="")
    if total_bytes:
        print(f", {total_bytes / 1024 / 1024:.1f} MB total", end="")
    print()

    if skipped:
        print(f"skipped {len(skipped)} folders with no full-page render: {', '.join(skipped)}")

    if args.check or not args.upload:
        return 0

    print(f"\nuploading to {args.container}/{args.prefix}")

    def storage_args() -> list[str]:
        """Authentication arguments for the az storage commands."""
        connection = os.environ.get("AZURE_STORAGE_CONNECTION_STRING")
        if connection:
            # Appended, not spliced in: an argument inserted mid-command makes the CLI
            # read the connection string as a subcommand name.
            return ["--connection-string", connection]
        return ["--auth-mode", "login"]

    # Only upload thumbnails that have a published PDF. The output folder also holds
    # generator scratch folders with no product, and those must not be published as
    # artwork — nothing would ever request them and they linger in storage.
    stems = published_pdf_stems(args.container, args.prefix, storage_args())
    if stems is None:
        print("  could not list published PDFs; uploading every built thumbnail")
    else:
        pruned = 0
        for name in sorted(os.listdir(args.out)):
            if name.endswith(".webp") and name[: -len(".webp")] not in stems:
                os.remove(os.path.join(args.out, name))
                pruned += 1
        print(f"  {len(stems)} published PDFs; skipped {pruned} thumbnail(s) with no product")

    command = [
        "az", "storage", "blob", "upload-batch",
        "--destination", f"{args.container}/{args.prefix}",
        "--source", args.out,
        "--pattern", "*.webp",
        "--content-type", "image/webp",
        "--content-cache-control", "public, max-age=86400",
        "--overwrite",
        "--no-progress",
    ]
    command += storage_args()

    return subprocess.call(command)


def published_pdf_stems(container: str, prefix: str, auth: list[str]) -> set[str] | None:
    """List the file stems of every PDF already published under a blob prefix.

    Args:
        container: Blob container holding the products.
        prefix: Blob prefix in front of the thumbnails (``guides/images``); its parent
            is where the PDFs live.
        auth: Authentication arguments for the az CLI.

    Returns:
        The stems, or ``None`` when the listing failed, in which case the caller
        falls back to uploading everything.
    """
    folder = prefix.rsplit("/", 1)[0] if "/" in prefix else ""
    lookup = f"{folder}/" if folder else ""

    command = [
        "az", "storage", "blob", "list",
        "--container-name", container,
        "--prefix", lookup,
        "--query", "[].name",
        "-o", "tsv",
    ]
    command += auth

    try:
        result = subprocess.run(command, capture_output=True, text=True, timeout=120)
    except (OSError, subprocess.SubprocessError):
        return None

    if result.returncode != 0:
        return None

    return {
        os.path.basename(name)[: -len(".pdf")]
        for name in result.stdout.split()
        if name.endswith(".pdf")
    }


if __name__ == "__main__":
    sys.exit(main())
