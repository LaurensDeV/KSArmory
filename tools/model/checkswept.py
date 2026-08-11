#!/usr/bin/env python3
"""
Checks the assembled vehicle for the geometry defects no other tool can see.

Four questions, none of which the mesh, the pivots or a preview render can answer:

  1. does every piece reach the chassis, or has something come adrift?
  2. does each articulated body actually touch what it hangs off?
  3. does any cover stand proud of the thing it covers?
  4. does any assembly pass through another anywhere in its travel?

checkmesh.py answers "does this body render cleanly"; validate-parts.py answers "do the three
copies of each pivot agree". Neither can answer "do the pods sweep through the gun sponsons",
because that is a question about two bodies at a pose neither file describes. Renders cannot
answer it either: they show the poses someone thought to ask for.

The atlas is a pose-invariant library — every body sits in its own pivot-local frame at its
modelled reference pose — so any pose is reconstructible from the atlas plus muzzles.json, with
no Blender and no game. Bodies are split into their convex primitives by connected component,
which is exactly how pantsir.py builds them, and tested with SAT: the result is the metres one
body would have to move to leave the other.

    ./tools/model/checkswept.py
    ./tools/model/checkswept.py --atlas <path> --step 2

Exits non-zero if anything is adrift, standing proud, or passing through something else.
"""

import argparse
import json
import math
import re
import sys
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from checkmesh import read_accessor, read_glb  # noqa: E402

REPO = Path(__file__).resolve().parent.parent.parent
MOD = REPO / "src" / "KSArmory"

# Interpenetration below this is a modelled joint, not a defect: box() inflates every primitive
# by a skin plus jitter, and assemblies are deliberately seated into their mounts.
SEATED = 0.06

# Pairs that are structurally allowed to interpenetrate, and by how much. Each of these is a
# joint rather than a defect: a trunnion inside its own bearing, or a ring seated on a deck.
#
# Pods/Turret is the weakest of the three and the one to design out. The pods' inner tube column
# occupies the same Z band as the turret cheeks, so it runs through them near the trunnion at
# every elevation — a full tube diameter. It is hidden rather than visibly clipping, because the
# column is narrower than the cheek it is inside, but the allowance is wide enough to mask a real
# defect between those two bodies. See docs/MODULARITY.md.
#
# Nothing here is for the director, deliberately. Splitting a body out of an assembly is what makes
# a clash visible at all -- a body cannot intersect itself, so the Pantsir's blanked-off optic stub
# sat inside the tracking array's housing for as long as it was part of the turret's own mesh and
# no check could see it. The moment it became a body in its own right the pair was measurable, at
# 7.0 cm. That was fixed by moving the mount, not by granting it an allowance: an entry here says
# "this is a joint", and two things that merely overlap are not a joint. Adding a body to an
# existing assembly is the case to re-run this for.
ALLOWED = {# The trunnion runs into its bearing, which is the whole point of a trunnion. It is
           # also the *only* contact the CIWS's two moving groups can ever have: everything else
           # in the elevating head is narrower than the gap between the cheeks, and elevation
           # turns about +Z, so a gap in Z cannot be closed by any pose.
           ("KSArmory_Subpart_CiwsGuns", "KSArmory_Subpart_CiwsTurret"): 0.06,
           ("KSArmory_Subpart_Guns", "KSArmory_Subpart_Turret"): 0.30,
           ("KSArmory_Subpart_Pods", "KSArmory_Subpart_Turret"): 0.22,
           ("KSArmory_Subpart_Chassis", "KSArmory_Subpart_Turret"): 0.10}


def load_bodies(atlas):
    """Maps mesh name -> list of convex primitives, each (verts, tris)."""
    gltf, binary = read_glb(str(atlas))
    out = {}
    for mesh in gltf["meshes"]:
        name = mesh.get("name", "?")
        if name.endswith("_VM"):
            continue
        verts, tris = [], []
        for prim in mesh["primitives"]:
            if prim.get("mode", 4) != 4:
                continue
            base = len(verts)
            verts.extend(read_accessor(gltf, binary, prim["attributes"]["POSITION"]))
            idx = list(read_accessor(gltf, binary, prim["indices"]))
            tris.extend((base + idx[i], base + idx[i + 1], base + idx[i + 2])
                        for i in range(0, len(idx) - 2, 3))
        out[name] = [Prim(v, t, name) for v, t in components(*weld(verts, tris))]
    return out


