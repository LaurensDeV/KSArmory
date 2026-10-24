#!/usr/bin/env python3
"""Score an A/B split flown within one salvo.

    ./tools/ab-shot.py <log>

Each tube's condition is read off the arm's own "screen: tube N flies ..." lines; an arm that prints
none is read as odd tubes under test and even tubes shipped. Warheads from one bus share the cutoff,
the trim and the frame pacing, so one flight is a paired comparison.

Only a per-round term can be split this way: anything upstream of the release is shared by every
warhead and reads a dead heat however large it is. And it is a screen, not a verdict -- what ships is
decided on the group miss over an interleaved batch. docs/SHOT-PROTOCOL.md, section 0.
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

# "screen: tube 3 flies 1 ms sub-step"
SCREEN = re.compile(r"screen: tube\s+(\d+)\s+flies\s+(.+?)\s*$")


# For an arm that prints no screen line.
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

    # Keyed on the tube, never on an offset between the two lists: a warhead that fails to arrive
    # shifts one list and not the other, and an offset then mislabels every round silently.
    if flew:
        label = {t: flew.get(t, "?") for t in impacts}

        missing = sorted(set(flew) - set(impacts))
        if missing:
            print(f"warning: tube(s) {', '.join(map(str, missing))} flew but never arrived -- "
                  "the means below are over what landed, not over what was sent", file=sys.stderr)
    else:
        label = {t: ("under test" if under_test(t) else "shipped") for t in impacts}

    if any(v == "?" for v in label.values()):
        print("warning: a round impacted from a tube the arm never announced", file=sys.stderr)

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

    # The scale: how much warheads on the same side disagree, everything shared held constant.
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
