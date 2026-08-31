# What the player sets

**A plan, not a record. Nothing here is built.**

The ballistic computer has **24 settings on `IcbmConfig`** and about eighteen of them are research
instruments that reached the panel because that is where a setting has to go to be reachable — see
CLAUDE.md's rule that a setting nobody can reach is not a setting. That rule is right and it has had
an unintended result: the panel now offers a player a page of numbers whose correct values are
properties of their trajectory, not of their preference.

The shipped mod should ask for almost nothing and **say what it expects to achieve**: whether this
rocket can reach that place, how much propellant that leaves, and how close it will land.

## 1. Why this is now possible and was not last week

Showing a predicted miss is only honest if the prediction matches what lands. That was established
on 2026-08-31 over 96 flights, four ways — `docs/ACCURACY-PLAN.md` 3n and 3o:

| arm | what the loop accepted | what landed |
| --- | --- | --- |
| base | 108 m | 110 m |
| h6 | 40 m | 40 m |
| h3b | 27 m | 30 m |
| h1b | 18 m | 20 m |

Within about a tenth, and `WarheadTrace` puts each warhead within **4 m** of its own release
prediction. The miss is also **common mode** — the within-group spread is 0-5 m — so one number
describes the whole salvo rather than hiding a scatter.

And the floor has a closed form: `axes x cycle x holding cost`. Every term is computable before
launch, so this is a **pre-flight** figure and not only a readout during one.

## 2. What the mod derives

Three of these are the levers that mattered most this week, and every one of them was a number
somebody typed.

| today | becomes |
| --- | --- |
| `HoldingCostMetresPerSecond` | **measured**, from two `ImpactPredictor` calls — a release now against one a second later. It spans 0.82 m/s at 500 km to 21.79 at 12,900, so no constant is right; the derived value won 12 of 12 in flight |
| `MinArrivalAngleDeg` | **searched**, the way `BurnWindow` already searches departure time. The optimum is interior — near 26 degrees at 2,000 km — so it is found, not guessed |
| `Loft` | **retired from the aim of it.** `docs/ARRIVAL-ANGLE.md` already shows it inverts the arrival from orbit; with the angle searched it has no remaining job |
| `MaxAccelerationGee` | **already nearly derived** — the mod reads the airframe's own limit and takes the smaller. Drop the asking half |
| `TurnStartMetres`, `TurnEndMetres`, `MaxAngleOfAttackDeg`, `HandoverPressurePa` | **internal.** The ascent profile is engineering; a player has no view on the dynamic pressure at which guidance takes over |
| `DeployAltitudeMetres`, `ReleaseBeforeArrivalSeconds`, `TrimBudgetMetresPerSecond` | **internal.** Sequencing |
| `TrimCeilingFromBudget`, `AimWithinTrimBudget`, `KeepOutCoversTheClearance`, `RepointBetweenReleases`, `TrimBeforeRelease`, `CorrectAim` | **internal.** Each is a right answer or a wrong one, not a taste. They stay settable from a shot spec, which is what `Sim/ShotArms.cs` is for |

**They do not disappear; they stop being questions.** A field an arm can still set is still testable
by `tools/shot-batch.sh --paired`, which is the whole reason the research surface exists. What
changes is that the panel stops presenting them as choices.

## 3. What the player sets

| control | what it is |
| --- | --- |
| **Armed** | the master arm. Unchanged |
| **Precision — Range** | the one trade, below |
| **Release automatically** | whether the bus deploys on its own or waits to be told |
| **Stage automatically** | the same question for the boost phase |
| Designate by clicking, Mark the target, Draw the trajectory | tools and drawing, not tuning |

Everything else goes.

### The single trade, and why it is one axis

Every lever measured this week trades precision against **reach and time**, and they turn out to be
the same axis:

* a steeper arrival is more accurate and costs delta-v and range — 0.44x at 25.9 degrees against
  17.7, and rung reach falls from 2,736 km at 15 degrees to 418 km at 60
* more correction passes are more accurate and cost flight time, at the derived holding cost per
  second

So one control sets the arrival-angle floor the search is bounded by, and biases the derived holding
cost. **Toward precision** the shot arrives steeper and holds longer; **toward range** it flattens
and releases sooner. Nothing else needs to move, and the labels are honest because both ends cost
exactly what they say.

## 4. What the mod shows

Answered per craft, before launch, against the designated point:

| line | where it comes from |
| --- | --- |
| **Reachable / short by N m/s / no trajectory / too shallow** | `IcbmReach`, which already returns exactly this and is not surfaced as an answer |
| **delta-v: X available, Y needed** | `BoosterPerformance` against `BallisticArc` |
| **expected miss: N m** | the floor's closed form at the searched geometry |
| **flight time, arrival angle** | the held arc |

Three of the four are computed today and simply not presented. The fourth is new and is the one that
needs the guard in §5.

## 5. The honest-number rule

**A predicted miss may only be shown where the prediction has been checked against what lands.**
Tonight's agreement is at **2,000 km and nowhere else.** Showing a CEP for an intercontinental shot
on the strength of a 2,000 km validation is precisely the error that put 26.0 into the code: one good
measurement generalised without asking.

So the display degrades rather than guesses:

* **validated geometry** — a number
* **outside it** — the reach verdict and the propellant, and no miss figure
* **either way** — never a number the mod has not earned

The validation set is a matrix of range against arrival angle, one paired night a cell, and it is the
same rig `docs/SHOT-PROTOCOL.md` already describes. Until a cell is flown, that cell shows no number.

## 6. Order

1. **Derive the holding cost.** Biggest measured lever, already proven headlessly, and removes the
   setting most obviously not a player's business.
2. **Search the arrival angle** under the one control's bound.
3. **Surface reach and delta-v**, which needs no new maths.
4. **Fly the validation matrix**, then show the miss where it is earned.
5. **Retire the settings** from the panel, keeping them settable from a shot spec.

Steps 1 and 2 are also the two biggest remaining accuracy items, so this is not a detour from
`docs/ACCURACY-PLAN.md` — it is the same work with the panel as the deliverable rather than a night
of shots.
