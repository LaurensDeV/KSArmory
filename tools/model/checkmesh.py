#!/usr/bin/env python3
"""
Hunts for the geometry that makes a mesh z-fight, without needing the game to see it.

Two faces on the same plane leave the depth buffer to pick a winner per pixel, per frame, which in
KSA reads as speckle crawling over the part. Blender's preview render does not reproduce it.

Every triangle is reduced to its plane (unit normal, offset), sign-canonicalised so that two
back-to-back faces hash together, and quantised. Any plane holding triangles from more than one
direction, with overlapping extents, is a candidate.

    ./tools/model/checkmesh.py src/KSArmory/Meshes/KSArmory_MeshAtlas.glb
    ./tools/model/checkmesh.py <atlas.glb> --mesh KSArmory_Subpart_Chassis
    ./tools/model/checkmesh.py <a.glb> --compare <b.glb>    # same model, or genuinely changed?
    ./tools/model/checkmesh.py <gun.glb> --units-per-metre 100   # a character attachment

Exits non-zero if it reports any problem.
"""

import json
import math
import struct
import sys
from collections import defaultdict

# Planes are bucketed at this resolution. Coarser than the 8 mm modelling skin, so genuinely
# separated surfaces do not collide in the same bucket.
PLANE_QUANT = 2.0e-4

# Conflicts under this (m^2, about 5 cm across) are grazes between pieces of one triangulated disc.
MIN_AREA = 2.5e-3

# Parallel faces with a gap inside this band are treated as fighting. The upper bound sits under
# the 8 mm modelling skin; the lower keeps the two triangles of one quad from reporting themselves.
NEAR_MIN = 3.0e-4
NEAR_MAX = 4.0e-3

COMPONENT = {5120: "b", 5121: "B", 5122: "h", 5123: "H", 5125: "I", 5126: "f"}
COUNT = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def read_glb(path):
    """(gltf json, binary chunk)."""
    with open(path, "rb") as fh:
        magic, version, _ = struct.unpack("<III", fh.read(12))
        if magic != 0x46546C67:
            raise ValueError(f"{path} is not a GLB")
        if version != 2:
            raise ValueError(f"unsupported glTF version {version}")

        gltf = binary = None
        while True:
            header = fh.read(8)
            if len(header) < 8:
                break
            length, kind = struct.unpack("<II", header)
            payload = fh.read(length)
            if kind == 0x4E4F534A:
                gltf = json.loads(payload.decode("utf-8"))
            elif kind == 0x004E4942:
                binary = payload
    if gltf is None:
        raise ValueError("no JSON chunk")
    return gltf, binary


def read_accessor(gltf, binary, index):
    """One accessor as a list of tuples, or of scalars."""
    acc = gltf["accessors"][index]
    view = gltf["bufferViews"][acc["bufferView"]]
    fmt = COMPONENT[acc["componentType"]]
    n = COUNT[acc["type"]]
    size = struct.calcsize(fmt) * n
    stride = view.get("byteStride") or size
    base = view.get("byteOffset", 0) + acc.get("byteOffset", 0)

    out = []
    for i in range(acc["count"]):
        chunk = binary[base + i * stride: base + i * stride + size]
        values = struct.unpack("<" + fmt * n, chunk)
        out.append(values[0] if n == 1 else values)
    return out


def triangles(gltf, binary, mesh, with_uv=False):
    """Yields (a, b, c) vertex positions for every triangle, optionally with their UVs."""
    for prim in mesh["primitives"]:
        if prim.get("mode", 4) != 4:
            continue
        pos = read_accessor(gltf, binary, prim["attributes"]["POSITION"])
        uvs = None
        if with_uv and "TEXCOORD_0" in prim["attributes"]:
            uvs = read_accessor(gltf, binary, prim["attributes"]["TEXCOORD_0"])
        idx = (read_accessor(gltf, binary, prim["indices"])
               if "indices" in prim else range(len(pos)))
        idx = list(idx)
        for i in range(0, len(idx) - 2, 3):
            a, b, c = idx[i], idx[i + 1], idx[i + 2]
            if with_uv:
                yield (pos[a], pos[b], pos[c]), (uvs[a], uvs[b], uvs[c]) if uvs else None
            else:
                yield pos[a], pos[b], pos[c]


