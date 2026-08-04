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

dotnet build "$REPO_ROOT/src/KSArmory/KSArmory.csproj" -c "${1:-Release}" --nologo
