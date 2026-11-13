#!/usr/bin/env python3
"""Reconcile the storefront catalog with the finished guides in ``zz_tp``.

Three places have to agree before a guide can be sold:

  * ``zz_tp/output/<listing>/`` — the rendered PDF (plus its image renders)
  * ``src/RealKevbase.Api/Data/projects.db`` — the listing row (title, template)
  * ``src/RealKevbase.Core/Services/InMemoryProductCatalog.cs`` — the product the
    API (and therefore the SPA) reads

A folder that exists in one and not the others is invisible on the site, and every
mismatch fails silently. This script diffs all three and repairs the mechanical parts:

  1. Registers unlisted folders — database row, PDF upload and catalog entry — with a
     product GUID derived from the listing id, so a re-run produces the same product
     instead of a duplicate.
  2. Refreshes names and descriptions from ``zz_tp/documents/<listing>/listing.txt``,
     the canonical shop copy. Only the first pipe segment becomes the product name;
     colour editions keep their ``— Green``-style suffix.
  3. Uses ``scripts/catalog_meta.json`` for the two things a title cannot express:
     category and card colour. Anything absent from it is inferred from the title.
  4. Reports folders with no entry, entries with no folder and rows with no folder.
     Nothing is ever deleted.

Preview is the default; ``--apply`` writes. Uploads happen before the database
commit, so a failed upload cannot leave a product selling a PDF that is not stored.

Usage:
    python3 scripts/sync-catalog.py                     # dry run, prints the diff
    python3 scripts/sync-catalog.py --apply             # rows, uploads, catalog source, images
    python3 scripts/sync-catalog.py --apply --no-upload # when the PDFs are already up
    python3 scripts/sync-catalog.py --apply --no-images # skip thumbnail publishing
    python3 scripts/sync-catalog.py --apply --images    # republish images for old listings too
    python3 scripts/sync-catalog.py --json              # machine-readable diff
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import os
import re
import shutil
import sqlite3
import subprocess
import sys
import tempfile
import uuid

from datetime import datetime

from listing_files import (
    CONTAINER,
    edition_name,
    find_source_pdf,
    parent_slug,
    read_config,
    read_listing,
    site_name,
    upload_pdf,
    variant_words,
)
from storage_auth import storage_arguments

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
# REALKEVBASE_DB lets tests point at a copy instead of the shipped database.
DATABASE = os.environ.get("REALKEVBASE_DB") or os.path.join(
    REPO_ROOT, "src", "RealKevbase.Api", "Data", "projects.db"
)
CATALOG = os.path.join(REPO_ROOT, "src", "RealKevbase.Core", "Services", "InMemoryProductCatalog.cs")
META = os.path.join(REPO_ROOT, "scripts", "catalog_meta.json")
OUTPUT = os.path.join(REPO_ROOT, "zz_tp", "output")
DOCUMENTS = os.path.join(REPO_ROOT, "zz_tp", "documents")
FAMILIES = os.path.join(REPO_ROOT, "zz_tp", "scripts", "families.py")

BLOB_PREFIX = "guides"
DEFAULT_PRICE = 5.0

#: Card palettes, keyed by the names used in ``catalog_meta.json``.
ACCENTS = {
    "amber": ("#FEF7E7", "#FBE3B5", "#92400E"),
    "blue": ("#E7EEFF", "#C7D8FF", "#1D4ED8"),
    "green": ("#EAF7F1", "#C6EBDD", "#0F766E"),
    "pink": ("#FDF2F8", "#FBCFE8", "#BE185D"),
    "purple": ("#F1ECFF", "#DCD0FF", "#6D28D9"),
    "slate": ("#ECEFF3", "#D4DAE2", "#334155"),
}

#: Fallback card colour per storefront category, used only for brand-new listings.
CATEGORY_ACCENT = {
    "moving-out": "blue",
    "healthy-living": "green",
    "money-budgeting": "amber",
    "guides": "purple",
}

#: Sales-report families grouped into the four storefront categories.
FAMILY_CATEGORY = {
    "kidney / renal": "healthy-living",
    "diverticulitis": "healthy-living",
    "fatty liver": "healthy-living",
    "gallbladder": "healthy-living",
    "low carb / keto": "healthy-living",
    "folic acid": "healthy-living",
    "blood sugar": "healthy-living",
    "mediterranean": "healthy-living",
    "glp-1": "healthy-living",
    "kids / adhd": "healthy-living",
    "baby": "healthy-living",
    "heart health": "healthy-living",
    "high protein": "healthy-living",
    "budget / finance": "money-budgeting",
    "home / move": "moving-out",
}

INDENT = " " * 12
ENTRY_PATTERN = re.compile(r"(?m)^        new\(\)\n        \{\n(?P<body>(?:.*\n)*?)        \},\n")
SEED_END = "\n    ];"


# --------------------------------------------------------------------------- catalog


def read_field(body: str, name: str) -> str | None:
    """Return an unescaped C# string field from a catalog entry body."""
    match = re.search(rf'(?m)^{INDENT}{name} = "((?:[^"\\]|\\.)*)"', body)
    if not match:
        return None
    return match.group(1).replace('\\"', '"').replace("\\\\", "\\")


