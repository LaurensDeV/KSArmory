#!/usr/bin/env python3
"""
Checks the mod's part XML before it ever reaches the game.

A bad asset Id or a mistyped texture path is a *silent* failure in-game: the part renders
untextured, or invisible, or not at all, with nothing in any log to say why. This walks the
XML and verifies four things:

  1. every SubPart InstanceOf resolves, against our own file or against Core's library
  2. every Material Id resolves, likewise
  3. every Mesh Id exists in the mesh atlas the file declares
  4. every file path (<MeshAtlas>, <Diffuse>, <Normal>, <AoRoughMetal>) is actually there

Check 3 is the one that earns its keep -- the mesh names come out of Blender, and nothing
else in the toolchain would notice a rename.

    ./tools/validate-parts.py
    KSA_DIR=/path/to/KSA ./tools/validate-parts.py

Exits non-zero if anything is unresolved or the XML is malformed.
"""

import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from importlib import import_module
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
meshinfo = import_module("meshinfo")

REPO = Path(__file__).resolve().parent.parent
MOD = REPO / "src" / "AirDefence"
KSA_DIR = Path(os.environ.get("KSA_DIR", "/mnt/c/Program Files/Kitten Space Agency"))
CORE = KSA_DIR / "Content" / "Core"

TEXTURE_TAGS = ("Diffuse", "Normal", "AoRoughMetal", "Emissive")


def exists_case_exact(path):
    """True only if the file exists with exactly this name.

    plain is_file() is not enough: Windows and macOS filesystems are case-insensitive, so a
    `Textures/airdefence_diffuse.png` typo loads there and fails on Linux, where KSA also runs.
    Comparing against the real directory listing catches it on any platform.
    """
    if not path.is_file():
        return False
    return path.name in {entry.name for entry in path.parent.iterdir()}


def collect_core_ids(core_dir):
    """Maps element tag -> set of declared Ids across every Core asset XML."""
    declared = {}
    for path in sorted(core_dir.glob("*.xml")):
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError as exc:
            print(f"  skipping unparseable {path.name}: {exc}", file=sys.stderr)
            continue
        for el in root.iter():
            ident = el.get("Id")
            if ident:
                declared.setdefault(el.tag, set()).add(ident)
    return declared


def atlas_mesh_names(glb_path):
    """Mesh names inside a .glb, or None if it cannot be read."""
    try:
        gltf = meshinfo.read_glb_json(str(glb_path))
    except Exception as exc:
        print(f"  could not read {glb_path.name}: {exc}", file=sys.stderr)
        return None
    return {m.get("name") for m in gltf.get("meshes", [])}


def check_file(path, core_subparts, core_materials):
    """Returns (problems, references checked) for one asset XML."""
    problems = checked = 0

    try:
        root = ET.parse(path).getroot()
    except ET.ParseError as exc:
        print(f"  MALFORMED: {exc}", file=sys.stderr)
        return 1, 0

    local_subparts = {el.get("Id") for el in root.iter("SubPart") if el.get("Id")}
    local_materials = {el.get("Id") for el in root.iter("PbrMaterial") if el.get("Id")}

    # Paths are resolved against the XML's own directory, which for this mod is also the mod
    # root -- see the comment at the top of AirDefenceAssets.xml for why those are the same.
    meshes_in_atlas = set()
    for el in root.iter("MeshAtlas"):
        checked += 1
        atlas = path.parent / el.get("Path", "")
        if not exists_case_exact(atlas):
            print(f"  MISSING MeshAtlas Path=\"{el.get('Path')}\"", file=sys.stderr)
            problems += 1
            continue
        names = atlas_mesh_names(atlas)
        if names is None:
            problems += 1
        else:
            meshes_in_atlas |= names
            print(f"  atlas: {el.get('Path')}  ({len(names)} meshes)")

    for tag in TEXTURE_TAGS:
        for el in root.iter(tag):
            rel = el.get("Path")
            if not rel:
                continue          # referenced by Id instead, which the Core check covers
            checked += 1
            if not exists_case_exact(path.parent / rel):
                print(f"  MISSING {tag} Path=\"{rel}\"", file=sys.stderr)
                problems += 1

    for el in root.iter("Mesh"):
        ident = el.get("Id")
        if not ident:
            continue
        checked += 1
        # Only meaningful once this file declares an atlas of its own; a mesh Id could
        # otherwise legitimately live in Core's library.
        if meshes_in_atlas and ident not in meshes_in_atlas:
            print(f"  UNRESOLVED Mesh Id=\"{ident}\" (not in the declared atlas)", file=sys.stderr)
            problems += 1

    for el in root.iter("SubPart"):
        source = el.get("InstanceOf")
        if not source:
            continue
        checked += 1
        if core_subparts is None:
            continue
        if source not in core_subparts and source not in local_subparts:
            print(f"  UNRESOLVED SubPart InstanceOf=\"{source}\"", file=sys.stderr)
            problems += 1

    for el in root.iter("Material"):
        ident = el.get("Id")
        if not ident:
            continue
        checked += 1
        if core_materials is None:
            continue
        if ident not in core_materials and ident not in local_materials:
            print(f"  UNRESOLVED Material Id=\"{ident}\"", file=sys.stderr)
            problems += 1

    for el in root.iter("PartGameData"):
        print(f"  part: {el.get('Id')}  \"{el.get('DisplayName', '')}\"")
    for el in root.iter("Part"):
        print(f"  part: {el.get('Id')}")

    return problems, checked


