#!/usr/bin/env bash
#
# Reports how much of each source file is comment, and fails on the two things that are
# mechanically detectable.
#
#     ./tools/check-comments.sh            # report, and fail on a violation
#     ./tools/check-comments.sh --report   # report only, never fail
#
# The rules themselves are in CLAUDE.md under "Comments and documentation". Most of them need
# judgement and cannot be checked. These two can:
#
#   * history in a comment - commit hashes, "reported from play", "used to", and friends
#   * XML doc blocks on private members, which no tooling surfaces
#
# The ratio is reported rather than enforced. A hard threshold would be wrong for a file that is
# mostly interface, and right for one that is mostly arithmetic; what matters is noticing it move.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT" || exit 1

REPORT_ONLY=0
[[ "${1:-}" == "--report" ]] && REPORT_ONLY=1

FAIL=0

# --- history in comments ----------------------------------------------------

HISTORY='(reported from play|regression, commit|commit [0-9a-f]{7}|used to (be|run|live|sit|fall|advance|assume|report|do)|already happened twice|duly did|for months|first draft|on its first run|was rewritten|the old code|before (it|this|that|the mode) existed|before it, a |before that, )'

echo "History in comments"
hits="$(grep -rniE "^\s*(//|///|\*)" src tests --include='*.cs' 2>/dev/null \
        | grep -iE "$HISTORY" || true)"

if [[ -n "$hits" ]]; then
    echo "$hits" | sed 's/^/  /' | cut -c1-140
    echo
    echo "  A comment states what is true now. What broke, when, and which commit fixed it"
    echo "  belongs in git and in docs/. See CLAUDE.md, 'Comments and documentation'."
    FAIL=$((FAIL + 1))
else
    echo "  none"
fi

# --- XML docs on private members --------------------------------------------

echo
echo "XML doc blocks on private members"
private_docs="$(python3 - <<'PY'
import pathlib
found = []
for p in sorted(pathlib.Path("src").rglob("*.cs")):
    lines = p.read_text().splitlines()
    i = 0
    while i < len(lines):
        if lines[i].strip().startswith("/// <summary>"):
            j = i
            while j < len(lines) and lines[j].strip().startswith("///"):
                j += 1
            decl = lines[j].strip() if j < len(lines) else ""
            if decl.startswith("private "):
                found.append(f"{p}:{i+1}  {decl[:70]}")
            i = j
        else:
            i += 1
print("\n".join(found))
PY
)"

if [[ -n "$private_docs" ]]; then
    echo "$private_docs" | sed 's/^/  /'
    echo
    echo "  GenerateDocumentationFile is off and these members are private, so /// reaches"
    echo "  no tooling. Use a plain // comment."
    FAIL=$((FAIL + 1))
else
    echo "  none"
fi

# --- ratio, reported only ---------------------------------------------------

echo
echo "Comment ratio by file"
python3 - <<'PY'
import pathlib, re
rows = []
tot_c = tot_l = 0
for p in sorted(pathlib.Path("src").rglob("*.cs")):
    lines = p.read_text().splitlines()
    code = [l for l in lines if l.strip()]
    com = [l for l in code if re.match(r"\s*(//|/\*|\*)", l)]
    if not code:
        continue
    tot_c += len(com); tot_l += len(code)
    rows.append((len(com) * 100 // len(code), len(com), len(code), p.name))
for pct, c, t, name in sorted(rows, reverse=True)[:8]:
    print(f"  {pct:3d}%  {c:4d}/{t:4d}  {name}")
print(f"\n  overall {tot_c * 100 // tot_l}%  ({tot_c}/{tot_l} non-blank lines)")
PY

echo
if [[ $FAIL -eq 0 ]]; then
    echo "comments ok"
    exit 0
fi

if (( REPORT_ONLY )); then
    echo "$FAIL check(s) would fail; --report given, not failing"
    exit 0
fi

echo "$FAIL comment check(s) failed"
exit 1
