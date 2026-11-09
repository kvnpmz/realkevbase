#!/usr/bin/env python3
"""Register a new PDF listing so it appears in the storefront.

A folder dropped into ``zz_tp/output/`` is invisible until several separate things
agree, and every mismatch fails silently. This script does the mechanical parts and
refuses to guess the editorial ones.

What it does:
  1. Finds the folder's full-page PDF (ignoring the _a4/_ipad/_zoom renders)
  2. Reads the display title from the generator config (``header``, else ``title``)
     and numbers it if that title is already taken ("Foo", then "Foo 2", "Foo 3")
  3. Inserts the listing row into the shipped SQLite database
  4. Uploads the PDF to Azure Blob Storage as ``guides/<slug>.pdf``
  5. Prints a ready-to-paste product entry, with a fresh stable GUID

What it deliberately does not do:
  - Choose a description, category or card colour (editorial decisions). Price is
    not asked for: every guide is $5, which is the default.
  - Edit InMemoryProductCatalog.cs (the entry is printed for you to paste)
  - Copy zz_tp/projects.db over the shipped database (see below)
  - Deploy; run ./scripts/deploy.sh afterwards

Why the source database is never copied:
  zz_tp/projects.db has no ``title`` column. The shipped database carries 65
  backfilled unique titles, so copying the source over it would silently blank every
  product name. New rows are INSERTed; the shipped database is the master.

Usage:
    python3 scripts/add-listing.py kidney_foodlist            # preview only
    python3 scripts/add-listing.py kidney_foodlist --apply     # write + upload
    python3 scripts/add-listing.py kidney_foodlist --apply --no-upload

After applying:
    .venv/bin/python scripts/build-thumbnails.py --upload
    # paste the printed entry into InMemoryProductCatalog.cs
    ./scripts/deploy.sh --yes
"""

from __future__ import annotations

import argparse
import os
import re
import sqlite3
import subprocess
import sys
import uuid

from storage_auth import storage_arguments

VARIANTS = ("_a4", "_ipad", "_zoom")
REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
# REALKEVBASE_DB lets tests point at a copy instead of the shipped database.
DATABASE = os.environ.get("REALKEVBASE_DB") or os.path.join(
    REPO_ROOT, "src", "RealKevbase.Api", "Data", "projects.db"
)
CATALOG = os.path.join(REPO_ROOT, "src", "RealKevbase.Core", "Services", "InMemoryProductCatalog.cs")
OUTPUT = os.path.join(REPO_ROOT, "zz_tp", "output")
DOCUMENTS = os.path.join(REPO_ROOT, "zz_tp", "documents")
CONTAINER = "pdfs"
BLOB_PREFIX = "guides"

CATEGORIES = ("moving-out", "healthy-living", "money-budgeting", "guides")

# Every digital PDF in the catalog is $5. Merch is priced individually, but this
# script only registers PDFs, so the price is a constant rather than a decision.
DEFAULT_PRICE = 5.0


def is_variant(stem: str) -> bool:
    """True for a device/zoom render or a numbered page artifact."""
    lowered = stem.lower()
    if any(lowered.endswith(suffix) for suffix in VARIANTS):
        return True
    tail = lowered.rsplit("-", 1)
    return len(tail) == 2 and tail[1].isdigit()


def find_source_pdf(folder: str) -> str | None:
    """Return the full-page PDF in an output folder, or None."""
    candidates = []
    for name in os.listdir(folder):
        if not name.lower().endswith(".pdf"):
            continue
        if is_variant(name[: -len(".pdf")]):
            continue
        path = os.path.join(folder, name)
        if os.path.getsize(path) < 1024:
            continue
        candidates.append(path)
    if not candidates:
        return None
    return max(candidates, key=os.path.getsize)


def read_config(path: str) -> dict[str, str]:
    """Pull the simple string fields out of a generator config.tl."""
    if not os.path.exists(path):
        return {}

    text = open(path, encoding="utf-8").read()
    fields = {}
    pattern = re.compile(r'^\s*(\w+)\s*=\s*"((?:[^"\\]|\\.)*)"', re.MULTILINE)
    for match in pattern.finditer(text):
        fields[match.group(1)] = match.group(2).replace('\\"', '"')
    return fields