def weld(verts, tris, eps=1e-5):
    """Merges coincident vertices so connected components come out one per primitive."""
    key, remap, out = {}, [], []
    for v in verts:
        k = (round(v[0] / eps), round(v[1] / eps), round(v[2] / eps))
        if k not in key:
            key[k] = len(out)
            out.append(v)
        remap.append(key[k])
    return out, [(remap[a], remap[b], remap[c]) for a, b, c in tris]


def components(verts, tris):
    parent = list(range(len(verts)))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    for a, b, c in tris:
        for u, v in ((a, b), (b, c)):
            ru, rv = find(u), find(v)
            if ru != rv:
                parent[ru] = rv

    groups = defaultdict(list)
    for t in tris:
        groups[find(t[0])].append(t)

    comps = []
    for ts in groups.values():
        vids = sorted({v for t in ts for v in t})
        index = {v: i for i, v in enumerate(vids)}
        comps.append(([verts[v] for v in vids],
                      [(index[a], index[b], index[c]) for a, b, c in ts]))
    return comps


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def norm(a):
    length = math.sqrt(dot(a, a))
    return (a[0] / length, a[1] / length, a[2] / length) if length > 1e-12 else None


def canon(d):
    """Sign-normalised direction, so an axis and its negation dedupe to one."""
    for c in d:
        if abs(c) > 1e-9:
            s = -1.0 if c < 0 else 1.0
            return (d[0] * s, d[1] * s, d[2] * s)
    return None


class Prim:
    """One convex primitive: its vertices, unique face normals and unique edge directions."""

    def __init__(self, verts, tris, owner=""):
        self.owner = owner
        self.verts = verts
        self.tris = tris
        normals, edges = {}, {}
        for a, b, c in tris:
            n = norm(cross(sub(verts[b], verts[a]), sub(verts[c], verts[a])))
            if n and (n := canon(n)):
                normals[tuple(round(x, 4) for x in n)] = n
            for i, j in ((a, b), (b, c), (c, a)):
                e = norm(sub(verts[j], verts[i]))
                if e and (e := canon(e)):
                    edges[tuple(round(x, 4) for x in e)] = e
        self.normals = list(normals.values())
        self.edges = list(edges.values())
        self._bounds()

    def _bounds(self):
        self.lo = [min(v[i] for v in self.verts) for i in range(3)]
        self.hi = [max(v[i] for v in self.verts) for i in range(3)]

    def placed(self, fn):
        p = Prim.__new__(Prim)
        p.owner = self.owner
        p.verts = [fn(v) for v in self.verts]
        p.tris = self.tris
        p.normals = [fn(n, True) for n in self.normals]
        p.edges = [fn(e, True) for e in self.edges]
        p._bounds()
        return p


def aabb_apart(a, b):
    return any(a.lo[i] > b.hi[i] or b.lo[i] > a.hi[i] for i in range(3))


def sat(a, b):
    """Minimum translation distance for two convex polyhedra, or 0.0 if they are disjoint."""
    best = float("inf")
    axes = list(a.normals) + list(b.normals)
    seen = set()
    for ea in a.edges:
        for eb in b.edges:
            x = norm(cross(ea, eb))
            if x is None or (x := canon(x)) is None:
                continue
            k = tuple(round(c, 3) for c in x)
            if k not in seen:
                seen.add(k)
                axes.append(x)

    for ax in axes:
        alo = ahi = dot(a.verts[0], ax)
        for v in a.verts[1:]:
            d = dot(v, ax)
            alo, ahi = min(alo, d), max(ahi, d)
        blo = bhi = dot(b.verts[0], ax)
        for v in b.verts[1:]:
            d = dot(v, ax)
            blo, bhi = min(blo, d), max(bhi, d)
        overlap = min(ahi, bhi) - max(alo, blo)
        if overlap <= 0.0:
            return 0.0
        best = min(best, overlap)
    return best