def read_guid(body: str) -> str | None:
    """Return the ``Id`` GUID of a catalog entry body."""
    match = re.search(rf'(?m)^{INDENT}Id = Guid\.Parse\("([0-9a-fA-F-]{{36}})"\)', body)
    return match.group(1) if match else None


def read_accent(body: str) -> tuple[str, str, str] | None:
    """Return the three colours of a catalog entry's accent."""
    match = re.search(rf'(?m)^{INDENT}Accent = new ProductAccent\("(#[0-9A-Fa-f]{{6}})", "(#[0-9A-Fa-f]{{6}})", "(#[0-9A-Fa-f]{{6}})"\)', body)
    return match.groups() if match else None


def parse_catalog(text: str) -> list[dict]:
    """Split the seed array into entries, keeping the span of each body."""
    entries = []
    for match in ENTRY_PATTERN.finditer(text):
        body = match.group("body")
        entries.append(
            {
                "id": read_guid(body),
                "name": read_field(body, "Name"),
                "description": read_field(body, "Description"),
                "category": read_field(body, "Category"),
                "accent": read_accent(body),
                "blob": read_field(body, "PdfBlobName"),
                "body_start": match.start("body"),
                "body_end": match.end("body"),
                "body": body,
            }
        )
    return entries


def quote(value: str) -> str:
    """Escape a value for a C# string literal."""
    return '"' + value.replace("\\", "\\\\").replace('"', '\\"') + '"'


def replace_field(body: str, name: str, value: str) -> str:
    """Rewrite one string field inside an entry body, leaving the rest untouched."""
    pattern = re.compile(rf'(?m)^({INDENT}{name} = )"(?:[^"\\]|\\.)*"')
    if not pattern.search(body):
        raise RuntimeError(f"could not find the {name} field to update")
    return pattern.sub(lambda match: match.group(1) + quote(value), body, count=1)


def render_entry(entry: dict) -> str:
    """Render a new catalog entry in the same shape as the existing ones."""
    accent = ", ".join(quote(colour) for colour in entry["accent"])
    return (
        "        new()\n"
        "        {\n"
        f'            Id = Guid.Parse("{entry["id"]}"),\n'
        f"            Name = {quote(entry['name'])},\n"
        f"            Description = {quote(entry['description'])},\n"
        f"            Price = {DEFAULT_PRICE:.1f}m,\n"
        "            Fulfillment = FulfillmentType.DigitalPdf,\n"
        f"            Category = {quote(entry['category'])},\n"
        f"            Accent = new ProductAccent({accent}),\n"
        f"            PdfBlobName = {quote(entry['blob'])}\n"
        "        },\n"
    )


def rewrite_catalog(text: str, edits: list[tuple[dict, dict]], additions: list[dict]) -> str:
    """Apply name/description edits and append new entries to the seed array.

    Edits are spliced from the end backwards so earlier offsets stay valid.
    """
    for entry, desired in sorted(edits, key=lambda item: item[0]["body_start"], reverse=True):
        body = entry["body"]
        if desired["name"] != entry["name"]:
            body = replace_field(body, "Name", desired["name"])
        if desired["description"] != entry["description"]:
            body = replace_field(body, "Description", desired["description"])
        text = text[: entry["body_start"]] + body + text[entry["body_end"] :]

    if additions:
        marker = text.index(SEED_END)
        block = "".join(render_entry(entry) for entry in additions)
        text = text[:marker] + "\n" + block.rstrip("\n") + text[marker:]

    return text


