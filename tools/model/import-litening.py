#!/usr/bin/env python3
"""
Brings a hand-authored LITENING pod into the mod's atlas format.

Unlike every other part here the pod is **authored, not generated**: it was modelled in the
Blender UI and exported as a `.glb` with its own unwrap and baked maps, so there is no script that
is the model. What this does instead is reframe that export into what KSA reads, which is four
separate things the exporter has no way to know about.

    ./tools/model/import-litening.py                     # from the default source
    ./tools/model/import-litening.py --source <dir>      # from a re-export

Needs neither Blender nor the game: glTF is JSON plus one binary buffer, and the attributes are
plain float32 vectors, so the transform is exact and the tool is a few hundred lines.

## What has to change, and why the exporter cannot do it

1.  **Node transforms have to be baked.** KSA reads meshes out of an atlas and places bodies from
    the part XML; across all 44 Core atlases, the only nodes carrying a transform are `_ColPrim_*`
    collider helpers and floating-point dust. The authored file puts the roll body under a 90°
    node rotation and the nod body under a two-level translation chain, so taken raw the shroud
    arrives on its side.

2.  **The frame is different.** The pod is modelled nose along +X with its lugs on +Y; part space
    is +X out of the mounting face, +Y along the host, +Z its right. That is one rotation, and it
    has to be *baked* rather than parked in the XML — the mod rewrites the `<Transform>` of every
    moving subpart each frame, so a rest rotation there is overwritten on the first update.

3.  **A moving body's mesh has to be recentred on its pivot**, because KSA turns a subpart about
    its own mesh origin. Both moving bodies share one pivot here — the ball's centre, which the
    roll axis passes through — which is what `OpticProfile.HeadPivot` is and why the pod needs one
    number rather than two.

4.  **The roll body has to be clocked.** Its aperture is a recession cut into one side of the
    shroud, and the nod only ever tilts one way — `OpticGeometry` always solves a non-negative nod
    and lets the roll choose the direction. So the recession has to face the direction the nod
    tilts, or the sight looks out through the closed side and the travel is the 107° of the
    plain shell rather than the 158° of the recession.

Then it emits a `_VM` twin of each body, because that is what the editor previews and Core ships
one for every subpart.

## What it measures

The travel, by casting rays from the ball's pivot until the shroud or the aft body stops them.
That is the *aperture*; the gimbal's own stop is a separate and smaller number, and the point of
measuring is to know which one binds. Printed for `Sim/Arsenal.cs` to be pasted from, and recorded
in `muzzles.json` so `tools/validate-parts.py` can hold the two together.
"""

import argparse
import json
import math
import os
import shutil
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from checkmesh import read_accessor, read_glb  # noqa: E402

REPO = Path(__file__).resolve().parent.parent.parent
MOD = REPO / "src" / "KSArmory"

DEFAULT_SOURCE = Path("/mnt/c/Users/devoo/Documents/LiteningPod")

ATLAS = MOD / "Meshes" / "KSArmory_Litening.glb"

# Source body -> the Id the asset XML instances. "Window" rather than "Nod" because the marker it
# becomes is the *head*, which is what every consumer of an optical head calls it.
BODIES = {
    "LiteningPod_Fixed": "KSArmory_Subpart_LiteningBody",
    "LiteningPod_Roll": "KSArmory_Subpart_LiteningRoll",
    "LiteningPod_Nod": "KSArmory_Subpart_LiteningWindow",
}

# Which of them ride the gimbal, and so are recentred on the pivot and clocked.
MOVING = ("LiteningPod_Roll", "LiteningPod_Nod")

TEXTURES = {
    "litening_fixed_diffuse.png": "KSArmory_Litening_Body_Diffuse.png",
    "litening_fixed_normal.png": "KSArmory_Litening_Body_Normal.png",
    "litening_fixed_aoroughmetal.png": "KSArmory_Litening_Body_PBR.png",
    "litening_roll_diffuse.png": "KSArmory_Litening_Roll_Diffuse.png",
    "litening_roll_normal.png": "KSArmory_Litening_Roll_Normal.png",
    "litening_roll_aoroughmetal.png": "KSArmory_Litening_Roll_PBR.png",
    "litening_nod_diffuse.png": "KSArmory_Litening_Window_Diffuse.png",
    "litening_nod_normal.png": "KSArmory_Litening_Window_Normal.png",
    "litening_nod_aoroughmetal.png": "KSArmory_Litening_Window_PBR.png",
}

