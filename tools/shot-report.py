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
# baseline ever recorded on the 26.5S,64.0W shot is 3.43 km over 26 shots. Two of them from one
# arm is that arm. The floor holds only until the baseline has flown: past that the same night's
# baseline sets it, because a target where the control lands at 5 km is a target where 4 km is
# an ordinary shot.
WILD_KM = 4.0
WILD_RATIO = 2.0

# A round arriving at angle g covers cot(g) of ground for every unit it descends, so ground that
# falls away downrange at tan(a) moves the impact by 1/(tan g - tan a) per unit of trajectory
# error. Flat ground gives cot(g); as tan(a) approaches tan(g) the round grazes and the impact
# point diverges. Past this much amplification a night is measuring the hillside as much as the
# guidance, and the miss distribution goes bimodal in a way that reads as scatter.
GRAZE_AMPLIFICATION = 2.0

# Below this span along the impacts' own axis there is not enough ground under the night to fit a
# slope to. A tight group is good news and no evidence about the terrain.
MIN_TERRAIN_SPAN_M = 100.0

# The bodies flown here are Earth-sized, and this only ever converts degrees to a local metre
# scale for the fit -- a few parts in a thousand of radius does not move the slope.
BODY_RADIUS_M = 6371000.0


# --- parsing ----------------------------------------------------------------

# One line per rocket when several fly in one world. A run used to be one shot, so read_shot
# took the FIRST verdict in the file -- which with eight flights scores one and silently discards
# seven. Silent undercounting reads exactly like a smaller night.
PERFLIGHT = re.compile(r"FLIGHT (.+?) :: (PASS|FAIL) (.*)$", re.M)

# "mirv: GeoSat FAT 2 flies arm trim (TrimCeilingFromBudget=true)". The craft is non-greedy and the
# arm is one token, because craft names carry spaces and arm names do not.
FLIESARM = re.compile(r"^.*?: (.+?) flies arm (\S+)", re.M)

# "GeoSat FAT 3_1" is the craft plus its launcher ordinal, and the FLIGHT line says "GeoSat FAT 3".
# Matching one against the other without this fails silently and every per-craft reading falls back
# to a whole-log one -- which reported 8 of 8 corrections ending on the clearance for a run whose
# arm had 0 of 4.
ORDINAL = re.compile(r"_\d+$")


def _craft(name):
    return ORDINAL.sub("", name.strip())


# "trimming the bus on <craft>: still N m from the spent stack after N s" -- the clearance giving up,
# which is the one ending that leaves no post-boost line to read.
GAVEUP = re.compile(r"trimming the bus on (.+?): still [\d.]+ m from the spent stack", re.M)

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

# What the round's own clock made of the flight beside what its predictor expected. The two use the
# same ground and the same air; a round that arrives early against its own probe is short by that
# time times its ground speed, which at a 7 deg arrival is most of the walk.
# The walk at the END of the flight, signed, off the landing line alone.
#
# Two traps, both of which have misled a reading of this batch. The trace prints a walk line at
# every sample, so a median over all of them medians an accumulating quantity rather than reporting
# the final one. And the leading figure is a MAGNITUDE: a round 3 km short and one 700 m long read
# 2958 and 679, whose difference is not the 3637 m of swing between them. Sign first.
FINAL_WALK = re.compile(
    r"landed at\s+[-\d.,]+\s*\|\s*[-\d.]+\s*m from the aim\s*\|\s*"
    r"walk from the release probe\s+[-\d.]+\s*m\s*\(([-+\d.]+)\s*down,\s*([-+\d.]+)\s*cross\)")

FLIGHT = re.compile(
    r"flight\s+([-\d.]+)s by the world clock,\s+([-\d.]+)s by its own, "
    r"probe said\s+([-\d.]+)s")
LAG = re.compile(r"lag\s+([-\d.]+)ms\s*=\s*([-\d.]+)\s*m at")
CLOCKS = re.compile(r"flight\s+([\d.]+)s by the world clock,\s*([\d.]+)s by its own")
SAMPLE = re.compile(r"dt=([\d.]+)ms step=([\d.]+)ms sim=([\d.]+)x")
BAND = re.compile(
    r"DEBUG\s+(\S+)\s+control:.*?pointing band\s+([\d.]+)\s*deg")
BANNER = re.compile(r"KSArmory\s+(\S+)\s+built for KSA\s+(\S+),\s*running\s+(\S+)")

# Where each warhead stopped, and how high the ground was there. Together they make the target's
# own relief measurable out of a night flown for something else. The landing line carries the
# signed downrange walk as well, which is what orients the axis without needing a frame or the
# body's rotation rate.
IMPACT = re.compile(
    r"warhead trace: round \d+ landed at\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\|\s*"
    r"[-\d.]+\s*m from the aim\s*\|\s*walk from the release probe\s+[-\d.]+\s*m\s*"
    r"\(([-+\d.]+)\s*down,\s*([-+\d.]+)\s*cross\)")
