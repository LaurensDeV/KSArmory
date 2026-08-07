#!/usr/bin/env python3
"""
Generates the KSArmory / Kessler Systems wordmark.

Scripted rather than drawn, for the same reason the vehicle is: a logo that only exists as a PNG
cannot be re-cut at a different size, colour or lockup without starting again. Run it, look at the
output, change a number.

    ./tools/logo.py                     # what ships, into branding/
    ./tools/logo.py --all               # ...plus the faces that were not chosen
    ./tools/logo.py --out /tmp/logo     # somewhere else

Output goes to branding/ rather than dist/, which is gitignored release scratch: the wordmark is
a source asset that the README points at, not a build artefact.

## The mark

A radar scope with an interception track through it: a ring, a graticule, and a chord rising
across it to a point. That is what the mod does in one shape, and it survives being 32 px wide,
which a missile silhouette does not.

## Fonts

Bahnschrift is Microsoft's DIN, the face on German engineering drawings and road signs; Agency FB
is the condensed technical style used across military and aerospace interfaces. Both ship with
Windows. Nothing here embeds them - the render is rasterised, so the output carries no font.
"""

import argparse
import math
import os
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

FONTS = Path("/mnt/c/Windows/Fonts")

# Amber is the designation ring's own colour, so the brand and the thing it labels agree.
AMBER = (255, 217, 64)
PALE = (232, 236, 240)
STEEL = (138, 148, 158)
INK = (14, 16, 19)


def font(name, size):
    path = FONTS / name
    if not path.is_file():
        raise SystemExit(f"font not found: {path}")
    return ImageFont.truetype(str(path), size)


def variable(name, size, style):
    """Bahnschrift is a variable font; PIL can only reach its weights by name."""
    f = font(name, size)
    try:
        f.set_variation_by_name(style)
    except (OSError, AttributeError):
        pass
    return f


def scope(draw, cx, cy, r, ring, accent):
    """Radar scope with an intercept track: ring, graticule, and a chord rising to a point."""
    w = max(2, round(r * 0.09))
    draw.ellipse((cx - r, cy - r, cx + r, cy + r), outline=ring, width=w)

    # Graticule, stopping short of the ring so the ring stays the strongest line.
    inner = r * 0.62
    for angle in (0, 90):
        a = math.radians(angle)
        dx, dy = math.cos(a) * inner, math.sin(a) * inner
        draw.line((cx - dx, cy - dy, cx + dx, cy + dy), fill=ring, width=max(1, w // 2))

    # The track: in low from the left, out high to the right, tipped with the round itself.
    # An arrowhead rather than a dot -- a dot on a stick reads as a map pin, which is the one
    # thing this must not look like.
    x0, y0 = cx - r * 0.86, cy + r * 0.52
    x1, y1 = cx + r * 0.86, cy - r * 0.78

    dx, dy = x1 - x0, y1 - y0
    length = math.hypot(dx, dy)
    ux, uy = dx / length, dy / length
    px, py = -uy, ux

    head = r * 0.34
    draw.line((x0, y0, x1 - ux * head * 0.8, y1 - uy * head * 0.8), fill=accent, width=w)
    draw.polygon([(x1, y1),
                  (x1 - ux * head + px * head * 0.42, y1 - uy * head + py * head * 0.42),
                  (x1 - ux * head - px * head * 0.42, y1 - uy * head - py * head * 0.42)],
                 fill=accent)


def wordmark(out_dir, name, face, style, dark=True):
    """One lockup: mark, KSARMORY, and the company rule beneath."""
    title = variable(face, 190, style) if style else font(face, 190)
    sub = variable(face, 54, "Light") if style else font(face, 54)

    mark_r = 132
    margin = 56
    height = 480

    # Sized to its contents. A fixed canvas leaves a different amount of dead air behind every
    # face, which is what makes a set of variants impossible to compare.
    probe = ImageDraw.Draw(Image.new("RGBA", (1, 1)))
    text_w = probe.textlength("KSARMORY", font=title)

    mark_cx, mark_cy = margin + mark_r, height // 2
    width = round(mark_cx + mark_r + 74 + text_w + margin)

    image = Image.new("RGBA", (width, height), INK if dark else (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    scope(draw, mark_cx, mark_cy, mark_r, PALE if dark else STEEL, AMBER)

    text_x = mark_cx + mark_r + 74

    # Anchored by the cap line rather than centred: the descender-free caps and the small print
    # below have to sit on a shared optical baseline, which centring does not give.
    # KS in the company's colour: Kessler Systems reading out of the product name is the whole
    # point of choosing initials that match.
    ks_w = draw.textlength("KS", font=title)
    draw.text((text_x, mark_cy - 96), "KS", font=title, fill=AMBER, anchor="lt")
    draw.text((text_x + ks_w, mark_cy - 96), "ARMORY", font=title,
              fill=PALE if dark else INK, anchor="lt")

    rule_y = mark_cy + 74
    draw.line((text_x, rule_y, text_x + text_w, rule_y), fill=AMBER, width=5)

    draw.text((text_x, rule_y + 20), "KESSLER SYSTEMS", font=sub,
              fill=STEEL, anchor="lt")

    path = Path(out_dir) / name
    image.save(path)
    print(f"  {path}  {width}x{height}")


def icon(out_dir, name, dark=True):
    """The mark alone, square, for an avatar or a favicon."""
    size = 512
    image = Image.new("RGBA", (size, size), INK if dark else (0, 0, 0, 0))
    scope(ImageDraw.Draw(image), size // 2, size // 2, size * 0.36,
          PALE if dark else STEEL, AMBER)

    path = Path(out_dir) / name
    image.save(path)
    print(f"  {path}  {size}x{size}")


def main():
    ap = argparse.ArgumentParser(description="Generate the KSArmory wordmark.")
    ap.add_argument("--out", default="branding")
    ap.add_argument("--all", action="store_true",
                    help="also render the faces that were not chosen, for reconsidering")
    args = ap.parse_args()

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    # Bahnschrift is Microsoft's DIN: the face on engineering drawings, which is the register a
    # defence contractor's mark wants. Agency FB was the other candidate and is louder.
    print("wordmarks:")
    wordmark(out, "logo.png", "bahnschrift.ttf", "SemiBold")
    wordmark(out, "logo-light.png", "bahnschrift.ttf", "SemiBold", dark=False)

    print("icons:")
    icon(out, "icon.png")
    icon(out, "icon-transparent.png", dark=False)

    if args.all:
        print("alternates:")
        wordmark(out, "alt-bahnschrift-condensed.png", "bahnschrift.ttf", "SemiBold Condensed")
        wordmark(out, "alt-agency.png", "AGENCYB.TTF", None)


main()