# The ball's centre in the authored file's own frame, which is the bearing both moving bodies turn
# on. Read off the node chain rather than typed: GMB_Roll + GMB_Nod.
# Recomputed in main(); this is only the fallback if the rig is ever flattened.
BALL_CENTRE_POD = 0.9428217649459839

# How far round the pod's axis the moving bodies are turned before the frame change, so the
# shroud's recession ends up facing the way the nod tilts. Half a turn: the recession is modelled
# on the same side as the lugs, and the nod tilts away from the mounting face.
CLOCK_DEG = 180.0

# The gimbal's own stop (deg off the pod's centreline). Their rig's limit, and the AselPOD-class
# figure it was taken from -- Rafael does not publish Litening's. Kept as a constant rather than
# measured because it is a property of the mechanism rather than of the shell; what IS measured is
# the aperture, so the two can be compared and the binding one reported.
MECHANICAL_STOP_DEG = 150.0

KEYHOLE_DEG = 4.0


# ---------------------------------------------------------------------------
# Reading
# ---------------------------------------------------------------------------

def quat_matrix(q):
    x, y, z, w = q
    return ((1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)),
            (2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)),
            (2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)))


def rotate(m, v):
    return tuple(sum(m[r][c] * v[c] for c in range(3)) for r in range(3))


def load(path):
    """Every mesh in the source, with its node chain baked into the vertices."""
    gltf, binary = read_glb(str(path))
    nodes, meshes = gltf["nodes"], gltf["meshes"]

    parent = {}
    for i, node in enumerate(nodes):
        for child in node.get("children") or []:
            parent[child] = i

    def chain(index):
        out = []
        while True:
            out.append(nodes[index])
            if index not in parent:
                return out
            index = parent[index]

    def place(index, v, directions_only=False):
        for node in chain(index):
            if node.get("scale"):
                raise SystemExit(f"node {node.get('name')!r} carries a scale; not handled")
            if node.get("rotation"):
                v = rotate(quat_matrix(node["rotation"]), v)
            if not directions_only:
                t = node.get("translation") or (0.0, 0.0, 0.0)
                v = (v[0] + t[0], v[1] + t[1], v[2] + t[2])
        return v

    bodies, empties = {}, {}
    for i, node in enumerate(nodes):
        if node.get("mesh") is None:
            empties[node.get("name")] = place(i, (0.0, 0.0, 0.0))
            continue

        prim = meshes[node["mesh"]]["primitives"][0]
        attrs = prim["attributes"]

        bodies[meshes[node["mesh"]]["name"]] = {
            "position": [place(i, v) for v in read_accessor(gltf, binary, attrs["POSITION"])],
            "normal": [place(i, v, True) for v in read_accessor(gltf, binary, attrs["NORMAL"])],
            "uv": list(read_accessor(gltf, binary, attrs["TEXCOORD_0"])),
            "index": list(read_accessor(gltf, binary, prim["indices"])),
        }

    return bodies, empties


# ---------------------------------------------------------------------------
# Reframing
# ---------------------------------------------------------------------------

def clock(v, degrees):
    """Turns a vector about the pod's own centreline, which is +X in the authored frame."""
    a = math.radians(degrees)
    cos, sin = math.cos(a), math.sin(a)
    return (v[0], cos * v[1] - sin * v[2], sin * v[1] + cos * v[2])


def to_part(v, standoff):
    """Authored frame to part space.

    Nose (+X) onto the host's long axis (+Y); the lug face (+Y) onto the mounting face's inward
    direction (-X), so the pod hangs at +X the way every other store here does. A rotation, not a
    reflection: +Z comes out as +Z and the handedness is preserved.
    """
    return (-v[1] + standoff, v[0], v[2])