def display_title(config: dict[str, str], listing_id: str) -> str:
    """Prefer the config header, then its title, then a titleised identifier."""
    for key in ("header", "title"):
        value = config.get(key, "").strip()
        if value:
            return value
    return listing_id.replace("_", " ").title()


def existing_titles(connection: sqlite3.Connection) -> dict[str, str]:
    """Map lowercased title to listing id, for uniqueness checks."""
    rows = connection.execute("SELECT id, title FROM Projects WHERE title IS NOT NULL")
    return {(title or "").strip().lower(): row_id for row_id, title in rows}


def unique_title(connection: sqlite3.Connection, base: str) -> tuple[str, str | None]:
    """Return a title that is not already in use, numbering later duplicates.

    Product cards are identified by title, so two identical titles read as the same
    product. Rather than blocking on a decision, the first listing keeps the plain
    title and subsequent ones are numbered — "Kidney Food List", then
    "Kidney Food List 2", "Kidney Food List 3", and so on.

    Args:
        connection: Open database connection.
        base: Desired title, usually from the generator config.

    Returns:
        A tuple of the title to use and, when numbering was applied, the title that
        was already taken; otherwise ``None`` as the second element.

    Raises:
        RuntimeError: When every candidate up to 100 is taken.
    """
    base = base.strip()
    existing = existing_titles(connection)

    if base.lower() not in existing:
        return base, None

    for number in range(2, 101):
        candidate = f"{base} {number}"
        if candidate.lower() not in existing:
            return candidate, base

    raise RuntimeError(f"could not find a free number for '{base}' within 100 attempts")


def fail(message: str) -> int:
    print(f"error: {message}", file=sys.stderr)
    return 1


