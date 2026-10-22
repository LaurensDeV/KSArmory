#!/usr/bin/env python3
"""Write a working copy of every KSArmory weapon into the example mod, read from HEAD.

    ./tools/export-example-copy.py                  # write it into ../KSArmory-example-mod
    ./tools/export-example-copy.py --check          # say which files differ from HEAD's
    ./tools/export-example-copy.py --to <folder>    # another example-mod checkout

The copy is a weapon pack like any other: the definitions in the mod's KSArmory/ folder, its own parts,
meshes, textures and sounds. Every Id KSArmory owns is renamed from KSArmory_ to ExampleCopy_, inside
the meshes too, because KSA matches a mesh by name and a second declaration of an Id collides. So the
copy loads beside KSArmory; with every built-in family switched off it is the only arsenal there is.

Read from the commit rather than the working tree, so the commit named in each header is the one the
copy came from. Run by hand when the example mod is updated; the example mod is another repository and
a family edit here must not block on it.
"""

import argparse
import json
import re
import struct
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MOD = "src/KSArmory"
DEFINITIONS = f"{MOD}/KSArmory"
REPO = "https://github.com/LaurensDeV/KSArmory"

OWN, COPY = "KSArmory_", "ExampleCopy_"
# Sound Ids carry no underscore (KSArmoryCannon), so they are renamed on their own.
SOUND_ID = re.compile(r"\bKSArmory(?=[A-Z])")
PART_FILES = {"KSArmoryAssets.xml": "ExampleCopyAssets.xml",
              "KSArmoryGameData.xml": "ExampleCopyGameData.xml",
              "KSArmorySounds.xml": "ExampleCopySounds.xml"}
INDEX = "ARSENAL.md"