def placement(pivot_from_turret, reference_rad, elevation_rad, bearing_rad, turret_pivot):
    """Mirrors TubeGeometry.ElevatingPose: pitch about +Z, then ride the traverse about +X."""
    ce, se = math.cos(reference_rad - elevation_rad), math.sin(reference_rad - elevation_rad)
    cb, sb = math.cos(bearing_rad), math.sin(bearing_rad)

    def fn(v, rotate_only=False):
        x, y = ce * v[0] - se * v[1], se * v[0] + ce * v[1]
        z = v[2]
        if not rotate_only:
            x, y, z = x + pivot_from_turret[0], y + pivot_from_turret[1], z + pivot_from_turret[2]
        y, z = cb * y - sb * z, sb * y + cb * z
        if not rotate_only:
            x, y, z = x + turret_pivot[0], y + turret_pivot[1], z + turret_pivot[2]
        return (x, y, z)

    return fn


def read_travel(profile="PantsirS1"):
    """Elevation travel and the forward depression floor, from the C# rather than a fourth copy.

    Scoped to one profile's initialiser. Swept over the whole file it takes whichever launcher
    appears first, so adding a CIWS that depresses to -25 sent the Pantsir -- which cannot go
    below level -- through poses its drives will never command, and reported the collisions.
    """
    defaults = (MOD / "Sim" / "LauncherProfile.cs").read_text()
    arsenal = (MOD / "Sim" / "Arsenal.cs").read_text()

    block = re.search(rf"{profile}\s*=\s*new\(\)\s*\{{(.*?)\n\s*\}};", arsenal, re.S)
    overrides = block.group(1) if block else ""

    def value(field, fallback):
        match = re.search(rf"{field}\s*=\s*(-?[\d.]+)f\s*[,;]", overrides)
        if match:
            return float(match.group(1))
        match = re.search(rf"public float {field}\s*=\s*(-?[\d.]+)f\s*;", defaults)
        return float(match.group(1)) if match else fallback

    return {name: value(name, fallback) for name, fallback in
            (("MinElevationDeg", 0.0), ("MaxElevationDeg", 82.0),
             ("ForwardMinElevationDeg", 15.0), ("ForwardArcDeg", 80.0),
             ("ForwardPlateauDeg", 62.0))}


def depression_floor(bearing_deg, travel):
    """Mirrors Turret.DepressionFloorAt."""
    off_axis = abs((bearing_deg + 180.0) % 360.0 - 180.0)
    arc, plateau = travel["ForwardArcDeg"], travel["ForwardPlateauDeg"]
    if off_axis >= arc or arc <= 0.0:
        return travel["MinElevationDeg"]
    if off_axis <= plateau:
        return max(travel["MinElevationDeg"], travel["ForwardMinElevationDeg"])
    t = (off_axis - plateau) / (arc - plateau)
    return max(travel["MinElevationDeg"], travel["ForwardMinElevationDeg"] * (1.0 - t * t))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--atlas", default=str(MOD / "Meshes" / "KSArmory_MeshAtlas.glb"))
    ap.add_argument("--muzzles", default=str(Path(__file__).resolve().parent / "muzzles.json"))
    ap.add_argument("--step", type=float, default=2.0, help="elevation step, degrees")
    ap.add_argument("--bearing-step", type=float, default=5.0)
    ap.add_argument("--ignore-floor", action="store_true",
                    help="sweep poses the depression interlock forbids, to check it is needed")
    args = ap.parse_args()

    muzzles = json.loads(Path(args.muzzles).read_text())
    bodies = load_bodies(Path(args.atlas))

    print("no cover stands proud of what it covers")
    problems = check_no_coaxial_lips(bodies)
    print()

    swept = vehicles(muzzles)
    problems += check_every_articulated_launcher_is_swept(swept)

    for v in swept:
        problems += sweep(bodies, v, args)

    if problems:
        print(f"FOUND {problems} problem(s): assemblies detached at rest, or passing "
              f"through each other in travel")
        return 1
    print("clear: everything is attached, and nothing passes through anything in its travel")
    return 0


