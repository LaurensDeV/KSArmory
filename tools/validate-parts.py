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

        # The mod's own Ids must resolve in its own file, whether or not Core's library could be
        # read.
        #
        # The Core check below is skipped when that library is unavailable - offline, or a
        # different install layout - and this case must not be skipped with it. A SubPart
        # instancing a KSArmory_* template that does not exist kills the game on load with
        # "PartTemplate is null", and detecting that needs no Core: a name used here is declared
        # here.
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


def check_turret_launcher_geometry(profile, key, label):
    """Verifies one turreted launcher's profile still agrees with the mesh it was modelled in.

    The launch positions exist twice: once in the model script, which places the containers and
    the barrels, and once in Sim/Arsenal.cs, which draws markers on them and spawns rounds from
    them. Nothing at build or run time connects the two, and a silent disagreement puts the
    launch markers in mid-air beside the vehicle. So compare them here.

    `key` names the muzzles.json block; None means the top-level one, which is the Pantsir's.
    Every regex is scoped to the profile's own initialiser for the same reason
    check_fixed_launcher_geometry is: an unscoped search binds to whichever launcher appears
    first in the file and passes whatever every other one says.

    Fields absent from the block are skipped rather than missing, because a turret is not
    obliged to carry all of them -- the CIWS has no tubes and no pods.

    Skips quietly if the model has not been built in this checkout -- muzzles.json is written
    by tools/model/build.sh, which needs Blender.
    """
    muzzles = REPO / "tools" / "model" / "muzzles.json"
    source = MOD / "Sim" / "Arsenal.cs"
    if not muzzles.is_file():
        print("  (no tools/model/muzzles.json -- run tools/model/build.sh to enable this check)")
        return 0, 0

    document = json.loads(muzzles.read_text())
    expected = document if key is None else document.get(key)
    if expected is None:
        return 0, 0

    text = source.read_text()

    problems = 0
    checked = 0

    import math

    found = re.search(rf"{profile}\s*=\s*new\(\)\s*\{{(.*?)\n\s*\}};", text, re.S)
    if found is None:
        print(f"  MISSING Arsenal.{profile}", file=sys.stderr)
        return 1, 1
    text = found.group(1)

    scalars = []
    for field, name in (("MuzzleForwardOffset", "muzzle_forward_offset"),
                        ("TubeRingRadius", "tube_ring_radius")):
        if name in expected:
            scalars.append((field, expected[name]))
    for field, name in (("PodReferenceElevationRad", "pod_reference_elevation_deg"),
                        ("GunReferenceElevationRad", "gun_reference_elevation_deg")):
        if name in expected:
            scalars.append((field, math.radians(expected[name])))

    for field, want in scalars:
        checked += 1
        match = re.search(rf"{field}\s*=\s*([0-9.]+)\s*,", text)
        if match is None:
            print(f"  MISSING Arsenal.{profile}.{field}", file=sys.stderr)
            problems += 1
        elif abs(float(match.group(1)) - want) > 5e-4:
            print(f"  STALE Arsenal.{profile}.{field} = {match.group(1)}, "
                  f"mesh says {want:.5f}", file=sys.stderr)
            problems += 1

    # Every slew pivot. Getting one wrong swings that assembly around the chassis rather than
    # spinning it in place, and the runtime writes the stale value back every frame -- so in game
    # it looks like the model change never happened.
    for field, name in (("PodPivotFromTurret", "pod_pivot_from_turret"),
                        ("TurretPivot", "turret_pivot"),
                        ("GunPivotFromTurret", "gun_pivot_from_turret"),
                        ("RadarPivotFromTurret", "radar_pivot_from_turret"),
                        ("OpticBaseFromTurret", "optic_base_from_turret")):
        if name not in expected:
            continue
        checked += 1
        match = re.search(rf"{field}\s*=\s*new\(\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\)", text)
        if match is None:
            print(f"  MISSING Arsenal.{profile}.{field}", file=sys.stderr)
            problems += 1
        else:
            got = [float(v) for v in match.groups()]
            if any(abs(a - b) > 5e-4 for a, b in zip(got, expected[name])):
                print(f"  STALE Arsenal.{profile}.{field} = {tuple(got)}, "
                      f"mesh says {tuple(expected[name])}", file=sys.stderr)
                problems += 1

    # Scope this to the Tubes initialiser. The pivots beside it are also `new(x, y, z),` and
    # sweeping the whole profile picks those up as extra tubes.
    #
    # Only the bare `new(x, y, z)` form is a generated tube position. A tube that declares its own
    # direction is written `new(new double3(...), new double3(...))` and is hand-authored for a
    # splayed launcher -- the generator only knows parallel bundles, because it reads them off a
    # mesh that has parallel tubes. Such a tube is skipped here rather than mismatched.
    block = re.search(r"Tubes\s*=\s*\[(.*?)\]", text, re.S)
    found = [] if block is None else [
        tuple(float(v) for v in m)
        for m in re.findall(r"new\(\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\)", block.group(1))]
    want = [tuple(t) for t in expected.get("tubes", [])]
    checked += max(len(want), 1)

    if block is None:
        print(f"  MISSING Arsenal.{profile}.Tubes", file=sys.stderr)
        return problems + 1, checked

    if found != want:
        print(f"  STALE Arsenal.{profile}.Tubes: {len(found)} entries, "
              f"mesh has {len(want)}", file=sys.stderr)
        for i, (a, b) in enumerate(zip(found + [None] * len(want), want)):
            if a != b:
                print(f"    tube {i}: file {a}, mesh {b}", file=sys.stderr)
        print("    rerun ./tools/model/build.sh and paste the block it prints", file=sys.stderr)
        problems += 1
    elif want:
        print(f"  {label} launch geometry: {len(want)} tubes match the mesh")
    else:
        print(f"  {label} carries no missiles, and the mesh models none")

    # The cannon barrels, the same way and for the same reason.
    if "gun_muzzles" in expected:
        guns = re.search(r"GunMuzzles\s*=\s*\[(.*?)\n\s*\]", text, re.S)
        want_guns = [tuple(m) for m in expected["gun_muzzles"]]
        checked += len(want_guns)
        if guns is None:
            print(f"  MISSING Arsenal.{profile}.GunMuzzles", file=sys.stderr)
            problems += 1
        else:
            found_guns = [
                tuple(float(v) for v in m)
                for m in re.findall(r"new\(\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\)",
                                    guns.group(1))]
            if found_guns != want_guns:
                print(f"  STALE Arsenal.{profile}.GunMuzzles: {len(found_guns)} entries, "
                      f"mesh has {len(want_guns)}", file=sys.stderr)
                for i, (a, b) in enumerate(zip(found_guns + [None] * len(want_guns), want_guns)):
                    if a != b:
                        print(f"    barrel {i}: file {a}, mesh {b}", file=sys.stderr)
                problems += 1
            else:
                print(f"  {label} cannon geometry: {len(want_guns)} barrels match the mesh")

    return problems, checked