# What ended the post-boost correction, which is the one thing that decides whether the aim loop
# was allowed to finish. Every Finish() in Sim/PostBoostAim.cs, in the order it is tested, plus the
# two numbers that say how near it got: the passes it ran and what the trim was still owed. A shot
# whose loop the budget or the clock cut off is a different shot from one that converged, and
# nothing else in this report separates them.
# The craft is optional because logs written before it was added carry no name, and those batches
# are still read. When it is there the terminator is attributable to one flight; when it is not,
# every rocket in the shot gets the same answer -- which is what `why_it_ended` says it is doing.
POSTBOOST = re.compile(r"INFO  post-boost(?: on (.+?))?: (.+)$", re.M)
PASS = re.compile(r"post-boost: correcting the aim, ([\d.]+) km out \(pass (\d+)\)")
TOGAIN = re.compile(r"trimming ([\d.]+) m/s on")

WHY = [
    ("budget",   re.compile(r"^released on .* of trim")),
    ("clock",    re.compile(r"^released after [\d.]+ s of correcting")),
    ("unsteady", re.compile(r"^released after [\d.]+ s of the bus not holding still")),
    ("trim",     re.compile(r"the trim (stopped|refused)")),
    ("settled",  re.compile(r"^aim settled ")),
    ("noimprov", re.compile(r"^\d+ passes without beating")),
    ("cycles",   re.compile(r"^released after \d+ corrections")),
    ("payback",  re.compile(r"^[\d.]+ m out, under the ")),
]

ABANDONED = re.compile(r"still [\d.]+ m from the spent stack after [\d.]+ s")


def why_it_ended(log, craft=None):
    """Which stopping rule fired, its passes, and what the trim still owed when it did.

    `craft` picks out one flight's own ending. Without it the first ending in the log is returned
    for every rocket in the world, which reports one craft's outcome eight times -- 8z's n=40 was
    six shots wearing eight coats. Logs written before the line carried a name have no craft to
    match, and fall back to that shared reading rather than to nothing.
    """
    end = None
    anyNamed = any(m.group(1) for m in POSTBOOST.finditer(log))
    named = craft is not None and anyNamed
    want = _craft(craft) if craft else None

    for m in POSTBOOST.finditer(log):
        if named and _craft(m.group(1) or "") != want:
            continue
        if any(r.search(m.group(2)) for _, r in WHY):
            end = m
            break

    # No Finish() at all is not a gap in this parser -- it is the separation clearance releasing
    # the warheads over the top of a loop that never got to decide. MIRV-NEXT item 8p, where it
    # was 22 of 24 shots, and it reads as `clearance` rather than as a missing measurement.
    if end is None:
        # This craft's own giving-up, when it says which craft. Falling back to any of them is what
        # made every unattributed flight read as abandoned.
        cut = next((m for m in GAVEUP.finditer(log) if want is None or _craft(m.group(1)) == want),
                   None)

        if cut is not None:
            named = True
        else:
            if named:
                return None, None, None, True
            cut = ABANDONED.search(log)

        if cut is None:
            return None, None, None, named
        name, at = "clearance", cut.start()
    else:
        name = next((n for n, r in WHY if r.search(end.group(2))), "other")
        at = end.start()

    passes = max((int(g) for _, g in PASS.findall(log[:at])), default=0)
    owed = TOGAIN.findall(log[:at])
    return name, passes, (float(owed[-1]) if owed else None), named


SURFACE = re.compile(
    r"surface at the landing point: the round stopped on\s*([\d.]+)\s*m")


def _floats(pattern, text, groups=1):
    out = []
    for m in pattern.finditer(text):
        out.append(tuple(float(g) for g in m.groups()[:groups]))
    return out


def split_flights(out_path, log_path):
    """One record per rocket that flew, or one for the whole run when only one did.

    Several rockets in one world are several shots, and they are NOT independent draws -- they
    share the frame pacing, the warp decisions and the solver load. Treated as independent by a
    rank test they inflate n without inflating information, so they are reported with the craft
    that flew them and `--main` pools on the arm rather than on the flight.
    """
    text = out_path.read_text(errors="replace") if out_path.exists() else ""
    log = log_path.read_text(errors="replace") if log_path.exists() else ""

    # The scenario reports through Log.Info and scenario.sh copies those same lines to its
    # stdout, so BOTH files carry every FLIGHT line. Scanning the pair counted each flight twice,
    # which doubles n without adding an observation. Invisible with one rocket, because that path
    # returns the whole run as a single shot below.
    flights = PERFLIGHT.findall(text) or PERFLIGHT.findall(log)

    # Which variant each craft drew, when the run was flown paired. Read from the same pair of
    # files for the same reason: the scenario reports through Log.Info and scenario.sh copies it.
    drew = dict(FLIESARM.findall(text) or FLIESARM.findall(log))

    # One rocket: the run is the shot, and the id is unchanged so old batches read as before.
    if len(flights) <= 1:
        craft = flights[0][0] if flights else ""
        rec = read_shot(out_path, log_path, craft or None)
        rec["within"] = drew.get(craft)
        return [(rec, "", craft)]

    out = []

    for i, (craft, _passfail, said) in enumerate(flights):
        rec = read_shot(out_path, log_path, craft)

        # The verdict and the terminator are this flight's own -- the terminator because the
        # post-boost line now names its craft, which is what makes 8z's table a per-flight count
        # rather than one shot's outcome worn by eight rockets. The remaining columns are still
        # read over the whole log and describe the world.
        m = VERDICT.search(said)
        if m:
            rec["worst"], rec["best"], rec["mean"], rec["spread"] = (float(g) for g in m.groups())

        a = ARRIVED.search(said)
        if a:
            rec["arrived"], rec["released"] = int(a.group(1)), int(a.group(2))

        # Where this rocket sat in the roster. 8y measured a 175x gradient down it -- first rocket
        # 0.09 km, eighth 15.81 km, monotone across every arm -- and every arm comparison since has
        # had to be laid out so both variants sit on both ends of it. Kept so a run can say whether
        # the gradient is still there rather than assuming the finding of one night.
        rec["seat"] = i

        out.append((rec, chr(ord("a") + i) if i < 26 else f".{i}", craft))

    for rec, _suffix, craft in out:
        rec["within"] = drew.get(craft)

    return out