def strip_degenerate(body):
    """Drops triangles with no area, and reports how many.

    An authored mesh arrives with collapsed faces -- a UV sphere's poles are a quad grid folded
    into a fan, and every quad there degenerates. They render nothing, so removing them changes no
    pixel; what they *do* is give the tangent basis a zero-length vector, whose normalize() is NaN,
    and NaN survives being multiplied by zero. That is the speckle `checkmesh.py` exists to catch,
    and it is invisible in Blender because the preview material has no normal map wired in.
    """
    verts, index = body["position"], body["index"]
    kept = []
    dropped = 0

    for k in range(0, len(index) - 2, 3):
        a, b, c = index[k], index[k + 1], index[k + 2]
        p, q, r = verts[a], verts[b], verts[c]
        u = (q[0] - p[0], q[1] - p[1], q[2] - p[2])
        v = (r[0] - p[0], r[1] - p[1], r[2] - p[2])
        cross = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])

        if cross[0] * cross[0] + cross[1] * cross[1] + cross[2] * cross[2] < 1e-24:
            dropped += 1
            continue
        kept.extend((a, b, c))

    body["index"] = kept
    return dropped


def reframe(bodies, standoff, pivot_part):
    """Every body in part space, the moving ones clocked and recentred on the shared pivot."""
    out = {}
    for name, body in bodies.items():
        moving = name in MOVING
        turn = CLOCK_DEG if moving else 0.0
        origin = pivot_part if moving else (0.0, 0.0, 0.0)

        positions = []
        for v in body["position"]:
            p = to_part(clock(v, turn), standoff)
            positions.append((p[0] - origin[0], p[1] - origin[1], p[2] - origin[2]))

        # Directions take the rotation and not the offsets, so the standoff is dropped rather
        # than applied -- a normal displaced by 0.27 m is not a normal.
        normals = [to_part(clock(n, turn), 0.0) for n in body["normal"]]

        out[name] = {"position": positions, "normal": normals,
                     "uv": body["uv"], "index": body["index"]}
    return out


# ---------------------------------------------------------------------------
# Measuring
# ---------------------------------------------------------------------------

def ray_hits(origin, direction, body, near=0.02):
    """Nearest hit distance along a ray, or None. Moller-Trumbore, no acceleration structure:
    a few thousand triangles against a few hundred rays is seconds, and this runs by hand."""
    verts, index = body["position"], body["index"]
    ox, oy, oz = origin
    dx, dy, dz = direction
    best = None

    for k in range(0, len(index) - 2, 3):
        v0, v1, v2 = verts[index[k]], verts[index[k + 1]], verts[index[k + 2]]
        e1 = (v1[0] - v0[0], v1[1] - v0[1], v1[2] - v0[2])
        e2 = (v2[0] - v0[0], v2[1] - v0[1], v2[2] - v0[2])

        px, py, pz = dy * e2[2] - dz * e2[1], dz * e2[0] - dx * e2[2], dx * e2[1] - dy * e2[0]
        det = e1[0] * px + e1[1] * py + e1[2] * pz
        if -1e-12 < det < 1e-12:
            continue

        inv = 1.0 / det
        tx, ty, tz = ox - v0[0], oy - v0[1], oz - v0[2]
        u = (tx * px + ty * py + tz * pz) * inv
        if u < 0.0 or u > 1.0:
            continue

        qx, qy, qz = ty * e1[2] - tz * e1[1], tz * e1[0] - tx * e1[2], tx * e1[1] - ty * e1[0]
        w = (dx * qx + dy * qy + dz * qz) * inv
        if w < 0.0 or u + w > 1.0:
            continue

        t = (e2[0] * qx + e2[1] * qy + e2[2] * qz) * inv
        if t > near and (best is None or t < best):
            best = t

    return best


def aperture_deg(reframed, pivot_part, step=0.5):
    """How far off the centreline the line of sight stays clear of the pod's own structure.

    Swept in the nod plane, which after clocking is the plane containing the part's +Y (the
    centreline) and +X (out of the mounting face, the way the nod tilts). Only that one plane
    matters: the shroud rolls *with* the nod, so the aim never leaves it.

    Both sides are reported because the difference is the whole point of the recession, and
    because only the larger one is ever used -- the roll puts the open side on the target.
    """
    shroud = reframed["LiteningPod_Roll"]
    aft = reframed["LiteningPod_Fixed"]

    # The roll body is stored recentred on the pivot; the fixed body is in part space.
    def blocked(direction):
        return (ray_hits((0.0, 0.0, 0.0), direction, shroud) is not None
                or ray_hits(pivot_part, direction, aft) is not None)

    reach = {}
    for side, label in ((+1.0, "open"), (-1.0, "closed")):
        clear = 0.0
        angle = 0.0
        while angle <= 180.0:
            a = math.radians(angle)
            if blocked((side * math.sin(a), math.cos(a), 0.0)):
                break
            clear = angle
            angle += step
        reach[label] = clear

    return reach


