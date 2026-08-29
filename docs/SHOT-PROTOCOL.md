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

Two-sided rank test at 5%, 80% power, simulated on the fitted distribution:

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

**The harness now asks for a step rather than a speed**, which removes the cause rather than only
reporting it: `BallisticScenario.CoastStepSeconds` is 66 ms and the coast speed is derived from the
machine's frame time, so a 21 ms machine runs 3.1x and a 30 ms machine 2.2x and both integrate the
coast identically. A slow machine pays in wall clock instead of in accuracy. The screen stays,
because the frame time still says what a night cost and because a batch can still be flown on a
build that predates this.

It marks the *session*, not the shot: within a night the rank correlation between a shot's frame
time and its miss is nil. Whether it causes the miss or is a symptom of whatever else the machine is
doing is not known, and it does not need to be to be useful as a screen. `MIRV-NEXT` **8ac** is the
record, including the control shot that ruled out the build.

## 1. The statistic

**Endpoint: the group's `mean` miss, on a log scale.** Not `worst`, which is the pass/fail bar and
is a maximum over six warheads — the noisiest of the four numbers `ShotGroup.Judge` prints. Not
`spread`, which is a different mechanism (the tube cant against the attitude the burn left) and is
analysed *beside* the mean, never mixed into it. `spread` gets its own table in the report and its
own verdict.

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
`shot-report.py`'s "what ended the post-boost correction" table is where the modes are visible; read
it before the ratio, and if the two arms differ there, the ratio is answering a different question
from the one you asked.

**This is not licence to change endpoint after seeing the data.** The mechanism has to be named in
advance, which for that night it was — `docs/MIRV-NEXT.md` item 8s set the open question as whether
more budget makes a *stopped* shot converge. What the night showed is that the pre-registered
*statistic* was wrong for the pre-registered *question*, and those are separable.

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
| per tube: degrees off the salvo's line | `warhead away from tube N, D deg off` | the cant, per round — the only per-warhead term that is meant to produce *spread* rather than bias. |
| **per tube: the release probe's own miss** | `release probe: ... N km from the target` | **the aim's error at the instant of release** — everything upstream of the round. Subtract it from the impact and what is left is the round disagreeing with its own predictor, which is the exact quantity item −1a says is the miss. The single most attributive number available. |
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
