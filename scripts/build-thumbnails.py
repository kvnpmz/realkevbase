#!/usr/bin/env python3
"""Generate product thumbnails from the PDF generator's output folders.

Each output folder contains an ``images/`` directory holding up to four renders of the
same page: the full-page render (no suffix), plus ``_zoom``, ``_ipad`` and ``_a4``
variants. Some folders also contain numbered page artifacts (``foo-0.png``, ``foo-1.png``).

The full-page render is the thumbnail source. Its file name follows the generator
*template*, not the folder, so it can differ from the folder name and can repeat across
folders (``baby_registrylist`` and ``baby_registrylist_lightblue`` both render
``baby_registrylist.png``). Thumbnails are therefore renamed on the way out, using the
PDF's own file name, which is unique per product.

The extra renders are published beside the thumbnail as the product gallery, which the
storefront requests by slot: ``<stem>.webp`` (cover), ``<stem>-zoom.webp``,
``<stem>-ipad.webp`` and ``<stem>-a4.webp``. Variants a listing never rendered are simply
not uploaded; the API reports those slots as missing and the gallery drops them.

Renders are incremental: an image is rebuilt only when its target is missing, older
than the source PNG, or was produced at a different width. Uploads are incremental too —
one blob listing decides which files storage is missing, and only the files rebuilt in
this run plus those missing are sent. Keep the same ``--out`` between runs (the default
``/tmp/realkevbase-thumbs`` does) or everything is rebuilt from scratch.

Usage:
    python3 scripts/build-thumbnails.py                 # write to /tmp/realkevbase-thumbs
    python3 scripts/build-thumbnails.py --out build/thumbs
    python3 scripts/build-thumbnails.py --upload        # also push changed files to Azure
    python3 scripts/build-thumbnails.py --force         # rebuild everything
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
import shutil
import subprocess
import sys
import tempfile

from storage_auth import storage_arguments

DEFAULT_SOURCE = "zz_tp/output"
DEFAULT_OUT = "/tmp/realkevbase-thumbs"
DEFAULT_WIDTH = 500
DEFAULT_VARIANT_WIDTH = 800
DEFAULT_QUALITY = 82

VARIANT_SUFFIXES = ("_a4", "_ipad", "_zoom")

# Extra renders published as gallery images, in the order the API exposes them.
# Slot 0 (the cover thumbnail) is handled separately because it is always required.
GALLERY_VARIANTS = ("_zoom", "_ipad", "_a4")

# How those variants appear in the published file name.
GALLERY_SUFFIXES = tuple(variant.replace("_", "-") for variant in GALLERY_VARIANTS)


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
    parser.add_argument("--width", type=int, default=DEFAULT_WIDTH, help="cover width in px")
    parser.add_argument(
        "--variant-width",
        type=int,
        default=DEFAULT_VARIANT_WIDTH,
        help="gallery image width in px",
    )
    parser.add_argument("--quality", type=int, default=DEFAULT_QUALITY, help="WebP quality")
    parser.add_argument("--upload", action="store_true", help="upload to Azure Blob Storage")
    parser.add_argument("--container", default="pdfs", help="blob container (default: pdfs)")
    parser.add_argument("--prefix", default="guides/images", help="blob prefix for thumbnails")
    parser.add_argument("--check", action="store_true", help="report coverage without writing")
    parser.add_argument("--force", action="store_true", help="rebuild images that are already current")
    parser.add_argument(
        "--exclude",
        action="append",
        default=[],
        metavar="LISTING",
        help="skip a listing folder, such as a generator probe (repeatable)",
    )
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

    excluded = set(args.exclude)
    stems = {
        listing: stem
        for listing, stem in blob_stem_by_listing(args.source).items()
        if listing not in excluded
    }
    if not args.check:
        os.makedirs(args.out, exist_ok=True)

    built, rebuilt, current, covers, skipped, total_bytes = 0, 0, 0, 0, [], 0
    written: set[str] = set()

    def is_current(source: str, target: str, width: int) -> bool:
        """True when the WebP exists, is not older than its render and has the right width."""
        if args.force or not os.path.exists(target):
            return False
        if os.path.getmtime(target) < os.path.getmtime(source):
            return False
        try:
            with Image.open(source) as image:
                expected = min(width, image.width)
            with Image.open(target) as image:
                return image.width == expected
        except OSError:
            return False

    def write_webp(source: str, target: str, width: int) -> int:
        """Resize a render to WebP and return the bytes written."""
        with Image.open(source) as image:
            image = image.convert("RGB")
            width = min(width, image.width)
            height = round(image.height * width / image.width)
            image = image.resize((width, height), Image.LANCZOS)
            image.save(target, "WEBP", quality=args.quality, method=6)
        return os.path.getsize(target)

    for listing, stem in stems.items():
        images_dir = os.path.join(args.source, listing, "images")
        cover = find_base_image(images_dir)
        if cover is None:
            skipped.append(listing)
            continue

        # The renders share the template's stem, which is not always the folder name.
        source_stem = os.path.basename(cover)[: -len(".png")]
        renders = [(cover, "", args.width)]
        for variant in GALLERY_VARIANTS:
            variant_source = os.path.join(images_dir, f"{source_stem}{variant}.png")
            if os.path.exists(variant_source):
                renders.append((variant_source, variant.replace("_", "-"), args.variant_width))

        for source, suffix, width in renders:
            name = f"{stem}{suffix}.webp"
            target = os.path.join(args.out, name)
            built += 1

            if is_current(source, target, width):
                current += 1
                continue

            rebuilt += 1
            if args.check:
                continue

            size = write_webp(source, target, width)
            total_bytes += size
            written.add(name)
            print(f"  {name:52s} {width:4d}px  {size / 1024:5.0f} KB")

        covers += 1
        if args.check:
            extra = ", ".join(suffix or "cover" for _, suffix, _ in renders)
            print(f"  {listing:42s} -> {stem}.webp  ({extra})")

    label = "would build" if args.check else "built"
    print(
        f"\n{label} {rebuilt} of {built} images ({current} already current): "
        f"{covers} covers + {built - covers} gallery",
        end="",
    )
    if total_bytes:
        print(f", {total_bytes / 1024 / 1024:.1f} MB total", end="")
    print()

    if skipped:
        print(f"skipped {len(skipped)} folders with no full-page render: {', '.join(skipped)}")

    if args.check or not args.upload:
        return 0

    print(f"\nuploading to {args.container}/{args.prefix}")

    # One listing drives both decisions: which local files have no product (and must not
    # be published) and which published files storage is missing.
    auth, source_label = storage_arguments()
    print(f"credentials: {source_label}")
    published = list_published(args.container, args.prefix, auth)
    pdf_stems, stored = published if published else (None, None)

    local = [name for name in sorted(os.listdir(args.out)) if name.endswith(".webp")]
    if published is None:
        print("  could not list storage; every built image is a candidate")
    else:
        kept = []
        for name in local:
            if stem_of(name) in pdf_stems:
                kept.append(name)
            else:
                os.remove(os.path.join(args.out, name))
        print(
            f"  {len(pdf_stems)} published PDFs; removed "
            f"{len(local) - len(kept)} local image(s) with no product"
        )
        local = kept

    # Upload what changed: files rebuilt in this run, plus anything storage is missing.
    candidates = [
        name for name in local if name in written or stored is None or name not in stored
    ]
    if not candidates:
        print(f"  storage already matches all {len(local)} images; nothing to upload")
        return 0

    print(f"  uploading {len(candidates)} of {len(local)} images")

    staging = tempfile.mkdtemp(prefix="realkevbase-thumbs-")
    try:
        for name in candidates:
            shutil.copy2(os.path.join(args.out, name), os.path.join(staging, name))

        command = [
            "az", "storage", "blob", "upload-batch",
            "--destination", f"{args.container}/{args.prefix}",
            "--source", staging,
            "--pattern", "*.webp",
            "--content-type", "image/webp",
            "--content-cache-control", "public, max-age=86400",
            "--overwrite",
            "--no-progress",
        ]
        command += auth

        return subprocess.call(command)
    finally:
        shutil.rmtree(staging, ignore_errors=True)


def stem_of(name: str) -> str:
    """Strip ``.webp`` and any gallery suffix: ``x-zoom.webp`` -> ``x``."""
    stem = name[: -len(".webp")] if name.endswith(".webp") else name
    for suffix in GALLERY_SUFFIXES:
        if stem.endswith(suffix):
            return stem[: -len(suffix)]
    return stem


def list_published(
    container: str, prefix: str, auth: list[str]
) -> tuple[set[str], set[str]] | None:
    """List published PDF stems and published image names in one call.

    Args:
        container: Blob container holding the products.
        prefix: Blob prefix in front of the thumbnails (``guides/images``); its parent
            is where the PDFs live.
        auth: Authentication arguments for the az CLI.

    Returns:
        A tuple of PDF file stems and image file names (relative to the prefix), or
        ``None`` when the listing failed, in which case the caller uploads everything.
    """
    folder = prefix.rsplit("/", 1)[0] if "/" in prefix else ""
    lookup = f"{folder}/" if folder else ""
    image_prefix = prefix.rstrip("/") + "/"

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

    pdf_stems, images = set(), set()
    for name in result.stdout.split():
        if name.endswith(".pdf"):
            pdf_stems.add(os.path.basename(name)[: -len(".pdf")])
        elif name.startswith(image_prefix):
            images.add(name[len(image_prefix):])
    return pdf_stems, images

if __name__ == "__main__":
    sys.exit(main())
