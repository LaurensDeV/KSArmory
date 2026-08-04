#!/usr/bin/env bash
#
# Regenerates the launcher's art: palette textures, then the mesh atlas.
#
#   ./tools/model/build.sh                       # build, render previews, install into the mod
#   ./tools/model/build.sh --previews            # renders only, leave the committed atlas alone
#   ./tools/model/build.sh --pose elev=0         # ...posed the way the drives would pose it
#   ./tools/model/build.sh --previews --pose elev=0,bearing=90
#
# Without --pose the previews show the modelled rest pose, which is the one pose the game never
# displays once the drives are running. A defect that only appears at another elevation - an
# assembly sweeping through its own mount, say - is invisible without it. Angles are degrees.
#
# --pose implies --previews: a posed scene exports a posed atlas, and the runtime composes poses
# itself from the rest library.
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
POSE=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --previews) PREVIEWS_ONLY=1 ;;
        --pose)     POSE="${2:-}"; shift ;;
        --pose=*)   POSE="${1#--pose=}" ;;
        *)          echo "unknown option: $1" >&2; exit 2 ;;
    esac
    shift
done

# Posing moves the assemblies in the scene, so the export would carry that pose in its node
# transforms. It is a way of looking at the model, never a way of producing one.
if [[ -n "$POSE" && $PREVIEWS_ONLY -eq 0 ]]; then
    echo "--pose renders only; the committed atlas is left alone"
    PREVIEWS_ONLY=1
fi

EXPORT_FLAG=()
[[ $PREVIEWS_ONLY -eq 1 ]] && EXPORT_FLAG=(--no-export)

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
    -- "$WIN_WORK" "$WIN_WORK\\palette.json" "$WIN_WORK\\AirDefence_Diffuse.png" "$POSE" \
    "${EXPORT_FLAG[@]}" \
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