def read_shot(out_path, log_path, craft=None):
    """Everything one shot is worth attributing, from its stdout and its copied-out log."""
    shot = {"mean": None, "spread": None, "worst": None, "best": None,
            "arrived": None, "released": None, "pickup_km": None, "pickup_ms": None,
            "residual": None, "own_km": None, "trim_split": None, "trim_release": None,
            "offline": [], "probe_km": [], "thrown": [], "arrival_deg": [],
            "arrival_ms": [], "trace_km": [], "walk_m": [], "walk_down": [], "walk_cross": [],
            "early_s": [], "final_down": [], "final_cross": [],
            "band_deg": [], "impacts": [],
            "why": None, "passes": None, "owed": None, "why_named": False,
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
    shot["why"], shot["passes"], shot["owed"], shot["why_named"] = why_it_ended(log, craft)

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

    for down, cross in _floats(FINAL_WALK, log, 2):
        shot["final_down"].append(down)
        shot["final_cross"].append(cross)

    # The surface line follows the landing it belongs to and carries no round number, so the two
    # pair by order. A landing with no surface line after it is dropped rather than guessed at.
    pending = None
    for line in log.splitlines():
        m = IMPACT.search(line)
        if m:
            pending = (float(m.group(1)), float(m.group(2)), float(m.group(3)))
            continue
        m = SURFACE.search(line)
        if m and pending is not None:
            shot["impacts"].append(pending + (float(m.group(1)),))
            pending = None

    # Positive is early: the round beat the flight time its own predictor gave it.
    for _world, own, probe in _floats(FLIGHT, log, 3):
        shot["early_s"].append(probe - own)
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


def _sign_p(better, worse):
    """Two-sided exact p for 'the variant is as likely to lose as to win', ties dropped."""
    n = better + worse
    if n == 0:
        return 1.0

    def choose(k):
        return math.comb(n, k)

    total = 2 ** n
    k = min(better, worse)
    tail = sum(choose(i) for i in range(0, k + 1))
    return min(1.0, 2.0 * tail / total)


def wilcoxon_p(values):
    """Two-sided exact signed-rank p for 'these differences are centred on zero'.

    The sign test throws the magnitudes away, and for an arm whose wins are large and whose losses
    are small that is most of the evidence: 15 of 24 is p=0.307 on signs alone while the same shots
    carry a reproducible 0.76x. This keeps the magnitudes and still assumes nothing about the
    distribution's shape.

    Exact rather than normal-approximated, because n here is a couple of dozen and the tail is
    where the answer lives. The null distribution of the signed-rank sum is the coefficient list of
    the product of (1 + x^i), which is a few hundred integers at this size.
    """
    v = [x for x in values if x != 0.0]
    n = len(v)
    if n == 0:
        return 1.0

    ranks = _ranks([abs(x) for x in v])
    w = sum(r for x, r in zip(v, ranks) if x > 0)

    # Coefficients of prod(1 + x^r): counts[k] is how many sign assignments give rank sum k.
    counts = [1]
    for r in ranks:
        shifted = [0] * int(r) + counts
        counts = counts + [0] * int(r)
        counts = [a + b for a, b in zip(counts, shifted)]

    total = sum(counts)
    centre = sum(ranks) / 2.0
    tail = sum(c for k, c in enumerate(counts) if abs(k - centre) >= abs(w - centre) - 1e-9)
    return min(1.0, tail / total)


def _ranks(values):
    """Ranks from 1, ties given their average -- which is what the signed-rank test needs."""
    order = sorted(range(len(values)), key=lambda i: values[i])
    out = [0.0] * len(values)
    i = 0
    while i < len(order):
        j = i
        while j + 1 < len(order) and values[order[j + 1]] == values[order[i]]:
            j += 1
        share = (i + j) / 2.0 + 1.0
        for k in range(i, j + 1):
            out[order[k]] = share
        i = j + 1
    return out


def _median_interval(values):
    """A distribution-free interval for the median, from the same binomial the sign test uses."""
    v = sorted(values)
    n = len(v)
    if n == 0:
        return 0.0, 0.0, 0.0

    total = 2 ** n
    k = 0
    cum = 0
    for i in range(n):
        cum += math.comb(n, i)
        if 2.0 * cum / total > ALPHA:
            break
        k = i + 1

    k = min(k, (n - 1) // 2)
    return statistics.median(v), v[k], v[n - 1 - k]


# Above this, every night flown so far has been in the bad regime and below it in the good one.
# Not a threshold anything is tuned to -- the observed frame times cluster at 21-22 ms and 27-30 ms
# with nothing in between, and 24 is the gap.
SLOW_FRAME_MS = 24.0


def regime(shots):
    """The session's frame time, and whether it is one where the correction loop runs at all.

    Frame time separates the good sessions from the bad ones -- 9.25 km against 20.49 km over 57
    shots -- and does NOT predict the miss within a session, where the rank correlation is +0.04,
    +0.06, +0.12 and -0.03 across the four nights that have enough shots to ask. So it marks the
    regime rather than driving each shot, and whether it is the cause or a symptom of whatever the
    machine is doing is not established.

    What is established is what happens to the correction: 0.23-0.25 passes per flight in the slow
    regime against 1.17-3.38 in the fast one. An arm that acts on the post-boost loop cannot be
    measured in a session where the loop does not run, which is what this exists to say before
    somebody reads a null as an answer.
    """
    dts = [statistics.median(s["dt_ms"]) for s in shots if s.get("dt_ms")]
    if not dts:
        return None
    return statistics.median(dts)


def _say_regime(shots):
    dt = regime(shots)
    if dt is None:
        return

    passes = [s["passes"] for s in shots if s.get("passes") is not None]
    said = f"   frame time: {dt:.1f} ms"
    if passes:
        said += f", {statistics.median(passes):.0f} correction pass(es) at the median shot"
    print(said)

    # The frame time is a proxy; what it was ever a proxy FOR is whether the post-boost loop got to
    # run. That is directly observable, so it is asked rather than inferred -- a session that ran the
    # correction is a session an arm acting on the correction can be measured in, however slow the
    # frames were. Before KeepOutCoversTheClearance shipped the two were the same question: a slow
    # night ran 0.24 passes a flight against 1.4 in a fast one and landed 20.5 km against 9.3.
    if dt >= SLOW_FRAME_MS:
        ran = statistics.median(passes) if passes else 0.0

        if ran >= 1.0:
            print(f"   NOTE: {dt:.0f} ms is the old slow regime, but the loop ran anyway "
                  f"({ran:.0f} passes at the median shot).")
            print("   That threshold was calibrated when a slow night meant the correction did not")
            print("   run at all. It does now, so this session is readable.")
        else:
            print(f"   WARNING: at or above {SLOW_FRAME_MS:.0f} ms this session is in the slow "
                  "regime, and")
            print(f"   the median shot ran {ran:.1f} correction passes. An arm that acts on that")
            print("   loop cannot be measured here, and a null from this session is not a null")
            print("   result.")
    print()


def paired(root, shots):
    """Compare the variants flown INSIDE each shot, which is the only comparison this
    instrument currently supports.

    The between-run test compares an arm's shots against a baseline's shots flown at other
    moments, and the moment turned out to dominate: the same baseline read 14.49 km and 5.43 km
    on identical code three hours apart, a 2.7x swing, while the arm under test moved by less and
    reversed sign between the two batches. Nothing under about 3x is readable that way.

    Rockets sharing a world share the frame pacing, the warp history, the solver load and the
    target. So one shot yields one ratio with all of that cancelled, and the test is over SHOTS --
    a sign test on which variant won each of them. Six shots can reach p=0.031, which the
    between-run test could not reach at any n this project can afford.
    """
    spec = ""
    for line in (root / "batch.tsv").read_text().splitlines():
        if line.startswith("paired\t"):
            spec = line.split("\t", 1)[1].strip()

    if spec in ("", "<none>"):
        sys.exit("this batch was not flown paired -- there is no within-run split to report")

    order = [piece.split(":")[0].strip() for piece in spec.split("|") if piece.strip()]

    groups = defaultdict(lambda: defaultdict(list))
    for r in shots:
        if r.get("within") and usable(r):
            groups[_shot_id(r["n"])][r["within"]].append(r["mean"])

    if not groups:
        sys.exit("no flight in this batch says which variant it flew")

    base = order[0]

    print(f"== paired within {len(groups)} shot(s) in {root}")
    print(f"   spec: {spec}")
    print(f"   baseline: {base}")
    _say_regime(shots)

    # Pooled, for scale only. It is NOT the comparison: pooling across shots puts the
    # between-shot swing back into the number the ratio was constructed to remove.
    pooled = defaultdict(list)
    for per_arm in groups.values():
        for name, values in per_arm.items():
            pooled[name].extend(values)

    print("   arm            flights   median km   (pooled, for scale only)")
    for name in order:
        if name in pooled:
            print(f"   {name:<14} {len(pooled[name]):>7}   {statistics.median(pooled[name]):>9.2f}")
    print()

    for name in order[1:]:
        ratios, wins, losses = [], 0, 0

        for shot, per_arm in sorted(groups.items()):
            # Both variants have to have flown in the SHOT for it to be a pair. A shot where one
            # of them lost every rocket is not a tie, it is not an observation.
            if base not in per_arm or name not in per_arm:
                continue

            a = statistics.median(per_arm[base])
            b = statistics.median(per_arm[name])
            if a <= 0 or b <= 0:
                continue

            ratios.append(math.log(b / a))
            if b < a:
                wins += 1
            elif b > a:
                losses += 1

        if not ratios:
            print(f"   {name}: no shot flew both it and {base}")
            continue

        point, lo, hi = _median_interval(ratios)
        p = _sign_p(wins, losses)
        w = wilcoxon_p(ratios)

        # The rank test is the one to read. The sign test is kept beside it because it assumes less
        # and because every number in docs/MIRV-NEXT.md before 8af was scored on it.
        best = min(p, w)

        print(f"   {name} vs {base}: {math.exp(point):.2f}x"
              f"   [{math.exp(lo):.2f}, {math.exp(hi):.2f}] at {int((1 - ALPHA) * 100)}%")
        print(f"      won {wins} of {len(ratios)} paired shots, "
              f"sign p={p:.3f}, signed-rank p={w:.3f}"
              + ("   RESOLVED" if best <= 0.05 else "   unresolved"))
        print("      per shot: "
              + ", ".join(f"{math.exp(r):.2f}" for r in ratios))
        print()

    _say_seats(shots)
    _say_terminators(shots, order)

    if len(groups) < 6:
        print(f"   NOTE: {len(groups)} shots cannot reach p<=0.05 on a sign test. Six is the floor,")
        print("   and that is only if the variant wins every one of them.")


def _say_seats(shots):
    """What each seat in the roster was worth, and whether the gradient down it is still there.

    8y found a 175x spread between the first rocket and the eighth, monotone, across every arm --
    a term far larger than anything being tested, which is the whole reason arms alternate down the
    roster rather than sitting in blocks. It is not a law: it was attributed to SeparationClearance
    abandoning the trim, which KeepOutCoversTheClearance removes. So it is worth re-reading rather
    than carrying forward, and it costs nothing to ask of a run already flown.

    Reported as a rank correlation because the claim is monotone rather than linear, and the
    interesting answer is "no trend" as much as a number.
    """
    rows = [s for s in shots if s.get("seat") is not None and usable(s)]
    seats = sorted({s["seat"] for s in rows})
    if len(seats) < 3:
        return

    print("   what each seat in the roster was worth")
    print(f"   {'seat':>6}{'n':>5}{'median km':>12}")
    for i in seats:
        got = [s["mean"] for s in rows if s["seat"] == i]
        if got:
            print(f"   {i + 1:>6}{len(got):>5}{statistics.median(got):>12.3f}")

    rho, p = _spearman([s["seat"] for s in rows], [s["mean"] for s in rows])

    if math.isnan(rho):
        print()
        return

    print(f"   rank correlation seat vs miss: rho={rho:+.2f}, p={p:.3f}"
          + ("   the gradient is still there" if p <= 0.05 and rho > 0
             else "   no gradient at this n"))
    print()


def _spearman(xs, ys):
    """Spearman's rho and a normal-approximation two-sided p. NaN when it cannot be computed."""
    n = len(xs)
    if n < 4:
        return float("nan"), float("nan")

    rx, ry = _ranks(xs), _ranks(ys)
    mx, my = statistics.fmean(rx), statistics.fmean(ry)

    num = sum((a - mx) * (b - my) for a, b in zip(rx, ry))
    den = math.sqrt(sum((a - mx) ** 2 for a in rx) * sum((b - my) ** 2 for b in ry))

    if den <= 0.0:
        return float("nan"), float("nan")

    rho = num / den

    # The t approximation, which is what everything else here would use at this n.
    if abs(rho) >= 1.0:
        return rho, 0.0

    t = rho * math.sqrt((n - 2) / (1.0 - rho * rho))
    return rho, 2.0 * (1.0 - _student_cdf(abs(t), n - 2))


def _ranks(values):
    """Fractional ranks, so ties do not bias the correlation."""
    order = sorted(range(len(values)), key=lambda i: values[i])
    out = [0.0] * len(values)

    i = 0
    while i < len(order):
        j = i
        while j + 1 < len(order) and values[order[j + 1]] == values[order[i]]:
            j += 1
        shared = (i + j) / 2.0 + 1.0
        for k in range(i, j + 1):
            out[order[k]] = shared
        i = j + 1

    return out


def _student_cdf(t, df):
    """Student's t CDF via the regularised incomplete beta, which math.lgamma gives cheaply."""
    x = df / (df + t * t)
    return 1.0 - 0.5 * _betainc(0.5 * df, 0.5, x)


def _betainc(a, b, x):
    """Regularised incomplete beta, by the continued fraction in Numerical Recipes."""
    if x <= 0.0:
        return 0.0
    if x >= 1.0:
        return 1.0

    front = math.exp(math.lgamma(a + b) - math.lgamma(a) - math.lgamma(b)
                     + a * math.log(x) + b * math.log(1.0 - x))

    if x < (a + 1.0) / (a + b + 2.0):
        return front * _betacf(a, b, x) / a

    return 1.0 - math.exp(math.lgamma(a + b) - math.lgamma(a) - math.lgamma(b)
                          + b * math.log(1.0 - x) + a * math.log(x)) * _betacf(b, a, 1.0 - x) / b


def _betacf(a, b, x):
    tiny = 1e-30
    qab, qap, qam = a + b, a + 1.0, a - 1.0
    c, d = 1.0, 1.0 - qab * x / qap
    if abs(d) < tiny:
        d = tiny
    d = 1.0 / d
    h = d

    for m in range(1, 200):
        m2 = 2 * m
        aa = m * (b - m) * x / ((qam + m2) * (a + m2))
        d = 1.0 + aa * d
        if abs(d) < tiny:
            d = tiny
        c = 1.0 + aa / c
        if abs(c) < tiny:
            c = tiny
        d = 1.0 / d
        h *= d * c

        aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2))
        d = 1.0 + aa * d
        if abs(d) < tiny:
            d = tiny
        c = 1.0 + aa / c
        if abs(c) < tiny:
            c = tiny
        d = 1.0 / d
        delta = d * c
        h *= delta

        if abs(delta - 1.0) < 3e-7:
            break

    return h


