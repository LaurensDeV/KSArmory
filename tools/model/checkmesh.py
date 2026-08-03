#!/usr/bin/env python3
"""
Hunts for the geometry that makes a mesh z-fight, without needing the game to see it.

A model assembled out of interpenetrating primitives is fine right up until two faces land on
the *same plane*. Then the depth buffer has no basis to choose between them and picks a winner
per pixel, per frame — which in KSA reads as the whole vehicle crawling with speckle. Blender's
own preview render does not reproduce it, so this is the only way to find it short of
restarting the game.

Every triangle is reduced to its plane (unit normal, offset), sign-canonicalised so that two
back-to-back faces hash together, and quantised. Any plane holding triangles from more than one
direction, with overlapping extents, is a candidate.

    ./tools/model/checkmesh.py src/AirDefence/Meshes/AirDefence_MeshAtlas.glb
    ./tools/model/checkmesh.py <atlas.glb> --mesh AirDefence_Subpart_Chassis
    ./tools/model/checkmesh.py <a.glb> --compare <b.glb>    # same model, or genuinely changed?

Exits non-zero if it finds conflicting coplanar area above the reporting threshold.
"""

import json
import math
import struct
import sys
from collections import defaultdict

# Planes are bucketed at this resolution. Coarser than the 8 mm modelling skin, so genuinely
# separated surfaces do not collide in the same bucket.
PLANE_QUANT = 2.0e-4

# Ignore conflicts smaller than this (m^2). A patch this size is about 5 cm across on an
# eight-metre vehicle, and everything the tool reported below it on this model turned out to be
# a graze between two pieces of one triangulated disc rather than two surfaces in conflict.
# Raise it back if something small but visible ever slips through.
MIN_AREA = 2.5e-3

# Parallel faces separated by a gap inside this band are treated as fighting. The upper bound
# sits under the 8 mm modelling skin, so deliberate interpenetration is not flagged; the lower
# bound exists because two triangles of the *same* flat quad are coplanar to within float
# noise, and without it every quad in the model reports itself.
NEAR_MIN = 3.0e-4
NEAR_MAX = 4.0e-3