# --------------------------------------------------------------------------- discovery


def load_families():
    """Import the sales-report family rules so both reports agree on a listing."""
    if not os.path.exists(FAMILIES):
        return None
    spec = importlib.util.spec_from_file_location("zz_tp_families", FAMILIES)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def discover(families) -> tuple[list[tuple[str, str]], list[str]]:
    """Return the sellable listing ids and the excluded dev fixtures.

    A listing is sellable when its output folder holds a full-page PDF. The PDF
    generator's own scratch documents are skipped so a probe layout never reaches
    the storefront.
    """
    excluded = set(getattr(families, "EXCLUDED_DOCS", ()))
    listings, skipped = [], []
    for name in sorted(os.listdir(OUTPUT)):
        folder = os.path.join(OUTPUT, name)
        if not os.path.isdir(folder):
            continue
        pdf = find_source_pdf(folder)
        if pdf is None:
            skipped.append(name)
            continue
        if name in excluded:
            continue
        listings.append((name, pdf))

    return listings, skipped


def infer_category(title: str, families) -> str:
    """Map a listing title to one of the four storefront categories."""
    if families is not None:
        family = families.family_of(title)
        if family in FAMILY_CATEGORY:
            return FAMILY_CATEGORY[family]
    return "guides"


def accent_for(slug: str, category: str, meta: dict) -> tuple[str, str, str]:
    """Pick the card palette: explicit override first, then the category default."""
    override = meta.get(slug, {}).get("accent")
    if override:
        if isinstance(override, (list, tuple)) and len(override) == 3:
            return tuple(override)
        if override in ACCENTS:
            return ACCENTS[override]
    return ACCENTS[CATEGORY_ACCENT.get(category, "purple")]


# --------------------------------------------------------------------------- plan


def desired_copy(listings: list[str], meta: dict) -> dict[str, dict]:
    """Compute the name and description every listed folder should carry.

    ``listing.txt`` wins because it is the copy the shop actually published.
    Editions are named from their base listing so a colour never repeats the base
    name: ``split_budget_balanced_blue`` becomes "50 30 20 Budget Worksheet —
    Balanced Blue". ``catalog_meta.json`` may override either field, or supply copy
    for a folder that has no ``listing.txt`` yet.
    """
    texts = {}
    for slug in listings:
        listing = read_listing(os.path.join(DOCUMENTS, slug))
        if listing.get("title"):
            texts[slug] = listing

    available = list(texts)
    copy: dict[str, dict] = {}
    # Shortest folder names first, so a base listing is named before its editions.
    for slug in sorted(texts, key=lambda name: (len(name), name)):
        listing = texts[slug]
        parent = parent_slug(slug, available)
        if parent in copy:
            name = edition_name(copy[parent]["name"], variant_words(slug, parent))
            description = listing["description"] or copy[parent]["description"]
        else:
            name = site_name(listing["title"])
            description = listing["description"]
        copy[slug] = {"name": name, "description": description or None, "source": "listing.txt"}

    for slug, override in meta.items():
        if slug in copy:
            if override.get("name"):
                copy[slug]["name"] = override["name"]
            if override.get("description"):
                copy[slug]["description"] = override["description"]
        elif override.get("name"):
            copy[slug] = {
                "name": override["name"],
                "description": override.get("description"),
                "source": "catalog_meta.json",
            }

    return copy


def fallback_copy(slug: str, config: dict[str, str]) -> dict:
    """Copy for a folder the catalog has never sold: derive it from the generator."""
    title = (config.get("header") or config.get("title") or slug.replace("_", " ").title()).strip()
    return {"name": site_name(title), "description": config.get("description"), "source": "config.tl"}


def product_guid(slug: str) -> str:
    """Stable GUID for a listing id, so re-registering cannot duplicate a product."""
    return str(uuid.uuid5(uuid.NAMESPACE_URL, f"realkevbase/listing/{slug}"))


