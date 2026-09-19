# How to spend a night of ballistic shots

Ten changes were flown in one session and every one lost. Some of them deserved to; several were
priced below what the measurement could ever have resolved, and were argued about anyway. This
file is the protocol that stops that happening again — how many shots a question needs, what the
baseline is, what order to fly them in, what to write down, and when to stop.

It is executable. `tools/shot-batch.sh` runs it and `tools/shot-report.py` reads the result and
prints the verdict, so the operator makes no judgement calls between starting the batch and
reading the table in the morning.

`docs/MIRV-NEXT.md` is what the shots are *for*; item **-1** there is the record of the session
this exists because of.


## A seat is a hillside, not a draw

**Eight rockets in one world are not eight samples of one distribution.** `AimSpread` puts each
rocket's aimpoint 12 to 72 km from the group's point in seat order, so each one lands on its own
piece of ground — and the height its round stops at against that ground is a **fixed bias**, not
noise. Measured over 12 shots and three hours on 2026-09-02: 44 of 44 traces share their seat's sign
(p = 5.7e-14), repeatable to **1-2 m**, running −16.7, −7.6, −42.4, +29.3, −110.0, +62.0, +61.3,
+50.0 m across the eight. Worst seat to best is **49x** in median miss.

It is fully mediated — seat to stop-height +0.680, stop-height to miss +0.892, seat to miss
controlling for stop-height **+0.084, p = 0.586** — and it vanishes at a flat aim, where the
stopping-height error is 0.0 m in all 56 traces and Friedman's p goes from 1.4e-4 to 0.38.

Two rules follow, and the second is the one that has already cost a night:

* **Never pool seats within one arm.** A median over one arm's flights is a median over whichever
  hillsides that arm happened to draw. `--paired` survives only because the arm rotates across seats
  from shot to shot; take that rotation away and the comparison is terrain.

### The rotation saves the estimate and not the interval

**Rotating is what makes the comparison unbiased. It is also what makes it deaf.** Within one shot
the two arms sit on *different* ground, so identical code does not produce a ratio near 1.0 — it
produces one that alternates. Flown on 2026-09-05-2112: 0.56, 2.58, 0.47, 2.06, 0.35, 2.24, 0.51,
1.05, 0.35, 2.49, 0.49, 2.00. The median of that is 0.96 and correct; the *spread* of it is the
roster's terrain, and the distribution-free interval reads it as scatter.

It therefore does not shrink with n — 6 blocks report [0.28, 3.47] and 20 blocks [0.37, 2.73] on the
same identical code — because a deterministic alternation is not sampling noise and no number of
shots averages it away.

**So the seat's level is divided out before the ratio is formed.** Each seat's level is the
*geometric mean of the per-arm medians* at that seat, which is what makes it arm-neutral: an arm
worse by k everywhere raises every level by sqrt(k), and that divides out of the ratio exactly.
Pooling the seat's flights instead would let whichever arm flew it more often set the level, putting
the effect under test into the thing it is measured against.

Measured over 3,000 random 14-block nights drawn from the three single-arm nights — genuine
identical code, 34 worlds:

| | false RESOLVED | interval width | power at ×0.80 | at ×0.70 | at ×0.60 |
| --- | --- | --- | --- | --- | --- |
| un-levelled | 0.0% | 5.69× | 0% | 2% | 9% |
| **seat-levelled** | **3.0%** | **1.58×** | **33%** | **62%** | **90%** |

Nominal α is 2.9%, so the levelled test is correctly calibrated and the raw one is merely
conservative — its interval is so wide it can seldom resolve anything above ×0.4, which is most of
what gets flown. **A ×0.60 change goes from 9% power to 90% for the same night.**

It is calibrated from the night under test rather than from a stored table, so it needs no control
data and works on the first night at a new target — which matters, because moving the target from
2,000 km to 6,269 km on 2026-09-02 took the worst-to-best seat spread from 1.4× to 12× and nothing
announced it.

The un-levelled ratio is printed beside the levelled one. A large gap between them is the roster's
ground, and worth looking at rather than absorbing silently.

**Levelling is for a two-arm night, and on a four-arm one read the un-levelled line.** Eight seats
across four arms is two flights per seat per arm per shot — a quarter of what two arms give — so the
level is estimated from too little and its own noise outweighs the terrain it removes. Measured on
2026-09-01-2148, the only case that goes the wrong way: p50 reads [0.24, 1.52] levelled against
[0.26, 1.12] raw. Two arms is what `--paired` is for and what §3 already recommends; this is one
more reason.

**What this already cost.** 2026-09-03-1544 flew `ArrivalPreference=0.0` and reported ×2.24
[0.90, 4.57], p=0.105 — unresolved, a night that answered nothing. Levelled it is ×2.36
[1.88, 2.94]: a clear loss, from the shots already flown.
* **Anything that lands the two arms at different times is a confound**, because the world does not
  hold still between them. Flown: the harness asks for 8x once the first salvo is away, so one arm's
  rockets stopped on 18-33 ms frames and the other's on 117-267 ms, which multiplies a seat's own
  bias by **5.8x**. That is what `docs/ACCURACY-PLAN.md` item 5d measured instead of the setting it
  was flying.


## The measurement, before anything else

Run-to-run scatter on an identical pick-up, from the only two batches recorded shot by shot
(`MIRV-NEXT.md` item 0, ten shots with no intervening change):

```
0.38  0.42  0.49  0.63  0.70  0.82  0.96  1.18  1.47  2.10  km
median 0.76   mean 0.92   sd 0.54   max/min 5.5x
```

Fitted lognormal: **median 0.79 km, geometric sd ×1.74** (σ on logs 0.555). The older sixteen-shot
batch, with the 2 m/s ejection kick still in, implies σ 0.83 and reached 3.43 km — so the tail has
come in, and the distribution is still multiplicative and still right-skewed.

That distribution is the whole constraint. Everything below follows from it.

### What a batch of n can resolve

Two-sided rank test at 5%, 80% power, simulated on the fitted distribution. **This table assumes
the seat level is divided out** — it is a simulation of the miss alone, so it describes the
levelled estimator and always did. Un-levelled, the real numbers are about one column worse than
the worst row here, and flatten rather than improving with n.

| shots per arm | smallest difference it settles |
| --- | --- |
| 6 | ×0.33 — **530 m** |
| 8 | ×0.42 — 460 m |
| 12 | ×0.50 — **400 m** |
| 16 | ×0.56 — 350 m |
| 20 | ×0.58 — 330 m |
| 25 | ×0.62 — **300 m** |

Read the last row twice. **Fifty shots split evenly between one change and a re-flown baseline is
what a 300 m difference costs.** A whole night, for one question, at four-in-five odds of seeing
the effect if it is there.

A **1 km** difference is cheap by comparison, and the reason is arithmetic rather than luck: the
median is 0.79 km, so a kilometre downward is not a shift but the near-total removal of the miss —
a factor under 0.25, which **six shots an arm** settle at 99%. A kilometre *upward* is a factor of
2.2, which eight an arm catch at about two-thirds. This asymmetry is worth keeping in mind, because
ten of ten flown changes lost: catching losses is the common case and it is the cheap one.

