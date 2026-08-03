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

dotnet build "$REPO_ROOT/src/AirDefence/AirDefence.csproj" -c "${1:-Release}" --nologo
