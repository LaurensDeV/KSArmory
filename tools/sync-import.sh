#!/usr/bin/env bash
#
# Copies the KSA game assemblies and the StarMap API into ./Import, which the mod and the
# api-dump tool compile against. Import/ is gitignored: these are the game's binaries.
#
# Re-run this after a KSA update -- the game's API is pre-release and moves between builds.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMPORT_DIR="$REPO_ROOT/Import"

# Default install path under WSL. Override with: KSA_DIR=/some/path ./tools/sync-import.sh
KSA_DIR="${KSA_DIR:-/mnt/c/Program Files/Kitten Space Agency}"

if [[ ! -d "$KSA_DIR" ]]; then
    echo "error: KSA not found at '$KSA_DIR'" >&2
    echo "       set KSA_DIR to your install directory and retry" >&2
    exit 1
fi

mkdir -p "$IMPORT_DIR"

echo "syncing game assemblies from $KSA_DIR"
for pattern in 'KSA.dll' 'Brutal.*.dll' 'BepuPhysics.dll' 'BepuUtilities.dll' \
               'Planet.*.dll' 'MemoryPack.Core.dll' 'Tomlet.dll' \
               'CommunityToolkit.HighPerformance.dll' 'MathNet.Numerics.dll'; do
    # shellcheck disable=SC2086
    cp "$KSA_DIR"/$pattern "$IMPORT_DIR/" 2>/dev/null || true
done

# StarMap.API.dll ships with the StarMap loader, not with the game.
if [[ -n "${STARMAP_DIR:-}" && -f "$STARMAP_DIR/StarMap.API.dll" ]]; then
    echo "syncing StarMap API from $STARMAP_DIR"
    cp "$STARMAP_DIR"/StarMap.API.dll "$IMPORT_DIR/"
    cp "$STARMAP_DIR"/0Harmony.dll "$IMPORT_DIR/" 2>/dev/null || true
elif [[ ! -f "$IMPORT_DIR/StarMap.API.dll" ]]; then
    echo
    echo "warning: StarMap.API.dll is missing from Import/." >&2
    echo "         Download a StarMap release from" >&2
    echo "         https://github.com/StarMapLoader/StarMap/releases" >&2
    echo "         and either copy StarMap.API.dll into Import/, or re-run with" >&2
    echo "         STARMAP_DIR=/path/to/starmap ./tools/sync-import.sh" >&2
fi

echo "done: $(find "$IMPORT_DIR" -name '*.dll' | wc -l) assemblies in Import/"

# Right after a KSA update is the moment this drifts: Import/ is now the new game, while CI is
# still compiling against the old assemblies in the private repo. Say so here rather than
# leaving it to be discovered as behaviour nobody can reproduce.
echo
if ! "$REPO_ROOT/tools/check-assemblies.sh" "$IMPORT_DIR" 2>&1; then
    echo
    echo "warning: the game assemblies changed. CI is still on the old ones until you" >&2
    echo "         refresh the private repo - the steps are printed above." >&2
fi
