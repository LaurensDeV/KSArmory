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

# "screen: tube 3 flies 1 ms sub-step" -- the arm says what it flew, so nothing here guesses the
# tube numbering or the assignment.
SCREEN = re.compile(r"screen: tube\s+(\d+)\s+flies\s+(.+?)\s*$")


# The fallback for an arm that predates the screen line: odd tubes under test, even shipped.
def under_test(tube: int) -> bool:
    return tube % 2 == 1


def read(path: pathlib.Path):
    impacts, walks, flew = {}, {}, {}

    for line in path.read_text(errors="replace").splitlines():
        if (m := IMPACT.search(line)):
            impacts[int(m.group(1))] = float(m.group(2))
        if (m := WALK.search(line)):
            walks[int(m.group(1))] = (float(m.group(2)), float(m.group(3)))
        if (m := SCREEN.search(line)):
            flew[int(m.group(1))] = m.group(2).strip()

    return impacts, walks, flew


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("log", type=pathlib.Path)
    args = ap.parse_args()

    if not args.log.is_file():
        print(f"error: no such log: {args.log}", file=sys.stderr)
        return 2

    impacts, walks, flew = read(args.log)

    if not impacts:
        print("no warhead impacts in that log -- did the salvo arrive?", file=sys.stderr)
        return 1

    # The arm's own account of what each tube flew, when it gives one. A round reports its tube
    # one-based at impact and the assignment is logged as the index, so they are lined up here
    # rather than assumed to agree.
    if flew:
        offset = min(impacts) - min(flew)
        label = {t: flew.get(t - offset, "?") for t in impacts}
    else:
        label = {t: ("under test" if under_test(t) else "shipped") for t in impacts}

    groups: dict[str, list[float]] = {}
    for t, m in impacts.items():
        groups.setdefault(label[t], []).append(m)

    print(f"== {args.log.name}: {len(impacts)} warheads, {len(groups)} condition(s)")
    print("\n   tube  flew                    miss")

    for t in sorted(impacts):
        walk = f"   walk {walks[t][0]:+.0f} down" if t in walks else ""
        print(f"   {t:>4}  {label[t]:<20}{impacts[t]:>7.0f} m{walk}")

    if len(groups) < 2:
        print("\nonly one condition flew -- not a screening arm's log", file=sys.stderr)
        return 1

    control = "shipped" if "shipped" in groups else sorted(groups)[0]
    base = statistics.mean(groups[control])

    # The scale to judge against: how much warheads on the SAME side disagree, which is everything
    # the split holds constant.
    within = max((statistics.pstdev(v) for v in groups.values() if len(v) > 1), default=0.0)

    print(f"\n   condition                mean       vs {control}")

    for name in sorted(groups, key=lambda k: (k != control, k)):
        mean = statistics.mean(groups[name])
        delta = "" if name == control else f"{mean - base:>+10.0f} m"
        sigma = "" if name == control or within <= 0.0 \
            else f"   {abs(mean - base) / within:>4.0f} sigma"
        print(f"   {name:<20}{mean:>9.0f} m{delta}{sigma}")

    if within > 0.0:
        print(f"\n   within-condition scatter {within:.1f} m, from one flight -- the cutoff, the"
              "\n   trim and the frame pacing are held identical by construction.")

    print("\n   a screen, not a verdict: what ships is decided on the group miss over an"
          "\n   interleaved batch. This says whether a change is worth one.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
