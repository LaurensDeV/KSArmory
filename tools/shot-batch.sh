#!/usr/bin/env bash
#
# Flies a batch of ballistic shots overnight, interleaved between arms, and keeps every log.
#
#     ./tools/shot-batch.sh --aim 26.5S,64.0W --arms base=HEAD,grav=arm/gravity --blocks 6
#     ./tools/shot-batch.sh --resume ~/shots/2026-08-22          # carry on after an interruption
#     ./tools/shot-batch.sh --plan-only --arms ... --blocks 6    # print the run order and stop
#
# docs/SHOT-PROTOCOL.md is why this exists and how to read what it writes. The short version:
# run-to-run scatter on an identical pick-up is a factor of 1.7 either side of a 0.79 km median,
# so a difference of 300 m needs about twenty-five shots an arm and a single shot each way settles
# nothing at all. Everything here is in service of spending fifty shots so that they add up.
#
# Four things it does that a bare `for` loop around scenario.sh does not:
#
#   * Every arm is BUILT ONCE, UP FRONT, and stashed. Nothing during the night reads the working
#     tree, so an edit at 3 a.m. cannot reach a shot in flight. The build is byte-reproducible, so
#     the deployed DLL's SHA-256 is proof of which arm actually flew -- recorded per shot, and
#     checked against the arm's stash before the game is launched.
#   * ARMS ARE INTERLEAVED, in a seeded random order within each block. Anything that changes with
#     time or machine state -- thermal throttling, a service waking, a long session fragmenting --
#     becomes noise shared by every arm instead of a difference between the ones flown early and
#     the ones flown late. It costs one file copy a shot against eight minutes of flight.
#   * THE LOG IS COPIED OUT between runs. scenario.sh truncates KSArmory.log at every launch, so
#     the release probes, the warhead traces and the frame pacing of shot N are gone the instant
#     shot N+1 starts.
#   * ONE BATCH AT A TIME, enforced with a lock. Two batches sharing one game install and one mods
#     folder produce shots that belong to neither.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
    sed -n '2,29p' "${BASH_SOURCE[0]}" | sed 's/^# \?//'
    exit "${1:-0}"
}

AIM="26.5S,64.0W"
BAR=""
ARMS_SPEC=""
BLOCKS=6
OUT=""
SEED=""
PLAN_ONLY=0
RESUME=0

while (( $# )); do
    case "$1" in
        --aim)       AIM="$2"; shift 2 ;;
        --bar)       BAR="$2"; shift 2 ;;
        --arms)      ARMS_SPEC="$2"; shift 2 ;;
        --blocks)    BLOCKS="$2"; shift 2 ;;
        --out)       OUT="$2"; shift 2 ;;
        --seed)      SEED="$2"; shift 2 ;;
        --plan-only) PLAN_ONLY=1; shift ;;
        --resume)    RESUME=1; OUT="$2"; shift 2 ;;
        -h|--help)   usage 0 ;;
        *) echo "unknown option: $1" >&2; usage 2 ;;
    esac
done

[[ -n "$OUT" ]] || OUT="$HOME/shots/$(date +%Y-%m-%d-%H%M)"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"

# --- one batch at a time ----------------------------------------------------
#
# Not advisory. Three of this project's batches were contaminated by a second one starting while
# the first ran, and the shots are indistinguishable afterwards: both write the same log, both
# deploy into the same mods folder, and both kill each other's game on the way past.

LOCKFILE="$HOME/.ksarmory-shot-batch.lock"
exec 9>"$LOCKFILE"
if ! flock -n 9; then
    echo "error: another shot batch holds $LOCKFILE." >&2
    echo "       Wait for it, or kill it -- two batches produce shots belonging to neither." >&2
    exit 1
fi
echo "$$ $(date -Is) $OUT" >&9

# --- the plan ---------------------------------------------------------------

PLAN="$OUT/plan.tsv"
ARMS_TSV="$OUT/arms.tsv"
SHOTS_TSV="$OUT/shots.tsv"
mkdir -p "$OUT/arms" "$OUT/shots"

if (( RESUME )); then
    [[ -f "$PLAN" && -f "$ARMS_TSV" ]] || { echo "error: $OUT holds no plan to resume" >&2; exit 1; }
    echo "== resuming $OUT"
