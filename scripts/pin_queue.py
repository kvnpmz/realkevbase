#!/usr/bin/env python3
"""Build a day's pinning batch for hand upload: the cards, in order, with the copy.

Pinterest's web uploader takes one pin at a time, so the cost of manual posting is mostly
decision-making and tab-switching. This writes a dated folder under ``zz_tp/pin_queue/`` holding the chosen cards
numbered in posting order, plus ``queue.md`` to read and ``queue.csv`` to paste from.

Which cards get chosen follows the two rules the drip will use once the API is approved:
a share of the batch goes to listings that have never been pinned, and the rest goes to the
oldest back catalogue. The copy is the same copy the API path would send, because it comes
from the same functions in :mod:`pinterest_pins` - a manual pin cannot drift from an
automated one.

The ledger is read if it exists, so yesterday's batch is not offered again.

Usage:
    python3 scripts/pin_queue.py                  # today, 20 pins
    python3 scripts/pin_queue.py --limit 10
    python3 scripts/pin_queue.py --new-share 0.8  # weight fresher products
"""

from __future__ import annotations

import argparse
import csv
import json
import os
import shutil
import sys
import time

from pinterest_pins import (
    DEFAULT_BOARD,
    DEFAULT_DOCUMENTS,
    DEFAULT_OUTPUT,
    DEFAULT_SITE,
    build_plan,
    fetch_catalog,
    load_state,
    shop_copy,
)
from pinterest_api import default_state_path

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

#: Batches live beside ``output``, not inside it: ``output/<listing>`` is one folder per
#: product, and a queue of mixed listings does not belong in that namespace.
QUEUE_ROOT = os.path.join(os.path.dirname(DEFAULT_OUTPUT), "pin_queue")

#: Cards are ordered page, block, text: the page card is the one that shows the product.
STYLE_ORDER = {"page": 0, "block": 1, "text": 2}


def read_cards(output_dir: str) -> list[dict]:
    """Return every rendered card, in posting preference order."""
    cards: list[dict] = []
    for listing in sorted(os.listdir(output_dir)):
        manifest = os.path.join(output_dir, listing, "pins", "manifest.json")
        if not os.path.exists(manifest):
            continue
        with open(manifest, encoding="utf-8") as handle:
            for entry in json.load(handle):
                entry["listing"] = listing
                cards.append(entry)
    cards.sort(key=lambda card: (card["listing"], STYLE_ORDER.get(card["style"], 9)))
    return cards


def pinned_keys(state: dict) -> set[str]:
    """Return the ``listing@variant`` keys already posted, from either ledger format."""
    keys = set(state.get("pins", {}))
    keys |= {f"{listing}@a" for listing in state.get("created", {})}
    return keys


def choose(cards: list[dict], pinned: set[str], limit: int, new_share: float) -> list[dict]:
    """Pick a batch mixing brand-new listings with the oldest back catalogue.

    A listing counts as new while none of its cards has been posted; that is what gives a
    product its first pin promptly instead of queueing behind a year of backlog.
    """
    posted_listings = {key.split("@")[0] for key in pinned}
    unseen = [card for card in cards if f"{card['listing']}@{card['variant']}" not in pinned]
    fresh = [card for card in unseen if card["listing"] not in posted_listings]
    backlog = [card for card in unseen if card["listing"] in posted_listings]

    # One card per new listing, so a fresh product is not used up before it is seen.
    fresh_first: list[dict] = []
    seen_listings: set[str] = set()
    for card in fresh:
        if card["listing"] in seen_listings:
            continue
        seen_listings.add(card["listing"])
        fresh_first.append(card)

    wanted_fresh = min(len(fresh_first), max(1, round(limit * new_share)))
    batch = fresh_first[:wanted_fresh]
    batch += backlog[: limit - len(batch)]
    if len(batch) < limit:
        # Top up with other listings' first cards before reaching for a second card from a
        # listing already in the batch: variety across products beats depth on one page.
        taken = {id(card) for card in batch}
        batch += [card for card in fresh_first[wanted_fresh:] if id(card) not in taken]
        batch = batch[:limit]
    if len(batch) < limit:
        taken = {id(card) for card in batch}
        batch += [card for card in unseen if id(card) not in taken][: limit - len(batch)]
    return batch


