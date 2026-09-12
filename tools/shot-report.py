#!/usr/bin/env python3
"""Reads a night of ballistic shots and says what it settled.

    ./tools/shot-report.py ~/shots/2026-08-22            # the table, the arms, the verdicts
    ./tools/shot-report.py ~/shots/2026-08-22 --shots    # ...with every shot's diagnostics
    ./tools/shot-report.py ~/shots/2026-08-22 --gate     # names arms to drop, for shot-batch.sh
    ./tools/shot-report.py ~/shots/2026-08-22 --instrument  # is the trace recording? after shot 1

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
import random
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
# The craft is optional because logs written before the line carried a name still have to read.
# Without it this took the FIRST cutoff in the file and handed it to every rocket in the world --
# the same one-craft's-reading-worn-by-eight that why_it_ended exists to avoid, and the reason the
# residual could not be attributed to an arm on a paired night (3bo).
CUTOFF = re.compile(r"cutoff(?: on (?P<craft>.+?))?:\s*residual\s+(?P<residual>[-\d.]+)\s*m/s,"
                    r"\s*own prediction\s+(?P<own>[-\d.]+)\s*km off")
TRIM = re.compile(r"owed\s+([-\d.]+)\s*m/s at the split,\s*([-\d.]+)\s*m/s on release")
TRIM_GAVE_UP = re.compile(r"release summary on (?P<craft>.+?): .*?GAVE UP")
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
#
# The craft is optional and is the whole of the attribution: the aim points cannot recover it,
# because seats 5 and 6 land 100 m apart and nearest-point matching mislabels them (3ce). A log
# written before the name was added keeps behaving as it did -- every flight gets the whole shot.
FINAL_WALK = re.compile(
    r"warhead trace(?: on (?P<craft>.+?))?: round \d+ "
    r"landed at\s+[-\d.,]+\s*\|\s*(?P<aim>[-\d.]+)\s*m from the aim\s*\|\s*"
    r"walk from the release probe\s+[-\d.]+\s*m\s*"
    r"\((?P<down>[-+\d.]+)\s*down,\s*(?P<cross>[-+\d.]+)\s*cross\)")

# A warhead that DETONATED instead of landing. It still prints a walk, and the walk is measured
# where it stopped -- which is not where it was going, so it is not an arrival and must not be
# scored as one. What it must also not be is invisible: these matched nothing, were dropped from
# the endpoint without a word, and on 2026-09-09-walk3 all four were the same arm. An exclusion
# that lands on one side is the difference under test.
FINAL_BURST = re.compile(
    r"warhead trace(?: on (?P<craft>.+?))?: round \d+ burst at")

FLIGHT = re.compile(
    r"flight\s+([-\d.]+)s by the world clock,\s+([-\d.]+)s by its own, "
    r"probe said\s+([-\d.]+)s")
LAG = re.compile(r"lag\s+([-\d.]+)ms\s*=\s*([-\d.]+)\s*m at")
CLOCKS = re.compile(r"flight\s+([\d.]+)s by the world clock,\s*([\d.]+)s by its own")
SAMPLE = re.compile(r"dt=([\d.]+)ms step=([\d.]+)ms sim=([\d.]+)x")
# What the coast is doing that gravity does not account for -- item 17's walk, which needs about
# 0.03 m/s sustained. Reported as a MAX rather than a median: the divergence is a burst over ~170 s
# of a flight that is otherwise quiet, and a median over every probe reports the quiet.
#
# THREE REGIMES ARE EXCLUDED, and each one alone would swamp the column on every flight:
#   * each craft's FIRST reading -- it spans the off-rails/on-rails transition the engine makes
#     when thrust stops, and re-fitting the conic reads 0.019-0.040 m/s at coast entry;
#   * anything with DENSITY -- a reentering body decelerates 230-242 m/s per probe, which is drag
#     being measured correctly and is not a perturbation;
#   * anything but a trim that has never run -- the bus's own trim is a commanded push worth
#     0.5-4.0 m/s, and it leaks into the sample after it stops.
# What is left is the pre-split coast, where item 17's walk happens, and it settles to 0.0007 m/s
# over 752 samples of a healthy flight -- a floor some fortyfold below the signal.
# Named rather than positional because the rails and hold states sit in the MIDDLE of the line, so
# a new capture cannot simply be appended without renumbering everything after it.
OFFGRAV = re.compile(
    r"coast probe on (?P<who>[^:]+):.*?density\s+(?P<density>[\dE.+-]+),"
    r".*?off-gravity\s+(?P<push>[\d.]+)\s*m/s"
    r" \(r [-+]?[\d.]+, a [-+]?[\d.]+, c (?P<cross>[-+]?[\d.]+)\)"
    r".*?bubble (?P<bubble>-?\d+),"
    r".*?\b(?P<rails>on rails|off rails|rails unknown)\b(?: \(forced\))?"
    r"(?:, (?P<hold>quiet|holding \([^)]*\)))?"
    r".*?\btrim\s+(?P<trim>\w+)")
BAND = re.compile(
    r"DEBUG\s+(\S+)\s+control:.*?pointing band\s+([\d.]+)\s*deg")
BANNER = re.compile(r"KSArmory\s+(\S+)\s+built for KSA\s+(\S+),\s*running\s+(\S+)")

# Where each warhead stopped, and how high the ground was there. Together they make the target's
# own relief measurable out of a night flown for something else. The landing line carries the
# signed downrange walk as well, which is what orients the axis without needing a frame or the
# body's rotation rate.
IMPACT = re.compile(
    r"warhead trace(?: on (?P<craft>.+?))?: round \d+ "
    r"landed at\s*(?P<lat>-?[\d.]+),\s*(?P<lon>-?[\d.]+)\s*\|\s*"
    r"[-\d.]+\s*m from the aim\s*\|\s*walk from the release probe\s+[-\d.]+\s*m\s*"
    r"\((?P<down>[-+\d.]+)\s*down,\s*(?P<cross>[-+\d.]+)\s*cross\)")
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
    ("floor",    re.compile(r"^[\d.]+ m out, inside the ")),
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


# "release summary on <craft>: ..." -- everything the correction loop left behind, written once per
# flight at the first release. Split in two on purpose: the head names the craft so a world of eight
# can be told apart, and the tail is picked over field by field, so re-wording one number does not
# stop the other five being read.
RELEASE = re.compile(r"release summary on (.+?): (.*)$", re.M)
RELEASE_FIELDS = {
    "arc_deg":      re.compile(r"arriving at ([-\d.]+) deg"),
    "release_owed": re.compile(r"and ([\d.]+) m/s on release"),
    "response":     re.compile(r"aim response ([\d.]+)"),
    "raw_response": re.compile(r"\(raw ([\d.]+)\)"),
    "plant":        re.compile(r"off (\d+) plant reading"),
    "worse_for":    re.compile(r"worse for (\d+)"),
    "floor_deg":    re.compile(r"against a ([-\d.]+) deg floor"),
    "afford_deg":   re.compile(r"of the ([-\d.]+) deg the tanks could afford"),
}


def release_summary(log, craft=None):
    """What the loop left, per flight. Empty lists for a log written before the line existed."""
    got = {k: [] for k in RELEASE_FIELDS}
    named = any(RELEASE.finditer(log))
    want = _craft(craft) if craft and named else None

    for m in RELEASE.finditer(log):
        if want is not None and _craft(m.group(1)) != want:
            continue
        for key, pattern in RELEASE_FIELDS.items():
            hit = pattern.search(m.group(2))
            if hit:
                got[key].append(float(hit.group(1)))
    return got


SURFACE = re.compile(
    r"surface at the landing point: the round stopped on\s*([\d.]+)\s*m")


def _floats(pattern, text, groups=1):
    out = []
    for m in pattern.finditer(text):
        out.append(tuple(float(g) for g in m.groups()[:groups]))
    return out


def _traces(pattern, log, craft):
    """One flight's own trace lines, or the whole shot's where nothing names a craft.

    The name is the whole of the attribution and there is no fallback: the aim points are 12 km
    apart but seats 5 and 6 land 100 m from each other, so matching a landing to a rocket by
    position mislabels three survivors of four -- ACCURACY-PLAN.md 3ce, which is what naming the
    line was for. A log written before it keeps behaving exactly as it did, every flight carrying
    the whole shot; a log that carries the name is cut down to one rocket, and a flight whose
    trace did not finish gets nothing rather than its neighbour's.
    """
    found = list(pattern.finditer(log))
    named = [m for m in found if m.group("craft")]
    if not named or craft is None:
        return found, False
    want = _craft(craft)
    return [m for m in named if _craft(m.group("craft")) == want], True


AWAY_NAMED = re.compile(r"warhead trace on (?P<craft>.+?): round \d+ away")


def _release_probes(log, craft):
    """This flight's own release probe, paired to the release it follows.

    The probe line carries no craft name -- it reads `warhead trace: probe from the round's own
    state ->` -- so matched on its own it belongs to every flight in the shot, which is exactly the
    fault 3ce found in the landing lines and fixed by naming them. It is emitted one line after the
    named `round N away` for the same round, so the preceding release is the attribution.

    Returns nothing rather than guessing when no release has been seen yet, and only that flight's
    probes when the craft is known. A log whose releases are unnamed gets the whole shot's, which is
    the pre-3ce behaviour and is reported as unattributed by the caller.
    """
    want = _craft(craft) if craft else None
    mine, seen_named, owner = [], False, None

    for line in log.splitlines():
        named = AWAY_NAMED.search(line)
        if named:
            seen_named = True
            owner = _craft(named.group("craft"))
            continue

        hit = TRACEPROBE.search(line)
        if hit and (want is None or owner == want):
            mine.append(hit)

    return mine, seen_named


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
            "arc_deg": [], "release_owed": [], "response": [], "raw_response": [],
            "plant": [], "worse_for": [], "floor_deg": [], "afford_deg": [],
            "offline": [], "probe_km": [], "thrown": [], "arrival_deg": [],
            "arrival_ms": [], "trace_km": [], "walk_m": [], "walk_down": [], "walk_cross": [],
            "early_s": [], "final_down": [], "final_cross": [], "final_aim": [],
            "trace_named": False, "own_impacts": [], "bursts": 0,
            "release_km": [], "probe_named": False,
            "band_deg": [], "impacts": [],
            "why": None, "passes": None, "owed": None, "why_named": False, "gave_up": False,
            "lag_ms": [], "lag_m": [], "clock_gap": [], "dt_ms": [], "sim": [], "coast_ms": [],
            "off_grav": [], "off_cross": [], "shared_bubble": 0,
            "rails_probes": 0, "rails_off": 0, "quiet_probes": 0,
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
    # This craft's own cutoff where the line names one, and the shared first reading where it does
    # not -- old batches keep behaving exactly as they did.
    named = [m for m in CUTOFF.finditer(both) if m.group("craft")]
    want = _craft(craft) if craft else None
    m = next((m for m in named if _craft(m.group("craft")) == want), None) if want else None
    m = m or (None if named and want else CUTOFF.search(both))
    if m:
        shot["residual"], shot["own_km"] = float(m.group("residual")), float(m.group("own"))
    m = TRIM.search(both)
    if m:
        shot["trim_split"], shot["trim_release"] = float(m.group(1)), float(m.group(2))
    m = BANNER.search(log)
    if m:
        shot["version"] = m.group(1)
    shot["why"], shot["passes"], shot["owed"], shot["why_named"] = why_it_ended(log, craft)

    # The trim's own verdict, which is strictly more sensitive than the terminator. Over 2,662
    # flights the two agree 99.51%, and where they disagree it is always this way round: 13 flights
    # gave up and then hit an EARLIER terminator -- clock twelve times, budget once -- so `why`
    # reads "clock" and the flight is scored as sound while it landed 0.86-70.3 km out.
    gave, _ = _traces(TRIM_GAVE_UP, log, craft)
    shot["gave_up"] = bool(list(gave))
    shot.update(release_summary(log, craft))

    shot["offline"] = [v for _, v in _floats(OFFLINE, both, 2)]
    shot["probe_km"] = [v for v, _ in _floats(PROBE, log, 2)]

    # This flight's own release probe, not the shot's. The miss it reports is the PRE-RELEASE half
    # of the total -- what the shot is already wrong by before the warheads are let go -- and 3ci
    # measured cot(gamma) acting there rather than on the walk.
    own_probes, shot["probe_named"] = _release_probes(log, craft)
    shot["release_km"] = [float(h.group(2)) / 1000.0 for h in own_probes]
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

    # Counted so the coverage line can say so. A burst is a real outcome and a real cost, but it
    # is not an arrival, and a silent drop is what let four of them leave one arm short.
    bursts, _ = _traces(FINAL_BURST, log, craft)
    shot["bursts"] = len(list(bursts))

    landings, shot["trace_named"] = _traces(FINAL_WALK, log, craft)
    for m in landings:
        shot["final_down"].append(float(m.group("down")))
        shot["final_cross"].append(float(m.group("cross")))
        shot["final_aim"].append(float(m.group("aim")))

    # The surface line follows the landing it belongs to and carries no round number, so the two
    # pair by order. A landing with no surface line after it is dropped rather than guessed at.
    #
    # Every landing in the shot, not just this flight's: the relief under the target is a property
    # of the ground and the terrain report wants all eight aim points, which is what `--terrain`
    # reading one seat cost it (3cb).
    pending, whose = None, None
    want = _craft(craft) if craft else None
    for line in log.splitlines():
        m = IMPACT.search(line)
        if m:
            pending = (float(m.group("lat")), float(m.group("lon")), float(m.group("down")))
            whose = _craft(m.group("craft")) if m.group("craft") else None
            continue
        m = SURFACE.search(line)
        if m and pending is not None:
            shot["impacts"].append(pending + (float(m.group(1)),))

            # This flight's own, which is what makes the ground under ONE seat measurable -- and
            # what stops the pooled fit counting every landing once per rocket in the world. The
            # craft is the only thing that can say: seats 5 and 6 land 100 m apart (3ce).
            if whose is not None and (want is None or whose == want):
                shot["own_impacts"].append(pending + (float(m.group(1)),))
            pending, whose = None, None

    # Positive is early: the round beat the flight time its own predictor gave it.
    for _world, own, probe in _floats(FLIGHT, log, 3):
        shot["early_s"].append(probe - own)
    for ms, metres in _floats(LAG, log, 2):
        shot["lag_ms"].append(ms)
        shot["lag_m"].append(metres)
    for world, own in _floats(CLOCKS, log, 2):
        shot["clock_gap"].append(world - own)
    seen = set()
    for m in OFFGRAV.finditer(log):
        who, density, push = m.group("who"), float(m.group("density")), float(m.group("push"))
        cross, bubble, trim = float(m.group("cross")), int(m.group("bubble")), m.group("trim")

        # Every probe counts towards how the coast was SPENT, including the entry one and the ones
        # taken while the trim is working -- those are exactly the parts of the coast the quiet
        # window is bounded away from, so excluding them would measure the window against itself.
        #
        # THIS ONE CRAFT'S probes, unlike the columns below it. A paired night gives the two arms
        # different rockets in one world, so a whole-log count is identical for both by
        # construction -- which is what it read before this filter, 4% against 4%. Same trap
        # why_it_ended documents, one craft's reading worn by eight.
        if _craft(who) == _craft(craft or who):
            shot["rails_probes"] += 1
            if m.group("rails") == "off rails":
                shot["rails_off"] += 1
            if m.group("hold") == "quiet":
                shot["quiet_probes"] += 1

        if who not in seen:          # the coast-entry transition, not a push
            seen.add(who)
            continue
        if density > 0.0 or trim != "idle":
            continue
        shot["off_grav"].append(push)
        shot["off_cross"].append(abs(cross))

        # Sharing a bubble is what stops the engine propagating a conic and starts it integrating
        # -- PhysicsBubble needs NumVehicles < 2 for the rails path. Bubbles merge on proximity and
        # only ever leave on a parent or frame change, so the sharing does not end once it starts.
        if bubble > 1:
            shot["shared_bubble"] += 1
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


def _say_loop_left(shots, order):
    """What each arm's correction loop left behind, split by arm.

    The merged table in the main report is keyed on the batch's arm column, which in a paired night
    is one value for the whole world -- so it prints one row and hides the very split the night was
    flown for. Here the arm is `within`, which is per flight.
    """
    per = defaultdict(lambda: defaultdict(list))
    for r in shots:
        if not (r.get("within") and usable(r)):
            continue
        for key in ("arc_deg", "floor_deg", "afford_deg", "release_owed", "response"):
            per[r["within"]][key].extend(r[key])

    if not any(per[name]["arc_deg"] for name in order if name in per):
        return

    print("   what each arm's correction loop left (medians)")
    print(f"   {'arm':<14}{'arc deg':>9}{'floor':>8}{'afford':>8}{'owed m/s':>10}"
          f"{'response':>10}{'flights':>9}")
    for name in order:
        if name not in per:
            continue

        def med(key):
            got = per[name][key]
            return statistics.median(got) if got else float("nan")

        print(f"   {name:<14}{med('arc_deg'):>9.1f}{med('floor_deg'):>8.1f}"
              f"{med('afford_deg'):>8.1f}{med('release_owed'):>10.2f}"
              f"{med('response'):>10.2f}{len(per[name]['arc_deg']):>9d}")
    print()


def paired(root, shots, endpoint="miss", levels_from=None):
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

    # Dropped before anything is scored, and dropped on a property of the RUN rather than of the
    # result: a shot whose burn phase ran slower than SLOW_BURN_MS is not measuring the arm at all
    # -- the trim gives up at the split and every warhead in the world is lost. Both such shots on
    # 2026-09-09-walk2 lost all eight, both arms equally, and between them they took the walk from
    # 0.72x p=0.027 to 0.80x p=0.268. Knowable before the shot is scored, which is what makes it a
    # rule rather than a choice. shot-batch.sh re-flies these, so a night flown since has none.
    slow = set()
    for log_path in sorted(root.glob("shots/*.log")):
        if coast_divergence(log_path) > DIVERGED_COAST:
            slow.add(_shot_id(log_path.stem.split("-", 1)[0]))

    # Filtered here rather than only where the groups are built, because the SEAT LEVELS are the
    # estimator's denominator and are fitted from this same list. Levelling on a set that still
    # holds two shots' worth of 85-100 km landings puts the excluded flights back into the answer
    # through the divisor -- read as 0.73x p=0.052 against 0.73x p=0.027 with them out of both.
    shots = [r for r in shots if _shot_id(r["n"]) not in slow]

    groups = defaultdict(lambda: defaultdict(list))
    for r in shots:
        if r.get("within") and usable(r):
            groups[_shot_id(r["n"])][r["within"]].append(r)

    if not groups:
        sys.exit("no flight in this batch says which variant it flew")

    base = order[0]
    label, unit, score = ENDPOINTS[endpoint]

    print(f"== paired within {len(groups)} shot(s) in {root}")
    print(f"   scored on the {label} ({unit})")
    if slow:
        print(f"   {len(slow)} shot(s) excluded: a bus was integrated in a rotating frame before "
              "it released,")
        print("      so it picked up ~0.42 m/s2 the guidance never asked for and the trim could "
              "not pay")
        print("      it back. Read off the RUN rather than off the result -- ACCURACY-PLAN.md "
              "3ci, 3cn.")

    # A flight with no score is not a tie and not a zero -- it is an observation the instrument
    # did not take, and saying how many were missed is what 3cd needed and did not have: it named
    # the walk its primary endpoint on a night whose trace covered half the roster, and could not
    # read its own headline. Said before the comparison rather than after it.
    if endpoint != "miss":
        flown = [r for r in shots if r.get("within") and usable(r)]
        got = [r for r in flown if score(r) is not None]
        print(f"   coverage: {len(got)} of {len(flown)} usable flights carry one "
              f"({len(got) / len(flown):.0%})" if flown else "   coverage: nothing usable flew")

        # Said per arm, because that is the only form in which it is readable. A burst is not an
        # arrival and is not scored; four of them all on one arm is not a coverage note, it is a
        # confound, and it has to be visible beside the ratio rather than inferred from a seat's n.
        burst_by_arm = defaultdict(int)
        for r in flown:
            burst_by_arm[r["within"]] += r.get("bursts", 0)
        if any(burst_by_arm.values()):
            spread = ", ".join(f"{a}={burst_by_arm[a]}" for a in sorted(burst_by_arm))
            print(f"   burst rather than landed, so not scored: {spread}")
            if len([a for a in burst_by_arm if burst_by_arm[a]]) == 1:
                print("      !! ALL ON ONE ARM -- that is an exclusion falling on the difference")
                print("         under test, not a coverage note. Read the ratio with it in mind.")
        if not got:
            sys.exit(f"   nothing in this night carries an attributable {label}, so there is "
                     f"no comparison to make on it.\n"
                     "   A night flown before the warhead trace named its craft cannot be "
                     "rescored: the\n"
                     "   landings cannot be matched to rockets after the fact, because seats 5 "
                     "and 6 land\n"
                     "   100 m apart. ACCURACY-PLAN.md 3ce. Score it on --endpoint miss, or "
                     "re-fly it.")
        if len(got) < 0.75 * len(flown):
            print("   !! under three quarters of the roster is traced -- read this as a "
                  "diagnostic,")
            print("      not as a comparison. ACCURACY-PLAN.md 3cd is what that costs.")

    # One block never flips, and that is the whole difference. ShotArms alternates the variants
    # down the roster and swaps them each shot, so seat and arm decouple over a night and are
    # PERFECTLY confounded within a single one. The seat term is the larger of the two -- measured
    # 9 m to 92 m across the roster on 2026-09-07-1824 -- so the arm medians below are a statement
    # about which seats a variant drew. Two such runs were read as an arm result before this said
    # so: docs/ACCURACY-PLAN.md 3by.
    if len(groups) < 2:
        print()
        print("   !! ONE BLOCK: the arms did not flip, so each variant flew a FIXED set of seats.")
        print("      Seat is worth 9-92 m across the roster and swamps anything under test, so")
        print("      the per-arm medians below rank SEATS, not arms. Read this run as a")
        print("      diagnostic; fly several blocks to compare arms. ACCURACY-PLAN.md 3by.")
    print(f"   spec: {spec}")
    print(f"   baseline: {base}")
    _say_regime(shots)

    # Pooled, for scale only. It is NOT the comparison: pooling across shots puts the
    # between-shot swing back into the number the ratio was constructed to remove.
    pooled = defaultdict(list)
    for per_arm in groups.values():
        for name, records in per_arm.items():
            pooled[name].extend(v for v in (score(r) for r in records) if v is not None)

    print(f"   arm            flights   median {unit:<3}  (pooled, for scale only)")
    for name in order:
        if pooled.get(name):
            print(f"   {name:<14} {len(pooled[name]):>7}   {statistics.median(pooled[name]):>9.2f}")
    print()

    _say_loop_left(shots, order)

    levels, lopsided = _seat_levels(shots, score)
    borrowed = ""

    # Out of sample when asked for. The divisor is a property of the world -- seat 3 reads 76-108 m
    # against seat 1's 6-12 across seven consecutive nights and many builds -- so it can be fitted
    # from a night that is not the one under test, and then it cannot absorb any of the arm. Fitted
    # in-sample the two nights of 3ci read 0.72x and 1.12x; levelled with each other's numbers they
    # read 0.87x and 0.86x, which is the same measurement twice. That difference is larger than the
    # effect being chased, so which set is used has to be stated rather than assumed.
    if levels_from:
        other_root, other_shots = load(levels_from)
        outside, _ = _seat_levels(other_shots, score)
        missing = sorted(set(levels) - set(outside))
        if not outside:
            sys.exit(f"--levels-from {other_root}: no seat levels could be fitted there")
        if missing:
            print(f"   !! {other_root.name} has no level for seat(s) "
                  + ", ".join(f"s{m + 1}" for m in missing) + " -- those seats are dropped")
        levels = {seat: v for seat, v in outside.items() if seat in levels}
        borrowed = f", from {other_root.name}"

    if levels:
        scale = 1000.0 if unit == "km" else 1.0
        print(f"   seat levels divided out (arm-neutral{borrowed or ', from this night'}): "
              + ", ".join(f"s{s + 1}={levels[s] * scale:.0f}m" for s in sorted(levels)))
        if lopsided:
            print(f"   seats excluded for flying only one arm: "
                  + ", ".join(f"s{s + 1}" for s in lopsided))
        print()

    for name in order[1:]:
        ratios, raws, wins, losses = [], [], 0, 0

        for shot, per_arm in sorted(groups.items()):
            # Both variants have to have flown in the SHOT for it to be a pair. A shot where one
            # of them lost every rocket is not a tie, it is not an observation.
            if base not in per_arm or name not in per_arm:
                continue

            got_a = [v for v in (score(r) for r in per_arm[base]) if v is not None]
            got_b = [v for v in (score(r) for r in per_arm[name]) if v is not None]
            if not got_a or not got_b:
                continue

            raw_a = statistics.median(got_a)
            raw_b = statistics.median(got_b)
            if raw_a > 0 and raw_b > 0:
                raws.append(math.log(raw_b / raw_a))

            # The comparison is made on seat-levelled flights, so the two arms are not being
            # scored against different ground. Where no seat could be levelled this falls back to
            # the raw values, which is the pre-levelling instrument and is reported as such.
            levelled_a = _levelled(per_arm[base], levels, score)
            levelled_b = _levelled(per_arm[name], levels, score)
            if not levelled_a or not levelled_b:
                continue

            a = statistics.median(levelled_a)
            b = statistics.median(levelled_b)
            if a <= 0 or b <= 0:
                continue

            ratios.append(math.log(b / a))
            if b < a:
                wins += 1
            elif b > a:
                losses += 1

        if not ratios:
            print(f"   {name}: no shot flew both it and {base}")
            print(f"   {' ' * len(name)}  so nothing above is an arm comparison -- see the note.")
            continue

        point, lo, hi = _median_interval(ratios)
        p = _sign_p(wins, losses)
        w = wilcoxon_p(ratios)
        flip = _shot_flip_p(shots, groups, base, name, score, point,
                            levels if levels_from else None)

        # The rank test is the one to read, so read it. The sign test is kept beside it because it
        # assumes less and because every number in docs/MIRV-NEXT.md before 8af was scored on it --
        # but taking min(p, w) was two chances at the same threshold, and against 0.05 where the
        # interval beside it is built at ALPHA. An arm at sign 0.04 and rank 0.20 read as RESOLVED.
        # The verdict is read off the RANDOMISATION, not off the signed rank. The rank test shares
        # a nuisance parameter with its own null -- see _shot_flip_p -- and reads two to four times
        # too small here. It is still printed, because a large gap between the two is the tell that
        # the levelling is doing more work than the arm.
        best = flip if not math.isnan(flip) else w

        print(f"   {name} vs {base}: {math.exp(point):.2f}x"
              f"   [{math.exp(lo):.2f}, {math.exp(hi):.2f}] at {int((1 - ALPHA) * 100)}%")
        print(f"      won {wins} of {len(ratios)} paired shots, "
              f"sign p={p:.3f}, signed-rank p={w:.3f}, "
              + (f"shot-flip p={flip:.3f}" if not math.isnan(flip) else "shot-flip n/a")
              + ("   RESOLVED" if best <= ALPHA else "   unresolved"))
        print("      per shot: "
              + ", ".join(f"{math.exp(r):.2f}" for r in ratios))

        _say_graded(groups, base, name, levels, score, shots, unit)

        # The un-levelled reading, for continuity with every night flown before levelling and so
        # that a large gap between the two is visible rather than silently absorbed. The levelled
        # line above is the one to read: this one has the roster's ground in it.
        if raws and levels:
            rp, rlo, rhi = _median_interval(raws)
            print(f"      un-levelled: {math.exp(rp):.2f}x"
                  f"   [{math.exp(rlo):.2f}, {math.exp(rhi):.2f}],"
                  f" signed-rank p={wilcoxon_p(raws):.3f}")
        print()

    _say_coast(shots, order)
    _say_modes(shots, order)
    _say_seats(shots)
    _say_terminators(shots, order)

    if len(groups) < 6:
        print(f"   NOTE: {len(groups)} shots cannot reach p<=0.05 on a sign test. Six is the floor,")
        print("   and that is only if the variant wins every one of them.")


def _say_coast(shots, order):
    """How each arm's coast was actually spent — off rails, and under a quiet hold.

    **The fraction is the discriminator, not the presence.** 65 of 65 divergent flights and 124 of
    126 healthy ones went off rails at some point, so a binary reading separates nothing; what
    separates them is how much of the coast, 70% against 1%.

    And it is what makes a null readable. The quiet window is bounded at both ends
    (`Sim/CoastQuiet.cs`), so how much of a coast it covers is a per-flight outcome — an arm that
    changed nothing because it never engaged looks exactly like one that engaged and did not
    matter, and only this table tells them apart.
    """
    rows = [s for s in shots if s.get("within") and usable(s) and s.get("rails_probes")]
    if not rows:
        return

    print("   how the coast was spent (median over flights, per cent of coast probes)")
    print(f"   {'arm':<14}{'off rails':>11}{'quiet':>8}{'probes':>9}{'flights':>9}")
    for name in order:
        mine = [s for s in rows if s["within"] == name]
        if not mine:
            continue
        off = statistics.median(s["rails_off"] / s["rails_probes"] for s in mine)
        quiet = statistics.median(s["quiet_probes"] / s["rails_probes"] for s in mine)
        probes = statistics.median(s["rails_probes"] for s in mine)
        print(f"   {name:<14}{off:>10.0%}{quiet:>8.0%}{probes:>9.0f}{len(mine):>9}")
    print()


def _seat_levels(shots, score):
    """What each seat is worth before any arm is compared, so it can be divided out.

    A seat is a fixed point on the ground. `AimSpread.AimFor` anchors seat 0 on the operator's aim
    and displaces every other by a fixed multiple of the lethal radius along a fixed bearing, so
    seat 3 lands on the same hillside on every night flown at the same target. Measured over the
    eight nights at 26.485S,68.148W: seat 3 reads 76-108 m against seat 1's 6-12 m across seven
    consecutive nights and many different builds, which makes it a property of the world rather
    than of anything under test.

    That is not merely scatter. Arms alternate down the roster and flip each shot, so within one
    shot the two arms sit on DIFFERENT ground: the ratio a null night produces alternates about
    2.3 and 0.44 rather than sitting at 1.0. It is a deterministic bias the median-interval
    estimator reads as spread, which is why the interval does not shrink with n -- 6 blocks report
    [0.28, 3.47] and 20 blocks [0.37, 2.73] on identical code.

    The level is the GEOMETRIC MEAN OF THE PER-ARM MEDIANS, not the median over the seat's
    flights. That is what makes it arm-neutral: an arm worse by k everywhere raises every seat's
    level by sqrt(k), which divides out of the ratio exactly. Pooling instead would let whichever
    arm happened to fly a seat more often set that seat's level, putting the effect under test
    into the thing it is measured against.

    A seat that flew only one arm has no such level and is excluded rather than normalised
    against itself.
    """
    per = defaultdict(lambda: defaultdict(list))
    for r in shots:
        if r.get("seat") is None or not r.get("within") or not usable(r):
            continue
        v = score(r)
        if v is not None and v > 0:
            per[r["seat"]][r["within"]].append(v)

    levels, lopsided = {}, []
    for seat, by_arm in per.items():
        if len(by_arm) < 2:
            lopsided.append(seat)
            continue
        levels[seat] = math.exp(statistics.fmean(
            math.log(statistics.median(v)) for v in by_arm.values()))

    return levels, sorted(lopsided)


def _paired_ratios(groups, base, name, levels, score):
    """The per-shot log ratios the verdict is computed from, for a given set of seat levels."""
    ratios = []
    for _, per_arm in sorted(groups.items()):
        if base not in per_arm or name not in per_arm:
            continue
        levelled_a = _levelled(per_arm[base], levels, score)
        levelled_b = _levelled(per_arm[name], levels, score)
        if not levelled_a or not levelled_b:
            continue
        a = statistics.median(levelled_a)
        b = statistics.median(levelled_b)
        if a > 0 and b > 0:
            ratios.append(math.log(b / a))
    return ratios


# Enough that the smallest p it can report is well under the 0.0294 bar, and cheap: a draw is a
# relabel and two medians per shot, not a re-parse.
FLIP_DRAWS = 2000


def _shot_flip_p(shots, groups, base, name, score, observed, fixed_levels=None):
    """p from the one thing the design actually randomises: which parity of the roster is the arm.

    The signed-rank asks whether the per-shot log ratios are centred on zero, which assumes they
    are exchangeable about it. They are not. `_seat_levels` fits its divisor from the SAME flights
    the ratios are built from, so the statistic and its null share a nuisance parameter, and the
    level is the geometric mean of the per-arm medians -- relabelling the arms moves it. Refitting
    the levels inside the permutation is what accounts for that; nothing else does.

    **A divisor borrowed with `--levels-from` is held fixed rather than refitted.** Fitted on
    another night, it cannot move with this one's labels, so there is nothing to account for --
    and refitting would test a ratio built on the borrowed levels against a null built on this
    night's own, which are tighter, and call a ratio significant whose interval spans one.

    Calibrated on identical code -- one arm's roster split into two pseudo-arms, so the truth is
    1.000 -- at the design's own fourteen blocks: the signed rank reads **6.1%** false RESOLVED
    against a nominal 2.9% and this reads **3.1%**. At eight blocks it inverts (3.5% and 1.7%), so
    the correction belongs to the n that is flown rather than to the test.

    The same calibration says the estimator returns 0.45x to 2.37x on identical code at fourteen
    blocks. Read a verdict from it accordingly.
    """
    flippable = [g for g, per_arm in groups.items() if base in per_arm and name in per_arm]
    if not flippable or not observed:
        return float("nan")

    labelled = [r for r in shots if r.get("within") in (base, name) and r.get("seat") is not None]
    by_shot = defaultdict(list)
    for r in labelled:
        by_shot[_shot_id(r["n"])].append(r)

    rng = random.Random(20260909)
    hits = 0

    for _ in range(FLIP_DRAWS):
        flipped = [g for g in flippable if rng.random() < 0.5]

        for g in flipped:
            for r in by_shot.get(g, ()):
                r["within"] = name if r["within"] == base else base

        try:
            levels = fixed_levels if fixed_levels is not None else _seat_levels(shots, score)[0]
            swapped = defaultdict(lambda: defaultdict(list))
            for g, per_arm in groups.items():
                for arm, rows in per_arm.items():
                    other = (name if arm == base else base) if g in set(flipped) else arm
                    swapped[g][other] = rows
            ratios = _paired_ratios(swapped, base, name, levels, score)
            if ratios and abs(_median_interval(ratios)[0]) >= abs(observed) - 1e-12:
                hits += 1
        finally:
            for g in flipped:
                for r in by_shot.get(g, ()):
                    r["within"] = name if r["within"] == base else base

    return (hits + 1) / (FLIP_DRAWS + 1)


def _seat_relief(shots):
    """How rough the ground under each seat's own landings is, as an rms about its own mean.

    A seat is a fixed point on the ground and its landings fall within a few hundred metres of
    each other, so the spread of the surface heights they stopped on IS the relief the walk runs
    over. It needs the trace to name its craft: seats 5 and 6 land 100 m apart, which is why this
    could not be computed before 3ce and why 3cd's strong test had to be assembled by hand.
    """
    per = defaultdict(list)
    for r in shots:
        if r.get("seat") is None:
            continue
        per[r["seat"]].extend(q[3] for q in r["own_impacts"])

    return {seat: statistics.pstdev(h) for seat, h in per.items() if len(h) >= 3}


def _say_graded(groups, base, name, levels, score, shots, unit):
    """Per seat: what the arm did to it, against how rough the ground under it is.

    **This is 3cc's strong test**, and it is the one that separates a terrain mechanism from a
    number. If a steeper arrival works by shortening the ground the round samples on the way in,
    the seats with the most relief have the most to give and the flat ones have almost none -- so
    the ratio should FALL as the relief rises, a negative rank correlation. A uniform gain across
    seats is some other mechanism wearing the same ratio.

    3cd ran this on the miss and read rho=+0.12, p=0.79. The miss is 70% walk, so that test was
    diluted twice over -- once by the endpoint and once by seat 3 carrying most of the signal.
    """
    relief = _seat_relief(shots)
    if not relief:
        return

    rows = []
    for seat in sorted(relief):
        a, b = [], []
        for per_arm in groups.values():
            for rec in per_arm.get(base, []):
                if rec.get("seat") == seat and score(rec) is not None:
                    a.append(score(rec))
            for rec in per_arm.get(name, []):
                if rec.get("seat") == seat and score(rec) is not None:
                    b.append(score(rec))
        if len(a) < 2 or len(b) < 2:
            continue
        ma, mb = statistics.median(a), statistics.median(b)
        if ma <= 0:
            continue
        rows.append((seat, len(a), len(b), ma, mb, mb / ma, relief[seat]))

    if len(rows) < 4:
        return

    print(f"   is the gain graded by the ground under the seat?  ({name} vs {base})")
    print(f"   {'seat':>6}{'n ' + base[:6]:>10}{'n ' + name[:6]:>10}"
          f"{base[:6] + ' ' + unit:>12}{name[:6] + ' ' + unit:>12}{'ratio':>8}{'relief m':>10}")
    for seat, na, nb, ma, mb, ratio, rms in rows:
        print(f"   {seat + 1:>6}{na:>10}{nb:>10}{ma:>12.1f}{mb:>12.1f}{ratio:>8.2f}{rms:>10.1f}")

    rho, p = _spearman([r[6] for r in rows], [r[5] for r in rows])
    if math.isnan(rho):
        print()
        return

    verdict = ("the roughest seats gained most -- the terrain mechanism"
               if p <= 0.05 and rho < 0 else "no grading at this n")
    print(f"   rank correlation relief vs ratio: rho={rho:+.2f}, p={p:.3f}   {verdict}")
    print()


def _levelled(records, levels, score):
    """One arm's flights in one shot, each divided by its own seat's level."""
    out = []
    for r in records:
        v = score(r)
        if v is None:
            continue
        seat = r.get("seat")
        if seat is None:
            # A one-rocket run has no roster and needs no levelling: there is only one seat, so
            # every shot's pair sits on the same ground already.
            out.append(v)
        elif seat in levels:
            out.append(v / levels[seat])
    return out


# Which ending marks a flight as having lost its correction outright, splitting the outcome into
# two populations that no single median describes. Mechanistic on purpose: `trim` is the bus
# giving up before its first pulse, and it separates the modes almost perfectly -- 60 of 61 such
# flights past 60 km against 0 of 185 that converged. A cut on the miss itself would be a
# threshold fitted to the sample it is then read from, which SHOT-PROTOCOL.md warns against.
LOST_ENDINGS = ("trim",)


def _fisher_p(a, b, c, d):
    """Two-sided Fisher exact on a 2x2 table, by summing every table no likelier than this one."""
    n = a + b + c + d
    if n == 0 or (a + c) == 0 or (b + d) == 0 or (a + b) == 0 or (c + d) == 0:
        return 1.0

    def prob(x):
        return (math.comb(a + b, x) * math.comb(c + d, a + c - x)) / math.comb(n, a + c)

    obs = prob(a)
    lo = max(0, (a + c) - (c + d))
    hi = min(a + b, a + c)
    return min(1.0, sum(p for x in range(lo, hi + 1)
                        if (p := prob(x)) <= obs * (1.0 + 1e-9)))


def _say_modes(shots, order):
    """Which mode each arm's flights landed in, tested as a count rather than a median.

    The outcome is bimodal -- a healthy population around 20 m and a lost one around 88 km, with a
    real gap between 4 and 60 km -- and a median endpoint is blind to a change that moves flights
    between them while leaving each mode where it was. SHOT-PROTOCOL.md records a night read
    UNRESOLVED at p=0.464 on a change that moved 12 of 25 to 1 of 25 at p=3.8e-4.

    So the count is reported beside the ratio, always, and the two modes are summarised apart. An
    arm that fixes the lost mode and wrecks the healthy one -- which is what QuietCoast did, 0.15x
    on divergent worlds against 89x on the rest -- reads as a regression on the pooled median and
    as exactly what it is here.
    """
    rows = [s for s in shots if s.get("within") and s.get("why") and usable(s)
            and s.get("why_named")]
    if not rows:
        return

    lost = {name: [s for s in rows if s["within"] == name
                   and (s["why"] in LOST_ENDINGS or s.get("gave_up"))]
            for name in order}
    flown = {name: [s for s in rows if s["within"] == name] for name in order}
    if not any(lost.values()):
        return

    print("   which mode the flights landed in")
    print(f"   {'arm':<14}{'flights':>9}{'lost':>7}{'rate':>8}"
          f"{'healthy med':>14}{'lost med':>12}")
    for name in order:
        if not flown[name]:
            continue
        n, bad = len(flown[name]), len(lost[name])
        ok = [s["mean"] for s in flown[name]
              if s["why"] not in LOST_ENDINGS and not s.get("gave_up")]
        far = [s["mean"] for s in lost[name]]
        print(f"   {name:<14}{n:>9}{bad:>7}{bad / n:>7.0%}"
              f"{(statistics.median(ok) if ok else float('nan')):>14.3f}"
              f"{(statistics.median(far) if far else float('nan')):>12.2f}")

    base = order[0]
    for name in order[1:]:
        if not flown[name] or not flown[base]:
            continue
        a, b = len(lost[name]), len(flown[name]) - len(lost[name])
        c, d = len(lost[base]), len(flown[base]) - len(lost[base])
        p = _fisher_p(a, b, c, d)
        print(f"   {name} vs {base}: {a}/{a + b} lost against {c}/{c + d},"
              f" Fisher p={p:.4f}"
              + ("   RESOLVED" if p <= ALPHA else "   unresolved"))
    print()


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


# The trace prints whole metres, so a zero means "under half a metre" rather than "exactly none".
# A ratio needs a positive number and this is the smallest the instrument can distinguish.
WALK_FLOOR_M = 0.5


RELEASE_FLOOR_M = 0.5


def _release_score(shot):
    """What this flight is already wrong by BEFORE the warheads are let go, in metres.

    `miss = (release probe - target) + (walk from the probe)`. The walk is measured FROM the probe,
    so it cannot see this half at all -- and on both nights of 3ci this is where the arrival angle
    acts: the base arm carries a systematic 8 m short bias here where the steep arm carries none,
    and the ratio measures 0.62-0.70 against cot(gamma)'s predicted 0.701. The walk measures 1.12
    and 0.72 on the same flights.

    Unattributed probes are refused for the reason 3ce refuses unattributed landings: one flight's
    number standing in for all eight is a dead heat wearing the shape of a measurement.
    """
    if not shot.get("release_km"):
        return None
    if shot.get("seat") is not None and not shot.get("probe_named"):
        return None
    return max(statistics.median(abs(v) for v in shot["release_km"]) * 1000.0, RELEASE_FLOOR_M)


def _walk_score(shot):
    """This flight's own downrange walk after release, in metres, or None if it was not traced.

    **Signed downrange, read as a magnitude.** The walk's sign is a property of the seat -- seat 3
    reads -54 to -87 m on every flight and seat 4 +14 to +21 -- so what an arm can do to it is
    shrink it toward zero, which is a comparison of magnitudes. Pooling the signed values instead
    would cancel two seats against each other and measure nothing.

    **An unattributed trace is not this flight's**, and a night flown before the trace named its
    craft has none that are. Read as a score they hand every rocket in the world the same number,
    which is a ratio of exactly 1.00 with an interval of [1.00, 1.00] -- a dead heat that is
    really the absence of a measurement. So a multi-rocket flight with no name on its trace scores
    nothing, and the coverage line says how many of those there were.
    """
    if not shot.get("final_down"):
        return None
    if shot.get("seat") is not None and not shot.get("trace_named"):
        return None
    return max(statistics.median(abs(v) for v in shot["final_down"]), WALK_FLOOR_M)


# What a paired comparison is scored on. The miss is the shipped endpoint and every night before
# 2026-09-09 was read on it; the walk is 70% of it, is the only part the arrival angle acts on,
# and scoring the total diluted a 0.71x effect to 0.80x on an interval that could not resolve it
# (3cf). Both are per FLIGHT and both are levelled per seat.
ENDPOINTS = {
    "miss": ("miss at the ground", "km", lambda s: s["mean"]),
    "walk": ("downrange walk after release", "m", _walk_score),
    "release": ("miss the shot already has at release", "m", _release_score),
}


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


# The trace reports a release and, one poll after the round stops, a landing. Counting the two
# against each other is the only check that sees an instrument fault the miss endpoint cannot:
# every flight lands and is scored, so a night reads healthy while its declared endpoint is empty.
TRACE_AWAY = re.compile(r"warhead trace on .+?: round \d+ away")
TRACE_LANDED = re.compile(r"warhead trace on .+?: round \d+ (?:landed|burst)")

# A whole arm goes missing as HALF the roster, so anything at or below this is the fault rather
# than an unlucky round. Set below 1.0 because a genuinely reaped or shot-down warhead is a real
# outcome and must not stop a night on its own.
TRACE_COVERAGE_FLOOR = 0.75


# The burn phase alone -- every sample before the first warhead is down. A whole-shot median mixes
# in the descent, where a standing mushroom cloud costs 6-8 ms a frame, so it reports the decoration
# rather than the regime the guidance actually ran in.
FIRST_LANDING = re.compile(r"warhead trace on .+?: round \d+ (?:landed|burst)")

# Reported, NOT used to exclude. It reads 28.0 and 29.7 ms on the two shots of 2026-09-09-walk2
# that lost every warhead against 18.2-24.1 for the twelve that did not, which looks causal and is
# not: WarheadTrace only starts sampling at the first RELEASE -- measured at 37 ms after the first
# release summary -- so this window opens minutes AFTER the trim has already given up. The ascent
# frame time, which could have been causal, does not separate the shots at all (21.6-21.7 ms on the
# two failures against 19.5-22.9 healthy). What is slow is the descent, and it is slow BECAUSE the
# coast diverged: the world is carrying shed debris and every vehicle is integrated rather than
# propagated. Symptom, downstream of the cause below.
SLOW_BURN_MS = 26.0

# One probe is enough. The quantity is now a COUNT of rotating-frame probes on an unsplit bus
# rather than a sum of accelerations, so there is no scale to calibrate and no dilution to allow
# for: a bus either was integrated in the wrong frame before it released or it was not. Measured
# across every night flown, sound shots read exactly 0 and the three ruined ones read 16 and up.
DIVERGED_COAST = 0


def burn_frame_ms(log_path):
    """Median frame step before the first warhead lands, or None if the shot took no samples."""
    before = []
    landed = False
    for line in pathlib.Path(log_path).read_text(errors="replace").splitlines():
        if not landed and FIRST_LANDING.search(line):
            landed = True
        if landed:
            continue
        m = SAMPLE.search(line)
        if m:
            before.append(float(m.group(1)))
    return statistics.median(before) if before else None


OFF_GRAVITY = re.compile(r"off-gravity ([\d.]+)")
RELEASE_LINE = re.compile(r"release summary")
STAMP = re.compile(r"^(\d{2}:\d{2}:\d{2})")


COAST_PROBE = re.compile(r"coast probe on (?P<craft>.+?):")
SPLIT_ON = re.compile(r"split on (?P<craft>[^:]+):")
ROTATING = re.compile(r"Ccf origin")


def coast_divergence(log_path):
    """Whether any bus was integrated in a rotating frame before it let its warheads go.

    Counts coast probes reading `Ccf origin` on a craft that has not yet split. That is the fault
    itself rather than a proxy for it: a bubble spanning the near-surface radius takes its frame
    from its heaviest member, and if that is something on the ground the frame is `Ccf` -- at which
    point a bus a thousand kilometres up is advanced as though the rotating frame were inertial,
    because the fictitious forces sit behind a per-vehicle `InPhysicsRadius` test it fails. It picks
    up about 0.42 m/s^2 it should not have, and four craft of 2026-09-10-trimgate shot 020 measured
    0.428-0.447 against that prediction. ACCURACY-PLAN.md 3bv, 3ci, 3cn.

    Separation is 0 against 16 on that night, every clean shot to nothing.

    **Per craft, and bounded by that craft's OWN split.** The first form summed |off-gravity| and
    stopped at the first `release summary` in the file, which on a paired night is the EARLY arm's --
    the arms release two to five minutes apart, so the window closed before the late arm had even
    split, and the gate was structurally blind to half of every paired night. On shot 020 it shut at
    15:15:38, where the divergence began at 15:17:29 and the split it ruined was at 15:18:04.
    """
    split = set()
    hits = 0

    for line in pathlib.Path(log_path).read_text(errors="replace").splitlines():
        done = SPLIT_ON.search(line)
        if done:
            split.add(done.group("craft").strip())
            continue

        probe = COAST_PROBE.search(line)
        if probe and ROTATING.search(line):
            craft = probe.group("craft").strip()
            # A bus carries the stack's name with a suffix -- `GeoSat FAT 7` splits into
            # `GeoSat FAT 7_1` -- so the child has to be recognised as already split. The clean
            # shots are full of rotating-frame probes on those: the big bubble forms around the
            # already-released, re-entering first group 55-84 s AFTER the last split, which is
            # ordinary and is not what ruins a shot.
            if craft not in split and craft.rsplit("_", 1)[0] not in split:
                hits += 1

    return hits


def frame_check(root, only=None):
    """Name the shots whose coast diverged badly enough that they measure nothing.

    Prints the divergence and the burn-phase frame time per shot and exits non-zero on the first,
    not the second -- see the constants above for why the frame time is a symptom. `only` restricts
    it to one shot, which is what shot-batch.sh asks after each flight.
    """
    root = pathlib.Path(root)
    logs = sorted(root.glob("shots/*.log"))
    if only:
        # Either form: the bare shot number the plan uses, or the "<n>-<arm>" stem shot-batch.sh
        # names its files with. Matching only the second with a "-" appended silently matches
        # NOTHING and passes every shot, which is a gate that reports success without looking.
        logs = [p for p in logs if p.stem == only or p.stem.startswith(f"{only}-")]

    bad = []
    for log_path in logs:
        ms = burn_frame_ms(log_path)
        drift = coast_divergence(log_path)
        diverged = drift > DIVERGED_COAST
        shown = f"{ms:.1f} ms" if ms is not None else "no samples"
        flag = "  DIVERGED" if diverged else ""
        print(f"run: {log_path.stem} rotating-frame probes {drift:.0f}, "
              f"burn-phase {shown}{flag}")
        if diverged:
            bad.append(log_path.stem)

    if bad:
        print(f"run: {len(bad)} shot(s) over the {DIVERGED_COAST:.0f} coast floor: "
              f"{', '.join(bad)}")
        print("run: the coast left the inertial frame, so the trim owed a debt it could not pay")
        print("run: and every warhead left on a trajectory already tens of km wrong.")
    return 1 if bad else 0


def instrument(root, traced):
    """Is the trace recording what the night was declared on? Run after the FIRST shot.

    The walk night of 2026-09-08 traced 8 away and 4 landed in every one of its fourteen shots,
    and the missing four were one whole arm -- the one that releases later and lands last, cut off
    because the scenario ended on the last impact. Three and a half hours bought no endpoint.
    Visible on shot one, and nothing was looking. ACCURACY-PLAN.md 3cg.
    """
    root = pathlib.Path(root)
    logs = sorted(root.glob("shots/*.log"))
    if not logs:
        return 0

    away = landed = 0
    for log_path in logs:
        text = log_path.read_text(errors="replace")
        away += len(TRACE_AWAY.findall(text))
        landed += len(TRACE_LANDED.findall(text))

    if not traced:
        return 0

    if away == 0:
        print(f"instrument: this night asked for traces and {len(logs)} shot(s) released none")
        return 1

    share = landed / away
    print(f"instrument: warhead trace {landed} of {away} releases reported "
          f"({share:.0%}) over {len(logs)} shot(s)")

    if share < TRACE_COVERAGE_FLOOR:
        print(f"instrument: below the {TRACE_COVERAGE_FLOOR:.0%} floor -- the endpoint will be "
              "empty or confounded.")
        print("instrument: the arm that lands LAST is the one that goes missing, so on a paired "
              "night this")
        print("instrument: removes one arm entirely and the report cannot compare. Stop the "
              "night and fix it.")
        return 1

    return 0


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


# "ground under the aim on <craft>: N samples over 5.0 km of the approach, swing X m, below a 1 km
# wavelength Y m peak-to-peak and Z m rms" -- written once per flight, per craft, since the release
# summary shipped. The terrain section below reads the SCENARIO aim point and nothing else, which is
# seat 1 of eight; AimSpread puts the others 12 km apart on ground that is nothing like it. Seat 3
# is seven times rougher and was never in the report -- docs/ACCURACY-PLAN.md 3cb.
GROUND = re.compile(r"ground under the aim on (?P<craft>.+?)(?:_\d+)?: .*?swing (?P<swing>[\d.]+) m, "
                    r"below a 1 km wavelength (?P<pp>[\d.]+) m peak-to-peak "
                    r"and (?P<rms>[\d.]+) m rms")


def ground_per_seat(root):
    """Every seat's own ground, off the lines already in the logs."""
    per = defaultdict(lambda: defaultdict(list))
    for log_path in sorted(root.glob("shots/*.log")):
        log = log_path.read_text(errors="replace")
        order = [c for c, _p, _s in PERFLIGHT.findall(log)]
        where = {c: i for i, c in enumerate(order)}
        for m in GROUND.finditer(log):
            seat = where.get(m["craft"])
            if seat is None:
                continue
            for k in ("swing", "pp", "rms"):
                per[seat][k].append(float(m[k]))
    return per


