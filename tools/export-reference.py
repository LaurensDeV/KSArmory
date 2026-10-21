#!/usr/bin/env python3
"""Copy KSArmory's own weapon files into the example mod as a reference, read from HEAD.

    ./tools/export-reference.py                  # write ../KSArmory-example-mod/reference/
    ./tools/export-reference.py --check          # say which copies differ from HEAD's files
    ./tools/export-reference.py --to <folder>    # another example-mod checkout

Read from the commit rather than the working tree, so the commit named in each header is the one the
copy came from. Run by hand when the example mod is updated; nothing runs it, because the example mod
is another repository and a family edit here must not block on it.
"""

import argparse
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCE = "src/KSArmory/KSArmory"
REPO = "https://github.com/LaurensDeV/KSArmory"

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


def git(*args: str) -> str:
    return subprocess.run(["git", "-C", str(ROOT), *args], check=True, capture_output=True,
                          text=True).stdout


def family_files(commit: str) -> dict[str, tuple[str, str]]:
    """File name to (family, body) for every family file at the commit."""
    found = {}
    for path in git("ls-tree", "--name-only", commit, f"{SOURCE}/").split():
        if not path.endswith(".xml"):
            continue
        text = git("show", f"{commit}:{path}")
        match = FAMILY.search(text)
        if match is None:
            continue  # Bodies.xml, and the empty Weapons.xml an old install is overwritten with
        # A pack may not carry Family, so the copy is one a pack author can paste as it stands.
        found[Path(path).name] = (match.group(2), FAMILY.sub(r"\1", text, count=1))
    return found


def header(name: str, commit: str, date: str) -> str:
    return (f"<!--\n  Generated from KSArmory {commit[:10]} ({date}) by tools/export-reference.py. Do not edit:\n"
            f"  the live file is {SOURCE}/{name}\n"
            f"  {REPO}/blob/{commit}/{SOURCE}/{name}\n"
            f"  The root's Family attribute is left out, because a pack may not carry it.\n-->\n")


def with_header(body: str, head: str) -> str:
    decl, sep, rest = body.partition("?>\n")
    return decl + sep + head + rest if sep else head + body


def without_header(text: str) -> str:
    return re.sub(r"<!--\n  Generated from KSArmory .*?-->\n", "", text, count=1, flags=re.S)


def index(files: dict[str, tuple[str, str]], commit: str, date: str) -> str:
    rows = []
    for name, (family, _) in sorted(files.items()):
        if family not in SHOWS:
            sys.exit(f"export-reference: no index line for family {family} ({name}); add one to SHOWS")
        rows.append(f"| [`{name}`]({name}) | {SHOWS[family]} | [live]({REPO}/blob/{commit}/{SOURCE}/{name}) |")
    return "\n".join([
        "# Reference: every KSArmory weapon as a pack declares it",
        "",
        f"Generated from KSArmory `{commit[:10]}` ({date}) by `tools/export-reference.py` in that "
        "repository. Do not edit these files; they are overwritten.",
        "",
        "These are KSArmory's own weapon definitions, in the same format as `../KSArmory/Weapons.xml`. "
        "They are here to read, not to load: KSArmory reads only the `KSArmory/` folder, so nothing in "
        "this one registers.",
        "",
        "To start from one, copy the definitions you want into a file in your mod's `KSArmory/` folder. "
        "Any `.xml` there is read and the file name is yours to choose. **Change every `PartId` to a part "
        "your mod declares**, or switch that family off under KSArmory > Built-in weapons to replace it on "
        "KSArmory's own part: while the family is on, its parts are taken and a second definition is refused. A `Name` "
        "can stay as it is, because a pack's names are its own (`YourMod:20MM`); a reference to one of "
        "KSArmory's is written `KSArmory:20MM`, which also lets a pack use its rounds and sensors without "
        "copying them.",
        "",
        "| File | What it shows | At that commit |",
        "| --- | --- | --- |",
        *rows,
        "",
        "Every attribute is described in KSArmory's `docs/WEAPON-PACKS.md`.",
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
    files = family_files(commit)
    out = args.to / "reference"

    if args.check:
        stale = [name for name, (_, body) in sorted(files.items())
                 if not (out / name).is_file() or without_header((out / name).read_text()) != body]
        stale += [p.name for p in sorted(out.glob("*.xml")) if p.name not in files]
        for name in stale:
            print(f"stale: reference/{name}")
        if not stale:
            print(f"reference matches {commit[:10]}")
        return 1 if stale else 0

    out.mkdir(exist_ok=True)
    for old in out.glob("*.xml"):
        if old.name not in files:
            old.unlink()
    for name, (_, body) in files.items():
        (out / name).write_text(with_header(body, header(name, commit, date)))
    (out / "README.md").write_text(index(files, commit, date))
    print(f"wrote {len(files)} file(s) and README.md to {out}, from {commit[:10]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
