#!/usr/bin/env python3
"""
Prints the meshes inside a KSA mesh atlas (.glb) with their bounding boxes.

KSA parts are assembled by referencing named sub-meshes out of a shared atlas and placing
them with <Transform> offsets, so you need real dimensions to lay a part out. glTF stores
per-accessor min/max for POSITION, which gives exact bounds without decoding any vertices.

    ./tools/meshinfo.py "/mnt/c/Program Files/Kitten Space Agency/Content/Core/Meshes/CoreStructuralA_MeshAtlas.glb"
    ./tools/meshinfo.py <atlas.glb> Tube      # only meshes whose name contains "Tube"
"""

import json
import struct
import sys


def read_glb_json(path):
    """Returns the JSON chunk of a .glb as a dict."""
    with open(path, "rb") as fh:
        magic, version, _total = struct.unpack("<III", fh.read(12))
        if magic != 0x46546C67:  # 'glTF'
            raise ValueError(f"{path} is not a GLB (bad magic)")
        if version != 2:
            raise ValueError(f"unsupported glTF version {version}")

        while True:
            header = fh.read(8)
            if len(header) < 8:
                raise ValueError("no JSON chunk found")
            length, kind = struct.unpack("<II", header)
            payload = fh.read(length)
            if kind == 0x4E4F534A:  # 'JSON'
                return json.loads(payload.decode("utf-8"))


def mesh_bounds(gltf, mesh):
    """Union of the POSITION accessor bounds across a mesh's primitives."""
    lo = [float("inf")] * 3
    hi = [float("-inf")] * 3
    found = False

    for prim in mesh.get("primitives", []):
        idx = prim.get("attributes", {}).get("POSITION")
        if idx is None:
            continue
        accessor = gltf["accessors"][idx]
        amin, amax = accessor.get("min"), accessor.get("max")
        if not amin or not amax:
            continue
        found = True
        for axis in range(3):
            lo[axis] = min(lo[axis], amin[axis])
            hi[axis] = max(hi[axis], amax[axis])

    return (lo, hi) if found else (None, None)


def main():
    if len(sys.argv) < 2:
        print(__doc__.strip(), file=sys.stderr)
        return 1

    path = sys.argv[1]
    needle = sys.argv[2].lower() if len(sys.argv) > 2 else ""

    gltf = read_glb_json(path)
    meshes = gltf.get("meshes", [])

    print(f"{len(meshes)} meshes in {path}\n")
    print(f"{'name':<52} {'size X':>9} {'size Y':>9} {'size Z':>9}   centre")
    print("-" * 110)

    for mesh in meshes:
        name = mesh.get("name", "<unnamed>")
        if needle and needle not in name.lower():
            continue

        lo, hi = mesh_bounds(gltf, mesh)
        if lo is None:
            print(f"{name:<52} {'(no bounds)':>29}")
            continue

        size = [hi[i] - lo[i] for i in range(3)]
        centre = [(hi[i] + lo[i]) / 2 for i in range(3)]
        print(
            f"{name:<52} {size[0]:>9.3f} {size[1]:>9.3f} {size[2]:>9.3f}   "
            f"({centre[0]:+.3f}, {centre[1]:+.3f}, {centre[2]:+.3f})"
        )

    return 0


if __name__ == "__main__":
    sys.exit(main())