# ---------------------------------------------------------------------------
# Writing
# ---------------------------------------------------------------------------

def write_glb(path, reframed):
    """One mesh per body plus a `_VM` twin, no node transforms, node name == mesh name.

    The twins share their accessors rather than duplicating the vertex data: the editor's preview
    is the same geometry, the part is low-poly enough that a simplified one would buy nothing, and
    glTF is happy for two meshes to point at one buffer view.

    No materials and no images, which is what every Core atlas does -- the material comes from
    `<PbrMaterial>` in the asset XML, and a material here would be ignored.
    """
    blob = bytearray()
    views, accessors, meshes, nodes = [], [], [], []

    def view(data, target):
        while len(blob) % 4:
            blob.append(0)
        views.append({"buffer": 0, "byteOffset": len(blob), "byteLength": len(data),
                      "target": target})
        blob.extend(data)
        return len(views) - 1

    def accessor(values, kind, component, target, bounds=False):
        count = len(values)
        if kind == "SCALAR":
            data = struct.pack(f"<{count}I", *values)
        else:
            n = 3 if kind == "VEC3" else 2
            flat = [c for v in values for c in v]
            data = struct.pack(f"<{len(flat)}f", *flat)

        acc = {"bufferView": view(data, target), "componentType": component,
               "count": count, "type": kind}
        if bounds:
            n = 3 if kind == "VEC3" else 2
            acc["min"] = [min(v[a] for v in values) for a in range(n)]
            acc["max"] = [max(v[a] for v in values) for a in range(n)]
        accessors.append(acc)
        return len(accessors) - 1

    for source, ident in BODIES.items():
        body = reframed[source]
        prim = {
            "attributes": {
                "POSITION": accessor(body["position"], "VEC3", 5126, 34962, bounds=True),
                "NORMAL": accessor(body["normal"], "VEC3", 5126, 34962),
                "TEXCOORD_0": accessor(body["uv"], "VEC2", 5126, 34962),
            },
            "indices": accessor(body["index"], "SCALAR", 5125, 34963),
        }
        for name in (ident, ident + "_VM"):
            meshes.append({"name": name, "primitives": [dict(prim)]})
            nodes.append({"name": name, "mesh": len(meshes) - 1})

    while len(blob) % 4:
        blob.append(0)

    gltf = {
        "asset": {"version": "2.0", "generator": "KSArmory import-litening.py"},
        "scene": 0,
        "scenes": [{"nodes": list(range(len(nodes)))}],
        "nodes": nodes,
        "meshes": meshes,
        "accessors": accessors,
        "bufferViews": views,
        "buffers": [{"byteLength": len(blob)}],
    }

    payload = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    payload += b" " * (-len(payload) % 4)

    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "wb") as fh:
        fh.write(struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(payload) + 8 + len(blob)))
        fh.write(struct.pack("<II", len(payload), 0x4E4F534A))
        fh.write(payload)
        fh.write(struct.pack("<II", len(blob), 0x004E4942))
        fh.write(blob)

    return len(blob) + len(payload)


