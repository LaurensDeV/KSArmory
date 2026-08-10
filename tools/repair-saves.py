#!/usr/bin/env python3
"""Realign saves whose KSArmory parts predate a subpart change.

KSA pairs a saved part with its current definition *positionally*, bounded by the
save's count and indexing the definition (`KSA.PartTree.Deserialize`):

    for (int i = 0; i < nextNode2.SubPartInstances?.Count; i++)
        Part part2 = nextNode.SubParts[i];

So a part that has **lost** a subpart throws `IndexOutOfRangeException` on every save
holding it, and takes the process with it -- the load runs inside `OnDrawUiFrame`, and
nothing between there and `Main` catches it. `InstanceOf` is written into the save and
never matched, so renaming a subpart is free and removing one is fatal.

This drops the surplus entries so the two lists line up again. It reads the current
definitions out of the mod's own asset XML, so it needs no table of what changed.

    ./tools/repair-saves.py             # report
    ./tools/repair-saves.py --fix       # rewrite, keeping a .bak beside each

Adding a subpart needs no repair: the loop stops at the save's shorter count, and the
new subpart simply starts unconfigured.
"""

from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
ASSETS = REPO / "src" / "KSArmory" / "KSArmory*.xml"

# Every way the same assembly has been spelled across the Id renames. Matching on this
# rather than on the literal name is what lets a save written before a rename still be
# understood -- and the renames are exactly why the count is the only thing to trust.
NOISE = re.compile(r"(?i)(^KSArmory_|_?Sub_?[Pp]art_?|^Prefab_|^Launcher_|\d+$|_$)")

# Both spellings: a craft built around the part writes RootPartRef, one carrying it
# bolted to something else writes PartRef. Matching only the root misses every save
# where the launcher is a passenger, which is most of them.
PART_OPEN = re.compile(r'<(Root)?PartRef\s+InstanceOf="(KSArmory_[^"]*)"')
SUBPART = re.compile(r'^(\s*)<SubPartRef\s+InstanceOf="([^"]*)"([^>]*?)(/?)>\s*$')

# Where a part's own subpart list stops. PartConnectorRef and SubPartRef both begin
# "<Part"-ish, so these are matched whole rather than by prefix.
def ends_block(stripped: str) -> bool:
    return any(stripped.startswith(tag)
               for tag in ("</PartRef", "</RootPartRef", "<PartRef", "<RootPartRef"))


def canonical(name: str) -> str:
    """A subpart name with the era-specific decoration taken off."""
    previous = None
    while previous != name:
        previous = name
        name = NOISE.sub("", name)
    return name.lower()


def declared_subparts() -> dict[str, list[str]]:
    """Each shipped part's subparts, in declaration order, as canonical names."""
    parts: dict[str, list[str]] = {}

    for path in sorted((REPO / "src" / "KSArmory").glob("KSArmory*.xml")):
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError as exc:
            sys.exit(f"{path}: {exc}")

        for element in root.iter():
            if not element.tag.endswith("Part"):
                continue
            part_id = element.get("Id")
            if not part_id or not part_id.startswith("KSArmory_"):
                continue
            subs = [s for s in element if s.tag.endswith("SubPart")]
            if subs:
                parts[part_id] = [canonical(s.get("InstanceOf") or s.get("Id") or "") for s in subs]

    return parts


def surplus_lines(saved: list[tuple[int, str]], declared: list[str]) -> list[int]:
    """Which saved entries have no counterpart, aligning the two lists in order.

    Greedy rather than a full edit-distance match: the two lists differ by whole
    assemblies being added or dropped, never by a reordering, so walking them together
    and skipping what does not line up finds the same answer for less.
    """
    surplus: list[int] = []
    want = 0

    for index, (line_no, name) in enumerate(saved):
        if want < len(declared) and name == declared[want]:
            want += 1
            continue
        # Nothing left to match against, or a name that is not the one expected here.
        surplus.append(line_no)

    return surplus


