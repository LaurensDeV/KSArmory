#!/usr/bin/env bash
#
# Downloads StarMap, installs it on the Windows side, and points it at your KSA install.
# Run once. Afterwards you launch the game with StarMap.exe instead of KSA.exe.
#
#     ./tools/setup-starmap.sh
#     STARMAP_INSTALL_DIR=/mnt/c/Games/StarMap ./tools/setup-starmap.sh
#
# StarMap must live on the Windows filesystem (/mnt/...), not inside the WSL filesystem --
# it is a Windows executable and has to be able to launch the game.
#
set -euo pipefail

KSA_DIR="${KSA_DIR:-/mnt/c/Program Files/Kitten Space Agency}"

if [[ ! -f "$KSA_DIR/KSA.dll" ]]; then
    echo "error: KSA not found at '$KSA_DIR'" >&2
    echo "       set KSA_DIR and retry" >&2
    exit 1
fi

# Default to a folder beside the user's KSA data, so no admin rights are needed.
if [[ -z "${STARMAP_INSTALL_DIR:-}" ]]; then
    WIN_USER_DIR="$(find /mnt/c/Users -maxdepth 4 -type d -path '*My Games/Kitten Space Agency' 2>/dev/null | head -1)"
    if [[ -z "$WIN_USER_DIR" ]]; then
        echo "error: could not find your KSA user folder; set STARMAP_INSTALL_DIR" >&2
        exit 1
    fi
    # .../Users/<name>/Documents/My Games/Kitten Space Agency -> .../Users/<name>/StarMap
    STARMAP_INSTALL_DIR="$(cd "$WIN_USER_DIR/../../.." && pwd)/StarMap"
fi

echo "installing StarMap to $STARMAP_INSTALL_DIR"
mkdir -p "$STARMAP_INSTALL_DIR"

# Asset naming changed at 0.4.6, which merged the separate launcher and standalone builds into
# a single StarMap-<version>.zip. Prefer a standalone-named asset if one exists (<= 0.4.5),
# otherwise take the only zip on offer.
ASSETS="$(curl -s https://api.github.com/repos/StarMapLoader/StarMap/releases/latest \
    | grep -oP '"browser_download_url":\s*"\K[^"]+')"

ASSET_URL="$(echo "$ASSETS" | grep -i 'standalone' | head -1 || true)"
[[ -z "$ASSET_URL" ]] && ASSET_URL="$(echo "$ASSETS" | grep -iE '\.zip$' | grep -vi 'launcher' | head -1 || true)"

if [[ -z "$ASSET_URL" ]]; then
    echo "error: could not find a StarMap release zip" >&2
    echo "       download it by hand from https://github.com/StarMapLoader/StarMap/releases" >&2
    exit 1
fi

echo "downloading $(basename "$ASSET_URL")"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
curl -sL -o "$TMP/starmap.zip" "$ASSET_URL"

# Unzipping over an old install leaves files the new release no longer ships. 0.4.6 merged the
# separate launcher process away, so StarMap.Loader.* from 0.4.5 would sit there looking live.
# Set aside anything not in the new archive rather than deleting it.
if [[ -f "$STARMAP_INSTALL_DIR/StarMap.exe" ]]; then
    NEW_FILES="$(unzip -Z1 "$TMP/starmap.zip")"
    STALE_DIR="$STARMAP_INSTALL_DIR/_superseded"
    for existing in "$STARMAP_INSTALL_DIR"/*.dll "$STARMAP_INSTALL_DIR"/*.json; do
        [[ -f "$existing" ]] || continue
        name="$(basename "$existing")"
        [[ "$name" == "StarMapConfig.json" ]] && continue
        if ! grep -qxF "$name" <<< "$NEW_FILES"; then
            mkdir -p "$STALE_DIR"
            mv "$existing" "$STALE_DIR/"
            echo "  superseded, moved to _superseded/: $name"
        fi
    done
fi

unzip -o -q "$TMP/starmap.zip" -d "$STARMAP_INSTALL_DIR"

# Zip entries carry no exec bit, and /mnt/c is mounted with metadata, so the extracted
# StarMap.exe comes out 0644 and WSL refuses to run it ("Permission denied").
chmod +x "$STARMAP_INSTALL_DIR"/*.exe 2>/dev/null || true

# StarMapConfig.json wants a Windows path. Translate, and escape backslashes for JSON.
if command -v wslpath >/dev/null 2>&1; then
    WIN_KSA_DIR="$(wslpath -w "$KSA_DIR")"
else
    WIN_KSA_DIR="$(echo "$KSA_DIR" | sed 's|^/mnt/\(.\)|\U\1:|; s|/|\\|g')"
fi
JSON_KSA_DIR="${WIN_KSA_DIR//\\/\\\\}"

CONFIG="$STARMAP_INSTALL_DIR/StarMapConfig.json"
if [[ -f "$CONFIG" ]]; then
    echo "keeping existing StarMapConfig.json"
else
    cat > "$CONFIG" <<EOF
{
  "GameLocation": "$JSON_KSA_DIR",
  "RepositoryLocation": "",
  "GameArguments": []
}
EOF
    echo "wrote StarMapConfig.json -> $WIN_KSA_DIR"
fi

echo
echo "done. Launch the game with:"
echo "  \"$STARMAP_INSTALL_DIR/StarMap.exe\""
echo
echo "or from Explorer, open ${WIN_KSA_DIR%\\*}\\..\\StarMap and double-click StarMap.exe"
