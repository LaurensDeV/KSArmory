#!/usr/bin/env bash
#
# Checks the facts in the prose against the things that produce them.
#
#     ./tools/check-docs.sh            # check, and fail on a mismatch
#     ./tools/check-docs.sh --report   # report only, never fail
#
# Every number here has been wrong at least once, in a file whose own rule is that a stale line
# is worse than a missing one. They drift because they are hand-copied from output that moves:
# the test count, the API surface, the layout table, the KSA build.
#
# The rule this enforces in practice: do not write a generated figure into prose without a check
# that reads it back. Judgement-bearing prose is out of scope and always will be - this catches
# the mechanical half, which is the half that actually rots.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT" || exit 1

REPORT_ONLY=0
[[ "${1:-}" == "--report" ]] && REPORT_ONLY=1

FAIL=0

fail() {
    echo "  $1"
    FAIL=$((FAIL + 1))
}

# --- the layout table lists every source file -------------------------------
#
# A file absent from the table is invisible to anyone reading CLAUDE.md to find their way around,
# which is the table's only job. Sight.cs - a headline feature - was missing for two releases.

echo "Layout table covers src/"

# Table rows only, not the whole file. Searching all of CLAUDE.md passes a file that prose
# happens to name while its row is missing, which is the failure this check exists to catch --
# it did, for WarpPolicy.cs, in the same session the check was written.
rows="$(grep '^|' CLAUDE.md || true)"

missing=""
while IFS= read -r path; do
    grep -qF "$(basename "$path")" <<< "$rows" || missing+="    $path"$'\n'
done < <(find src/KSArmory/Sim src/KSArmory/Ksa -name '*.cs' | sed 's|src/KSArmory/||' | sort)

if [[ -n "$missing" ]]; then
    echo "$missing"
    fail "these source files are in no row of CLAUDE.md's Layout table"
fi

# And the other direction. A row naming a file that no longer exists is the worse half of the
# same problem -- coverage alone stays green when a file is deleted, and the row is then trusted.
stale=""
while IFS= read -r path; do
    [[ -e "$path" || -e "src/KSArmory/$path" ]] || stale+="    $path"$'\n'
done < <(grep -oE '`(src/|tools/|docs/|tests/|Sim/|Ksa/)[A-Za-z0-9_./*-]+`' <<< "$rows" \
         | tr -d '`' | grep -v '[*]' | sort -u)

if [[ -n "$stale" ]]; then
    echo "$stale"
    fail "CLAUDE.md's Layout table names these, and they do not exist"
fi

# --- the API surface counts -------------------------------------------------
#
# docs/KSA-API-SURFACE.md is generated and states its own totals on line 10. Any other file
# quoting a member or type count is quoting it from memory.

if [[ -f docs/KSA-API-SURFACE.md ]]; then
    echo "API surface counts"
    surface_line="$(sed -n '10p' docs/KSA-API-SURFACE.md)"
    types="$(sed -nE 's/^([0-9]+) types.*/\1/p' <<< "$surface_line")"
    members="$(sed -nE 's/.* and ([0-9]+) members.*/\1/p' <<< "$surface_line")"

    # Exclusions are anchored to the path, not matched anywhere in the line: the layout row that
    # carries the member count also names KSA-API-SURFACE.md, so a loose -v drops the one line
    # this check exists for. It did, and the check passed against a count it never read.
    if [[ -n "$members" ]]; then
        while IFS= read -r hit; do
            [[ -z "$hit" ]] && continue
            grep -qE "\b$members members\b" <<< "$hit" || fail "stale member count: $hit"
        done < <(grep -rnE "\b[0-9]+ members\b" CLAUDE.md docs/*.md .claude 2>/dev/null \
                 | grep -vE "^docs/(KSA-API-SURFACE|AUDIT-)" || true)
    fi

    if [[ -n "$types" ]]; then
        while IFS= read -r hit; do
            [[ -z "$hit" ]] && continue
            grep -qE "\b$types types\b" <<< "$hit" || fail "stale type count: $hit"
        done < <(grep -rnE "\b[0-9]+ types this mod\b" CLAUDE.md docs/*.md .claude 2>/dev/null \
                 | grep -vE "^docs/(KSA-API-SURFACE|AUDIT-)" || true)
    fi
fi

# --- no hand-written test count ---------------------------------------------
#
# Deliberately not checked against a number: 19 [MemberData] theories expand at run time, so
# Fact+InlineData counts 315 against the runner's 353 and no static count can ever be right.
# The fix is not a cleverer count - it is not writing one down. Guidance prose says "the suite".
#
# docs/ is exempt: MODULARITY.md's "117 -> 203 tests" is a historical measurement and true.

echo "No hand-written test count in guidance"
while IFS= read -r hit; do
    [[ -z "$hit" ]] && continue
    fail "quote the suite, not a count that drifts: $hit"
done < <(grep -rnE "\b[0-9]{2,} tests\b" CLAUDE.md README.md CONTRIBUTING.md .claude 2>/dev/null || true)

# --- the KSA build, in every place it is written down -----------------------
#
# ksa-assemblies.lock is the one CI enforces; the others are for humans and drift silently.

echo "KSA build number"
if [[ -f ksa-assemblies.lock ]]; then
    build="$(awk '/^build /{print $2}' ksa-assemblies.lock)"
    if [[ -n "$build" ]]; then
        while IFS= read -r f; do
            [[ -f "$f" ]] || continue
            if grep -qE "20[0-9]{2}\.[0-9]+\.[0-9]+\.[0-9]+" "$f" \
               && ! grep -qF "$build" "$f"; then
                fail "$f names a KSA build that is not $build (the lock)"
            fi
        done <<< "CLAUDE.md
docs/KSA-MODDING-NOTES.md
docs/BLOCKED-ON-KSA.md
README.md"
    fi
fi

# Nobody's home directory belongs in a public repository, and a hardcoded one is wrong for
# everyone else anyway. `tools/ksa-user-dir.sh` and STARMAP_DIR are the portable answers.
# A bare /mnt/c/Users is fine -- that is a search root -- as is one continuing into a variable.
echo "No personal paths"
while IFS= read -r hit; do
    [[ -n "$hit" ]] && fail "personal path: $hit"
done < <(git grep -nIE '(/mnt/c/Users|/home)/[A-Za-z0-9._-]+' \
             -- . ':!tools/check-docs.sh' 2>/dev/null || true)

echo
if [[ $FAIL -eq 0 ]]; then
    echo "docs ok"
    exit 0
fi

if (( REPORT_ONLY )); then
    echo "$FAIL check(s) would fail; --report given, not failing"
    exit 0
fi

echo "$FAIL doc check(s) failed"
exit 1
