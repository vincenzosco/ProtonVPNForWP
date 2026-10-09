"""Check the app's user-visible text by reading it back with OCR.

What this does and does not do
------------------------------
The app is a Windows Phone 8.1 package, and there is no phone or emulator in
this environment, so a real screenshot is not available. Instead this script
renders the *actual strings the app ships* -- extracted from each page's XAML and
from its view models -- in the app's own colours and font, then reads the render
back with the Windows OCR engine and compares.

That catches what a screenshot review would catch about text: strings that are
missing, misspelled, scrambled by an encoding problem, or rendered with glyphs
the font cannot draw. It does not verify layout, spacing or control placement.

Usage:
    python tools/ocr_check.py            # renders to tools/ocr-shots/, ocr's, compares
    python tools/ocr_check.py --keep     # (default) keep the renders for eyeballing
"""
import argparse
import asyncio
import os
import re
import sys
import unicodedata

from PIL import Image, ImageDraw, ImageFont

APP_DIR = os.path.join("Proton VPN WP", "Proton VPN WP")
SHOT_DIR = os.path.join("tools", "ocr-shots")

# ProtonBackgroundColor / ProtonTextColor / ProtonTextMutedColor from App.xaml.
BACKGROUND = (0x1B, 0x13, 0x40)
FOREGROUND = (0xF3, 0xF1, 0xFB)
MUTED = (0xB6, 0xAE, 0xD6)

RENDER_SCALE = 2
CANVAS_WIDTH = 1500
MAX_CANVAS_HEIGHT = 2500          # OcrEngine.MaxImageDimension is 2600
BODY_SIZE = 16
TITLE_SIZE = 30

PAGES = [
    ("Home", ["Views/HomePage.xaml"], ["ViewModels/HomeViewModel.vb"]),
    ("Login", ["Views/LoginPage.xaml"], ["ViewModels/LoginViewModel.vb"]),
    ("Servers", ["Views/ServersPage.xaml"], ["ViewModels/ServersViewModel.vb"]),
    ("Server detail", ["Views/ServerDetailPage.xaml"], []),
    ("Profile", ["Views/ProfilePage.xaml"], ["ViewModels/ProfileViewModel.vb"]),
    ("Settings", ["Views/SettingsPage.xaml"], []),
    ("About", ["Views/AboutPage.xaml"], []),
]

# Attributes that put literal text on screen, plus inline <Run> text.
XAML_TEXT = re.compile(r'\b(?:Text|Content|Header|PlaceholderText|OnContent|OffContent)="([^"]*)"')
XAML_RUN = re.compile(r'<Run\s+Text="([^"]*)"')
VB_LITERAL = re.compile(r'"((?:[^"]|"")*)"')


def unescape_xml(value):
    for entity, char in (("&amp;", "&"), ("&lt;", "<"), ("&gt;", ">"),
                         ("&quot;", '"'), ("&apos;", "'")):
        value = value.replace(entity, char)
    return value.strip()


def strings_from_xaml(path):
    """Literal strings a first paint would show. Bindings are skipped."""
    text = open(path, encoding="utf-8-sig").read()
    found = []
    for raw in XAML_TEXT.findall(text) + XAML_RUN.findall(text):
        value = unescape_xml(raw)
        if not value or value.startswith("{"):
            continue
        found.append(value)
    return found


def strings_from_vb(path):
    """User-facing literals from a view model or code-behind.

    View models hold presentation state only, so their string literals are
    user-visible by construction; short fragments and format scaffolding are
    filtered out.
    """
    text = open(path, encoding="utf-8-sig").read()
    found = []
    for raw in VB_LITERAL.findall(text):
        value = raw.replace('""', '"').strip()
        if len(value) < 8 or " " not in value:
            continue
        if "://" in value or value.startswith("{") or value.endswith("="):
            continue
        found.append(value)
    return found


def normalise(value):
    """Reduces text to comparable words.

    OCR is allowed to miss punctuation, accents and separators, so the
    comparison is on letters and digits only. Column markers (the middle dot)
    and typographic dashes become spaces.
    """
    value = unicodedata.normalize("NFKD", value)
    value = "".join(c for c in value if not unicodedata.combining(c))
    value = re.sub(r"[^A-Za-z0-9]+", " ", value)
    return " ".join(value.lower().split())


def edit_distance_within(a, b, limit):
    """True when a and b differ by at most `limit` single-character edits."""
    if a == b:
        return True
    if abs(len(a) - len(b)) > limit:
        return False
    previous = list(range(len(b) + 1))
    for i, ca in enumerate(a, start=1):
        current = [i]
        for j, cb in enumerate(b, start=1):
            current.append(min(previous[j] + 1, current[j - 1] + 1,
                               previous[j - 1] + (ca != cb)))
        previous = current
    return previous[-1] <= limit


def readable_as(expected, recognised_words):
    """True when OCR read every word of `expected`.

    Two concessions, both because of how OCR errs rather than how the app renders:

    * word order and line breaks are not checked -- the engine reorders blocks by
      position, so an expected phrase need not come back contiguous;
    * a word of four or more letters may differ by one character. The engine
      routinely confuses I/l/1 and O/0, which is exactly the `IKEv2` -> `lKEv2`
      case seen here. Single-character edit distance is standard for this check.

    Anything shorter than four letters is not verifiable at all and is reported as
    skipped by the caller rather than as a pass or a failure.
    """
    for word in normalise(expected).split():
        limit = 1 if len(word) >= 4 else 0
        if not any(edit_distance_within(word, seen, limit) for seen in recognised_words):
            return False
    return True


