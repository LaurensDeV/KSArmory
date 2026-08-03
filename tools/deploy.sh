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

# StarMap reads mods from the user's My Games folder as well as the install directory.
MODS_DIR="${KSA_MODS_DIR:-/mnt/c/Users/$(whoami)/Documents/My Games/Kitten Space Agency/mods}"

# Fall back to scanning for the folder if the username does not match the Windows one.
if [[ ! -d "$(dirname "$MODS_DIR")" ]]; then
    FOUND="$(find /mnt/c/Users -maxdepth 4 -type d -path '*My Games/Kitten Space Agency' 2>/dev/null | head -1)"
    if [[ -n "$FOUND" ]]; then
        MODS_DIR="$FOUND/mods"
    else
        echo "error: could not locate the KSA user folder." >&2
        echo "       set KSA_MODS_DIR to <My Games>/Kitten Space Agency/mods and retry" >&2
        exit 1
    fi
fi

TARGET="$MODS_DIR/AirDefence"

echo "building ($CONFIG)..."
dotnet build "$REPO_ROOT/src/AirDefence/AirDefence.csproj" -c "$CONFIG" --nologo

OUT="$REPO_ROOT/src/AirDefence/bin/$CONFIG/net10.0"

# The build output is deliberately just the mod itself: game assemblies are referenced with
# Private=false so we never ship a second copy alongside the running game's.
mkdir -p "$TARGET"

# KSA holds the assembly open while it runs, so the copy fails with a bare "Permission denied"
# that looks like a filesystem problem rather than "close the game".
if ! cp "$OUT/AirDefence.dll" "$TARGET/" 2>/dev/null; then
    echo "error: could not write AirDefence.dll to the mods folder." >&2
    if pgrep -f 'StarMap.exe|KSA.exe' >/dev/null 2>&1 || tasklist.exe 2>/dev/null | grep -qi '^KSA.exe'; then
        echo "       KSA is still running and has the file locked -- close the game and retry." >&2
    else
        echo "       Check permissions on $TARGET" >&2
    fi
    exit 1
fi
cp "$OUT/mod.toml" "$TARGET/"
[[ -f "$OUT/AirDefence.pdb" ]] && cp "$OUT/AirDefence.pdb" "$TARGET/"

# Part definitions and the art they reference. Paths here must match the assets array in
# mod.toml and the <MeshAtlas>/<PbrMaterial> paths inside AirDefenceAssets.xml. A missing
# texture is a silent in-game failure, so copy the folders wholesale rather than by name.
cp "$OUT"/AirDefence*.xml "$TARGET/"
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
elif grep -q '^[[:space:]]*id[[:space:]]*=[[:space:]]*"AirDefence"' "$MANIFEST"; then
    echo "already registered in $(basename "$MANIFEST")"
else
    cp "$MANIFEST" "$MANIFEST.bak"
    printf '\n[[mods]]\nid = "AirDefence"\nenabled = true\n' >> "$MANIFEST"
    echo "registered in $(basename "$MANIFEST") (backup: $(basename "$MANIFEST").bak)"
fi

echo
echo "now launch StarMap.exe (not KSA.exe). See tools/setup-starmap.sh if it isn't installed."