# ---------------------------------------------------------------------------

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", default=str(DEFAULT_SOURCE),
                    help="the authored export: part.xml, Meshes/, Textures/")
    ap.add_argument("--no-textures", action="store_true")
    args = ap.parse_args()

    source = Path(args.source)
    glb = source / "Meshes" / "litening_pod.glb"
    if not glb.is_file():
        print(f"error: no {glb}", file=sys.stderr)
        return 1

    bodies, empties = load(glb)
    missing = [name for name in BODIES if name not in bodies]
    if missing:
        print(f"error: the export has no {', '.join(missing)}", file=sys.stderr)
        return 1

    # The bearing both moving bodies turn on, read off the rig rather than typed.
    ball = empties.get("GMB_Nod", (BALL_CENTRE_POD, 0.0, 0.0))[0]

    # The mounting face is the top of the suspension lugs, which is the highest thing on the fixed
    # body. Everything above the pod's own skin there is lug: the masts are elsewhere.
    top = max(v[1] for v in bodies["LiteningPod_Fixed"]["position"])
    standoff = top

    pivot_part = to_part((ball, 0.0, 0.0), standoff)

    reframed = reframe(bodies, standoff, pivot_part)

    for source_name, ident in BODIES.items():
        dropped = strip_degenerate(reframed[source_name])
        if dropped:
            print(f"  dropped {dropped} zero-area triangle(s) from {ident}")

    print(f"source   {glb}")
    print(f"  ball centre  {ball:.5f} m along the pod, from the rig's GMB_Nod")
    print(f"  lug face     {top:.5f} m above the centreline -> part standoff {standoff:.5f}")
    print()

    for source_name, ident in BODIES.items():
        p = reframed[source_name]["position"]
        lo = [min(v[a] for v in p) for a in range(3)]
        hi = [max(v[a] for v in p) for a in range(3)]
        print(f"  {ident:34} {len(p):5} verts  "
              f"x[{lo[0]:+.3f},{hi[0]:+.3f}] y[{lo[1]:+.3f},{hi[1]:+.3f}] z[{lo[2]:+.3f},{hi[2]:+.3f}]")

    print("\nmeasuring the aperture (this casts rays, and is the slow part)...")
    reach = aperture_deg(reframed, pivot_part)
    binding = min(reach["open"], MECHANICAL_STOP_DEG)

    print(f"  clear line of sight, open side (the recession) : {reach['open']:.1f} deg")
    print(f"  clear line of sight, closed side               : {reach['closed']:.1f} deg")
    print(f"  the gimbal's own stop                          : {MECHANICAL_STOP_DEG:.1f} deg")
    print(f"  -> travel is {binding:.0f} deg, bound by "
          f"{'the gimbal' if binding == MECHANICAL_STOP_DEG else 'the shell'}")

    size = write_glb(ATLAS, reframed)
    print(f"\nwrote {ATLAS.relative_to(REPO)}  ({size / 1024:.0f} KB, "
          f"{len(BODIES)} bodies plus their _VM twins)")

    if not args.no_textures:
        for src, dst in TEXTURES.items():
            shutil.copy2(source / "Textures" / src, MOD / "Textures" / dst)
        print(f"copied {len(TEXTURES)} texture(s) into {(MOD / 'Textures').relative_to(REPO)}")

    # Where the eye sits: outside the glass, so the camera is not inside the ball it looks out of.
    # Measured off the ball rather than typed -- it is the window body's own reach from the pivot.
    ball = max(math.dist(v, (0.0, 0.0, 0.0)) for v in reframed["LiteningPod_Nod"]["position"])
    eye = round(ball + 0.03, 3)

    print("\n=== LITENING pod (paste into src/KSArmory/Sim/Arsenal.cs)")
    print(f"        HeadPivot = new({pivot_part[0]:.5f}, {pivot_part[1]:.5f}, {pivot_part[2]:.5f}),")
    print(f"        EyeForward = {eye:.3f}f,")
    print(f"        MaxOffBoresightDeg = {binding:.0f}f,")
    print(f"        KeyholeDeg = {KEYHOLE_DEG:.0f}f,")

    muzzles = Path(__file__).resolve().parent / "muzzles.json"
    data = json.loads(muzzles.read_text()) if muzzles.is_file() else {}
    data["litening"] = {
        "head_pivot": [round(v, 5) for v in pivot_part],
        "eye_forward": eye,
        "ball_radius": round(ball, 5),
        "max_off_boresight_deg": round(binding),
        "keyhole_deg": KEYHOLE_DEG,
        "aperture_open_deg": reach["open"],
        "aperture_closed_deg": reach["closed"],
        "mechanical_stop_deg": MECHANICAL_STOP_DEG,
        "standoff": round(standoff, 5),
    }
    muzzles.write_text(json.dumps(data, indent=2) + "\n")
    print(f"recorded in {muzzles.relative_to(REPO)}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
