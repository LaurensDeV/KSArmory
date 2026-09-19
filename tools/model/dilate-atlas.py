#!/usr/bin/env python3
"""Fills the empty space around a baked atlas's islands from their nearest neighbour.

Why this exists, and why a bake margin alone is not enough.

Nothing samples outside a UV island at full resolution, so an unfilled background is invisible in
a texture viewer and invisible in a close-up. It is *mipmapping* that finds it: every lower mip
averages 2x2 blocks, so whatever sits beside an island bleeds further into its edge the further
away the part is drawn. Left black, that reads as dark speckle crawling over the paint.

Blender's own bake margin cannot be the whole answer, because it cuts both ways. Islands in a
packed atlas sit a few texels apart, so a margin wider than that gap writes one body's dilation
over another body's island *interior*. Measured on the HARM: a 48 px margin put the rail's dark
grey over 12.95% of the round's own island area -- grey blotches on a white missile -- where 8 px
puts it over 0.01%. So the bake keeps a small margin and everything beyond it is filled here,
each empty texel taking the colour of the nearest baked one. A mip then only ever averages an
island with more of itself, at any radius.

    ./tools/model/dilate-atlas.py --key 0,255,0 <keyed.png> [other.png ...]

**The first texture must be baked over a key colour** -- generate the bake target filled with
something that cannot occur in the bake (pure green on a grey-and-red missile) and bake with
use_clear off, so every texel the bake did not write still holds it. That key is the mask, and it
is exact.

A mask rasterised from the atlas's UVs instead is only an approximation, and the error is not
harmless: it over-covers slightly, which leaves unbaked texels *inside* the mask, and the fill
then propagates those outward as a spreading stain. That was a real failure here, caught only by
counting key-coloured texels afterwards -- which is why this script verifies its own result.

Later textures are filled from the same mask, downsampled. They must share the first one's UVs
and bake margin, which is true of a diffuse/normal/ORM set baked in one pass.
"""

import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage


def parse_key(text):
    parts = [int(v) for v in text.split(",")]
    if len(parts) != 3:
        raise SystemExit("error: --key wants three 0-255 components, e.g. --key 0,255,0")
    return np.array(parts, dtype=np.int16)


def fill(path, mask):
    """Replaces everything outside *mask* with its nearest texel inside it."""
    image = Image.open(path)
    mode = image.mode
    pixels = np.asarray(image.convert("RGB"))

    step = mask.shape[0] // pixels.shape[0]
    m = mask[::step, ::step] if step > 1 else mask
    if m.shape[0] != pixels.shape[0]:
        raise SystemExit(f"error: {Path(path).name} is {pixels.shape[0]}px, "
                         f"which the {mask.shape[0]}px mask does not divide")

    # One pass: the nearest in-mask texel for every texel. In-mask ones map to themselves, so
    # baked pixels are copied through untouched and this can never damage them.
    _, (yi, xi) = ndimage.distance_transform_edt(~m, return_distances=True, return_indices=True)
    Image.fromarray(pixels[yi, xi], "RGB").convert(mode).save(path)
    return int((~m).sum()), int(m.size)


def main(argv):
    if "--key" not in argv or len(argv) < 4:
        print(__doc__.strip(), file=sys.stderr)
        return 2

    at = argv.index("--key")
    key = parse_key(argv[at + 1])
    paths = [a for i, a in enumerate(argv[1:], 1) if i not in (at, at + 1)]

    first = np.asarray(Image.open(paths[0]).convert("RGB")).astype(np.int16)
    # Tolerant, because an 8-bit round trip moves a key by a unit or two.
    mask = np.abs(first - key).max(axis=2) > 8
    if not mask.any():
        raise SystemExit(f"error: {Path(paths[0]).name} is entirely the key colour -- "
                         "nothing was baked into it")
    print(f"  mask from key {tuple(int(v) for v in key)}: "
          f"{(~mask).mean() * 100:.1f}% of {Path(paths[0]).name} was never baked")

    for path in paths:
        filled, total = fill(path, mask)
        print(f"  {Path(path).name:34} {Image.open(path).size[0]:>5}px  "
              f"filled {filled} of {total} texels")

    # Verify rather than assume: any key colour left means the fill propagated the key instead of
    # replacing it, which is the failure this script exists to have caught once already.
    after = np.asarray(Image.open(paths[0]).convert("RGB")).astype(np.int16)
    left = int((np.abs(after - key).max(axis=2) <= 8).sum())
    if left:
        print(f"  FAILED: {left} key-coloured texels survive the fill", file=sys.stderr)
        return 1

    print("  clean: no key colour survives, so every texel now holds real paint")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