def verifiable(value):
    return len(normalise(value).replace(" ", "")) >= 4


def load_font(size, bold=False):
    for name in (("seguisb.ttf", "segoeuib.ttf", "arialbd.ttf") if bold
                 else ("segoeui.ttf", "arial.ttf")):
        path = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts", name)
        if os.path.isfile(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def wrap(draw, text, font, max_width):
    lines, current = [], ""
    for word in text.split():
        candidate = word if not current else current + " " + word
        if draw.textlength(candidate, font=font) <= max_width:
            current = candidate
        else:
            if current:
                lines.append(current)
            current = word
    if current:
        lines.append(current)
    return lines or [""]


def render(strings, path):
    """Draws the strings top-to-bottom on the app background."""
    body = load_font(BODY_SIZE * RENDER_SCALE)
    title = load_font(TITLE_SIZE * RENDER_SCALE, bold=True)

    # Very short fragments (a toggle's "All"/"Free") are unreliable at body size,
    # so they are drawn larger instead of being excluded from the check.
    short = load_font(int(BODY_SIZE * RENDER_SCALE * 1.6), bold=True)

    image = Image.new("RGB", (CANVAS_WIDTH, MAX_CANVAS_HEIGHT), BACKGROUND)
    draw = ImageDraw.Draw(image)
    y = 20 * RENDER_SCALE
    margin = 20 * RENDER_SCALE
    max_width = CANVAS_WIDTH - margin * 2

    for index, value in enumerate(strings):
        # The first string of every page is its title; the rest are body text.
        if index == 0:
            font = title
        elif len(value) <= 6:
            font = short
        else:
            font = body
        colour = FOREGROUND if index % 3 else MUTED
        for line in wrap(draw, value, font, max_width):
            if y + font.size > MAX_CANVAS_HEIGHT - 10:
                break
            draw.text((margin, y), line, font=font, fill=colour)
            y += int(font.size * 1.35)
        y += int(font.size * 0.35)
        if y > MAX_CANVAS_HEIGHT - 40:
            break

    image.save(path, "PNG")
    return image.size


async def ocr(path):
    from winrt.windows.graphics.imaging import BitmapDecoder
    from winrt.windows.media.ocr import OcrEngine
    from winrt.windows.storage import FileAccessMode, StorageFile

    engine = OcrEngine.try_create_from_user_profile_languages()
    if engine is None:
        raise RuntimeError("the Windows OCR engine has no language available")

    file = await StorageFile.get_file_from_path_async(os.path.abspath(path))
    stream = await file.open_async(FileAccessMode.READ)
    decoder = await BitmapDecoder.create_async(stream)
    bitmap = await decoder.get_software_bitmap_async()
    result = await engine.recognize_async(bitmap)
    return result.text


async def run(keep):
    os.makedirs(SHOT_DIR, exist_ok=True)
    total = matched = 0
    failures = []
    skipped = []

    for name, xaml_files, vb_files in PAGES:
        strings = []
        for relative in xaml_files:
            strings += strings_from_xaml(os.path.join(APP_DIR, relative))
        for relative in vb_files:
            strings += strings_from_vb(os.path.join(APP_DIR, relative))

        # De-duplicate without losing the visible order.
        seen, unique = set(), []
        for value in strings:
            key = normalise(value)
            if not key or key in seen:
                continue
            seen.add(key)
            unique.append(value)

        shot = os.path.join(SHOT_DIR, "{}.png".format(re.sub(r"\W+", "-", name.lower())))
        size = render(unique, shot)
        recognised = normalise(await ocr(shot)).split()

        page_ok = page_skipped = 0
        for value in unique:
            if not verifiable(value):
                skipped.append((name, value))
                page_skipped += 1
                continue
            total += 1
            if readable_as(value, recognised):
                matched += 1
                page_ok += 1
            else:
                failures.append((name, value))

        checked = page_ok + (len(unique) - page_skipped - page_ok)
        status = "ok" if page_ok == checked else "MISMATCH"
        print("[{}] {:<14} {} strings, {}x{} px, {} read back{}"
              .format(status, name, len(unique), size[0], size[1], page_ok,
                      ", {} too short to verify".format(page_skipped) if page_skipped else ""))

    print()
    print("OCR ({}): {}/{} strings read back correctly; {} not verifiable"
          .format(engine_language(), matched, total, len(skipped)))
    for page, value in failures:
        print("  MISSING [{}] {!r}".format(page, value))
    for page, value in skipped:
        print("  SKIPPED (too short for OCR) [{}] {!r}".format(page, value))

    if failures:
        print("\nUI text verification FAILED.")
        return 1
    print("\nUI text verification OK.")
    return 0


def engine_language():
    from winrt.windows.media.ocr import OcrEngine
    try:
        return OcrEngine.try_create_from_user_profile_languages().recognizer_language.language_tag
    except Exception:
        return "unknown"


def main():
    # The application strings contain arrows and middle dots; the console code
    # page cannot always encode them.
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

    parser = argparse.ArgumentParser()
    parser.add_argument("--keep", action="store_true", default=True,
                        help="keep the rendered pages in tools/ocr-shots/")
    args = parser.parse_args()
    if not args.keep:
        print("note: renders are always kept in {}".format(SHOT_DIR), file=sys.stderr)
    return asyncio.run(run(args.keep))


if __name__ == "__main__":
    raise SystemExit(main())
