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

# Notice a KSA update at the earliest possible moment - the first build after it happened.
# Silent unless the installed game has moved away from ksa-assemblies.lock, in which case
# everything downstream (Import/, the private repo CI builds against) is now stale.
"$REPO_ROOT/tools/check-assemblies.sh" --game --quiet || true

CONFIG="${1:-Release}"

# The argument is a build configuration, and MSBuild accepts any string as one -- so a mistyped
# `./tools/build.sh ./tools/run.sh` builds a configuration called "./tools/run.sh", drops the
# assembly under bin/tools/run.sh/, deploys nothing, and reports success. Refusing is the only
# thing that separates that from a real build.
case "$CONFIG" in
    Debug|Release) ;;
    *)
        echo "error: '$CONFIG' is not a build configuration (expected Debug or Release)" >&2
        echo "       to build and launch the game, run ./tools/run.sh on its own" >&2
        exit 2
        ;;
esac

dotnet build "$REPO_ROOT/src/KSArmory/KSArmory.csproj" -c "$CONFIG" --nologo
