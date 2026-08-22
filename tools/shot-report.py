#!/usr/bin/env python3
"""Reads a night of ballistic shots and says what it settled.

    ./tools/shot-report.py ~/shots/2026-08-22            # the table, the arms, the verdicts
    ./tools/shot-report.py ~/shots/2026-08-22 --shots    # ...with every shot's diagnostics
    ./tools/shot-report.py ~/shots/2026-08-22 --gate     # names arms to drop, for shot-batch.sh

docs/SHOT-PROTOCOL.md is the protocol this implements and the reasoning behind every constant
here. The three decisions worth knowing without reading it:

  * The endpoint is the group's MEAN miss, on a LOG scale. Not the worst -- a max over six
    warheads is the noisiest of the four numbers the verdict prints. Not the mean of the arm's
    shots -- the distribution has a heavy right tail, and one shot in sixteen moves an arithmetic
    mean by 40%.
  * The comparison is Wilcoxon rank-sum with an EXACT permutation null, and the effect is the
    Hodges-Lehmann median pairwise log-ratio with a distribution-free interval. Ranks because
    nothing here is normal at n=12; Hodges-Lehmann because a difference of medians is a point
    with no interval, and the interval is what makes "unresolved" a finding rather than a shrug.
  * A comparison is only ever made against the BASELINE ARM FLOWN THE SAME NIGHT. Numbers from an
    earlier night are printed for drift and never entered into a test.
"""

import argparse
import math
import pathlib
import re
import statistics
import sys
from collections import defaultdict

# Two looks are taken at every comparison -- the gate's, mid-batch, and the report's at the end --
# so the nominal level has to be tightened or the pair of them spends more than 5%. Pocock's
# constant boundary for two looks, which is the one that does not depend on where the looks fall.
ALPHA = 0.0294

# An arm this much worse than the baseline in the median is not a candidate that needs settling,
# it is a catastrophe: the flown losses ran 4x, 11x and 29x. Dropping it hands its remaining
# shots back to the arms the night is actually about.
CATASTROPHE_RATIO = 3.0
CATASTROPHE_MIN_SHOTS = 4

# A shot this far out is not a sample from the same distribution as the rest -- the widest
# baseline ever recorded here is 3.43 km over 26 shots. Two of them from one arm is that arm.
WILD_KM = 4.0


# --- parsing ----------------------------------------------------------------

VERDICT = re.compile(
    r"worst\s+([\d.]+)\s*km,\s*best\s+([\d.]+)\s*km,\s*"
    r"mean\s+([\d.]+)\s*km,\s*spread\s+([\d.]+)\s*km")
ARRIVED = re.compile(r"(\d+)\s+of\s+(\d+)\s+arrived")
PICKUP = re.compile(r"already flying at\s+(\d+)\s*km doing\s+(\d+)\s*m/s")
ONPAD = re.compile(r"on the ground at")
CUTOFF = re.compile(r"cutoff:\s*residual\s+([-\d.]+)\s*m/s,\s*own prediction\s+([-\d.]+)\s*km off")
TRIM = re.compile(r"owed\s+([-\d.]+)\s*m/s at the split,\s*([-\d.]+)\s*m/s on release")
OFFLINE = re.compile(r"warhead away from tube\s+(\d+),\s*([-\d.]+)\s*deg off the salvo's line")
PROBE = re.compile(r"release probe:.*?([\d.]+)\s*km from the target,\s*(\d+)\s*s of flight")
THROWN = re.compile(r"thrown\s+([-\d.]+)\s*deg from the platform's track")
TRACEPROBE = re.compile(
    r"probe from the round's own state ->.*?([\d.]+)\s*s of flight,\s*([-\d.]+)\s*m from the aim,"
    r"\s*arriving at\s+([-\d.]+)\s*m/s,\s*([-\d.]+)\s*deg below")
WALK = re.compile(r"walk from the release probe\s+([-\d.]+)\s*m\s*\(([-+\d.]+)\s*down,\s*([-+\d.]+)\s*cross\)")
LAG = re.compile(r"lag\s+([-\d.]+)ms\s*=\s*([-\d.]+)\s*m at")
CLOCKS = re.compile(r"flight\s+([\d.]+)s by the world clock,\s*([\d.]+)s by its own")
SAMPLE = re.compile(r"dt=([\d.]+)ms step=([\d.]+)ms sim=([\d.]+)x")
BAND = re.compile(
    r"DEBUG\s+(\S+)\s+control:.*?pointing band\s+([\d.]+)\s*deg")
