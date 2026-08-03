#!/usr/bin/env bash
#
# Builds a release archive: the mod, and nothing else.
#
#   ./tools/package.sh                 # dist/AirDefence-<version>.zip
#   ./tools/package.sh --version 1.2.0 # override the version in the csproj
#
# Release carries no debug symbols (see the csproj) and the log starts at INFO rather than
# DEBUG (see Ksa/Log.cs), so a player's log is the handful of lines that say what the battery
# did rather than several hundred lines of spawn arithmetic.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=env.sh
source "$REPO_ROOT/tools/env.sh"

VERSION=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --version) VERSION="$2"; shift 2 ;;
        -h|--help) sed -n '2,10p' "$0" | sed 's/^# \?//'; exit 0 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

PROJECT="$REPO_ROOT/src/AirDefence/AirDefence.csproj"

if [[ -z "$VERSION" ]]; then
    VERSION="$(grep -oP '(?<=<Version>)[^<]+' "$PROJECT" | head -1)"
fi
[[ -n "$VERSION" ]] || { echo "error: no <Version> in the csproj and none given" >&2; exit 1; }

OUT_DIR="$REPO_ROOT/dist"
STAGE="$OUT_DIR/AirDefence"
ARCHIVE="$OUT_DIR/AirDefence-$VERSION.zip"

echo "packaging AirDefence $VERSION"

# A clean build, so a stale Debug artefact cannot ride along into a release.
rm -rf "$REPO_ROOT/src/AirDefence/bin/Release" "$STAGE" "$ARCHIVE"
dotnet build "$PROJECT" -c Release --nologo -p:Version="$VERSION"

BUILD="$REPO_ROOT/src/AirDefence/bin/Release/net10.0"

mkdir -p "$STAGE/Meshes" "$STAGE/Textures"
cp "$BUILD/AirDefence.dll" "$STAGE/"
cp "$BUILD/mod.toml" "$STAGE/"
cp "$BUILD"/AirDefence*.xml "$STAGE/"
cp "$BUILD/Meshes"/*.glb "$STAGE/Meshes/"
cp "$BUILD/Textures"/*.png "$STAGE/Textures/"

# Refuse to ship debug symbols even if the build somehow produced them.
if compgen -G "$STAGE/*.pdb" >/dev/null || compgen -G "$BUILD/*.pdb" >/dev/null; then
    echo "error: a .pdb reached the release build -- check DebugType in the csproj" >&2
    exit 1
fi

# Game assemblies are referenced with Private=false and must never be redistributed. This is
# the last gate before an archive leaves the machine.
for stray in "$STAGE"/*.dll; do
    case "$(basename "$stray")" in
        AirDefence.dll) ;;
        *) echo "error: $stray is not ours and must not ship" >&2; exit 1 ;;
    esac
done

printf '%s\n' "$VERSION" > "$STAGE/VERSION"
cp "$REPO_ROOT/README.md" "$REPO_ROOT/LICENSE" "$STAGE/"

# zip(1) is not installed everywhere - notably not on this WSL image, nor on a bare CI runner -
# and Python's zipfile is already a dependency of the model tooling.
( cd "$OUT_DIR" && python3 -c "
import shutil, sys
shutil.make_archive('AirDefence-$VERSION', 'zip', root_dir='.', base_dir='AirDefence')
" )
rm -rf "$STAGE"

echo
echo "$ARCHIVE"
python3 - "$ARCHIVE" <<'EOF'
import sys, zipfile
with zipfile.ZipFile(sys.argv[1]) as z:
    for info in sorted(z.infolist(), key=lambda i: i.filename):
        if not info.is_dir():
            print(f"  {info.file_size:8d}  {info.filename}")
EOF