def degenerate_uvs(gltf, binary, mesh):
    """Triangles whose UV area is zero, and the world area they cover.

    A zero-area UV triangle normalises a zero-length tangent into NaN, which survives a flat
    normal map: flickering white speckle in game, invisible in any DCC preview.
    """
    bad = 0
    bad_area = 0.0
    total = 0
    missing = False

    for tri, uv in triangles(gltf, binary, mesh, with_uv=True):
        total += 1
        if uv is None:
            missing = True
            continue
        (u0, v0), (u1, v1), (u2, v2) = uv
        uv_area = abs((u1 - u0) * (v2 - v0) - (u2 - u0) * (v1 - v0)) / 2.0
        if uv_area < 1e-12:
            bad += 1
            _, _, area = plane_of(*tri)
            bad_area += area

    return bad, bad_area, total, missing


def plane_of(a, b, c):
    """Unit normal and offset, with the sign canonicalised so opposed faces share a key."""
    u = (b[0] - a[0], b[1] - a[1], b[2] - a[2])
    v = (c[0] - a[0], c[1] - a[1], c[2] - a[2])
    n = (u[1] * v[2] - u[2] * v[1],
         u[2] * v[0] - u[0] * v[2],
         u[0] * v[1] - u[1] * v[0])
    length = math.sqrt(n[0] ** 2 + n[1] ** 2 + n[2] ** 2)
    if length < 1e-12:
        return None, 0.0, 0.0
    n = (n[0] / length, n[1] / length, n[2] / length)
    area = length / 2.0

    # First significant component positive, so back-to-back faces share a key.
    flip = 1.0
    for comp in n:
        if abs(comp) > 1e-9:
            flip = -1.0 if comp < 0 else 1.0
            break
    n = (n[0] * flip, n[1] * flip, n[2] * flip)
    d = n[0] * a[0] + n[1] * a[1] + n[2] * a[2]
    return n, d, area


def near_coplanar(gltf, binary, mesh):
    """Overlapping parallel face pairs with a gap inside NEAR_MIN..NEAR_MAX.

    Surfaces a millimetre apart z-fight at distance. Bucketing by plane misses a pair straddling a
    bucket boundary, so this sorts by plane offset within each normal direction and walks neighbours.
    """
    by_normal = defaultdict(list)

    for a, b, c in triangles(gltf, binary, mesh):
        n, d, area = plane_of(a, b, c)
        if n is None or area < 1e-6:
            continue
        key = (round(n[0] / 0.02), round(n[1] / 0.02), round(n[2] / 0.02))
        lo = [min(a[i], b[i], c[i]) for i in range(3)]
        hi = [max(a[i], b[i], c[i]) for i in range(3)]
        by_normal[key].append((d, area, lo, hi))

    hits = []
    for key, faces in by_normal.items():
        faces.sort()
        for i in range(len(faces)):
            di, ai, loi, hii = faces[i]
            for j in range(i + 1, len(faces)):
                dj, aj, loj, hij = faces[j]
                gap = dj - di
                if gap > NEAR_MAX:
                    break
                if gap < NEAR_MIN:
                    continue          # same surface, or exactly coplanar - analyse() has those
                # Only a problem where they actually overlap on screen.
                if all(loi[k] <= hij[k] and loj[k] <= hii[k] for k in range(3)):
                    hits.append((gap, min(ai, aj), key,
                                 [min(loi[k], loj[k]) for k in range(3)],
                                 [max(hii[k], hij[k]) for k in range(3)]))

    hits.sort(key=lambda h: -h[1])
    return hits


def overlap_area_2d(p, q):
    """Area shared by two triangles in a plane, by Sutherland-Hodgman clipping.

    Each triangle's own area would report a 16 mm seam between two boxes as 5.7 m² of conflict.
    """
    # Orient the clip triangle counter-clockwise so "inside" is a consistent sign.
    (ax, ay), (bx, by), (cx, cy) = q
    if (bx - ax) * (cy - ay) - (by - ay) * (cx - ax) < 0:
        q = [q[0], q[2], q[1]]

    poly = list(p)
    for i in range(3):
        ex, ey = q[i]
        fx, fy = q[(i + 1) % 3]
        edge = (fx - ex, fy - ey)

        def inside(v):
            return edge[0] * (v[1] - ey) - edge[1] * (v[0] - ex) >= -1e-12

        clipped = []
        for j in range(len(poly)):
            cur, prv = poly[j], poly[j - 1]
            if inside(cur):
                if not inside(prv):
                    clipped.append(_line_cross(prv, cur, (ex, ey), (fx, fy)))
                clipped.append(cur)
            elif inside(prv):
                clipped.append(_line_cross(prv, cur, (ex, ey), (fx, fy)))
        poly = clipped
        if not poly:
            return 0.0

    total = 0.0
    for i in range(len(poly)):
        x0, y0 = poly[i - 1]
        x1, y1 = poly[i]
        total += x0 * y1 - x1 * y0
    return abs(total) / 2.0


