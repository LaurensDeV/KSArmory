#!/usr/bin/env bash
#
# Checks whether this machine can build, test and run the mod, and says what to do about
# whatever it cannot.
#
#     ./tools/doctor.sh
#
# Written for someone who has just cloned the repository. Every check prints what it looked for
# and, on failure, the one command that fixes it - the alternative is a newcomer bisecting
# NETSDK1045 or a wall of "type or namespace not found" against a README.
#
# Exit code is the number of things that are actually broken. Optional pieces (a game install, a
# Blender for the model pipeline) warn rather than fail: plenty of useful work needs neither.
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT" || exit 1

FAIL=0
WARN=0

# Colour only when someone is watching, so piping to a file or CI stays readable.
if [[ -t 1 ]]; then
    R=$'\033[31m'; G=$'\033[32m'; Y=$'\033[33m'; D=$'\033[2m'; Z=$'\033[0m'
else
    R=''; G=''; Y=''; D=''; Z=''
fi

ok()   { printf '  %sok%s    %s\n' "$G" "$Z" "$1"; }
bad()  { printf '  %sFAIL%s  %s\n' "$R" "$Z" "$1"; FAIL=$((FAIL + 1)); }
warn() { printf '  %swarn%s  %s\n' "$Y" "$Z" "$1"; WARN=$((WARN + 1)); }
hint() { printf '        %s%s%s\n' "$D" "$1" "$Z"; }
head_() { printf '\n%s\n' "$1"; }

# --- platform ---------------------------------------------------------------

head_ "Platform"

UNAME="$(uname -s 2>/dev/null || echo unknown)"
if grep -qi microsoft /proc/version 2>/dev/null; then
    PLATFORM="wsl"
    ok "WSL ($UNAME) - the game is launchable from here via tools/run.sh"
elif [[ "$UNAME" == MINGW* || "$UNAME" == MSYS* || "$UNAME" == CYGWIN* ]]; then
    PLATFORM="gitbash"
    ok "Windows, under $UNAME"
    hint "build, tests, packaging and deploy work; launch the game with StarMap.exe directly"
    hint "tools/run.sh and the model pipeline assume WSL paths - set KSA_DIR / STARMAP_DIR if you use them"

    # The one that stops everything, checked before anything else can fail confusingly. CRLF in a
    # shell script makes bash read the \r as part of the command: `$'\r': command not found`,
    # which names neither the file nor the cause.
    if [[ -n "$(find tools -name '*.sh' -exec grep -lU $'\r' {} + 2>/dev/null | head -1)" ]]; then
        bad "shell scripts have CRLF line endings - bash cannot run them"
        hint "git config core.autocrlf input && git rm --cached -r . >/dev/null && git reset --hard"
        hint ".gitattributes now forces LF, so a fresh clone will be correct"
    else
        ok "shell scripts have LF line endings"
    fi
elif [[ "$UNAME" == "Linux" ]]; then
    PLATFORM="linux"
    ok "native Linux"
    hint "build, tests and packaging all work; tools/run.sh and the Blender pipeline are WSL/Windows only"
elif [[ "$UNAME" == "Darwin" ]]; then
    PLATFORM="macos"
    ok "macOS"
    hint "build and tests work; the game, run.sh and the Blender pipeline are not available here"
else
    PLATFORM="other"
    warn "unrecognised platform '$UNAME' - the build should still work"
fi

# --- the SDK ----------------------------------------------------------------

head_ ".NET SDK"

# env.sh returns non-zero and explains itself when there is no net10; swallow its output so we
# can print our own, then report.
if (source tools/env.sh) >/dev/null 2>&1; then
    # shellcheck disable=SC1091
    source tools/env.sh >/dev/null 2>&1
    ok ".NET 10 SDK available ($(dotnet --version 2>/dev/null || echo 'version unknown'))"
    [[ -n "${DOTNET_ROOT:-}" ]] && hint "using $DOTNET_ROOT"
else
    bad "no .NET 10 SDK - the mod targets net10.0 and a distro dotnet 8 fails with NETSDK1045"
    hint "curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir \$HOME/.dotnet"
fi

# --- the game's assemblies --------------------------------------------------

head_ "KSA assemblies (needed to compile against the game)"

FOUND_DLL=""
for candidate in \
    "${KSA_DLL_DIR:-}" \
    "${KSA_DIR:-}" \
    "$REPO_ROOT/Import" \
    "$REPO_ROOT/../ksa-game-assemblies/current/dll" \
    "C:/Program Files/Kitten Space Agency" \
    "/c/Program Files/Kitten Space Agency" \
    "$HOME/.steam/steam/steamapps/common/Kitten Space Agency" \
    "$HOME/.local/share/Steam/steamapps/common/Kitten Space Agency" \
    "$HOME/Games/Kitten Space Agency" \
    "$HOME/Kitten Space Agency" \
    "/mnt/c/Program Files/Kitten Space Agency"
do
    [[ -n "$candidate" && -f "$candidate/KSA.dll" ]] && { FOUND_DLL="$candidate"; break; }
done

if [[ -n "$FOUND_DLL" ]]; then
    ok "found at $FOUND_DLL"
    [[ "$FOUND_DLL" != "$REPO_ROOT/Import" ]] && \
        hint "./tools/sync-import.sh copies them into Import/ so the build stops searching"
else
    bad "not found - the game-facing half cannot compile without them"
    hint "own KSA?  KSA_DLL_DIR=/path/to/Kitten\\ Space\\ Agency ./tools/build.sh"
    hint "or clone the private ksa-game-assemblies mirror next to this repository"
    hint "they are RocketWerkz's copyrighted files: keep a copy, never commit or publish one"
