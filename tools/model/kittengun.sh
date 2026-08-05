#!/usr/bin/env bash
#
# Builds the kitten's shoulder cannon and installs it into the mod.
#
#   ./tools/model/kittengun.sh
#
# Separate from build.sh because this is not a part and does not belong in the part atlas: KSA
# hangs it off a character's bone through CharacterAttachmentReference, so it ships as its own
# glTF beside Core's helmet and MMU attachments.
#
# Blender is a Windows binary, so the script path and every output path have to be Windows
# paths, and it cannot write into the WSL tree reliably -- hence the temp folder and the copy
# back. Same dance as build.sh.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BLENDER="${BLENDER:-/mnt/c/Program Files/Blender Foundation/Blender 5.2/blender.exe}"

if [[ ! -x "$BLENDER" ]]; then
    echo "error: Blender not found at '$BLENDER'" >&2
    echo "       set BLENDER to your install and retry" >&2
    exit 1
fi

WIN_WORK='C:\Windows\Temp\kittengun'
WORK="$(wslpath -u "$WIN_WORK")"
mkdir -p "$WORK"

OUT_NAME="KittenGun.glb"
MESH_DIR="$REPO_ROOT/src/KSArmory/Meshes"

"$BLENDER" --background --python "$(wslpath -w "$REPO_ROOT/tools/model/kittengun.py")" -- \
    "$WIN_WORK\\$OUT_NAME" \
    "$(wslpath -w "$REPO_ROOT/tools/model/palette.json")" \
    | grep -vE "^(Blender|Read prefs|found bundled|Warning:)" || true

if [[ ! -f "$WORK/$OUT_NAME" ]]; then
    echo "error: Blender produced no $OUT_NAME" >&2
    exit 1
fi

mkdir -p "$MESH_DIR"
cp "$WORK/$OUT_NAME" "$MESH_DIR/$OUT_NAME"
chmod u+rw "$MESH_DIR/$OUT_NAME"

echo "installed $MESH_DIR/$OUT_NAME"

# The two defects that are invisible outside the game, and are the whole reason this check
# exists. checkmesh.py exits non-zero on either.
"$REPO_ROOT/tools/model/checkmesh.py" "$MESH_DIR/$OUT_NAME" --units-per-metre 100