def check_every_articulated_launcher_is_swept(swept):
    """Fails if a launcher that trains has no entry in vehicles().

    vehicles() is written by hand, because the body names, pivots and parent chain are not
    derivable from the profile. What *is* derivable is which launchers need an entry: any that
    declares a TurretMarker moves, and a body set nobody named is silently not swept while this
    tool still prints "clear". The registry is the authority on which those are.
    """
    arsenal = (MOD / "Sim" / "Arsenal.cs").read_text()

    registered = re.search(r"Launchers\s*=\s*\[(.*?)\];", arsenal, re.S)
    if registered is None:
        print("  cannot read Arsenal.Launchers -- coverage unchecked")
        return 1

    covered = {v["profile"] for v in swept}
    problems = 0

    for profile in (name.strip() for name in registered.group(1).split(",") if name.strip()):
        block = re.search(rf"{profile}\s*=\s*new\(\)\s*\{{(.*?)\n\s*\}};", arsenal, re.S)
        if block is None or "TurretMarker" not in block.group(1):
            continue                    # does not train, so it has nothing to sweep
        if profile not in covered:
            print(f"  UNSWEPT {profile}: it trains and has no entry in vehicles()")
            problems += 1

    if problems == 0:
        print(f"every launcher that trains is swept ({len(covered)} vehicle(s))")
    print()
    return problems


def vehicles(muzzles):
    """Every articulated vehicle in the atlas, each with its own body set and its own travel.

    One entry per launcher that moves. Sweeping only the first is how a whole vehicle goes
    unchecked while the tool still reports clean: the CIWS had a traverse, an elevating head and
    no coverage at all, because this read the Pantsir's body names and stopped.
    """
    ciws = muzzles["ciws"]
    return [
        {
            "name": "Pantsir S1",
            "profile": "PantsirS1",
            "chassis": "KSArmory_Subpart_Chassis",
            "turret_pivot": muzzles["turret_pivot"],
            "riding": {
                "KSArmory_Subpart_Turret": ((0.0, 0.0, 0.0), 0.0),
                "KSArmory_Subpart_Pods": (muzzles["pod_pivot_from_turret"],
                                          math.radians(muzzles["pod_reference_elevation_deg"])),
                "KSArmory_Subpart_Guns": (muzzles["gun_pivot_from_turret"],
                                          math.radians(muzzles["gun_reference_elevation_deg"])),
                # The array's spin is about the traverse axis, so it cannot change any clearance
                # the bearing does not already cover.
                "KSArmory_Subpart_Radar": (muzzles["radar_pivot_from_turret"], 0.0),
                # The director. Its base rides the traverse and nothing else, so it is swept
                # exactly like the array's turntable.
                #
                # Its head is swept at REST only, and that understates it: the ball points freely
                # rather than on a named axis, so its lens and hood sweep a shell this tool has no
                # way to describe. The ball itself is the bulk of the volume and is very nearly
                # spherical about the pivot, so what is checked here is the clearance that
                # matters -- the pods and cannon coming past it. A lens fouling something at one
                # aim would not be caught, which is what MIN_ELEVATION_DEG exists to bound.
                "KSArmory_Subpart_OpticBase": (muzzles["optic_base_from_turret"], 0.0),
                "KSArmory_Subpart_OpticHead": (
                    [a + b for a, b in zip(muzzles["optic_base_from_turret"],
                                           muzzles["optic"]["head_pivot"])], 0.0),
            },
            "elevating": {"KSArmory_Subpart_Pods", "KSArmory_Subpart_Guns"},
            "parents": {"KSArmory_Subpart_Pods": "KSArmory_Subpart_Turret",
                        "KSArmory_Subpart_Guns": "KSArmory_Subpart_Turret",
                        "KSArmory_Subpart_Radar": "KSArmory_Subpart_Turret",
                        "KSArmory_Subpart_OpticBase": "KSArmory_Subpart_Turret",
                        "KSArmory_Subpart_OpticHead": "KSArmory_Subpart_OpticBase",
                        "KSArmory_Subpart_Turret": "KSArmory_Subpart_Chassis"},
        },
        {
            "name": "Mk 15 Phalanx",
            "profile": "Ciws",
            "chassis": "KSArmory_Subpart_CiwsBase",
            "turret_pivot": ciws["turret_pivot"],
            "riding": {
                "KSArmory_Subpart_CiwsTurret": ((0.0, 0.0, 0.0), 0.0),
                "KSArmory_Subpart_CiwsGuns": (ciws["gun_pivot_from_turret"],
                                              math.radians(ciws["gun_reference_elevation_deg"])),
            },
            "elevating": {"KSArmory_Subpart_CiwsGuns"},
            "parents": {"KSArmory_Subpart_CiwsGuns": "KSArmory_Subpart_CiwsTurret",
                        "KSArmory_Subpart_CiwsTurret": "KSArmory_Subpart_CiwsBase"},
        },
    ]


