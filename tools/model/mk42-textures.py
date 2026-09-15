#!/usr/bin/env python3
"""
Composes the Mk 42's KSA maps from the ones Mallikas paints.

He ships the turret as a Blender-style set -- base, normal, occlusion, roughness and metalness, one
file each -- and the shell as a single painted image. KSA wants three maps per material in the glTF
ORM convention, so this turns whatever he sends into that without anybody hand-editing a channel.

Run it again on every version he sends. The unwrap is his, so a new map drops onto the same UVs and
neither the mesh, the XML nor the profile has to move.

    ./tools/model/mk42-textures.py /mnt/c/Users/<you>/Downloads/5InchGun
"""

import argparse
import pathlib
import sys

try:
    from PIL import Image
except ImportError:
    sys.exit("needs Pillow: pip install Pillow")

OUT = pathlib.Path(__file__).resolve().parents[2] / "src" / "KSArmory" / "Textures"

# The shell is painted and nothing else: no roughness or metalness map arrives with it, and inventing
# variation he did not paint would disagree with his next version.
SHELL_ROUGHNESS = 150  # 0.59 -- painted steel
SHELL_METALNESS = 25   # 0.10 -- it is the paint that is seen


def grey(path, size):
    im = Image.open(path)
    if im.size != size:
        sys.exit(f"{path.name} is {im.size[0]}x{im.size[1]}, the base map is {size[0]}x{size[1]}")
    return im.convert("RGB").split()[0]


def turret(folder):
    base_path = folder / "5InchGunTurret_base.png"
    if not base_path.is_file():
        sys.exit(f"no {base_path.name} in {folder}")
    base = Image.open(base_path).convert("RGB")
    size = base.size

    base.save(OUT / "KSArmory_Mk42_Diffuse.png")

    # Blender bakes OpenGL-convention normals, which is what every other authored atlas here ships.
    nor = folder / "5InchGunTurret_nor.png"
    (Image.open(nor).convert("RGB") if nor.is_file() else Image.new("RGB", size, (128, 128, 255))) \
        .save(OUT / "KSArmory_Mk42_Normal.png")

    # ORM: R = occlusion, G = roughness, B = metalness, as Core's default_pbr.png (255, 180, 0) shows.
    def channel(suffix, fill):
        p = folder / f"5InchGunTurret_{suffix}.png"
        return grey(p, size) if p.is_file() else Image.new("L", size, fill)

    Image.merge("RGB", [channel("occ", 255), channel("rough", 180), channel("metal", 0)]) \
        .save(OUT / "KSArmory_Mk42_PBR.png")
    return size


def shell(folder):
    p = folder / "5InchShellTexture.png"
    if not p.is_file():
        print(f"note: no {p.name}; shell maps left as they are")
        return None
    im = Image.open(p).convert("RGB")
    im.save(OUT / "KSArmory_Mk42Shell_Diffuse.png")
    Image.new("RGB", im.size, (128, 128, 255)).save(OUT / "KSArmory_Mk42Shell_Normal.png")
    Image.new("RGB", im.size, (255, SHELL_ROUGHNESS, SHELL_METALNESS)).save(OUT / "KSArmory_Mk42Shell_PBR.png")
    return im.size


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("folder", type=pathlib.Path, help="the folder he sent")
    args = ap.parse_args()
    if not args.folder.is_dir():
        sys.exit(f"not a folder: {args.folder}")
    OUT.mkdir(parents=True, exist_ok=True)

    t = turret(args.folder)
    s = shell(args.folder)
    print(f"turret: 3 maps at {t[0]}x{t[1]}")
    if s:
        print(f"shell:  3 maps at {s[0]}x{s[1]}")


if __name__ == "__main__":
    raise SystemExit(main())
