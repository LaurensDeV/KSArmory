#!/usr/bin/env python3
"""Realign saves whose KSArmory parts predate a subpart change.

KSA pairs a saved part with its current definition *positionally*, bounded by the
save's count and indexing the definition (`KSA.PartTree.Deserialize`):

    for (int i = 0; i < nextNode2.SubPartInstances?.Count; i++)
        Part part2 = nextNode.SubParts[i];

So a part that has **lost** a subpart threw `IndexOutOfRangeException` on every save holding it
before 2026.11.10.5801, and since then shifts the saved state of every subpart after it.
`InstanceOf` is written into the save and never matched, so renaming a subpart is free.

This drops the surplus entries so the two lists line up again. It reads the current
definitions out of the mod's own asset XML, so it needs no table of what changed.

    ./tools/repair-saves.py             # report
    ./tools/repair-saves.py --fix       # rewrite, keeping a .bak beside each
    ./tools/repair-saves.py --fix --designs   # ...and the vehicle designs too

It also renames Core parts KSA has renamed (CORE_RENAMES), in what an earlier build wrote. A save names a part by Id and drops a
craft naming one that no longer exists, but holds its connectors by index and its pose as its own
transform, so a part swapped for one built from the same subparts loads as it was.

Adding a subpart needs no repair: the loop stops at the save's shorter count, and the
new subpart simply starts unconfigured.

It also re-dresses kittens wearing a KSArmory character the mod no longer declares. A save
records each roster kitten's character, and `KittenEva.CreateKittenFromSaveData` resolves
it through `ModLibrary.Get`, which throws on an Id nothing declares -- inside the same
uncaught load. Every character registered is drawn from when a roster is made, so a mod's
character ends up on kittens in nearly every save made while it was installed. Each is
given one of Core's own, chosen by the kitten's name so a rerun picks the same one.
"""

from __future__ import annotations

import argparse
import os
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
import zlib
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
ASSETS = REPO / "src" / "KSArmory" / "KSArmory*.xml"
KSA_DIR = Path(os.environ.get("KSA_DIR", "/mnt/c/Program Files/Kitten Space Agency"))

# A roster entry is a <Kitten Name=...>, and a kitten out on EVA a <Vehicle Id=...> carrying
# the same name: both record the character, and both are resolved on load.
KITTEN = re.compile(r'(<(?:Kitten|Vehicle)\b[^>]*?\bCharacter=")(KSArmory[^"]*)(")')
KITTEN_NAME = re.compile(r'<(?:Kitten\b[^>]*?\bName|Vehicle\b[^>]*?\bId)="([^"]*)"')