def _line_cross(p0, p1, e0, e1):
    dx, dy = p1[0] - p0[0], p1[1] - p0[1]
    ex, ey = e1[0] - e0[0], e1[1] - e0[1]
    denom = dx * ey - dy * ex
    if abs(denom) < 1e-15:
        return p1
    t = ((e0[0] - p0[0]) * ey - (e0[1] - p0[1]) * ex) / denom
    return (p0[0] + t * dx, p0[1] + t * dy)


def coplanar_overlaps(gltf, binary, mesh):
    """Coplanar triangles that overlap, whichever way they face.

    Two boxes whose outer faces land on one plane point the same way and fight as hard as opposed
    faces, so each plane's triangles are projected into 2D and tested for real overlap.
    """
    return plane_conflicts([(a, b, c, None) for a, b, c in triangles(gltf, binary, mesh)])


def plane_conflicts(tagged, cross_body=False):
    """Each triangle tagged with its body; with `cross_body`, only pairs whose tags differ count."""
    planes = defaultdict(list)

    for a, b, c, tag in tagged:
        n, d, area = plane_of(a, b, c)
        if n is None or area < 1e-7:
            continue
        # Between bodies the pair that matters faces inward (a cap resting on a cap), so the plane
        # is unoriented; within one body it keeps its orientation to find same-facing pairs.
        if cross_body:
            for component in n:
                if abs(component) > 1e-9:
                    if component < 0.0:
                        n, d = tuple(-v for v in n), -d
                    break
        key = (round(n[0] / PLANE_QUANT), round(n[1] / PLANE_QUANT),
               round(n[2] / PLANE_QUANT), round(d / PLANE_QUANT))
        planes[key].append((a, b, c, n, area, tag))

    hits = []
    for key, faces in planes.items():
        if len(faces) < 2:
            continue

        n = faces[0][3]
        # Any basis in the plane will do; only overlap matters, not orientation.
        ref = (0.0, 0.0, 1.0) if abs(n[2]) < 0.9 else (1.0, 0.0, 0.0)
        u = (n[1] * ref[2] - n[2] * ref[1], n[2] * ref[0] - n[0] * ref[2],
             n[0] * ref[1] - n[1] * ref[0])
        ulen = math.sqrt(sum(v * v for v in u)) or 1.0
        u = tuple(v / ulen for v in u)
        w = (n[1] * u[2] - n[2] * u[1], n[2] * u[0] - n[0] * u[2], n[0] * u[1] - n[1] * u[0])

        flat = []
        for a, b, c, _n, area, tag in faces:
            flat.append(([(sum(p[k] * u[k] for k in range(3)),
                           sum(p[k] * w[k] for k in range(3))) for p in (a, b, c)],
                         {tuple(round(v, 6) for v in p) for p in (a, b, c)}, area, tag))

        for i in range(len(flat)):
            for j in range(i + 1, len(flat)):
                if cross_body and flat[i][3] == flat[j][3]:
                    continue
                if flat[i][1] & flat[j][1]:
                    continue          # shares a vertex: same surface, not a conflict
                shared = overlap_area_2d(flat[i][0], flat[j][0])
                # Triangles that merely abut clip to a sliver of numerical noise.
                if shared > 0.05 * min(flat[i][2], flat[j][2]):
                    pair = tuple(sorted((flat[i][3], flat[j][3]))) if cross_body else key
                    hits.append((shared, pair))

    merged = defaultdict(float)
    for area, key in hits:
        merged[key] += area
    return sorted(((a, k) for k, a in merged.items() if a >= MIN_AREA), reverse=True)


