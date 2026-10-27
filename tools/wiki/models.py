#!/usr/bin/env python3
"""One viewable .glb per KSArmory part, assembled the way KSA assembles it, for the wiki.

    ./tools/wiki/models.py <out-dir>     # write <slug>.glb for every <Part>
    ./tools/wiki/models.py --check       # resolve and place every part, write nothing

An atlas is not a viewable model: its meshes sit at their own pivots, carry no material, and KSA puts
them together from KSArmoryAssets.xml at load. This does the same: each <SubPart>'s PartModel mesh,
looked up by mesh name with the first atlas winning, placed by the subpart's <Transform> (KSA's Euler
rule, from tools/model/checkmesh.py, so there is one transcription), with its <PbrMaterial>'s textures
as a glTF material. KSA ignores an atlas's own nodes, materials and vertex colours, so this does too.

Standard library only, and the textures are embedded as the PNGs they are: the CI step after this
(gltf-transform) resizes and compresses them.
"""

import argparse
import json
import struct
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "src/KSArmory"
sys.path.insert(0, str(ROOT / "tools/model"))
from checkmesh import euler_quaternion, read_glb  # noqa: E402

# A round the mod parks before it is fired is <Scale 0.001>; nothing a player sees is that small.
PARKED_SCALE = 0.01

# Models of hardware that is not a part of its own: (the part carrying it, the subpart Id marker that
# selects it). A missile's launch rail is declared parked on each missile part and shown only when attached.
VIEWS = {"KSArmory_Prefab_LaunchRail": ("KSArmory_Prefab_Aim9J", "LaunchRail")}

# KSA's part frame has +X out of the mounting face and +Y forward; a web viewer is Y-up. A 120 degree
# turn about (1, 1, 1) takes X to Y, Y to Z and Z to X, and keeps the handedness.
KSA_TO_Y_UP = [0.5, 0.5, 0.5, 0.5]

# Stores and what they hang from are shown under their mounting face, as they are carried: the turn
# above and then half a turn about forward, which puts +X down.
HANGING = {"KSArmory_Prefab_Aim9J", "KSArmory_Prefab_Aim120C", "KSArmory_Prefab_Agm88",
           "KSArmory_Prefab_B61", "KSArmory_Prefab_Lau10", "KSArmory_Prefab_ZuniPod6",
           "KSArmory_Prefab_SidewinderRail", "KSArmory_Prefab_AmraamRail", "KSArmory_Prefab_HarmRail",
           "KSArmory_Prefab_NukeRack", "KSArmory_Prefab_WeaponsRail", "KSArmory_Prefab_Rail",
           "KSArmory_Prefab_LaunchRail"}
KSA_TO_HANGING = [-0.5, 0.5, 0.5, -0.5]

# KSA's ModelPbr.frag discards a texel under this alpha; its glass draws at this opacity.
ALPHA_CUTOFF = 0.1
GLASS_OPACITY = 0.75

ARRAY_BUFFER, ELEMENT_ARRAY_BUFFER = 34962, 34963
COMPONENT_BYTES = {5120: 1, 5121: 1, 5122: 2, 5123: 2, 5125: 4, 5126: 4}
TYPE_COUNT = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def slug(part_id: str) -> str:
    return part_id.removeprefix("KSArmory_Prefab_").lower()


def vector(element: ET.Element | None, default: tuple[float, float, float]) -> list[float]:
    if element is None:
        return list(default)
    return [float(element.get(axis, fallback)) for axis, fallback in zip("XYZ", default)]


