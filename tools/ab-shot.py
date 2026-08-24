#!/usr/bin/env python3
"""Read one shot's log and score an A/B split flown *within* a single salvo.

    ./tools/ab-shot.py <log>            # a shot flown by a split arm
    ./tools/ab-shot.py <log> --tubes    # ...and every warhead, not just the summary

WHY THIS EXISTS. A night of sixteen shots measured a 336 m term at 27 sigma, which is twenty-five
shots more than the question needed. The cost was in the wrong place: the group *miss* carries the
aim correction's shot-to-shot variance, 11%, which has nothing to do with a term inside the round.

Six warheads from one bus land within about 20 m of each other. So a split arm -- odd tubes flying
the shipped code and even tubes the change -- turns one flight into a paired comparison with the
cutoff, the trim, the frame pacing and the weather all held identical by construction. The same
336 m separates at seventeen sigma from a single shot.

WHAT IT CANNOT DO. Only a per-round term can be split this way. Anything upstream of the release --
guidance, the bus, the arrival, the release timing -- is shared by all six warheads, and a split arm
would read a dead heat however large the effect. If the change is not inside the round, fly a night.

AND IT IS A SCREEN, NOT A VERDICT. It scores the walk and the per-warhead miss on one flight; what
ships is decided on the group miss over a proper interleaved batch, because that is the number a
player gets. Use this to choose what is worth a night.
"""

import argparse
import pathlib
import re
import statistics
import sys

# "round 4 detonated on the ground, 824 m from the aim point (landed ... aimed ...)"
IMPACT = re.compile(r"round\s+(\d+)\s+detonated on the ground,\s+([\d.]+)\s*m from the aim point")

# "warhead trace: round 1 landed at ... | walk from the release probe 698 m (+698 down, +26 cross)"
WALK = re.compile(
    r"round\s+(\d+)\s+landed at\s+[-\d.,]+\s*\|.*?walk from the release probe\s+[-\d.]+\s*m\s*"
    r"\(([-+\d.]+)\s*down,\s*([-+\d.]+)\s*cross\)")

# Which side a tube is on. Must match UnderTest() in the split arm.
def under_test(tube: int) -> bool:
    return tube % 2 == 1


def read(path: pathlib.Path):
    impacts, walks = {}, {}

    for line in path.read_text(errors="replace").splitlines():
        if (m := IMPACT.search(line)):
            impacts[int(m.group(1))] = float(m.group(2))
        if (m := WALK.search(line)):
            walks[int(m.group(1))] = (float(m.group(2)), float(m.group(3)))

    return impacts, walks


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("log", type=pathlib.Path)
    ap.add_argument("--tubes", action="store_true", help="print every warhead")
    args = ap.parse_args()

    if not args.log.is_file():
        print(f"error: no such log: {args.log}", file=sys.stderr)
        return 2

    impacts, walks = read(args.log)

    if not impacts:
        print("no warhead impacts in that log -- did the salvo arrive?", file=sys.stderr)
        return 1

    test = {t: m for t, m in impacts.items() if under_test(t)}
    ship = {t: m for t, m in impacts.items() if not under_test(t)}

    print(f"== {args.log.name}: {len(impacts)} warheads, "
          f"{len(ship)} shipped against {len(test)} under test")

    if args.tubes or len(impacts) <= 8:
        print("\n   tube  side          miss")
        for t in sorted(impacts):
            side = "under test" if under_test(t) else "shipped"
            walk = f"   walk {walks[t][0]:+.0f} down" if t in walks else ""
            print(f"   {t:>4}  {side:<12}{impacts[t]:>7.0f} m{walk}")

    if not test or not ship:
        print("\nonly one side flew -- this log is not from a split arm", file=sys.stderr)
        return 1

    a, b = statistics.mean(ship.values()), statistics.mean(test.values())

    # The scale to judge the difference against: how much the warheads on one side disagree with
    # each other, which is everything the split holds constant.
    within = max(statistics.pstdev(list(ship.values())) if len(ship) > 1 else 0.0,
                 statistics.pstdev(list(test.values())) if len(test) > 1 else 0.0)

    print(f"\n   shipped     {a:>8.0f} m   (n={len(ship)})")
    print(f"   under test  {b:>8.0f} m   (n={len(test)})")
    print(f"   difference  {b - a:>+8.0f} m", end="")

    if within > 0.0:
        print(f"   against {within:.1f} m of within-side scatter"
              f"  ->  {abs(b - a) / within:.0f} sigma")
    else:
        print()

    print("\n   a screen, not a verdict: what ships is decided on the group miss over an"
          "\n   interleaved batch. This says whether a change is worth one.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