def resolve_collisions(desired: list[dict], taken: dict[str, str]) -> list[dict]:
    """Number any product name that two listings would otherwise share."""
    for item in desired:
        candidate, number = item["name"], 2
        while candidate.lower() in taken:
            candidate = f"{item['name']} {number}"
            number += 1
        if candidate != item["name"]:
            item["numbered_from"] = item["name"]
            item["name"] = candidate
        taken[candidate.lower()] = item["slug"]
    return desired


def build_plan(listings, catalog_entries, rows, meta, families):
    """Work out what has to change, without touching a single file."""
    by_slug = {}
    for entry in catalog_entries:
        if entry["blob"]:
            slug = entry["blob"][len(BLOB_PREFIX) + 1 : -len(".pdf")].replace("-", "_")
            by_slug[slug] = entry

    slugs = [slug for slug, _ in listings]
    copy = desired_copy(slugs, meta)

    new_items, edits = [], []
    for slug, pdf in listings:
        entry = by_slug.get(slug)
        config = read_config(os.path.join(DOCUMENTS, slug, "config.tl"))

        if entry is None:
            chosen = copy.get(slug) or fallback_copy(slug, config)
            category = meta.get(slug, {}).get("category") or infer_category(chosen["name"], families)
            new_items.append(
                {
                    "slug": slug,
                    "pdf": pdf,
                    "blob": f"{BLOB_PREFIX}/{slug.replace('_', '-')}.pdf",
                    "name": chosen["name"],
                    "description": chosen["description"] or "",
                    "source": chosen["source"],
                    "category": category,
                    "accent": accent_for(slug, category, meta),
                    "id": product_guid(slug),
                    "template": config.get("template") or os.path.basename(pdf)[:-4],
                    "output_name": config.get("output_name") or config.get("template") or "",
                    "completed_date": config.get("completed_date"),
                }
            )
            continue

        chosen = copy.get(slug)
        if not chosen:
            continue
        desired = {
            "name": chosen["name"] or entry["name"],
            "description": chosen["description"] or entry["description"],
            "source": chosen["source"],
        }
        if desired["name"] != entry["name"] or desired["description"] != entry["description"]:
            edits.append((entry, desired, slug))

    # Names the catalog will hold once the edits land: a renamed listing may reuse a
    # name another entry gives up, so everything still locked is claimed first.
    edited = {slug for _, _, slug in edits}
    locked = {slug: entry["name"] for slug, entry in by_slug.items() if entry["name"] and slug not in edited}

    taken = {name.lower(): slug for slug, name in locked.items()}
    claimed = edited | {item["slug"] for item in new_items}
    for row_id, title in rows.items():
        # A listing being renamed must not block itself with its old database title.
        if title and row_id not in claimed:
            taken.setdefault(title.lower(), row_id)

    # Deterministic claims: existing renames in folder order, then new listings.
    claims = [{"slug": slug, "name": desired["name"]} for _, desired, slug in edits]
    claims.sort(key=lambda item: item["slug"])
    claims += [{"slug": item["slug"], "name": item["name"]} for item in new_items]
    resolve_collisions(claims, taken)

    numbered = {item["slug"]: item for item in claims if item.get("numbered_from")}
    for entry, desired, slug in edits:
        if slug in numbered:
            desired["numbered_from"] = numbered[slug]["numbered_from"]
            desired["name"] = numbered[slug]["name"]
    for item in new_items:
        if item["slug"] in numbered:
            item["numbered_from"] = numbered[item["slug"]]["numbered_from"]
            item["name"] = numbered[item["slug"]]["name"]

    final = dict(locked)
    final.update({slug: desired["name"] for _, desired, slug in edits})
    final.update({item["slug"]: item["name"] for item in new_items})

    # Two products with one name read as the same product on the grid.
    seen, conflicts = {}, {}
    for slug, name in final.items():
        key = name.lower()
        if key in seen:
            conflicts.setdefault(name, [seen[key]]).append(slug)
        else:
            seen[key] = slug

    warnings = []
    folder_blobs = {f"{BLOB_PREFIX}/{slug.replace('_', '-')}.pdf" for slug in slugs}
    orphan_entries = sorted(entry["blob"] for entry in catalog_entries if entry["blob"] and entry["blob"] not in folder_blobs)
    if orphan_entries:
        warnings.append(f"catalog entries without a folder: {', '.join(orphan_entries)}")
    orphan_rows = sorted(row_id for row_id in rows if row_id not in set(slugs))
    if orphan_rows:
        warnings.append(f"database rows without a folder: {', '.join(orphan_rows)}")
    no_listing = sorted(set(slugs) - set(copy))
    if no_listing:
        warnings.append(f"{len(no_listing)} listings have no listing.txt; they keep their current copy")

    return {
        "listings": slugs,
        "new": new_items,
        "edits": edits,
        "conflicts": conflicts,
        "warnings": warnings,
        "no_listing": no_listing,
    }