def _say_terminators(shots, order):
    """Which rule ended each arm's corrections, and what that ending was worth.

    8z's table, and it is the sharpest thing this instrument produces: a correction that ran to
    completion landed at 140 m and every other ending at 5 to 45 km. It is a COUNT rather than a
    median, so it separates on far fewer shots than the miss does -- an arm that doubles the number
    of loops that finish is visible long before its median moves.

    Per flight rather than per shot, which needs the post-boost line to name its craft. Batches
    flown before it did read one craft's ending for all eight and are not counted here.
    """
    rows = [s for s in shots if s.get("within") and s.get("why") and usable(s)]
    if not rows:
        return

    # A batch flown before the post-boost line named its craft has one ending read for the whole
    # shot and handed to every rocket in it. Splitting that by arm produces a table that looks like
    # evidence and is one flight's outcome wearing eight coats -- which is the mistake 8w's list
    # exists to stop being made a fifth time.
    if not any(s.get("why_named") for s in rows):
        print("   what ended each arm's corrections: NOT AVAILABLE for this batch -- its logs")
        print("   predate the post-boost line naming its craft, so one ending is read per shot and")
        print("   shared by every rocket in it. Re-fly to get this table.")
        print()
        return

    endings = sorted({s["why"] for s in rows})

    print("   what ended each arm's corrections (per flight)")
    print(f"   {'arm':<14} " + "".join(f"{e:>10}" for e in endings))

    for name in order:
        mine = [s for s in rows if s["within"] == name]
        if not mine:
            continue
        counts = "".join(f"{sum(1 for s in mine if s['why'] == e):>10}" for e in endings)
        print(f"   {name:<14} {counts}")

    print()
    print(f"   {'ending':<14}{'n':>5}{'median km':>12}")
    for e in endings:
        got = [s["mean"] for s in rows if s["why"] == e]
        print(f"   {e:<14}{len(got):>5}{statistics.median(got):>12.2f}")
    print()


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
        out_path = root / "shots" / f"{n}-{arm}.out"
        log_path = root / "shots" / f"{n}-{arm}.log"

        for rec, suffix, craft in split_flights(out_path, log_path):
            # Kept as text. shot-batch.sh writes 'x' for a shot re-allocated off a dropped arm, so
            # anything numeric here refuses to read the one batch shape the gate actually produces.
            rec.update(n=n + suffix, block=block, arm=arm, verdict=verdict, dll=dll, craft=craft)
            shots.append(rec)
    return root, shots