def check_fixed_launcher_geometry(profile, munition, key, label):
    """Checks one fixed launcher's tube against what the model script emitted.

    Fixed launchers declare their own tube axis, because they have no pods for their rounds to
    follow. Parameterised rather than written per launcher: a check that reads whichever
    initialiser it finds first passes whatever every other one says. Scoping the regex to the
    named block is the same reason.
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

        gltf, binary = checkmesh.read_glb(str(glb))

        # One part at a time. Two bodies can only z-fight if they are drawn together, and bodies
        # belonging to different parts never are -- they are separate things a player attaches
        # separately. Pooling them reports every pair of parts whose mounting faces both sit at
        # X = 0, which is all of them by construction.
        for part in root.findall("Part"):
            placements = {}
            for sub in part.findall("SubPart"):
                mesh = mesh_of.get(sub.get("InstanceOf"))
                if mesh is None:
                    continue
                position = sub.find("Transform/Position")
                origin = ([float(position.get(axis, "0")) for axis in "XYZ"]
                          if position is not None else [0.0, 0.0, 0.0])
                rotation = sub.find("Transform/Rotation")
                euler = ([float(rotation.get(axis, "0")) for axis in "XYZ"]
                         if rotation is not None else [0.0, 0.0, 0.0])
                # A body instanced more than once - the twelve round bodies - would collide with
                # its own copies at rest, which is how they are stowed.
                placements.setdefault(mesh, (origin, euler))

            checked += len(placements)
            for area, (a, b) in checkmesh.cross_body_overlaps(gltf, binary, placements):
                print(f"  COPLANAR {area * 1e4:.1f} cm² shared by {a} and {b} "
                      f"in {part.get('Id')}", file=sys.stderr)
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

    # Every launcher, not the first one the regex happens to find. Reading `text` whole checks
    # one profile and silently ignores the rest, which lets a launcher carry markers matching
    # none of its own subparts and drives that therefore never resolve.
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
                  ("OpticBaseMarker", "OpticBaseFromTurret"))

    # Per part, not across the whole mod. LauncherPart.FindSubPart searches one launcher's own
    # subparts, so a marker only has to be unique within its part -- and with several launchers
    # registered, a global check calls "Turret" ambiguous for a configuration that runs correctly.
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


def check_asset_id_collisions():
    """No Id may name two different assets, counting the meshes the atlas registers.

    A <MeshAtlas> registers every mesh under its glTF node name, so declaring a SubPart -- or
    anything else -- under that same name puts two assets on one Id and the loader keeps whichever
    it saw first. Nothing fails: the mod loads, the part renders, the game runs. Only an importer
    that enforces uniqueness, such as SpaceDock's, rejects it.
    """
    problems = checked = 0

    # Across the whole mod, not per file: the loader registers one namespace for all of them, and
    # the importer's rule is "declared more than once within this mod". Two files each internally
    # consistent can still collide with each other.
    from_atlas = set()
    declared = {}

    for path in sorted(MOD.glob("KSArmory*.xml")):
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError:
            continue

        for el in root.iter("MeshAtlas"):
            atlas = path.parent / el.get("Path", "")
            if atlas.is_file():
                from_atlas |= set(atlas_mesh_names(atlas))

        # Top-level only. An Id deeper in the tree is a *reference* -- <Mesh Id>, <StartSound Id>,
        # a nested <SubPart Id> naming an instance -- and only the direct children of <Assets>
        # register a name. Walking the whole tree flags every reference as a redeclaration.
        for el in root:
            ident = el.get("Id")
            if ident is None:
                continue

            # A *GameData entry is a companion keyed on another asset's Id -- that is how KSA
            # pairs a part's art with its physics, and Core does the same. It is a different
            # registry, so it is not a redeclaration.
            if el.tag.endswith("GameData"):
                continue

            checked += 1

            if ident in declared:
                where, first = declared[ident]
                print(f"  DUPLICATE Id \"{ident}\" on <{first}> in {where} and <{el.tag}> "
                      f"in {path.name}", file=sys.stderr)
                problems += 1
            elif ident in from_atlas:
                print(f"  DUPLICATE Id \"{ident}\" on <{el.tag}> in {path.name} — the atlas "
                      f"already registers a mesh under that name", file=sys.stderr)
                problems += 1

            declared[ident] = (path.name, el.tag)

    if problems == 0:
        print(f"  {checked} asset id(s), none declared twice")

    return problems, checked


def check_body_markers():
    """Verifies every MunitionProfile body and fin marker matches a declared subpart instance.

    A round's drawn body is found by substring: LauncherPart takes every subpart whose instance Id
    *contains* the marker. A marker matching nothing is completely silent -- the round launches,
    flies, fuses and detonates exactly as the log says, and the body simply never leaves the rail.
    That shipped once, as BodyMarker "Bomb" against a subpart declared KSArmory_Rack_Mk8200.

    Checked against the subparts of *the launcher's own part*, not against every subpart in the mod.
    The global form passes a marker that resolves on somebody else's launcher, which is exactly how
    a nuclear rack instancing the Mk 82's bodies shipped with BodyMarker "Mk82": the name existed,
    on the other rack, and the bomb was released invisibly.
    """
    source = MOD / "Sim" / "Arsenal.cs"

    instances = set()
    for path in sorted(MOD.glob("KSArmory*.xml")):
        instances |= set(re.findall(r'<SubPart\s+Id="([^"]+)"\s+InstanceOf=', path.read_text()))

    problems = checked = 0
    text = source.read_text()

    for munition, body in re.findall(r'(\w+)\s*=\s*new\(\)\s*\{(.*?)\n\s*\};', text, re.S):
        for field in ("BodyMarker", "FinMarker"):
            found = re.search(rf'{field}\s*=\s*"([^"]+)"', body)
            if found is None:
                continue

            marker = found.group(1)
            checked += 1

            if not any(marker.lower() in name.lower() for name in instances):
                print(f"  MISSING subpart for {munition}.{field} = \"{marker}\" — no declared "
                      f"subpart instance contains it, so the round body never moves",
                      file=sys.stderr)
                problems += 1

    p, c = check_body_markers_resolve_on_their_own_launcher(text)
    problems += p
    checked += c

    if problems == 0 and checked:
        print(f"  body markers: {checked} match a declared subpart")

    return problems, checked


def part_subparts(part_id):
    """Subpart instance Ids declared inside one <Part>, or None if that Part is not declared."""
    for path in sorted(MOD.glob("KSArmory*.xml")):
        block = re.search(rf'<Part\s+Id="{re.escape(part_id)}"\s*>(.*?)</Part>',
                          path.read_text(), re.S)
        if block is not None:
            return set(re.findall(r'<SubPart\s+Id="([^"]+)"', block.group(1)))
    return None


def check_body_markers_resolve_on_their_own_launcher(text):
    """Every launcher's round has to have a body on *that* launcher's part.

    LauncherPart matches the marker against the subparts of the part it found, so a marker naming a
    subpart of a different launcher resolves nowhere at runtime and the round is invisible.
    """
    problems = checked = 0

    for launcher in registered(text, "Launchers"):
        block = re.search(rf'{launcher}\s*=\s*new\(\)\s*\{{(.*?)\n\s*\}};', text, re.S)
        if block is None:
            continue

        part = re.search(r'PartId\s*=\s*"([^"]+)"', block.group(1))
        key = re.search(r'Munition\s*=\s*"([^"]+)"', block.group(1))
        if part is None or key is None:
            continue

        round_block = re.search(rf'=\s*new\(\)\s*\{{([^}}]*?Name\s*=\s*"{re.escape(key.group(1))}".*?)\n\s*\}};',
                                text, re.S)
        if round_block is None:
            continue

        marker = re.search(r'BodyMarker\s*=\s*"([^"]+)"', round_block.group(1))
        if marker is None:
            continue                      # a round with no drawn body, which is allowed

        subparts = part_subparts(part.group(1))
        if subparts is None:
            continue                      # check_registered_part_ids reports an undeclared part

        checked += 1
        if not any(marker.group(1).lower() in name.lower() for name in subparts):
            print(f"  UNRESOLVED {launcher}: its round's BodyMarker \"{marker.group(1)}\" matches no "
                  f"subpart of {part.group(1)}, so the round is released invisibly", file=sys.stderr)
            problems += 1

    return problems, checked


def check_editor_tags(core_dir):
    """Verifies every <EditorTag> a part names is defined, here or by Core.

    A tag with no <EditorTagDef> is a *warning* in KSA's own log and nothing else: the part still
    loads, still appears under All, and simply has no category of its own. So a typo -- or Core
    renaming a tag under us -- costs a part its place in the picker with nothing failing.

    The flags are the other half and matter more. A tag carries RootPartWhitelist,
    FaceSnapTargetWhitelist and DiameterFilterlist, so moving a part between tags silently changes
    whether it can be a craft's root part and what can attach to it.

    Skips the Core half offline: those definitions live in the game install.
    """
    problems = 0
    checked = 0

    declared = set()
    used = {}

    for path in sorted(MOD.glob("KSArmory*.xml")):
        text = path.read_text()
        declared.update(re.findall(r'<EditorTagDef\s+Id="([^"]+)"', text))
        for tag in re.findall(r'<EditorTag\s+Value="([^"]+)"', text):
            used.setdefault(tag, path.name)

    if core_dir is not None:
        for path in Path(core_dir).rglob("*.xml"):
            try:
                declared.update(re.findall(r'<EditorTagDef\s+Id="([^"]+)"', path.read_text()))
            except (OSError, UnicodeDecodeError):
                continue

    for tag, where in sorted(used.items()):
        checked += 1
        if core_dir is None and tag not in declared:
            continue                       # offline: Core's own tags are not readable
        if tag in declared:
            continue

        print(f"  UNDECLARED EditorTag \"{tag}\" in {where} -- no <EditorTagDef> defines it",
              file=sys.stderr)
        problems += 1

    if problems == 0 and checked:
        scope = "ours" if core_dir is None else "ours and Core's"
        print(f"  editor tags: {checked} tag(s) used, all declared ({scope})")

    return problems, checked


# Which muzzles.json block each registered launcher's geometry was emitted into, and what to call
# it in a message. The key cannot be derived from the profile name -- the Pantsir's block is the
# top-level document rather than a named one -- so it is written down, and main() fails on a
# registered launcher missing from here rather than skipping it.
LAUNCHER_GEOMETRY = {
    "PantsirS1": (None, "Pantsir"),
    "Ciws": ("ciws", "CIWS"),
    "SidewinderRail": ("sidewinder", "rail"),
    "BombRack": ("bombrack", "rack"),
    "NukeRack": ("bombrack", "nuclear rack"),
    "AmraamRail": (None, "AMRAAM rail"),      # authored -- see AUTHORED_LAUNCHERS below
    "HarmRail": (None, "HARM rail"),          # authored, as above
    "MirvBus": (None, "MIRV bus"),            # authored, clustered -- CLUSTER_LAUNCHERS
}

# Launchers whose art was authored rather than generated, and whose geometry is therefore checked
# against the committed mesh and XML instead of against muzzles.json.
#
# The generated path can compare Arsenal.cs to what the model script printed, because that script
# is in the repository and anyone can rerun it. An authored part's source is a .blend that is
# deliberately not, so there is nothing to rerun and nothing to print -- and the numbers still
# exist three times over: in the mesh, in the seat position the part XML declares, and here.
#
#   profile -> (part Id, seated round's SubPart Id, round's mesh Id, munition profile, label)
AUTHORED_LAUNCHERS = {
    "AmraamRail": ("KSArmory_Prefab_AmraamRail", "KSArmory_Amraam_Round00",
                   "KSArmory_Subpart_Amraam", "Missile120C", "AMRAAM rail"),
    "HarmRail": ("KSArmory_Prefab_HarmRail", "KSArmory_Harm_Round00",
                 "KSArmory_Subpart_Harm", "MissileAgm88", "HARM rail"),
}


def check_authored_launcher_geometry(profile, part_id, seat_id, mesh_id, munition, label):
    """Checks one authored fixed launcher's tube against the mesh and the XML that place it.

    Three numbers have to agree or the round is drawn somewhere other than where it is fired
    from: the seat offset out of the mounting face, the body length, and the tube mouth, which is
    the first plus half the second. Nothing in the build would notice -- the part loads, the round
    renders, and it leaves from a point that is not its nose.
    """
    text = (MOD / "Sim" / "Arsenal.cs").read_text()
    problems = checked = 0

    found = re.search(rf"{profile}\s*=\s*new\(\)\s*\{{(.*?)\n\s*\}};", text, re.S)
    if found is None:
        print(f"  MISSING Arsenal.{profile}", file=sys.stderr)
        return 1, 1
    block = found.group(1)

    seat = bounds = None
    for path in sorted(MOD.glob("KSArmory*.xml")):
        root = ET.parse(path).getroot()
        for part in root.findall("Part"):
            if part.get("Id") != part_id:
                continue
            for sub in part.findall("SubPart"):
                if sub.get("Id") != seat_id:
                    continue
                position = sub.find("Transform/Position")
                if position is not None:
                    seat = [float(position.get(axis, "0")) for axis in "XYZ"]
        for atlas in root.findall(".//MeshAtlas"):
            glb = MOD / atlas.get("Path", "")
            if not glb.is_file():
                continue
            gltf = meshinfo.read_glb_json(str(glb))
            for mesh in gltf.get("meshes", []):
                if mesh.get("name") == mesh_id:
                    bounds = meshinfo.mesh_bounds(gltf, mesh)

    if seat is None:
        print(f"  MISSING <SubPart Id=\"{seat_id}\"> position in the part XML", file=sys.stderr)
        return 1, 1
    if bounds is None or bounds[0] is None:
        print(f"  MISSING mesh {mesh_id} in any declared atlas", file=sys.stderr)
        return 1, 1
    lo, hi = bounds
    length = hi[0] - lo[0]

    # The mod seats a round half a body length back from the tube mouth, which takes the mesh
    # origin for the body's centre. A mesh centred anywhere else is drawn off its own rail by
    # exactly that error, and nothing else in the toolchain looks.
    checked += 1
    if abs(lo[0] + hi[0]) > 5e-4:
        print(f"  OFF-CENTRE mesh {mesh_id}: spans {lo[0]:.4f}..{hi[0]:.4f} along its own axis, "
              f"so its origin is not its centre", file=sys.stderr)
        problems += 1

    checked += 1
    body = re.search(rf"{munition}\s*=\s*new\(\)\s*\{{(.*?)\n\s*\}};", text, re.S)
    body = None if body is None else re.search(r"BodyLength\s*=\s*([\d.]+)f\s*,", body.group(1))
    if body is None:
        print(f"  MISSING Arsenal.{munition}.BodyLength", file=sys.stderr)
        problems += 1
    elif abs(float(body.group(1)) - length) > 5e-4:
        print(f"  STALE Arsenal.{munition}.BodyLength = {body.group(1)}, "
              f"mesh is {length:.4f}", file=sys.stderr)
        problems += 1

    checked += 1
    offset = re.search(r"MuzzleForwardOffset\s*=\s*([\d.]+)\s*,", block)
    if offset is None:
        print(f"  MISSING Arsenal.{profile}.MuzzleForwardOffset", file=sys.stderr)
        problems += 1
    elif abs(float(offset.group(1)) - seat[0]) > 5e-4:
        print(f"  STALE Arsenal.{profile}.MuzzleForwardOffset = {offset.group(1)}, "
              f"the XML seats the round at {seat[0]}", file=sys.stderr)
        problems += 1

    # The tube mouth is the round's nose: the seat, plus half a body length along the axis it
    # leaves on. Getting this wrong by the whole length is what firing from the tail looks like.
    checked += 1
    tube = re.search(r"Tubes\s*=\s*\[\s*new\(new\(\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\),"
                     r"\s*new\(\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\)\s*\)\s*\]", block)
    if tube is None:
        print(f"  MISSING Arsenal.{profile}.Tubes", file=sys.stderr)
        problems += 1
    else:
        got = [float(v) for v in tube.groups()]
        axis = got[3:]
        want = [seat[i] + axis[i] * length / 2 for i in range(3)] + axis
        if any(abs(a - b) > 5e-4 for a, b in zip(got, want)):
            print(f"  STALE Arsenal.{profile}.Tubes = {tuple(got)}, the seat and the mesh "
                  f"say {tuple(round(v, 5) for v in want)}", file=sys.stderr)
            problems += 1

    if problems == 0:
        print(f"  {label} geometry: 1 tube and its round match the mesh and the XML")

    return problems, checked

# Authored launchers carrying a *cluster* of seated rounds rather than one. The single-tube check
# above cannot cover them: it reads one tube out of Tubes and compares it to one seat, where these
# have to agree seat by seat, and each seat points somewhere different.
#
#   profile -> (part Id, seat Id prefix, round's mesh Id, munition profile, count, label)
CLUSTER_LAUNCHERS = {
    "MirvBus": ("KSArmory_Prefab_MirvBus", "KSArmory_Mirv_Rv",
                "KSArmory_Subpart_Rv", "ReentryVehicleMk21", 6, "MIRV bus"),
}


def euler_forward(euler):
    """Where a subpart's own +X points after <Rotation>, through checkmesh's reading of it.

    Derived from euler_quaternion rather than reimplemented, because the whole value of this check
    is that it uses the same convention the cross-body pass does. Two spellings of XYZ Euler that
    disagree would let a seat and its tube both be wrong in the same direction.
    """
    qx, qy, qz, qw = import_module("checkmesh").euler_quaternion(euler)
    v, q = (1.0, 0.0, 0.0), (qx, qy, qz)

    def cross(a, b):
        return (a[1]*b[2] - a[2]*b[1], a[2]*b[0] - a[0]*b[2], a[0]*b[1] - a[1]*b[0])

    inner = cross(q, tuple(cross(q, v)[i] + qw * v[i] for i in range(3)))
    return tuple(v[i] + 2.0 * inner[i] for i in range(3))


def check_cluster_launcher_geometry(profile, part_id, seat_prefix, mesh_id, munition, count, label):
    """Checks every seat of a clustered launcher against its tube, the mesh and the XML.

    Same three numbers as the single-tube case and one more: each seat has its own direction, so a
    tube paired with the wrong seat puts a warhead on a neighbour's vector. That is invisible until
    two of them are in the air.
    """
    text = (MOD / "Sim" / "Arsenal.cs").read_text()
    problems = checked = 0

    found = re.search(rf"{profile}\s*=\s*new\(\)\s*\{{(.*?)\n\s*\}};", text, re.S)
    if found is None:
        print(f"  MISSING Arsenal.{profile}", file=sys.stderr)
        return 1, 1
    block = found.group(1)

    seats, bounds = {}, None
    for path in sorted(MOD.glob("KSArmory*.xml")):
        root = ET.parse(path).getroot()
        for part in root.findall("Part"):
            if part.get("Id") != part_id:
                continue
            for sub in part.findall("SubPart"):
                sid = sub.get("Id") or ""
                if not sid.startswith(seat_prefix):
                    continue
                position = sub.find("Transform/Position")
                rotation = sub.find("Transform/Rotation")
                if position is None:
                    continue
                seats[sid] = ([float(position.get(a, "0")) for a in "XYZ"],
                              [float(rotation.get(a, "0")) for a in "XYZ"]
                              if rotation is not None else [0.0, 0.0, 0.0])
        for atlas in root.findall(".//MeshAtlas"):
            glb = MOD / atlas.get("Path", "")
            if not glb.is_file():
                continue
            gltf = meshinfo.read_glb_json(str(glb))
            for mesh in gltf.get("meshes", []):
                if mesh.get("name") == mesh_id:
                    bounds = meshinfo.mesh_bounds(gltf, mesh)

    checked += 1
    if len(seats) != count:
        print(f"  MISCOUNT {part_id}: {len(seats)} <SubPart Id=\"{seat_prefix}..\">, "
              f"Arsenal.{profile} declares {count} tubes", file=sys.stderr)
        return problems + 1, checked
    if bounds is None or bounds[0] is None:
        print(f"  MISSING mesh {mesh_id} in any declared atlas", file=sys.stderr)
        return problems + 1, checked

    lo, hi = bounds
    length = hi[0] - lo[0]

    checked += 1
    if abs(lo[0] + hi[0]) > 5e-4:
        print(f"  OFF-CENTRE mesh {mesh_id}: spans {lo[0]:.4f}..{hi[0]:.4f} along its own axis, "
              f"so its origin is not its centre", file=sys.stderr)
        problems += 1

    checked += 1
    body = re.search(rf"{munition}\s*=\s*new\(\)\s*\{{(.*?)\n\s*\}};", text, re.S)
    body = None if body is None else re.search(r"BodyLength\s*=\s*([\d.]+)f\s*,", body.group(1))
    if body is None:
        print(f"  MISSING Arsenal.{munition}.BodyLength", file=sys.stderr)
        problems += 1
    elif abs(float(body.group(1)) - length) > 5e-4:
        print(f"  STALE Arsenal.{munition}.BodyLength = {body.group(1)}, "
              f"mesh is {length:.4f}", file=sys.stderr)
        problems += 1

    tubes = re.findall(r"new\(new\(\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\),"
                       r"\s*new\(\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\)\s*\)", block)
    checked += 1
    if len(tubes) != count:
        print(f"  MISCOUNT Arsenal.{profile}.Tubes: {len(tubes)}, expected {count}", file=sys.stderr)
        return problems + 1, checked

    for i, sid in enumerate(sorted(seats)):
        seat, euler = seats[sid]
        axis = euler_forward(euler)
        want = [seat[j] + axis[j] * length / 2 for j in range(3)] + list(axis)
        got = [float(v) for v in tubes[i]]
        checked += 1
        if any(abs(a - b) > 5e-4 for a, b in zip(got, want)):
            print(f"  STALE Arsenal.{profile}.Tubes[{i}] = {tuple(got)}, {sid} and the mesh "
                  f"say {tuple(round(v, 5) for v in want)}", file=sys.stderr)
            problems += 1

    if problems == 0:
        print(f"  {label} geometry: {count} tubes and their rounds match the mesh and the XML")

    return problems, checked


# Munition named by each fixed launcher, whose body length the tube standoff is checked against.
# A turreted launcher's standoff comes off its pods instead, so it needs no entry.
FIXED_LAUNCHER_MUNITION = {
    "SidewinderRail": "Missile9J",
    "BombRack": "BombMk82",
    "NukeRack": "NukeB61",
}


def registered(text, registry):
    """The profile field names in one of Arsenal's registry lists, in declared order.

    The registry is what the mod actually loads, so it is what the geometry checks below must be
    driven from. Naming the launchers here by hand instead is how a fifth one gets no check at
    all while this script still exits 0 -- the shape CLAUDE.md warns about, reached at four.
    """
    found = re.search(rf"{registry}\s*=\s*\[(.*?)\];", text, re.S)
    if found is None:
        return []
    return [name.strip() for name in found.group(1).split(",") if name.strip()]


def trains(text, profile):
    """Whether a launcher declares training gear, which is what picks its geometry check."""
    found = re.search(rf"{profile}\s*=\s*new\(\)\s*\{{(.*?)\n\s*\}};", text, re.S)
    return found is not None and "TurretMarker" in found.group(1)


def check_launcher_geometry():
    """Runs the right geometry check over every launcher in the registry, and misses none.

    Turreted or fixed is read off the profile rather than listed, so a launcher that grows a
    turret is checked as one without this script being told.
    """
    text = (MOD / "Sim" / "Arsenal.cs").read_text()
    problems = 0
    checked = 0

    for profile in registered(text, "Launchers"):
        if profile not in LAUNCHER_GEOMETRY:
            print(f"  UNCHECKED Arsenal.{profile}: no geometry check. Add it to "
                  f"LAUNCHER_GEOMETRY in tools/validate-parts.py", file=sys.stderr)
            problems += 1
            checked += 1
            continue

        key, label = LAUNCHER_GEOMETRY[profile]
        if profile in CLUSTER_LAUNCHERS:
            p, c = check_cluster_launcher_geometry(profile, *CLUSTER_LAUNCHERS[profile])
            problems += p; checked += c
            continue
        if profile in AUTHORED_LAUNCHERS:
            p, c = check_authored_launcher_geometry(profile, *AUTHORED_LAUNCHERS[profile])
        elif trains(text, profile):
            p, c = check_turret_launcher_geometry(profile, key, label)
        else:
            p, c = check_fixed_launcher_geometry(
                profile, FIXED_LAUNCHER_MUNITION.get(profile, ""), key, label)
        problems += p
        checked += c

    return problems, checked


# Which model script emitted each optical head's geometry. Two profiles share the mast director's
# block because they are the same instrument on different hosts; the pod is its own model, its own
# mechanism and its own tool. A profile absent from here is checked against nothing, so a new head
# has to be named -- the same trap tools/model/checkswept.py's vehicles() has.
OPTIC_GEOMETRY = {
    "EoDirector": "optic",
    "PantsirDirector": "optic",
    "Litening": "litening",
}


def check_optic_geometry(profile="EoDirector"):
    """Verifies the optical head's pivot agrees in all three places it is written down.

    The model script recentres the moving meshes on it, Sim/Arsenal.cs aims from it, and the asset
    XML puts the bodies back at it. A disagreement between the first two swings the head around its
    mount instead of turning it in place; between the first and third it draws the head somewhere
    the mod is not aiming from, and the picture points somewhere the model does not.

    Nothing at build or run time connects the three, which is the whole reason for this.
    """
    muzzles = REPO / "tools" / "model" / "muzzles.json"
    if not muzzles.is_file():
        return 0, 0

    block_name = OPTIC_GEOMETRY.get(profile)
    if block_name is None:
        print(f"  UNCHECKED Arsenal.{profile}: no entry in OPTIC_GEOMETRY, so its pivot is "
              f"compared against nothing", file=sys.stderr)
        return 1, 1

    expected = json.loads(muzzles.read_text()).get(block_name)
    if expected is None:
        return 0, 0

    problems = 0
    checked = 0

    text = (MOD / "Sim" / "Arsenal.cs").read_text()
    found = re.search(rf"{profile}\s*=\s*new\(\)\s*\{{(.*?)\n\s*\}};", text, re.S)
    if found is None:
        print(f"  MISSING Arsenal.{profile}", file=sys.stderr)
        return 1, 1
    block = found.group(1)

    checked += 1
    pivot = re.search(r"HeadPivot\s*=\s*new\(\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\)", block)
    want = expected["head_pivot"]
    if pivot is None:
        print(f"  MISSING Arsenal.{profile}.HeadPivot", file=sys.stderr)
        problems += 1
    else:
        got = [float(v) for v in pivot.groups()]
        if any(abs(a - b) > 5e-4 for a, b in zip(got, want)):
            print(f"  STALE Arsenal.{profile}.HeadPivot = {tuple(got)}, "
                  f"mesh says {tuple(want)}", file=sys.stderr)
            problems += 1

    checked += 1
    eye = re.search(r"EyeForward\s*=\s*([\d.]+)f\s*,", block)
    if eye is None:
        print(f"  MISSING Arsenal.{profile}.EyeForward", file=sys.stderr)
        problems += 1
    elif abs(float(eye.group(1)) - expected["eye_forward"]) > 5e-4:
        print(f"  STALE Arsenal.{profile}.EyeForward = {eye.group(1)}, "
              f"mesh says {expected['eye_forward']}", file=sys.stderr)
        problems += 1

    # And the third copy: where the asset XML actually puts the bodies. The subparts are named by
    # this profile's own markers, so a director carried on a launcher is checked against its own
    # pair rather than against the standalone part's.
    #
    # What is compared is head *minus base*, because HeadPivot is an offset from the base rather
    # than a point in the part -- so this is the one form that holds for a director bolted to a
    # hull and one riding a turret several metres out.
    # A roll-nod head's travel is a mechanical stop, and the importer measures the nose's aperture
    # beside it -- so this holds the C# to the number the tool decided was binding. Widening it
    # here alone drives the sight out through the shell.
    if "max_off_boresight_deg" in expected:
        checked += 1
        reach = re.search(r"MaxOffBoresightDeg\s*=\s*([\d.]+)f\s*,", block)
        if reach is None:
            print(f"  MISSING Arsenal.{profile}.MaxOffBoresightDeg", file=sys.stderr)
            problems += 1
        elif abs(float(reach.group(1)) - expected["max_off_boresight_deg"]) > 0.5:
            print(f"  STALE Arsenal.{profile}.MaxOffBoresightDeg = {reach.group(1)}, "
                  f"the model says {expected['max_off_boresight_deg']}", file=sys.stderr)
            problems += 1

    checked += 1
    wanted = ["BaseMarker", "HeadMarker"]
    if re.search(r'RollMarker\s*=\s*"([^"]+)"', block):
        wanted.append("RollMarker")

    ids = {marker: f"KSArmory_{re.search(rf'{marker}\s*=\s*"([^"]+)"', block).group(1)}"
           for marker in wanted
           if re.search(rf'{marker}\s*=\s*"([^"]+)"', block)}

    if len(ids) != len(wanted):
        print(f"  MISSING Arsenal.{profile}.BaseMarker or .HeadMarker", file=sys.stderr)
        return problems + 1, checked

    placed = {}
    for path in sorted(MOD.glob("KSArmory*.xml")):
        for part in ET.parse(path).getroot().findall("Part"):
            for sub in part.findall("SubPart"):
                for marker, ident in ids.items():
                    if sub.get("Id") != ident:
                        continue
                    position = sub.find("Transform/Position")
                    placed[marker] = ([float(position.get(axis, "0")) for axis in "XYZ"]
                                      if position is not None else [0.0, 0.0, 0.0])

    missing = [ids[m] for m in wanted if m not in placed]
    if missing:
        print(f"  MISSING <SubPart Id=\"{missing[0]}\"> in the asset XML", file=sys.stderr)
        problems += 1
    else:
        # Every moving body, against the same pivot. The roll gimbal shares it with the head --
        # that is what lets the ball sweep its travel without fouling the shroud -- so placing the
        # two apart in the XML would swing one of them around the other.
        for marker in [m for m in wanted if m != "BaseMarker"]:
            offset = [h - b for h, b in zip(placed[marker], placed["BaseMarker"])]
            if any(abs(a - b) > 5e-4 for a, b in zip(offset, want)):
                print(f"  STALE <SubPart Id=\"{ids[marker]}\"> sits {tuple(offset)} from its "
                      f"base, mesh says {tuple(want)}", file=sys.stderr)
                problems += 1

    if problems == 0:
        print("  optic geometry: the head's pivot matches in all three places")

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
    # Without the game installed, everything that depends only on the mod's own files can still
    # be checked -- and on Linux that includes case, which is the difference between a mod that
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

    print("checking every editor tag a part names is defined")
    p, c = check_editor_tags(None if offline else CORE)
    problems += p
    checked += c

    print("checking no asset id is declared twice")
    p, c = check_asset_id_collisions()
    problems += p
    checked += c

    print("checking every body marker names a subpart that exists")
    p, c = check_body_markers()
    problems += p
    checked += c

    print("checking src/KSArmory/Sim/Arsenal.cs against the mesh")
    p, c = check_launcher_geometry()
    problems += p
    checked += c

    print("checking every optical head's pivot against the mesh and the XML")
    for optic in registered((MOD / "Sim" / "Arsenal.cs").read_text(), "Optics"):
        p, c = check_optic_geometry(optic)
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