def place(point, trs):
    t, q, s = trs
    x, y, z = point[0] * s[0], point[1] * s[1], point[2] * s[2]
    qx, qy, qz, qw = q
    tx, ty, tz = 2 * (qy * z - qz * y), 2 * (qz * x - qx * z), 2 * (qx * y - qy * x)
    return (x + qw * tx + qy * tz - qz * ty + t[0],
            y + qw * ty + qz * tx - qx * tz + t[1],
            z + qw * tz + qx * ty - qy * tx + t[2])


def cross_body_overlaps(gltf, binary, placements):
    """Coplanar overlapping faces between two different bodies, at the rest pose only.

    Each mesh sits in its own pivot-local frame, so the caller (validate-parts.py) supplies
    `placements`: mesh name -> ((x, y, z), (rx, ry, rz)), the asset XML's <SubPart><Transform>
    in part space. The rotation matters: a round on a rail is turned a quarter onto the tube axis.
    """
    by_name = {m.get("name", "?"): m for m in gltf.get("meshes", [])}

    tagged = []
    for name, (origin, euler) in placements.items():
        if name not in by_name:
            continue
        trs = (tuple(origin), euler_quaternion(euler), (1.0, 1.0, 1.0))
        for a, b, c in triangles(gltf, binary, by_name[name]):
            tagged.append((place(a, trs), place(b, trs), place(c, trs), name))

    return plane_conflicts(tagged, cross_body=True)


def euler_quaternion(euler):
    """XYZ Euler radians -> (x, y, z, w), matching how KSA reads <Rotation>.

    Transcribed from KSA.QuaternionEx.CreateFromXyzRadians, which TransformReference.RotationValue
    calls. The composition is qz * qy * qx: X is applied first and Z last.
    """
    import math

    half = [angle / 2.0 for angle in euler]
    cx, cy, cz = (math.cos(a) for a in half)
    sx, sy, sz = (math.sin(a) for a in half)
    return (cy * cz * sx - cx * sy * sz,
            cx * cz * sy + sx * cy * sz,
            cx * cy * sz - sx * cz * sy,
            cx * cy * cz + sx * sy * sz)


def analyse(gltf, binary, mesh):
    """Groups triangles by plane and returns the conflicting ones, worst first."""
    planes = defaultdict(lambda: {"area": 0.0, "facing": defaultdict(float), "lo": [1e9] * 3,
                                  "hi": [-1e9] * 3, "count": 0})

    for a, b, c in triangles(gltf, binary, mesh):
        n, d, area = plane_of(a, b, c)
        if n is None:
            continue
        key = (round(n[0] / PLANE_QUANT), round(n[1] / PLANE_QUANT),
               round(n[2] / PLANE_QUANT), round(d / PLANE_QUANT))
        rec = planes[key]
        rec["area"] += area
        rec["count"] += 1

        # Which way this particular triangle actually faces, before canonicalisation.
        u = (b[0] - a[0], b[1] - a[1], b[2] - a[2])
        v = (c[0] - a[0], c[1] - a[1], c[2] - a[2])
        raw = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
        side = 1 if sum(raw[i] * n[i] for i in range(3)) > 0 else -1
        rec["facing"][side] += area

        for p in (a, b, c):
            for i in range(3):
                rec["lo"][i] = min(rec["lo"][i], p[i])
                rec["hi"][i] = max(rec["hi"][i], p[i])

    conflicts = []
    for key, rec in planes.items():
        front = rec["facing"].get(1, 0.0)
        back = rec["facing"].get(-1, 0.0)
        overlap = min(front, back)          # area that has a face on both sides of the plane
        if overlap >= MIN_AREA:
            conflicts.append((overlap, key, rec))

    conflicts.sort(key=lambda t: -t[0])
    return conflicts


def fingerprint(gltf, binary, mesh):
    """One mesh's surface, independent of triangle order and starting vertex; winding is kept."""
    out = []
    for prim in mesh["primitives"]:
        if prim.get("mode", 4) != 4:
            continue
        attrs = prim["attributes"]
        pos = read_accessor(gltf, binary, attrs["POSITION"])
        nrm = read_accessor(gltf, binary, attrs["NORMAL"]) if "NORMAL" in attrs else None
        uvs = read_accessor(gltf, binary, attrs["TEXCOORD_0"]) if "TEXCOORD_0" in attrs else None
        idx = list(read_accessor(gltf, binary, prim["indices"])
                   if "indices" in prim else range(len(pos)))

        for i in range(0, len(idx) - 2, 3):
            tri = tuple((pos[v], nrm[v] if nrm else None, uvs[v] if uvs else None)
                        for v in idx[i:i + 3])
            start = tri.index(min(tri))
            out.append(tri[start:] + tri[:start])
    out.sort()
    return out