BANNER = re.compile(r"KSArmory\s+(\S+)\s+built for KSA\s+(\S+),\s*running\s+(\S+)")


def _floats(pattern, text, groups=1):
    out = []
    for m in pattern.finditer(text):
        out.append(tuple(float(g) for g in m.groups()[:groups]))
    return out


def read_shot(out_path, log_path):
    """Everything one shot is worth attributing, from its stdout and its copied-out log."""
    shot = {"mean": None, "spread": None, "worst": None, "best": None,
            "arrived": None, "released": None, "pickup_km": None, "pickup_ms": None,
            "residual": None, "own_km": None, "trim_split": None, "trim_release": None,
            "offline": [], "probe_km": [], "thrown": [], "arrival_deg": [],
            "arrival_ms": [], "trace_km": [], "walk_m": [], "walk_down": [], "walk_cross": [],
            "band_deg": [],
            "lag_ms": [], "lag_m": [], "clock_gap": [], "dt_ms": [], "sim": [], "coast_ms": [],
            "version": None}

    text = out_path.read_text(errors="replace") if out_path.exists() else ""
    log = log_path.read_text(errors="replace") if log_path.exists() else ""
    both = text + "\n" + log

    m = VERDICT.search(both)
    if m:
        shot["worst"], shot["best"], shot["mean"], shot["spread"] = (float(g) for g in m.groups())
    m = ARRIVED.search(both)
    if m:
        shot["arrived"], shot["released"] = int(m.group(1)), int(m.group(2))
    m = PICKUP.search(both)
    if m:
        shot["pickup_km"], shot["pickup_ms"] = float(m.group(1)), float(m.group(2))
    elif ONPAD.search(both):
        shot["pickup_km"], shot["pickup_ms"] = 0.0, 0.0
    m = CUTOFF.search(both)
    if m:
        shot["residual"], shot["own_km"] = float(m.group(1)), float(m.group(2))
    m = TRIM.search(both)
    if m:
        shot["trim_split"], shot["trim_release"] = float(m.group(1)), float(m.group(2))
    m = BANNER.search(log)
    if m:
        shot["version"] = m.group(1)

    shot["offline"] = [v for _, v in _floats(OFFLINE, both, 2)]
    shot["probe_km"] = [v for v, _ in _floats(PROBE, log, 2)]
    shot["thrown"] = [v for (v,) in _floats(THROWN, log)]
    for _, aim, speed, deg in (t for t in _floats(TRACEPROBE, log, 4)):
        shot["trace_km"].append(aim / 1000.0)
        shot["arrival_ms"].append(speed)
        shot["arrival_deg"].append(deg)
    seen = BAND.findall(log)
    if seen:
        last = seen[-1][0]
        shot["band_deg"] = [float(v) for name, v in seen if name == last]

    for metres, down, cross in _floats(WALK, log, 3):
        shot["walk_m"].append(metres)
        shot["walk_down"].append(down)
        shot["walk_cross"].append(cross)
    for ms, metres in _floats(LAG, log, 2):
        shot["lag_ms"].append(ms)
        shot["lag_m"].append(metres)
    for world, own in _floats(CLOCKS, log, 2):
        shot["clock_gap"].append(world - own)
    for dt, step, sim in _floats(SAMPLE, log, 3):
        shot["dt_ms"].append(dt)

        # The step the coast is actually integrated at, which is not the median frame: the entry
        # runs at 1x and supplies most of the samples, so a median over all of them reports the
        # entry and hides the coast entirely. WarpPolicy holds the world down the first time a
        # frame exceeds the round's preferred step and never lifts it, so which side of that a
        # shot lands on decides its coast step for the whole flight -- and the walk is linear in
        # it. Reported per arm because it is a covariate, not noise: a shot that releases higher
        # trips less often, so it correlates with the arm rather than averaging out.
        if sim > 1.5:
            shot["coast_ms"].append(step)
        shot["sim"].append(sim)
    return shot


# --- statistics -------------------------------------------------------------


def mannwhitney_null(m, n, _memo={}):
    """Exact null distribution of U for sizes m and n, as counts indexed by U.

    count(m, n, u) = count(m-1, n, u-n) + count(m, n-1, u) -- the standard recurrence, which is
    exact where a normal approximation is not. At the sizes a night produces (n <= 30) the whole
    table costs milliseconds, and an approximate p-value at n=12 is the sort of thing that turns
    an unresolved arm into a reported win.
    """
    key = (m, n)
    if key in _memo:
        return _memo[key]
    if m == 0 or n == 0:
        _memo[key] = [1]
        return _memo[key]

    left = mannwhitney_null(m - 1, n)
    right = mannwhitney_null(m, n - 1)
    counts = [0] * (m * n + 1)
    for u, c in enumerate(left):
        counts[u + n] += c
    for u, c in enumerate(right):
        counts[u] += c
    _memo[key] = counts
    return counts


