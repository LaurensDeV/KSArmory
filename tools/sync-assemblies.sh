#!/usr/bin/env bash
#
# Refreshes the private ksa-game-assemblies checkout from a local KSA install.
#
#   ./tools/sync-assemblies.sh ../ksa-game-assemblies
#   KSA_DIR=/path/to/KSA ./tools/sync-assemblies.sh ../ksa-game-assemblies
#
# Run this after a KSA update, then commit and push in that repository. CI compiles against it,
# so until it is refreshed CI is building against the previous game build.
#
# Only the assemblies the projects actually reference are copied - verified as the minimum that
# both builds the mod and runs its tests. Keeping the set small is deliberate: these are
# RocketWerkz's copyrighted files, kept privately for our own builds and never redistributed.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSA_DIR="${KSA_DIR:-/mnt/c/Program Files/Kitten Space Agency}"
# StarMap.API.dll comes from the loader, not the game. The Windows username rarely matches the
# Linux one, so search rather than assume.
find_starmap() {
    [[ -n "${STARMAP_DIR:-}" ]] && { printf '%s\n' "$STARMAP_DIR"; return 0; }
    local hit
    hit="$(find /mnt/c/Users "$HOME" -maxdepth 4 -iname 'StarMap.API.dll' 2>/dev/null | head -1)"
    [[ -n "$hit" ]] && { dirname "$hit"; return 0; }
    return 1
}
STARMAP_DIR="$(find_starmap || true)"

TARGET="${1:-}"
if [[ -z "$TARGET" ]]; then
    echo "usage: $(basename "$0") <path to ksa-game-assemblies checkout>" >&2
    exit 2
fi
[[ -d "$TARGET/.git" ]] || { echo "error: $TARGET is not a git checkout" >&2; exit 1; }

# What to mirror.
#
# The default is every RocketWerkz first-party assembly plus the loader and the third-party
# libraries the game ships, so the private repository is a general KSA SDK mirror that *any*
# mod can build against - not just this one. That is ~44 assemblies and under 10 MB; the
# alternative, mirroring only what this mod's csproj references, meant the next mod started by
# discovering its assemblies were missing.
#
# Deliberately excluded: the .NET runtime (System.*, Microsoft.*), which every SDK already has.
# That is what keeps this at 10 MB instead of 45.
#
# --subset restores the old minimal behaviour: only what this repository's projects reference.
# Smaller, and a defensible position on holding less of someone else's copyrighted code, but
# only useful for this one mod.
SUBSET=0
[[ "${2:-}" == "--subset" ]] && SUBSET=1

referenced="$(grep -ohP '(?<=\$\(KsaDllDir\))[A-Za-z0-9._]+(?=\.dll)' \
    "$REPO_ROOT"/src/KSArmory/*.csproj "$REPO_ROOT"/tests/*/*.csproj | sort -u)"

# Game-shipped third-party. Taken from the install rather than NuGet on purpose: the game is
# pre-release and binds to the exact build it ships, so a NuGet package of the same nominal
# version is not guaranteed to be the same assembly.
EXTRAS=(BepuUtilities BepuPhysics MathNet.Numerics MemoryPack.Core
        CommunityToolkit.HighPerformance Tomlet 0Harmony)

ASSEMBLIES=()
if (( SUBSET )); then
    mapfile -t ASSEMBLIES <<< "$referenced"
else
    while IFS= read -r path; do
        name="$(basename "$path" .dll)"
        case "$name" in
            KSA*|Brutal.*|Planet.*) ASSEMBLIES+=("$name") ;;
        esac
    done < <(find "$KSA_DIR" -maxdepth 1 -name '*.dll' 2>/dev/null | sort)

    for name in "${EXTRAS[@]}"; do
        [[ -f "$KSA_DIR/$name.dll" ]] && ASSEMBLIES+=("$name")
    done

    # The loader ships separately from the game.
    while IFS= read -r path; do
        ASSEMBLIES+=("$(basename "$path" .dll)")
    done < <(find "${STARMAP_DIR:-/nonexistent}" -maxdepth 1 -name 'StarMap.*.dll' 2>/dev/null | sort)
fi

[[ ${#ASSEMBLIES[@]} -gt 0 ]] || { echo "error: nothing to mirror; is $KSA_DIR a KSA install?" >&2; exit 1; }

# Whatever the mode, this repository's own references must all be covered - otherwise the next
# CI run fails on an assembly nobody noticed was absent.
missing=""
while IFS= read -r name; do
    [[ -n "$name" ]] || continue
    printf '%s\n' "${ASSEMBLIES[@]}" | grep -qx "$name" || missing+=" $name"
done <<< "$referenced"
if [[ -n "$missing" ]]; then
    echo "error: this repository references assemblies the mirror would not contain:$missing" >&2
    echo "       add them to EXTRAS and re-run" >&2
    exit 1
fi

DEST="$TARGET/current/dll"
mkdir -p "$DEST"

copied=0
for name in "${ASSEMBLIES[@]}"; do
    src="$KSA_DIR/$name.dll"
    # StarMap ships its own API assembly, not the game.
    [[ -f "$src" ]] || src="$STARMAP_DIR/$name.dll"
    if [[ ! -f "$src" ]]; then
        echo "error: could not find $name.dll in the KSA install or StarMap" >&2
        echo "       looked in: $KSA_DIR and $STARMAP_DIR" >&2
        exit 1
    fi
    cp "$src" "$DEST/$name.dll"
    chmod u+rw "$DEST/$name.dll"
    copied=$((copied + 1))
done

# Record which game build these came from; the mod is only known to work against one at a time.
# There is no machine-readable version in the install, so this is maintained by hand.
[[ -f "$TARGET/current/KSA_BUILD" ]] || printf 'unknown\n' > "$TARGET/current/KSA_BUILD"

echo "copied $copied assembl$([ $copied -eq 1 ] && echo y || echo ies) to $DEST"
echo "current build recorded as: $(cat "$TARGET/current/KSA_BUILD")"
echo
echo "update $TARGET/current/KSA_BUILD if the game version changed, then commit and push there."
