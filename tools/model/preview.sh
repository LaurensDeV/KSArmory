#!/usr/bin/env bash
#
# Renders an exported .glb from a few angles, so an authored asset can be judged before it is
# declared.
#
#   ./tools/model/preview.sh src/KSArmory/Meshes/KSArmory_Amraam.glb
#   ./tools/model/preview.sh <atlas.glb> <outdir>          # somewhere other than the temp folder
#   ./tools/model/preview.sh <atlas.glb> <outdir> <mesh>   # one body out of the atlas
#
# This exists because Blender is a Windows binary: it needs a Windows path for the script it runs
# *and* for everywhere it writes, so calling preview-glb.py by hand means two wslpath conversions
# and a backslash-quoting trap. The rendered files are reported back as WSL paths.
#
# It renders what is *in the file*, node transforms and all. A multi-body atlas therefore shows
# each body in its own export frame, so a rail lying along +Y and its round along +X will cross
# each other on screen. That is the file being read honestly rather than a fault in the model.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BLENDER="${BLENDER:-/mnt/c/Program Files/Blender Foundation/Blender 5.2/blender.exe}"

if [[ $# -lt 1 ]]; then
    sed -n '3,12p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//' >&2
    exit 2
fi

SOURCE="$1"
WIN_OUT="${2:-C:\\Windows\\Temp\\airdefence-preview}"
ONLY="${3:-}"

if [[ ! -f "$SOURCE" ]]; then
    echo "error: no such file: $SOURCE" >&2
    exit 1
fi

if [[ ! -x "$BLENDER" ]]; then
    echo "error: Blender not found at $BLENDER" >&2
    echo "       set BLENDER to your blender.exe and retry" >&2
    exit 1
fi

# A caller who passes a WSL path for the output means it, so convert rather than refuse: Blender
# cannot write there reliably, but wslpath gives the UNC form and Blender can be told to try.
case "$WIN_OUT" in
    /*) WIN_OUT="$(wslpath -w "$WIN_OUT")" ;;
esac

OUT="$(wslpath -u "$WIN_OUT")"
mkdir -p "$OUT"

"$BLENDER" --background \
    --python "$(wslpath -w "$REPO_ROOT/tools/model/preview-glb.py")" \
    -- "$(wslpath -w "$SOURCE")" "$WIN_OUT" "$ONLY" \
    2>&1 | grep -E '^(BOUNDS|RENDER|DONE|Error|error)' || true

echo
echo "previews in $OUT"
ls -1 "$OUT" | sed 's/^/  /'
