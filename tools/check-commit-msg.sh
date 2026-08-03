#!/usr/bin/env bash
#
# Validates commit messages against Conventional Commits.
#
#   ./tools/check-commit-msg.sh .git/COMMIT_EDITMSG   # one message file (the git hook)
#   ./tools/check-commit-msg.sh --range origin/main..HEAD
#   ./tools/check-commit-msg.sh --text "feat: do a thing"
#
# semantic-release parses these to decide the next version, so a message that does not parse
# produces no release and never reaches the changelog. That failure is silent, which is exactly
# why it is worth catching here.
#
# One script for both the local hook and CI, so the two can never disagree about what is legal.
#
set -euo pipefail

TYPES="feat|fix|perf|build|revert|docs|refactor|test|chore|ci|style"

# type(optional scope)optional !: subject
HEADER_RE="^(${TYPES})(\([a-z0-9._/-]+\))?!?: .+"
MAX_HEADER=72

fail=0

check() {
    local header="$1" origin="$2"

    # Machine-generated and in-progress commits are none of our business: merges, reverts,
    # semantic-release's own release commit, and the fixup/squash markers rebase consumes.
    case "$header" in
        Merge\ *|Revert\ *|fixup!\ *|squash!\ *|amend!\ *) return 0 ;;
    esac

    if [[ ! "$header" =~ $HEADER_RE ]]; then
        echo "✗ $origin" >&2
        echo "    $header" >&2
        echo "    does not match: <type>(<scope>)!: <subject>" >&2
        echo "    type is one of: ${TYPES//|/, }" >&2
        fail=1
        return 0
    fi

    if (( ${#header} > MAX_HEADER )); then
        echo "✗ $origin" >&2
        echo "    $header" >&2
        echo "    subject line is ${#header} characters; keep it under $MAX_HEADER" >&2
        fail=1
        return 0
    fi

    echo "✓ $header"
}

mode="${1:-}"
case "$mode" in
    --range)
        range="${2:?usage: --range <rev range>}"
        # A range can legitimately not resolve: the first commit in a repository has no parent,
        # and a freshly pushed branch has no "before". Checking the tip is the useful thing to
        # do then, rather than failing on a git error nobody can act on.
        if ! git rev-list "$range" >/dev/null 2>&1; then
            echo "note: '$range' does not resolve; checking HEAD only" >&2
            while IFS=$'\t' read -r sha subject; do
                [[ -n "$sha" ]] || continue
                check "$subject" "${sha:0:8}"
            done < <(git log --no-merges -1 --format='%H%x09%s')
        else
            # Subjects only; %s is the first line.
            while IFS=$'\t' read -r sha subject; do
                [[ -n "$sha" ]] || continue
                check "$subject" "${sha:0:8}"
            done < <(git log --no-merges --format='%H%x09%s' "$range")
        fi
        ;;
    --text)
        check "${2:?usage: --text <message>}" "message"
        ;;
    "")
        echo "usage: $(basename "$0") <message-file> | --range <range> | --text <message>" >&2
        exit 2
        ;;
    *)
        [[ -f "$mode" ]] || { echo "no such file: $mode" >&2; exit 2; }
        # First line only, and ignore the comment block git appends to the template.
        check "$(grep -v '^#' "$mode" | sed '/^$/d' | head -1)" "$(basename "$mode")"
        ;;
esac

if (( fail )); then
    cat >&2 <<'EOF'

Conventional Commits, e.g.:
    feat(turret): elevate the pods on their trunnions
    fix(rounds): anchor bodies to the tube they left
    docs: write an install guide

feat -> minor, fix -> patch, docs/chore/ci/test/style/refactor -> no release.
See the Committing section of CLAUDE.md.
EOF
    exit 1
fi