def inspect(save: Path, declared: dict[str, list[str]], fix: bool) -> tuple[int, int]:
    """Returns (parts needing repair, entries dropped)."""
    # newline="" throughout: the saves are CRLF, and letting Python translate them would
    # rewrite every line in a file this is meant to change by one.
    with open(save, encoding="utf-8", errors="surrogateescape", newline="") as handle:
        lines = handle.read().splitlines(keepends=True)

    needing = 0
    dropped: set[int] = set()

    index = 0
    while index < len(lines):
        opened = PART_OPEN.search(lines[index])
        if not opened:
            index += 1
            continue

        part_id = opened.group(2)
        indent = len(lines[index]) - len(lines[index].lstrip())

        # The part's own subparts are the SubPartRef lines indented inside it. A nested
        # part's would be indented further; the block ends at the closing tag.
        saved: list[tuple[int, str]] = []
        cursor = index + 1
        while cursor < len(lines):
            line = lines[cursor]
            stripped = line.strip()
            if ends_block(stripped):
                break
            matched = SUBPART.match(line)
            if matched and len(matched.group(1)) == indent + 2:
                if not matched.group(4):
                    # Carries module save data. Dropping it would discard state, and it
                    # is not the shape any KSArmory subpart has -- so refuse rather than guess.
                    print(f"    ! {part_id}: subpart with save data at line {cursor + 1}; left alone")
                    saved = []
                    break
                saved.append((cursor, canonical(matched.group(2))))
            cursor += 1

        want = declared.get(part_id)
        if saved and want is not None and len(saved) != len(want):
            extra = surplus_lines(saved, want)
            if len(saved) > len(want):
                needing += 1
                names = ", ".join(lines[n].strip()[:60] for n in extra[:3])
                print(f"    {part_id}: {len(saved)} saved vs {len(want)} declared "
                      f"-> drop {len(extra)} ({names})")
                dropped.update(extra)
            else:
                print(f"    {part_id}: {len(saved)} saved vs {len(want)} declared "
                      f"- fewer than now, which loads; nothing to do")

        index = cursor

    if dropped and fix:
        backup = save.with_suffix(save.suffix + ".bak")
        if not backup.exists():
            shutil.copy2(save, backup)
        kept = [line for n, line in enumerate(lines) if n not in dropped]
        with open(save, "w", encoding="utf-8", errors="surrogateescape", newline="") as handle:
            handle.write("".join(kept))
        print(f"    rewritten ({len(dropped)} dropped); original at {backup.name}")

    return needing, len(dropped)


def ksa_user_dir() -> Path:
    out = subprocess.run([str(REPO / "tools" / "ksa-user-dir.sh")],
                         capture_output=True, text=True)
    if out.returncode != 0 or not out.stdout.strip():
        sys.exit("could not locate the KSA user directory")
    return Path(out.stdout.strip())


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--fix", action="store_true", help="rewrite the saves, keeping a .bak")
    ap.add_argument("--saves", type=Path, help="a saves folder; defaults to the KSA user dir")
    args = ap.parse_args()

    declared = declared_subparts()
    if not declared:
        sys.exit("no shipped parts with subparts found; is the asset XML in place?")

    root = args.saves or (ksa_user_dir() / "saves")
    if not root.is_dir():
        sys.exit(f"no saves folder at {root}")

    print(f"declared: " + ", ".join(f"{k.replace('KSArmory_Prefab_', '')}={len(v)}"
                                    for k, v in sorted(declared.items())))
    print(f"saves:    {root}\n")

    total_parts = 0
    total_dropped = 0

    for save in sorted(root.iterdir()):
        universe = save / "universe.xml"
        if not universe.is_file():
            continue
        print(f"  {save.name}")
        needing, count = inspect(universe, declared, args.fix)
        total_parts += needing
        total_dropped += count

    print()
    if total_parts == 0:
        print("every save lines up with the parts as they are now")
        return 0

    if args.fix:
        print(f"repaired {total_parts} part(s), {total_dropped} entries dropped")
        return 0

    print(f"{total_parts} part(s) would crash the game on load; rerun with --fix")
    return 1


if __name__ == "__main__":
    sys.exit(main())