# What each family shows a pack author, for the index. A family missing here fails the export, so a
# new family file cannot ship without a line saying what it is for.
SHOWS = {
    "Pantsir": "a launcher that traverses and elevates, twelve tubes in two pods, a cannon on the same "
               "traverse, and a director on its roof (`<Optic>` beside a `<Launcher>` on one part)",
    "Rails": "a launcher with nothing that moves; a heat seeker, an active radar seeker, an "
             "anti-radiation round, and a boost motor with a `<Stage>`",
    "Rockets": "unguided rockets: a `Guidance=\"None\"` round with a motor, released rather than launched, "
               "several tubes on a part that does not move, and a fin set folded in the tube (`FinMarker`)",
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
# KSArmory declares these editor categories; a second declaration collides, and naming one works without it.
TAG_DEF = re.compile(r"\n?[ \t]*<EditorTagDef\b[^>]*/>[ \t]*\n?")
EDITOR_NAME = re.compile(r'(<PartGameData\b[^>]*\bDisplayName=")([^"]*)(")')


def git(*args: str) -> str:
    return subprocess.run(["git", "-C", str(ROOT), *args], check=True, capture_output=True,
                          text=True).stdout


def blob(commit: str, path: str) -> bytes:
    return subprocess.run(["git", "-C", str(ROOT), "show", f"{commit}:{path}"], check=True,
                          capture_output=True).stdout


def tracked(commit: str, folder: str) -> list[str]:
    return git("ls-tree", "-r", "--name-only", commit, f"{folder}/").split()


def renamed(text: str) -> str:
    return SOUND_ID.sub("ExampleCopy", text.replace(OWN, COPY).replace("KSArmory*.xml", "ExampleCopy*.xml"))


def header(source: str, commit: str, date: str) -> str:
    return (f"<!--\n  Generated from KSArmory {commit[:10]} ({date}) by tools/export-example-copy.py. Do not edit:\n"
            f"  the live file is {source}\n"
            f"  {REPO}/blob/{commit}/{source}\n"
            f"  Every Id is KSArmory's renamed from {OWN} to {COPY}, so this loads beside it.\n-->\n")


def with_header(body: str, head: str) -> str:
    decl, sep, rest = body.partition("?>\n")
    return decl + sep + head + rest if sep else head + body


def glb_renamed(data: bytes) -> bytes:
    """The same .glb with every name in its JSON chunk renamed; the binary chunk is untouched."""
    magic, version, _ = struct.unpack_from("<III", data, 0)
    length, kind = struct.unpack_from("<II", data, 12)
    if kind != 0x4E4F534A:
        sys.exit("export-example-copy: a .glb whose first chunk is not JSON")
    text = renamed(data[20:20 + length].decode()).encode()
    text += b" " * (-len(text) % 4)
    rest = data[20 + length:]
    out = struct.pack("<II", len(text), kind) + text + rest
    return struct.pack("<III", magic, version, 12 + len(out)) + out


def glb_names(data: bytes) -> set[str]:
    length, kind = struct.unpack_from("<II", data, 12)
    doc = json.loads(data[20:20 + length]) if kind == 0x4E4F534A else {}
    return {m.get("name", "") for m in doc.get("meshes", [])} | {n.get("name", "") for n in doc.get("nodes", [])}


def expected(commit: str, date: str) -> tuple[dict[str, bytes | str], dict[str, tuple[str, str]]]:
    """Every file the copy consists of, by its path in the example mod, and the family files."""
    files: dict[str, bytes | str] = {}
    families: dict[str, tuple[str, str]] = {}

    for path in tracked(commit, DEFINITIONS):
        text = git("show", f"{commit}:{path}") if path.endswith(".xml") else ""
        if (match := FAMILY.search(text)) is None:
            continue  # Bodies.xml, and the empty Weapons.xml an old install is overwritten with
        name = Path(path).name
        # A pack may not carry Family.
        body = renamed(FAMILY.sub(r"\1", text, count=1))
        families[name] = (match.group(2), body)
        files[f"KSArmory/{name}"] = with_header(body, header(path, commit, date))

    for own, copy in PART_FILES.items():
        text = renamed(git("show", f"{commit}:{MOD}/{own}"))
        if own == "KSArmoryGameData.xml":
            text = EDITOR_NAME.sub(r"\1\2 (copy)\3", TAG_DEF.sub("\n", text))
        files[copy] = with_header(text, header(f"{MOD}/{own}", commit, date))

    # Only what the copied XML names: the particle textures belong to KSArmory's effects, not to a part.
    named = set(re.findall(r'Path="([^"]+)"', files["ExampleCopyAssets.xml"] + files["ExampleCopySounds.xml"]))
    for folder in ("Meshes", "Textures", "Sounds"):
        for path in tracked(commit, f"{MOD}/{folder}"):
            target = renamed(path[len(MOD) + 1:])
            if target not in named:
                continue
            data = blob(commit, path)
            files[target] = glb_renamed(data) if target.endswith(".glb") else data

    missing = named - files.keys()
    if missing:
        sys.exit(f"export-example-copy: the copied XML names files that are not in the commit: {sorted(missing)}")
    return files, families


def ids(element: ET.Element, tag: str) -> set[str]:
    return {e.get("Id", "") for e in element.iter(tag)}


def union(sets) -> set[str]:
    return set().union(*sets)


def art_by_family(files: dict[str, bytes | str],
                  families: dict[str, tuple[str, str]]) -> dict[str, dict[str, set[str]]]:
    """For each family, the parts it defines and the meshes, textures and sounds those parts use."""
    assets = ET.fromstring(HEADER.sub("", files["ExampleCopyAssets.xml"]).encode())
    sounds = ET.fromstring(HEADER.sub("", files["ExampleCopySounds.xml"]).encode())

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
        root = ET.fromstring(body.encode())
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
            sys.exit(f"export-example-copy: no index line for family {family} ({name}); add one to SHOWS")
        a = art[family]
        sections += [
            f"### {family}: [`KSArmory/{name}`](KSArmory/{name})",
            "",
            f"{SHOWS[family][0].upper()}{SHOWS[family][1:]}.",
            "",
            f"- **Parts:** {listed(a['parts'])}, in `ExampleCopyAssets.xml` and `ExampleCopyGameData.xml`",
            f"- **Meshes:** {listed(a['meshes'])}",
            f"- **Textures:** {listed(a['textures'])}",
            f"- **Sounds it names:** {listed(a['sounds'])}",
            f"- [KSArmory's own file at that commit]({REPO}/blob/{commit}/{DEFINITIONS}/{name})",
            "",
        ]

    return "\n".join([
        "# The example copy: every KSArmory weapon as a pack",
        "",
        f"Generated from KSArmory `{commit[:10]}` ({date}) by `tools/export-example-copy.py` in that "
        "repository. Do not edit these files; they are overwritten. What is generated is listed below, "
        "and the Mk 82 rack beside it is this mod's own.",
        "",
        "Every weapon KSArmory ships, defined the way any pack defines one: the definitions in "
        "`KSArmory/`, the parts in `ExampleCopyAssets.xml` and `ExampleCopyGameData.xml`, the gun sounds "
        "in `ExampleCopySounds.xml`, and the art in `Meshes/`, `Textures/` and `Sounds/`, all under "
        f"`{COPY}` Ids so it loads beside KSArmory. The parts are named with `(copy)` in the editor.",
        "",
        "**To fly the copy alone**, switch every family off under *KSArmory > Built-in weapons* and "
        "restart. KSArmory's own parts then load as structure and leave the editor, and every weapon "
        "there is one of these. A craft needs the copy's parts to carry them: KSArmory's "
        "`tools/copy-save.py` makes a save's craft carry them instead.",
        "",
        "To start your own from one, copy what that family uses below under Ids of your own. A marker a "
        "definition names is found inside the Id the `<Part>` gives a subpart, not the template's. A "
        "gun naming no sound plays KSArmory's `KSArmoryCannon`, and the plume, flash and smoke are "
        "KSArmory's effects for every pack.",
        "",
        "## By family",
        "",
        *sections,
        "Every attribute of a definition is described in KSArmory's `docs/WEAPON-PACKS.md`.",
        "",
    ])


def generated(to: Path) -> set[str]:
    """What an earlier export left in the example mod, found by name and header rather than listed."""
    found = {p.relative_to(to).as_posix() for folder in ("Meshes", "Textures", "Sounds")
             for p in (to / folder).glob(f"{COPY}*")}
    found |= {p.name for p in to.glob("ExampleCopy*.xml")}
    found |= {f"KSArmory/{p.name}" for p in (to / "KSArmory").glob("*.xml") if HEADER.search(p.read_text())}
    if (to / INDEX).is_file():
        found.add(INDEX)
    return found


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--to", type=Path, default=ROOT.parent / "KSArmory-example-mod")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    toml = args.to / "mod.toml"
    if not toml.is_file():
        print(f"export-example-copy: {args.to} is not the example mod (no mod.toml)", file=sys.stderr)
        return 2

    commit = git("rev-parse", "HEAD").strip()
    date = git("show", "-s", "--format=%cs", commit).strip()
    files, families = expected(commit, date)
    files[INDEX] = index(families, art_by_family(files, families), commit, date)
    unlisted = [f for f in PART_FILES.values() if f'"{f}"' not in toml.read_text()]

    if args.check:
        # Headers and the index name a commit, so a copy taken earlier of the same content still matches.
        def body(v: bytes | str) -> bytes | str:
            return re.sub(r"[0-9a-f]{10,40}|\d{4}-\d\d-\d\d", "", v) if isinstance(v, str) else v

        def held(rel: str, want: bytes | str) -> bytes | str | None:
            path = args.to / rel
            if not path.is_file():
                return None
            return path.read_bytes() if isinstance(want, bytes) else path.read_text()

        stale = sorted(rel for rel, want in files.items()
                       if (have := held(rel, want)) is None or body(have) != body(want))
        stale += sorted(generated(args.to) - files.keys())
        for rel in stale:
            print(f"stale: {rel}")
        for name in unlisted:
            print(f"mod.toml does not list {name}, so KSA never reads it")
        if not stale and not unlisted:
            print(f"the example copy matches {commit[:10]}")
        return 1 if stale or unlisted else 0

    for rel in generated(args.to) - files.keys():
        (args.to / rel).unlink()
    for rel, content in files.items():
        target = args.to / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        if isinstance(content, bytes):
            target.write_bytes(content)
        else:
            target.write_text(content)
    print(f"wrote {len(files)} file(s) into {args.to}, from {commit[:10]}")
    for name in unlisted:
        print(f"warning: mod.toml does not list {name}, so KSA never reads it", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