def compare(path_a, path_b):
    """Whether two atlases describe the same geometry.

    Blender's glTF exporter does not emit triangles in a stable order, so an unchanged rebuild is a
    different file; this compares the surface instead of the bytes.
    """
    ga, ba = read_glb(path_a)
    gb, bb = read_glb(path_b)

    by_name_a = {m.get("name", "?"): m for m in ga["meshes"]}
    by_name_b = {m.get("name", "?"): m for m in gb["meshes"]}

    changed = node_transforms(ga, gb, path_a, path_b)
    for name in sorted(set(by_name_a) | set(by_name_b)):
        if name not in by_name_a or name not in by_name_b:
            side = "only in " + (path_a if name in by_name_a else path_b)
            print(f"  {name:<34} {side}")
            changed += 1
            continue

        fa = fingerprint(ga, ba, by_name_a[name])
        fb = fingerprint(gb, bb, by_name_b[name])
        if fa == fb:
            print(f"  {name:<34} identical ({len(fa)} triangles)")
        else:
            moved = sum(1 for x, y in zip(fa, fb) if x != y)
            note = (f"{len(fa)} vs {len(fb)} triangles" if len(fa) != len(fb)
                    else f"{moved} triangle(s) differ")
            print(f"  {name:<34} CHANGED — {note}")
            changed += 1

    print()
    if changed:
        print(f"{changed} difference(s) that are not triangle order")
        return 1
    print("same geometry — the files differ only in triangle order, which is expected")
    return 0


def node_transforms(ga, gb, path_a, path_b):
    """How many mesh-bearing nodes' TRS differ.

    A pose from `build.sh --pose` lands only here, leaving every vertex buffer untouched.
    """
    def by_mesh(gltf):
        names = [m.get("name", "?") for m in gltf.get("meshes", [])]
        out = {}
        for node in gltf.get("nodes", []):
            if node.get("mesh") is None:
                continue
            trs = tuple(round(v, 6) for key, default in (("translation", (0.0, 0.0, 0.0)),
                                                         ("rotation", (0.0, 0.0, 0.0, 1.0)),
                                                         ("scale", (1.0, 1.0, 1.0)))
                        for v in node.get(key, default))
            out[names[node["mesh"]]] = (trs, tuple(round(v, 6) for v in node["matrix"])
                                        if "matrix" in node else None)
        return out

    a, b = by_mesh(ga), by_mesh(gb)
    changed = 0
    for name in sorted(set(a) | set(b)):
        if name not in a or name not in b:
            continue  # a missing mesh is reported by the geometry pass
        if a[name] != b[name]:
            print(f"  {name:<34} MOVED — node transform differs")
            changed += 1
    if changed:
        print(f"  ({path_a} and {path_b} place {changed} body(s) differently — "
              f"one of them is posed)")
    return changed


def main():
    global PLANE_QUANT, MIN_AREA, NEAR_MIN, NEAR_MAX

    # By position, not by matching the value: `--near-max 0` would otherwise leave "0" as a path.
    VALUED = ("--units-per-metre", "--near-max", "--mesh", "--compare")
    args, options, rest = [], {}, list(sys.argv[1:])
    while rest:
        token = rest.pop(0)
        if token in VALUED:
            if not rest:
                print(f"error: {token} needs a value", file=sys.stderr)
                return 2
            options[token] = rest.pop(0)
        elif not token.startswith("--"):
            args.append(token)

    # Every threshold is in metres. A character attachment is authored in centimetres, so the
    # thresholds are scaled rather than the mesh, keeping printed numbers in the file's own units.
    if "--units-per-metre" in options:
        upm = float(options["--units-per-metre"])
        if upm <= 0.0:
            print("error: --units-per-metre must be positive", file=sys.stderr)
            return 2
        PLANE_QUANT *= upm
        MIN_AREA *= upm * upm
        NEAR_MIN *= upm
        NEAR_MAX *= upm
    # NEAR_MAX is calibrated to box()'s 8 mm modelling skin. An authored mesh has none, so the band
    # reports every deliberate panel step; `--near-max 0` turns that advisory off and leaves the
    # zero-UV-area and exact coplanar checks on.
    if "--near-max" in options:
        NEAR_MAX = float(options["--near-max"]) / 1000.0
    only = options.get("--mesh")
    other = options.get("--compare")
    if not args:
        print(__doc__.strip().splitlines()[-3].strip(), file=sys.stderr)
        return 2

    if other:
        return compare(args[0], other)

    total = 0
    for path in args:
        total += check_atlas(path, only, header=len(args) > 1)

    print()
    if total:
        print(f"FOUND {total} problem(s) — these are what make the part render wrong in game")
        return 1
    print("clean: names pair, every triangle has UV area, and no two faces share a plane")
    return 0


