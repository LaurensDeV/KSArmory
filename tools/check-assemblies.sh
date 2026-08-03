#!/usr/bin/env bash
#
# Checks that a set of KSA assemblies matches the one this repository is known to build against.
#
#   ./tools/check-assemblies.sh                 # check whatever the build would resolve
#   ./tools/check-assemblies.sh <dir>           # check a specific folder
#   ./tools/check-assemblies.sh <dir> --update  # record that folder as the new expectation
#
# The assemblies exist in two places that can drift apart: your local Import/ (or game install),
# and the private ksa-game-assemblies repository CI compiles against. A KSA update refreshes the
# first and not the second, and nothing about that is visible - CI keeps building against the
# old game while you build against the new one, and the mismatch only surfaces as behaviour
# nobody can reproduce.
#
# ksa-assemblies.lock records the expected digests. Only hashes and names, so it is safe in a
# public repository - it says nothing about the contents.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOCK="$REPO_ROOT/ksa-assemblies.lock"

DIR="${1:-}"
UPDATE=0
for arg in "$@"; do [[ "$arg" == "--update" ]] && UPDATE=1; done
[[ "$DIR" == "--update" ]] && DIR=""

# Mirror Directory.Build.props' resolution order, so this checks what the build would use.
if [[ -z "$DIR" ]]; then
    for candidate in \
        "${KSA_DLL_DIR:-}" \
        "$REPO_ROOT/Import" \
        "$REPO_ROOT/../ksa-game-assemblies/current/dll" \
        "/mnt/c/Program Files/Kitten Space Agency" \
        "C:/Program Files/Kitten Space Agency"
    do
        [[ -n "$candidate" && -f "$candidate/KSA.dll" ]] && { DIR="$candidate"; break; }
    done
fi

[[ -n "$DIR" && -d "$DIR" ]] || { echo "error: no assembly folder found; pass one" >&2; exit 1; }

# The set the projects actually reference, from the csproj files, so this cannot fall behind.
mapfile -t NAMES < <(grep -ohP '(?<=\$\(KsaDllDir\))[A-Za-z0-9._]+(?=\.dll)' \
    "$REPO_ROOT"/src/AirDefence/*.csproj "$REPO_ROOT"/tests/*/*.csproj | sort -u)
[[ ${#NAMES[@]} -gt 0 ]] || { echo "error: no \$(KsaDllDir) references found in the csproj files" >&2; exit 1; }

digests() {
    for name in "${NAMES[@]}"; do
        local path="$DIR/$name.dll"
        if [[ ! -f "$path" ]]; then
            echo "error: $name.dll missing from $DIR" >&2
            return 1
        fi
        printf '%s  %s.dll\n' "$(sha256sum "$path" | cut -d' ' -f1)" "$name"
    done
}

if (( UPDATE )); then
    build="$(grep -oP '(?<=^build ).*' "$LOCK" 2>/dev/null || echo unknown)"
    {
        echo "# KSA assemblies this repository is known to build against."
        echo "# Regenerate with ./tools/check-assemblies.sh <dir> --update after a KSA update,"
        echo "# and refresh the private ksa-game-assemblies repo to match - see CLAUDE.md."
        echo "build $build"
        digests
    } > "$LOCK"
    echo "recorded ${#NAMES[@]} assemblies from $DIR"
    echo "update the 'build' line in $(basename "$LOCK") if the game version changed"
    exit 0
fi

if [[ ! -f "$LOCK" ]]; then
    echo "error: $LOCK does not exist; create it with --update" >&2
    exit 1
fi

actual="$(digests)"
expected="$(grep -v '^#' "$LOCK" | grep -v '^build ' | grep -v '^$')"

if [[ "$actual" == "$expected" ]]; then
    echo "assemblies match the lock ($(grep -oP '(?<=^build ).*' "$LOCK"), ${#NAMES[@]} files)"
    echo "  $DIR"
    exit 0
fi

echo "assemblies in $DIR do not match $(basename "$LOCK"):" >&2
diff <(printf '%s\n' "$expected") <(printf '%s\n' "$actual") \
    | grep -E '^[<>]' | sed 's/^</  expected/; s/^>/  found   /' >&2
cat >&2 <<EOF

The local assemblies and the ones CI builds against have drifted. After a KSA update:

  ./tools/sync-import.sh                                   # refresh Import/ from the game
  ./tools/sync-assemblies.sh ../ksa-game-assemblies        # refresh the private repo
  (commit and push there)
  ./tools/check-assemblies.sh --update                     # record the new expectation
  (commit ksa-assemblies.lock here)
EOF
exit 1
