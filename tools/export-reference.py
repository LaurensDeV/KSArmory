#!/usr/bin/env python3
"""Copy KSArmory's weapons, parts and art into the example mod as a reference, read from HEAD.

    ./tools/export-reference.py                  # write ../KSArmory-example-mod/reference/
    ./tools/export-reference.py --check          # say which files differ from HEAD's
    ./tools/export-reference.py --to <folder>    # another example-mod checkout

Read from the commit rather than the working tree, so the commit named in each header is the one the
copy came from. Run by hand when the example mod is updated; nothing runs it, because the example mod
is another repository and a family edit here must not block on it.

The folder is laid out as the mod's own root, so every relative path in the part XML resolves inside it.
"""

import argparse
import json
import re
import shutil
import struct
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MOD = "src/KSArmory"
DEFINITIONS = f"{MOD}/KSArmory"
REPO = "https://github.com/LaurensDeV/KSArmory"
PART_FILES = ["KSArmoryAssets.xml", "KSArmoryGameData.xml", "KSArmoryParticles.xml", "KSArmorySounds.xml"]
ART_FOLDERS = ["Meshes", "Textures", "Sounds"]

# What each family shows a pack author, for the index. A family missing here fails the export, so a
# new family file cannot ship without a line saying what it is for.
SHOWS = {
    "Pantsir": "a launcher that traverses and elevates, twelve tubes in two pods, a cannon on the same "
               "traverse, and a director on its roof (`<Optic>` beside a `<Launcher>` on one part)",
    "Rails": "a launcher with nothing that moves; a heat seeker, an active radar seeker, an "
             "anti-radiation round, and a boost motor with a `<Stage>`",
    "Guns": "guns with no tubes: `<Muzzle>`, belts and bursts, a rotary cluster, a time-fuzed shell, "
            "a chin turret's travel limit, and the cylinder and rod drawn on its linkage",
    "Sensors": "sights as their own parts: a mast head, a roll-nod pod (`Gimbal`), and a search set "
               "(`<SearchSet>`) whose head turns behind a window",
    "Countermeasures": "`<Decoy>` and `<Dispenser>`: one kind of decoy per part",
    "DirectedEnergy": "a beam weapon: `<Beam>` inside an `<Optic>`, and a `<Launcher>` with "
                      "`FiresBeam` and no round",
    "Strategic": "a store that falls (`HitsTerrain`, a tail kit, a bomb sight) and a ballistic bus: "
                 "`Guidance`, trim, hops and `<ReleaseSpin>`",
}

FAMILY = re.compile(r'(<WeaponPack\b[^>]*?)\s+Family="([^"]*)"')
HEADER = re.compile(r"<!--\n  Generated from KSArmory .*?-->\n", re.S)


def git(*args: str) -> str:
    return subprocess.run(["git", "-C", str(ROOT), *args], check=True, capture_output=True,
                          text=True).stdout


def blob(commit: str, path: str) -> bytes:
    return subprocess.run(["git", "-C", str(ROOT), "show", f"{commit}:{path}"], check=True,
                          capture_output=True).stdout


def tracked(commit: str, folder: str) -> list[str]:
    return git("ls-tree", "-r", "--name-only", commit, f"{folder}/").split()


def family_files(commit: str) -> dict[str, tuple[str, str]]:
    """File name to (family, body) for every family file at the commit."""
    found = {}
    for path in tracked(commit, DEFINITIONS):
        if not path.endswith(".xml"):
            continue
        text = git("show", f"{commit}:{path}")
        match = FAMILY.search(text)
        if match is None:
            continue  # Bodies.xml, and the empty Weapons.xml an old install is overwritten with
        # A pack may not carry Family, so the copy is one a pack author can paste as it stands.
        found[Path(path).name] = (match.group(2), FAMILY.sub(r"\1", text, count=1))
    return found


def header(source: str, commit: str, date: str, note: str = "") -> str:
    return (f"<!--\n  Generated from KSArmory {commit[:10]} ({date}) by tools/export-reference.py. Do not edit:\n"
            f"  the live file is {source}\n"
            f"  {REPO}/blob/{commit}/{source}\n"
            + (f"  {note}\n" if note else "") + "-->\n")


