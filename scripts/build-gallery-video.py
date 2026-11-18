#!/usr/bin/env python3
"""Render a short, loopable gallery clip from the page render already on disk.

A five-second clip of a printable is mostly a still page moving, so the motion has to be
seamless or it reads as a twitchy TV. ffmpeg's ``zoompan`` moves its crop window in whole
pixels: on a 2550px render a 7% push-in travels under one pixel per frame, so the picture
holds for two or three frames and then lurches. This script samples the crop in floating
point with Pillow (bicubic, sub-pixel) and pipes raw frames into x264, which measured 100x
more uniform frame-to-frame motion than ``zoompan`` on the same source.

The move is a ping-pong: push in for half the clip, ease back out for the rest. It ends on
exactly the frame it started on, so a muted ``loop`` plays with no visible cut — a one-way
push-in cannot loop. Both ends and the turnaround sit at zero velocity, so there is nothing
to see at the seam.

Clips are written to ``zz_tp/output/<listing>/video/<slug>.mp4`` — generated output, never
committed. Dense moving text is close to the worst case for H.264, so the bitrate is capped
rather than left to CRF: CRF 20 lands near 9 MB for five seconds, while 1.2 Mbps holds the
text together at roughly 600 KB.

Usage:
    python3 scripts/build-gallery-video.py                        # dry run, every listing
    python3 scripts/build-gallery-video.py --only apartment_checklist --apply
    python3 scripts/build-gallery-video.py --all --limit 5 --apply
    python3 scripts/build-gallery-video.py --only apartment_checklist --upload
"""

from __future__ import annotations

import argparse
import math
import shutil
import subprocess
import sys
from pathlib import Path

from PIL import Image

from listing_files import find_cover_image
from storage_auth import storage_arguments

DEFAULT_SOURCE = "zz_tp/output"
OUTPUT_DIR = "video"
DEFAULT_WIDTH = 1080
DEFAULT_DURATION = 5.0
DEFAULT_FPS = 30
DEFAULT_ZOOM = 1.07
DEFAULT_KBPS = 1200
#: Container holding public marketing media. It must exist and be anonymous-read; the paid
#: PDFs live in the private ``pdfs`` container and are never published here.
DEFAULT_CONTAINER = "media"
DEFAULT_PREFIX = "guides/video"
#: Five minutes, not a day: a re-rendered clip is live again quickly, and a video is worth
#: less to cache than the artwork the page paints with.
CACHE_CONTROL = "public, max-age=300"
# Sub-pixel sampling needs headroom; 2x the output keeps text crisp without paying for a
# full-resolution transform on every frame.
OVERSAMPLE = 2


def smoothstep(value: float) -> float:
    """Zero velocity at both ends, used so the zoom starts and stops without a visible kick."""
    return value * value * (3 - 2 * value)


def zoom_at(progress: float, zoom: float) -> float:
    """Ping-pong zoom factor: 1.0 -> zoom -> 1.0 across the clip, eased at both ends."""
    peak = 1 - abs(2 * progress - 1)
    return 1 + (zoom - 1) * smoothstep(peak)