else
    [[ -n "$ARMS_SPEC" ]] || { echo "error: --arms is required" >&2; usage 2; }
    [[ -n "$SEED" ]] || SEED="$(date +%s)"

    # The tree is read exactly once, here, and it has to be clean: an arm built from a dirty tree
    # is a binary nobody can reproduce, and the whole night hangs off being able to say what flew.
    if [[ -n "$(git -C "$REPO_ROOT" status --porcelain)" ]]; then
        echo "error: the working tree is dirty. Commit or stash before building the arms --" >&2
        echo "       an arm built from uncommitted work cannot be rebuilt or attributed." >&2
        exit 1
    fi

    STARTED_ON="$(git -C "$REPO_ROOT" rev-parse --abbrev-ref HEAD)"
    [[ "$STARTED_ON" == "HEAD" ]] && STARTED_ON="$(git -C "$REPO_ROOT" rev-parse HEAD)"

    restore_tree() { git -C "$REPO_ROOT" checkout --quiet "$STARTED_ON" 2>/dev/null || true; }

    echo "== building the arms"
    : > "$ARMS_TSV"
    printf 'arm\tref\tsha\tdll_sha256\n' >> "$ARMS_TSV"

    IFS=',' read -ra SPECS <<< "$ARMS_SPEC"
    for spec in "${SPECS[@]}"; do
        name="${spec%%=*}"
        ref="${spec#*=}"
        [[ "$name" == "$ref" ]] && ref="$name"

        echo "   $name  <- $ref"
        if ! git -C "$REPO_ROOT" checkout --quiet --detach "$ref" 2>/dev/null; then
            restore_tree
            echo "error: no such ref '$ref' for arm '$name'" >&2
            exit 1
        fi

        if ! "$REPO_ROOT/tools/build.sh" >"$OUT/arms/$name.build.log" 2>&1; then
            restore_tree
            echo "error: arm '$name' does not build; see $OUT/arms/$name.build.log" >&2
            exit 1
        fi

        rm -rf "${OUT:?}/arms/$name"
        cp -a "$REPO_ROOT/src/KSArmory/bin/Release/net10.0" "$OUT/arms/$name"

        printf '%s\t%s\t%s\t%s\n' \
            "$name" "$ref" "$(git -C "$REPO_ROOT" rev-parse HEAD)" \
            "$(sha256sum "$OUT/arms/$name/KSArmory.dll" | cut -d' ' -f1)" >> "$ARMS_TSV"
    done

    restore_tree

    # Two arms with one binary is not a comparison. It happens: a constant edited in a file the
    # build does not reach, a revert that reverted more than it meant to, an arm ref that resolved
    # to the same commit as the baseline. The night would run to completion and report a dead heat.
    dupes="$(tail -n +2 "$ARMS_TSV" | cut -f4 | sort | uniq -d)"
    if [[ -n "$dupes" ]]; then
        echo "error: two arms built the same binary -- they would fly as one arm:" >&2
        tail -n +2 "$ARMS_TSV" | grep -F "$dupes" | cut -f1,4 | sed 's/^/       /' >&2
        exit 1
    fi

    # Randomised within each block rather than a fixed rotation. A fixed order confounds the arm
    # with its position in the block, and position is not nothing: the first game launched after
    # the machine has been idle for eight minutes is not in the state the fourth is.
    mapfile -t NAMES < <(tail -n +2 "$ARMS_TSV" | cut -f1)
    : > "$PLAN"
    python3 - "$SEED" "$BLOCKS" "${NAMES[@]}" >> "$PLAN" <<'PY'
import random, sys
seed, blocks, *names = sys.argv[1:]
rng = random.Random(int(seed))
n = 0
for b in range(1, int(blocks) + 1):
    order = names[:]
    rng.shuffle(order)
    for arm in order:
        n += 1
        print(f"{n:03d}\t{b}\t{arm}")
PY

    {
        printf 'started\t%s\n' "$(date -Is)"
        printf 'host\t%s\n'    "$(hostname)"
        printf 'aim\t%s\n'     "$AIM"
        printf 'bar\t%s\n'     "${BAR:-default}"
        printf 'seed\t%s\n'    "$SEED"
        printf 'blocks\t%s\n'  "$BLOCKS"
        printf 'base\t%s\n'    "$STARTED_ON"
        printf 'ksa\t%s\n'     "$(grep -oE '^build[[:space:]]*=.*' "$REPO_ROOT/ksa-assemblies.lock" | head -1)"
        printf 'craft\t%s\n'   "${KSARMORY_SCENARIO_CRAFT:-<settings.toml>}"
        printf 'save\t%s\n'    "${KSARMORY_SCENARIO_SAVE:-<none>}"
    } > "$OUT/batch.tsv"

    printf 'n\tblock\tarm\tverdict\tdll_sha256\tseconds\tstarted\n' > "$SHOTS_TSV"