def mark_done(folder: str, state_path: str) -> int:
    """Record a posted batch in the ledger, so the next run offers different cards.

    Without API access nothing else can know a pin exists, so the manual path has to write
    the ledger itself; the keys match the ones the API path would create.
    """
    if not os.path.isdir(folder):
        print(f"no queue folder at {folder}", file=sys.stderr)
        return 2

    state = load_state(state_path)
    pins = state.setdefault("pins", {})
    recorded = 0
    for name in sorted(os.listdir(folder)):
        if not name.endswith(".jpg"):
            continue
        stem = name[:-4]
        _, _, rest = stem.partition("-")
        listing, _, variant = rest.rpartition("-")
        if not listing:
            continue
        pins[f"{listing}@{variant}"] = {
            "source": "manual",
            "file": name,
            "posted_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        }
        recorded += 1

    from pinterest_pins import save_state

    save_state(state_path, state)
    print(f"recorded {recorded} pins in {state_path}")
    return 0


def write_batch(
    batch: list[dict],
    plans: dict[str, dict],
    day: str,
    board: str,
    documents_dir: str = DEFAULT_DOCUMENTS,
) -> str:
    """Copy the cards into a dated folder and write the copy sheet beside them."""
    folder = os.path.join(QUEUE_ROOT, day)
    os.makedirs(folder, exist_ok=True)

    known = [card["listing"] for card in batch]
    rows = []
    for index, card in enumerate(batch, start=1):
        plan = plans.get(card["listing"])
        if plan is None:
            continue
        number = f"{index:02d}"
        name = f"{number}-{card['listing']}-{card['variant']}.jpg"
        shutil.copyfile(os.path.join(ROOT, card["file"]), os.path.join(folder, name))
        rows.append(
            {
                "order": number,
                "file": name,
                "title": plan["title"],
                "description": plan["description"],
                "tags": shop_copy(card["listing"], known, documents_dir).get("tags", ""),
                "link": plan["link"],
                "board": board,
            }
        )

    with open(os.path.join(folder, "queue.csv"), "w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]) if rows else [])
        writer.writeheader()
        writer.writerows(rows)

    lines = [f"# Pins for {day}", "", f"Board: {board}", ""]
    for row in rows:
        lines += [
            f"## {row['order']} · {row['file']}",
            "",
            f"**Title**  ",
            row["title"],
            "",
            f"**Description**  ",
            row["description"],
            "",
        ]
        if row["tags"]:
            lines += [row["tags"], ""]
        lines += [
            f"**Link**  ",
            row["link"],
            "",
        ]
    with open(os.path.join(folder, "queue.md"), "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines))

    return folder


def main(argv: list[str] | None = None) -> int:
    """Choose a batch, copy the cards, write the copy sheet."""
    parser = argparse.ArgumentParser(
        description="Build a day's manual pinning batch.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument("--limit", type=int, default=20)
    parser.add_argument("--new-share", type=float, default=0.6)
    parser.add_argument("--day", default=None, help="folder name (default: today, YYYY-MM-DD)")
    parser.add_argument("--board", default=DEFAULT_BOARD)
    parser.add_argument("--state", default=default_state_path())
    parser.add_argument("--output", default=DEFAULT_OUTPUT)
    parser.add_argument("--documents", default=DEFAULT_DOCUMENTS)
    parser.add_argument("--site-url", default=DEFAULT_SITE)
    parser.add_argument("--utm", action="store_true", help="append utm_* campaign tags")
    parser.add_argument("--done", action="store_true",
                        help="record the day's folder as posted, then stop")
    args = parser.parse_args(argv)

    day = args.day or time.strftime("%Y-%m-%d")
    if args.done:
        return mark_done(os.path.join(QUEUE_ROOT, day), args.state)

    cards = read_cards(args.output)
    if not cards:
        print("no cards rendered yet: run scripts/pin_cards.py --all first", file=sys.stderr)
        return 2

    state = load_state(args.state)
    batch = choose(cards, pinned_keys(state), args.limit, args.new_share)
    if not batch:
        print("nothing left to post: every card is already in the ledger")
        return 0

    catalog = fetch_catalog("https://api.realkevbase.com")
    plans = {
        plan.listing: {
            "title": plan.title,
            "description": plan.description,
            "link": plan.link,
            "alt_text": plan.alt_text,
        }
        for plan in build_plan(catalog, args.output, args.site_url, utm=args.utm)[0]
    }

    folder = write_batch(batch, plans, day, args.board, args.documents)
    print(f"{len(batch)} pins for {day}")
    for card in batch:
        print(f"  {card['listing']:<34} {card['variant']}  {card['style']}")
    print(f"\ncards and copy sheet: {os.path.relpath(folder, ROOT)}")
    print("post them, then record them: python3 scripts/pin_queue.py --done")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