# --------------------------------------------------------------------------- apply


def write_database(plan, connection) -> int:
    """Insert missing rows and refresh titles. Returns the number of rows written."""
    written = 0
    existing = {row[0] for row in connection.execute("SELECT id FROM Projects")}
    for item in plan["new"]:
        if item["slug"] in existing:
            continue
        connection.execute(
            "INSERT INTO Projects "
            "(id, folder_path, completed_date, overrides, output_name, is_editable, template, title) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
            (
                item["slug"],
                f"/workspace/documents/{item['slug']}",
                item["completed_date"] or None,
                "",
                item["output_name"],
                0,
                item["template"],
                item["name"],
            ),
        )
        written += 1

    for entry, desired, slug in plan["edits"]:
        if desired["name"] == entry["name"]:
            continue
        current = connection.execute("SELECT title FROM Projects WHERE id = ?", (slug,)).fetchone()
        if current is None or current[0] == desired["name"]:
            continue
        connection.execute("UPDATE Projects SET title = ? WHERE id = ?", (desired["name"], slug))
        written += 1

    for item in plan["new"]:
        if item["slug"] in existing:
            connection.execute("UPDATE Projects SET title = ? WHERE id = ?", (item["name"], item["slug"]))
            written += 1

    return written


def find_a4_pdf(listing: str, directory: str) -> str | None:
    """Find the A4 edition render of one listing folder.

    Renders were named after the template stem, so the file is not always
    ``<listing>_a4.pdf``; the largest candidate wins when a folder holds several.

    Args:
        listing: Listing id, which is also the folder name.
        directory: Absolute path of the listing's output folder.

    Returns:
        Path of the A4 PDF, or ``None`` when the listing was never rendered for A4.
    """
    candidates = [
        name for name in os.listdir(directory)
        if name.lower().endswith(".pdf") and "a4" in name.lower()
    ]
    if not candidates:
        return None

    preferred = f"{listing}_a4.pdf"
    chosen = (
        preferred
        if preferred in candidates
        else max(candidates, key=lambda name: os.path.getsize(os.path.join(directory, name)))
    )
    return os.path.join(directory, chosen)


def local_a4_editions(source: str) -> dict[str, str]:
    """Map listing id to the A4 edition render it has on disk.

    Args:
        source: Directory holding one folder per listing (``zz_tp/output``).

    Returns:
        Listing id to PDF path; folders without an A4 render are absent.
    """
    editions = {}
    for listing in sorted(os.listdir(source)):
        directory = os.path.join(source, listing)
        if not os.path.isdir(directory):
            continue
        path = find_a4_pdf(listing, directory)
        if path:
            editions[listing] = path
    return editions