def usable(shot):
    """A shot that produced six impacts. Anything else is a failure, not a miss distance."""
    return (shot["mean"] is not None and shot["arrived"] is not None
            and shot["released"] == shot["arrived"] and shot["arrived"] > 0)


def baseline_name(root, arms):
    # A batch aborted before its first shot landed has arms declared and none flown, which is a
    # night that answered nothing rather than a malformed directory.
    if not arms:
        sys.exit(f"no shots flown in {root} -- the batch was aborted before its first one landed")

    order = [line.split("\t")[0] for line in (root / "arms.tsv").read_text().splitlines()[1:]]
    for name in order:
        if name in arms:
            return name
    return sorted(arms)[0]


def _shot_id(n):
    """The run a flight record came from. split_flights suffixes a, b, c... per rocket."""
    return re.sub(r"(?:[a-z]|\.\d+)$", "", n)


def by_shot(records):
    """One score per SHOT, and one broken count per shot -- never per flight.

    Every constant in the gate is a count of shots, and eight rockets in one world are one shot:
    they share the frame pacing, the warp decisions and the solver load, which is the same reason
    `--main` pools on the arm. Left per-flight, one eight-rocket run presents as eight, so
    `broken >= 2` fires on a single run that happened to meet two intercepted warheads and the arm
    is dropped on n=1. Flown 2026-08-28: the gate dropped the best arm of the night -- 0.04 km
    against a 4.26 km baseline -- after one shot.

    A shot scores the median of the flights that arrived, and counts as broken only when NOT ONE
    of its rockets produced a usable group. A warhead lost to the site being shot at is a fact
    about the target, shared by every arm flown at it, and is not evidence against the arm.
    """
    groups = defaultdict(list)
    for r in records:
        groups[_shot_id(r["n"])].append(r)

    scores, broken = [], 0

    for _, recs in sorted(groups.items()):
        arrived = [r["mean"] for r in recs if usable(r)]
        if arrived:
            scores.append(statistics.median(arrived))
        else:
            broken += 1

    return scores, broken