fi

# --- the build and the tests ------------------------------------------------

head_ "Build and tests"

if [[ $FAIL -eq 0 ]]; then
    if ./tools/build.sh >/dev/null 2>&1; then
        ok "./tools/build.sh"
    else
        bad "./tools/build.sh failed - run it directly to see why"
    fi

    if TEST_OUT="$(./tools/test.sh 2>&1)"; then
        COUNT="$(printf '%s' "$TEST_OUT" | grep -oE 'Passed: +[0-9]+' | grep -oE '[0-9]+' | tail -1)"
        ok "./tools/test.sh (${COUNT:-?} tests, no game required)"
    else
        bad "./tools/test.sh failed - run it directly to see why"
    fi
else
    warn "skipped: fix the failures above first"
fi

# --- repository hygiene -----------------------------------------------------

head_ "Repository setup"

if [[ "$(git config core.hooksPath 2>/dev/null)" == ".githooks" ]] || [[ -x .githooks/commit-msg ]]; then
    ok "commit-msg hook installed"
else
    warn "commit-msg hook not installed - a message that does not parse silently cuts no release"
    hint "./tools/install-hooks.sh"
fi

if command -v shellcheck >/dev/null 2>&1; then
    ok "shellcheck available (CI runs it at -S warning)"
else
    warn "shellcheck not installed - CI will still check, you just will not see it first"
fi

if command -v python3 >/dev/null 2>&1; then
    ok "python3 ($(python3 --version 2>&1 | cut -d' ' -f2)) for validate-parts.py and the model tools"
else
    bad "python3 missing - validate-parts.py and the model pipeline need it"
fi

# --- optional: running and modelling ---------------------------------------

head_ "Optional (only needed for some work)"

if [[ "$PLATFORM" == "wsl" ]]; then
    # Only search roots that exist, and swallow find's exit code. find returns non-zero for any
    # missing root, and under `pipefail` that fails the whole pipeline even when grep matched -
    # which reported StarMap missing on a machine that had it, purely because /mnt/c/Games did
    # not exist. tools/run.sh documents the same trap; it is easy to walk back into.
    STARMAP_ROOTS=()
    for root in "${STARMAP_DIR:-}" /mnt/c/Users /mnt/c/Games /mnt/c/StarMap "/mnt/c/Program Files"; do
        [[ -n "$root" && -d "$root" ]] && STARMAP_ROOTS+=("$root")
    done

    STARMAP_HIT=""
    if [[ ${#STARMAP_ROOTS[@]} -gt 0 ]]; then
        STARMAP_HIT="$(find "${STARMAP_ROOTS[@]}" -maxdepth 4 -iname 'StarMap.exe' 2>/dev/null | head -1 || true)"
    fi

    if [[ -n "$STARMAP_HIT" ]]; then
        ok "StarMap found at $(dirname "$STARMAP_HIT") - ./tools/run.sh can launch the game"
    else
        warn "StarMap not found - needed to actually run the mod"
        hint "./tools/setup-starmap.sh"
    fi

    BLENDER="${BLENDER:-/mnt/c/Program Files/Blender Foundation/Blender 5.2/blender.exe}"
    if [[ -f "$BLENDER" ]]; then
        ok "Blender found - the model pipeline works"
    else
        warn "Blender 5.2 not found - only needed to rebuild the mesh"
        hint "set BLENDER=/path/to/blender if it lives elsewhere"
    fi
elif [[ "$PLATFORM" == "gitbash" ]]; then
    STARMAP_HIT=""
    for root in "${STARMAP_DIR:-}" "$HOME" "/c/Games" "/c/StarMap" "/c/Program Files"; do
        [[ -n "$root" && -d "$root" ]] || continue
        STARMAP_HIT="$(find "$root" -maxdepth 4 -iname 'StarMap.exe' 2>/dev/null | head -1 || true)"
        [[ -n "$STARMAP_HIT" ]] && break
    done

    if [[ -n "$STARMAP_HIT" ]]; then
        ok "StarMap found at $(dirname "$STARMAP_HIT")"
        hint "launch it directly - tools/run.sh assumes WSL and will not find it from here"
    else
        warn "StarMap not found - needed to actually run the mod"
        hint "install it, then ./tools/deploy.sh puts the mod where KSA will load it"
    fi

    hint "the Blender model pipeline is not wired up for this shell; set BLENDER if you need it"
else
    hint "running the game and rebuilding the model are WSL/Windows only; everything else works here"
fi

# --- verdict ----------------------------------------------------------------

head_ "Summary"

if [[ $FAIL -eq 0 && $WARN -eq 0 ]]; then
    printf '  %severything checks out%s\n\n' "$G" "$Z"
elif [[ $FAIL -eq 0 ]]; then
    printf '  %sready to build and test%s (%d optional item(s) missing)\n\n' "$G" "$Z" "$WARN"
else
    printf '  %s%d thing(s) need fixing%s before this repository will build\n\n' "$R" "$FAIL" "$Z"
fi

printf '  Next: %sCONTRIBUTING.md%s for the workflow, %sCLAUDE.md%s for how the code is laid out\n' \
       "$D" "$Z" "$D" "$Z"
printf '        %sdocs/FRAMES-AND-EPOCHS.md before touching rounds, drawing or timing%s\n\n' "$D" "$Z"

exit "$FAIL"