def published_pdfs(container: str) -> dict[str, float] | None:
    """List published PDF blobs with their last-modified timestamp.

    Args:
        container: Blob container holding the products.

    Returns:
        Blob name to a POSIX timestamp, or ``None`` when the listing failed.
    """
    auth, _ = storage_arguments()
    command = [
        "az", "storage", "blob", "list",
        "--container-name", container,
        "--prefix", BLOB_PREFIX + "/",
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
        name = entry.get("name")
        if not name or not name.lower().endswith(".pdf"):
            continue
        modified = entry.get("modified")
        try:
            published[name] = datetime.fromisoformat(str(modified)).timestamp()
        except ValueError:
            published[name] = 0.0
    return published


def publish_a4_editions(source: str, container: str = CONTAINER) -> bool:
    """Publish the A4 edition of every listing that has one.

    A4 is legacy: no new listing gets one, but the older guides were rendered for
    A4 paper and their buyers are promised that file. Uploading it is what makes the
    ``US Letter + A4`` chip and the extra download link true, so this runs on every
    apply and only sends what storage is missing or older than the local render.

    Args:
        source: Directory holding one folder per listing (``zz_tp/output``).
        container: Blob container holding the products.

    Returns:
        True when storage already matched or every upload succeeded.
    """
    editions = local_a4_editions(source)
    if not editions:
        print("\nno A4 editions to publish")
        return True

    print(f"\nA4 editions: {len(editions)} listing(s) have one")
    published = published_pdfs(container)
    if published is None:
        print("  could not list storage; every edition is a candidate")

    pending = []
    for listing, path in editions.items():
        blob = f"{BLOB_PREFIX}/{listing.replace('_', '-')}-a4.pdf"
        remote = None if published is None else published.get(blob)
        if remote is not None and remote >= os.path.getmtime(path) - 1:
            continue
        pending.append((path, blob))

    if not pending:
        print("  storage already matches every A4 edition; nothing to upload")
        return True

    print(f"  uploading {len(pending)} of {len(editions)} editions")
    auth, source_label = storage_arguments()
    print(f"  credentials: {source_label}")

    staging = tempfile.mkdtemp(prefix="realkevbase-a4-")
    try:
        for path, blob in pending:
            shutil.copy2(path, os.path.join(staging, os.path.basename(blob)))

        command = [
            "az", "storage", "blob", "upload-batch",
            "--destination", f"{container}/{BLOB_PREFIX}",
            "--source", staging,
            "--pattern", "*.pdf",
            "--content-type", "application/pdf",
            "--overwrite",
        ]
        command += auth
        result = subprocess.run(command, capture_output=True, text=True, timeout=900)
        if result.returncode != 0:
            print(f"  upload failed: {result.stderr.strip()[:400]}", file=sys.stderr)
            return False
    finally:
        shutil.rmtree(staging, ignore_errors=True)

    return True


def publish_images(excluded=()) -> bool:
    """Publish covers and gallery renders, skipping the generator's probe folders."""
    script = os.path.join(REPO_ROOT, "scripts", "build-thumbnails.py")
    interpreter = os.path.join(REPO_ROOT, ".venv", "bin", "python")
    if not os.path.exists(interpreter):
        interpreter = sys.executable
    command = [interpreter, script, "--upload"]
    for listing in excluded:
        command += ["--exclude", listing]
    print("\npublishing thumbnails and gallery renders…")
    return subprocess.call(command) == 0


def describe(plan, args) -> None:
    """Print the diff in the order a person would read it."""
    verb = "applying" if args.apply else "preview (pass --apply to write)"
    print(f"catalog sync — {verb}")
    print(f"listings in zz_tp/output : {len(plan['listings'])}")
    print(f"new listings to register  : {len(plan['new'])}")
    if plan["new"]:
        for item in plan["new"]:
            extra = f" (numbered from '{item['numbered_from']}')" if item.get("numbered_from") else ""
            print(f"  + {item['slug']:<32} {item['name']}{extra}")
            print(f"    {item['blob']}  [{item['category']}]  copy from {item['source']}")

    renames = [item for item in plan["edits"] if item[1]["name"] != item[0]["name"]]
    rewrites = [item for item in plan["edits"] if item[1]["description"] != item[0]["description"]]
    print(f"names to refresh          : {len(renames)}")
    for entry, desired, slug in renames:
        print(f"  ~ {slug:<32} {entry['name']}  ->  {desired['name']}")
    print(f"descriptions to refresh   : {len(rewrites)}")
    for entry, desired, slug in rewrites[:3]:
        print(f"  ~ {slug:<32} {desired['description'][:72]}…")

    if plan["conflicts"]:
        print("duplicate product names")
        for name, slugs in plan["conflicts"].items():
            print(f"  ! {name}: {', '.join(slugs)}")

    if plan["warnings"]:
        print("notes")
        for warning in plan["warnings"]:
            print(f"  ! {warning}")


def dump_json(plan) -> None:
    """Print the plan as JSON for other tooling."""
    payload = {
        "listings": len(plan["listings"]),
        "new": [
            {key: item[key] for key in ("slug", "name", "blob", "category", "source", "id")}
            for item in plan["new"]
        ],
        "renamed": [
            {"slug": slug, "from": entry["name"], "to": desired["name"]}
            for entry, desired, slug in plan["edits"]
            if desired["name"] != entry["name"]
        ],
        "redescribed": [slug for entry, desired, slug in plan["edits"] if desired["description"] != entry["description"]],
        "warnings": plan["warnings"],
    }
    print(json.dumps(payload, indent=2))


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Reconcile zz_tp listings, the shipped database and the product catalog.",
    )
    parser.add_argument("--apply", action="store_true", help="write rows, upload PDFs and update the catalog")
    parser.add_argument("--no-upload", action="store_true", help="skip PDF uploads with --apply")
    parser.add_argument(
        "--images",
        action="store_true",
        help="republish thumbnails and gallery renders even when no listing was added",
    )
    parser.add_argument(
        "--no-images",
        action="store_true",
        help="skip thumbnail publishing with --apply",
    )
    parser.add_argument("--json", action="store_true", help="print the diff as JSON")
    args = parser.parse_args()

    for path, label in ((OUTPUT, "zz_tp/output"), (CATALOG, "the product catalog"), (DATABASE, "the database")):
        if not os.path.exists(path):
            print(f"error: {label} not found at {os.path.relpath(path, REPO_ROOT)}", file=sys.stderr)
            return 1

    families = load_families()
    listings, skipped = discover(families)
    if not listings:
        print("error: no sellable listings found in zz_tp/output", file=sys.stderr)
        return 1

    text = open(CATALOG, encoding="utf-8").read()
    catalog_entries = parse_catalog(text)
    if not catalog_entries:
        print("error: could not parse any catalog entries; refusing to continue", file=sys.stderr)
        return 1

    meta = json.load(open(META, encoding="utf-8")) if os.path.exists(META) else {}

    connection = sqlite3.connect(DATABASE)
    try:
        rows = {row_id: title for row_id, title in connection.execute("SELECT id, title FROM Projects")}
        plan = build_plan(listings, catalog_entries, rows, meta, families)

        if args.json:
            dump_json(plan)
        else:
            describe(plan, args)
            if skipped:
                print(f"skipped (no full-page PDF): {', '.join(sorted(skipped))}")

        if plan["conflicts"]:
            print(
                "\nerror: two listings would share a product name. Adjust listing.txt "
                "or scripts/catalog_meta.json, then re-run.",
                file=sys.stderr,
            )
            return 1

        if not args.apply:
            if not args.json:
                if plan["new"] or plan["edits"]:
                    print("\nnothing written. Re-run with --apply to update the shop.")
                else:
                    print("\ncatalog is up to date.")
            return 0

        # Upload first: a product must never be visible before its PDF is stored.
        if plan["new"] and not args.no_upload:
            for item in plan["new"]:
                print()
                if not upload_pdf(item["pdf"], item["blob"]):
                    connection.rollback()
                    print(
                        f"\nerror: upload failed for {item['slug']}; nothing was written. "
                        "Re-run once storage is reachable.",
                        file=sys.stderr,
                    )
                    return 1

        written = write_database(plan, connection)
        connection.commit()

        updated = rewrite_catalog(text, [(entry, desired) for entry, desired, _ in plan["edits"]], plan["new"])
        if updated != text:
            temporary = CATALOG + ".tmp"
            with open(temporary, "w", encoding="utf-8", newline="") as handle:
                handle.write(updated)
            os.replace(temporary, CATALOG)

        print(f"\nrows written   : {written}")
        print(f"entries added  : {len(plan['new'])}")
        print(f"copy refreshed : {len(plan['edits'])}")
    finally:
        connection.close()

    if not args.no_upload:
        if not publish_a4_editions(OUTPUT):
            print("error: A4 edition publishing failed", file=sys.stderr)
            return 1

    # A listing that was just registered has no cover in storage yet, so publish the
    # renders whenever one was added; --images forces a refresh of the rest too.
    if not args.no_images and (plan["new"] or args.images):
        if not publish_images(sorted(getattr(families, "EXCLUDED_DOCS", ()))):
            print("error: thumbnail publishing failed", file=sys.stderr)
            return 1

    print("\nnext: dotnet build, then ./scripts/deploy.sh --yes (not run by this script)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
