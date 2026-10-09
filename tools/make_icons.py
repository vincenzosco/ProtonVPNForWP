"""Regenerate the Windows Phone tile, store and splash assets from one logo.

Every image the app ships is derived from a single source PNG so the branding
can be refreshed by re-running this script instead of hand-editing binaries.

Usage:
    python tools/make_icons.py "C:/Users/Vincenzo/Downloads/proton.png.png"

The asset file names and pixel sizes are the scale-240 values Visual Studio 2013
expects for a Windows Phone 8.1 project; they must stay in step with the
Package.appxmanifest references.
"""
import argparse
import os
import sys

from PIL import Image

# #1B1340 -- ProtonBackgroundColor in App.xaml. Only the splash screen gets a
# painted background: the tiles stay transparent so Windows Phone can draw them
# on the user's accent colour.
BRAND_BACKGROUND = (27, 19, 64, 255)

ASSET_DIR = os.path.join("Proton VPN WP", "Proton VPN WP", "Assets")

# name -> (width, height, background, logo width as a fraction of the canvas)
TARGETS = {
    # Used by the sign-in page as an inline image, not by the tile system.
    "ProtonLogo.png": (256, 256, None, 0.92),
    "Logo.scale-240.png": (360, 360, None, 0.82),
    "SmallLogo.scale-240.png": (106, 106, None, 0.82),
    "Square71x71Logo.scale-240.png": (170, 170, None, 0.82),
    "StoreLogo.scale-240.png": (120, 120, None, 0.86),
    "WideLogo.scale-240.png": (744, 360, None, 0.72),
    "SplashScreen.scale-240.png": (1152, 1920, BRAND_BACKGROUND, 0.46),
}


def fit(logo, canvas_size, fraction):
    """Scales the logo to `fraction` of the canvas width, never enlarging past it."""
    width, height = canvas_size
    target_width = max(1, int(width * fraction))
    scale = target_width / float(logo.width)
    return logo.resize((target_width, max(1, int(round(logo.height * scale)))),
                       Image.LANCZOS)


def compose(logo, size, background, fraction):
    canvas = Image.new("RGBA", size, background if background else (0, 0, 0, 0))
    scaled = fit(logo, size, fraction)
    canvas.alpha_composite(scaled, ((size[0] - scaled.width) // 2,
                                    (size[1] - scaled.height) // 2))
    return canvas


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source", help="source logo PNG")
    parser.add_argument("--assets", default=ASSET_DIR)
    args = parser.parse_args()

    if not os.path.isfile(args.source):
        print("Source logo not found: {}".format(args.source), file=sys.stderr)
        return 2

    logo = Image.open(args.source).convert("RGBA")
    os.makedirs(args.assets, exist_ok=True)

    for name, (width, height, background, fraction) in sorted(TARGETS.items()):
        path = os.path.join(args.assets, name)
        compose(logo, (width, height), background, fraction).save(path, "PNG")
        written = Image.open(path)
        status = "ok" if written.size == (width, height) else "SIZE MISMATCH"
        print("{:<34} {:>4}x{:<4} {}".format(name, written.size[0], written.size[1], status))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