def check_launcher_geometry():
    """Verifies LauncherPart.cs still agrees with the mesh the tubes were modelled in.

    The launch positions exist twice: once in tools/model/pantsir.py, which places the
    containers, and once in LauncherPart.cs, which draws markers on them and spawns rounds
    from them. Nothing at build or run time connects the two, and a silent disagreement puts
    the launch markers in mid-air beside the vehicle. So compare them here.

    Skips quietly if the model has not been built in this checkout -- muzzles.json is written
    by tools/model/build.sh, which needs Blender.
    """
    muzzles = REPO / "tools" / "model" / "muzzles.json"
    source = MOD / "Sim" / "Arsenal.cs"
    if not muzzles.is_file():
        print("  (no tools/model/muzzles.json -- run tools/model/build.sh to enable this check)")
        return 0, 0

    expected = json.loads(muzzles.read_text())
    text = source.read_text()

    problems = 0
    checked = 0

    import math

    scalars = [("MuzzleForwardOffset", expected["muzzle_forward_offset"]),
               ("TubeRingRadius", expected["tube_ring_radius"])]
    if "pod_reference_elevation_deg" in expected:
        scalars.append(("PodReferenceElevationRad",
                        math.radians(expected["pod_reference_elevation_deg"])))

    for field, want in scalars:
        checked += 1
        match = re.search(rf"{field}\s*=\s*([0-9.]+)\s*,", text)
        if match is None:
            print(f"  MISSING Arsenal.{field}", file=sys.stderr)
            problems += 1
        elif abs(float(match.group(1)) - want) > 5e-4:
            print(f"  STALE Arsenal.{field} = {match.group(1)}, "
                  f"mesh says {want:.5f}", file=sys.stderr)
            problems += 1

    # The two slew pivots. Getting one of these wrong swings the turret around the chassis
    # rather than spinning it in place, which is a whole restart to discover.
    for field, key in (("PodPivotFromTurret", "pod_pivot_from_turret"),
                       ("TurretPivot", "turret_pivot"),
                       ("RadarPivotFromTurret", "radar_pivot_from_turret")):
        if key not in expected:
            continue
        checked += 1
        match = re.search(rf"{field}\s*=\s*new\(\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\)", text)
        if match is None:
            print(f"  MISSING Arsenal.{field}", file=sys.stderr)
            problems += 1
        else:
            got = [float(v) for v in match.groups()]
            if any(abs(a - b) > 5e-4 for a, b in zip(got, expected[key])):
                print(f"  STALE Arsenal.{field} = {tuple(got)}, "
                      f"mesh says {tuple(expected[key])}", file=sys.stderr)
                problems += 1

    # Scope this to the TubeOffsets initialiser. The pivots beside it are also `new(x, y, z),`
    # and sweeping the whole file picks those up as extra tubes.
    block = re.search(r"TubeOffsets\s*=\s*\[(.*?)\]", text, re.S)
    found = [] if block is None else [
        tuple(float(v) for v in m)
        for m in re.findall(r"new\(\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\)", block.group(1))]
    want = [tuple(t) for t in expected["tubes"]]
    checked += len(want)

    if block is None:
        print("  MISSING Arsenal TubeOffsets", file=sys.stderr)
        return problems + 1, checked

    if found != want:
        print(f"  STALE Arsenal TubeOffsets: {len(found)} entries, "
              f"mesh has {len(want)}", file=sys.stderr)
        for i, (a, b) in enumerate(zip(found + [None] * len(want), want)):
            if a != b:
                print(f"    tube {i}: file {a}, mesh {b}", file=sys.stderr)
        print("    rerun ./tools/model/build.sh and paste the block it prints", file=sys.stderr)
        problems += 1
    else:
        print(f"  launch geometry: {len(want)} tubes match the mesh")

    return problems, checked


def main():
    # Without the game installed, everything that depends only on our own files can still be
    # checked -- and on Linux that includes case, which is the difference between a mod that
    # loads on both platforms and one that only loads on Windows.
    offline = "--offline" in sys.argv

    if offline:
        print("offline: skipping the checks that need KSA's Core assets\n")
        core_subparts = core_materials = None
    elif not CORE.is_dir():
        print(f"error: Core content not found at {CORE}", file=sys.stderr)
        print("       set KSA_DIR to your install, or pass --offline", file=sys.stderr)
        return 1
    else:
        print(f"reading Core assets from {CORE}")
        declared = collect_core_ids(CORE)
        core_subparts = declared.get("SubPart", set())
        core_materials = declared.get("PbrMaterial", set())
        print(f"  {len(core_subparts)} subparts, {len(core_materials)} materials declared\n")

    files = sorted(MOD.glob("AirDefence*.xml"))
    if not files:
        print(f"error: no asset XML found in {MOD}", file=sys.stderr)
        return 1

    problems = checked = 0
    for path in files:
        print(f"checking {path.relative_to(REPO)}")
        p, c = check_file(path, core_subparts, core_materials)
        problems += p
        checked += c

    print("checking src/AirDefence/Sim/Arsenal.cs against the mesh")
    p, c = check_launcher_geometry()
    problems += p
    checked += c

    print()
    if problems:
        print(f"FAILED: {problems} problem(s) across {checked} reference(s)", file=sys.stderr)
        return 1

    print(f"OK: {checked} asset reference(s) resolve")
    return 0


if __name__ == "__main__":
    sys.exit(main())