def upload_pdf(pdf: str, blob_name: str) -> bool:
    """Upload a product PDF to blob storage, returning whether it succeeded.

    Credentials are resolved automatically by :mod:`storage_auth` — from an exported
    ``AZURE_STORAGE_CONNECTION_STRING`` or straight from the live App Service settings —
    so no secret is ever stored in a file or typed in by hand.

    Args:
        pdf: Local path of the PDF to upload.
        blob_name: Destination blob name inside the products container.

    Returns:
        True when the upload succeeded.
    """
    arguments, source = storage_arguments()

    command = [
        "az", "storage", "blob", "upload",
        "--container-name", CONTAINER,
        "--name", blob_name,
        "--file", pdf,
        "--content-type", "application/pdf",
        "--overwrite",
        "--no-progress",
        "--output", "none",
    ]
    command += arguments

    print(f"uploading to {CONTAINER}/{blob_name} (credentials: {source})")
    if subprocess.call(command) == 0:
        return True

    print(
        "\ncould not authenticate to blob storage.\n"
        "The script reads the connection string from App Service settings, which needs\n"
        "az to be logged in with permission to read them. Check with:\n"
        "  az webapp config appsettings list -g realkevbase-rg -n realkevbase-platform \\\n"
        "    --query \"[?name=='ConnectionStrings__AzureStorage'].value\" -o tsv",
        file=sys.stderr,
    )
    return False


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Register a new PDF listing from zz_tp/output/<folder>.",
    )
    parser.add_argument("folder", help="folder name under zz_tp/output")
    parser.add_argument("--apply", action="store_true", help="write the row and upload (default: preview)")
    parser.add_argument("--no-upload", action="store_true", help="skip the blob upload with --apply")
    parser.add_argument("--title", help="override the title read from config.tl (numbered if taken)")
    parser.add_argument(
        "--price",
        type=float,
        default=DEFAULT_PRICE,
        help=f"unit price for this PDF (default: {DEFAULT_PRICE}, the standard guide price)",
    )
    args = parser.parse_args()

    listing_id = args.folder
    source_dir = os.path.join(OUTPUT, listing_id)
    if not os.path.isdir(source_dir):
        return fail(f"no such output folder: {os.path.relpath(source_dir, REPO_ROOT)}")

    pdf = find_source_pdf(source_dir)
    if pdf is None:
        return fail("no full-page PDF found (only _a4/_ipad/_zoom renders?)")

    config = read_config(os.path.join(DOCUMENTS, listing_id, "config.tl"))
    title = (args.title or display_title(config, listing_id)).strip()
    slug = listing_id.replace("_", "-")
    blob_name = f"{BLOB_PREFIX}/{slug}.pdf"

    if not os.path.exists(DATABASE):
        return fail(f"database not found: {os.path.relpath(DATABASE, REPO_ROOT)}")

    connection = sqlite3.connect(DATABASE)
    try:
        columns = {row[1] for row in connection.execute("PRAGMA table_info(Projects)")}
        if "title" not in columns:
            return fail("the database has no 'title' column; refusing to write untitled rows")

        if connection.execute("SELECT 1 FROM Projects WHERE id = ?", (listing_id,)).fetchone():
            return fail(f"listing '{listing_id}' already exists in the database")

        try:
            title, numbered_from = unique_title(connection, title)
        except RuntimeError as error:
            return fail(str(error))

        catalog_text = open(CATALOG, encoding="utf-8").read()
        if blob_name in catalog_text:
            return fail(f"the catalog already sells '{blob_name}'")

        product_id = str(uuid.uuid4())
        template = config.get("template") or os.path.basename(pdf)[:-4]
        output_name = config.get("output_name") or template

        print(f"folder      : zz_tp/output/{listing_id}")
        print(f"pdf         : {os.path.basename(pdf)}  ({os.path.getsize(pdf) / 1024:.0f} KB)")
        print(f"listing id  : {listing_id}")
        print(f"title       : {title}")
        if numbered_from:
            print(f"              (auto-numbered: '{numbered_from}' was already taken)")
        print(f"template    : {template}")
        print(f"output_name : {output_name}")
        print(f"completed   : {config.get('completed_date', '') or '(none)'}")
        print(f"blob        : {CONTAINER}/{blob_name}")
        print(f"derived id  : {blob_name.rsplit('/', 1)[-1][:-4].replace('-', '_')}  (must equal the listing id)")

        if not args.apply:
            print("\npreview only. Re-run with --apply to write the row and upload the PDF.")
        else:
            # The row is written first but only committed once the upload succeeds, so a
            # failed upload cannot leave a product that sells a PDF that is not in storage.
            connection.execute(
                "INSERT INTO Projects "
                "(id, folder_path, completed_date, overrides, output_name, is_editable, template, title) "
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                (
                    listing_id,
                    f"/workspace/documents/{listing_id}",
                    config.get("completed_date") or None,
                    "",
                    output_name,
                    0,
                    template,
                    title,
                ),
            )

            if args.no_upload:
                connection.commit()
                print(f"\ninserted listing row '{listing_id}'")
                print("skipped blob upload (--no-upload); the row points at a PDF that is not in storage")
            else:
                if not upload_pdf(pdf, blob_name):
                    connection.rollback()
                    return fail("upload failed; the listing row was rolled back. Nothing was changed.")
                connection.commit()
                print(f"\ninserted listing row '{listing_id}'")
                print(f"uploaded {CONTAINER}/{blob_name}")
    finally:
        connection.close()

    print("\nPaste this into InMemoryProductCatalog.cs, then fill in the TODO fields:\n")
    print("            new()")
    print("            {")
    print(f'                Id = Guid.Parse("{product_id}"),')
    print(f'                Name = "{title}",')
    print('                Description = "TODO: one sentence for the card.",')
    print(f"                Price = {args.price:.1f}m,")
    print("                Fulfillment = FulfillmentType.DigitalPdf,")
    print(f'                Category = null,  // TODO: one of: {", ".join(CATEGORIES)}')
    print('                Accent = new ProductAccent("#FDF2F8", "#FBCFE8", "#BE185D"),  // TODO')
    print(f'                PdfBlobName = "{blob_name}"')
    print("            },")
    print("\nNext:")
    print("  1. paste the entry above and fill in the TODO fields")
    print("  2. .venv/bin/python scripts/build-thumbnails.py --upload")
    print("  3. ./scripts/deploy.sh --yes")
    return 0


if __name__ == "__main__":
    sys.exit(main())
