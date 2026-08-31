#!/usr/bin/env bash
#
# Flies a batch of ballistic shots overnight, interleaved between arms, and keeps every log.
#
#     ./tools/shot-batch.sh --aim 26.5S,64.0W --arms base=HEAD,grav=arm/gravity --blocks 6
#     ./tools/shot-batch.sh --paired 'base|trim:TrimCeilingFromBudget=true' --blocks 6
#     ./tools/shot-batch.sh --aim none --arms base=HEAD --blocks 6   # shoot whatever the save defends
#     ./tools/shot-batch.sh --resume ~/shots/2026-08-22          # carry on after an interruption
#     ./tools/shot-batch.sh --plan-only --arms ... --blocks 6    # print the run order and stop
#
# docs/SHOT-PROTOCOL.md is why this exists and how to read what it writes. The short version:
# run-to-run scatter on an identical pick-up is a factor of 1.7 either side of a 0.79 km median,
# so a difference of 300 m needs about twenty-five shots an arm and a single shot each way settles
# nothing at all. Everything here is in service of spending fifty shots so that they add up.
#
# --paired is the OTHER kind of night, and it is the one to prefer. Instead of a build per arm and
# an arm per shot, it builds once and gives every rocket in the world its own variant -- so the two
# arms share a frame trace, a warp history, a solver load and a target. The between-run instrument
# stopped being able to see anything smaller than 3x when the same baseline read 14.49 km and
# 5.43 km on identical code three hours apart; the within-run one has no such term. Sim/ShotArms.cs
# has the whole argument. It needs a save carrying several rockets, and what it cannot compare is
# anything the rockets share -- the build, the system, the ground under the target.
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
PAIRED=""
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
        --paired)    PAIRED="$2"; shift 2 ;;
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
    [[ -f "$OUT/batch.tsv" ]] || { echo "error: $OUT holds no batch.tsv to resume from" >&2; exit 1; }

    # Restored from the record, never re-defaulted. --resume takes no --aim, so AIM kept its
    # default here and the resumed night flew a DIFFERENT SHOT from the one it was planning:
    # naming any point sets AimWasGiven and the scenario MOVES the defended site to it. Measured
    # as 6,379 km downrange against the 12,902 km the same plan flew unresumed, every arm 301 km
    # out, four shots deep before anything looked wrong. Nothing in the output said so, because
    # both halves are behaving exactly as documented.
    AIM="$(awk -F'\t' '$1 == "aim"  { print $2 }' "$OUT/batch.tsv")"
    BAR="$(awk -F'\t' '$1 == "bar"  { print $2 }' "$OUT/batch.tsv")"
    PAIRED="$(awk -F'\t' '$1 == "paired" { print $2 }' "$OUT/batch.tsv")"
    [[ "$PAIRED" == "<none>" ]] && PAIRED=""
    [[ "$BAR" == "default" ]] && BAR=""
    [[ -n "$AIM" ]] || { echo "error: $OUT/batch.tsv records no aim" >&2; exit 1; }

    # The save and the craft come from the environment rather than from a flag, so they are
    # checked rather than restored: a resume run in a shell that has lost KSARMORY_SCENARIO_SAVE
    # would fly a different scene under the same night's name. Refusing beats correcting, because
    # the operator's shell is the thing that is wrong.
    for var in SAVE CRAFT; do
        eval "got=\"\${KSARMORY_SCENARIO_$var:-}\""
        want="$(awk -F'\t' -v k="$(echo "$var" | tr 'A-Z' 'a-z')" '$1 == k { print $2 }' "$OUT/batch.tsv")"
        [[ "$want" == "<none>" || "$want" == "<settings.toml>" ]] && want=""
        if [[ "$got" != "$want" ]]; then
            echo "error: KSARMORY_SCENARIO_$var is '${got:-<unset>}', but this night was planned" >&2
            echo "       with '${want:-<unset>}'. Fix the environment rather than the record." >&2
            exit 1
        fi
    done

    echo "== resuming $OUT (aim ${AIM}, bar ${BAR:-default})"
