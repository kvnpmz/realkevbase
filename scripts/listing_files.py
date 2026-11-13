"""Read the pieces a listing is made of: its rendered folder and its shop copy.

A finished guide lives in two places inside ``zz_tp``:

  * ``zz_tp/output/<listing>/`` holds the rendered PDF and its image renders
  * ``zz_tp/documents/<listing>/`` holds the generator config and, for most
    listings, the ``listing.txt`` that was written for the Etsy shop

The maintenance scripts need the same view of both, so the parsing lives here
instead of being copied between them. Nothing in this module writes to disk
except :func:`upload_pdf`, which shells out to ``az``; everything else only reads.
"""

from __future__ import annotations

import os
import re
import subprocess
import sys

from storage_auth import storage_arguments

#: Render suffixes that are not the full-page artifact (device previews, zoom crops).
VARIANTS = ("_a4", "_ipad", "_zoom")

#: Publisher keyword filler that reads badly as a storefront product name.
NAME_FILLER = ("Printable", "Print", "PDF", "Instant Download", "Digital Download")

#: Variant words used to tell two editions of one listing apart on the storefront.
VARIANT_WORDS = {
    "babyblue": "Baby Blue",
    "lightblue": "Light Blue",
    "blue": "Blue",
    "green": "Green",
    "mustard": "Mustard",
    "pink": "Pink",
    "plum": "Plum",
    "purple": "Purple",
    "teal": "Teal",
    "serif": "Serif",
    "balanced": "Balanced",
    "minimal": "Minimal",
    "essential": "Essential",
    "grid": "Grid",
    "tabbed": "Tabbed",
    "alternative": "Alternative",
    "decorative": "Decorative",
    "regal": "Regal",
    "card": "Card",
    "bullet": "Bullet",
}

CONTAINER = "pdfs"


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


def read_listing(doc_dir: str) -> dict[str, str]:
    """Return the ``listing.txt`` blocks for a document folder, or an empty dict.

    The file is plain shop metadata, one block per heading::

        TITLE
        Pipe | separated | keywords
        TAGS
        case, study, tags
        DESCRIPTION
        One sentence about the PDF.

    Args:
        doc_dir: Path to ``zz_tp/documents/<listing>``.

    Returns:
        A dict with ``title`` (the full pipe-separated line), ``tags`` and
        ``description``; empty when the folder has no ``listing.txt``.
    """
    path = os.path.join(doc_dir, "listing.txt")
    if not os.path.exists(path):
        return {}

    blocks: dict[str, list[str]] = {}
    current = None
    for raw in open(path, encoding="utf-8").read().splitlines():
        line = raw.strip()
        if line in ("TITLE", "TAGS", "DESCRIPTION"):
            current = line
            blocks[current] = []
        elif line and current:
            blocks[current].append(line)

    return {
        "title": " ".join(blocks.get("TITLE", [])),
        "tags": " ".join(blocks.get("TAGS", [])),
        "description": " ".join(blocks.get("DESCRIPTION", [])),
    }


def site_name(listing_title: str) -> str:
    """Turn a shop title into a storefront product name.

    Only the first pipe segment names the product; the rest is keyword filler for
    search. Trailing words such as "Printable" are dropped as well, because the
    storefront already labels every guide as a printable PDF.
    """
    name = listing_title.split("|")[0].strip()
    for _ in range(4):  # "Instant Download PDF" needs more than one pass
        trimmed = name
        for filler in NAME_FILLER:
            suffix = " " + filler.lower()
            if trimmed.lower().endswith(suffix):
                trimmed = trimmed[: -len(suffix)].rstrip(" ,-–—")
        if trimmed == name:
            break
        name = trimmed
    return re.sub(r"\s+", " ", name).strip()


def parent_slug(slug: str, candidates: list[str]) -> str | None:
    """Return the closest listing that ``slug`` is an edition of, if any.

    Editions are named by appending a word to the base listing's folder name
    (``split_budget`` -> ``split_budget_balanced``), so the longest candidate that
    is a prefix at an underscore boundary is the parent.
    """
    parent = None
    for other in candidates:
        if other != slug and slug.startswith(other + "_"):
            if parent is None or len(other) > len(parent):
                parent = other
    return parent


def variant_words(slug: str, parent: str) -> str:
    """Describe the edition suffix, e.g. ``split_budget_balanced_blue`` -> "Balanced Blue"."""
    return " ".join(
        VARIANT_WORDS.get(token.lower(), token.replace("_", " ").title())
        for token in slug[len(parent) + 1 :].split("_")
        if token
    )


def edition_name(base: str, edition: str) -> str:
    """Attach an edition suffix, folding it into one already present.

    Colour editions stack ("Balanced", then "Balanced Blue"), so the suffix grows
    instead of gaining a second dash. Words the base name already carries are not
    repeated.
    """
    root, _, existing = base.partition(" — ")
    words = [word for word in f"{existing} {edition}".split() if word.lower() not in root.lower()]
    return f"{root} — {' '.join(words)}" if words else root


def upload_pdf(pdf: str, blob_name: str, container: str = CONTAINER) -> bool:
    """Upload a product PDF to blob storage, returning whether it succeeded.

    Credentials are resolved automatically by :mod:`storage_auth` — from an exported
    ``AZURE_STORAGE_CONNECTION_STRING`` or straight from the live App Service settings —
    so no secret is ever stored in a file or typed in by hand.

    Args:
        pdf: Local path of the PDF to upload.
        blob_name: Destination blob name inside the products container.
        container: Blob container holding the products (defaults to ``pdfs``).

    Returns:
        True when the upload succeeded.
    """
    arguments, source = storage_arguments()

    command = [
        "az",
        "storage",
        "blob",
        "upload",
        "--container-name",
        container,
        "--name",
        blob_name,
        "--file",
        pdf,
        "--content-type",
        "application/pdf",
        "--overwrite",
        "--no-progress",
        "--output",
        "none",
    ]
    command += arguments

    print(f"uploading to {container}/{blob_name} (credentials: {source})")
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