The corollary nobody likes: **a change priced headlessly at 60 m, or 160 m, or 235 m is not
flyable.** It is not that it needs more shots than the night has; it needs more shots than a week
has. Land those on the strength of the rig and the argument, batched together so their sum clears
the bar, or leave them.

## 0. First ask whether it needs a night at all

**A night is for a change to the *shot*. A change inside the *round* can be flown against itself.**

The bus drops six warheads on one trajectory, and on the flown shot they land within **6.2 m** of
each other. So a split arm — odd tubes shipped, even tubes under test — makes a single flight a
paired comparison with the cutoff, the trim, the frame pacing and the weather all held identical by
construction. `tools/ab-shot.py` reads one log and scores it.

Validated against a term whose answer was already known. The mid-frame gravity aim, flown 2026-08-24
as eight against eight interleaved, measured **-336 m** of walk and took 2.45 hours. The same change
as a split arm, **one shot, nine minutes**:

```
   tube  side          miss
      1  under test      423 m
      2  shipped         746 m
      ...
   difference      -311 m   against 14.7 m of within-side scatter  ->  21 sigma
```

Within 7% of the night, at a sixteenth of the cost.

> **It was suspended for a day and the rig was never at fault.** A three-condition split read
> within-condition scatter of **193 m**, with two warheads labelled `shipped` landing **332 m apart**.
> The cause was `dev` itself: a `git add -A` on a screening branch had swept that arm's tube-parity
> split into a tools commit, which was then cherry-picked onto `dev`. Tube 3 is odd and tube 6 is
> even, so the two `shipped` warheads were **two different builds**.
>
> The lesson is not about splits. **A screen is only as good as the baseline underneath it**, and a
> split arm's control is the one thing that can detect a contaminated baseline — it did, a whole
> night before the arithmetic would have. Read a broken control as "something is wrong with the
> tree" before reading it as "something is wrong with the rig".

### The same argument one level up: split the ROCKETS

**A change upstream of the release cannot be split by tube, and it can be split by craft.** Each
rocket in a multi-rocket world carries its own computer, its own trim and its own correction loop —
so `--paired` gives four rockets the change and four the baseline in one run, and the guidance, the
bus, the arrival and the release timing are all back inside the comparison.

```bash
./tools/shot-batch.sh --paired 'base|ceiling:TrimCeilingFromBudget=true' --blocks 6
./tools/shot-report.py ~/shots/<night> --paired
```

**It exists because the between-run instrument stopped working at eight rockets.** `MIRV-NEXT` 8aa
flew two batches three hours apart on identical code and read the same baseline at **14.49 km and
5.43 km** — a 2.7x session swing, larger than any effect on the backlog, with the arm between them
reversing from 0.66x to 3.52x. Every number in section 1 below was derived from a *single-rocket*
distribution with a 0.79 km median and a x1.74 geometric sd; the eight-rocket geometry runs a 5–15 km
median with shots from 2 km to 69, and **none of that arithmetic was ever re-derived for it**.

Three things make the paired version work, and the third is the one that is easy to get wrong:

* **The statistic is a sign test over shots, not a rank test over flights.** One shot yields one
  ratio with the world held identical, so six shots reach p=0.031 — which the between-run test
  cannot reach at any n this project can afford. Eight rockets are not eight draws and are counted
  as one.
* **The change must be a *setting*, not a branch.** One build flies the whole night; the arms differ
  by what `IcbmConfig` says. A branch cannot be two things in one world.
* **The variants alternate down the roster, and the phase rotates between shots.** A rocket's place
  in the roster is worth **175x** in miss, monotone (8y), so handing one arm the first four rockets
  measures the gradient and calls it the change. `Sim/ShotArms.cs` does both.

What it cannot compare is anything the rockets share: the build, the system, the terrain under the
target. Those still need a night.

**Two limits on the per-tube split, and the first is a silent false negative.** Only a **per-round**
term can be split that way —
the aim, the integrator, the sub-step, the drag. Anything upstream of the release is shared by all
six warheads: guidance, the bus, the arrival angle, the release timing. A split arm reads those as a
dead heat *however large the effect is*, and nothing in the output says so. Check the change is
inside the round before trusting a null.

And it is a **screen, not a verdict**. It scores the walk and the per-warhead miss on one flight;
what ships is decided on the group miss over an interleaved batch, because that is the number a
player gets. Screen many ideas cheaply, then spend a night confirming the one that won.

**Why the night was ever needed for this.** The group *miss* carries the aim correction's
shot-to-shot variance — 11% across the sixteen — while the term under test moved the *walk* by 48%
against 2% of scatter. Sixteen shots measured a **27 sigma** effect. The endpoint was the expense,
not the question.

### Screen the session before trusting the night

**Read the frame time first.** `shot-report.py` prints it and warns above 24 ms:

```
   frame time: 29.8 ms, 0 correction pass(es) at the median shot
   WARNING: at or above 24 ms this session is in the slow regime.
```

Every night flown at 21 ms landed a median 9.3 km and ran the post-boost correction 1.2–3.4 passes
a flight. Both nights flown at 27–30 ms landed 20.5 km and ran it **0.24**. An arm that acts on the
correction loop cannot be measured in a session where the loop does not run, and the result comes
back as an ordinary unresolved rather than as an error — which is how a night gets spent and read as
a null.

**The harness asks for the whole speed through the coast** — `BallisticScenario.CoastWarpFactor`,
100x — and lets `WarpPolicy` take it back wherever something is being integrated, which is the span
that matters. Asking for a *step* instead was tried and reverted: it pinned the whole pre-release
cruise, forty minutes in which nothing is integrated and any step is free, and cost four times the
wall clock a shot for no accuracy. The step that matters is the trim's, and it is asked for where it
is needed, as `BusTrim.MaxFaithfulStep`. So the frame-time screen stays: it still says what a night
cost and what regime it ran in.

It marks the *session*, not the shot: within a night the rank correlation between a shot's frame
time and its miss is nil. Whether it causes the miss or is a symptom of whatever else the machine is
doing is not known, and it does not need to be to be useful as a screen. `MIRV-NEXT` **8ac** is the
record, including the control shot that ruled out the build.

## 1. The statistic

**Endpoint: the group's `mean` miss, on a log scale.** Not `worst`, which is the pass/fail bar and
is a maximum over six warheads — the noisiest of the four numbers `ShotGroup.Judge` prints. Not
`spread`, which is a different mechanism and is analysed *beside* the mean, never mixed into it: the
six tube mouths sit on a 0.86 m ring about the release line while every release prediction uses the
mean mouth, so a group lands on the ground image of that ring, at whatever roll the bus happened to
hold (`docs/ACCURACY-PLAN.md` 3db). `spread` gets its own table in the report and its own verdict.
At a metre-level shot read the mean as `--endpoint landing`: the FLIGHT line prints it to a whole
metre (below, after `spread`).

**Comparison: Wilcoxon rank-sum, exact null.** Ranks, because at n=12 nothing here is normal and a
t-test on a lognormal tail is a machine for generating significant nonsense. Exact rather than the
normal approximation, because the approximation is loosest at exactly these sizes and the whole
point is not to over-claim. `tools/shot-report.py` builds the null by the standard recurrence; it
costs milliseconds.

