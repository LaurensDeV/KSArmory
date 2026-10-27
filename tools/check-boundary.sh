#!/usr/bin/env bash
#
# Checks that src/KSArmory/Sim stays free of KSA types.
#
# The real guard is the test project, which links Sim/** and references no KSA assembly; it needs
# the game's assemblies to build, so this is the textual stand-in for a runner without them.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SIM="$REPO_ROOT/src/KSArmory/Sim"

if [[ ! -d "$SIM" ]]; then
    echo "error: $SIM does not exist" >&2
    exit 1
fi

# `using KSA;` and `KSA.Something`. Comment lines may name KSA.
offenders="$(grep -rnE '^\s*(using KSA;|using KSA\.)|(^|[^A-Za-z_."/])KSA\.[A-Z]' "$SIM" --include='*.cs' \
    | grep -vE '^\s*[^:]+:[0-9]+:\s*(//|///|\*)' || true)"

if [[ -n "$offenders" ]]; then
    echo "Sim/ must not reference KSA types:" >&2
    echo "$offenders" >&2
    echo >&2
    echo "Move the KSA-facing part into src/KSArmory/Ksa/ and keep the maths in Sim/." >&2
    exit 1
fi

count=$(find "$SIM" -name '*.cs' | wc -l)
echo "boundary ok: $count file(s) under Sim/ are free of KSA types"
