#!/usr/bin/env python3
"""
Generates the soft particle sprite the smoke is drawn with.

Why this exists: KSA's Volumetric renderer makes convincing smoke out of overlapping spheres,
but it is the *screen-space* renderer and only draws when the player has Screen Space Particles
switched on, which is off by default. The Billboard renderer has no such gate - it is an
alpha-blended camera-facing quad sampling a material - so smoke that has to work on a default
install is a billboard with a soft sprite on it. Solid spheres are the thing that reads as a
heap of balls; a soft-edged sprite does not, because it has no edge.

The sprite is a radial falloff raised to a power, with a little value noise so a cloud of them
does not look like a cloud of identical discs. Alpha carries the shape; RGB is white so
ParticleColor alone decides the tint.

Writes, next to the mod's other textures:
    Textures/KSArmory_Smoke_Diffuse.png     white, alpha = the puff
    Textures/KSArmory_Smoke_Normal.png      flat
    Textures/KSArmory_Smoke_PBR.png         unoccluded, fully rough, non-metal

    ./tools/model/smokepuff.py            regenerate
    ./tools/model/smokepuff.py --check    fail if the committed files differ
"""

import math
import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    sys.exit("error: this needs Pillow (pip install pillow)")

REPO = Path(__file__).resolve().parent.parent.parent
TEXTURES = REPO / "src" / "KSArmory" / "Textures"

SIZE = 128

# How sharply the puff fades to nothing. Higher is wispier. Chosen so the sprite has no visible
# edge at any size the burst is seen at, which is the entire point of using one.
FALLOFF = 2.6

# Noise cell count and depth. Small and shallow: enough to break up the disc, not enough to read
# as a texture in its own right.
NOISE_CELLS = 8
NOISE_DEPTH = 0.22


def _noise_grid(seed: int) -> list[list[float]]:
    """A tiny value-noise lattice. Deterministic, so the PNG is reproducible and diffable."""
    grid = []
    state = seed
    for _ in range(NOISE_CELLS + 1):
        row = []
        for _ in range(NOISE_CELLS + 1):
            # A plain LCG rather than random.random(): the point is that this file regenerates
            # byte-identically on any machine and any Python build, which CI checks.
            state = (state * 1103515245 + 12345) & 0x7FFFFFFF
            row.append((state >> 8) / 0x7FFFFF)
        grid.append(row)
    return grid


def _sample(grid: list[list[float]], u: float, v: float) -> float:
    x = u * NOISE_CELLS
    y = v * NOISE_CELLS
    x0, y0 = int(x), int(y)
    fx, fy = x - x0, y - y0

    # Smoothstep, so the lattice does not show as a grid of diamonds.
    fx = fx * fx * (3.0 - 2.0 * fx)
    fy = fy * fy * (3.0 - 2.0 * fy)

    a = grid[y0][x0] * (1 - fx) + grid[y0][x0 + 1] * fx
    b = grid[y0 + 1][x0] * (1 - fx) + grid[y0 + 1][x0 + 1] * fx
    return a * (1 - fy) + b * fy


def build() -> dict[str, Image.Image]:
    diffuse = Image.new("RGBA", (SIZE, SIZE), (255, 255, 255, 0))
    pixels = diffuse.load()
    grid = _noise_grid(seed=20260805)

    centre = (SIZE - 1) / 2.0
    for y in range(SIZE):
        for x in range(SIZE):
            dx = (x - centre) / centre
            dy = (y - centre) / centre
            r = math.sqrt(dx * dx + dy * dy)

            if r >= 1.0:
                continue

            # Radial falloff, then broken up so overlapping puffs do not stack into a clean disc.
            a = (1.0 - r) ** FALLOFF
            a *= 1.0 - NOISE_DEPTH + NOISE_DEPTH * _sample(grid, x / SIZE, y / SIZE)
            pixels[x, y] = (255, 255, 255, max(0, min(255, int(a * 255.0 + 0.5))))

    return {
        "KSArmory_Smoke_Diffuse.png": diffuse,
        "KSArmory_Smoke_Normal.png": Image.new("RGB", (SIZE, SIZE), (128, 128, 255)),
        "KSArmory_Smoke_PBR.png": Image.new("RGB", (SIZE, SIZE), (255, 255, 0)),
    }


def main() -> int:
    images = build()

    if "--check" in sys.argv:
        for name, image in images.items():
            path = TEXTURES / name
            if not path.is_file():
                print(f"missing: {path}")
                return 1
            with Image.open(path) as committed:
                if committed.convert("RGBA").tobytes() != image.convert("RGBA").tobytes():
                    print(f"differs from the committed file: {path}")
                    return 1
        print(f"smoke sprite reproducible ({len(images)} file(s))")
        return 0

    TEXTURES.mkdir(parents=True, exist_ok=True)
    for name, image in images.items():
        image.save(TEXTURES / name)
        print(f"wrote {TEXTURES / name}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