# KSA 2026.11.10.5801 renamed Core's kit, and gave two Ids to different parts. Old Id -> the part
# built from its subparts in the same order, and how its connectors were renumbered, found by
# mating every saved pair against the new definitions. Applied only to what an earlier build wrote.
CORE_BUILD = (2026, 11, 10, 5801)
CORE_RENAMES: dict[str, tuple[str, dict[int, int]]] = {
    "CoreCouplingA_Prefab_DockingPort1WB": ("CoreCouplingA_Prefab_DockingPort1WAndrogynousA", {}),
    "CoreElectricalA_Prefab_InlineBatteryBankA": ("CoreElectricalA_Prefab_InlineBattery1WBankV1A", {}),
    "CoreElectricalA_Prefab_InlineBatteryBankB": ("CoreElectricalA_Prefab_InlineBattery2WBankV1A", {}),
    "CoreElectricalA_Prefab_RadialBatteryB": ("CoreElectricalA_Prefab_RadialBatteryMediumV1A", {}),
    "CoreElectricalA_Prefab_SolarPanelA": ("CoreElectricalA_Prefab_SolarPanelFixedSmallV1A", {}),
    "CoreElectricalA_Prefab_SolarPanelB": ("CoreElectricalA_Prefab_SolarPanelTrackerMediumV1A", {}),
    "CoreFairingA_Prefab_AeroshroudCC": ("CoreFairingA_Prefab_AeroshroudLargeV3A", {}),
    "CoreFairingA_Prefab_InterstageBridge3W2WB": ("CoreFairingA_Prefab_InterstageBridge3W2WA", {}),
    "CoreFairingA_Prefab_NoseconeA": ("CoreFairingA_Prefab_Nosecone05WBluntA", {}),
    "CoreFairingA_Prefab_NoseconeC": ("CoreFairingA_Prefab_Nosecone2WBluntA", {}),
    "CoreFairingA_Prefab_NoseconeD": ("CoreFairingA_Prefab_Nosecone05WBallisticA", {}),
    "CorePropulsionB_Prefab_RCSALargeB": ("CorePropulsionB_Prefab_RCSLargeFiveHornV1A", {}),
    "CorePropulsionB_Prefab_RCSAMediumLowB": ("CorePropulsionB_Prefab_RCSMediumTwoHornV2A", {}),
    "CorePropulsionB_Prefab_RCSAMediumLowD": ("CorePropulsionB_Prefab_RCSMediumFourHornV2A", {}),
    "CorePropulsionB_Prefab_RCSASmallA": ("CorePropulsionB_Prefab_RCSSmallOneHornV1A", {}),
    "CorePropulsionB_Prefab_RCSASmallC": ("CorePropulsionB_Prefab_RCSSmallFourHornV2A", {}),
    "CorePropulsionB_Prefab_RCSASmallE": ("CorePropulsionB_Prefab_RCSSmallFiveHornV1A", {}),
    "CoreServiceModuleA_Prefab_SetAHeightB": ("CoreServiceModuleA_Prefab_SetA2DoubleA", {}),
    "CoreStructuralA_Prefab_EnginePlateLowProfile2WA": ("CoreStructuralA_Prefab_EnginePlate1WSingle2WA", {}),
    "CoreStructuralA_Prefab_EnginePlateLowProfile3WA": ("CoreStructuralA_Prefab_EnginePlate2WSingle3WA", {}),
    "CoreStructuralA_Prefab_EnginePlateLowProfile3WB": ("CoreStructuralA_Prefab_EnginePlate1WSingle3WA", {}),
    "CoreStructuralA_Prefab_EnginePlateLowProfile3WC": ("CoreStructuralA_Prefab_EnginePlate1WQuad3WA", {}),
    "CoreStructuralA_Prefab_EnginePlateLowProfile3WD": ("CoreStructuralA_Prefab_EnginePlate1WTriple3WA", {}),
    # Kept its Id and its subparts; its connectors were renumbered.
    "CoreFuelTankA_Prefab_LF3W6HC": ("CoreFuelTankA_Prefab_LF3W6HC", {3: 2}),
    # Its Id now names what was 3W2WB; it is the hollow bridge.
    "CoreFairingA_Prefab_InterstageBridge3W2WA": ("CoreFairingA_Prefab_InterstageBridgeHollow3W2WA", {}),
    # Not the same subparts, the nearest of the family: the docking ports and the 2-1 m bridge by
    # their shared subparts, the trusses by the strut they are built from.
    "CoreCouplingA_Prefab_DockingPort1WA": ("CoreCouplingA_Prefab_DockingPort1WAndrogynousA", {}),
    "CoreCouplingA_Prefab_DockingPort1WC": ("CoreCouplingA_Prefab_DockingPort1WAndrogynousA", {0: 1}),
    "CoreFairingA_Prefab_InterstageBridge2W1WB": ("CoreFairingA_Prefab_InterstageBridge2W1WA", {}),
    "CoreStructuralA_Prefab_StrutA": ("CoreStructuralA_Prefab_StrutCubeHalfWHalfHA", {}),
    "CoreStructuralA_Prefab_StrutB": ("CoreStructuralA_Prefab_StrutOctoHalfHA", {}),
    "CoreStructuralA_Prefab_StrutC": ("CoreStructuralA_Prefab_StrutOctoHalfHA", {}),
}
# Left in a file the renames have been applied to, so a second run leaves it alone.
CORE_MARK = "<!-- KSArmory: Core parts renamed for 2026.11.10.5801 -->"
CORE_PART = re.compile(r'(<(?:Root)?PartRef\s+InstanceOf=")(Core[^"]*)(")')
CONNECTOR_REF = re.compile(r'(<PartConnectorRef\s+Index=")(\d+)(")')

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

        for element in root.findall("Part"):
            part_id = element.get("Id")
            if element.get("Modify") or not part_id or not part_id.startswith("KSArmory_"):
                continue
            subs = element.findall("SubParts/SubPart")
            if subs:
                parts[part_id] = [canonical(s.get("Ref") or s.get("Key") or "") for s in subs]

    return parts


def declared_characters() -> set[str]:
    """Every character Id the mod's own asset XML still declares."""
    ids: set[str] = set()
    for path in sorted((REPO / "src" / "KSArmory").glob("KSArmory*.xml")):
        for element in ET.parse(path).getroot().iter("Character"):
            if element.get("Id"):
                ids.add(element.get("Id"))
    return ids


def stock_characters() -> list[str]:
    """Core's own characters, read from the install rather than typed here, or [] without one."""
    path = KSA_DIR / "Content" / "Core" / "CharacterAssets.xml"
    if not path.is_file():
        return []
    return sorted(e.get("Id") for e in ET.parse(path).getroot().iter("Character") if e.get("Id"))


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


def redress_kittens(lines: list[str], characters: set[str], stock: list[str]) -> int:
    """Puts every kitten wearing an undeclared KSArmory character into a stock one, in place."""
    changed = 0
    for n, line in enumerate(lines):
        worn = KITTEN.search(line)
        if not worn or worn.group(2) in characters:
            continue
        name = KITTEN_NAME.search(line)
        pick = stock[zlib.crc32((name.group(1) if name else str(n)).encode()) % len(stock)]
        lines[n] = line[:worn.start(2)] + pick + line[worn.end(2):]
        changed += 1
    return changed