def render_clip(cover: Path, out: Path, width: int, duration: float, fps: int,
                zoom: float, kbps: int) -> int:
    """Write one clip and return its size in bytes."""
    source = Image.open(cover).convert("RGB")
    # Canvas inherits the page's own aspect so the video fits the slot-0 image's box and the
    # poster-to-video swap cannot shift the layout.
    height = max(2, round(width * source.height / source.width) // 2 * 2)

    work = source.resize((width * OVERSAMPLE, height * OVERSAMPLE),
                         Image.LANCZOS, reducing_gap=3)
    sw, sh = work.size
    frames = max(2, round(duration * fps))

    out.parent.mkdir(parents=True, exist_ok=True)
    encoder = subprocess.Popen(
        [
            "ffmpeg", "-y", "-v", "error",
            "-f", "rawvideo", "-pix_fmt", "rgb24",
            "-s", f"{width}x{height}", "-r", str(fps), "-i", "-",
            "-c:v", "libx264", "-preset", "slow",
            "-b:v", f"{kbps}k", "-maxrate", f"{round(kbps * 1.25)}k",
            "-bufsize", f"{kbps * 2}k",
            "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-an",
            str(out),
        ],
        stdin=subprocess.PIPE,
    )
    try:
        for index in range(frames):
            factor = zoom_at(index / (frames - 1), zoom)
            a = (sw / factor) / width
            e = (sh / factor) / height
            frame = work.transform(
                (width, height), Image.AFFINE,
                (a, 0, sw / 2 - a * width / 2, 0, e, sh / 2 - e * height / 2),
                resample=Image.BICUBIC,
            )
            encoder.stdin.write(frame.tobytes())
    finally:
        encoder.stdin.close()
        encoder.wait()

    if encoder.returncode != 0:
        raise RuntimeError(f"ffmpeg failed for {cover}")
    return out.stat().st_size


def blob_name_for(listing: str, prefix: str) -> str:
    """Published blob name for a listing's clip.

    The sync publishes PDFs as ``guides/<slug>.pdf`` with underscores turned into hyphens, so
    the clip is named the same way and lands beside them.
    """
    return f"{prefix}/{listing.replace('_', '-')}.mp4"


def upload_clip(path: Path, blob: str, container: str, auth: list[str]) -> bool:
    """Publish one clip to the public media container, returning success."""
    command = [
        "az", "storage", "blob", "upload",
        "--container-name", container,
        "--file", str(path),
        "--name", blob,
        "--content-type", "video/mp4",
        "--content-cache-control", CACHE_CONTROL,
        "--overwrite",
        "--no-progress",
        "--output", "none",
    ]
    command += auth
    try:
        return subprocess.call(command) == 0
    except OSError as error:
        print(f"  az is not available: {error}", file=sys.stderr)
        return False


def listings_with_covers(source: Path) -> list[tuple[str, Path]]:
    found = []
    for folder in sorted(p for p in source.iterdir() if p.is_dir()):
        cover = find_cover_image(str(folder / "images"))
        if cover:
            found.append((folder.name, Path(cover)))
    return found


def main() -> int:
    if not shutil.which("ffmpeg"):
        print("ffmpeg is not on PATH", file=sys.stderr)
        return 1

    parser = argparse.ArgumentParser(description="Render loopable gallery clips from page renders.")
    parser.add_argument("--source", default=DEFAULT_SOURCE, help="generator output folder")
    parser.add_argument("--only", action="append", default=[], help="listing id (repeatable)")
    parser.add_argument("--all", action="store_true", help="every listing with a cover image")
    parser.add_argument("--limit", type=int, help="stop after N listings")
    parser.add_argument("--width", type=int, default=DEFAULT_WIDTH, help="output width in px")
    parser.add_argument("--duration", type=float, default=DEFAULT_DURATION, help="seconds")
    parser.add_argument("--fps", type=int, default=DEFAULT_FPS, help="frames per second")
    parser.add_argument("--zoom", type=float, default=DEFAULT_ZOOM, help="peak zoom factor")
    parser.add_argument("--kbps", type=int, default=DEFAULT_KBPS, help="video bitrate cap")
    parser.add_argument("--force", action="store_true", help="rebuild clips that already exist")
    parser.add_argument("--apply", action="store_true", help="write clips (default: dry run)")
    parser.add_argument(
        "--upload",
        action="store_true",
        help="publish clips to the media container (implies --apply)",
    )
    parser.add_argument("--container", default=DEFAULT_CONTAINER, help="blob container for clips")
    parser.add_argument("--prefix", default=DEFAULT_PREFIX, help="blob prefix for clips")
    args = parser.parse_args()

    if args.upload:
        args.apply = True

    source = Path(args.source)
    if not source.is_dir():
        print(f"no such folder: {source}", file=sys.stderr)
        return 1

    available = dict(listings_with_covers(source))
    if args.only:
        targets = [(name, available[name]) for name in args.only if name in available]
        for missing in (name for name in args.only if name not in available):
            print(f"  skip  {missing} (no cover image)")
    elif args.all:
        targets = sorted(available.items())
    else:
        print("nothing to do: pass --only <listing> or --all")
        return 0

    if args.limit:
        targets = targets[: args.limit]

    auth: list[str] = []
    auth_label = ""
    if args.upload:
        auth, auth_label = storage_arguments()
        print(f"publishing to {args.container}/{args.prefix} using {auth_label or 'no credential'}")

    done = skipped = failed = 0
    for name, cover in targets:
        out = source / name / OUTPUT_DIR / f"{name}.mp4"
        blob = blob_name_for(name, args.prefix)
        current = out.exists() and not args.force and out.stat().st_mtime >= cover.stat().st_mtime

        if not args.apply:
            if current:
                skipped += 1
                print(f"  skip   {name}  {out.relative_to(source.parent)}")
            else:
                done += 1
                print(f"  would  {name}  {cover.name} -> {out.relative_to(source.parent)}")
            continue

        if current:
            skipped += 1
        else:
            size = render_clip(cover, out, args.width, args.duration, args.fps, args.zoom, args.kbps)
            print(f"  write  {name}  {size / 1024:.0f} KB  {out.relative_to(source.parent)}")
            done += 1

        if args.upload:
            if upload_clip(out, blob, args.container, auth):
                print(f"  upload {name}  {args.container}/{blob}")
            else:
                failed += 1
                print(f"  FAILED {name}: could not publish {args.container}/{blob}", file=sys.stderr)

    verb = "rendered" if args.apply else "would render"
    print(f"{verb} {done}, up to date {skipped}")
    if args.upload:
        print(f"published {done + skipped - failed} clip(s), {failed} failure(s)")
        if failed:
            print(
                "if the container is missing, create it once as anonymous read:\n"
                f"  az storage container create --name {args.container} "
                f"--public-access blob --account-name <account>",
                file=sys.stderr,
            )
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
