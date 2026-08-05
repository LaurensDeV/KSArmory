#!/usr/bin/env bash
#
# Builds the mod and installs it where StarMap looks for mods.
#
#   ./tools/deploy.sh              # Release build, install to the user mods folder
#   ./tools/deploy.sh Debug        # Debug build
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG="${1:-Release}"

# The mod targets net10.0; a distro dotnet 8 cannot build it.
# shellcheck source=env.sh
source "$REPO_ROOT/tools/env.sh"

# Where StarMap reads mods from. KSA_MODS_DIR overrides everything.
# shellcheck source=ksa-user-dir.sh
source "$REPO_ROOT/tools/ksa-user-dir.sh"

if [[ -n "${KSA_MODS_DIR:-}" ]]; then
    MODS_DIR="$KSA_MODS_DIR"
elif USER_DIR="$(ksa_user_dir)"; then
    MODS_DIR="$USER_DIR/mods"
else
    echo "error: could not locate the KSA user folder on this machine." >&2
    echo "       set KSA_MODS_DIR to <user dir>/Kitten Space Agency/mods and retry" >&2
    exit 1
fi

TARGET="$MODS_DIR/KSArmory"

echo "building ($CONFIG)..."
dotnet build "$REPO_ROOT/src/KSArmory/KSArmory.csproj" -c "$CONFIG" --nologo

OUT="$REPO_ROOT/src/KSArmory/bin/$CONFIG/net10.0"

# Before the copy, not after: a bad asset Id or a marker resolving to nothing is a *silent*
# in-game failure, so without this the first sign is a launch, an inspection and a quit. 89 ms
# against that. --offline because a deploy only needs our own files to be consistent.
if ! "$REPO_ROOT/tools/validate-parts.py" --offline; then
    echo >&2
    echo "error: the part assets did not validate; not deploying." >&2
    echo "       run ./tools/validate-parts.py for the full report." >&2
    exit 1
fi

# The build output is deliberately just the mod itself: game assemblies are referenced with
# Private=false so we never ship a second copy alongside the running game's.
mkdir -p "$TARGET"

# KSA holds the assembly open while it runs, so the copy fails with a bare "Permission denied"
# that looks like a filesystem problem rather than "close the game".
if ! cp "$OUT/KSArmory.dll" "$TARGET/" 2>/dev/null; then
    echo "error: could not write KSArmory.dll to the mods folder." >&2
    if pgrep -f 'StarMap.exe|KSA.exe' >/dev/null 2>&1 || tasklist.exe 2>/dev/null | grep -qi '^KSA.exe'; then
        echo "       KSA is still running and has the file locked -- close the game and retry." >&2
    else
        echo "       Check permissions on $TARGET" >&2
    fi
    exit 1
fi
cp "$OUT/mod.toml" "$TARGET/"
[[ -f "$OUT/KSArmory.pdb" ]] && cp "$OUT/KSArmory.pdb" "$TARGET/"

# Part definitions and the art they reference. Paths here must match the assets array in
# mod.toml and the <MeshAtlas>/<PbrMaterial> paths inside KSArmoryAssets.xml. A missing
# texture is a silent in-game failure, so copy the folders wholesale rather than by name.
cp "$OUT"/KSArmory*.xml "$TARGET/"
for dir in Meshes Textures; do
    if [[ -d "$OUT/$dir" ]]; then
        mkdir -p "$TARGET/$dir"
        cp "$OUT/$dir"/* "$TARGET/$dir/"
    else
        echo "warning: $dir/ missing from the build output -- the part will render untextured" >&2
    fi
done

# An older layout put the XML under Assets/. Leaving it there means two copies of the part
# fighting for the same Id.
if [[ -d "$TARGET/Assets" ]]; then
    rm -rf "$TARGET/Assets"
    echo "removed stale $TARGET/Assets"
fi

echo "installed to $TARGET"

# KSA discovers mods through manifest.toml, and StarMap walks the same list to find code mods.
# Dropping the folder in is not enough -- without an entry here nothing loads.
MANIFEST="$(dirname "$MODS_DIR")/manifest.toml"

if [[ ! -f "$MANIFEST" ]]; then
    echo "warning: no manifest at $MANIFEST -- register the mod by hand" >&2
elif grep -q '^[[:space:]]*id[[:space:]]*=[[:space:]]*"KSArmory"' "$MANIFEST"; then
    echo "already registered in $(basename "$MANIFEST")"
else
    cp "$MANIFEST" "$MANIFEST.bak"
    printf '\n[[mods]]\nid = "KSArmory"\nenabled = true\n' >> "$MANIFEST"
    echo "registered in $(basename "$MANIFEST") (backup: $(basename "$MANIFEST").bak)"
fi

echo
echo "now launch StarMap.exe (not KSA.exe). See tools/setup-starmap.sh if it isn't installed."
