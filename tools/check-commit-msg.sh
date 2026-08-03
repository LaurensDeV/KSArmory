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

# Warns when a commit will cut a release that changes nothing a player installs.
#
# `feat` means "feature" to semantic-release whatever the scope says, so `feat(tools)` on a
# developer script bumps the *mod's* minor version and publishes an archive identical to the
# last one but for the version string. That happened twice in one day: 0.1.1, 0.2.0 and 0.3.0
# differ only in <Version>, and anyone who upgraded got nothing.
#
# A warning rather than a rule, and scope is deliberately not used to decide. A packaging fix in
# tools/package.sh changes the artifact without touching src/ at all, so any mechanical rule
# gets that backwards. The author knows which it is; this only asks the question.
advise_release_impact() {
    local header="$1"
    local type="${header%%[(:!]*}"

    case "$type" in feat|fix|perf) ;; *) return 0 ;; esac

    # Staged contents, which is what this commit will contain.
    local touched
    touched="$(git diff --cached --name-only 2>/dev/null || true)"
    [[ -n "$touched" ]] || return 0
    printf '%s\n' "$touched" | grep -q '^src/AirDefence/' && return 0

    local bump="patch"; [[ "$type" == feat ]] && bump="minor"
    echo >&2
    echo "note: this cuts a $bump release, but nothing under src/AirDefence/ changed." >&2
    echo "      The published archive will differ from the last only by its version number." >&2
    echo "      If this is developer tooling, chore/ci/test/refactor cut no release." >&2
    echo "      If it changes what gets packaged, ignore this — that is the exception." >&2
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
        subject="$(grep -v '^#' "$mode" | sed '/^$/d' | head -1)"
        check "$subject" "$(basename "$mode")"
        # Only in hook mode: the staged set is this commit's contents, and the author is still
        # here to act on it. In --range mode the commit already exists and the advice is noise.
        (( fail )) || advise_release_impact "$subject"
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
