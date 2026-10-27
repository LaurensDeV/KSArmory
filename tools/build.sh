#!/usr/bin/env bash
#
# Builds the mod with the right SDK on PATH.
#
#     ./tools/build.sh            # Release
#     ./tools/build.sh Debug
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=env.sh
source "$REPO_ROOT/tools/env.sh"

# Silent unless the installed game has moved off ksa-assemblies.lock, which makes Import/ and the
# private repo CI builds against stale.
"$REPO_ROOT/tools/check-assemblies.sh" --game --quiet || true

CONFIG="${1:-Release}"

# MSBuild accepts any string as a configuration, so `./tools/build.sh ./tools/run.sh` would build
# one called "./tools/run.sh" and report success.
case "$CONFIG" in
    Debug|Release) ;;
    *)
        echo "error: '$CONFIG' is not a build configuration (expected Debug or Release)" >&2
        echo "       to build and launch the game, run ./tools/run.sh on its own" >&2
        exit 2
        ;;
esac

dotnet build "$REPO_ROOT/src/KSArmory/KSArmory.csproj" -c "$CONFIG" --nologo
