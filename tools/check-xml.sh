#!/usr/bin/env bash
#
# Parses every asset XML the mod ships.
#
#     ./tools/check-xml.sh
#
# KSA reports a malformed asset file as the part simply not existing, with nothing in its log
# tying that back to a stray angle bracket. This is the cheapest check in the repository and it
# covers the failure that is hardest to read from the game.
#
# validate-parts.py checks what the XML *says*; this only checks that it can be read at all, so
# it stays useful when the game is not installed.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT" || exit 1

python3 - <<'PY'
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

paths = sorted(Path("src/KSArmory").glob("KSArmory*.xml"))
if not paths:
    print("error: no asset XML found under src/KSArmory", file=sys.stderr)
    sys.exit(1)

bad = 0
for path in paths:
    try:
        ET.parse(path)
    except ET.ParseError as e:
        print(f"  MALFORMED {path}: {e}", file=sys.stderr)
        bad += 1
    else:
        print(f"  {path} ok")

if bad:
    print(f"\n{bad} malformed file(s)", file=sys.stderr)
    sys.exit(1)

print(f"\nxml ok: {len(paths)} file(s) parse")
PY
