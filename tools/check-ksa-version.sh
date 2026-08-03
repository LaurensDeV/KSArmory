#!/usr/bin/env bash
#
# Asks RocketWerkz's master server what the current KSA build is, and compares it to the one
# this repository is pinned to.
#
#   ./tools/check-ksa-version.sh          # report, exit 1 if a newer build is out
#   ./tools/check-ksa-version.sh --quiet  # say nothing unless it has moved
#
# This is the only check that can notice an update *before* it is installed - everything else
# compares assemblies, which requires having the new ones already. A scheduled CI job runs this
# and opens an issue when the answer changes.
#
# Deliberately not wired into build.sh: a network call on every build is slow, breaks offline,
# and would be the first thing anyone disabled.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOCK="$REPO_ROOT/ksa-assemblies.lock"
ENDPOINT="${KSA_VERSION_URL:-http://ksa-master1.rocketwerkz.com:8082/version}"

QUIET=0
[[ "${1:-}" == "--quiet" ]] && QUIET=1

pinned="$(grep -oP '(?<=^build ).*' "$LOCK" 2>/dev/null || true)"
[[ -n "$pinned" ]] || { echo "error: no 'build' line in $LOCK" >&2; exit 1; }

# A master server that is down, moved, or slow is not a reason to fail a build or a scheduled
# job. Report and succeed.
if ! response="$(curl -sS --max-time 20 "$ENDPOINT" 2>&1)"; then
    (( QUIET )) || echo "could not reach $ENDPOINT; skipping" >&2
    exit 0
fi

latest="$(printf '%s' "$response" | python3 -c '
import json, sys
try:
    print(json.load(sys.stdin).get("Version", ""))
except Exception:
    pass
' 2>/dev/null || true)"

if [[ -z "$latest" ]]; then
    (( QUIET )) || {
        echo "could not read a version from $ENDPOINT; skipping" >&2
        printf '%s\n' "$response" | head -5 >&2
    }
    exit 0
fi

if [[ "$latest" == "$pinned" ]]; then
    (( QUIET )) || echo "KSA $latest — up to date with ksa-assemblies.lock"
    exit 0
fi

cat >&2 <<EOF
KSA has been updated upstream.

  published: $latest
  pinned:    $pinned

The mod is built and tested against the pinned build. To move:

  (install the update, then)
  ./tools/sync-import.sh                              # refresh Import/
  ./tools/sync-assemblies.sh ../ksa-game-assemblies   # refresh the private repo, commit, push
  ./tools/check-assemblies.sh --update                # record the new digests
  #   set 'build $latest' in ksa-assemblies.lock, and update CLAUDE.md's Environment line
  ./tools/test.sh                                     # the API moves between builds
EOF
exit 1
