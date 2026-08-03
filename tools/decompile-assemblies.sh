#!/usr/bin/env bash
#
# Decompiles the referenced KSA assemblies into the private ksa-game-assemblies checkout, so
# that a KSA update produces a readable `git diff` of what actually changed in the game.
#
#   ./tools/decompile-assemblies.sh ../ksa-game-assemblies
#
# Run it alongside sync-assemblies.sh after a KSA update, then commit in that repository. The
# diff between two of its commits is the upgrade report - see the upgrade-ksa skill.
#
# Why bother when the compiler already tells you what broke: it doesn't, not all of it. A
# renamed member is a build error and you find it in seconds. A member that kept its name and
# changed its *meaning* - different units, a different frame of reference, an enum whose
# members were reordered, a method that started returning normalised values - compiles clean and
# is then wrong in flight. Those are only visible in the source, and this repository has already
# been bitten by exactly that class of thing in its own code.
#
# ILSpy's output is deterministic for a given assembly and ilspycmd version, verified by
# decompiling twice and diffing, so a diff between two runs is real change and not churn.
#
# Two things make it churn anyway, and both produce a diff far too large to read:
#
#   - Upgrading ilspycmd. Pin one version across updates.
#   - Changing which assemblies are in current/dll. The decompiler resolves cross-assembly
#     types against its -r path, so a type that was an unresolved reference before comes out
#     with a real name after. Widening the mirror from 8 assemblies to 44 moved KSA.dll from
#     250,187 lines to 224,074 without the game changing at all.
#
# If you have to do either, do it in its own commit with nothing else in it, so the next real
# update still diffs cleanly against something.
#
# THE OUTPUT IS ROCKETWERKZ'S COPYRIGHTED CODE IN SOURCE FORM. It goes in the private repository
# and nowhere else. This script refuses to write anywhere inside this public repository.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=/dev/null
source "$REPO_ROOT/tools/env.sh"

TARGET="${1:-}"
if [[ -z "$TARGET" ]]; then
    echo "usage: $(basename "$0") <path to ksa-game-assemblies checkout>" >&2
    exit 2
fi
[[ -d "$TARGET/.git" ]] || { echo "error: $TARGET is not a git checkout" >&2; exit 1; }

TARGET="$(cd "$TARGET" && pwd)"
# Decompiled game source must never land in the public repository, including via a symlink or a
# checkout nested inside it.
if [[ "$TARGET" == "$REPO_ROOT" || "$TARGET" == "$REPO_ROOT"/* ]]; then
    echo "error: $TARGET is inside this public repository." >&2
    echo "       Decompiled KSA source is RocketWerkz's copyrighted code and cannot go here." >&2
    exit 1
fi

command -v ilspycmd >/dev/null 2>&1 || {
    echo "error: ilspycmd not found" >&2
    echo "       dotnet tool install -g ilspycmd" >&2
    exit 1
}

DLL_DIR="$TARGET/current/dll"
[[ -f "$DLL_DIR/KSA.dll" ]] || {
    echo "error: no assemblies in $DLL_DIR" >&2
    echo "       run ./tools/sync-assemblies.sh $1 first" >&2
    exit 1
}

# Whatever the mirror holds, not whatever this mod references. The private repository is a
# general KSA SDK mirror that other mods build against too, and a corpus covering only
# AirDefence's eight assemblies would be useless to them - and would quietly stop covering this
# mod the moment it referenced something new.
mapfile -t NAMES < <(find "$DLL_DIR" -maxdepth 1 -name '*.dll' -printf '%f\n' | sed 's/\.dll$//' | sort)
[[ ${#NAMES[@]} -gt 0 ]] || { echo "error: no assemblies in $DLL_DIR" >&2; exit 1; }

SRC="$TARGET/current/src"
echo "decompiling ${#NAMES[@]} assemblies with $(ilspycmd --version | head -1)"
echo
failures=0

for name in "${NAMES[@]}"; do
    dll="$DLL_DIR/$name.dll"
    [[ -f "$dll" ]] || { echo "  skip $name (not in current/dll)"; continue; }

    out="$SRC/$name"
    # Wipe first: without it a type deleted by a KSA update lingers as a stale file and the diff
    # shows the removal as nothing at all.
    rm -rf "$out"
    mkdir -p "$out"

    # -r points the decompiler at the sibling assemblies so cross-assembly types resolve to real
    # names instead of being emitted as unresolved references.
    #
    # StackAllocInitializers=false is not a preference. ILSpy 10.1 throws "given Block is
    # invalid!" on KSA.PartModelRenderer.DepthData.CreateCsmPipeline, and in project mode one
    # unhandled method aborts the entire assembly - which silently cost us KSA.dll, the only one
    # that really matters, on the first run. Disabling the pattern emits that method as plain
    # IL-faithful C# and the other 1321 files come out fine. Applied to every assembly rather
    # than just KSA so the corpus stays consistent to diff.
    if ! err="$(ilspycmd -p -o "$out" -r "$DLL_DIR" -ds StackAllocInitializers=false "$dll" 2>&1 >/dev/null)"; then
        echo "  FAILED $name" >&2
        printf '%s\n' "$err" | head -3 | sed 's/^/      /' >&2
        failures=$((failures + 1))
        continue
    fi
    files=$(find "$out" -name '*.cs' | wc -l)
    lines=$(find "$out" -name '*.cs' -exec cat {} + 2>/dev/null | wc -l)
    printf '  %-28s %5s files  %8s lines\n' "$name" "$files" "$lines"
done

# The version these sources came from. sync-assemblies.sh writes the same file; keep them in step.
build="$(cat "$TARGET/current/KSA_BUILD" 2>/dev/null || echo unknown)"

if [[ ! -f "$TARGET/README.md" ]]; then
    cat > "$TARGET/README.md" <<'EOF'
# ksa-game-assemblies (private)

Kitten Space Agency's assemblies and their decompiled sources, kept so that the AirDefence mod
can be built by CI and so that a KSA update produces a readable diff of what changed.

**This repository must stay private.** These are RocketWerkz's copyrighted game files, both the
binaries and the decompiled source. Keeping a licensed copy for your own builds is fine;
publishing it is not.

    current/dll/     the eight assemblies the mod references
    current/src/     those assemblies decompiled, one folder each
    current/KSA_BUILD    the game build they came from

Both halves are refreshed from a machine with the game installed, from the mod repository:

    ./tools/sync-assemblies.sh       ../ksa-game-assemblies   # binaries
    ./tools/decompile-assemblies.sh  ../ksa-game-assemblies   # sources
    # set current/KSA_BUILD, then commit both together

Committing them together is what makes the next update diffable: `git diff` between two of
those commits is the list of game changes, and the mod's `upgrade-ksa` skill reads it against
`docs/KSA-API-SURFACE.md` to find the ones that matter.
EOF
    echo
    echo "  wrote README.md (this repository must stay private)"
fi

echo
echo "decompiled from build $build into $SRC"
echo "commit in $TARGET together with current/dll, so the pair stays consistent."

# A partial corpus is worse than none: the missing assembly's changes are invisible in the next
# diff, and nothing about the output says so.
if (( failures )); then
    echo >&2
    echo "error: $failures assembl$( (( failures == 1 )) && echo y || echo ies) failed to decompile." >&2
    echo "       Do not commit this corpus - the next upgrade diff would silently omit them." >&2
    exit 1
fi