fi

echo
echo "== plan: $(wc -l < "$PLAN") shots into $OUT"
cat "$OUT/batch.tsv"
(( PLAN_ONLY )) && { column -t "$PLAN"; exit 0; }

# --- fly it -----------------------------------------------------------------

USER_DIR="$("$REPO_ROOT/tools/ksa-user-dir.sh")"
LOG="$USER_DIR/Logs/KSArmory.log"
MODS="${KSA_MODS_DIR:-$USER_DIR/mods}/KSArmory"
mkdir -p "$MODS"

# settings.toml is restored by scenario.sh on a clean exit and not on a killed one, so a night
# that ends badly otherwise leaves the player booted into a test craft with the start dialog off.
cp -f "$USER_DIR/settings.toml" "$OUT/settings.toml.before" 2>/dev/null || true

SCENARIO_ARG="mirv:$AIM"
[[ -n "$BAR" ]] && SCENARIO_ARG="$SCENARIO_ARG,$BAR"

flown=0
while IFS=$'\t' read -r n block arm; do
    # Resume skips what is already recorded, so an interrupted night carries on rather than
    # re-flying shots whose logs are already on disk.
    if cut -f1 "$SHOTS_TSV" | grep -qx "$n"; then continue; fi

    want="$(awk -F'\t' -v a="$arm" '$1 == a { print $4 }' "$ARMS_TSV")"

    printf '\n=== shot %s  block %s  arm %s  (%s)\n' "$n" "$block" "$arm" "$(date +%H:%M:%S)"

    cp -a "$OUT/arms/$arm/." "$MODS/"
    got="$(sha256sum "$MODS/KSArmory.dll" | cut -d' ' -f1)"
    if [[ "$got" != "$want" ]]; then
        echo "error: the deployed DLL is not arm '$arm' ($got, wanted $want)." >&2
        echo "       Something else is writing to $MODS. Stopping rather than flying it." >&2
        exit 1
    fi

    started="$(date -Is)"
    t0=$SECONDS
    set +e
    "$REPO_ROOT/tools/scenario.sh" "$SCENARIO_ARG" --no-deploy \
        > "$OUT/shots/$n-$arm.out" 2>&1
    rc=$?
    set -e
    elapsed=$(( SECONDS - t0 ))

    # Before anything else can launch the game and truncate it. Everything the shot is worth
    # attributing -- the release probes, the traces, the frame pacing -- is only in here.
    cp -f "$LOG" "$OUT/shots/$n-$arm.log" 2>/dev/null || true

    verdict="$(grep -oE '^scenario .*: (PASS|FAIL|TIMEOUT)' "$OUT/shots/$n-$arm.out" \
               | tail -1 | sed 's/.*: //')"
    [[ -n "$verdict" ]] || verdict="NOVERDICT"

    printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
        "$n" "$block" "$arm" "$verdict" "$got" "$elapsed" "$started" >> "$SHOTS_TSV"

    flown=$(( flown + 1 ))
    printf '    %s in %d:%02d (exit %d)\n' "$verdict" $(( elapsed / 60 )) $(( elapsed % 60 )) "$rc"
    grep -oE 'worst .*spread [0-9.]+ km' "$OUT/shots/$n-$arm.out" | tail -1 | sed 's/^/    /' || true

    # The gate can only remove an arm, never call one a win: a catastrophe is visible in a shot or
    # two and is worth the budget back, and everything finer than that is a morning-after question
    # the whole batch has to be in hand to answer. --gate prints the arms to drop, if any.
    if (( flown % 4 == 0 )); then
        dropped="$("$REPO_ROOT/tools/shot-report.py" "$OUT" --gate || true)"
        if [[ -n "$dropped" ]]; then
            echo "    gate: dropping $dropped"
            for dead in $dropped; do
                grep -vP "\t$dead$" "$PLAN" > "$PLAN.tmp" && mv "$PLAN.tmp" "$PLAN"
            done
        fi
    fi
done < "$PLAN"

echo
echo "== $flown shots flown into $OUT"
"$REPO_ROOT/tools/shot-report.py" "$OUT"
