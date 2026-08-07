#!/usr/bin/env bash
#
# Flies one scripted engagement with nobody watching, and reports pass or fail.
#
#   ./tools/scenario.sh head-on          # build, deploy, launch, fly it, report
#   ./tools/scenario.sh overhead
#   ./tools/scenario.sh passing
#   ./tools/scenario.sh head-on --keep   # leave the game running afterwards
#
# The gap this closes is not headless rendering -- KSA ships Windows-only natives and threads its
# simulation through a Vulkan renderer, so there is no headless to have. It is that verifying a
# behaviour change needed a person to click things. The game still draws to a window; nobody has
# to look at it.
#
# The mod reads the scenario from a one-line file beside its log, drives the engagement, and writes
# SCENARIO lines. This waits for the verdict, screenshots whenever the mod says CAPTURE, and exits
# non-zero on FAIL or TIMEOUT so it can sit in a script.
#
# What it cannot judge is appearance. That is what the screenshots are for: they arrive without
# anyone sitting through the flight, and a person -- or a model -- looks at them afterwards.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

SCENARIO="${1:-head-on}"
KEEP=0
[[ "${2:-}" == "--keep" ]] && KEEP=1

# The craft to boot into. It must carry a launcher, or the runner waits forever for a battery to
# crew: the default startVehicle is a plain Rocket and nothing crews on it.
#
# settings.toml's startVehicle, not a save. StarMap's GameArguments do carry "-load <name>" through
# to KSA's terminal commands in principle, but it was measured not to fire -- the game booted its
# default situation with no save-load line in its own log. Install the craft with
# tools/install-testcraft.sh.
SAVE="${KSARMORY_SCENARIO_SAVE:-rocket missile}"

case "$SCENARIO" in
    head-on|overhead|passing) ;;
    *) echo "usage: $0 {head-on|overhead|passing} [--keep]" >&2; exit 2 ;;
esac

USER_DIR="$("$REPO_ROOT/tools/ksa-user-dir.sh")"
LOG="$USER_DIR/Logs/KSArmory.log"
SHOTS="$REPO_ROOT/screenshots"

# Consumed by the mod as it reads it, so a later launch cannot silently re-run this.
mkdir -p "$USER_DIR/Logs"
printf '%s|%s\n' "$SCENARIO" "$SAVE" > "$USER_DIR/Logs/scenario.txt"

# KSA shows a configuration dialog at startup and waits for START KSA to be clicked, which is
# exactly the human this exists to remove. The dialog is the "Always Show" checkbox, persisted as
# selectSystemOnStart, and with it off the game boots straight into settings.toml's startVehicle.
# Restored on exit: it is the player's setting, not ours.
SETTINGS="$USER_DIR/settings.toml"
DIALOG_WAS=""
CRAFT_WAS=""
if [[ -f "$SETTINGS" ]]; then
    DIALOG_WAS="$(grep -oE '^selectSystemOnStart = (true|false)' "$SETTINGS" | head -1 || true)"
    CRAFT_WAS="$(grep -oE '^startVehicle = ".*"' "$SETTINGS" | head -1 || true)"

    sed -i 's/^selectSystemOnStart = true/selectSystemOnStart = false/' "$SETTINGS"

fi

echo "== deploying"
"$REPO_ROOT/tools/deploy.sh" >/dev/null

echo "== launching, scenario '$SCENARIO', save '$SAVE'"
: > "$LOG" 2>/dev/null || true
"$REPO_ROOT/tools/run.sh" --no-build >/dev/null 2>&1 &
LAUNCHER=$!

cleanup() {
    if (( ! KEEP )); then
        cmd.exe /c "taskkill /IM StarMap.exe /F" >/dev/null 2>&1 || true
    fi
    kill "$LAUNCHER" 2>/dev/null || true


    # The game rewrites settings.toml on exit, so this has to run after it is gone.
    if [[ -f "$SETTINGS" ]]; then
        sleep 1
        [[ "$DIALOG_WAS" == "selectSystemOnStart = true" ]] &&
            sed -i 's/^selectSystemOnStart = false/selectSystemOnStart = true/' "$SETTINGS"
        [[ -n "$CRAFT_WAS" ]] &&
            sed -i "s|^startVehicle = \".*\"|$CRAFT_WAS|" "$SETTINGS"
    fi
}
trap cleanup EXIT

# StarMap shows a configuration dialog before the game starts, so the wall-clock budget has to
# cover a human-free start plus the flight itself.
DEADLINE=$(( SECONDS + 300 ))
VERDICT=""
SEEN=""

while (( SECONDS < DEADLINE )); do
    [[ -f "$LOG" ]] || { sleep 2; continue; }

    while IFS= read -r line; do
        case "$line" in
            *"SCENARIO"*) ;;
            *) continue ;;
        esac

        # Only report each line once; the log is re-read rather than followed, so a restart or a
        # truncation cannot leave this waiting on output it already consumed.
        [[ "$SEEN" == *"$line"* ]] && continue
        SEEN+="$line"
        echo "   ${line#*SCENARIO }"

        case "$line" in
            *CAPTURE*)
                sleep 1
                "$REPO_ROOT/tools/screenshot.sh" >/dev/null 2>&1 || true
                echo "   -> screenshot in screenshots/"
                ;;
            *PASS*)    VERDICT=PASS ;;
            *FAIL*)    VERDICT=FAIL ;;
            *TIMEOUT*) VERDICT=TIMEOUT ;;
        esac
    done < "$LOG"

    [[ -n "$VERDICT" ]] && break
    sleep 2
done

echo
case "$VERDICT" in
    PASS) echo "scenario '$SCENARIO': PASS" ;;
    "")   echo "scenario '$SCENARIO': no verdict within $(( 300 / 60 )) minutes" >&2
          echo "  the game may still be on StarMap's configuration dialog -- it needs START KSA clicked" >&2
          exit 1 ;;
    *)    echo "scenario '$SCENARIO': $VERDICT" >&2; exit 1 ;;
esac

ls -t "$SHOTS"/*.png 2>/dev/null | head -3 | sed 's|.*/|  shot: |' || true
