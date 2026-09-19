#!/usr/bin/env python3
"""Build a save carrying N copies of one rocket, for pricing how the vehicle solver scales.

The engine fans per-bubble work across a worker pool, so N rockets far apart should be N bubbles
stepped concurrently -- until the solver's tick passes the governor's deadline and the whole world
starts running slow.  Nothing measures where that is.  See docs/METRE-LEVEL.md.

Copies are rotated about the body's spin axis (+Z in Cci) rather than displaced in a straight line:
that keeps every copy on the surface at its original latitude, and carries the surface velocity
round with it, so each one is standing on the pad exactly as the original was.
"""
import argparse, math, re, shutil, sys
from pathlib import Path

# Far enough apart that two of them are never a contact-imminence candidate, so they stay in their
# own bubbles and take the single-vehicle path.  The Mk 21's blast radius is 6 km and its lethal
# radius 2, so this is also clear of one salvo shooting another down.
DEFAULT_SPACING_M = 20_000.0


def rotate_z(x, y, theta):
    c, s = math.cos(theta), math.sin(theta)
    return x * c - y * s, x * s + y * c


def block(lines, vehicle_id):
    """The [start, end) line range of one <Vehicle> element."""
    start = next(i for i, l in enumerate(lines) if f'<Vehicle Id="{vehicle_id}"' in l)
    end = next(i for i in range(start, len(lines)) if "</Vehicle>" in lines[i]) + 1
    return start, end


def respace(text, theta, new_id, old_id):
    """One copy: renamed, and turned about the spin axis by theta."""
    text = text.replace(f'<Vehicle Id="{old_id}"', f'<Vehicle Id="{new_id}"', 1)

    def turn(m):
        tag, x, y = m.group(1), float(m.group(2)), float(m.group(3))
        nx, ny = rotate_z(x, y, theta)
        return f'<{tag} X="{nx:.4f}" Y="{ny:.4f}"'

    # Position and velocity only. Attitude and rates are left alone: the copies stand on their own
    # pads pointing the way the original did, which is up.
    return re.sub(r'<(PositionCci|VelocityCci) X="([-\d.]+)" Y="([-\d.]+)"', turn, text)


def main():
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--saves", required=True, help="the KSA saves directory")
    p.add_argument("--from", dest="source", default="ICBM E2E", help="save to copy")
    p.add_argument("--name", required=True, help="name of the save to write")
    p.add_argument("--rocket", default="GeoSat FAT", help="the vehicle to duplicate")
    p.add_argument("--count", type=int, required=True, help="how many rockets in total")
    p.add_argument("--drop", action="append", default=[],
                   help="a vehicle to leave out; repeatable")
    p.add_argument("--spacing", type=float, default=DEFAULT_SPACING_M,
                   help=f"metres between copies along the surface (default {DEFAULT_SPACING_M:.0f})")
    a = p.parse_args()

    saves = Path(a.saves)
    src, dst = saves / a.source, saves / a.name
    if not (src / "universe.xml").is_file():
        sys.exit(f"no universe.xml under {src}")
    if dst.exists():
        shutil.rmtree(dst)
    shutil.copytree(src, dst)

    lines = (dst / "universe.xml").read_text(encoding="utf-8").splitlines(keepends=True)

    for name in a.drop:
        s, e = block(lines, name)
        del lines[s:e]
        print(f"  dropped {name} ({e - s} lines)")

    s, e = block(lines, a.rocket)
    original = "".join(lines[s:e])

    # The radius of the circle of latitude the pad sits on -- what an arc length has to be divided
    # by to become an angle. Not the body's radius, which would space polar pads far too widely.
    m = re.search(r'<PositionCci X="([-\d.]+)" Y="([-\d.]+)"', original)
    if not m:
        sys.exit(f"{a.rocket} carries no PositionCci")
    r = math.hypot(float(m.group(1)), float(m.group(2)))

    copies = []
    for i in range(1, a.count):
        theta = (a.spacing * i) / r
        copies.append(respace(original, theta, f"{a.rocket} {i + 1}", a.rocket))
        print(f"  + {a.rocket} {i + 1}: {a.spacing * i / 1000:.0f} km along, {math.degrees(theta):.4f} deg")

    lines[e:e] = ["".join(copies)]
    (dst / "universe.xml").write_text("".join(lines), encoding="utf-8")

    meta = dst / "meta.toml"
    if meta.is_file():
        meta.write_text(re.sub(r'^name = ".*"', f'name = "{a.name}"',
                               meta.read_text(encoding="utf-8"), count=1, flags=re.M),
                        encoding="utf-8")

    total = (dst / "universe.xml").read_text(encoding="utf-8").count("<Vehicle ")
    print(f"wrote '{a.name}': {total} vehicle(s), {a.count} rocket(s)")


if __name__ == "__main__":
    main()