def gate(root, shots, arms):
    """Arms to stop flying. Removal only -- a win is never called mid-batch."""
    base = baseline_name(root, arms)
    base_scores, _ = by_shot([s for s in shots if s["arm"] == base])
    dead = []

    for arm in sorted(arms):
        if arm == base:
            continue
        mine = [s for s in shots if s["arm"] == arm]
        if not mine:
            continue
        scores, broken = by_shot(mine)

        if broken >= 2:
            dead.append(arm)
            continue
        # 4 km is a fact about one target, not about the mod. On a geometry where the baseline
        # itself lands past it, an absolute floor drops arms that match the control -- and the
        # baseline is never a candidate, so the asymmetry keeps the wrong one.
        wild = WILD_KM
        if len(base_scores) >= 2:
            wild = max(wild, WILD_RATIO * statistics.median(base_scores))
        if sum(1 for s in scores if s >= wild) >= 2:
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


# --- terrain ----------------------------------------------------------------


def _slope_fit(xs, ys):
    """Least-squares slope of ys on xs, with the standard error that says whether to believe it."""
    n = len(xs)
    mx, my = statistics.mean(xs), statistics.mean(ys)
    sxx = sum((x - mx) ** 2 for x in xs)
    if n < 3 or sxx <= 0:
        return None
    slope = sum((xs[i] - mx) * (ys[i] - my) for i in range(n)) / sxx
    resid = [ys[i] - (my + slope * (xs[i] - mx)) for i in range(n)]
    se = math.sqrt(sum(r * r for r in resid) / (n - 2) / sxx)
    return slope, se, statistics.pstdev(resid)