def sweep(bodies, v, args):
    """Attachment and travel for one vehicle."""
    travel = read_travel(v["profile"])

    names = [n for n in sorted(bodies) if n in v["riding"] or n == v["chassis"]]
    if len(names) < 2:
        print(f"== {v['name']}: not in the atlas, nothing swept\n")
        return 0

    steps = int((travel["MaxElevationDeg"] - travel["MinElevationDeg"]) / args.step) + 1
    elevations = [travel["MinElevationDeg"] + i * args.step for i in range(steps)]
    bearings = [i * args.bearing_step for i in range(int(360 / args.bearing_step))]

    print(f"== {v['name']}")
    print("the assembled vehicle is one connected piece")
    problems = check_vehicle_is_connected(bodies, v)
    print()

    print("attachment at the modelled rest pose")
    problems += check_attachment(bodies, v)
    print()

    worst = {}
    cache = {}
    for i, a in enumerate(names):
        for b in names[i + 1:]:
            # Both riding the turret: the traverse is one rigid motion applied to the pair, so it
            # cancels and only elevation is free. Against the chassis it does not.
            pair_bearings = [0.0] if (a in v["riding"] and b in v["riding"]) else bearings

            for bearing in pair_bearings:
                floor = -1e9 if args.ignore_floor else depression_floor(bearing, travel)
                for elev in elevations:
                    if elev < floor - 1e-9:
                        continue
                    depth = pair_depth(bodies, v, a, b, elev, bearing, cache)
                    if depth > worst.get((a, b), (0.0,))[0]:
                        worst[(a, b)] = (depth, elev, bearing)

    allowed = {tuple(sorted(k)): metres for k, metres in ALLOWED.items()}
    print(f"{len(names)} bodies, elevation {travel['MinElevationDeg']:.0f}"
          f"-{travel['MaxElevationDeg']:.0f}° step {args.step:g}°, bearing step {args.bearing_step:g}°")
    print()
    for pair in sorted(worst, key=lambda p: -worst[p][0]):
        depth, elev, bearing = worst[pair]
        limit = allowed.get(tuple(sorted(pair)), SEATED)
        short = tuple(n.replace("KSArmory_Subpart_", "") for n in pair)
        if depth <= limit:
            print(f"  ok         {short[0]:<12} / {short[1]:<12} "
                  f"max interpenetration {depth * 100:5.1f} cm (allowed {limit * 100:.0f})")
            continue
        print(f"  PASSES THROUGH {short[0]:<12} / {short[1]:<12} "
              f"{depth * 100:5.1f} cm at elevation {elev:g}°, bearing {bearing:g}°")
        print(f"      see it: ./tools/model/build.sh --pose elev={elev:g},bearing={bearing:g}")
        problems += 1

    print()
    return problems


