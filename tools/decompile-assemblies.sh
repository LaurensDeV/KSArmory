#!/usr/bin/env bash
#
# Decompiles the referenced KSA assemblies into the private ksa-game-assemblies checkout, so
# that a KSA update produces a readable `git diff` of what actually changed in the game.
#
#   ./tools/decompile-assemblies.sh ../ksa-game-assemblies
#
# Run it alongside sync-assemblies.sh after a KSA update, then commit in that repository; the diff
# between two of its commits is the upgrade report (the upgrade-ksa skill). It catches what the
# compiler cannot: a member that keeps its name and changes its meaning.
#
# ILSpy's output is deterministic for a given assembly and ilspycmd version, so a diff between two
# runs is real change. Two things churn it anyway: upgrading ilspycmd, and changing which assemblies
# are in current/dll, since cross-assembly types resolve against that set (8 assemblies to 44 took
# KSA.dll from 250,187 lines to 224,074 with no game change). Do either in a commit of its own.
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

# Whatever the mirror holds, not only this mod's references: it is a general KSA SDK other mods
# build against. Less third-party libraries that publish their own source, which apisurface never
# tracks and so ksa-api-diff.sh never reads; 0Harmony alone is 138k lines. Their DLLs stay in
# current/dll, which is still the resolver directory below.
SKIP_DECOMPILE=(0Harmony)

mapfile -t NAMES < <(find "$DLL_DIR" -maxdepth 1 -name '*.dll' -printf '%f\n' | sed 's/\.dll$//' | sort)
for skip in "${SKIP_DECOMPILE[@]}"; do
    for i in "${!NAMES[@]}"; do
        [[ ${NAMES[$i]} == "$skip" ]] && unset 'NAMES[$i]'
    done
done
NAMES=("${NAMES[@]}")
[[ ${#NAMES[@]} -gt 0 ]] || { echo "error: no assemblies in $DLL_DIR" >&2; exit 1; }

SRC="$TARGET/current/src"
# A folder for an assembly the mirror no longer holds is a renamed or dropped one, and left in
# place it reads as current source.
for dir in "$SRC"/*/; do
    [[ -d "$dir" ]] || continue
    name="$(basename "$dir")"
    [[ -f "$DLL_DIR/$name.dll" ]] || { echo "removing src/$name: not in current/dll"; rm -rf "$dir"; }
done

echo "decompiling ${#NAMES[@]} assemblies with $(ilspycmd --version | head -1)"
echo
failures=0

for name in "${NAMES[@]}"; do
    dll="$DLL_DIR/$name.dll"
    [[ -f "$dll" ]] || { echo "  skip $name (not in current/dll)"; continue; }

    out="$SRC/$name"
    # Wipe first, or a type a KSA update deleted lingers and the diff shows no removal.
    rm -rf "$out"
    mkdir -p "$out"

    # -r resolves cross-assembly types to real names. StackAllocInitializers=false: ILSpy 10.1
    # throws "given Block is invalid!" on KSA.PartModelRenderer.DepthData.CreateCsmPipeline, and in
    # project mode that aborts the whole of KSA.dll. Applied to every assembly so the corpus diffs
    # consistently.
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

build="$(cat "$TARGET/current/KSA_BUILD" 2>/dev/null || echo unknown)"

if [[ ! -f "$TARGET/README.md" ]]; then
    cat > "$TARGET/README.md" <<'EOF'
# ksa-game-assemblies (private)

Kitten Space Agency's assemblies and their decompiled sources, kept so that the KSArmory mod
can be built by CI and so that a KSA update produces a readable diff of what changed.

**This repository must stay private.** These are RocketWerkz's copyrighted game files, both the
binaries and the decompiled source. Keeping a licensed copy for your own builds is fine;
publishing it is not.

    current/dll/     every assembly the game and the loader ship, so any KSA mod can build
    current/src/     those decompiled, one folder each, less third-party libraries that
                     publish their own source - see SKIP_DECOMPILE in decompile-assemblies.sh
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

# A partial corpus hides the missing assembly's changes from the next diff.
if (( failures )); then
    echo >&2
    echo "error: $failures assembl$( (( failures == 1 )) && echo y || echo ies) failed to decompile." >&2
    echo "       Do not commit this corpus - the next upgrade diff would silently omit them." >&2
    exit 1
fi