else
    # One build, because the arms differ by settings rather than by code. Named for the spec's
    # own arms so the run directory still says what was being compared.
    if [[ -n "$PAIRED" ]]; then
        [[ -z "$ARMS_SPEC" ]] || {
            echo "error: --paired and --arms are two different experiments; pick one." >&2
            echo "       --paired varies settings between rockets in one world and builds once;" >&2
            echo "       --arms builds a binary per arm and flies one of them a shot." >&2
            usage 2
        }
        ARMS_SPEC="paired=HEAD"
    fi

    [[ -n "$ARMS_SPEC" ]] || { echo "error: --arms or --paired is required" >&2; usage 2; }
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

    # Every ref pinned to a sha BEFORE the first checkout. The loop below detaches HEAD to build
    # each arm, so a relative ref -- HEAD most of all, but @, HEAD~1 and a branch name being moved
    # underneath all do it -- resolves against whichever arm was built last rather than against the
    # tree the operator typed it in. `--arms base=<sha>,fixes=HEAD` silently made both arms the
    # first sha; only the identical-source check downstream caught it, and it caught it by refusing
    # the night rather than by explaining it.
    declare -a ARM_NAME_LIST=() ARM_REF_LIST=() ARM_PIN_LIST=()
    for spec in "${SPECS[@]}"; do
        name="${spec%%=*}"
        ref="${spec#*=}"
        [[ "$name" == "$ref" ]] && ref="$name"

        if ! pin="$(git -C "$REPO_ROOT" rev-parse --verify --quiet "$ref^{commit}")"; then
            echo "error: no such ref '$ref' for arm '$name'" >&2
            exit 1
        fi

        ARM_NAME_LIST+=("$name")
        ARM_REF_LIST+=("$ref")
        ARM_PIN_LIST+=("$pin")
    done

    for (( a = 0; a < ${#ARM_NAME_LIST[@]}; a++ )); do
        name="${ARM_NAME_LIST[a]}"
        ref="${ARM_REF_LIST[a]}"
        pin="${ARM_PIN_LIST[a]}"

        echo "   $name  <- $ref  ($(git -C "$REPO_ROOT" rev-parse --short "$pin"))"
        if ! git -C "$REPO_ROOT" checkout --quiet --detach "$pin" 2>/dev/null; then
            restore_tree
            echo "error: could not check out '$ref' for arm '$name'" >&2
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
            "$name" "$ref" "$pin" \
            "$(sha256sum "$OUT/arms/$name/KSArmory.dll" | cut -d' ' -f1)" >> "$ARMS_TSV"
    done

    restore_tree

    # Two arms with one behaviour is not a comparison. It happens: a constant edited in a file the
    # build does not reach, a revert that reverted more than it meant to, an arm ref that resolved
    # to the same commit as the baseline. The night would run to completion and report a dead heat.
    #
    # Compared on the source, because the DLLs cannot answer it: SourceLink stamps the commit into
    # AssemblyInformationalVersion, so two arms always differ by that one string whatever their
    # code says. That stamp is what makes the DLL hash an exact arm identity below, and it is
    # exactly why the hash cannot also be the sameness test.
    mapfile -t ARM_NAMES < <(tail -n +2 "$ARMS_TSV" | cut -f1)
    mapfile -t ARM_SHAS  < <(tail -n +2 "$ARMS_TSV" | cut -f3)
    for (( i = 0; i < ${#ARM_SHAS[@]}; i++ )); do
        for (( j = i + 1; j < ${#ARM_SHAS[@]}; j++ )); do
            if git -C "$REPO_ROOT" diff --quiet \
                   "${ARM_SHAS[i]}" "${ARM_SHAS[j]}" -- src/KSArmory; then
                echo "error: arms '${ARM_NAMES[i]}' and '${ARM_NAMES[j]}' ship identical source --" >&2
                echo "       they would fly as one arm." >&2
                exit 1
            fi
        done
    done

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
        printf 'paired\t%s\n'  "${PAIRED:-<none>}"
        printf 'base\t%s\n'    "$STARTED_ON"
        printf 'ksa\t%s\n'     "$(grep -oE '^build[[:space:]]+\S+' "$REPO_ROOT/ksa-assemblies.lock" | head -1)"
        printf 'craft\t%s\n'   "${KSARMORY_SCENARIO_CRAFT:-<settings.toml>}"
        printf 'save\t%s\n'    "${KSARMORY_SCENARIO_SAVE:-<none>}"

        # Recorded because it is worth a factor in frame rate and was invisible: the 25-body Sol
        # is every celestial's work per frame and per ground lookup, and a night flown on it is not
        # comparable with one flown on SolLite. scenario.sh defaults mirv to SolLite.
        printf 'system\t%s\n'  "${KSARMORY_SCENARIO_SYSTEM:-SolLite (scenario.sh default)}"
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

# "--aim none" flies the scenario's own choice, which is whatever site in the scene is defended.
# That is not the same shot as naming the site's coordinates: naming any point sets AimWasGiven,
# and the scenario then MOVES the site to it so the impact lands somewhere with a camera on it. On
# a save whose target is where the operator put it, that is the one thing a batch must not do.
if [[ "$AIM" == "none" || -z "$AIM" ]]; then
    SCENARIO_ARG="mirv"
    [[ -n "$BAR" ]] && SCENARIO_ARG="mirv::$BAR"
else
    SCENARIO_ARG="mirv:$AIM"
    [[ -n "$BAR" ]] && SCENARIO_ARG="$SCENARIO_ARG,$BAR"
fi

# Read once, into memory. A dropped arm cannot be taken out of the file the loop is reading:
# `mv` replaces the inode while the loop's redirect still holds the old one, so the drop would
# apply to nothing and every dead arm would fly its full share anyway.
mapfile -t PLAN_ROWS < "$PLAN"
DROPPED=""

flown=0
at=0
while (( at < ${#PLAN_ROWS[@]} )); do
    row="${PLAN_ROWS[at]}"
    at=$(( at + 1 ))
    IFS=$'\t' read -r n block arm <<< "$row"

    # Resume skips what is already recorded, so an interrupted night carries on rather than
    # re-flying shots whose logs are already on disk.
    if cut -f1 "$SHOTS_TSV" | grep -qx "$n"; then continue; fi
    if [[ " $DROPPED " == *" $arm "* ]]; then continue; fi

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
    # The phase is the shot's own number, so each variant draws each place in the roster across
    # the night rather than one of them owning the odd positions -- which matters because a
    # rocket's place in the roster is itself worth 175x in miss.
    PAIRED_ARGS=()
    [[ -n "$PAIRED" ]] && PAIRED_ARGS=(--arms "$PAIRED" --arm-phase "$(( 10#$n ))")

    "$REPO_ROOT/tools/scenario.sh" "$SCENARIO_ARG" --no-deploy "${PAIRED_ARGS[@]+"${PAIRED_ARGS[@]}"}" \
        > "$OUT/shots/$n-$arm.out" 2>&1
    rc=$?
    set -e
    elapsed=$(( SECONDS - t0 ))

    # Before anything else can launch the game and truncate it. Everything the shot is worth
    # attributing -- the release probes, the traces, the frame pacing -- is only in here.
    cp -f "$LOG" "$OUT/shots/$n-$arm.log" 2>/dev/null || true

    # `|| true` because pipefail is on and a shot that printed nothing at all is exactly the case
    # NOVERDICT is for. Without it grep's empty exit takes the whole night down on the first bad
    # shot, with no row in shots.tsv and nothing on stdout saying which shot or why -- which is
    # strictly worse than the failure it is reporting.
    verdict="$( { grep -oE '^scenario .*: (PASS|FAIL|TIMEOUT)' "$OUT/shots/$n-$arm.out" || true; } \
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
        for dead in $dropped; do
            [[ " $DROPPED " == *" $dead "* ]] && continue
            DROPPED="$DROPPED $dead"
            printf '%s\t%s\n' "$dead" "$(date -Is)" >> "$OUT/dropped.tsv"

            # The budget is a night, not a count. An arm dropped early still has most of its share
            # sitting in the plan, and simply skipping those finishes hours short -- so they are
            # appended over the arms still flying, a whole block at a time, which is what keeps the
            # interleaving intact through a removal.
            freed=0
            for (( k = at; k < ${#PLAN_ROWS[@]}; k++ )); do
                [[ "${PLAN_ROWS[k]}" == *$'\t'"$dead" ]] && freed=$(( freed + 1 ))
            done

            mapfile -t ALIVE < <(tail -n +2 "$ARMS_TSV" | cut -f1 \
                | grep -vxF "$(printf '%s\n' $DROPPED)")
            (( ${#ALIVE[@]} < 2 )) && { echo "    gate: only ${#ALIVE[@]} arm left"; break; }

            echo "    gate: dropping '$dead'; its $freed remaining shots go to ${ALIVE[*]}"
            extra="$(( ${#PLAN_ROWS[@]} + 1 ))"
            while (( freed > 0 )); do
                for alive in "${ALIVE[@]}"; do
                    (( freed > 0 )) || break
                    PLAN_ROWS+=("$(printf '%03d\tx\t%s' "$extra" "$alive")")
                    extra=$(( extra + 1 ))
                    freed=$(( freed - 1 ))
                done
            done
            printf '%s\n' "${PLAN_ROWS[@]}" > "$PLAN"
        done
    fi
done

echo
echo "== $flown shots flown into $OUT"
"$REPO_ROOT/tools/shot-report.py" "$OUT"

# A paired night builds ONE arm, so the between-arm tables above are empty by construction and the
# comparison it was flown for lives behind --paired. Printing only the first reports nothing the
# night was for.
if [[ -n "$PAIRED" ]]; then
    echo
    "$REPO_ROOT/tools/shot-report.py" "$OUT" --paired
fi