def terrain(shots):
    """The ground under a night's impacts, and whether it is steep enough to have shaped them.

    The axis is the impacts' own principal axis rather than a computed ground track: it needs
    neither the body's rotation rate nor a release state, and it is the direction the impacts
    actually move in, which is the one the conditioning is about. Its sign is the one thing the
    scatter cannot give -- a principal axis is a line -- so the walk's signed downrange component
    orients it.
    """
    pts = [q for s in shots for q in s["impacts"]]
    degs = [d for s in shots for d in s["arrival_deg"]]
    if len(pts) < 4 or not degs:
        return None

    lat0 = statistics.mean(q[0] for q in pts)
    lon0 = statistics.mean(q[1] for q in pts)
    east_m = math.radians(1.0) * BODY_RADIUS_M * math.cos(math.radians(lat0))
    north_m = math.radians(1.0) * BODY_RADIUS_M
    xy = [((lo - lon0) * east_m, (la - lat0) * north_m, down, r) for la, lo, down, r in pts]

    me = statistics.mean(q[0] for q in xy)
    mn = statistics.mean(q[1] for q in xy)
    cee = sum((q[0] - me) ** 2 for q in xy)
    cnn = sum((q[1] - mn) ** 2 for q in xy)
    cen = sum((q[0] - me) * (q[1] - mn) for q in xy)
    th = 0.5 * math.atan2(2 * cen, cee - cnn)
    ux, uy = math.cos(th), math.sin(th)

    along = [(q[0] - me) * ux + (q[1] - mn) * uy for q in xy]
    walk = [q[2] for q in xy]
    orient = _slope_fit(walk, along)
    if orient and orient[0] < 0:
        ux, uy, along = -ux, -uy, [-a for a in along]

    span = max(along) - min(along)
    gamma = statistics.mean(degs)
    out = {"n": len(pts), "lat": lat0, "lon": lon0, "span": span, "gamma": gamma,
           "bearing": math.degrees(math.atan2(ux, uy)) % 360,
           "oriented": orient is not None,
           "spread": max(q[3] for q in xy) - min(q[3] for q in xy)}

    fit = _slope_fit(along, [q[3] for q in xy]) if span >= MIN_TERRAIN_SPAN_M else None
    if not fit:
        return out
    slope, se, rms = fit
    tan_a = -slope                       # positive: the ground falls away downrange
    tan_g = math.tan(math.radians(gamma))
    denom = tan_g - tan_a
    out.update(tan_a=tan_a, se=se, rms=rms, tan_g=tan_g,
               amplification=abs(tan_g / denom) if abs(denom) > 1e-9 else float("inf"),
               sensitivity=(1.0 / denom) if abs(denom) > 1e-9 else float("inf"))
    return out


