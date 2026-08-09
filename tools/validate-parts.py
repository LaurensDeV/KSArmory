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

import argparse
import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from importlib import import_module
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parent / "model"))
meshinfo = import_module("meshinfo")

REPO = Path(__file__).resolve().parent.parent
MOD = REPO / "src" / "KSArmory"
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
    # root -- see the comment at the top of KSArmoryAssets.xml for why those are the same.
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

        # Our own Ids must resolve in our own file, whether or not Core's library could be read.
        #
        # The Core check below is skipped when that library is unavailable - offline, or a
        # different install layout - and that skip used to swallow this case too. A SubPart
        # instancing an KSArmory_* template that does not exist passed validation and then
        # killed the game on load with "PartTemplate is null". Nothing about that needs Core to
        # detect: if we named it, we declare it.
        if source.startswith("KSArmory_") and source not in local_subparts:
            print(f"  UNDECLARED SubPart InstanceOf=\"{source}\" - no such template in this file",
                  file=sys.stderr)
            problems += 1
            continue

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
    """Verifies the Pantsir's profile still agrees with the mesh its tubes were modelled in.

    The launch positions exist twice: once in tools/model/pantsir.py, which places the
    containers, and once in Sim/Arsenal.cs, which draws markers on them and spawns rounds
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
    for field, key in (("PodReferenceElevationRad", "pod_reference_elevation_deg"),
                       ("GunReferenceElevationRad", "gun_reference_elevation_deg")):
        if key in expected:
            scalars.append((field, math.radians(expected[key])))

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

    # Every slew pivot. Getting one wrong swings that assembly around the chassis rather than
    # spinning it in place, and the runtime writes the stale value back every frame -- so in game
    # it looks like the model change never happened.
    for field, key in (("PodPivotFromTurret", "pod_pivot_from_turret"),
                       ("TurretPivot", "turret_pivot"),
                       ("GunPivotFromTurret", "gun_pivot_from_turret"),
                       ("RadarPivotFromTurret", "radar_pivot_from_turret"),
                       ("OpticPivotFromTurret", "eo_pivot_from_turret")):
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

    # Scope this to the Tubes initialiser. The pivots beside it are also `new(x, y, z),` and
    # sweeping the whole file picks those up as extra tubes.
    #
    # Only the bare `new(x, y, z)` form is a generated tube position. A tube that declares its own
    # direction is written `new(new double3(...), new double3(...))` and is hand-authored for a
    # splayed launcher -- the generator only knows parallel bundles, because it reads them off a
    # mesh that has parallel tubes. Such a tube is skipped here rather than mismatched.
    block = re.search(r"Tubes\s*=\s*\[(.*?)\n\s*\]", text, re.S)
    found = [] if block is None else [
        tuple(float(v) for v in m)
        for m in re.findall(r"new\(\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\)", block.group(1))]
    want = [tuple(t) for t in expected["tubes"]]
    checked += len(want)

    if block is None:
        print("  MISSING Arsenal Tubes", file=sys.stderr)
        return problems + 1, checked

    if found != want:
        print(f"  STALE Arsenal Tubes: {len(found)} entries, "
              f"mesh has {len(want)}", file=sys.stderr)
        for i, (a, b) in enumerate(zip(found + [None] * len(want), want)):
            if a != b:
                print(f"    tube {i}: file {a}, mesh {b}", file=sys.stderr)
        print("    rerun ./tools/model/build.sh and paste the block it prints", file=sys.stderr)
        problems += 1
    else:
        print(f"  launch geometry: {len(want)} tubes match the mesh")

    # The cannon barrels, the same way and for the same reason.
    if "gun_muzzles" in expected:
        guns = re.search(r"GunMuzzles\s*=\s*\[(.*?)\n\s*\]", text, re.S)
        want_guns = [tuple(m) for m in expected["gun_muzzles"]]
        checked += len(want_guns)
        if guns is None:
            print("  MISSING Arsenal GunMuzzles", file=sys.stderr)
            problems += 1
        else:
            found_guns = [
                tuple(float(v) for v in m)
                for m in re.findall(r"new\(\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\)",
                                    guns.group(1))]
            if found_guns != want_guns:
                print(f"  STALE Arsenal GunMuzzles: {len(found_guns)} entries, "
                      f"mesh has {len(want_guns)}", file=sys.stderr)
                for i, (a, b) in enumerate(zip(found_guns + [None] * len(want_guns), want_guns)):
                    if a != b:
                        print(f"    barrel {i}: file {a}, mesh {b}", file=sys.stderr)
                problems += 1
            else:
                print(f"  cannon geometry: {len(want_guns)} barrels match the mesh")

    return problems, checked


def check_fixed_launcher_geometry(profile, munition, key, label):
    """Checks one fixed launcher's tube against what the model script emitted.

    Fixed launchers declare their own tube axis, because they have no pods for their rounds to
    follow. Parameterised rather than written per launcher: the rail was the only one for a while
    and a check that reads whichever initialiser it finds first passes whatever the second one
    says. Scoping the regex to the named block is the same reason.
    """
    muzzles = REPO / "tools" / "model" / "muzzles.json"
    if not muzzles.is_file():
        return 0, 0

    expected = json.loads(muzzles.read_text()).get(key)
    if expected is None:
        return 0, 0

    text = (MOD / "Sim" / "Arsenal.cs").read_text()
    problems = 0
    checked = 0

    found = re.search(rf"{profile}\s*=\s*new\(\)\s*\{{(.*?)\n\s*\}};", text, re.S)
    if found is None:
        print(f"  MISSING Arsenal.{profile}", file=sys.stderr)
        return 1, 1
    block = found.group(1)

    # A directed tube: position and axis together, because a fixed launcher has no pods for its
    # rounds to follow and the axis is the only thing saying which way they leave.
    checked += 1
    tube = re.search(r"Tubes\s*=\s*\[\s*new\(new\(\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\),"
                     r"\s*new\(\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\)\s*\)\s*\]", block)
    if tube is None:
        print(f"  MISSING Arsenal.{profile}.Tubes", file=sys.stderr)
        problems += 1
    else:
        got = [float(v) for v in tube.groups()]
        want = list(expected["tubes"][0]) + list(expected["tube_directions"][0])
        if any(abs(a - b) > 5e-4 for a, b in zip(got, want)):
            print(f"  STALE Arsenal.{profile}.Tubes = {tuple(got)}, "
                  f"mesh says {tuple(want)}", file=sys.stderr)
            problems += 1

    checked += 1
    offset = re.search(r"MuzzleForwardOffset\s*=\s*([\d.]+)\s*,", block)
    if offset is None:
        print(f"  MISSING Arsenal.{profile}.MuzzleForwardOffset", file=sys.stderr)
        problems += 1
    elif abs(float(offset.group(1)) - expected["muzzle_forward_offset"]) > 5e-4:
        print(f"  STALE Arsenal.{profile}.MuzzleForwardOffset = {offset.group(1)}, "
              f"mesh says {expected['muzzle_forward_offset']}", file=sys.stderr)
        problems += 1

    # The round seats half a body length back from the tube mouth, so a BodyLength that does not
    # match the mesh hangs it off the end of its own rail.
    checked += 1
    round_block = re.search(rf"{munition}\s*=\s*new\(\)\s*\{{(.*?)\n\s*\}};", text, re.S)
    body = (None if round_block is None
            else re.search(r"BodyLength\s*=\s*([\d.]+)f\s*,", round_block.group(1)))
    if body is None:
        print(f"  MISSING Arsenal.{munition}.BodyLength", file=sys.stderr)
        problems += 1
    elif abs(float(body.group(1)) - expected["body_length"]) > 5e-4:
        print(f"  STALE Arsenal.{munition}.BodyLength = {body.group(1)}, "
              f"mesh says {expected['body_length']}", file=sys.stderr)
        problems += 1

    if problems == 0:
        print(f"  {label} geometry: 1 tube and its round match the mesh")

    return problems, checked


def check_cross_body_planes():
    """Looks for faces shared between two different subparts, placed as the XML places them.

    checkmesh.py analyses one mesh at a time, so a plane shared by two *bodies* — a turntable
    resting exactly on the cap of its mast — is invisible to it and z-fights in game like any
    other coincident pair. Worse when the two ride a common axis, because the fight then rotates.

    This lives here rather than in checkmesh.py because the atlas carries no node transforms: the
    meshes are pivot-local and only this file knows where the XML puts them.
    """
    checkmesh = import_module("checkmesh")

    problems = checked = 0
    for path in sorted(MOD.glob("KSArmory*.xml")):
        root = ET.parse(path).getroot()

        atlas = root.find(".//MeshAtlas")
        if atlas is None or atlas.get("Path") is None:
            continue
        glb = MOD / atlas.get("Path")
        if not glb.is_file():
            continue

        # Definition Id -> the mesh it draws with. The MeshView copy is the same geometry under
        # another name, so it would report every body as fighting itself.
        mesh_of = {}
        for sub in root.findall("SubPart"):
            model = sub.find("PartModel/Mesh")
            if sub.get("Id") and model is not None and model.get("Id"):
                mesh_of[sub.get("Id")] = model.get("Id")

        placements = {}
        for part in root.findall("Part"):
            for sub in part.findall("SubPart"):
                mesh = mesh_of.get(sub.get("InstanceOf"))
                if mesh is None:
                    continue
                position = sub.find("Transform/Position")
                origin = ([float(position.get(axis, "0")) for axis in "XYZ"]
                          if position is not None else [0.0, 0.0, 0.0])
                # A body instanced more than once - the twelve round bodies - would collide with
                # its own copies at rest, which is how they are stowed.
                placements.setdefault(mesh, origin)

        gltf, binary = checkmesh.read_glb(str(glb))
        checked += len(placements)
        for area, (a, b) in checkmesh.cross_body_overlaps(gltf, binary, placements):
            print(f"  COPLANAR {area * 1e4:.1f} cm² shared by {a} and {b}", file=sys.stderr)
            problems += 1

    return problems, checked


def check_subpart_positions():
    """Verifies the asset XML places each articulated subpart where Arsenal.cs says its pivot is.

    Every pivot exists three times: in pantsir.py, which recentres that mesh on it; in Arsenal.cs,
    which composes the runtime pose from it; and in the XML, which places the subpart at rest. The
    mod rewrites PositionParentAsmb every frame from the Arsenal value, so an XML that disagrees
    looks correct until the drives run and then snaps.

    Also checks each marker resolves to exactly one subpart. LauncherPart.FindSubPart matches on
    the Id *containing* the marker and takes the first hit, so two matches is a silent coin toss.
    """
    source = MOD / "Sim" / "Arsenal.cs"
    text = source.read_text()

    def vector(field):
        match = re.search(
            rf"{field}\s*=\s*new\(\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\)", text)
        return [float(v) for v in match.groups()] if match else None

    def marker(field):
        match = re.search(rf'{field}\s*=\s*"([^"]+)"', text)
        return match.group(1) if match else None

    # Every launcher, not the first one the regex happens to find. Reading `text` whole checked
    # the Pantsir and silently ignored every other profile -- which is how a CIWS shipped with
    # markers that matched none of its own subparts, and drives that therefore never resolved.
    profiles = re.findall(
        r"public static readonly LauncherProfile (\w+) = new\(\)\s*\{(.*?)\n\s*\};",
        text, re.S)

    turret_pivot = vector("TurretPivot")
    if turret_pivot is None:
        print("  MISSING Arsenal.TurretPivot", file=sys.stderr)
        return 1, 1

    # Marker, and the offset from the traverse axis. The turret sits on the axis itself.
    assemblies = (("TurretMarker", None),
                  ("PodsMarker", "PodPivotFromTurret"),
                  ("GunsMarker", "GunPivotFromTurret"),
                  ("RadarMarker", "RadarPivotFromTurret"),
                  ("OpticMarker", "OpticPivotFromTurret"))

    # Per part, not across the whole mod. LauncherPart.FindSubPart searches one launcher's own
    # subparts, so a marker only has to be unique within its part -- and once a second launcher
    # exists, a global check calls "Turret" ambiguous for a configuration that runs correctly.
    by_part = {}
    for path in sorted(MOD.glob("KSArmory*.xml")):
        for part in ET.parse(path).getroot().findall("Part"):
            here = {}
            for sub in part.findall("SubPart"):
                position = sub.find("Transform/Position")
                if position is None or sub.get("Id") is None:
                    continue
                here[sub.get("Id")] = [float(position.get(axis, "0")) for axis in "XYZ"]
            if here:
                by_part[part.get("Id")] = here

    # The profile this check is about. It reads one launcher's numbers, and with several
    # registered it has to be told which -- otherwise it silently reads whichever appears first
    # and compares one launcher's markers against another's geometry.
    problems = checked = 0

    for profile_name, body in profiles:
        def field(f, scope=body):
            m = re.search(rf'{f}\s*=\s*"([^"]+)"', scope)
            return m.group(1) if m else None

        part_id = field("PartId")
        placed = by_part.get(part_id, {})
        if not placed:
            # A launcher whose part places no subparts has nothing to check. The rail is one:
            # its only moving thing is a round, which the mod positions rather than the XML.
            continue

        problems_here, checked_here = _check_markers(profile_name, body, placed, assemblies)
        problems += problems_here
        checked += checked_here

    return problems, checked


def _check_markers(profile_name, body, placed, assemblies):
    """Every declared assembly of one launcher resolves to exactly one of its own subparts.

    And sits where the profile says. Each launcher is measured against its OWN TurretPivot, which
    is the other half of what reading the whole file got wrong.
    """
    def vector(f):
        m = re.search(rf"{f}\s*=\s*new\(\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\)", body)
        return [float(v) for v in m.groups()] if m else None

    def marker(f):
        m = re.search(rf'{f}\s*=\s*"([^"]+)"', body)
        return m.group(1) if m else None

    problems = checked = 0
    for marker_field, offset_field in assemblies:
        name = marker(marker_field)
        if name is None:
            continue

        hits = [sub_id for sub_id in placed if name.lower() in sub_id.lower()]
        checked += 1
        if len(hits) != 1:
            found = ", ".join(sorted(hits)) or "nothing"
            print(f"  {profile_name}.{marker_field} '{name}' matches {found} -- "
                  f"LauncherPart.FindSubPart needs exactly one", file=sys.stderr)
            problems += 1
            continue

        offset = [0.0, 0.0, 0.0] if offset_field is None else vector(offset_field)
        if offset is None:
            print(f"  MISSING Arsenal.{offset_field}", file=sys.stderr)
            problems += 1
            continue

        turret_pivot = vector("TurretPivot") or [0.0, 0.0, 0.0]
        want = [pivot + delta for pivot, delta in zip(turret_pivot, offset)]
        got = placed[hits[0]]
        if any(abs(a - b) > 5e-4 for a, b in zip(got, want)):
            show = lambda v: "(" + ", ".join(f"{x:g}" for x in v) + ")"  # noqa: E731
            print(f"  STALE <SubPart Id=\"{hits[0]}\"> at {show(got)}, "
                  f"Arsenal.cs says {show(want)}", file=sys.stderr)
            problems += 1

    return problems, checked


def check_assets_declared():
    """Verifies every asset XML at the mod root is listed in mod.toml.

    mod.toml names its assets one by one. A file left out is simply never loaded and nothing
    reports it -- no warning at load, no missing-asset error. Whatever it declared then resolves
    to null at the point something first asks for it, which for a character is a crash inside
    KSA's own constructor rather than anything pointing back here.
    """
    toml = MOD / "mod.toml"
    declared = set(re.findall(r'"([^"]+\.xml)"', toml.read_text()))

    problems = checked = 0
    for path in sorted(MOD.glob("KSArmory*.xml")):
        checked += 1
        if path.name not in declared:
            print(f"  UNDECLARED {path.name} -- present but not in mod.toml's assets, so KSA "
                  f"never loads it", file=sys.stderr)
            problems += 1

    for name in sorted(declared):
        if not (MOD / name).is_file():
            print(f"  MISSING {name} -- listed in mod.toml, not on disk", file=sys.stderr)
            problems += 1

    return problems, checked


def check_registered_part_ids():
    """Verifies every registered LauncherProfile.PartId is declared in the asset XML.

    This is the documented extension path -- add a profile, add art -- and nothing else compares
    the two sides. A profile naming a part that exists nowhere passes the build, the tests, the
    boundary and comment checks and this validator, and then finds no launcher at run time.
    """
    source = MOD / "Sim" / "Arsenal.cs"
    declared = set()
    for path in sorted(MOD.glob("KSArmory*.xml")):
        declared |= set(re.findall(r'<Part\s+Id="([^"]+)"', path.read_text()))

    problems = checked = 0
    for part_id in re.findall(r'PartId\s*=\s*"([^"]+)"', source.read_text()):
        checked += 1
        if part_id not in declared:
            print(f'  MISSING <Part Id="{part_id}"> -- registered in Arsenal.cs, declared in no XML',
                  file=sys.stderr)
            problems += 1

    if not checked:
        print("  no PartId in Arsenal.cs -- nothing registered?", file=sys.stderr)
        problems += 1

    return problems, checked


def main():
    # Without the game installed, everything that depends only on our own files can still be
    # checked -- and on Linux that includes case, which is the difference between a mod that
    # loads on both platforms and one that only loads on Windows.
    parser = argparse.ArgumentParser(
        description="Check the part XML, the launch geometry and the registry against each other.")
    parser.add_argument("--offline", action="store_true",
                        help="skip the checks that need KSA's Core assets")
    offline = parser.parse_args().offline

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

    files = sorted(MOD.glob("KSArmory*.xml"))
    if not files:
        print(f"error: no asset XML found in {MOD}", file=sys.stderr)
        return 1

    problems = checked = 0
    for path in files:
        print(f"checking {path.relative_to(REPO)}")
        p, c = check_file(path, core_subparts, core_materials)
        problems += p
        checked += c

    print("checking every asset XML is declared in mod.toml")
    p, c = check_assets_declared()
    problems += p
    checked += c

    print("checking every registered PartId is declared in the XML")
    p, c = check_registered_part_ids()
    problems += p
    checked += c

    print("checking src/KSArmory/Sim/Arsenal.cs against the mesh")
    p, c = check_launcher_geometry()
    problems += p
    checked += c

    p, c = check_fixed_launcher_geometry("SidewinderRail", "Missile9J", "sidewinder", "rail")
    problems += p
    checked += c

    p, c = check_fixed_launcher_geometry("BombRack", "BombMk82", "bombrack", "rack")
    problems += p
    checked += c

    print("checking subpart placement against src/KSArmory/Sim/Arsenal.cs")
    p, c = check_subpart_positions()
    problems += p
    checked += c

    print("checking for planes shared between subparts")
    p, c = check_cross_body_planes()
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