class Assets:
    """What KSArmoryAssets.xml declares, and the meshes its atlases hold."""

    def __init__(self) -> None:
        root = ET.parse(MOD / "KSArmoryAssets.xml").getroot()
        self.parts = {p.get("Id"): p for p in root.findall("Part")}
        self.materials = {m.get("Id"): {slot: m.find(slot).get("Path")
                                        for slot in ("Diffuse", "Normal", "AoRoughMetal") if m.find(slot) is not None}
                          for m in root.findall("PbrMaterial")}
        self.templates: dict[str, tuple[str, str, bool]] = {}
        for template in root.findall("SubPart"):
            for tag, glass in (("PartModel", False), ("PartModelGlass", True)):
                if (model := template.find(tag)) is not None:
                    self.templates[template.get("Id")] = (model.find("Mesh").get("Id"),
                                                          model.find("Material").get("Id"), glass)

        self.atlases = []
        self.mesh_atlas: dict[str, int] = {}
        for atlas in root.findall("MeshAtlas"):
            gltf, binary = read_glb(MOD / atlas.get("Path"))
            self.atlases.append((gltf, binary))
            for mesh in gltf["meshes"]:
                self.mesh_atlas.setdefault(mesh["name"], len(self.atlases) - 1)

    def placements(self, part_id: str) -> list[tuple[str, str, str, bool, list, list, list]]:
        """(subpart id, mesh, material, glass, position, rotation, scale) for every subpart shown."""
        base, marker = VIEWS.get(part_id, (part_id, None))
        shown = []
        for sub in self.parts[base].findall("SubPart"):
            if marker is not None and marker not in sub.get("Id", ""):
                continue
            template = sub.get("InstanceOf")
            if template not in self.templates:
                raise SystemExit(f"models: {part_id} subpart {sub.get('Id')} names template '{template}', "
                                 "which declares no PartModel")
            mesh, material, glass = self.templates[template]
            transform = sub.find("Transform")
            find = (lambda tag: transform.find(tag)) if transform is not None else (lambda tag: None)
            scale = vector(find("Scale"), (1, 1, 1))
            if max(abs(s) for s in scale) < PARKED_SCALE:
                if marker is None:
                    continue
                scale = [1.0, 1.0, 1.0]
            if mesh not in self.mesh_atlas:
                raise SystemExit(f"models: {part_id} needs mesh '{mesh}', which no <MeshAtlas> holds")
            if material not in self.materials:
                raise SystemExit(f"models: {part_id} needs material '{material}', which is not declared")
            shown.append((sub.get("Id"), mesh, material, glass, vector(find("Position"), (0, 0, 0)),
                          list(euler_quaternion(vector(find("Rotation"), (0, 0, 0)))), scale))
        return shown


def rotate(q: list[float], v: list[float]) -> list[float]:
    x, y, z, w = q
    tx, ty, tz = 2 * (y * v[2] - z * v[1]), 2 * (z * v[0] - x * v[2]), 2 * (x * v[1] - y * v[0])
    return [v[0] + w * tx + y * tz - z * ty, v[1] + w * ty + z * tx - x * tz, v[2] + w * tz + x * ty - y * tx]


def turn(part_id: str) -> list[float]:
    return KSA_TO_HANGING if part_id in HANGING else KSA_TO_Y_UP


def standing(assets: Assets, part_id: str) -> tuple[float, float]:
    """How far the part reaches below and above its mounting face once turned Y-up, from each mesh's
    bounding box; for a hanging part, measured downward."""
    low, high = float("inf"), float("-inf")
    for _, mesh, _, _, position, rotation, scale in assets.placements(part_id):
        gltf, _ = assets.atlases[assets.mesh_atlas[mesh]]
        for primitive in next(m for m in gltf["meshes"] if m["name"] == mesh)["primitives"]:
            box = gltf["accessors"][primitive["attributes"]["POSITION"]]
            for corner in range(8):
                p = [box["max"][i] if corner >> i & 1 else box["min"][i] for i in range(3)]
                p = [p[i] * scale[i] for i in range(3)]
                p = rotate(rotation, p)
                p = rotate(turn(part_id), [p[i] + position[i] for i in range(3)])
                y = -p[1] if part_id in HANGING else p[1]
                low, high = min(low, y), max(high, y)
    return low, high