def point_triangle_distance(p, a, b, c):
    """Distance from a point to a triangle, clamped to the triangle rather than its plane."""
    ab, ac, ap = sub(b, a), sub(c, a), sub(p, a)
    d1, d2 = dot(ab, ap), dot(ac, ap)
    if d1 <= 0.0 and d2 <= 0.0:
        return math.sqrt(dot(ap, ap))

    bp = sub(p, b)
    d3, d4 = dot(ab, bp), dot(ac, bp)
    if d3 >= 0.0 and d4 <= d3:
        return math.sqrt(dot(bp, bp))

    cp = sub(p, c)
    d5, d6 = dot(ab, cp), dot(ac, cp)
    if d6 >= 0.0 and d5 <= d6:
        return math.sqrt(dot(cp, cp))

    # Inside an edge region or the face itself: project and measure from the projection.
    vc = d1 * d4 - d3 * d2
    if vc <= 0.0 and d1 >= 0.0 and d3 <= 0.0:
        t = d1 / (d1 - d3)
        q = (a[0] + ab[0] * t, a[1] + ab[1] * t, a[2] + ab[2] * t)
        return math.sqrt(dot(sub(p, q), sub(p, q)))

    vb = d5 * d2 - d1 * d6
    if vb <= 0.0 and d2 >= 0.0 and d6 <= 0.0:
        t = d2 / (d2 - d6)
        q = (a[0] + ac[0] * t, a[1] + ac[1] * t, a[2] + ac[2] * t)
        return math.sqrt(dot(sub(p, q), sub(p, q)))

    va = d3 * d6 - d5 * d4
    if va <= 0.0 and (d4 - d3) >= 0.0 and (d5 - d6) >= 0.0:
        t = (d4 - d3) / ((d4 - d3) + (d5 - d6))
        bc = sub(c, b)
        q = (b[0] + bc[0] * t, b[1] + bc[1] * t, b[2] + bc[2] * t)
        return math.sqrt(dot(sub(p, q), sub(p, q)))

    denom = 1.0 / (va + vb + vc)
    v, w = vb * denom, vc * denom
    q = (a[0] + ab[0] * v + ac[0] * w, a[1] + ab[1] * v + ac[1] * w,
         a[2] + ab[2] * v + ac[2] * w)
    return math.sqrt(dot(sub(p, q), sub(p, q)))


def body_gap(a_prims, b_prims):
    """Smallest distance between two bodies known to be disjoint."""
    def faces(prims):
        return [(p.verts[i], p.verts[j], p.verts[k]) for p in prims for i, j, k in p.tris]

    best = float("inf")
    for prims, tris in ((a_prims, faces(b_prims)), (b_prims, faces(a_prims))):
        for prim in prims:
            for vertex in prim.verts:
                for tri in tris:
                    best = min(best, point_triangle_distance(vertex, *tri))
    return best


# A coaxial cover wider than the thing it covers reads as a manufacturing lip. Above this ratio
# it reads as a deliberate flange instead — a muzzle brake is 1.9x its barrel and looks right.
LIP_RATIO = 1.25

# ...and only for a *cover*: something short sitting on the end of something long. Radius alone
# cannot tell a mistake from a design, because a booster stage is legitimately fatter than the
# sustainer it pushes and a hexagonal array than its turntable. Being stubby is what makes a
# primitive a cap rather than a section of the body.
LIP_MAX_EXTENT = 0.2

# Bands that are meant to stand proud, by body and cap radius in millimetres. A real interstage
# collar wraps the joint and stands slightly outside the booster — geometrically the same thing
# this check exists to catch, so it has to be named rather than inferred.
DELIBERATE_BANDS = {("KSArmory_Subpart_Missile", 89)}


def revolution(prim):
    """(axis, centre, radius, extent) if this primitive is a cylinder or cone, else None."""
    for axis in prim.normals:
        heights = sorted({round(dot(v, axis), 5) for v in prim.verts})
        if len(heights) != 2:
            continue

        ends = []
        for h in heights:
            ring = [v for v in prim.verts if abs(dot(v, axis) - h) < 1e-4]
            if len(ring) < 5:
                break
            centre = tuple(sum(v[k] for v in ring) / len(ring) for k in range(3))
            radii = [math.sqrt(max(dot(sub(v, centre), sub(v, centre))
                                   - dot(sub(v, centre), axis) ** 2, 0.0)) for v in ring]
            mean = sum(radii) / len(radii)
            if mean < 1e-6 or max(abs(r - mean) for r in radii) > 0.02 * mean:
                break            # not a circle: a box cap, or a squashed profile
            ends.append((centre, mean, h))
        if len(ends) != 2:
            continue

        # The two rings must sit on one line, or this is not a surface of revolution.
        offset = sub(ends[1][0], ends[0][0])
        along = dot(offset, axis)
        if dot(offset, offset) - along * along > 1e-6:
            continue

        # The midpoint, not either ring: callers compare centre separation against half the
        # combined extent, and a ring centre makes a cap at the far end look far away.
        midpoint = tuple((ends[0][0][k] + ends[1][0][k]) / 2.0 for k in range(3))
        return axis, midpoint, max(ends[0][1], ends[1][1]), abs(heights[1] - heights[0])
    return None


