#!/usr/bin/env python3
"""Create SaveSimple pins from the storefront catalog.

Every pin is derived, never hand-written: the words come from the shop copy the listing was
written with, the link is the guide's canonical page carrying campaign tags so the
storefront's analytics can attribute the visit, and the artwork is the full-page render the
generator wrote to ``zz_tp/output/<listing>/images/``. That way a pin cannot advertise a
product the shop stopped selling, and a new guide needs no extra bookkeeping to get one.

Pinterest accepts the PNG either as a public URL or as bytes inside the request. Bytes are
the default: the renders are already local, and sending them avoids publishing anything
just for Pinterest. ``--image-source=url`` switches to
``/api/products/<id>/pin-image`` for the day that route exists.

The first run is a dry run. It prints the plan and, when a token is stored, which links
are already pinned anywhere on the account, so a manually curated profile is never
duplicated. ``--apply`` creates pins, ``--limit`` caps how many this run may add.

Usage:
    python3 scripts/pinterest_pins.py                     # plan only
    python3 scripts/pinterest_pins.py --dedupe=account    # plan + already pinned
    python3 scripts/pinterest_pins.py --apply --limit 10  # create ten
    python3 scripts/pinterest_pins.py --apply --all       # everything still missing
    python3 scripts/pinterest_pins.py --apply --only first-apartment-checklist
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

from dataclasses import dataclass

from listing_files import find_cover_image, parent_slug, read_listing, strip_edition
from pinterest_api import (
    DESCRIPTION_LIMIT,
    Pinterest,
    PinterestError,
    TITLE_LIMIT,
    clip,
    default_state_path,
    default_token_path,
)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

DEFAULT_OUTPUT = os.path.join(ROOT, "zz_tp", "output")
#: Pins point at the guides host, not the apex: Pinterest blocked realkevbase.com as
#: possible spam (appeal open), and a subdomain carries its own reputation. Both hosts
#: serve the same build, so switching back is this one line.
DEFAULT_SITE = "https://guides.realkevbase.com"
DEFAULT_API = "https://api.realkevbase.com"
DEFAULT_BOARD = "Printables & Planners"
USER_AGENT = "realkevbase-pinterest/1.0 (+https://realkevbase.com)"

#: Suffix that tells a shopper the pin is a printable rather than a blog post.
TITLE_SUFFIX = " — Printable PDF"

#: Where the shop copy for each listing is written; the source of the pin keywords.
DEFAULT_DOCUMENTS = os.path.join(ROOT, "zz_tp", "documents")

#: Segments that name the file format rather than the subject. They fill Pinterest's
#: 100-character title without matching anything a shopper searches for.
FORMAT_WORDS = frozenset(
    {
        "a4",
        "a4 letter",
        "a4 letter pdf",
        "a4 letter size",
        "a4 pdf",
        "digital download",
        "digital file",
        "instant download",
        "letter size",
        "pdf",
        "pdf download",
        "print",
        "printable",
        "printable pdf",
    }
)

#: How many keyword phrases a title may pack in before it reads as keyword stuffing.
MAX_TITLE_SEGMENTS = 3


@dataclass(frozen=True)
class PinPlan:
    """One listing, ready to become a pin."""

    listing: str
    slug: str
    product_id: str
    name: str
    link: str
    title: str
    description: str
    alt_text: str
    image_path: str
    image_bytes: int

    @property
    def image_kb(self) -> int:
        """Size of the cover render, rounded to the nearest kilobyte."""
        return round(self.image_bytes / 1024)


def fetch_catalog(api_url: str) -> list[dict]:
    """Read the public catalog the storefront itself uses.

    Args:
        api_url: Origin of the API, for example ``https://api.realkevbase.com``.

    Returns:
        The product list from ``GET /api/products``.

    Raises:
        SystemExit: When the catalog cannot be fetched or is not a list.
    """
    url = f"{api_url.rstrip('/')}/api/products"
    request = urllib.request.Request(url, headers={"Accept": "application/json", "User-Agent": USER_AGENT})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            payload = json.load(response)
    except (urllib.error.URLError, json.JSONDecodeError) as error:
        raise SystemExit(f"could not read the catalog at {url}: {error}") from error

    if not isinstance(payload, list):
        raise SystemExit(f"{url} did not return a product list")
    return payload


def shop_copy(listing: str, known: list[str], documents_dir: str) -> dict[str, str]:
    """Return the shop copy written for a listing, or the copy of the edition it came from.

    Colour and translation editions ship without their own ``listing.txt``
    (``emergency_gobag_pink`` renders from ``emergency_gobag``), and the oldest listings
    predate the file entirely, so a miss is expected rather than an error.

    Args:
        listing: Listing id, which is also the ``zz_tp/documents`` folder name.
        known: Every listing id in the catalog, used to find a parent edition.
        documents_dir: Path to ``zz_tp/documents``.

    Returns:
        The ``title``, ``tags`` and ``description`` blocks, empty when there is no copy.
    """
    for candidate in (listing, parent_slug(listing, known)):
        if not candidate:
            continue
        copy = read_listing(os.path.join(documents_dir, candidate))
        if copy.get("title") or copy.get("description"):
            return copy
    return {}


def keyword_segments(title: str) -> list[str]:
    """Split a pipe-separated shop title into the phrases a pin title can use."""
    segments: list[str] = []
    for raw in title.split("|"):
        phrase = re.sub(r"\s+", " ", raw).strip(" ,-–—")
        if len(phrase) < 3 or phrase.lower() in FORMAT_WORDS:
            continue
        segments.append(phrase)
    return segments


def keyword_tags(tags: str) -> list[str]:
    """Return the shop tags that name a subject rather than a file format."""
    return [
        tag.strip()
        for tag in tags.split(",")
        if tag.strip() and tag.strip().lower() not in FORMAT_WORDS
    ]


def pin_title(name: str, segments: list[str]) -> str:
    """Pack keyword phrases into Pinterest's title budget without exceeding it.

    Phrases are added in the shop's own priority order until the next one would not fit
    with :data:`TITLE_SUFFIX`, so a title is never truncated mid-phrase.

    Every dash in a title becomes a pipe, which is the separator Pinterest boards already
    read as a phrase break: "First Apartment Essentials — Plum | Printable PDF" would
    otherwise carry two different separator characters in one line. Descriptions keep
    their dashes, where they punctuate a sentence instead of listing phrases.
    """
    chosen: list[str] = []
    for segment in segments:
        if chosen and len(" | ".join([*chosen, segment])) + len(TITLE_SUFFIX) > TITLE_LIMIT:
            break
        if not chosen and len(segment) + len(TITLE_SUFFIX) > TITLE_LIMIT:
            continue
        chosen.append(segment)
        if len(chosen) == MAX_TITLE_SEGMENTS:
            break

    if not chosen:
        return piped(clip(f"{name}{TITLE_SUFFIX}", TITLE_LIMIT))
    joined = " | ".join(chosen)
    if len(joined) + len(TITLE_SUFFIX) <= TITLE_LIMIT:
        return piped(f"{joined}{TITLE_SUFFIX}")
    return piped(clip(joined, TITLE_LIMIT))


def piped(title: str) -> str:
    """Turns every dash separator in a title into a pipe and tidies the spacing.

    The em dash reaches a title from two places: the suffix, and a product name such as
    "First Apartment Essentials — Costs". Both become "|", and repeated pipes collapse so a
    name that already ends in a dash cannot produce " | | ".
    """
    text = title.replace("\u2014", "|").replace("\u2013", "|")
    text = re.sub(r"\s*(\|\s*)+", " | ", text)
    return text.strip(" |")


def pin_description(lead: str, name: str, tags: list[str], price_text: str) -> str:
    """Build a description that leads with the subject, then the offer.

    Pinterest weighs the opening of a description most, so the shop's own sentence goes
    first and the price and delivery promise stay at the end where they are read but do not
    crowd out the subject. The tags are not repeated here: they are the guide's own TAGS
    line, and the manual queue prints them directly under the description, so repeating six
    of them as a "Keywords:" sentence read as keyword stuffing twice over.
    """
    parts = [lead] if lead else [f"{name} from SaveSimple."]
    parts.append(
        f"Instant PDF download from SaveSimple, {price_text}."
        if lead
        else f"Instant PDF download, {price_text}."
    )
    return clip(" ".join(parts), DESCRIPTION_LIMIT)


def build_plan(
    catalog: list[dict],
    output_dir: str,
    site_url: str,
    documents_dir: str = DEFAULT_DOCUMENTS,
    utm: bool = False,
) -> tuple[list[PinPlan], list[str]]:
    """Turn catalog products into pins, reporting the ones that cannot be pinned.

    The artwork and the product facts come from the catalog, but the words come from the
    shop copy in ``zz_tp/documents``: its TITLE block holds the keyword phrases each guide
    is sold on, and TAGS holds the phrases shoppers actually type. The catalog name is
    only the first of those phrases with the format words trimmed off, so using it alone
    would drop most of the keywords the listing was written around.

    Args:
        catalog: Products from ``GET /api/products``.
        output_dir: The generator's ``output`` folder.
        site_url: Storefront origin used for the guide links.
        documents_dir: The shop copy folder, for the keyword blocks.
        utm: Append campaign tags to the link. Off by default: Pinterest's spam filter
            blocks links carrying tracking parameters from a domain it has not seen, and
            an unclaimed domain has no reputation to spend on the first test pin.

    Returns:
        The pins in listing order, plus one message per skipped product.
    """
    plans: list[PinPlan] = []
    skipped: list[str] = []
    known = [str(item.get("listingId")) for item in catalog if item.get("listingId")]

    for product in sorted(catalog, key=lambda item: str(item.get("listingId") or "")):
        if product.get("fulfillment") != "DigitalPdf":
            continue
        listing = str(product.get("listingId") or "")
        if not listing:
            skipped.append(f"{product.get('name')}: no listing id")
            continue

        image = find_cover_image(os.path.join(output_dir, listing, "images"))
        if image is None:
            skipped.append(f"{listing}: no full-page render in images/")
            continue

        name = strip_edition(str(product.get("name") or listing).strip())
        slug = listing.replace("_", "-").lower()
        base = str(product.get("description") or "").strip()
        price = float(product.get("price") or 0)
        price_text = f"${price:g}"

        copy = shop_copy(listing, known, documents_dir)
        segments = keyword_segments(copy.get("title", ""))
        tags = keyword_tags(copy.get("tags", ""))
        lead = copy.get("description", "").strip() or base

        plans.append(
            PinPlan(
                listing=listing,
                slug=slug,
                product_id=str(product.get("id") or ""),
                name=name,
                link=(
                    f"{site_url.rstrip('/')}/guides/{slug}"
                    + (
                        f"?utm_source=pinterest&utm_medium=organic"
                        f"&utm_campaign=savesimple&utm_content={slug}"
                        if utm
                        else ""
                    )
                ),
                title=pin_title(name, segments),
                description=pin_description(lead, name, tags, price_text),
                alt_text=clip(
                    f"{', '.join(segments[:MAX_TITLE_SEGMENTS])}, printable PDF first page preview"
                    if segments
                    else f"{name}, printable PDF first page preview",
                    500,
                ),
                image_path=image,
                image_bytes=os.path.getsize(image),
            )
        )

    return plans, skipped


def normalise_link(url: str) -> str:
    """Reduce a pin link to host and path so ``http``/``https`` and ``www.`` compare equal."""
    if not url:
        return ""
    parsed = urllib.parse.urlsplit(url.strip())
    host = parsed.netloc.lower()
    if host.startswith("www."):
        host = host[4:]
    path = (parsed.path or "/").rstrip("/") or "/"
    return f"{host}{path}".lower()


def load_state(path: str) -> dict:
    """Read the local pin ledger, or start an empty one."""
    try:
        with open(path, encoding="utf-8") as handle:
            return json.load(handle)
    except FileNotFoundError:
        return {"created": {}}
    except json.JSONDecodeError:
        return {"created": {}}


def save_state(path: str, state: dict) -> None:
    """Write the ledger atomically so an interrupted run keeps what it already recorded."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    temporary = f"{path}.tmp"
    with open(temporary, "w", encoding="utf-8") as handle:
        json.dump(state, handle, indent=2, sort_keys=True)
        handle.write("\n")
    os.replace(temporary, path)