def terrain_report(shots, verbose):
    """The one line the default report owes, and the detail behind --terrain."""
    t = terrain(shots)
    if t is None:
        if verbose:
            print("\n== terrain: no warhead traces in this night -- nothing to measure")
        return

    amp = t.get("amplification")
    if amp is None:
        print(f"\n== terrain at {t['lat']:.3f},{t['lon']:.3f}: "
              f"{t['n']} impacts inside {t['span']:.0f} m -- too tight to measure the ground")
        return

    bad = amp > GRAZE_AMPLIFICATION
    note = "** ILL-CONDITIONED -- the ground is shaping this **" if bad else "well conditioned"
    print(f"\n== terrain at {t['lat']:.3f},{t['lon']:.3f}: "
          f"downrange slope {t['tan_a'] * 100:+.2f}% against a {t['gamma']:.1f} deg arrival, "
          f"{amp:.1f}x flat ground -- {note}")

    if not verbose:
        return
    print(f"   {t['n']} impacts over {t['span']:.0f} m along bearing {t['bearing']:.0f} deg"
          f"{'' if t['oriented'] else '  (UNORIENTED -- sign of the slope is a guess)'}")
    print(f"   ground height spread     {t['spread']:.1f} m, fit residual {t['rms']:.1f} m rms")
    print(f"   slope                    {t['tan_a'] * 100:+.2f} % +/- {t['se'] * 100:.2f}"
          f"   (descent {t['tan_g'] * 100:.2f} %)")
    print(f"   impact per unit of error {t['sensitivity']:+.1f}"
          f"   (flat ground {1 / t['tan_g']:.1f})")
    print("   A round descending at tan(g) onto ground falling away at tan(a) lands at")
    print("   1/(tan g - tan a) per unit of trajectory error. The two converging is a target")
    print("   whose miss distribution is the hillside's, not the guidance's.")


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("directory")
    ap.add_argument("--shots", action="store_true", help="one line of diagnostics per shot")
    ap.add_argument("--gate", action="store_true", help="print arms to drop and exit")
    ap.add_argument("--main", metavar="FACTOR",
                    help="pool the arms into FACTOR on/off and report that main effect")
    ap.add_argument("--paired", action="store_true",
                    help="compare the variants flown inside each shot -- see Sim/ShotArms.cs")
    ap.add_argument("--terrain", action="store_true",
                    help="the relief under the impacts, and whether it is shaping the misses")
    args = ap.parse_args()

    root, shots = load(args.directory)
    arms = sorted({s["arm"] for s in shots})

    if args.main:
        main_effect(shots, args.main)
        return

    if args.paired:
        paired(root, shots)
        return

    if args.gate:
        print(" ".join(gate(root, shots, arms)))
        return

    if args.terrain:
        terrain_report(shots, verbose=True)
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

    # Same shape of confound as the pick-up above: it invalidates comparisons silently, and the
    # night runs to completion looking like an ordinary result either way.
    terrain_report(shots, verbose=False)

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
          f"{'thrown':>8}{'arr deg':>8}{'band deg':>9}{'down m':>9}{'cross m':>9}{'early s':>9}{'lag m':>8}"
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
              f"{med('final_down', True):>+9.0f}{med('final_cross', True):>+9.0f}"
              f"{med('early_s', True):>9.2f}"
              f"{med('lag_m', True):>8.0f}"
              f"{med('dt_ms', True):>7.1f}{med('coast_ms', True):>9.1f}")

    if args.shots:
        print("\n== every shot")
        for s in sorted(shots, key=lambda r: r["n"]):
            head = (f"   {s['n']} b{s['block']} {s['arm']:<12} {s['verdict']:<9}")
            if not usable(s):
                print(head + f"  {s['arrived']}/{s['released']} arrived -- not scored")
                continue
            owed = f"{s['owed']:.2f}" if s["owed"] is not None else "  -"
            print(head + f"  mean {s['mean']:.2f}  spread {s['spread']:.2f}  "
                  f"worst {s['worst']:.2f}  residual {s['residual'] if s['residual'] is not None else float('nan'):.3f}  "
                  f"lag {statistics.median(s['lag_m']) if s['lag_m'] else float('nan'):.0f} m  "
                  f"{s['why'] or '-':<9} {s['passes'] if s['passes'] is not None else '-':>2}p "
                  f"owed {owed:>5} m/s")

    print("\n== what ended the post-boost correction")
    print("   the aim loop finishing is not the same shot as the loop being cut off; which rule")
    print("   fired is upstream of every number above it")
    for arm in arms:
        mine = [s for s in shots if s["arm"] == arm and usable(s)]
        if not mine:
            continue
        by = {}
        for s in mine:
            by.setdefault(s["why"] or "unknown", []).append(s)
        print(f"   {arm:<14}", end="")
        for name in sorted(by, key=lambda k: -len(by[k])):
            got = sorted(x["mean"] for x in by[name])
            med = statistics.median(got)
            print(f" {name} n={len(got)} median {med:.2f} km ", end="")
        print()

    compare(root, shots, arms, "mean")
    compare(root, shots, arms, "spread")


if __name__ == "__main__":
    main()
