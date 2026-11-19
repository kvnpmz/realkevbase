#!/usr/bin/env python3
"""Render 2:3 Pinterest cards, several per guide, from the renders already on disk.

Pinterest gives a taller image more feed room, and a pin of one product is worth more than
one post: the same guide can appear several times as long as each pin is a *different*
picture. The renders are Letter-shaped (2550x3300, ratio 0.77), so a card pads the artwork
onto a 1000x1500 canvas with a headline and the price, which is what makes a printable
readable in a scrolling grid.

A variant is one crop of the page plus one headline. The first variant of every listing is
always the whole page under the guide's own name, so there is always one honest, complete
preview. Further variants crop a full-width band or a single column and take their headline
from another of the guide's own keyword phrases. The choice is seeded by listing and
variant number, so a re-run reproduces the same pins instead of quietly inventing new ones.

Every card carries the company logo in the bottom-left corner, level with the price pill:
a pin reposted by someone else is then still branded, and a grid of them reads as one shop.
``--no-wordmark`` renders a bare card and ``--wordmark PATH`` swaps in another file.

Each card is recorded with its SHA-256 in ``pins/manifest.json``: that hash is what the pin
ledger uses to tell two artworks apart, and what stops a byte-identical repost being counted
as fresh.

Cards are written to ``zz_tp/output/<listing>/pins/`` — generated output, never committed.

Usage:
    python3 scripts/pin_cards.py --only gout_food_list --variants 3
    python3 scripts/pin_cards.py --limit 5 --variants 2
    python3 scripts/pin_cards.py --all
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request

from listing_files import (
    find_cover_image,
    parent_slug,
    read_listing,
    site_name,
    strip_edition,
)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_OUTPUT = os.path.join(ROOT, "zz_tp", "output")
DEFAULT_DOCUMENTS = os.path.join(ROOT, "zz_tp", "documents")
META_PATH = os.path.join(ROOT, "scripts", "catalog_meta.json")

#: Pinterest's preferred pin ratio, in pixels.
WIDTH, HEIGHT = 1000, 1500

PADDING = 44
IMAGE_BOX = (WIDTH - 2 * PADDING, 1108)
GAP = 30
TITLE_BOX = (WIDTH - 2 * PADDING, 180)
PRICE_HEIGHT = 84

BACKGROUND = "#FBF9F5"
INK = "#16181C"

#: The mark stamped on every card. The asset is the SaveSimple company logo, drawn on a
#: cream field with a drop shadow, so it is keyed to transparency before it is composited
#: (see :func:`wordmark_args`) rather than dropped on the card as a pale rectangle.
WORDMARK_SOURCE = os.path.join(ROOT, "zz_tp", "assets", "company_logo.png")

#: 24% of the card width. Smaller than this and the oval's lettering stops resolving in a
#: 236px feed tile; larger and it competes with the price pill beside it.
WORDMARK_WIDTH = 240

#: The green the logo is drawn in, sampled from the asset, flattened onto one colour so the
#: mark reads the same on the cream card and on any of the pastel backdrops.
WORDMARK_INK = "#436636"

#: Backdrops for the colour-block card. Eight pastels spread round the wheel so that two
#: neighbouring pins in a grid rarely share a look; the colour is picked from the listing
#: and variant, so it stays stable across runs.
PASTELS = (
    "#F6D6D6",  # rose
    "#FAE3C6",  # peach
    "#F7EBC8",  # butter
    "#DCEBD3",  # sage
    "#CFE5E0",  # mint
    "#D3E1F0",  # sky
    "#DCD8EE",  # lilac
    "#EBD9E7",  # mauve
)

#: Accent per storefront colourway, matching the guide accents.
ACCENTS = {
    "amber": "#9A6B1F",
    "blue": "#2C5C8F",
    "green": "#2F6B4F",
    "pink": "#A8365A",
    "purple": "#5B4B8A",
    "slate": "#46505C",
}

#: The three card designs. A guide gets one of each: the whole page, the same page on a
#: colour block, and a text-only card. Cropping a band out of the page was tried and
#: rejected — these pages are three-column tables, so a fractional crop cuts words in half.
STYLES = ("page", "block", "text")

#: Formats that name the file rather than the subject, so they make poor headlines.
FORMAT_WORDS = frozenset(
    {"a4", "a4 letter", "a4 letter pdf", "a4 letter size", "a4 pdf", "digital download",
     "digital file", "instant download", "letter size", "pdf", "pdf download", "print",
     "printable", "printable pdf"}
)

VARIANT_IDS = "abcdefghij"


def font(style: str, family: str = "Inter") -> str:
    """Return the installed font file for a family and style, falling back to DejaVu.

    The family is a fontconfig name, so ``--font "Source Serif 4"`` switches every card to
    a serif face without a code change. Missing weights fall back within the same family
    before leaving it, because a serif headline in a sans fallback would be obvious.
    """
    for query in (
        f"{family}:style={style}",
        f"{family}:style=Bold",
        family,
        f"DejaVu Sans:style={style}",
        "DejaVu Sans",
    ):
        try:
            path = subprocess.check_output(
                ["fc-match", "-f", "%{file}", query], text=True
            ).strip()
        except (OSError, subprocess.CalledProcessError):
            continue
        if path and os.path.exists(path):
            return path
    raise SystemExit("no usable font found; install fonts-inter or fonts-dejavu")


def pastel_for(listing: str, variant: str) -> str:
    """Pick a block backdrop from the listing id and variant number.

    Hashing the id rather than the category is what spreads the eight pastels across the
    grid: seeding from the accent clustered every guide in a category onto one colour. A
    new listing therefore picks a colour without disturbing any card already rendered.
    """
    seed = hashlib.sha256(f"{listing}:{variant}".encode()).digest()[0]
    return PASTELS[seed % len(PASTELS)]


def accent_for(listing: str, meta: dict[str, dict]) -> str:
    """Look up the listing's accent colour, defaulting to slate."""
    accent = str(meta.get(listing, {}).get("accent") or "slate")
    return ACCENTS.get(accent, ACCENTS["slate"])