def unpaired_names(gltf):
    """Nodes whose mesh is not named after them, which KSA renders as nothing.

    Blender silently renames a mesh "X.001" when an orphan already holds "X", and <Mesh Id="X">
    then resolves to nothing with nothing in any log.
    """
    meshes = gltf.get("meshes", [])
    bad = []
    for node in gltf.get("nodes", []):
        index = node.get("mesh")
        if index is None or index >= len(meshes):
            continue
        node_name = node.get("name", "?")
        mesh_name = meshes[index].get("name", "?")
        if node_name != mesh_name:
            bad.append((node_name, mesh_name))
    return bad


def check_atlas(path, only, header=False):
    """Reports one atlas and returns its problem count."""
    gltf, binary = read_glb(path)
    total = 0
    if header:
        print(f"\n--- {path}")

    for node_name, mesh_name in unpaired_names(gltf):
        print(f"\nnode {node_name}: draws mesh {mesh_name} — the names must match, or "
              f"<Mesh Id=\"{node_name}\"> resolves to nothing and the body is invisible")
        total += 1

    for mesh in gltf["meshes"]:
        name = mesh.get("name", "?")
        if name.endswith("_VM"):
            continue                        # same geometry as its parent; nothing new to say
        if name.startswith("_ColPrim"):
            # A collider primitive: never rendered, so it has no UVs and every check would fail it.
            continue
        if only and name != only:
            continue

        bad, bad_area, tris, missing = degenerate_uvs(gltf, binary, mesh)
        if missing:
            print(f"\n{name}: NO TEXCOORD_0 — the part will not texture")
            total += 1
        elif bad:
            print(f"\n{name}: {bad}/{tris} triangles have ZERO UV AREA "
                  f"({bad_area:.2f} m²) — degenerate tangents, expect speckle")
            total += 1
        else:
            print(f"\n{name}: {tris} triangles, all with usable UV area")

        near = near_coplanar(gltf, binary, mesh)
        if near:
            print(f"  {len(near)} near-coplanar pair(s) within {NEAR_MAX * 1000:.0f} mm "
                  f"— these fight at distance:")
            for gap, area, _key, lo, hi in near[:8]:
                print(f"    {gap * 1000:6.2f} mm apart, {area * 1e4:7.1f} cm²  "
                      f"x[{lo[0]:.2f},{hi[0]:.2f}] y[{lo[1]:.2f},{hi[1]:.2f}] z[{lo[2]:.2f},{hi[2]:.2f}]")
            if len(near) > 8:
                print(f"    ... and {len(near) - 8} more")
            total += 1

        overlaps = coplanar_overlaps(gltf, binary, mesh)
        if overlaps:
            print(f"  {len(overlaps)} coplanar overlap(s) — these z-fight:")
            for area, key in overlaps[:12]:
                n = [k * PLANE_QUANT for k in key[:3]]
                print(f"    {area * 1e4:8.1f} cm²  normal ({n[0]:+.2f},{n[1]:+.2f},{n[2]:+.2f})"
                      f"  at {key[3] * PLANE_QUANT:+.4f}")
            if len(overlaps) > 12:
                print(f"    ... and {len(overlaps) - 12} more")
            total += len(overlaps)
        else:
            print("  no coplanar overlaps")

    return total


if __name__ == "__main__":
    sys.exit(main())
