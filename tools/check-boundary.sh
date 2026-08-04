#!/usr/bin/env bash
#
# Checks that src/KSArmory/Sim stays free of KSA types.
#
# The real guard is the test project: it links Sim/** and references no KSA assembly, so a
# `using KSA;` there fails the test build. That guard needs the game's assemblies to run, which
# a hosted CI runner does not have — so this is the textual stand-in, and it is the reason CI
# can say anything at all about the boundary.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SIM="$REPO_ROOT/src/KSArmory/Sim"

if [[ ! -d "$SIM" ]]; then
    echo "error: $SIM does not exist" >&2
    exit 1
fi

# `using KSA;`, `KSA.Something`, and the loader's attributes. Comments and doc text are allowed
# to name KSA — half the value of those files is explaining why they do not touch it.
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
