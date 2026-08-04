#!/usr/bin/env bash
#
# Writes a version into the project file, so the assembly and the release archive agree.
#
#   ./tools/set-version.sh 1.2.0
#
# Called by semantic-release during a release (see .releaserc.json), and safe to run by hand.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$REPO_ROOT/src/KSArmory/KSArmory.csproj"

VERSION="${1:-}"
if [[ -z "$VERSION" ]]; then
    echo "usage: $(basename "$0") <version>" >&2
    exit 2
fi

# semantic-release hands over a bare semver; anything else is a mistake worth catching before
# it ends up stamped into an assembly.
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
    echo "error: '$VERSION' is not a semantic version" >&2
    exit 1
fi

if ! grep -q '<Version>' "$PROJECT"; then
    echo "error: no <Version> element in $PROJECT" >&2
    exit 1
fi

python3 - "$PROJECT" "$VERSION" <<'PY'
import re
import sys

path, version = sys.argv[1], sys.argv[2]
with open(path, encoding="utf-8") as fh:
    text = fh.read()

updated, count = re.subn(r"<Version>[^<]*</Version>", f"<Version>{version}</Version>", text, count=1)
if count != 1:
    sys.exit(f"error: expected one <Version> in {path}, replaced {count}")

with open(path, "w", encoding="utf-8") as fh:
    fh.write(updated)
PY

echo "version set to $VERSION in $(basename "$PROJECT")"
