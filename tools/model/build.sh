#!/usr/bin/env bash
#
# Regenerates the launcher's art: palette textures, then the mesh atlas.
#
#   ./tools/model/build.sh              # build, render previews, install into the mod
#   ./tools/model/build.sh --previews   # renders only, leave the committed atlas alone
#
# Blender is a Windows binary, so the script path and every output path it is given have to be
# Windows paths. It also cannot write into the WSL tree reliably, so it works in a temp folder
# and the results are copied back here.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BLENDER="${BLENDER:-/mnt/c/Program Files/Blender Foundation/Blender 5.2/blender.exe}"

WIN_WORK='C:\Windows\Temp\airdefence-model'
WORK="$(wslpath -u "$WIN_WORK")"

PREVIEWS_ONLY=0
[[ "${1:-}" == "--previews" ]] && PREVIEWS_ONLY=1

if [[ ! -x "$BLENDER" ]]; then
    echo "error: Blender not found at $BLENDER" >&2
    echo "       set BLENDER to your blender.exe and retry" >&2
    exit 1
fi

echo "generating palette textures..."
"$REPO_ROOT/tools/model/palette.py"

mkdir -p "$WORK"
cp "$REPO_ROOT/src/KSArmory/Textures/AirDefence_Diffuse.png" "$WORK/"
cp "$REPO_ROOT/tools/model/palette.json" "$WORK/"

echo
echo "building mesh (headless Blender)..."
"$BLENDER" --background \
    --python "$(wslpath -w "$REPO_ROOT/tools/model/pantsir.py")" \
    -- "$WIN_WORK" "$WIN_WORK\\palette.json" "$WIN_WORK\\AirDefence_Diffuse.png" \
    2>&1 | grep -v '^Fra:' | sed '/^$/d'

# The muzzle table validate-parts.py checks LauncherPart.cs against.
cp "$WORK/muzzles.json" "$REPO_ROOT/tools/model/"

if [[ $PREVIEWS_ONLY -eq 0 ]]; then
    mkdir -p "$REPO_ROOT/src/KSArmory/Meshes"
    cp "$WORK/AirDefence_MeshAtlas.glb" "$REPO_ROOT/src/KSArmory/Meshes/"
    echo
    echo "installed src/KSArmory/Meshes/AirDefence_MeshAtlas.glb"
    "$REPO_ROOT/tools/meshinfo.py" "$REPO_ROOT/src/KSArmory/Meshes/AirDefence_MeshAtlas.glb"

    # Zero-UV-area triangles and coplanar faces both render as flickering speckle in game and
    # are both invisible in the preview renders above. Fail the build rather than ship either.
    echo
    "$REPO_ROOT/tools/model/checkmesh.py" "$REPO_ROOT/src/KSArmory/Meshes/AirDefence_MeshAtlas.glb"
fi

echo
echo "previews in $WORK (Windows: $WIN_WORK)"
