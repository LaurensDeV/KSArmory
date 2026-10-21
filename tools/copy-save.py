#!/usr/bin/env python3
"""Copy a KSA save so its craft carry the example mod's copy of each KSArmory part instead.

    ./tools/copy-save.py CIWS "5inch_gun"        # writes "CIWS (copy)" and "5inch_gun (copy)"
    ./tools/copy-save.py --force CIWS            # replace a copy made before

A save names every part and subpart it holds by template Id (InstanceOf), and the example copy is the
same parts under ExampleCopy_ Ids, so renaming those values is the whole conversion. The original is
only read. With every built-in family switched off, a scenario flown on the copy flies the pack.
"""

import argparse
import re
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
INSTANCE = re.compile(r'(InstanceOf=")KSArmory_')
SUFFIX = " (copy)"


def user_dir() -> Path:
    return Path(subprocess.run([str(ROOT / "tools/ksa-user-dir.sh")], check=True, capture_output=True,
                               text=True).stdout.strip())


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("saves", nargs="+")
    parser.add_argument("--force", action="store_true", help="replace a copy that already exists")
    args = parser.parse_args()

    saves = user_dir() / "saves"
    failed = False
    for name in args.saves:
        source, target = saves / name, saves / f"{name}{SUFFIX}"
        if not (source / "universe.xml").is_file():
            print(f"{name}: no such save", file=sys.stderr)
            failed = True
            continue
        if target.exists():
            if not args.force:
                print(f"{target.name}: already exists; --force replaces it", file=sys.stderr)
                failed = True
                continue
            shutil.rmtree(target)

        # The .bak is KSA's previous write of the original; a copy has no history of its own.
        shutil.copytree(source, target, ignore=shutil.ignore_patterns("*.bak"))
        universe = target / "universe.xml"
        text, parts = INSTANCE.subn(r"\1ExampleCopy_", universe.read_text(encoding="utf-8"))
        universe.write_text(text, encoding="utf-8")

        # KSA lists saves by the name in meta.toml, so the copy needs its own or it reads as a duplicate.
        meta = target / "meta.toml"
        meta.write_text(re.sub(r'^name = "(.*)"$', lambda m: f'name = "{m.group(1)}{SUFFIX}"',
                               meta.read_text(encoding="utf-8"), count=1, flags=re.M), encoding="utf-8")
        print(f"{target.name}: {parts} part and subpart reference(s) moved onto the example copy")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