def existing_links(client: Pinterest, scope: str, board_name: str) -> set[str]:
    """Collect the links already pinned, on one board or across the whole account."""
    links: set[str] = set()
    if scope == "account":
        for board, pin in client.all_pins():
            if pin.get("link"):
                links.add(normalise_link(str(pin["link"])))
    elif scope == "board":
        board = client.find_board(board_name)
        if board is None:
            print(f"board {board_name!r} does not exist yet; nothing to compare against")
            return links
        for pin in client.list_board_pins(str(board["id"])):
            if pin.get("link"):
                links.add(normalise_link(str(pin["link"])))
    return links


def print_plan(rows: list[PinPlan], pinned: set[str], done: set[str]) -> None:
    """Print one line per pin with the facts that decide whether it is safe to create."""
    header = f"{'#':>3}  {'listing':<34} {'chars':>5} {'image':>6}  state"
    print(header)
    print("-" * len(header))
    for index, row in enumerate(rows, start=1):
        state = "pinned" if row.link in pinned else ("ledger" if row.listing in done else "")
        print(
            f"{index:>3}  {row.listing[:34]:<34} {len(row.title):>5} {row.image_kb:>5}K  {state}"
        )


def main(argv: list[str] | None = None) -> int:
    """Parse arguments, build the plan, and optionally create pins."""
    parser = argparse.ArgumentParser(
        description="Create Pinterest pins from the SaveSimple catalog.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument("--apply", action="store_true", help="create pins; without it nothing is written")
    parser.add_argument("--limit", type=int, default=10, help="maximum pins to create this run (default 10)")
    parser.add_argument("--all", action="store_true", help="create every missing pin, ignoring --limit")
    parser.add_argument(
        "--only", action="append", default=[], metavar="SLUG", help="restrict to one listing (repeatable)"
    )
    parser.add_argument("--board", default=DEFAULT_BOARD, help=f"board name (default {DEFAULT_BOARD!r})")
    parser.add_argument("--create-board", action="store_true", help="create the board when it does not exist")
    parser.add_argument(
        "--dedupe",
        choices=("account", "board", "none"),
        default="account",
        help="where to look for already pinned links (default account)",
    )
    parser.add_argument(
        "--image-source",
        choices=("base64", "url"),
        default="base64",
        help="send the PNG bytes (default) or point Pinterest at /pin-image",
    )
    parser.add_argument("--image-base-url", default=DEFAULT_API, help="API origin for --image-source=url")
    parser.add_argument("--site-url", default=DEFAULT_SITE, help="storefront origin for pin links")
    parser.add_argument(
        "--utm",
        action="store_true",
        help="append utm_* campaign tags to pin links (default off; Pinterest blocks them "
        "on domains it has not seen)",
    )
    parser.add_argument("--api-url", default=DEFAULT_API, help="API origin serving the catalog")
    parser.add_argument("--output", default=DEFAULT_OUTPUT, help="generator output folder (default zz_tp/output)")
    parser.add_argument("--sleep", type=float, default=3.0, help="seconds between pin creations (default 3)")
    parser.add_argument("--token-file", help=f"Pinterest token (default {default_token_path()})")
    parser.add_argument("--state", default=default_state_path(), help="local pin ledger")
    parser.add_argument("--expect-account", help="refuse to pin when the token belongs to another account")
    parser.add_argument("--force", action="store_true", help="ignore the local ledger and create anyway")
    parser.add_argument("--json", action="store_true", help="print the plan as JSON")
    args = parser.parse_args(argv)

    try:
        has_token = Pinterest.has_token(args.token_file)
    except PinterestError as error:
        print(error, file=sys.stderr)
        return 2
    if args.apply and not has_token:
        print("no Pinterest token: run scripts/pinterest_auth.py first", file=sys.stderr)
        return 2

    catalog = fetch_catalog(args.api_url)
    plans, skipped = build_plan(catalog, args.output, args.site_url, utm=args.utm)

    if args.only:
        wanted = {value.replace("_", "-").lower() for value in args.only} | {value.lower() for value in args.only}
        plans = [row for row in plans if row.slug in wanted or row.listing.lower() in wanted]
        if not plans:
            print(f"no listing matches {', '.join(args.only)}", file=sys.stderr)
            return 2

    state = load_state(args.state)
    done = set() if args.force else set(state.get("created", {}))

    client: Pinterest | None = None
    pinned: set[str] = set()
    account = ""

    if args.dedupe != "none":
        try:
            if has_token:
                client = Pinterest(args.token_file)
                account = str(client.token.get("account") or "")
                if args.expect_account and account and account != args.expect_account:
                    print(
                        f"token belongs to {account!r}, not {args.expect_account!r}; run "
                        "scripts/pinterest_auth.py again.",
                        file=sys.stderr,
                    )
                    return 2
                pinned = existing_links(client, args.dedupe, args.board)
            else:
                print(
                    "no token stored: skipping de-duplication (run scripts/pinterest_auth.py first)",
                    file=sys.stderr,
                )
        except PinterestError as error:
            print(f"could not read existing pins: {error}", file=sys.stderr)
            return 1

    if args.json:
        print(
            json.dumps(
                [
                    {
                        "listing": row.listing,
                        "slug": row.slug,
                        "link": row.link,
                        "title": row.title,
                        "description": row.description,
                        "image": row.image_path,
                        "imageBytes": row.image_bytes,
                        "status": "pinned" if row.link in pinned else ("ledger" if row.listing in done else "new"),
                    }
                    for row in plans
                ],
                indent=2,
            )
        )
    else:
        print_plan(plans, pinned, done)
        in_ledger = len(done & {row.listing for row in plans})
        print(f"\n{len(plans)} listings, {len(pinned)} links already on Pinterest, {in_ledger} in the ledger")
        if skipped:
            print(f"{len(skipped)} skipped:")
            for reason in skipped:
                print(f"  - {reason}")
        if account:
            print(f"account: {account}")

    pending = [row for row in plans if row.link not in pinned and row.listing not in done]
    if not args.apply:
        summary = f"{len(pending)} pins would be created. Re-run with --apply --limit N to create them."
        print(f"\n{summary}", file=sys.stderr if args.json else sys.stdout)
        return 0

    if not client:
        if not Pinterest.has_token(args.token_file):
            print("no Pinterest token: run scripts/pinterest_auth.py first", file=sys.stderr)
            return 2
        client = Pinterest(args.token_file)

    try:
        board = client.ensure_board(
            args.board,
            create=args.create_board,
            description="SaveSimple printables, from realkevbase.com",
        )
    except PinterestError as error:
        print(error, file=sys.stderr)
        return 2

    budget = None if args.all else max(0, args.limit)
    batch = pending if budget is None else pending[:budget]
    if not batch:
        print("nothing to create; every listing is already pinned or recorded")
        return 0

    print(f"\ncreating {len(batch)} pins on {board.get('name')!r}")
    failures = 0
    for index, row in enumerate(batch, start=1):
        try:
            if args.image_source == "url":
                image_bytes = None
                image_url = f"{args.image_base_url.rstrip('/')}/api/products/{row.product_id}/pin-image"
            else:
                with open(row.image_path, "rb") as handle:
                    image_bytes = handle.read()
                image_url = None

            pin = client.create_pin(
                board_id=str(board["id"]),
                title=row.title,
                description=row.description,
                link=row.link,
                alt_text=row.alt_text,
                image_bytes=image_bytes,
                image_url=image_url,
            )
        except (PinterestError, OSError) as error:
            failures += 1
            print(f"[{index}/{len(batch)}] {row.listing}: FAILED — {error}")
            continue

        created = state.setdefault("created", {})
        created[row.listing] = {
            "pin_id": pin.get("id"),
            "link": row.link,
            "board": board.get("name"),
            "image": os.path.relpath(row.image_path, ROOT),
            "created_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        }
        save_state(args.state, state)
        print(f"[{index}/{len(batch)}] {row.listing}: pin {pin.get('id')} → {row.link}")

        if index < len(batch):
            time.sleep(max(0.0, args.sleep))

    remaining = len(pending) - len(batch)
    print(f"\ncreated {len(batch) - failures}, failed {failures}, {remaining} still pending")
    if remaining:
        print("run again with --apply to continue where this stopped")
    return 1 if failures else 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        print("\ninterrupted; pins created so far are in the ledger", file=sys.stderr)
        raise SystemExit(130)