**Effect size: Hodges–Lehmann, the median pairwise log-ratio, with a distribution-free interval.**
Not a difference of medians. Two reasons:

- A difference of medians is a point with no interval, and **the interval is what makes an
  unresolved arm a finding instead of a shrug**. "Unresolved, and the night ruled out anything
  better than ×0.75" is a real result. "No significant difference" is not.
- On a log scale the estimator is a *ratio*, which is the right shape: every term in the error
  budget is a velocity error times a trajectory sensitivity, so the mechanisms multiply. A change
  that halves a term halves it at every median.

**Report the median for reading, never the mean.** In the sixteen-shot batch the mean is 1.20 km
against a median of 0.85 — one shot in sixteen moved the arithmetic mean by 40%. And the single
best result ever recorded here, 0.09 km, came from the radial-jets arm, which was four times worse
overall. Any statistic that reads the best, or that a tail can drag, is actively misleading on this
data.

**Two looks, so α is 0.0294.** The gate looks once mid-batch and the report once at the end;
Pocock's constant boundary for two looks spends 5% overall. Do not add a third look by eye.

### The endpoint assumes one mode, and a floored shot has two

**Check the shape before trusting the verdict.** The median-on-a-log-scale endpoint is chosen for
the distribution measured at the top of this file — multiplicative, right-skewed, one mode. Under
`IcbmConfig.MinArrivalAngleDeg` the miss stops being that. It becomes two tight clusters set by
whether the post-boost correction finished: **0.01–0.14 km if it did, 0.83–3.92 if it did not**, and
across the fifty shots of `2026-08-27-0040` **not one landed in between**.

A median cannot see a change that moves shots *between* modes; it only reports which mode the middle
shot fell in. That night the control's 12th, 13th and 14th shots were 0.04, 0.08 and 0.83 km, so its
median sat exactly on the boundary and was decided by 13 converging against 12 not. The rank test
fails the same way — thirteen control shots rank at or above the arm's typical shot. The verdict
read **UNRESOLVED, p=0.464**, on a change that took shots missing by over 500 m from 12 of 25 to 1
of 25 at `p = 3.8 × 10⁻⁴`.

So when the outcome is bimodal, the thing to pre-register is **which mode a shot lands in** — a
proportion, tested with Fisher's exact — and the median is a diagnostic rather than the endpoint.

`--paired` now runs that test itself, in "which mode the flights landed in", and splits on the
**terminator** rather than on a cut through the miss: `trim` is the bus giving up before its first
pulse, and it separates the two populations almost perfectly — 60 of 61 such flights past 60 km
against 0 of 185 that converged. A threshold on the outcome would be fitted to the sample it is
then read from; the ending is a mechanism, and it is already recorded per flight.

Read that table before the ratio. If the two arms differ there, the ratio is answering a different
question from the one you asked — 2026-09-05-2339 is the case: QuietCoast takes the lost mode from
12 of 56 to 1 of 56 (**Fisher p=0.0020**) and takes the healthy median from 0.017 km to 3.607, and
the pooled ratio of ×70 describes neither half.

**This is not licence to change endpoint after seeing the data.** The mechanism has to be named in
advance, which for that night it was — `docs/MIRV-NEXT.md` item 8s set the open question as whether
more budget makes a *stopped* shot converge. What the night showed is that the pre-registered
*statistic* was wrong for the pre-registered *question*, and those are separable.

### A ratio needs a positive number, and the walk crosses zero

`--endpoint walk` and `release` score a **magnitude**, because the whole pipeline is a median
pairwise **log**-ratio and a ratio has no answer at or below zero. That costs a floor, and
`WALK_FLOOR_M` is where it bites: on `2026-09-12-pulse2` **71 of 160 flights score the floor rather
than their walk**, and seats 1, 2 and 8 are forced to a per-seat ratio of exactly 1.00. Simulated, a
true 0.7x reads **0.84x** — about half the effect censored away. (The `release` floor is a different
and much smaller thing: half the step the trace's release probe prints, which at whole metres cost 3
of 160, and which follows the print to half a millimetre on a build printing `Distance.Measure`.)

**`--endpoint signed-walk` and `signed-cross` are the way out.** No `abs`, no floor, a **difference
in metres** rather than a ratio, and the per-seat level subtracted rather than divided — the
additive analogue, fitted as the **mean of the per-arm means** so the arm under test cannot set the
level it is measured against. The shot-label flip stays the verdict and costs nothing: measured
against pseudo-arms balanced on the real arm, where the truth is exactly zero, the signed forms
false-positive at 3.5% and 2.0% against a nominal 2.9%.

**But signed is a different question, and its trap is cancellation.** An arm that shrinks a walk
*toward zero* lifts the negative seats and lowers the positive ones, so pooling them nets the two
against each other. On `2026-09-09-walk3` seat 3 went −58 → −19 m and seat 6 +3 → +12: **+39 m and
+9 m of signed difference for one mechanism and its opposite**, which pool to far less than either.

So the rule is about the *shape of the term*, not about which endpoint is newer:

| the term is | read |
| --- | --- |
| one-signed across every seat | `signed-walk` pooled — it is exactly what that endpoint is for |
| **per-seat signed** — a gradient times a displacement | **`--per-seat`**, below. The pooled signed figure under-reports it, and the ratio censors it |
| a magnitude with no meaningful sign | `walk`, and check how many flights hit the floor |
| **how wide the group is**, rather than where it went | `spread` — the only endpoint the aim correction cannot reach, because it is over before the warheads separate |
| **the miss itself, at a metre-level shot** | `landing` — `miss` off each warhead's own line, at 0.1 m or a millimetre, where `miss`'s own print is a whole metre |
| **where the group's centre went**, or **how wide it is about that centre** | `centre` and `dispersion` — the miss split in two off the landing line's components. **First flown on `2026-09-13-spin`**, below |

**And `signed-cross` is a control channel, not a null.** It is near zero for most arms and genuinely
is not for some: `2026-09-12-order` resolves it at **−0.161 m [−0.170, −0.139], 0 of 20 shots
positive**. A cross reading that moves is evidence about geometry, not proof of a mistake.

There is deliberately **no signed `release`**: the `release probe:` line carries the components but
no craft name, so under 3ce's rule one flight's number would be worn by all eight. Nor a signed
`spread`, which is a magnitude with no direction to have a sign.

### `--per-seat`: each seat against itself

```bash
./tools/shot-report.py ~/shots/<night> --paired --endpoint signed-walk --per-seat
```

**A per-shot median over one arm's four seats discards the seat carrying a per-seat term.** On
`2026-09-12-query` the pooled `signed-walk` gave the fix the wrong sign on five of twenty blocks while
a `base` seat was walking 2–8 m. Per seat, nothing is pooled and nothing is levelled: each seat's
median on the arm is set against its own median on `base` **as magnitudes about the probe's zero**, so
seat 6 coming down from +5.49 m and seat 3 coming up from −2.81 m both score positive. The statistic
is the median over seats of `|base| − |arm|`.