def say_ground_per_seat(root):
    per = ground_per_seat(root)
    if len(per) < 2:
        return

    print("\n== the ground under each seat's own aim point")
    print("   the section above reads the SCENARIO aim point only, which is one of these.")
    print("   sub-km relief is what a shallow arrival turns into downrange miss (3cb).")
    print(f"   {'seat':>4}{'n':>5}{'swing':>10}{'sub-km p-p':>13}{'sub-km rms':>13}")
    for seat in sorted(per):
        g = per[seat]
        sw, pp, rm = (statistics.median(g[k]) for k in ("swing", "pp", "rms"))
        print(f"   {seat + 1:>4}{len(g['rms']):>5}{sw:>9.0f}m{pp:>12.1f}m{rm:>12.1f}m")

    rough = max(per, key=lambda s: statistics.median(per[s]["rms"]))
    smooth = min(per, key=lambda s: statistics.median(per[s]["rms"]))
    ratio = statistics.median(per[rough]["rms"]) / max(statistics.median(per[smooth]["rms"]), 1e-9)
    print(f"   seat {rough + 1} is {ratio:.1f}x rougher than seat {smooth + 1} below a kilometre, "
          f"and lands accordingly.")


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
    # The attributed landings where the night has them: pooling `impacts` gives every rocket in a
    # world the whole world's landings, so a fit over eight flights counted each one eight times.
    # The slope was unaffected -- a duplicated point does not move a least squares line -- but `n`
    # and the standard error were, and a log written before the trace named its craft has only
    # the pooled form.
    pts = [q for s in shots for q in s["own_impacts"]] or [q for s in shots for q in s["impacts"]]
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
    ap.add_argument("--frame-check", metavar="SHOT", nargs="?", const="",
                    help="burn-phase frame time per shot; non-zero if any is too slow to score")
    ap.add_argument("--instrument", action="store_true",
                    help="is the warhead trace recording? run after the FIRST shot -- exits "
                         "non-zero when the declared endpoint will be empty")
    ap.add_argument("--main", metavar="FACTOR",
                    help="pool the arms into FACTOR on/off and report that main effect")
    ap.add_argument("--paired", action="store_true",
                    help="compare the variants flown inside each shot -- see Sim/ShotArms.cs")
    ap.add_argument("--endpoint", choices=sorted(ENDPOINTS), default="miss",
                    help="what --paired scores on: the miss at the ground, or the walk after "
                         "release, which is the part of it the arrival angle acts on")
    ap.add_argument("--levels-from", metavar="DIR",
                    help="fit the seat levels from another night, so the divisor cannot absorb "
                         "any of the arm under test")
    ap.add_argument("--terrain", action="store_true",
                    help="the relief under the impacts, and whether it is shaping the misses")
    args = ap.parse_args()

    # Ahead of load(), which wants shots.tsv and a full parse. This reads the logs directly, so it
    # answers after a single shot -- which is the whole point of it.
    if args.frame_check is not None:
        sys.exit(frame_check(args.directory, args.frame_check or None))

    if args.instrument:
        traced = False
        for line in (pathlib.Path(args.directory) / "batch.tsv").read_text().splitlines():
            if line.startswith("trace\t"):
                traced = line.split("\t", 1)[1].strip() == "on"
        sys.exit(instrument(args.directory, traced))

    root, shots = load(args.directory)
    arms = sorted({s["arm"] for s in shots})

    if args.main:
        main_effect(shots, args.main)
        return

    if args.paired:
        paired(root, shots, args.endpoint, args.levels_from)
        return

    if args.endpoint != "miss":
        sys.exit("--endpoint only says what --paired scores on; pass --paired too")

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
    say_ground_per_seat(root)

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
          f"{'dt ms':>7}{'coast ms':>9}{'off-g max':>10}{'cross':>8}{'shared':>8}")
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

        def mx(key):
            vals = [v for s in mine for v in s[key]]
            return max(vals) if vals else float("nan")

        print(f"   {arm:<14}{med('residual'):>9.3f}{med('own_km'):>8.2f}"
              f"{med('trim_release'):>9.3f}{med('probe_km', True):>9.2f}"
              f"{med('thrown', True):>8.0f}{med('arrival_deg', True):>8.1f}"
              f"{med('band_deg', True):>9.2f}"
              f"{med('final_down', True):>+9.0f}{med('final_cross', True):>+9.0f}"
              f"{med('early_s', True):>9.2f}"
              f"{med('lag_m', True):>8.0f}"
              f"{med('dt_ms', True):>7.1f}{med('coast_ms', True):>9.1f}"
              f"{mx('off_grav'):>10.4f}{mx('off_cross'):>8.3f}"
              f"{sum(s['shared_bubble'] for s in mine):>8}")

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

    print("\n== what the correction loop left (medians over usable shots)")
    print("   the arc it actually flew, what the trim still owed when the warheads left, and how")
    print("   big a step the aim loop was taking. cot(gamma) says the first of these dominates the")
    print("   precision, and until the release summary shipped a baseline flight never recorded it")
    print(f"   {'arm':<14}{'arc deg':>9}{'floor':>8}{'afford':>8}{'owed m/s':>10}"
          f"{'response':>10}{'raw':>8}{'plant':>7}{'worse':>7}{'flights':>9}")
    for arm in arms:
        mine = [s for s in shots if s["arm"] == arm and usable(s)]
        if not mine:
            continue

        def per(key):
            vals = [v for s in mine for v in s[key]]
            return statistics.median(vals) if vals else float("nan")

        seen = sum(len(s["arc_deg"]) for s in mine)
        print(f"   {arm:<14}{per('arc_deg'):>9.1f}{per('floor_deg'):>8.1f}"
              f"{per('afford_deg'):>8.1f}{per('release_owed'):>10.3f}"
              f"{per('response'):>10.2f}{per('raw_response'):>8.2f}"
              f"{per('plant'):>7.0f}{per('worse_for'):>7.0f}{seen:>9d}")

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
