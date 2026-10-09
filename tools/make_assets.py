#!/usr/bin/env python3
"""Generate all Windows Phone 8.1 tile/splash/store assets from the Proton logo.

Source logo: the transparent-background Proton icon shipped by the user
(``~/Downloads/proton.png.png``).  The icon is blue/cyan on transparency, so the
large tiles get the Proton dark background baked in (``#1B1340``) and the small
app-list logo stays transparent.

Usage:
    python tools/make_assets.py [--source PATH] [--assets DIR]

Re-runnable: it overwrites the generated PNGs in place.
"""
from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

from PIL import Image

# Proton dark brand background.  Matches the theme background used in the app.
PROTON_BG = (27, 19, 64, 255)  # #1B1340
ACCENT = (109, 74, 255, 255)  # #6D4AFF

# Exact names and pixel sizes already referenced by Package.appxmanifest.
# Sizes match the scale-240 variants the VS2013 template produced.
TARGETS = [
    # (filename, width, height, bake_background, logo_scale_of_shorter_side)
    ("Logo.scale-240.png", 360, 360, True, 0.62),
    ("Square71x71Logo.scale-240.png", 170, 170, True, 0.62),
    ("WideLogo.scale-240.png", 744, 360, True, 0.42),
    ("StoreLogo.scale-240.png", 120, 120, True, 0.66),
    ("SmallLogo.scale-240.png", 106, 106, False, 0.92),
    ("SplashScreen.scale-240.png", 1152, 1920, True, 0.34),
    # In-app logo (transparent) used on the login/about pages.
    ("ProtonLogo.png", 256, 256, False, 0.94),
]


def fit(logo: Image.Image, box_w: int, box_h: int) -> Image.Image:
    """Scale the logo to fit inside box_w x box_h preserving aspect ratio."""
    ratio = min(box_w / logo.width, box_h / logo.height)
    new = (max(1, int(logo.width * ratio)), max(1, int(logo.height * ratio)))
    return logo.resize(new, Image.LANCZOS)


def build(src: Image.Image, name: str, w: int, h: int, bake_bg: bool, scale: float) -> Image.Image:
    canvas = Image.new("RGBA", (w, h), PROTON_BG if bake_bg else (0, 0, 0, 0))
    shorter = min(w, h)
    target = int(shorter * scale)
    icon = fit(src, target, target)
    canvas.alpha_composite(icon, ((w - icon.width) // 2, (h - icon.height) // 2))
    return canvas


def main() -> int:
    here = Path(__file__).resolve().parent
    default_source = Path.home() / "Downloads" / "proton.png.png"
    default_assets = here.parent / "Proton VPN WP" / "Proton VPN WP" / "Assets"

    ap = argparse.ArgumentParser()
    ap.add_argument("--source", default=str(default_source))
    ap.add_argument("--assets", default=str(default_assets))
    args = ap.parse_args()

    if not os.path.exists(args.source):
        print(f"ERROR: source logo not found: {args.source}", file=sys.stderr)
        return 2

    src = Image.open(args.source).convert("RGBA")
    print(f"source: {args.source} ({src.width}x{src.height})")
    os.makedirs(args.assets, exist_ok=True)

    for name, w, h, bake, scale in TARGETS:
        out = build(src, name, w, h, bake, scale)
        path = os.path.join(args.assets, name)
        out.save(path, "PNG", optimize=True)
        print(f"  wrote {name:36s} {w}x{h}{'  [dark bg]' if bake else '  [transparent]'}")

    print("done.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
