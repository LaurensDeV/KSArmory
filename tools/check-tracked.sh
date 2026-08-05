#!/usr/bin/env bash
#
# Refuses to let build artefacts or the game's assemblies live in the repository.
#
#     ./tools/check-tracked.sh
#
# Two separate hazards. A committed .dll or .pdb would be republishing RocketWerkz's copyrighted
# files, which is the one mistake here with consequences outside this project. And .gitignore does
# not untrack what is already tracked, so anything committed before its rule existed stays forever
# without anyone noticing -- which is how tools/__pycache__/meshinfo.cpython-312.pyc survived
# being ignored.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT" || exit 1

fail=0

if hits="$(git ls-files | grep -E '\.(dll|pdb)$')"; then
    echo "$hits" | sed 's/^/  /'
    echo "error: game assemblies must not be committed - see .gitignore" >&2
    fail=1
fi

if hits="$(git ls-files | grep -E '(^|/)__pycache__/|\.pyc$')"; then
    echo "$hits" | sed 's/^/  /'
    echo "error: build artefacts are tracked; git rm --cached them" >&2
    fail=1
fi

if (( fail )); then
    exit 1
fi

echo "tracked files ok: no assemblies or build artefacts"