def assemble(assets: Assets, part_id: str) -> bytes:
    gltf = {"asset": {"version": "2.0", "generator": "KSArmory tools/wiki/models.py"},
            "scene": 0, "scenes": [{"nodes": []}], "nodes": [], "meshes": [], "materials": [],
            "textures": [], "images": [], "samplers": [{"magFilter": 9729, "minFilter": 9987}],
            "accessors": [], "bufferViews": [], "buffers": []}
    blob = bytearray()

    def view(data: bytes, target: int | None = None) -> int:
        blob.extend(b"\0" * (-len(blob) % 4))
        entry = {"buffer": 0, "byteOffset": len(blob), "byteLength": len(data)}
        if target:
            entry["target"] = target
        gltf["bufferViews"].append(entry)
        blob.extend(data)
        return len(gltf["bufferViews"]) - 1

    textures: dict[str, int] = {}

    def texture(path: str) -> int:
        if path not in textures:
            gltf["images"].append({"bufferView": view((MOD / path).read_bytes()), "mimeType": "image/png",
                                   "name": Path(path).name})
            gltf["textures"].append({"sampler": 0, "source": len(gltf["images"]) - 1})
            textures[path] = len(gltf["textures"]) - 1
        return textures[path]

    materials: dict[tuple[str, bool], int] = {}

    def material(material_id: str, glass: bool) -> int:
        if (material_id, glass) not in materials:
            maps = assets.materials[material_id]
            pbr = {"baseColorFactor": [1, 1, 1, GLASS_OPACITY if glass else 1], "metallicFactor": 1,
                   "roughnessFactor": 1}
            entry = {"name": material_id, "pbrMetallicRoughness": pbr,
                     "alphaMode": "BLEND" if glass else "MASK"}
            if not glass:
                entry["alphaCutoff"] = ALPHA_CUTOFF
            if "Diffuse" in maps:
                pbr["baseColorTexture"] = {"index": texture(maps["Diffuse"])}
            if "AoRoughMetal" in maps:
                # KSA packs AO, roughness and metal in R, G and B, which is glTF's own layout for both.
                pbr["metallicRoughnessTexture"] = {"index": texture(maps["AoRoughMetal"])}
                entry["occlusionTexture"] = {"index": texture(maps["AoRoughMetal"])}
            if "Normal" in maps:
                entry["normalTexture"] = {"index": texture(maps["Normal"])}
            gltf["materials"].append(entry)
            materials[(material_id, glass)] = len(gltf["materials"]) - 1
        return materials[(material_id, glass)]

    def accessor(source: dict, binary: bytes, index: int, target: int) -> int:
        a = source["accessors"][index]
        bv = source["bufferViews"][a["bufferView"]]
        size = COMPONENT_BYTES[a["componentType"]] * TYPE_COUNT[a["type"]]
        stride = bv.get("byteStride") or size
        start = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
        data = (binary[start:start + size * a["count"]] if stride == size else
                b"".join(binary[start + i * stride:start + i * stride + size] for i in range(a["count"])))
        entry = {"bufferView": view(data, target), "componentType": a["componentType"], "count": a["count"],
                 "type": a["type"]}
        for key in ("min", "max", "normalized"):
            if key in a:
                entry[key] = a[key]
        gltf["accessors"].append(entry)
        return len(gltf["accessors"]) - 1

    meshes: dict[tuple[str, str, bool], int] = {}

    def mesh(mesh_id: str, material_id: str, glass: bool) -> int:
        if (mesh_id, material_id, glass) not in meshes:
            source, binary = assets.atlases[assets.mesh_atlas[mesh_id]]
            primitives = []
            for p in next(m for m in source["meshes"] if m["name"] == mesh_id)["primitives"]:
                # What KSA's loader reads; a vertex colour would be multiplied in by glTF and never by KSA.
                attributes = {name: accessor(source, binary, p["attributes"][name], ARRAY_BUFFER)
                              for name in ("POSITION", "NORMAL", "TEXCOORD_0") if name in p["attributes"]}
                primitives.append({"attributes": attributes,
                                   "indices": accessor(source, binary, p["indices"], ELEMENT_ARRAY_BUFFER),
                                   "material": material(material_id, glass)})
            gltf["meshes"].append({"name": mesh_id, "primitives": primitives})
            meshes[(mesh_id, material_id, glass)] = len(gltf["meshes"]) - 1
        return meshes[(mesh_id, material_id, glass)]

    children = []
    for sub_id, mesh_id, material_id, glass, position, rotation, scale in assets.placements(part_id):
        gltf["nodes"].append({"name": sub_id, "mesh": mesh(mesh_id, material_id, glass),
                              "translation": position, "rotation": rotation, "scale": scale})
        children.append(len(gltf["nodes"]) - 1)
    gltf["nodes"].append({"name": part_id, "rotation": turn(part_id), "children": children})
    gltf["scenes"][0]["nodes"] = [len(gltf["nodes"]) - 1]

    blob.extend(b"\0" * (-len(blob) % 4))
    gltf["buffers"].append({"byteLength": len(blob)})
    text = json.dumps(gltf, separators=(",", ":")).encode()
    text += b" " * (-len(text) % 4)
    return (struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(text) + 8 + len(blob))
            + struct.pack("<II", len(text), 0x4E4F534A) + text
            + struct.pack("<II", len(blob), 0x004E4942) + bytes(blob))


def check(assets: Assets) -> list[str]:
    """Every part resolves, and stands on its mounting face or hangs from it, never lies on its side."""
    faults = []
    for part_id in [*assets.parts, *VIEWS]:
        if not assets.placements(part_id):
            faults.append(f"{part_id}: no subpart would be shown")
            continue
        low, high = standing(assets, part_id)
        # Most of a part rises out of its mounting face; a turret ring or a shroud may sit a little below.
        if high <= 0 or -low > high / 3:
            way = "below" if part_id in HANGING else "above"
            faults.append(f"{part_id}: spans {low:.2f} to {high:.2f} m {way} its mounting face once turned "
                          "Y-up, so it would not stand on it or hang from it")
    return faults


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("out", nargs="?", type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    assets = Assets()
    if faults := check(assets):
        for fault in faults:
            print(f"models: {fault}", file=sys.stderr)
        return 1
    if args.check:
        print(f"models: {len(assets.parts)} parts and {len(VIEWS)} view(s) assemble and stand on or hang from their mounting faces")
        return 0
    if args.out is None:
        parser.error("an output directory, or --check")

    args.out.mkdir(parents=True, exist_ok=True)
    for part_id in [*assets.parts, *VIEWS]:
        (args.out / f"{slug(part_id)}.glb").write_bytes(assemble(assets, part_id))
    print(f"models: {len(assets.parts) + len(VIEWS)} models written to {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