**The mechanism is checked, not assumed.** Each landing is followed by `ground sample: over a N ms
frame`, so each seat's walk is fitted against the duration of its own frame, and the second statistic
is the median over seats of `|slope|` on `base` minus on the arm. A ground displacement times a
gradient collapses there; a term that shrinks magnitudes some other way does not, and
`2026-09-12-order` is that case — **+1.667 m per seat at p = 0.0005, slope +0.0012 m/ms at p = 0.94**.
A night whose logs lack the line says so and prints no slope.

Four things about how it is built:

* **The verdict is the shot flip, drawing exactly the flips `--paired` draws**, so the two relabel the
  same shots. Relabelling every shot negates both statistics, so the null is symmetric and the rule
  is two-sided.
* **The interval resamples shots, never seats.** Eight seats are the roster rather than a sample of
  one, so an interval over their eight values measures how unlike the hillsides are, which no number
  of blocks shrinks. Shots are resampled within their labelling, so every seat keeps its count on each
  arm.
* **A slope needs five flights with a frame time on each arm at a seat, and the interval five shots
  in each labelling** (`MIN_SEAT_FLIGHTS`). Below that the seat is refused and named, and no interval
  is printed.
* **`--levels-from` is refused**, because each seat is its own control and there is no level to lend.

**Calibrated on pseudo-arms balanced on the real arm per seat, not only per shot.** A fixed seat
partition flipped as `ShotArms` flips it — which is how the signed endpoints were calibrated — puts
each seat wholly on one real arm, so this test would read the real effect at full strength. The
pseudo-arm is instead the real label XOR a partition of two even and two odd seats XOR a per-shot coin
balanced within each real phase. Every shot then holds two flights of each real arm per pseudo-arm,
and every seat five of each. Over 400 splits a night, at twenty blocks, against a nominal 2.9% with a
standard error of 0.8%:

| night | its real arm, per seat | false RESOLVED, magnitude | slope | interval excludes 0, magnitude | slope |
| --- | --- | --- | --- | --- | --- |
| `2026-09-12-pulse2` | nothing (p = 0.59, 0.22) | 0.8% | 1.5% | 0.8% | 1.2% |
| `2026-09-11-read` | nothing (p = 0.85, 0.42) | 2.8% | 3.2% | 0.2% | 0.8% |
| `2026-09-12-floor` | nothing (p = 0.72, 0.16) | 2.0% | 2.2% | 0.5% | 1.2% |
| `2026-09-12-order` | magnitude only (p = 0.0005, 0.94) | 0.0% | 1.8% | 1.8% | 0.0% |
| `2026-09-12-query` | both (p = 0.0015, 0.0005) | 1.0% | 2.5% | 1.0% | 0.5% |

**At most a standard error over nominal where there is nothing to find, and under it where there
is.** On `order` and `query` the magnitude falls to 0.0% and 1.0%. A flip that unbalances a seat's
real arms carries the night's own effect into the null, and that costs power rather than validity.
Every interval excludes zero on 0.0–1.8% of splits.

**Below five, it refuses.** Subsampled to fewer blocks, with nulls from 200 pseudo-arm splits of
`pulse2` (standard error 1.2%) and power from 100 subsets of `query`'s real arm:

| blocks | flights a seat an arm | flip false RESOLVED, magnitude / slope | ungated interval excludes 0 | `query` resolved, magnitude / slope |
| --- | --- | --- | --- | --- |
| 6 | 3 | 0.0% / 0.0% | 26.0% / 16.0% | 0% / 0% |
| 8 | 4 | 1.0% / 2.0% | 4.0% / 1.5% | 42% / 79% |
| 10 | 5 | 2.5% / 2.0% | 3.5% / 1.5% | 82% / 99% |
| 12 | 6 | 3.5% / 1.0% | 2.0% / 2.0% | 97% / 100% |

The flip stays calibrated all the way down and is always printed. Six blocks cannot resolve at all:
the observed labelling and its mirror are already 2 of 64, and the report says so below seven.

What fails is what the flip does not protect:

* **The interval.** It resamples within a labelling, and a pseudo-arm's labellings hold a quarter of
  the blocks rather than half, so that column is one or two shots a labelling at six blocks and three
  at twelve.
* **A slope from four flights.** It has two degrees of freedom and a t quantile at α of 5.7, against
  3.9 at five.

So five flights of each arm at a seat before a slope is fitted. The interval is sound from three shots
a labelling, and it shares the slope's five so that one number decides whether a night is big enough
to read per seat. On an eight-block night that refuses a slope which would have resolved `query` 79%
of the time — the price of not printing a per-seat slope nobody can read.

Read on the night it was built for:

```
   query vs base, per seat: +0.810 m   [+0.422, +1.042] at 97%
      nearer zero on 7 of 8 seats, shot-flip p=0.0015   RESOLVED
   query vs base, slope on frame time: +0.0607 m/ms   [+0.0481, +0.0677] at 97%
      flatter on 8 of 8 seats, shot-flip p=0.0005   RESOLVED
```

The between-seat sd of the median walk goes from 2.61 m to 0.06 m. The slope's p is the floor of 2,000
draws; a scratch run of 20,000 put it at 0.0001 and the magnitude at 0.0010.

### `--endpoint spread`: how far apart one rocket's six warheads land

Everything above measures where a group *went*. This measures how *wide* it is — 2.0 m at the ground
from release probes 0.20 m apart (3cv) — and it is the one quantity the post-cutoff aim loop cannot
touch, because that loop is over before the warheads separate.

**Worst minus best**, which is what `ShotGroup` already reports, so no number in `docs/` changes
meaning. It is also the choice that measures best, which was not the expectation: on the null
scatter of the paired log ratio it reads **0.211, against 0.242 for the group's own standard
deviation, 0.307 for a trimmed range and 0.310 for a median absolute deviation** — the two order
statistics beat all three estimators that use more of the group. At six warheads a range is an
efficient spread estimator and a MAD is barely defined.

**Ranked on the null rather than on the flown arm, and that part matters.** A per-shot sd from a
handful of blocks cannot rank five statistics: on this very night it read **0.075 at six blocks and
0.196 at seven** — a 2.6× move from one shot landing. The null has thousands of replicates from the
same seven, by relabelling a **fixed 4/4 seat partition that cuts across parity** (so it is balanced
on the real arm) with its labels flipped per shot exactly as `ShotArms` flips them. That keeps the
design and removes only the arm. The five statistics' ranking is the same at six blocks, at seven,
and on the null; only the magnitude moved.

Three things to know before reading one:

* **It is a spread of *distances from the aim*, not of positions.** Six warheads 2 m out in six
  directions read zero. `--endpoint dispersion` reads positions, off the components the landing line
  carries since `7e079ea`, and the difference is not academic: on `2026-09-13-spin` bringing the
  group's centre in read **0.85x [0.80, 0.94]** on `spread` and **1.00x [0.98, 1.01]** on `dispersion`,
  with the ring untouched. A change that moves the centre is read on `dispersion`, never on `spread`.
* **A range's expectation grows with the group size**, so a night mixing five- and six-warhead
  groups would read the mix. Every flight of every night flown so far released six, and `usable`
  already requires all of them to arrive.
* **A night flown before `a1a1ae5` scores nothing**, deliberately. Those logs print the group in
  kilometres to two decimals — a **10 m quantum on a 2 m quantity** — and do not name the craft, so
  3ce's rule applies as well. Refusing is more honest than reporting the quantisation.