def with_header(body: str, head: str) -> str:
    decl, sep, rest = body.partition("?>\n")
    return decl + sep + head + rest if sep else head + body


def expected(commit: str, date: str) -> tuple[dict[str, bytes | str], dict[str, tuple[str, str]]]:
    """Every file the reference should hold, by its path inside it, and the family files."""
    files: dict[str, bytes | str] = {}
    families = family_files(commit)
    for name, (_, body) in families.items():
        files[f"KSArmory/{name}"] = with_header(body, header(
            f"{DEFINITIONS}/{name}", commit, date,
            "The root's Family attribute is left out, because a pack may not carry it."))
    for name in PART_FILES:
        files[name] = with_header(git("show", f"{commit}:{MOD}/{name}"), header(f"{MOD}/{name}", commit, date))
    for folder in ART_FOLDERS:
        for path in tracked(commit, f"{MOD}/{folder}"):
            files[path[len(MOD) + 1:]] = blob(commit, path)
    return files, families


def glb_names(data: bytes) -> set[str]:
    """Mesh and node names in a .glb, from its JSON chunk."""
    length, kind = struct.unpack_from("<II", data, 12)
    doc = json.loads(data[20:20 + length]) if kind == 0x4E4F534A else {}
    return {m.get("name", "") for m in doc.get("meshes", [])} | {n.get("name", "") for n in doc.get("nodes", [])}


def ids(element: ET.Element, tag: str) -> set[str]:
    return {e.get("Id", "") for e in element.iter(tag)}


def union(sets) -> set[str]:
    return set().union(*sets)


def art_by_family(files: dict[str, bytes | str],
                  families: dict[str, tuple[str, str]]) -> dict[str, dict[str, set[str]]]:
    """For each family, the parts it defines and the meshes, textures and sounds those parts use."""
    assets = ET.fromstring(HEADER.sub("", files["KSArmoryAssets.xml"]).encode())
    sounds = ET.fromstring(HEADER.sub("", files["KSArmorySounds.xml"]).encode())

    templates = {s.get("Id"): s for s in assets.iter("SubPart") if s.get("InstanceOf") is None}
    parts = {p.get("Id"): p for p in assets.iter("Part")}
    textures = {m.get("Id"): {t.get("Path") for t in m if t.get("Path")} for m in assets.iter("PbrMaterial")}
    atlases = {path: glb_names(data) for path, data in files.items() if path.endswith(".glb")}
    sound_files = {s.get("Id"): {f.get("Path") for f in s.iter("SoundFile")}
                   for s in sounds.iter("Sound") if s.find("SoundFile") is not None}
    for compound in sounds.iter("CompoundSound"):
        sound_files[compound.get("Id")] = union(sound_files.get(e.get("Id"), set()) for e in compound)

    found: dict[str, dict[str, set[str]]] = {}
    for family, body in families.values():
        root = ET.fromstring(HEADER.sub("", body).encode())
        part_ids = {e.get("PartId") for e in root if e.get("PartId")}
        bodies = {e.get("BodyModel") for e in root if e.get("BodyModel")}
        named_sounds = {v for e in root for k, v in e.attrib.items() if k.endswith("SoundId")}

        used = [templates[b] for b in bodies if b in templates]
        for part_id in part_ids:
            if (part := parts.get(part_id)) is not None:
                used.append(part)
                used += [templates[s.get("InstanceOf")] for s in part.iter("SubPart")
                         if s.get("InstanceOf") in templates]

        meshes = union(ids(e, "Mesh") for e in used)
        materials = union(ids(e, "Material") for e in used)
        found[family] = {
            "parts": part_ids,
            "meshes": {path for path, names in atlases.items() if names & meshes},
            "textures": union(textures.get(m, set()) for m in materials),
            "sounds": union(sound_files.get(s, set()) for s in named_sounds),
        }
    return found