COMPONENT = {5120: "b", 5121: "B", 5122: "h", 5123: "H", 5125: "I", 5126: "f"}
COUNT = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def read_glb(path):
    """Returns (gltf json, binary chunk)."""
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
    """Decodes one accessor into a list of tuples (or scalars)."""
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
    """Counts triangles whose UV area is zero, and the world area they cover.

    A zero-area UV triangle has no usable tangent frame. Renderers that build one from the UV
    derivative get a zero-length tangent; normalizing it yields NaN, and NaN survives being
    multiplied by zero, so even a flat normal map cannot save the shading. The symptom is
    flickering white speckle in game, invisible in any DCC preview.
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

    # Flip so the first significant component is positive; a face and its back-to-back
    # neighbour then land on the same key instead of two mirrored ones.
    flip = 1.0
    for comp in n:
        if abs(comp) > 1e-9:
            flip = -1.0 if comp < 0 else 1.0
            break
    n = (n[0] * flip, n[1] * flip, n[2] * flip)
    d = n[0] * a[0] + n[1] * a[1] + n[2] * a[2]
    return n, d, area


def near_coplanar(gltf, binary, mesh):
    """Face pairs that are parallel and closer together than `tolerance`.

    Exact coplanarity is not the only thing that z-fights. Two surfaces a millimetre apart are
    indistinguishable to the depth buffer once the camera is far enough away, and a model
    assembled from independently placed primitives grows those by accident. Bucketing by plane
    (as analyse() does) misses them whenever the pair straddles a bucket boundary, so this
    sorts by plane offset within each normal direction and walks neighbours.
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

    The real number matters. Reporting each triangle's own area instead turns a 16 mm seam
    between two boxes into "5.7 m² of conflict", and a tool that cries wolf gets ignored.
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
    """Coplanar triangles from different primitives that actually overlap on screen.

    The obvious test — "does this plane hold faces pointing both ways" — misses the case that
    matters most here. Two boxes whose outer faces land on the same plane point the *same* way,
    and fight exactly as hard. So this projects each plane's triangles into 2D and looks for
    real overlap, skipping pairs that share vertices (those are the two halves of one quad, or
    a fan, not a conflict).
    """
    planes = defaultdict(list)

    for a, b, c in triangles(gltf, binary, mesh):
        n, d, area = plane_of(a, b, c)
        if n is None or area < 1e-7:
            continue
        key = (round(n[0] / PLANE_QUANT), round(n[1] / PLANE_QUANT),
               round(n[2] / PLANE_QUANT), round(d / PLANE_QUANT))
        planes[key].append((a, b, c, n, area))

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
        for a, b, c, _n, area in faces:
            flat.append(([(sum(p[k] * u[k] for k in range(3)),
                           sum(p[k] * w[k] for k in range(3))) for p in (a, b, c)],
                         {tuple(round(v, 6) for v in p) for p in (a, b, c)}, area))

        for i in range(len(flat)):
            for j in range(i + 1, len(flat)):
                if flat[i][1] & flat[j][1]:
                    continue          # shares a vertex: same surface, not a conflict
                shared = overlap_area_2d(flat[i][0], flat[j][0])
                # A real overlap, not a graze. Triangles that merely abut - the pieces of one
                # triangulated disc, say - clip to a sliver whose area is numerical noise, and
                # counting those reports every wheel as fighting itself.
                if shared > 0.05 * min(flat[i][2], flat[j][2]):
                    hits.append((shared, key))

    merged = defaultdict(float)
    for area, key in hits:
        merged[key] += area
    return sorted(((a, k) for k, a in merged.items() if a >= MIN_AREA), reverse=True)


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
    """A canonical, order-independent description of one mesh's surface.

    Every triangle becomes ((position, normal, uv) x3), rotated so its lowest-sorting corner
    comes first — which preserves winding while removing the arbitrary choice of starting
    vertex — and the triangles are then sorted. Two fingerprints compare equal exactly when the
    two meshes describe the same surface, whatever order the exporter happened to emit it in.
    """
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
    """Reports whether two atlases describe the same geometry.

    Blender's glTF exporter does not emit triangles in a stable order, so rebuilding the model
    from unchanged sources produces a *different file* — same positions, normals and UVs, only
    the index buffer permuted. Byte comparison therefore says "changed" on every rebuild and is
    useless for answering the question you actually have, which is whether an edit to
    pantsir.py moved anything. This compares the surface instead.
    """
    ga, ba = read_glb(path_a)
    gb, bb = read_glb(path_b)

    by_name_a = {m.get("name", "?"): m for m in ga["meshes"]}
    by_name_b = {m.get("name", "?"): m for m in gb["meshes"]}

    changed = 0
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
        print(f"{changed} mesh(es) genuinely differ")
        return 1
    print("same geometry — the files differ only in triangle order, which is expected")
    return 0


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    only = other = None
    if "--mesh" in sys.argv:
        only = sys.argv[sys.argv.index("--mesh") + 1]
        args = [a for a in args if a != only]
    if "--compare" in sys.argv:
        other = sys.argv[sys.argv.index("--compare") + 1]
        args = [a for a in args if a != other]
    if not args:
        print(__doc__.strip().splitlines()[-3].strip(), file=sys.stderr)
        return 2

    if other:
        return compare(args[0], other)

    gltf, binary = read_glb(args[0])
    total = 0

    for mesh in gltf["meshes"]:
        name = mesh.get("name", "?")
        if name.endswith("_VM"):
            continue                        # same geometry as its parent; nothing new to say
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

    print()
    if total:
        print(f"FOUND {total} problem(s) — these are what make the part sparkle in game")
        return 1
    print("clean: every triangle has UV area, and no two faces share a plane")
    return 0


if __name__ == "__main__":
    sys.exit(main())