def check_no_coaxial_lips(bodies):
    """A cover sitting on the end of a tube must not be wider than the tube.

    Nothing else can see this. The mesh is clean, the two are meant to overlap, and it is far too
    small to show in a preview render — but in game it catches the light as a rim standing proud
    all the way round, and a cover with fewer facets than its tube makes that rim visibly
    polygonal.
    """
    problems = 0
    for name in sorted(bodies):
        shapes = [(p, revolution(p)) for p in bodies[name]]
        shapes = [(p, r) for p, r in shapes if r is not None]

        seen = set()
        for i, (pa, (axis_a, centre_a, radius_a, extent_a)) in enumerate(shapes):
            for pb, (axis_b, centre_b, radius_b, extent_b) in shapes[i + 1:]:
                if abs(dot(axis_a, axis_b)) < 0.999:
                    continue
                offset = sub(centre_b, centre_a)
                along = dot(offset, axis_a)
                if dot(offset, offset) - along * along > 1e-4:
                    continue          # parallel but not coaxial
                if abs(along) > (extent_a + extent_b) / 2.0 + 1e-3:
                    continue          # coaxial but nowhere near each other

                if extent_a >= extent_b:
                    host, cap, host_len, cap_len = radius_a, radius_b, extent_a, extent_b
                else:
                    host, cap, host_len, cap_len = radius_b, radius_a, extent_b, extent_a
                if host <= 0.0 or host_len <= 0.0:
                    continue
                if cap_len > LIP_MAX_EXTENT * host_len:
                    continue
                if not 1.0 + 1e-4 < cap / host <= LIP_RATIO:
                    continue
                if (name, round(cap * 1000)) in DELIBERATE_BANDS:
                    continue

                key = (name, round(cap, 4), round(host, 4))
                if key in seen:
                    continue
                seen.add(key)
                short = name.replace("KSArmory_Subpart_", "")
                print(f"  LIP        {short:<8} a coaxial cover of radius {cap * 1000:.1f} mm "
                      f"stands {(cap - host) * 1000:.1f} mm proud of the {host * 1000:.1f} mm "
                      f"body it caps")
                problems += 1
    if not problems:
        print("  ok         no coaxial cover stands proud of what it covers")
    return problems


