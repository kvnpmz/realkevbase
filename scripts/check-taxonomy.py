#!/usr/bin/env python3
"""Check that every printable declares a shelf the shop actually has.

The shelf is `category = "kidney"` in ``zz_tp/documents/<listing>/config.tl`` — declared
once, next to the document's id, and inherited by every edition because a variant is
created by copying its base config. This asserts the declarations rather than guessing a
shelf from a listing name: the old prefix lists needed a new entry for every spelling the
shop invented (`fatty_liver_food_list` beside `fattyliver_foodlist`), and a prefix like
`house` quietly claimed `warehouse_bulk_list`.

It also re-checks the sales side: families.py's keyword families must each roll into one
shelf, and where a product's family disagrees with its declared shelf that gets printed —
as an audit of the two vocabularies, never as a decision.

Read-only. Usage:
    python3 scripts/check-taxonomy.py             # assertions and a per-shelf count
    python3 scripts/check-taxonomy.py --verbose    # also list every document and its shelf
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TAXONOMY = os.path.join(ROOT, "scripts", "taxonomy.json")
FAMILIES = os.path.join(ROOT, "zz_tp", "scripts", "families.py")
DOCUMENTS = os.path.join(ROOT, "zz_tp", "documents")
HUB_LINKS = os.path.join(ROOT, "src", "realkevbase-web", "src", "data", "hubLinks.ts")

CATEGORY = re.compile(r"^\s*category\s*=\s*\"([^\"]+)\"", re.M)


def load_taxonomy() -> dict:
    with open(TAXONOMY, encoding="utf-8") as handle:
        return json.load(handle)


def load_families():
    """Load families.py by path: it lives in the printable pipeline, not on sys.path."""
    spec = importlib.util.spec_from_file_location("zz_tp_families", FAMILIES)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def shop_labels() -> dict[str, str]:
    """Return slug -> label from the module the chips and hubs share."""
    with open(HUB_LINKS, encoding="utf-8") as handle:
        source = handle.read()
    return dict(re.findall(r"slug:\s*'([^']+)',\s*label:\s*'([^']+)'", source))


def declared() -> tuple[dict[str, str], list[str]]:
    """Return the shelf declared in each document's config, and the folders that lack one."""
    found: dict[str, str] = {}
    missing: list[str] = []
    for name in sorted(os.listdir(DOCUMENTS)):
        path = os.path.join(DOCUMENTS, name, "config.tl")
        if not os.path.exists(path):
            missing.append(name)
            continue
        with open(path, encoding="utf-8") as handle:
            match = CATEGORY.search(handle.read())
        if match:
            found[name] = match.group(1)
        else:
            missing.append(name)
    return found, missing


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Validate the storefront taxonomy.")
    parser.add_argument("--verbose", action="store_true", help="list every document and shelf")
    args = parser.parse_args(argv)

    taxonomy = load_taxonomy()
    buckets: list[str] = taxonomy["buckets"]
    mapping: dict[str, str] = taxonomy["families"]
    unmapped = {item["family"] for item in taxonomy.get("unmappedFamilies", [])}
    labels = shop_labels()
    families = load_families()
    shelves, missing = declared()

    failures: list[str] = []

    # The shop's chip list and this file must name the same shelves, in either order.
    if set(labels) != set(buckets):
        only_shop = sorted(set(labels) - set(buckets))
        only_file = sorted(set(buckets) - set(labels))
        failures.append(
            f"taxonomy.json and hubLinks.ts disagree: {only_file} only in the file, "
            f"{only_shop} only in the shop"
        )
    for slug in {slug for slug in buckets if buckets.count(slug) > 1}:
        failures.append(f"shelf {slug!r} is listed twice")

    # Every document declares a shelf the shop has.
    counts: dict[str, int] = {slug: 0 for slug in buckets}
    for name, missing_one in [(name, True) for name in missing]:
        failures.append(f"{name}: no category in config.tl")
    for name, shelf in sorted(shelves.items()):
        if shelf not in counts:
            failures.append(f"{name}: category {shelf!r} is not a shelf the shop has")
            continue
        counts[shelf] += 1
    for slug, count in counts.items():
        if count == 0:
            failures.append(f"shelf {slug!r} holds no document")

    # Every sales family rolls into one shelf, or says why it does not.
    known = {family for family, _ in getattr(families, "RULES", [])}
    for family in sorted(set(mapping) - known):
        failures.append(f"family {family!r} is mapped but families.py does not define it")
    for family in sorted(known - set(mapping) - unmapped):
        failures.append(f"family {family!r} rolls into no shelf and is not declared unmapped")
    for family in sorted(set(mapping) & unmapped):
        failures.append(f"family {family!r} is both mapped and declared unmapped")
    for family, shelf in sorted(mapping.items()):
        if shelf not in counts:
            failures.append(f"family {family!r} rolls into {shelf!r}, which is not a shelf")

    # Audit, not decision: does the sales vocabulary agree with the declared shelf?
    disagree = []
    for name, shelf in sorted(shelves.items()):
        family = families.family_of(name.replace("_", " "))
        other = mapping.get(family, "")
        if other and other != shelf:
            disagree.append((name, shelf, other, family))

    print(f"{len(shelves)} documents · {len(buckets)} shelves · {len(mapping)} sales families")
    print()
    for slug in buckets:
        label = labels.get(slug, "(no label)")
        print(f"  {counts.get(slug, 0):>4}  {slug:<14} {label}")
    if unmapped:
        print(f"\n  unmapped families: {', '.join(sorted(unmapped))}")

    if args.verbose:
        print("\ndeclarations")
        for name, shelf in sorted(shelves.items()):
            print(f"  {shelf:<14} {name}")

    if disagree:
        print(f"\nname and shelf disagree on {len(disagree)} document(s) — the declaration wins")
        for name, shelf, other, family in disagree:
            print(f"  {name:<32} shelf {shelf:<13} family {family} → {other}")

    if failures:
        print(f"\n{len(failures)} problem(s)")
        for failure in failures:
            print(f"  FAIL {failure}")
        return 1

    print("\ntaxonomy holds: every document declares one shelf, every family rolls into one")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
