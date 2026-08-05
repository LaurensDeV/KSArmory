#!/usr/bin/env python3
"""
Generates the launcher's texture atlas: a grid of flat swatches, one per surface type.

Why a palette rather than a painted texture: every face of the model is UV-mapped to the
*centre* of one swatch, so the whole vehicle is textured by choosing a swatch name per
primitive. No unwrapping, no seams, no baking, and the result is deterministic and diffable.
The cells are large (128 px in a 512 px atlas) so mipmapping never bleeds one swatch into
its neighbour at any distance the part is visible from.

Writes, next to the mod's other assets:
    Textures/KSArmory_Diffuse.png     base colour
    Textures/KSArmory_PBR.png         R = ambient occlusion, G = roughness, B = metalness
    Textures/KSArmory_Normal.png      flat normal

and tools/model/palette.json, which the Blender script reads so the UV coordinates and the
pixels come from one source.

**Channel order is not a guess.** KSA's own Content/Core/Textures/default_pbr.png is
(255, 180, 0) and EmptyAoRoughMetallic.png is (255, 255, 0) - unoccluded, rough, non-metal -
which matches the <AoRoughMetal> element name and the glTF ORM convention.

    ./tools/model/palette.py
"""

import json
import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    sys.exit("error: this needs Pillow (pip install pillow)")

REPO = Path(__file__).resolve().parent.parent.parent
TEXTURES = REPO / "src" / "KSArmory" / "Textures"
PALETTE_JSON = Path(__file__).resolve().parent / "palette.json"

GRID = 4          # cells per side
CELL = 128        # pixels per cell
SIZE = GRID * CELL

# name -> (column, row, diffuse RGB, roughness 0-1, metalness 0-1)
#
# Row 0 is the *bottom* row, matching UV space, so the JSON below can hand Blender a
# v coordinate with no flipping anywhere in the chain.
SWATCHES = [
    # name          col row  colour             rough metal
    ("hull",          0, 0, (78, 88, 62),        0.82, 0.0),   # Russian olive drab
    ("hull_dark",     1, 0, (52, 60, 42),        0.85, 0.0),   # shadowed panels, turret sides
    ("deck",          2, 0, (64, 72, 52),        0.88, 0.0),   # walkway plate
    ("rubber",        3, 0, (26, 26, 28),        0.95, 0.0),   # tyres

    ("tube",          0, 1, (96, 104, 80),       0.80, 0.0),   # missile containers
    ("tube_cap",      1, 1, (172, 170, 160),     0.70, 0.0),   # frangible muzzle covers
    ("metal",         2, 1, (150, 152, 155),     0.35, 1.0),   # bare steel, hubs, frames
    ("steel_dark",    3, 1, (80, 82, 86),        0.45, 1.0),   # gun barrels, trunnions

    ("radar",         0, 2, (196, 196, 190),     0.65, 0.0),   # array housings
    ("array",         1, 2, (118, 126, 132),     0.45, 0.15),  # the radiating faces
    ("glass",         2, 2, (30, 42, 52),        0.12, 0.0),   # cab glazing, EO window
    ("detail",        3, 2, (110, 116, 96),      0.80, 0.0),   # stowage, light housings

    ("warn",          0, 3, (176, 58, 42),       0.78, 0.0),   # hazard markings
    ("black",         1, 3, (12, 12, 14),        0.90, 0.0),   # grilles, recesses, gaps
    ("bronze",        2, 3, (148, 94, 44),       0.55, 0.7),   # missile booster casing
    ("missile",       3, 3, (190, 193, 196),     0.40, 0.2),   # sustainer body
]


def cell_box(col, row):
    """Pixel box for a cell, with row 0 at the bottom of the image."""
    x0 = col * CELL
    y0 = SIZE - (row + 1) * CELL
    return (x0, y0, x0 + CELL, y0 + CELL)


def uv_centre(col, row):
    """Blender/glTF UV of a cell centre. Origin bottom-left, which is Blender's convention;
    the glTF exporter flips V on the way out."""
    return ((col + 0.5) / GRID, (row + 0.5) / GRID)


def build():
    """Renders the three textures and the swatch table, without touching disk."""
    diffuse = Image.new("RGB", (SIZE, SIZE), (255, 0, 255))   # magenta = a UV that missed
    pbr = Image.new("RGB", (SIZE, SIZE), (255, 255, 0))
    normal = Image.new("RGB", (SIZE, SIZE), (128, 128, 255))

    table = {}
    for name, col, row, rgb, rough, metal in SWATCHES:
        box = cell_box(col, row)
        diffuse.paste(rgb, box)
        pbr.paste((255, round(rough * 255), round(metal * 255)), box)
        u, v = uv_centre(col, row)
        table[name] = {"uv": [u, v], "rgb": list(rgb), "roughness": rough, "metalness": metal}

    return {"KSArmory_Diffuse.png": diffuse,
            "KSArmory_PBR.png": pbr,
            "KSArmory_Normal.png": normal}, table


def check():
    """Verifies the committed textures still match what this script generates.

    Compares decoded pixels, not file bytes. PNG encoders differ between Pillow versions, so a
    byte comparison fails on a machine with a different Pillow even though the images are
    identical - which as a CI gate is pure noise.
    """
    images, _ = build()
    problems = 0

    for name, image in images.items():
        path = TEXTURES / name
        if not path.is_file():
            print(f"  MISSING {path}", file=sys.stderr)
            problems += 1
            continue
        with Image.open(path) as committed:
            if committed.convert("RGB").tobytes() != image.tobytes():
                print(f"  STALE {name}: does not match palette.py", file=sys.stderr)
                problems += 1
            else:
                print(f"  ok {name}")

    if problems:
        print("\nrerun ./tools/model/palette.py and commit the result", file=sys.stderr)
        return 1
    print("textures match the palette definition")
    return 0


def main():
    if "--check" in sys.argv:
        return check()

    TEXTURES.mkdir(parents=True, exist_ok=True)

    images, table = build()
    for name, image in images.items():
        image.save(TEXTURES / name)

    PALETTE_JSON.write_text(json.dumps({"grid": GRID, "swatches": table}, indent=2) + "\n")

    print(f"wrote {len(table)} swatches at {SIZE}x{SIZE} ({CELL} px cells)")
    for name in table:
        u, v = table[name]["uv"]
        print(f"  {name:<12} uv = ({u:.4f}, {v:.4f})  rgb = {tuple(table[name]['rgb'])}")
    print(f"\n  {TEXTURES.relative_to(REPO)}/KSArmory_{{Diffuse,PBR,Normal}}.png")
    print(f"  {PALETTE_JSON.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