def check_vehicle_is_connected(bodies, v):
    """Every primitive of the assembled vehicle must be reachable from the chassis by overlap.

    Nothing else sees a piece come adrift. The mesh is clean, the pivots agree, and no pair of
    bodies passes through another — the piece simply stops touching what carried it and hangs in
    the air. Per-body connectivity is the wrong test: the cannon are legitimately two islands
    that never touch each other, and the fins are twelve. What matters is that each island
    reaches the chassis *through* something, which is exactly what a player reads as attached.

    Only the assembled vehicle counts. Stowed rounds sit far off the origin until fired.
    """
    parts = (v["chassis"],) + tuple(sorted(v["riding"]))

    cache = {}
    prims, owners = [], []
    for name in parts:
        if name not in bodies:
            continue
        rest = math.degrees(v["riding"][name][1]) if name in v["riding"] else 0.0
        placed = (bodies[name] if name == v["chassis"]
                  else placed_body(bodies, v, name, rest, 0.0, cache)[0])
        prims.extend(placed)
        owners.extend([name] * len(placed))

    parent = list(range(len(prims)))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    for i in range(len(prims)):
        for j in range(i + 1, len(prims)):
            if aabb_apart(prims[i], prims[j]) or sat(prims[i], prims[j]) <= 0.0:
                continue
            ri, rj = find(i), find(j)
            if ri != rj:
                parent[ri] = rj

    islands = defaultdict(list)
    for i in range(len(prims)):
        islands[find(i)].append(i)

    if len(islands) == 1:
        print(f"  ok         all {len(prims)} primitives reach the chassis")
        return 0

    order = sorted(islands.values(), key=len, reverse=True)
    print(f"  FLOATING   the vehicle is in {len(islands)} pieces; "
          f"{sum(len(g) for g in order[1:])} primitive(s) do not reach the chassis")
    for group in order[1:]:
        lo = [min(prims[i].lo[k] for i in group) for k in range(3)]
        hi = [max(prims[i].hi[k] for i in group) for k in range(3)]
        who = ", ".join(sorted({owners[i].replace("KSArmory_Subpart_", "") for i in group}))
        print(f"      {len(group):>3} primitive(s) of {who} at "
              f"X[{lo[0]:.2f},{hi[0]:.2f}] Y[{lo[1]:.2f},{hi[1]:.2f}] Z[{lo[2]:.2f},{hi[2]:.2f}]")
    return 1


def check_attachment(bodies, v):
    """Every articulated body must actually touch what it hangs off, at the modelled rest pose.

    A body that floats free is not a rendering fault and no other check sees it: the mesh is
    clean, the pivots agree, and nothing passes through anything. It simply reads as detached,
    which is the one defect a player notices immediately and a tool never does.
    """
    problems = 0
    for child, parent in sorted(v["parents"].items()):
        if child not in bodies or parent not in bodies:
            continue

        cache = {}
        # The rest pose is elevation == reference, which ElevatingPose makes the identity.
        kid = placed_body(bodies, v, child, math.degrees(v["riding"][child][1]), 0.0, cache)[0]
        mum = (bodies[parent] if parent == v["chassis"]
               else placed_body(bodies, v, parent, 0.0, 0.0, cache)[0])

        touching = any(not aabb_apart(pa, pb) and sat(pa, pb) > 0.0
                       for pa in kid for pb in mum)
        short = child.replace("KSArmory_Subpart_", ""), parent.replace("KSArmory_Subpart_", "")
        if touching:
            print(f"  ok         {short[0]:<8} is attached to {short[1]}")
            continue

        gap = body_gap(kid, mum)
        print(f"  DETACHED   {short[0]:<8} floats {gap * 100:.1f} cm clear of {short[1]}")
        problems += 1
    return problems


def placed_body(bodies, v, name, elev, bearing, cache):
    """Places one body at one pose, memoised — each body appears in several pairs."""
    key = (name, elev, bearing)
    if key in cache:
        return cache[key]

    if name == v["chassis"]:
        prims = bodies[name]
    else:
        pivot, reference = v["riding"][name]
        pitch = math.radians(elev) if name in v["elevating"] else 0.0
        fn = placement(pivot, reference, pitch, math.radians(bearing), v["turret_pivot"])
        prims = [p.placed(fn) for p in bodies[name]]

    lo = [min(p.lo[i] for p in prims) for i in range(3)]
    hi = [max(p.hi[i] for p in prims) for i in range(3)]
    cache[key] = (prims, lo, hi)
    return cache[key]


def pair_depth(bodies, v, a, b, elev, bearing, cache):
    """Deepest interpenetration between two bodies at one pose."""
    pa_all, alo, ahi = placed_body(bodies, v, a, elev, bearing, cache)
    pb_all, blo, bhi = placed_body(bodies, v, b, elev, bearing, cache)

    # Whole-body bounds first. Most poses separate two bodies entirely, and skipping those
    # avoids every primitive pair between them.
    if any(alo[i] > bhi[i] or blo[i] > ahi[i] for i in range(3)):
        return 0.0

    deepest = 0.0
    for pa in pa_all:
        for pb in pb_all:
            if aabb_apart(pa, pb):
                continue
            deepest = max(deepest, sat(pa, pb))
    return deepest


if __name__ == "__main__":
    sys.exit(main())
