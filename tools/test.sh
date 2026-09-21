#!/usr/bin/env bash
#
# Runs the headless guidance and fuse tests. No game required.
#
#     ./tools/test.sh                     # the guards -- what a push is gated on, ~32 s
#     ./tools/test.sh --studies           # ...only the instruments, ~30 s, which CI runs separately
#     ./tools/test.sh --all               # both, which is what a release wants
#     ./tools/test.sh Debug               # with the optimiser off, to step through one
#     ./tools/test.sh --filter Deorbit    # anything else goes to `dotnet test`
#
# Release because the suite is arithmetic: it flies whole trajectories and asserts on millimetres,
# and the optimiser is worth 7x on the heaviest of them -- 344 s to 60 s over the whole suite. It
# is also the configuration the shipped mod compiles Sim/ in, so Debug was testing codegen nobody
# ships. Nothing under Sim/ is conditioned on DEBUG, so the two differ in speed and floating-point
# contraction and in nothing else.
#
# `kind=study` is the ten tests that measure a term of the flight model and write the finding out
# for a reader rather than asserting a behaviour. They are 28 s of a 60 s suite and assert nothing
# but that the flight completed, so they buy a local push almost nothing and cost it half the wait.
# Same trade `check-all.sh` already makes for the drive sweep, and the same answer: out of the loop
# you run on every push, and a CI step of its own so nothing goes unrun before a merge.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=env.sh
source "$REPO_ROOT/tools/env.sh"

# Only a leading Debug/Release is a configuration; everything else is `dotnet test`'s, so
# `./tools/test.sh --filter X` still reaches the runner unchanged.
CONFIG=Release
case "${1:-}" in
    Debug|Release) CONFIG="$1"; shift ;;
esac

SELECT=("--filter" "kind!=study")
case "${1:-}" in
    --studies) SELECT=("--filter" "kind=study"); shift ;;
    --all)     SELECT=();                        shift ;;
esac

# A --filter of the caller's own replaces the selection rather than being added to it: dotnet test
# takes the last --filter and silently drops the earlier one, so passing both would quietly run
# something other than what was asked for.
for arg in "$@"; do
    [[ "$arg" == --filter || "$arg" == --filter=* ]] && SELECT=() && break
done

dotnet test "$REPO_ROOT/tests/KSArmory.Tests/KSArmory.Tests.csproj" \
    -c "$CONFIG" --nologo "${SELECT[@]+"${SELECT[@]}"}" "$@"