def mannwhitney_p(a, b):
    """Two-sided exact p for 'b is drawn from the same distribution as a'."""
    m, n = len(a), len(b)
    if m == 0 or n == 0:
        return 1.0
    u = sum(1 for x in a for y in b if y < x) + 0.5 * sum(1 for x in a for y in b if y == x)
    counts = mannwhitney_null(m, n)
    total = sum(counts)
    centre = m * n / 2.0
    tail = sum(c for k, c in enumerate(counts) if abs(k - centre) >= abs(u - centre) - 1e-9)
    return min(1.0, tail / total)


def hodges_lehmann(a, b):
    """Median pairwise (b - a), with a distribution-free interval at ALPHA."""
    diffs = sorted(y - x for x in a for y in b)
    if not diffs:
        return 0.0, 0.0, 0.0
    point = statistics.median(diffs)
    counts = mannwhitney_null(len(a), len(b))
    total = sum(counts)
    cum, k = 0, 0
    for u, c in enumerate(counts):
        if (cum + c) / total > ALPHA / 2:
            k = u
            break
        cum += c
    k = min(k, (len(diffs) - 1) // 2)
    return point, diffs[k], diffs[len(diffs) - 1 - k]


def summarise(values):
    if not values:
        return {"n": 0}
    s = sorted(values)
    return {"n": len(s), "median": statistics.median(s), "mean": statistics.fmean(s),
            "min": s[0], "max": s[-1],
            "q1": s[len(s) // 4], "q3": s[(3 * len(s)) // 4]}


# --- the report -------------------------------------------------------------


def load(root):
    root = pathlib.Path(root)
    shots = []
    tsv = root / "shots.tsv"
    if not tsv.exists():
        sys.exit(f"no shots.tsv in {root} -- is that a batch directory?")

    for line in tsv.read_text().splitlines()[1:]:
        parts = line.split("\t")
        if len(parts) < 5:
            continue
        n, block, arm, verdict, dll = parts[0], parts[1], parts[2], parts[3], parts[4]
        rec = read_shot(root / "shots" / f"{n}-{arm}.out", root / "shots" / f"{n}-{arm}.log")
        rec.update(n=n, block=int(block), arm=arm, verdict=verdict, dll=dll)
        shots.append(rec)
    return root, shots


def usable(shot):
    """A shot that produced six impacts. Anything else is a failure, not a miss distance."""
    return (shot["mean"] is not None and shot["arrived"] is not None
            and shot["released"] == shot["arrived"] and shot["arrived"] > 0)


def baseline_name(root, arms):
    order = [line.split("\t")[0] for line in (root / "arms.tsv").read_text().splitlines()[1:]]
    for name in order:
        if name in arms:
            return name
    return sorted(arms)[0]


def gate(root, shots, arms):
    """Arms to stop flying. Removal only -- a win is never called mid-batch."""
    base = baseline_name(root, arms)
    base_scores = [s["mean"] for s in shots if s["arm"] == base and usable(s)]
    dead = []

    for arm in sorted(arms):
        if arm == base:
            continue
        mine = [s for s in shots if s["arm"] == arm]
        if not mine:
            continue
        scores = [s["mean"] for s in mine if usable(s)]

        broken = sum(1 for s in mine if not usable(s))
        if broken >= 2:
            dead.append(arm)
            continue
        if sum(1 for s in scores if s >= WILD_KM) >= 2:
            dead.append(arm)
            continue
        if (len(scores) >= CATASTROPHE_MIN_SHOTS and len(base_scores) >= CATASTROPHE_MIN_SHOTS
                and statistics.median(scores) >= CATASTROPHE_RATIO * statistics.median(base_scores)
                and min(scores) > statistics.median(base_scores)):
            dead.append(arm)
            continue
        if len(scores) >= 6 and len(base_scores) >= 6:
            la = [math.log(v) for v in base_scores]
            lb = [math.log(v) for v in scores]
            if mannwhitney_p(la, lb) < ALPHA and statistics.median(lb) > statistics.median(la):
                dead.append(arm)
    return dead


def compare(root, shots, arms, endpoint):
    base = baseline_name(root, arms)
    scores = {a: [s[endpoint] for s in shots if s["arm"] == a and usable(s)] for a in arms}
    la = [math.log(max(v, 1e-3)) for v in scores[base]]

    print(f"\n  {endpoint} vs the baseline arm flown the same night ({base}, "
          f"n={len(la)}, median {statistics.median(scores[base]):.2f} km)"
          if la else f"\n  {endpoint}: the baseline arm produced no usable shot")
    if not la:
        return

    print(f"  {'arm':<14}{'n':>3} {'median':>8} {'ratio':>8} "
          f"{'interval':>17} {'p':>8}  verdict")
    for arm in sorted(arms):
        if arm == base:
            continue
        lb = [math.log(max(v, 1e-3)) for v in scores[arm]]
        if len(lb) < 3:
            print(f"  {arm:<14}{len(lb):>3} {'-':>8} {'-':>8} {'-':>17} {'-':>8}  TOO FEW")
            continue
        p = mannwhitney_p(la, lb)
        point, lo, hi = hodges_lehmann(la, lb)
        if p < ALPHA:
            verdict = "WIN" if point < 0 else "LOSS"
        else:
            verdict = "UNRESOLVED"
        print(f"  {arm:<14}{len(lb):>3} {statistics.median(scores[arm]):>7.2f} "
              f"{math.exp(point):>8.2f} {math.exp(lo):>7.2f}-{math.exp(hi):<9.2f} "
              f"{p:>8.3f}  {verdict}")

    print(f"\n  ratio below 1.00 is an improvement; the interval is a {100 * (1 - ALPHA):.0f}% "
          f"distribution-free bound on it.")
    print("  UNRESOLVED is not a null result -- read the interval as what the night ruled out.")


def main_effect(shots, factor):
    """The 2x2 factorial's answer for one factor: every arm carrying it against every arm not.

    This is what makes a factorial worth flying. Each shot is used in both factors' comparisons,
    so 48 shots answer two questions at 24-against-24 -- the resolution a one-at-a-time design
    buys for one question with the same budget. An arm is "on" for a factor when its name carries
    that factor, names being `+`-joined lists: base, grav, reopen, grav+reopen.
    """
    on, off = [], []
    for s in shots:
        if not usable(s):
            continue
        (on if factor in s["arm"].split("+") else off).append(s)

    if len(on) < 3 or len(off) < 3:
        print(f"main effect of '{factor}': {len(on)} on, {len(off)} off -- too few to compare")
        return

    for endpoint in ("mean", "spread"):
        a = [math.log(max(s[endpoint], 1e-3)) for s in off]
        b = [math.log(max(s[endpoint], 1e-3)) for s in on]
        p = mannwhitney_p(a, b)
        point, lo, hi = hodges_lehmann(a, b)
        verdict = ("WIN" if point < 0 else "LOSS") if p < ALPHA else "UNRESOLVED"

        print(f"\nmain effect of '{factor}' on {endpoint}")
        print(f"   off  n={len(off):<3} arms {sorted({s['arm'] for s in off})} "
              f"median {statistics.median([s[endpoint] for s in off]):.2f} km")
        print(f"   on   n={len(on):<3} arms {sorted({s['arm'] for s in on})} "
              f"median {statistics.median([s[endpoint] for s in on]):.2f} km")
        print(f"   ratio {math.exp(point):.2f}  interval {math.exp(lo):.2f}-{math.exp(hi):.2f}"
              f"  p {p:.4f}  ->  {verdict}")

        # An arm's effect measured with the other factor off, beside the same with it on. A large
        # gap between the two is an interaction, and a main effect only reads as one number when
        # there is not one. Twelve shots a cell resolves about a kilometre of it and no less.
        cells = defaultdict(list)
        for s in on + off:
            cells[s["arm"]].append(s[endpoint])
        if len(cells) == 4:
            print("   cells: " + "  ".join(
                f"{k} {statistics.median(v):.2f}({len(v)})" for k, v in sorted(cells.items())))


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("directory")
    ap.add_argument("--shots", action="store_true", help="one line of diagnostics per shot")
    ap.add_argument("--gate", action="store_true", help="print arms to drop and exit")
    ap.add_argument("--main", metavar="FACTOR",
                    help="pool the arms into FACTOR on/off and report that main effect")
    args = ap.parse_args()

    root, shots = load(args.directory)
    arms = sorted({s["arm"] for s in shots})

    if args.main:
        main_effect(shots, args.main)
        return

    if args.gate:
        print(" ".join(gate(root, shots, arms)))
        return

    dlls = defaultdict(set)
    for s in shots:
        dlls[s["arm"]].add(s["dll"])

    # Two arms that flew one binary is not a comparison, and it is silent otherwise: the night
    # runs to completion and reports a dead heat between an arm and itself.
    shared = {h for a in arms for h in dlls[a] if sum(h in dlls[b] for b in arms) > 1}

    print(f"== {len(shots)} shots in {root}")
    for line in (root / "batch.tsv").read_text().splitlines():
        print("   " + line.replace("\t", ": "))

    print("\n== what flew")
    for arm in arms:
        mine = [s for s in shots if s["arm"] == arm]
        ok = [s for s in mine if usable(s)]
        mark = "" if len(dlls[arm]) == 1 else f"  ** {len(dlls[arm])} DIFFERENT BINARIES **"
        if dlls[arm] & shared:
            mark += "  ** SHARES A BINARY WITH ANOTHER ARM **"
        print(f"   {arm:<14} {len(mine):>3} flown, {len(ok):>3} usable, "
              f"dll {sorted(dlls[arm])[0][:12]}{mark}")

    # The pick-up is the confound that cost this project the most: the same save resumed 35 s
    # further on is a differently conditioned arc worth 164 km, and nothing downstream of it can
    # be compared across shots that started in different places.
    pick = [(s["pickup_km"], s["pickup_ms"]) for s in shots if s["pickup_km"] is not None]
    if pick:
        kms = {round(k) for k, _ in pick}
        mss = {round(v / 10) * 10 for _, v in pick}
        note = "identical" if len(kms) == 1 and len(mss) == 1 else "** VARIES -- SHOTS ARE NOT COMPARABLE **"
        print(f"\n== pick-up: {sorted(kms)} km, {sorted(mss)} m/s -- {note}")

    print("\n== per arm")
    for endpoint in ("mean", "spread", "worst"):
        print(f"\n   {endpoint}")
        for arm in arms:
            st = summarise([s[endpoint] for s in shots if s["arm"] == arm and usable(s)])
            if not st["n"]:
                print(f"     {arm:<14} no usable shot")
                continue
            print(f"     {arm:<14} n={st['n']:<3} median {st['median']:.2f}  "
                  f"mean {st['mean']:.2f}  range {st['min']:.2f}-{st['max']:.2f}")

    print("\n== attribution (medians over usable shots)")
    print(f"   {'arm':<14}{'residual':>9}{'own km':>8}{'trim rel':>9}{'probe km':>9}"
          f"{'thrown':>8}{'arr deg':>8}{'band deg':>9}{'walk m':>8}{'lag m':>8}"
          f"{'dt ms':>7}{'coast ms':>9}")
    for arm in arms:
        mine = [s for s in shots if s["arm"] == arm and usable(s)]
        if not mine:
            continue

        def med(key, per_warhead=False):
            vals = []
            for s in mine:
                v = s[key]
                if per_warhead:
                    vals.extend(v)
                elif v is not None:
                    vals.append(v)
            return statistics.median(vals) if vals else float("nan")

        print(f"   {arm:<14}{med('residual'):>9.3f}{med('own_km'):>8.2f}"
              f"{med('trim_release'):>9.3f}{med('probe_km', True):>9.2f}"
              f"{med('thrown', True):>8.0f}{med('arrival_deg', True):>8.1f}"
              f"{med('band_deg', True):>9.2f}"
              f"{med('walk_m', True):>8.0f}{med('lag_m', True):>8.0f}"
              f"{med('dt_ms', True):>7.1f}{med('coast_ms', True):>9.1f}")

    if args.shots:
        print("\n== every shot")
        for s in sorted(shots, key=lambda r: r["n"]):
            head = (f"   {s['n']} b{s['block']} {s['arm']:<12} {s['verdict']:<9}")
            if not usable(s):
                print(head + f"  {s['arrived']}/{s['released']} arrived -- not scored")
                continue
            print(head + f"  mean {s['mean']:.2f}  spread {s['spread']:.2f}  "
                  f"worst {s['worst']:.2f}  residual {s['residual'] if s['residual'] is not None else float('nan'):.3f}  "
                  f"lag {statistics.median(s['lag_m']) if s['lag_m'] else float('nan'):.0f} m")

    compare(root, shots, arms, "mean")
    compare(root, shots, arms, "spread")


if __name__ == "__main__":
    main()
