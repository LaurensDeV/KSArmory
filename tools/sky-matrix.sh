#!/usr/bin/env bash
# Flies a list of nuclear bursts in a running game through the bridge, one at a time, and keeps what
# each looked like and cost: a capture and the GPU split at each age, the bridge's status, and whether
# KSA logged an exception. docs/NUCLEAR-ALTITUDE.md's flights are made of these.
#
#   tools/sky-matrix.sh [--out DIR] [--lat DEG] [--ages "2 20 120"] [--pose orbit|limb|player] CELL...
#
# A CELL is KT@KM, e.g. 1400@400 (Starfish) or 50000@100. The craft is set down at night first -- the
# longitude searched until the sun is 18 degrees down -- since everything high up is a night sight.
# Every call runs under a timeout, because an MCP call has hung for the full half hour, and each capture
# is copied out before the next call wipes the bridge's last/ folder.
set -euo pipefail

cd "$(dirname "$0")/.."
OUT="${HOME}/shots/sky-$(date +%Y%m%d-%H%M)"
LAT=16.7
AGES="2 20 120"
POSE=orbit
CELLS=()
while (($#)); do
    case "$1" in
        --out) OUT=$2; shift 2 ;;
        --lat) LAT=$2; shift 2 ;;
        --ages) AGES=$2; shift 2 ;;
        --pose) POSE=$2; shift 2 ;;
        *) CELLS+=("$1"); shift ;;
    esac
done
((${#CELLS[@]})) || { echo "usage: $0 [--out DIR] [--lat DEG] [--ages \"2 20 120\"] [--pose orbit|limb|player] KT@KM..." >&2; exit 2; }

cli() { timeout 180 python3 tools/ksa-mcp/server.py cli "$@"; }
LOGS="$(./tools/ksa-user-dir.sh)/Logs"
mkdir -p "$OUT"
echo -e "cell\tage_s\tstatus\tcost" > "$OUT/costs.tsv"

# Night: the first longitude on this latitude with the sun 18 degrees down.
night=""
for lon in 180 150 120 90 60 30 0 -30 -60 -90 -120 -150; do
    elev=$(cli site "{\"lat\":$LAT,\"lon\":$lon}" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("sun_elevation_deg", 99))')
    if python3 -c "import sys; sys.exit(0 if $elev < -18 else 1)"; then night=$lon; break; fi
done
[[ -n "$night" ]] || { echo "no night found at latitude $LAT" >&2; exit 1; }
echo "night at $LAT, $night"

age_now() { cli status | python3 -c 'import json,sys; print(json.load(sys.stdin).get("newest_burst",{}).get("age_s",-1))'; }

for cell in "${CELLS[@]}"; do
    kt=${cell%@*}; km=${cell#*@}
    dir="$OUT/${kt}kt-${km}km-${POSE}"; mkdir -p "$dir"
    ksa_log=$(ls -t "$LOGS"/KittenSpaceAgency.*.log | head -1)
    before=$(grep -c -E "Exception|Update task failed" "$ksa_log" || true)

    cli clear '{}' >/dev/null; sleep 2
    cli burst "{\"kt\":$kt,\"up_m\":$(python3 -c "print(int($km*1000))"),\"explode\":false}" > "$dir/burst.json"
    [[ "$POSE" == player ]] || cli camera "{\"preset\":\"$POSE\"}" >/dev/null
    cli cost '{"reset":true}' >/dev/null
    started=$(date +%s)

    for age in $AGES; do
        # The newest burst's age while its cloud or ball stands; past that -- a thin burst's cloud ends at
        # three minutes and its red wave runs ten -- the wall clock since the burst, since this flies at 1x.
        while python3 -c "import sys; a=$(age_now); a=a if a >= 0 else $(date +%s) - $started; sys.exit(0 if a < $age else 1)"; do sleep 1; done
        cost=$(cli cost '{}' | tr -d '\n')
        status=$(cli status | tr -d '\n ')
        cli cost '{"reset":true}' >/dev/null
        cli capture "{\"label\":\"${kt}kt-${km}km-${age}s\",\"crop\":false}" >/dev/null
        cp tools/ksa-mcp/last/01.jpg "$dir/${age}s.jpg" 2>/dev/null || true
        echo -e "$cell\t$age\t$status\t$cost" >> "$OUT/costs.tsv"
        echo "$cell at ${age}s: $cost"
    done

    after=$(grep -c -E "Exception|Update task failed" "$ksa_log" || true)
    ((after == before)) || echo "$cell: KSA logged $((after - before)) new exception(s) -- see $ksa_log" | tee -a "$OUT/costs.tsv"
done

[[ "$POSE" == player ]] || cli camera '{"release":true}' >/dev/null
echo "kept in $OUT"
