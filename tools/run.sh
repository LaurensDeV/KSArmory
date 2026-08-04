#!/usr/bin/env bash
#
# Builds, deploys and launches KSA through StarMap, showing the mod's output.
# WSL can execute Windows binaries directly, so this works from here.
#
#     ./tools/run.sh              # build, deploy, launch, show mod output
#     ./tools/run.sh --verbose    # ...and KSA's own log spam as well
#     ./tools/run.sh --no-build   # launch what is already deployed
#     ./tools/run.sh --attach     # do not launch; follow an already-running game
#
# Ctrl+C stops the game (or, with --attach, just stops following).
# Override the StarMap location with STARMAP_DIR=/mnt/c/path/to/StarMap.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

DO_BUILD=1
VERBOSE=0
ATTACH=0

for arg in "$@"; do
    case "$arg" in
        --no-build|--no-deploy) DO_BUILD=0 ;;
        --verbose|-v)           VERBOSE=1 ;;
        --attach|--log)         ATTACH=1 ;;
        -h|--help)              sed -n '2,13p' "${BASH_SOURCE[0]}" | sed 's/^# \?//'; exit 0 ;;
        *) echo "unknown option: $arg" >&2; exit 2 ;;
    esac
done

# `|| true` throughout: find exits non-zero for any missing search root, and under
# `set -e` that would abort the script with no message at all.
KSA_USER_DIR="$(find /mnt/c/Users -maxdepth 4 -type d -path '*My Games/Kitten Space Agency' 2>/dev/null | head -1 || true)"
MOD_LOG="${KSA_USER_DIR:+$KSA_USER_DIR/Logs/KSArmory.log}"

# --- attach to a running game ------------------------------------------------

if (( ATTACH )); then
    [[ -n "$MOD_LOG" ]] || { echo "error: could not find the KSA user folder" >&2; exit 1; }
    echo "following $MOD_LOG   (Ctrl+C to stop)"
    # -F keeps up across the truncation the mod does at each launch.
    exec tail -n +1 -F "$MOD_LOG"
fi

# --- locate StarMap ----------------------------------------------------------

if [[ -z "${STARMAP_DIR:-}" ]]; then
    SEARCH_ROOTS=()
    for root in /mnt/c/Users /mnt/c/Games /mnt/c/StarMap "/mnt/c/Program Files"; do
        [[ -d "$root" ]] && SEARCH_ROOTS+=("$root")
    done
    STARMAP_EXE="$(find "${SEARCH_ROOTS[@]}" -maxdepth 4 -iname 'StarMap.exe' 2>/dev/null | head -1 || true)"
    [[ -n "$STARMAP_EXE" ]] && STARMAP_DIR="$(dirname "$STARMAP_EXE")"
fi

if [[ -z "${STARMAP_DIR:-}" || ! -f "$STARMAP_DIR/StarMap.exe" ]]; then
    echo "error: StarMap.exe not found." >&2
    echo "       Run ./tools/setup-starmap.sh, or set STARMAP_DIR." >&2
    exit 1
fi

if [[ ! -f "$STARMAP_DIR/StarMapConfig.json" ]]; then
    echo "error: no StarMapConfig.json in $STARMAP_DIR" >&2
    echo "       Run ./tools/setup-starmap.sh to generate it." >&2
    exit 1
fi

# --- build and deploy --------------------------------------------------------

if (( DO_BUILD )); then
    "$REPO_ROOT/tools/deploy.sh" >/dev/null
    echo "deployed"
fi

# --- launch ------------------------------------------------------------------

# StarMap reads "./StarMapConfig.json" relative to its working directory, so it has to be
# launched from its own folder rather than from the repo.
cd "$STARMAP_DIR"

# Files unzipped onto /mnt/c come out without an exec bit, which WSL reports as
# "Permission denied". Restore it; if the mount has no metadata support, fall back to
# cmd.exe, which does not care about unix modes.
LAUNCH=(./StarMap.exe)
if [[ ! -x ./StarMap.exe ]]; then
    chmod +x ./StarMap.exe 2>/dev/null || true
    if [[ ! -x ./StarMap.exe ]]; then
        echo "note: StarMap.exe is not executable to WSL; launching via cmd.exe"
        LAUNCH=(cmd.exe /c StarMap.exe)
    fi
fi

echo "launching $STARMAP_DIR/StarMap.exe"
echo "log file: ${MOD_LOG:-<unknown>}"
echo

if (( VERBOSE )); then
    exec "${LAUNCH[@]}"
fi

# KSA logs hundreds of DEBUG lines at startup, which bury the handful that matter.
# Keep the mod's own output, StarMap's load messages, and anything that looks like a
# failure from either. --line-buffered so it appears as it happens, and grep never
# exits early, so the game is not killed by SIGPIPE the way `| head` would.
echo "(showing mod output only -- use --verbose for KSA's full log)"
echo
exec "${LAUNCH[@]}" 2>&1 | grep --line-buffered -E \
    '^\[KSArmory\]|^StarMap -|ERROR|WARN|Exception|error|failed'