**What it can resolve, re-measured at the 0.1 m print.** The audit put this at ×0.75 with n=20,
against the old 1 m quantum. The null sd of 0.211 is an **MDE of ×1.15 at twenty blocks** (90% band
[×1.08, ×1.19]), and the seven flown blocks agree at ×1.14. That is **far inside ×0.75: item 41 is
flyable at twenty blocks with a wide margin**, and the margin is what makes the conclusion safe
while the estimate is still moving.

**The print is no longer a material term.** Rounding one warhead to 0.1 m contributes 0.029 m of
standard deviation, and a max−min of two of them 0.041 m — **1.6% of the 2.50 m median spread**,
where the old 1 m quantum contributed 0.41 m, or 16%. Re-scoring with every value dithered inside
its own bin does not move the per-shot variance measurably at this n.

**It is material again once a group lands inside it, so the floors follow the print.** With the width
at 0.035 m (3dg) and item 43 aimed at the centre, every group endpoint on the arm reads the 0.1 m step
rather than the group. The landing line and both release probes print through `Distance.Measure` —
**a tenth of a millimetre** below a kilometre — and `shot-report.py` takes each flight's floor off the
step its own lines printed: a whole step for `spread`, `landing`, `centre` and `dispersion`, half of
one for `release`. A night printed at 0.1 m floors exactly where it did, and a night printed finer
floors finer with nothing to change, because `_print_quantum` reads the step off the number's own text.

**The millimetre went the same way the metre and the 0.1 m did**, and for the same reason: it was
sized when a group landed inside tens of millimetres. Since 3el–3en the median worst warhead is 5 mm
and the dispersion 2–3 mm, so the millimetre step was five bins and two — a few per cent of the
variance a night is trying to resolve, on the endpoint the verdict is read from. That is the fifth
time a readout has gone blind as the shot improved, which is what `Sim/Distance.cs` exists to say.

**One incoherence to know about, and it predates the signed endpoints.** The verdict is read off the
randomisation while the interval beside it comes from the sign test, so a night can print
**RESOLVED with an interval spanning zero** — `2026-09-11-ground` reads `+2.103 m [−0.176, +3.039]`
at shot-flip p=0.008. The ratio path has the same property. Read the flip.

### `--endpoint landing`, `centre` and `dispersion`: the miss at a metre, and the two terms in it

**`miss` is blind at a metre.** It scores the FLIGHT line's mean, printed in kilometres to three
places — a whole metre. On `2026-09-12-query` its 160 flights take **12 distinct values**, seats 1
and 2 level at exactly 2.00 m, and the pooled median prints `0.00` for both arms: `Sim/Distance.cs`'s
fixed unit going blind as the shot improves, one layer up.

**`landing` is the same quantity off the named 0.1 m lines** — the mean of one rocket's per-warhead
distances. The two may differ by the FLIGHT print's half metre plus a twentieth; on `query` the worst
of 160 is **0.50 m**, on two flights, and `landing` takes 116 distinct values. Read on that night:

| | levelled | shot-flip p | un-levelled |
| --- | --- | --- | --- |
| `miss` | 0.66x [0.55, 0.90] | 0.003 | 0.76x [0.62, 0.80] |
| `landing` | 0.70x [0.60, 0.86] | 0.004 | 0.78x [0.63, 0.85] |

**The same verdict on an interval 27% narrower on the log scale**, with the point nearer 1. It
refuses what `spread` refuses and says which reason applied: a night before `a1a1ae5` names no craft
and prints F2 km, and a flight with fewer lines than warheads arrived is not the set the FLIGHT mean
is over.

**A miss is two terms, and a distance cannot separate them.** Six warheads 2 m out on one side and
six on a 2 m ring about the aim read the same `landing`; the aim loop moves the first and item 41 the
second. `centre` is the distance of the group's centroid from the aim and `dispersion` the rms of
the six about it, both off the `(+1.8 m downrange, -0.9 m cross)` the landing line carries since
`7e079ea`. The rms is over n, so **`centre² + dispersion²` is exactly the mean squared distance in
the ground plane**, and the mean planar distance lies between `centre` and that root. Whenever a
night carries the components, both reports print the three side by side per arm, whatever endpoint
was asked for.

* **The components are in the ground plane and the distance is not.** The line does not print the
  arrival frame's up axis, so on a flown night `landing` can exceed `√(centre² + dispersion²)` by
  that term.
* **`dispersion` is an rms because it completes the decomposition, not because it was ranked.**
  `spread` earned max−min on the null scatter of the paired ratio; the same ranking decides this one,
  and `2026-09-13-spin` is the first flown night carrying the components to run it on.
* **A night without them is refused**, with a pointer to `landing`, rather than scored as zero.

**First flown on `2026-09-13-spin`**, 192 flights, all carrying the components, and the split did what
it is for: item 42 took `centre` to 0.55x [0.49, 0.60] and left `dispersion` at 1.00x [0.98, 1.01] on
the same flights (`ACCURACY-PLAN.md` 3de).

**Before that, both were checked on a synthetic log**, which checks the parse and the arithmetic and
measures nothing. Each of `query`'s 960 warheads was given a position consistent with its logged
distance — the 0.86 m tube ring through 3db's gains (1.85 downrange, 0.93 cross per metre, a
1.59 m × 0.80 m ellipse) at a random roll, the centroid fitted, each point rescaled onto its distance
— and written in the new format. Every parsed component is within the 0.05 m print, `centre` and
`dispersion` are within 0.04 m of their unrounded values, the identity holds to 10⁻¹⁴ m², and neither
inequality breaks on any of the 160 flights. That night reads `dispersion` at 0.98x because the model
gave both arms one ring: the construction, not a finding.

## 2. The baseline

**The baseline is an arm of the same batch, flown on the same schedule as every other arm.** It is
not a number from an earlier night, from an earlier commit, or from this file. Every comparison
`shot-report.py` makes is against the baseline arm's shots from the same directory; anything older
is printed for drift and never entered into a test.

That answers "how often should it be re-flown" by construction: **every block, which is every
fourth shot, about half an hour apart all night.**

### Interleave. Always.

Arms are flown in a randomised order *within each block* rather than in blocks of one arm at a
time. It costs one file copy per shot — a second against eight minutes of flight, 0.2% — and it
buys the difference between a nuisance being noise and a nuisance being the answer:

| a linear drift across the night | what a blocked design turns it into |
| --- | --- |
| 0.2 km | 0.10 km of pure artefact between arms — a third of a 300 m effect |
| 0.4 km | 0.20 km — two thirds of it |
| 0.8 km | 0.40 km — **larger than the effect being chased**, with the right sign or the wrong one by chance |

There is no measurement of whether this machine drifts across eight hours. That is the point: a
blocked design has to assume it does not, and an interleaved one does not have to know.

Randomised *within* the block, not rotated, because a fixed order confounds the arm with its
position in the block — the first game launched after the machine has been idle for eight minutes
is not in the state the fourth is. The seed is recorded in `batch.tsv`.

## 3. The run order

**Fly a 2×2 factorial, not one arm at a time.** This is the single largest change to how the last
session was measured, and it is close to free.

