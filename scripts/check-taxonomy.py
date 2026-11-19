#!/usr/bin/env python3
"""Check the storefront taxonomy in ``scripts/taxonomy.json`` against the real catalogue.

The taxonomy only holds up if every product lands in exactly one bucket and every sales
family rolls into one, so this asserts both instead of trusting the prefix lists to stay
tidy. It also prints where the two ways of classifying a product disagree — the listing id
is matched by prefix, the shop name is matched by the sales keyword rules — because those
disagreements are what put a warehouse bulk list under `moving-out` (the words "house" in
"warehouse") and dumped a third of the shop into `guides`.

Read-only: it fetches the catalogue for names, or falls back to the listing copy on disk.

Usage:
    python3 scripts/check-taxonomy.py            # assertions and a per-bucket count
    python3 scripts/check-taxonomy.py --verbose   # also list every product and its bucket
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import os
import sys
import urllib.error
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TAXONOMY = os.path.join(ROOT, "scripts", "taxonomy.json")
FAMILIES = os.path.join(ROOT, "zz_tp", "scripts", "families.py")
OUTPUT = os.path.join(ROOT, "zz_tp", "output")
DOCUMENTS = os.path.join(ROOT, "zz_tp", "documents")
API_URL = "https://api.realkevbase.com"


def load_taxonomy() -> dict:
    with open(TAXONOMY, encoding="utf-8") as handle:
        return json.load(handle)


def load_families():
    """Load families.py by path: it lives in the printable pipeline, not on sys.path."""
    spec = importlib.util.spec_from_file_location("zz_tp_families", FAMILIES)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def shop_names() -> dict[str, str]:
    """Return the catalogue's name per listing id, or the folder name when offline."""
    url = f"{API_URL}/api/products"
    request = urllib.request.Request(url, headers={"Accept": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            payload = json.load(response)
        return {
            str(item["listingId"]): str(item.get("name") or "")
            for item in payload
            if item.get("listingId")
        }
    except (urllib.error.URLError, TimeoutError, json.JSONDecodeError, KeyError):
        print(f"could not read {url}; names fall back to the listing folders", file=sys.stderr)
        return {}


def listings() -> list[str]:
    """Return every rendered listing, which is the set the shop actually sells."""
    return sorted(
        name
        for name in os.listdir(OUTPUT)
        if os.path.isdir(os.path.join(OUTPUT, name))
    )


def slug_of(listing: str) -> str:
    return listing.replace("_", "-")


def buckets_for(listing: str, buckets: list[dict]) -> list[str]:
    """Every bucket whose prefix matches; more than one is an ambiguity to fix."""
    slug = slug_of(listing)
    return [
        bucket["slug"]
        for bucket in buckets
        if any(slug.startswith(prefix) for prefix in bucket["prefixes"])
    ]


def bucket_of_family(families, buckets: list[dict], name: str) -> tuple[str, str]:
    """Return the sales family and the bucket it rolls into, for one shop name."""
    family = families.family_of(name) if name else "other"
    for bucket in buckets:
        if family in bucket.get("families", []):
            return family, bucket["slug"]
    return family, ""


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Validate the storefront taxonomy.")
    parser.add_argument("--verbose", action="store_true", help="list every product and bucket")
    args = parser.parse_args(argv)

    taxonomy = load_taxonomy()
    buckets = taxonomy["buckets"]
    unmapped = {item["family"] for item in taxonomy.get("unmappedFamilies", [])}
    families = load_families()
    names = shop_names()
    products = listings()

    failures: list[str] = []

    # A slug or a prefix defined twice makes the answer depend on list order, not on intent.
    slugs = [bucket["slug"] for bucket in buckets]
    for slug in {slug for slug in slugs if slugs.count(slug) > 1}:
        failures.append(f"bucket {slug!r} is defined twice")
    seen_prefix: dict[str, str] = {}
    for bucket in buckets:
        for prefix in bucket["prefixes"]:
            if prefix in seen_prefix:
                failures.append(
                    f"prefix {prefix!r} is in both {seen_prefix[prefix]!r} and {bucket['slug']!r}"
                )
            seen_prefix[prefix] = bucket["slug"]

    # Every product, exactly one bucket.
    counts: dict[str, int] = {bucket["slug"]: 0 for bucket in buckets}
    assigned: dict[str, str] = {}
    for listing in products:
        matched = buckets_for(listing, buckets)
        if not matched:
            failures.append(f"{listing}: matches no bucket")
            continue
        if len(matched) > 1:
            failures.append(f"{listing}: matches {len(matched)} buckets ({', '.join(matched)})")
            continue
        assigned[listing] = matched[0]
        counts[matched[0]] += 1

    for slug, count in counts.items():
        if count == 0:
            failures.append(f"bucket {slug!r} holds no product")

    # Every sales family either rolls into a bucket or says why it does not.
    known = {family for family, _ in getattr(families, "RULES", [])}
    placed = {family for bucket in buckets for family in bucket.get("families", [])}
    for family in sorted(placed - known):
        failures.append(f"family {family!r} is mapped but families.py does not define it")
    for family in sorted(known - placed - unmapped):
        failures.append(f"family {family!r} rolls into no bucket and is not declared unmapped")
    for family in sorted(placed & unmapped):
        failures.append(f"family {family!r} is both mapped and declared unmapped")

    # The two classifiers should agree; where they do not, the id prefix wins (it is the
    # deliberate choice) but the name is evidence the shop copy disagrees with the shelf.
    disagree = []
    for listing, bucket in sorted(assigned.items()):
        family, other = bucket_of_family(families, buckets, names.get(listing, ""))
        if other and other != bucket:
            disagree.append((listing, bucket, other, family, names.get(listing, "")))

    # Report.
    print(f"{len(products)} listings · {len(buckets)} buckets · {len(valid_prefixes(buckets))} prefixes")
    print()
    for bucket in buckets:
        print(f"  {counts[bucket['slug']]:>4}  {bucket['slug']:<14} {bucket['label']}")
    if unmapped:
        print(f"\n  unmapped families: {', '.join(sorted(unmapped))}")

    if args.verbose:
        print("\nassignments")
        for listing, bucket in sorted(assigned.items()):
            print(f"  {bucket:<14} {listing}")

    if disagree:
        print(f"\nname and id disagree on {len(disagree)} listing(s) (id prefix wins)")
        for listing, bucket, other, family, name in disagree:
            print(f"  {listing:<32} id→{bucket:<13} name→{other:<13} [{family}] {name}")

    if failures:
        print(f"\n{len(failures)} problem(s)")
        for failure in failures:
            print(f"  FAIL {failure}")
        return 1

    print("\ntaxonomy holds: one bucket per product, one bucket per family")
    return 0


def valid_prefixes(buckets: list[dict]) -> set[str]:
    return {prefix for bucket in buckets for prefix in bucket["prefixes"]}


if __name__ == "__main__":
    raise SystemExit(main())
