#!/usr/bin/env bash
#
# Runs the headless guidance and fuse tests. No game required.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=env.sh
source "$REPO_ROOT/tools/env.sh"

dotnet test "$REPO_ROOT/tests/AirDefence.Tests/AirDefence.Tests.csproj" --nologo "$@"