Pick two changes, A and B. Four cells:

| arm name | A | B |
| --- | --- | --- |
| `base` | off | off |
| `a` | **on** | off |
| `b` | off | **on** |
| `a+b` | **on** | **on** |

Twelve shots per cell, 48 shots, plus two held back for re-flights. Then:

- **The main effect of A** is `{a, a+b}` against `{base, b}` — 24 shots against 24, which resolves
  **300 m**.
- **The main effect of B** is the same, also at 24 against 24, also **300 m**.
- The **interaction** — whether A and B only help together — is estimable, but only at about a
  kilometre. It is the question one-at-a-time testing cannot ask at all, and this is the cheapest
  way to ask it. Do not read a small interaction as real.

Compare that with the alternative: 48 shots one-at-a-time gives you **one** 300 m answer, or two
400 m answers, and no way to see a combination. The factorial gives two 300 m answers and a look at
the combination, from the same night, because every shot is used twice.

The catch is stated plainly: the main effect of A is only a clean single number if A's effect does
not depend much on B. Where the interaction turns out large, the main effects stop meaning anything
and what you have is four cells of twelve — four 400 m pairwise comparisons, which is still more
than one-at-a-time would have given.

### Naming

Arm names are `+`-joined factor names, and the report pools on them:

```bash
./tools/shot-batch.sh --aim 26.5S,64.0W --blocks 12 \
    --arms base=dev,grav=arm/subgravity,reopen=arm/postboost,grav+reopen=arm/both
./tools/shot-report.py ~/shots/<night> --main grav      # the 24-vs-24 main effect
./tools/shot-report.py ~/shots/<night> --main reopen
```

`base` must be first — the report takes the first arm in `arms.tsv` as the baseline.

### What an arm actually edits

An arm is a commit, so a one-constant arm is a one-line commit on a branch. The scenario sets
`IcbmConfig.Armed` and **nothing else** — every other setting flies at the default declared in its
own file, which is what makes a constant an arm at all. The knobs `MIRV-NEXT.md` currently ranks:

| the arm | file, and what to change |
| --- | --- |
| ~~gravity re-read per sub-step~~ | **Shipped 2026-08-24** (`aea3e2a`), for 0.44 → 0.05 km. Item 2d's warning against flying it alone stands, but its pair was the **pull centre**, not the sub-step — and both went together. Against the round the game now flies, gravity's own marginal contribution is zero. |
| the warhead's own sub-step | `src/KSArmory/Sim/Arsenal.cs`, `ReentryVehicleMk21.SubStepSeconds` at ~1 ms. A **lone** term now, not half of a pair: it takes the round's gap with its own predictor from −149 m to −6 m, flat at every frame from 25 ms to 320 ms. `arm/substep` is built. |
| the coast step the warhead is integrated across | `src/KSArmory/Sim/Arsenal.cs`, `ReentryVehicleMk21.PreferredStepSeconds`. **Not `MaxFaithfulStepSeconds`** — that bounds a clamp that *discards* time, and tightening it flew at 48–60 km (item −0b). Two questions with one shape; the answer to one is never the answer to the other. |
| how far after cutoff the aim reopens | `src/KSArmory/Sim/PostBoostAim.cs` — `MaxSeconds`, `MaxCycles`, `PassesWithoutImprovement`. The largest single term at 740 m, and the one the rig cannot price. |
| what a pass has to beat, and what one costs | `AimCorrection.ImprovedByFraction`/`ImprovedByFloorMetres`, `PostBoostAim.HoldingCostsMetresPerSecond`, `BusTrim.SettledMetresPerSecond` — **one arm, not three**: the bar cannot go below the trim's leavings, because those are what moves the reading it is judging. Item 7f. |
| the warhead's own sub-step | `src/KSArmory/Sim/Arsenal.cs`, `ReentryVehicleMk21.SubStepSeconds`. First order at 30.6 m per ms, and `ProbeGapTests` says it *widens* the round-versus-probe gap alone (591 -> 754 m) — the same cancelling-pair shape as item 2d. Fly it paired or not at all. |
| predicting the warhead with the warhead's integrator | `src/KSArmory/Sim/ImpactPredictor.cs` — the other side of the same gap, 591 -> 47 m headless. Item 2h. |
| re-pointing between releases | `src/KSArmory/Sim/IcbmConfig.cs`, `RepointBetweenReleases` |
| the arrival-angle floor | `src/KSArmory/Sim/IcbmConfig.cs`, `MinArrivalAngleDeg` — the largest lever there is, and it changes every other term's price, so it is a poor thing to have as a *factor* alongside others. Fly it as its own night. |
| the ejection kick | `src/KSArmory/Sim/Arsenal.cs`, `ReentryVehicleMk21.LaunchSpeed` |

**Pick factors whose mechanisms are separate.** Two arms that both act on the aim correction
interact by construction, and a 2×2 that spends its interaction budget on something already known
to interact has learnt nothing the cells did not already say.

### If there is only one question

Then it is one question, and the night is 25 `base` and 25 `arm`, interleaved:

```bash
./tools/shot-batch.sh --aim 26.5S,64.0W --blocks 25 --arms base=dev,arm=arm/whatever
```

That is the right shape when a change is expected to be worth 300 m and nothing else is ready to
fly. It is the *wrong* shape when two changes are ready, because it wastes half the night proving
a baseline that a factorial would have proved anyway.

## 4. What to capture, and what each thing attributes

Everything below is already in the log at the verbosity `BallisticScenario` turns on for a
scenario run. None of it costs a flight. **All of it is gone the moment the next shot starts** —
`scenario.sh` truncates `KSArmory.log` at every launch — which is why `shot-batch.sh` copies the
log out between runs and why a bare `for` loop around `scenario.sh` loses the entire diagnostic
half of the night.