def index(families: dict[str, tuple[str, str]], art: dict[str, dict[str, set[str]]],
          commit: str, date: str) -> str:
    def listed(paths: set[str]) -> str:
        return ", ".join(f"`{p}`" for p in sorted(paths)) or "none of its own"

    sections = []
    for name, (family, _) in sorted(families.items()):
        if family not in SHOWS:
            sys.exit(f"export-reference: no index line for family {family} ({name}); add one to SHOWS")
        a = art[family]
        sections += [
            f"### {family}: [`KSArmory/{name}`](KSArmory/{name})",
            "",
            f"{SHOWS[family][0].upper()}{SHOWS[family][1:]}.",
            "",
            f"- **Parts:** {listed(a['parts'])}, declared in `KSArmoryAssets.xml` and `KSArmoryGameData.xml`",
            f"- **Meshes:** {listed(a['meshes'])}",
            f"- **Textures:** {listed(a['textures'])}",
            f"- **Sounds it names:** {listed(a['sounds'])}",
            f"- [The live file at that commit]({REPO}/blob/{commit}/{DEFINITIONS}/{name})",
            "",
        ]

    return "\n".join([
        "# Reference: every KSArmory weapon, its parts and its art",
        "",
        f"Generated from KSArmory `{commit[:10]}` ({date}) by `tools/export-reference.py` in that "
        "repository. Do not edit anything in this folder; it is overwritten.",
        "",
        "This folder is laid out as KSArmory's own install without its code: the weapon definitions in "
        "`KSArmory/`, the parts KSA reads in `KSArmory*.xml`, and the art in `Meshes/`, `Textures/` and "
        "`Sounds/`, so every path in the part XML resolves here. It is here to read, not to load: "
        "`tools/deploy.sh` does not install it, and it declares KSArmory's own part Ids, so installed as "
        "a mod beside KSArmory every one of them would collide.",
        "",
        "To start from a weapon, copy its definitions into a file in your mod's `KSArmory/` folder. Any "
        "`.xml` there is read and the file name is yours to choose. **Change every `PartId` to a part "
        "your mod declares**, or switch that family off under KSArmory > Built-in weapons to replace it "
        "on KSArmory's own part: while the family is on, its parts are taken and a second definition is "
        "refused. A `Name` can stay as it is, because a pack's names are its own (`YourMod:20MM`); a "
        "reference to one of KSArmory's is written `KSArmory:20MM`, which also lets a pack use its rounds "
        "and sensors without copying them.",
        "",
        "Copying a part means its `<SubPart>` templates, its `<Part>`, its `<PartGameData>`, and the atlas "
        "and materials they name, all under Ids of your own; a marker a definition names is the Id the "
        "`<Part>` gives a subpart, not the template's. A gun naming no sound plays `KSArmoryCannon`.",
        "",
        "## By family",
        "",
        *sections,
        "Every attribute of a definition is described in KSArmory's `docs/WEAPON-PACKS.md`.",
        "",
    ])


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--to", type=Path, default=ROOT.parent / "KSArmory-example-mod")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    if not (args.to / "mod.toml").is_file():
        print(f"export-reference: {args.to} is not the example mod (no mod.toml)", file=sys.stderr)
        return 2

    commit = git("rev-parse", "HEAD").strip()
    date = git("show", "-s", "--format=%cs", commit).strip()
    files, families = expected(commit, date)
    files["README.md"] = index(families, art_by_family(files, families), commit, date)
    out = args.to / "reference"

    if args.check:
        # Headers and the index name a commit, so a copy taken earlier of the same content still matches.
        def body(v: bytes | str) -> bytes | str:
            return re.sub(r"[0-9a-f]{10,40}|\d{4}-\d\d-\d\d", "", v) if isinstance(v, str) else v

        def held(rel: str, want: bytes | str) -> bytes | str | None:
            path = out / rel
            if not path.is_file():
                return None
            return path.read_bytes() if isinstance(want, bytes) else path.read_text()

        present = {p.relative_to(out).as_posix() for p in out.rglob("*") if p.is_file()} if out.is_dir() else set()
        stale = sorted(rel for rel, want in files.items()
                       if (have := held(rel, want)) is None or body(have) != body(want))
        stale += sorted(present - files.keys())
        for rel in stale:
            print(f"stale: reference/{rel}")
        if not stale:
            print(f"reference matches {commit[:10]}")
        return 1 if stale else 0

    if out.is_dir():
        shutil.rmtree(out)
    for rel, content in files.items():
        target = out / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        if isinstance(content, bytes):
            target.write_bytes(content)
        else:
            target.write_text(content)
    print(f"wrote {len(files)} file(s) to {out}, from {commit[:10]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