def catalog_meta(api_url: str) -> dict[str, dict]:
    """Return the storefront's name and description per listing, or an empty map offline."""
    url = f"{api_url.rstrip('/')}/api/products"
    request = urllib.request.Request(url, headers={"Accept": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            payload = json.load(response)
    except (urllib.error.URLError, json.JSONDecodeError, TimeoutError):
        print(f"could not read {url}; names fall back to shop copy", file=sys.stderr)
        return {}
    if not isinstance(payload, list):
        return {}
    return {
        str(item.get("listingId")): {
            "name": str(item.get("name") or ""),
            "description": str(item.get("description") or ""),
        }
        for item in payload
    }


def guide_phrases(
    listing: str,
    documents_dir: str,
    names: dict[str, dict],
    known: list[str],
) -> tuple[list[str], bool]:
    """Return the phrases a card may use as its headline, best first.

    The guide's own name leads; the remaining shop keywords follow, so a second variant can
    say "Low Purine Diet Guide" where the first said "Gout Food List" — both are phrases the
    listing already sells on, not invented copy.
    """
    copy = read_listing(os.path.join(documents_dir, listing))
    phrases = [
        phrase.strip()
        for phrase in copy.get("title", "").split("|")
        if phrase.strip() and phrase.strip().lower() not in FORMAT_WORDS
    ]
    authored = site_name(copy.get("title", "")) if copy.get("title") else ""
    catalog = str((names.get(listing) or {}).get("name") or "")
    folder = listing.replace("_", " ").title()
    parent = parent_slug(listing, known)
    parent_copy = read_listing(os.path.join(documents_dir, parent)) if parent else {}
    parent_name = site_name(parent_copy.get("title", "")) if parent_copy.get("title") else ""

    # A translated edition has shop copy of its own ("Control de Fondos de Reserva"), so it
    # keeps its own name. A colour or serif edition reuses the parent's copy, which would
    # name the parent and render two identical cards; the catalog name carries the edition
    # ("Kidney Lab Tracker — Pink"), so that is what those editions are called.
    if authored and authored.lower() != parent_name.lower():
        name = authored or catalog or folder
    else:
        name = catalog or authored or folder
    inherits_copy = bool(parent) and (not authored or authored.lower() == parent_name.lower())
    if parent_name:
        phrases = [phrase for phrase in phrases if phrase.lower() != parent_name.lower()]
    name = strip_edition(name)
    phrases = [strip_edition(phrase) for phrase in phrases]
    phrases = [name, *(phrase for phrase in phrases if phrase.lower() != name.lower())]
    return phrases, inherits_copy


def run(arguments: list[str], what: str) -> None:
    """Run one ImageMagick command, raising with its stderr when it fails."""
    result = subprocess.run(["magick", *arguments], capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f"{what} failed: {result.stderr.strip()}")


#: Keyed wordmarks, by source path and width, so the work happens once per run.
_WORDMARKS: dict[tuple[str, int], tuple[str, int]] = {}


def wordmark_args(source: str, width: int = WORDMARK_WIDTH) -> list[str]:
    """Compose the company logo into the card's bottom-left corner.

    Keying the asset by luma does the work of removing its field: the cream goes fully
    transparent, the drop shadow survives as partial alpha and is then thresholded away, and
    what is left is one flat green mark. Without that, a pastel card would carry a cream
    rectangle around the logo and the shadow would read as dirt.

    It is placed level with the price pill inside the same margin, so a page card, a block
    card and a text card all carry the brand in the same spot. Keyed once per run, not once
    per card.
    """
    if not source:
        return []
    key = (source, width)
    if key not in _WORDMARKS:
        if not os.path.exists(source):
            raise SystemExit(f"wordmark not found: {source}")
        target = os.path.join(tempfile.gettempdir(), f"pin-wordmark-{width}.png")
        run(
            [
                source,
                # Cream (luma 240) → 0 after the negate, the green line (91) → 255, so the
                # level drops the field without eating the thinner parts of the lettering.
                "-colorspace", "gray", "-negate", "-level", "8%,64%",
                "-alpha", "copy", "-fill", WORDMARK_INK, "-colorize", "100%",
                "-channel", "A", "-threshold", "45%", "+channel",
                "-trim", "+repage", "-resize", f"{width}x", "-depth", "8",
                target,
            ],
            "wordmark",
        )
        height = int(
            subprocess.check_output(
                ["magick", "identify", "-format", "%h", target], text=True
            ).strip()
        )
        _WORDMARKS[key] = (target, height)
    path, height = _WORDMARKS[key]
    pill_top = HEIGHT - PADDING - PRICE_HEIGHT
    return [
        "(", path, ")",
        "-gravity", "northwest",
        "-geometry", f"+{PADDING}+{pill_top + round((PRICE_HEIGHT - height) / 2)}",
        "-composite",
    ]


def build_card(
    image_path: str,
    headline: str,
    price_text: str,
    accent: str,
    destination: str,
    backdrop: str = BACKGROUND,
    family: str = "Inter",
    wordmark: str = "",
) -> None:
    """Compose one card: artwork on top, headline under it, price pill at the foot.

    ``backdrop`` replaces the cream card with a pastel, which is a different enough picture
    for Pinterest to treat it as a new pin while keeping the page itself intact. The
    headline stays dark ink, so a pale backdrop cannot wash the words out.
    """
    box_width, box_height = IMAGE_BOX
    source_width, source_height = (
        float(value)
        for value in subprocess.check_output(
            ["magick", "identify", "-format", "%[fx:w] %[fx:h]", image_path], text=True
        ).split()
    )
    scale = min(box_width / source_width, box_height / source_height)
    draw_width = round(source_width * scale)
    draw_height = round(source_height * scale)
    image_top = PADDING + round((box_height - draw_height) / 2)

    size = 62 if len(headline) <= 30 else 50 if len(headline) <= 46 else 40
    title_top = PADDING + box_height + GAP
    price_top = HEIGHT - PADDING - PRICE_HEIGHT

    run(
        [
            "-size", f"{WIDTH}x{HEIGHT}", f"xc:{backdrop}",
            "(", image_path, "-resize", f"{draw_width}x{draw_height}", ")",
            "-gravity", "north", "-geometry", f"+0+{image_top}", "-composite",
            "(", "-background", "none", "-fill", INK, "-font", font("Bold", family),
            "-pointsize", str(size), "-interline-spacing", "6", "-gravity", "center",
            "-size", f"{TITLE_BOX[0]}x{TITLE_BOX[1]}", f"caption:{headline}", ")",
            "-gravity", "north", "-geometry", f"+0+{title_top}", "-composite",
            "(", "-size", f"{WIDTH}x{PRICE_HEIGHT}", "xc:none",
            "-fill", accent,
            "-draw", f"roundrectangle 340,0,660,{PRICE_HEIGHT - 4},42,42",
            "-fill", "white", "-font", font("Bold", family),
            "-pointsize", "34",
            "-gravity", "north",
            "-annotate", f"+0+{round((PRICE_HEIGHT - 42) / 2)}",
            f"{price_text}  ·  Instant PDF", ")",
            "-gravity", "north", "-geometry", f"+0+{price_top}", "-composite",
            *wordmark_args(wordmark),
            "-strip", "-sampling-factor", "4:2:0", "-quality", "90",
            destination,
        ],
        "card",
    )


def digest(path: str) -> str:
    """Hash a finished card, so the ledger can recognise it later."""
    with open(path, "rb") as handle:
        return hashlib.sha256(handle.read()).hexdigest()


def build_text_card(
    headline: str,
    bullets: list[str],
    price_text: str,
    accent: str,
    destination: str,
    family: str = "Inter",
    paragraph: bool = False,
    wordmark: str = "",
) -> None:
    """Compose a card with no page artwork: a headline, the guide's own words, a price.

    ``paragraph`` drops the bullet dots and sets the text smaller, for listings whose words
    come from the catalog description rather than from a keyword list.
    """
    rule_top = 150
    headline_top = 300
    bullets_top = 760

    run(
        [
            "-size", f"{WIDTH}x{HEIGHT}", f"xc:{BACKGROUND}",
            "-fill", accent,
            "-draw", f"roundrectangle 380,{rule_top},620,{rule_top + 12},6,6",
            "(", "-background", "none", "-fill", INK, "-font", font("Bold", family),
            "-pointsize", "86", "-interline-spacing", "4", "-gravity", "center",
            "-size", f"{WIDTH - 160}x420", f"caption:{headline}", ")",
            "-gravity", "north", "-geometry", f"+0+{headline_top}", "-composite",
            "(", "-background", "none", "-fill", INK, "-font", font("Regular", family),
            "-pointsize", "30" if paragraph else "44",
            "-interline-spacing", "18" if paragraph else "26", "-gravity", "center",
            "-size", f"{WIDTH - 220}x420",
            f"caption:{chr(10).join(bullets) if paragraph else chr(10).join(f'· {b}' for b in bullets)}",
            ")",
            "-gravity", "north", "-geometry", f"+0+{bullets_top if not paragraph else bullets_top - 60}",
            "-composite",
            "(", "-size", f"{WIDTH}x{PRICE_HEIGHT}", "xc:none",
            "-fill", accent,
            "-draw", f"roundrectangle 340,0,660,{PRICE_HEIGHT - 4},42,42",
            "-fill", "white", "-font", font("Bold", family), "-pointsize", "34",
            "-gravity", "north",
            "-annotate", f"+0+{round((PRICE_HEIGHT - 42) / 2)}",
            f"{price_text}  ·  Instant PDF", ")",
            "-gravity", "north",
            "-geometry", f"+0+{HEIGHT - PADDING - PRICE_HEIGHT}", "-composite",
            *wordmark_args(wordmark),
            "-strip", "-sampling-factor", "4:2:0", "-quality", "90",
            destination,
        ],
        "text card",
    )


def wrap_sentence(text: str, limit: int) -> str:
    """Trim a description to whole words within a character limit, keeping one sentence."""
    if len(text) <= limit:
        return text
    cut = text[:limit].rsplit(" ", 1)[0].rstrip(" ,;:-—")
    return f"{cut}…"


def prune(destination_dir: str, keep: set[str]) -> list[str]:
    """Delete cards in a pins folder that the manifest no longer claims.

    Variants are skipped when a listing has no shop copy or when every rotation of its
    bullets is already taken, so a file from an earlier run can outlive the rule that made
    it. Leaving it behind means a card exists that no pin will ever use.
    """
    removed = []
    for name in sorted(os.listdir(destination_dir)):
        if name.endswith(".jpg") and name not in keep:
            os.remove(os.path.join(destination_dir, name))
            removed.append(name)
    return removed


def render_listing(
    listing: str,
    output_dir: str,
    documents_dir: str,
    names: dict[str, str],
    meta: dict[str, dict],
    price_text: str,
    count: int,
    family: str = "Inter",
    accent_override: str = "",
    seen: set[str] | None = None,
    wordmark: str = "",
) -> list[dict]:
    """Render ``count`` variants of one guide and return their manifest entries."""
    image = find_cover_image(os.path.join(output_dir, listing, "images"))
    if image is None:
        return []

    known = sorted(
        name
        for name in os.listdir(output_dir)
        if os.path.isdir(os.path.join(output_dir, name))
    )
    phrases, _inherits = guide_phrases(listing, documents_dir, names, known)
    has_copy = bool(read_listing(os.path.join(documents_dir, listing)))
    description = str((names.get(listing) or {}).get("description") or "").strip()
    accent = accent_override or accent_for(listing, meta)
    destination_dir = os.path.join(output_dir, listing, "pins")
    os.makedirs(destination_dir, exist_ok=True)

    seen = seen if seen is not None else set()
    entries = []
    for index in range(count):
        style = STYLES[index % len(STYLES)]
        # A text card has no page to identify it, so it leads with the guide's name; the
        # page cards can afford a keyword headline because the artwork shows the product.
        headline = (
            phrases[0]
            if style in ("page", "text")
            else phrases[index % len(phrases)]
        )
        variant = VARIANT_IDS[index]
        destination = os.path.join(destination_dir, f"{listing}-{variant}.jpg")

        if style == "text":
            pool = [phrase for phrase in phrases[1:] if phrase != headline]
            if not has_copy or not pool:
                # No shop copy means no keyword bullets, so the card carries the catalog
                # sentence instead: every product has one, and it explains what the page is.
                if not description:
                    continue
                paragraph = wrap_sentence(description, 190)
                offset = hashlib.sha256(listing.encode()).digest()[0] % len(PASTELS)
                for attempt in range(len(PASTELS)):
                    build_text_card(
                        headline, [paragraph], price_text, accent, destination, family,
                        paragraph=True, wordmark=wordmark,
                    )
                    sha = digest(destination)
                    if sha not in seen:
                        break
                    accent = PASTELS[(offset + attempt) % len(PASTELS)]
                else:
                    continue
                seen.add(sha)
                entries.append(
                    {
                        "listing": listing,
                        "variant": variant,
                        "style": style,
                        "headline": headline,
                        "font": family,
                        "accent": accent,
                        "file": os.path.relpath(destination, ROOT),
                        "sha256": sha,
                        "bytes": os.path.getsize(destination),
                    }
                )
                continue

            # An edition can share its parent's exact words, which would render the parent's
            # card again. Rotating the bullet order keeps the copy honest and the picture
            # different; if every rotation is already taken, the card is skipped rather than
            # repeated.
            offset = hashlib.sha256(listing.encode()).digest()[0] % len(pool)
            for attempt in range(len(pool)):
                start = (offset + attempt) % len(pool)
                rotated = pool[start:] + pool[:start]
                build_text_card(
                    headline, rotated[:3], price_text, accent, destination, family,
                    wordmark=wordmark,
                )
                sha = digest(destination)
                if sha not in seen:
                    break
            else:
                continue
            seen.add(sha)
            entries.append(
                {
                    "listing": listing,
                    "variant": variant,
                    "style": style,
                    "headline": headline,
                    "font": family,
                    "accent": accent,
                    "file": os.path.relpath(destination, ROOT),
                    "sha256": sha,
                    "bytes": os.path.getsize(destination),
                }
            )
            continue
        else:
            build_card(
                image, headline, price_text, accent, destination,
                backdrop=(
                    pastel_for(listing, variant) if style == "block" else BACKGROUND
                ),
                family=family,
                wordmark=wordmark,
            )

        sha = digest(destination)
        seen.add(sha)
        entries.append(
            {
                "listing": listing,
                "variant": variant,
                "style": style,
                "headline": headline,
                "font": family,
                "accent": accent,
                "file": os.path.relpath(destination, ROOT),
                "sha256": sha,
                "bytes": os.path.getsize(destination),
            }
        )
    return entries


def main(argv: list[str] | None = None) -> int:
    """Render cards for the requested listings."""
    parser = argparse.ArgumentParser(
        description="Render 2:3 Pinterest cards.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument("--only", action="append", default=[], metavar="SLUG")
    parser.add_argument("--limit", type=int, default=3)
    parser.add_argument("--all", action="store_true")
    parser.add_argument("--variants", type=int, default=3, help="cards per listing (default 3)")
    parser.add_argument("--price", default="5")
    parser.add_argument("--font", default="Inter", help="fontconfig family for headlines")
    parser.add_argument("--accent", default="", help="force one accent colour for every card")
    parser.add_argument(
        "--wordmark", default=WORDMARK_SOURCE, metavar="PATH",
        help="logo stamped on every card (default: the company logo)",
    )
    parser.add_argument(
        "--no-wordmark", action="store_true", help="render cards without the logo",
    )
    parser.add_argument("--output", default=DEFAULT_OUTPUT)
    parser.add_argument("--documents", default=DEFAULT_DOCUMENTS)
    parser.add_argument("--meta", default=META_PATH)
    parser.add_argument("--api-url", default="https://api.realkevbase.com")
    args = parser.parse_args(argv)

    with open(args.meta, encoding="utf-8") as handle:
        meta = json.load(handle)
    names = catalog_meta(args.api_url)

    listings = sorted(
        name
        for name in os.listdir(args.output)
        if os.path.isdir(os.path.join(args.output, name))
    )
    if args.only:
        wanted = {value.replace("-", "_").lower() for value in args.only}
        listings = [name for name in listings if name.lower() in wanted]
    if not args.all:
        listings = listings[: args.limit]
    if not listings:
        print("no matching listings", file=sys.stderr)
        return 2

    price_text = f"${args.price}"
    wordmark = "" if args.no_wordmark else args.wordmark
    rendered = 0
    seen: set[str] = set()
    for listing in listings:
        entries = render_listing(
            listing, args.output, args.documents, names, meta, price_text, args.variants,
            args.font, args.accent, seen, wordmark,
        )
        if not entries:
            print(f"{listing}: no cover render, skipped")
            continue
        pins_dir = os.path.join(args.output, listing, "pins")
        for name in prune(pins_dir, {os.path.basename(entry["file"]) for entry in entries}):
            print(f"{listing}: removed stale {name}")
        manifest_path = os.path.join(pins_dir, "manifest.json")
        with open(manifest_path, "w", encoding="utf-8") as handle:
            json.dump(entries, handle, indent=2, sort_keys=True)
            handle.write("\n")
        for entry in entries:
            print(
                f"{listing}: {entry['variant']} {entry['style']:<6} "
                f"{entry['bytes'] // 1024:>4}K  {entry['headline']}"
            )
        rendered += len(entries)

    print(f"\n{rendered} cards from {len(listings)} listings")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