def inspect(save: Path, declared: dict[str, list[str]], characters: set[str],
            stock: list[str], fix: bool) -> tuple[int, int, int]:
    """Returns (parts needing repair, entries dropped, kittens re-dressed)."""
    # newline="" throughout: the saves are CRLF, and letting Python translate them would
    # rewrite every line in a file this is meant to change by one.
    with open(save, encoding="utf-8", errors="surrogateescape", newline="") as handle:
        lines = handle.read().splitlines(keepends=True)

    needing = 0
    dropped: set[int] = set()

    renamed = rename_core_parts(lines) if written_before(save, CORE_BUILD) else 0
    if renamed:
        print(f"    {renamed} Core part(s) KSA renamed")

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

    wearing = sum(1 for line in lines
                  if (worn := KITTEN.search(line)) and worn.group(2) not in characters)
    if wearing:
        print(f"    {wearing} kitten(s) wearing a character the mod no longer declares")

    redressed = 0
    if wearing and fix:
        if not stock:
            print(f"    ! no Core characters to re-dress them in; set KSA_DIR -- left alone")
        else:
            redressed = redress_kittens(lines, characters, stock)

    if (dropped or redressed or renamed) and fix:
        backup = save.with_suffix(save.suffix + ".bak")
        if not backup.exists():
            shutil.copy2(save, backup)
        kept = [line for n, line in enumerate(lines) if n not in dropped]
        with open(save, "w", encoding="utf-8", errors="surrogateescape", newline="") as handle:
            handle.write("".join(kept))
        print(f"    rewritten ({len(dropped)} dropped, {redressed} re-dressed, {renamed} renamed); "
              f"original at {backup.name}")

    return needing + renamed, len(dropped), wearing


def written_before(path: Path, build: tuple[int, ...]) -> bool:
    """Whether the build that last wrote a save or design, by its meta.toml, predates `build`."""
    meta = path.parent / "meta.toml"
    found = re.search(r'^version = "v?([\d.]+)"', meta.read_text(), re.M) if meta.is_file() else None
    return found is not None and tuple(int(x) for x in found.group(1).split(".")) < build


def rename_core_parts(lines: list[str]) -> int:
    """Renames the parts CORE_RENAMES names, and renumbers each one's own connector references."""
    if any(CORE_MARK in line for line in lines[:3]):
        return 0

    renamed = 0
    for n, line in enumerate(lines):
        part = CORE_PART.search(line)
        if not part or part.group(2) not in CORE_RENAMES:
            continue
        new_id, renumber = CORE_RENAMES[part.group(2)]
        lines[n] = line[:part.start(2)] + new_id + line[part.end(2):]
        renamed += 1

        # Its own references sit one level in; a child part's are deeper and its own business.
        inner = len(line) - len(line.lstrip()) + 2
        for k in range(n + 1, len(lines)):
            depth = len(lines[k]) - len(lines[k].lstrip())
            if depth < inner and lines[k].strip():
                break
            ref = CONNECTOR_REF.search(lines[k])
            if depth == inner and ref and int(ref.group(2)) in renumber:
                lines[k] = lines[k][:ref.start(2)] + str(renumber[int(ref.group(2))]) + lines[k][ref.end(2):]

    if renamed:
        declaration = 1 if lines and lines[0].startswith("<?xml") else 0
        newline = "\r\n" if lines and lines[0].endswith("\r\n") else "\n"
        lines.insert(declaration, CORE_MARK + newline)
    return renamed


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
    ap.add_argument("--designs", action="store_true",
                    help="the vehicle designs beside it as well, which name parts as a save does")
    args = ap.parse_args()

    declared = declared_subparts()
    if not declared:
        sys.exit("no shipped parts with subparts found; is the asset XML in place?")

    characters = declared_characters()
    stock = stock_characters()

    root = args.saves or (ksa_user_dir() / "saves")
    if not root.is_dir():
        sys.exit(f"no saves folder at {root}")

    print(f"declared: " + ", ".join(f"{k.replace('KSArmory_Prefab_', '')}={len(v)}"
                                    for k, v in sorted(declared.items())))
    print(f"saves:    {root}\n")

    total_parts = 0
    total_dropped = 0
    total_kittens = 0

    files = [save / "universe.xml" for save in sorted(root.iterdir())]
    if args.designs:
        files += [design / "vehicle.xml" for design in sorted((root.parent / "Vehicles").iterdir())]
    for universe in files:
        if not universe.is_file():
            continue
        print(f"  {universe.parent.name}")
        needing, count, kittens = inspect(universe, declared, characters, stock, args.fix)
        total_parts += needing
        total_dropped += count
        total_kittens += kittens

    print()
    if total_parts == 0 and total_kittens == 0:
        print("every save lines up with the parts and characters as they are now")
        return 0

    if args.fix:
        print(f"repaired {total_parts} part(s), {total_dropped} entries dropped, "
              f"{total_kittens} kitten(s) re-dressed")
        return 0 if stock or total_kittens == 0 else 1

    print(f"{total_parts} part(s) and {total_kittens} kitten(s) would crash the game on load; "
          f"rerun with --fix")
    return 1


if __name__ == "__main__":
    sys.exit(main())