| captured | from | what it attributes |
| --- | --- | --- |
| **pick-up altitude and speed** | `already flying at N km doing M m/s` | **whether two shots are the same shot at all.** The same save picked up 35 s further on is a differently conditioned arc worth 164 km (item 7d). If this varies across the batch, nothing else in the table means anything, and the report says so in capitals. |
| **the deployed DLL's SHA-256** | `shot-batch.sh` | which binary flew. Not a diagnostic — the proof against contamination, see §5. |
| cutoff residual, and the computer's own predicted miss | `CAPTURE cutoff: residual R m/s, own prediction P km off` | splits the shot at the burn. A large residual is an ascent problem and not the thing under test; a clean cutoff with a bad impact puts the whole miss after the engines stopped. |
| trim owed at the split and on release | `trim: ... owed X at the split, Y on release` | whether `BusTrim` converged before the warheads went. Item 0's failure mode is releasing with metres a second still owed, and it is invisible in the miss alone. |
| per tube: degrees off the salvo's line | `warhead away from tube N, D deg off` | what each tube threw along. The tubes are straight, so this reads near zero; the spread comes from where the mouths *sit* — a 0.86 m ring the release prediction averages away (`ACCURACY-PLAN.md` 3db). |
| **per tube: the release probe's own miss** | `release probe: ... N m from the target` | **the aim's error at the instant of release** — everything upstream of the round. Subtract it from the impact and what is left is the round disagreeing with its own predictor, which is the exact quantity item −1a says is the miss. The single most attributive number available. |
| **per tube: `thrown D deg from the platform's track`** | release probe | **the held nose in the velocity frame.** `MIRV-NEXT.md` item 9 asked for it — "costs nothing, and turns the cant from a 141–1,684 m band into one number" — and it is logged; `shot-report.py` medians it per arm. |
| per warhead: `Cci r=(...) v=(...)` at release | `warhead trace: <round> away` | full precision, deliberately: the seed that re-flies that exact release in `tests/KSArmory.Tests` with no game. **This is what makes a losing night still productive** — a loss gets diagnosed offline instead of costing more flights. |
| per warhead: arrival speed and degrees below the horizontal | trace probe | every surface-side term scales as `cot γ` and every velocity-side term with the trajectory's sensitivity. If γ varies shot to shot, that is a large share of the scatter, and it can be conditioned on rather than suffered. |
| per warhead: **the final walk** from the release probe, split down/cross | trace finish | splits the miss into "the arc was already wrong at release" and "the round left the arc afterwards", and says which sensitivity it went out through. The probe miss and the walk are the two halves; they are not summaries of each other. |
| per warhead: `lag N ms = M m`, and world clock against own clock | trace finish | **the clamp's discarded time, in metres.** Item 7e measures the run-to-run scatter as one latched warp decision rather than frame pacing, and puts a second, rarer event here: non-zero `lag` is a clamped frame, worth 11 km on the one shot in 38 that logged it, and such a shot is dropped rather than scored. |
| the surface disagreement at the landing point | `warhead trace: surface at the landing point:` | the height field as the round reads it against as the prediction reads it — the one comparison the headless rig cannot make at all. |
| coast `dt`, `step`, `sim` per sampled frame | trace samples | the frame pacing itself. Separates "this build is slower" from "this build aims worse": a change that costs frame time degrades the shot through the clamp without being wrong about anything, and would otherwise be recorded as a guidance regression. |

`shot-report.py` reduces all of it to one line per arm and, with `--shots`, one line per shot. The
raw logs stay under `shots/` so a surprising line can be read in full.

## 5. Contamination, and why the tree is frozen

Three batches in the last session were contaminated by the tree being edited, or a second batch
being launched, while one ran. `scenario.sh` calls `deploy.sh`, which *builds from the working
tree*, so an edit at any point in the night silently changes what every subsequent shot flies.

Four things close it, and none of them rely on the operator remembering:

1. **Every arm is built once, up front, and stashed.** `shot-batch.sh` checks out each arm's ref,
   builds it, copies the whole deploy payload into `<batch>/arms/<name>/`, and returns the tree to
   where it started. During the night nothing reads the tree at all: each shot is a file copy
   followed by `scenario.sh ... --no-deploy`.
2. **The build is byte-reproducible**, verified: a clean rebuild of the same commit gives the
   identical `KSArmory.dll` down to the SHA-256, and SourceLink stamps the commit itself into
   `AssemblyInformationalVersion`. So the deployed DLL's hash *is* the arm's identity, exactly.
   `shot-batch.sh` re-hashes it after every copy and refuses to launch if it is not the arm it
   meant to fly; `shot-report.py` prints the hashes per arm and shouts if one arm flew more than
   one binary, or if two arms flew the same one.
3. **Two arms that ship identical source are refused before the night starts.** It happens — a
   constant edited in a file the build does not reach, a ref that resolves to the baseline. Without
   the check the night runs to completion and reports a dead heat.

   Compared on `src/KSArmory` between the two refs, **not** on the DLLs, which cannot answer it:
   that same SourceLink stamp makes two arms differ by one string whatever their code says. It was
   measured — two commits with byte-identical `src/` produced DLLs differing in exactly that one
   string and nothing else. The property that makes the hash a perfect identity is the property
   that stops it being a sameness test.
4. **One batch at a time**, held with `flock`. Two batches share one mods folder, one log and one
   game process; they kill each other's runs and produce shots belonging to neither.

The tree must be **clean** when the batch starts — `shot-batch.sh` refuses otherwise. An arm built
from uncommitted work is a binary nobody can rebuild, and the entire night hangs on being able to
say what flew.

Editing the tree *after* the arms are built is harmless and still not advised: `git checkout` of
another branch mid-batch would only confuse the operator, and `--resume` needs the batch directory
rather than the tree.

### Before starting

- `./tools/check-all.sh` passes, and every arm is committed on its own ref.
- The craft is on the pad or in the save the batch will pick up, and `KSARMORY_SCENARIO_CRAFT` /
  `KSARMORY_SCENARIO_SAVE` are exported if the defaults are not right.
- **Windows will not sleep, hibernate, or turn the display off**, and Windows Update is not going
  to reboot. A night that sleeps at shot 12 is a night lost.
- **Nothing is left holding the keyboard.** Once RocketWerkz publish a build newer than the install,
  every launch opens an UPDATE AVAILABLE modal, and a modal clears the held controls of the vehicle
  being flown — seat 1, which then keeps full throttle through its first staging and breaks up, and
  the shot times out 40 minutes later. `KittenSpaceAgency.log` says `a new version of KSA is
  available` when it applies. `ScenarioRunner` closes KSA's popups while it runs, and the smoke's
  output says `closed KSA's UpdateAvailablePopup` when it did.
- One shot flown by hand end to end, to confirm the aim point produces a verdict rather than a
  timeout. Fifty timeouts is the same information as one.
- **Check the ground under the aim point**, on that one hand-flown shot:
  `./tools/shot-report.py <dir> --terrain`. A target whose downrange slope approaches the arrival
  gradient makes the night unreadable, and it does not announce itself — see below.

### The target has to be flat, and it is not obvious when it is not

A round arriving at `g` below the horizontal covers `cot(g)` of ground for every unit it descends,
so ground falling away downrange at `tan(a)` moves the impact by `1/(tan g - tan a)` per unit of
trajectory error. Flat ground gives `cot(g)` — 8.0 at the 7.1° this scenario arrives at. As `tan(a)`
approaches `tan(g)` the trajectory and the ground become parallel and the impact point diverges.

This is not a bias that averages out over a night. It is a **degenerate intersection**: a few tens of
metres of trajectory height decides between stopping on the near side and running kilometres down the
far side, so the miss distribution goes bimodal and the two modes are kilometres apart. It reads
exactly like guidance scatter, and an arm's apparent win can be nothing more than which side of a
hill its aim bias happens to fall on — which is what 26.485S 68.148W cost a whole night to learn
(`docs/MIRV-NEXT.md` item 7g).

`shot-report.py` measures it from the warhead traces the night already writes: every landing line
carries the impact's coordinates and the ground height under it, so the relief is recoverable from a
night flown for something else. It prints one line in the ordinary report, beside the pick-up, and
flags past **2x** flat ground:

```
== terrain at -26.483,-68.142: downrange slope +6.17% against a 5.8 deg arrival,
   2.6x flat ground -- ** ILL-CONDITIONED -- the ground is shaping this **
```

