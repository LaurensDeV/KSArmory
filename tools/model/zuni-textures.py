#!/usr/bin/env python3
"""
Composes the Zuni's KSA maps from the ones Mallikas paints.

One unwrap carries the rocket and both pods, so one set of maps covers all three. He names them after
his file, 5InchRocketCamo_diffuse and, when they arrive, _normal, _ao, _rough and _metal. A map he has
not sent yet is filled with the neutral value, so the next version drops onto the same UVs with
nothing else to move.

    ./tools/model/zuni-textures.py /mnt/c/Users/<you>/Downloads
"""

import argparse
import pathlib
import sys

try:
    from PIL import Image
except ImportError:
    sys.exit("needs Pillow: pip install Pillow")

OUT = pathlib.Path(__file__).resolve().parents[2] / "src" / "KSArmory" / "Textures"
PREFIX = "5InchRocketCamo"


def grey(path, size):
    im = Image.open(path)
    if im.size != size:
        sys.exit(f"{path.name} is {im.size[0]}x{im.size[1]}, the diffuse map is {size[0]}x{size[1]}")
    return im.convert("RGB").split()[0]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("folder", type=pathlib.Path, help="the folder he sent")
    args = ap.parse_args()

    base_path = args.folder / f"{PREFIX}_diffuse.png"
    if not base_path.is_file():
        sys.exit(f"no {base_path.name} in {args.folder}")
    OUT.mkdir(parents=True, exist_ok=True)

    base = Image.open(base_path).convert("RGB")
    size = base.size
    base.save(OUT / "KSArmory_Zuni_Diffuse.png")

    nor = args.folder / f"{PREFIX}_normal.png"
    (Image.open(nor).convert("RGB") if nor.is_file() else Image.new("RGB", size, (128, 128, 255))) \
        .save(OUT / "KSArmory_Zuni_Normal.png")

    # ORM: R = occlusion, G = roughness, B = metalness, as Core's default_pbr.png (255, 180, 0) shows.
    def channel(suffix, fill):
        p = args.folder / f"{PREFIX}_{suffix}.png"
        return grey(p, size) if p.is_file() else Image.new("L", size, fill)

    Image.merge("RGB", [channel("ao", 255), channel("rough", 180), channel("metal", 0)]) \
        .save(OUT / "KSArmory_Zuni_PBR.png")

    print(f"3 maps at {size[0]}x{size[1]}")


if __name__ == "__main__":
    raise SystemExit(main())
