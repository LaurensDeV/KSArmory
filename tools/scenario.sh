#!/usr/bin/env bash
#
# Flies one scripted scenario with nobody watching, and reports pass or fail.
#
#   ./tools/scenario.sh head-on          # build, deploy, launch, fly it, report
#   ./tools/scenario.sh overhead
#   ./tools/scenario.sh passing
#   ./tools/scenario.sh mirv             # the ballistic shot, end to end
#   ./tools/scenario.sh mirv:26.485S,68.148W       # ...at somewhere else
#   ./tools/scenario.sh mirv:26.485S,68.148W,2     # ...and pass only under 2 km
#   ./tools/scenario.sh head-on --keep   # leave the game running afterwards
#   ./tools/scenario.sh head-on --shots  # ...and screenshot on CAPTURE (whole screen, opt-in)
#
# The gap this closes is not headless rendering -- KSA ships Windows-only natives and threads its
# simulation through a Vulkan renderer, so there is no headless to have. It is that verifying a
# behaviour change otherwise needs a person to click things. The game still draws to a window;
# nobody has to look at it.
#
# The mod reads the scenario from a one-line file beside its log, drives it, and writes SCENARIO
# lines. This waits for the verdict, screenshots whenever the mod says CAPTURE, and exits non-zero
# on FAIL or TIMEOUT so it can sit in a script.
#
# What it cannot judge is appearance. That is what the screenshots are for: they arrive without
# anyone sitting through the flight, and a person -- or a model -- looks at them afterwards.
#
# ---------------------------------------------------------------------------------------------
# mirv: what it needs of you, and what it does for you
#
# You have to supply the rocket. A mod cannot put one on the pad -- LoadVehicleFromLibrary
# resolves under the game install, see CLAUDE.md -- so this needs a craft carrying a MIRV bus,
# already loaded, on the ground, with a launch solution available. Point settings.toml's
# startVehicle at it, or name it here:
#
#   KSARMORY_SCENARIO_CRAFT="Peacekeeper" ./tools/scenario.sh mirv
#
# Everything after that is done for you: it finds whichever craft in the scene has a ballistic
# computer and its wheels on the ground, designates the aim point, arms, stages, asks the world
# for timewarp once, and follows the flight -- cutoff, separation, the trim, every release with
# how far off the salvo's line its tube was, and every impact with its miss. The verdict is the
# worst warhead of the group against a bar you can move from the request line.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
    sed -n '2,40p' "${BASH_SOURCE[0]}" | sed 's/^# \?//'
    exit 0
fi

SCENARIO="${1:-head-on}"
KEEP=0
SHOTS_ON=0
for arg in "${@:2}"; do
    case "$arg" in
        --keep)  KEEP=1 ;;
        --shots) SHOTS_ON=1 ;;
    esac
done

# The craft to boot into. It must carry a launcher, or the runner waits forever for a battery to
# crew: the default startVehicle is a plain Rocket and nothing crews on it.
#
# settings.toml's startVehicle, not a save. StarMap's GameArguments do carry "-load <name>" through
# to KSA's terminal commands in principle, but it does not fire: the game boots its default
# situation with no save-load line in its own log. Install the craft with
# tools/install-testcraft.sh.
CRAFT="${KSARMORY_SCENARIO_CRAFT:-}"

# How long to wait for a verdict, and the scenario's own budget. A ballistic shot is seven minutes
# of simulated flight and the timewarp it asks for can be refused, so its budget has to cover the
# whole thing at one times speed -- a deadline that cuts a working shot short reports a timeout
# for something that was going fine.
DEADLINE_SECONDS=300

case "${SCENARIO%%:*}" in
    head-on|overhead|passing)
        # A save game, because these need a launcher on the ground and a scene to spawn a drone
        # into, and the boot craft alone is neither.
        SAVE="${KSARMORY_SCENARIO_SAVE:-rocket missile}"
        ;;
    mirv)
        # No save by default: the rocket is the operator's, wherever they keep it, and a scenario
        # that insists on one particular save is one that only works on one machine.
        SAVE="${KSARMORY_SCENARIO_SAVE:-}"
        DEADLINE_SECONDS=1800
        ;;
    *)
        echo "usage: $0 {head-on|overhead|passing|mirv[:<lat>,<lon>[,<km>]]} [--keep] [--shots]" >&2
        exit 2
        ;;
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
# Restored on exit: it is the player's setting, not the harness's.
SETTINGS="$USER_DIR/settings.toml"
DIALOG_WAS=""
CRAFT_WAS=""
if [[ -f "$SETTINGS" ]]; then
    DIALOG_WAS="$(grep -oE '^selectSystemOnStart = (true|false)' "$SETTINGS" | head -1 || true)"
    CRAFT_WAS="$(grep -oE '^startVehicle = ".*"' "$SETTINGS" | head -1 || true)"

    sed -i 's/^selectSystemOnStart = true/selectSystemOnStart = false/' "$SETTINGS"

    [[ -n "$CRAFT" ]] && sed -i "s|^startVehicle = \".*\"|startVehicle = \"$CRAFT\"|" "$SETTINGS"
fi

echo "== deploying"
"$REPO_ROOT/tools/deploy.sh" >/dev/null

echo "== launching, scenario '$SCENARIO', save '$SAVE'${CRAFT:+, craft '$CRAFT'}"
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
DEADLINE=$(( SECONDS + DEADLINE_SECONDS ))
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
                # Off unless asked for. screenshot.sh grabs the whole primary screen, not the game
                # window, so an unattended run photographs whatever happens to be in front.
                # Verifying by eye is the operator's job; this harness is for the things a log
                # can answer.
                if (( SHOTS_ON )); then
                    sleep 1
                    "$REPO_ROOT/tools/screenshot.sh" >/dev/null 2>&1 || true
                    echo "   -> screenshot in screenshots/"
                fi
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
    "")   echo "scenario '$SCENARIO': no verdict within $(( DEADLINE_SECONDS / 60 )) minutes" >&2
          echo "  the game may still be on StarMap's configuration dialog -- it needs START KSA clicked" >&2
          exit 1 ;;
    *)    echo "scenario '$SCENARIO': $VERDICT" >&2; exit 1 ;;
esac

# An `if` rather than `&&`: this is the last statement, so with --shots off a false `&&` would
# make the whole script exit 1 on a PASS and there would be nothing to script against.
if (( SHOTS_ON )); then
    ls -t "$SHOTS"/*.png 2>/dev/null | head -3 | sed 's|.*/|  shot: |' || true
fi
