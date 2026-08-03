#!/usr/bin/env bash
#
# Points git at the hooks committed in .githooks/.
#
#   ./tools/install-hooks.sh
#
# Git will not use a repository's hooks without being told to - they live in .git/hooks, which
# is not version controlled. core.hooksPath moves that to a tracked directory, so a hook added
# later arrives with a pull instead of needing everyone to reinstall it.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

git -C "$REPO_ROOT" config core.hooksPath .githooks
chmod +x "$REPO_ROOT"/.githooks/*

echo "hooks enabled from .githooks/"
for hook in "$REPO_ROOT"/.githooks/*; do
    echo "  $(basename "$hook")"
done
echo
echo "bypass a single commit with: git commit --no-verify"