Two things it will not do. It says nothing when the group is tighter than 100 m, because a tight
group is good news and no evidence about the ground. And it measures the footprint *this night*
landed on, not the target: a night whose impacts fall inside a few hundred metres can flag on a local
feature where the regional slope is flat.

**The fix is the arrival angle, not the aim loop.** Nothing in a correction loop can condition a
degenerate intersection — at 15° the arrival gradient is 0.268 and the same terrain is well behaved
again. `docs/ARRIVAL-ANGLE.md` is the argument; `IcbmConfig.MinArrivalAngleDeg` is the control.

## 6. The stopping rule

Two looks, both mechanical, both in `tools/shot-report.py`.

### The instrument check — after the FIRST shot, fatal

`shot-report.py --instrument`, run by `shot-batch.sh` once the first shot is in. It counts warhead
trace landings against releases straight off the logs, and **stops the night** below 75%.

It is separate from the gate below, and earlier, because it answers a different question: the gate
asks which arm to stop flying, and this asks whether anything is being recorded at all. A night
whose instrument is dark still flies, still passes, and still produces a full set of miss
distances — so nothing downstream looks wrong until the morning.

**The arm that lands last is the one that goes missing**, which on a paired night removes one arm
entirely rather than thinning both. That is not hypothetical: the walk night of 2026-09-08 traced
8 releases and 4 landings in every one of its fourteen shots, all four baseline, and spent three
and a half hours confirming what shot one already showed. `ACCURACY-PLAN.md` 3ch.

The floor is 75% rather than 100% because a warhead can genuinely be reaped or shot down, and one
real outcome must not stop a night.

### The gate — mid-batch, removal only

Runs after every fourth shot. It can only take an arm *out*; it never calls a win, because a win
is a question the whole batch has to be in hand to answer and stopping early on one biases it
upward.

**Shots freed by a removal are appended to the arms that are left**, a block at a time, so the
interleaving survives it and the night stays the length it was budgeted for. Dropping one cell of
a 2×2 after three blocks frees nine shots and takes the other three from 12 to **15** each — 400 m
of resolution to about 360 — for no extra wall clock. The budget is a night, not a count, and a
batch that finishes two hours early has thrown the difference away.

An arm is dropped when any of these is true:

| | |
| --- | --- |
| **Broken** | two or more of its shots produced no verdict, or arrived with fewer warheads than they released. That is a failure, not a miss distance, and a rank test on miss distance must not absorb it. |
| **Wild** | two or more shots at or beyond **4 km**, or beyond **twice the same night's baseline median** once the baseline has flown twice, whichever is further out. 4 km is the widest baseline ever recorded on the 26.5S,64.0W shot, over 26 shots — but it is a fact about that target, and on a geometry where the control itself lands past it an absolute floor drops the arms that *match* the control. The baseline is never a candidate for dropping, so that asymmetry keeps the wrong one. |
| **Catastrophic** | from 4 shots each: the arm's median is **3× the baseline's or worse**, *and* its best shot is worse than the baseline's median. The flown losses ran 4×, 11× and 29×; none of them needed twelve shots to see. |
| **Settled loss** | from 6 shots each: rank test **p < 0.0294** with the arm's median above the baseline's. |

### The verdict — morning, per arm and per main effect

| verdict | condition | what to do |
| --- | --- | --- |
| **WIN** | p < 0.0294 and the Hodges–Lehmann ratio below 1 | commit it as a `fix`, quoting n, the ratio and the interval |
| **LOSS** | p < 0.0294 and the ratio above 1 | revert, and write the mechanism into `MIRV-NEXT.md` |
| **UNRESOLVED, ruled out** | not significant, and the interval's lower bound is above **0.75** | the night ruled out anything better than ~200 m. Do not re-fly it. Record the interval — that is the finding. |
| **UNRESOLVED, open** | not significant, and the interval still admits a ratio below **0.6** | worth another night, and only if nothing better is queued. Expect it to need 25 an arm. |
| **TOO FEW** | fewer than 3 usable shots | say nothing about it at all |

The report prints `UNRESOLVED` for both of those rows; which one it is comes off the interval
beside it.

**Never report an unresolved arm as "no difference".** Report the interval. Half the wasted
argument in the last session was about arms whose measurement admitted everything from a large win
to a large loss.

**And never compare an arm to a number from another night.** If the baseline arm's median has moved
against the last batch's, that is drift, and it invalidates cross-night comparison rather than
telling you something about the arm.

## 7. What fifty shots cannot do

Worth being blunt about, because the last session was blunt about it too late.

- **It cannot settle a change worth 200 m.** Not with more patience — with more nights, four or
  five of them, which is not what any of these changes are worth.
- **It cannot settle an interaction worth less than a kilometre.** The factorial can *see* a large
  interaction; it cannot measure a modest one.
- **It cannot rank two arms against each other.** Every comparison here is an arm against the
  baseline. Comparing two non-baseline arms directly costs the same shots again and spends α on a
  question nobody asked.
- **It cannot tell a guidance regression from a frame-rate regression** — but the captured coast
  step can, which is why it is captured. Not the median `dt`: the 1x entry supplies ~90% of the
  samples, so that number reports the entry and hides the coast entirely (item 7e).
- **It cannot prove a headless result.** `MIRV-NEXT.md` item −1 is seven headless improvements and
  seven flights that refused them. The rig prices a term; the flight says whether that term was
  the one that mattered. A batch that comes back UNRESOLVED has not confirmed the rig.

## 8. Running it

**Prefer `--paired` where the change is a setting.** Section 0 has the argument; the commands are
the same shape as below with `--paired '<spec>'` in place of `--arms`, and the report read with
`--paired`.



```bash
# once, before the night: build the arms, print the order, fly nothing
./tools/shot-batch.sh --aim 26.5S,64.0W --blocks 12 --plan-only \
    --arms base=dev,grav=arm/subgravity,reopen=arm/postboost,grav+reopen=arm/both

# the night itself -- 48 shots, about 6.5 hours
./tools/shot-batch.sh --aim 26.5S,64.0W --blocks 12 --out ~/shots/2026-08-23 \
    --arms base=dev,grav=arm/subgravity,reopen=arm/postboost,grav+reopen=arm/both

# it was interrupted
./tools/shot-batch.sh --resume ~/shots/2026-08-23

# the morning
./tools/shot-report.py ~/shots/2026-08-23
./tools/shot-report.py ~/shots/2026-08-23 --main grav
./tools/shot-report.py ~/shots/2026-08-23 --main reopen
./tools/shot-report.py ~/shots/2026-08-23 --shots     # every shot, for the surprising one
```

What lands in `~/shots/2026-08-23`:

```
batch.tsv          when, where, which seed, which base commit, which KSA build
arms.tsv           arm -> ref, commit SHA, DLL SHA-256
plan.tsv           the run order, as flown
shots.tsv          one row per shot: n, block, arm, verdict, DLL hash, seconds, start time
arms/<name>/       the built payload each arm was deployed from
shots/NNN-<arm>.out   scenario.sh's stdout -- the SCENARIO lines and the verdict
shots/NNN-<arm>.log   the whole mod log, copied out before the next shot truncated it
```

Keep the whole directory. It is small next to what it cost, the release states in it re-fly
headlessly, and it is the only thing that makes the *next* protocol argument settleable.
