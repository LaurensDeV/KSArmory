# Frame dependence in the ballistic surface

**A decision belongs in seconds or in physical units.** How long to freeze the steering, how long to
wait, when to give up, when to stop trying — none of those may depend on how fast the host machine
draws. Only a quantity genuinely bounded by the frame may scale with the step: *the velocity one
more frame of thrust would add* is `accel x step x throttle`, and a threshold below it is one
nothing can reach. `IcbmProgram.HoldDirectionFrames` is what happens when the two are confused — it
was introduced to bound a residual, which is frame-bounded, and expresses a freeze duration, which
is not, so it lasts `Frames x step` seconds and grew the off-plane residual 5.9x over a 4x step
(`docs/ACCURACY-PLAN.md` 3fk).

**The test that separates them, in order:**

1. Write the quantity's effective value at a 17 ms, a 25 ms and a 40 ms step. If it moves and it
   expresses a policy, it is a bug.
2. Ask what the number is *made of*. A count of frames is a duration; a count of passes whose
   passes are seconds-gated is not; a per-frame filter weight is a time constant in disguise.
3. Ask what geometry makes the consequence visible. `HoldDirectionFrames` is **identically zero**
   equator to equator, which is where every cutoff fixture flies — the blindness was one line of
   fixture geometry and it hid a real term for months.

Counts: **9 DECISION**, **13 FRAME-BOUNDED**, **27 CONVERGENCE**. One DECISION has a
seconds-valued sibling already built (`IcbmConfig.HoldDirectionSeconds`, off); the rest have none.

## DECISION — a policy whose effective value moves with the step

| Constant | Made of | 17 ms / 25 ms / 40 ms | Consequence | Seconds sibling |
| --- | --- | --- | --- | --- |
| `IcbmProgram.HoldDirectionFrames` = 20 | `Frames x step` seconds of frozen thrust line | measured **0.167 s / — / 0.400 s**; nominal `20 x step` is 0.34/0.50/0.80, shorter in flight because `HoldDirectionBelow` caps the entry and the throttle ramp slows the burn-off | everything the required velocity does in the freeze is left square to a line nothing can still thrust along. 0–2% of the residual along the track, **59–93% at 26° off the plane**; headless the 40 ms residual goes 0.3299 → 0.1468 m/s, which at 4,700 m per m/s is ~0.9 km | **`IcbmConfig.HoldDirectionSeconds`**, built, **off**, unflown |
| `WarpPolicy.OverridesBeforeYielding` = 2 | frames in which the speed reads above the request | 51 / 75 / 120 ms of tolerated contest | the condition tested is a *state*, not an event, so it counts frames. A yield is cleared only by an empty sky, so one spurious yield stands the policy down for the whole flight — 9 of 12 shots yielded, warheads landing on 117–267 ms frames against 18–33 for the 3 that held. An auto-warp ramp lasting 60 ms stands the mod down at 120 fps and not at 25 | none |
| `BusTrim.AccelerationGain` = 0.25 | a 4-frame EWMA time constant on the measured thruster acceleration | 0.068 / 0.100 / 0.160 s of lag | after every direction change `StopBand`, the budget charge and `Alive()` run on a stale acceleration for that long. Defensible — the noise rejected is a velocity difference over one step, whose size goes as `1/step` — but the lag is a duration and it grows | none |
| `SalvoProbe.MaxAgeSeconds` = 0.5 | a seconds bound over a span counted in frames | salvo span `6 x step` = 0.10 / 0.15 / 0.24 s | a salvo lets its six go one per frame. Past ~12 fps the last warhead's borrowed probe is refused as `TooOld` and it leaves unkicked, which lands 1.7–2.0 m out. The bound is right; what it bounds is frame-counted | none needed — the span is the fault |
| `ReleaseFocus.Columns.ReusableWithinSeconds` = 2.0 | the same, with 4x the headroom | same span | reverts to a fresh column solve rather than to a wrong answer | — |
| `_trimFloor` (`IcbmConfig.ReleaseInsideTheTrimFloor`) | `BusTrim.StopBand(accel, simStep, pulse) / perMetre`, sampled on one frame per pass | masked today | the step term binds above `0.02 / (0.5 x accel)` — **71 ms** on the shipped 0.56 m/s² bus, **13.3 ms** on a 3.0 m/s² one — and is masked entirely while `PulseTrim` is on, where the floor is `3 x accel x pulse` = 1.68 mm/s. Latent: uprate the bus's jets or turn pulsing off and *when to stop correcting and release* becomes frame-rate dependent with nothing saying so | none |
| `WeaponSystems.FruitlessSearchesBeforeRetiring` = 120 | frames of empty search | 2.0 / 3.0 / 4.8 s | a backstop far past any staging rebuild. Nothing observable | none |
| `SmoothedStep.Weight` = 0.15 | a ~6.7-frame EWMA time constant | 0.11 / 0.17 / 0.27 s | cosmetic — the chase transition's ease and nothing else. Defensible for the same reason as `AccelerationGain`: what it rejects is the display's per-frame pacing beat | none |
| `SightCamera`/`ChaseCamera.GiveUpAfterFrames` = 180 | frames of refused pose | 3.1 / 4.5 / 7.2 s | log cadence only; the camera keeps trying either way. Outside the ballistic surface | none |

`OverrunLog.ReportEvery` = 120 is the same shape in a diagnostic and is not counted above.

## FRAME-BOUNDED — the frame is the right unit

| Expression | Why the step belongs in it |
| --- | --- |
| `IcbmProgram.ShouldCutOff`: `_countdown <= 0.5 x _lastStep x throttleAchieved` | half of what one more frame of burning adds. An engine stops on a frame boundary, so this puts the cutoff at the boundary nearest the ideal instant |
| `IcbmProgram.ShouldCutOff`: `oneStep = accel x _lastStep`, floored at 1.0 | the rising-again backstop asks whether the velocity to gain grew by more than one frame's worth. The noise it discriminates against *is* one frame of thrust |
| `IcbmProgram.HoldDirectionThreshold`: `frame = accel x _lastStep x throttle` | the base quantity is correct and stays. Only the `x HoldDirectionFrames` multiplier on it is the bug |
| `IcbmConfig.HoldDirectionSeconds` floored at `frame` | below one frame the direction being held is the difference of two nearly equal vectors — the fault `HoldDirectionBelow` exists for |
| `BusTrim.StopBand(accel, step)` = `max(0.02, 0.5 x accel x step)` | half a frame of jets. A threshold below what a frame adds is one nothing can reach and the loop hunts round it. The 0.02 floor keeps the step out of it below 71 ms on the shipped bus |
| `BusTrim._firingFor >= 2` | a command reaches the engine's worker one frame after it is written, so the first interval after a change is a mixture. Two frames is the pipeline's own length, not a duration |
| `BusTrim._pulsedLast`/`_pulsedBefore`, `_pushDirLast`/`_pushDirBefore` | the same one-frame write latency, as shift registers |
| `WarpPolicy.SettleSteps` = 1 | exactly one step of write latency. Judging a request on the step that predates it reduces on top of a reduction already in flight |
| `WarpPolicy.FramesAwaitingWrite` = 4 | how long a write may go unobserved before it is a refusal. Write latency is a frame-count property of the engine |
| `StepGate.RoundingSeconds` = 1e-9 | KSA rounds its clock to whole nanoseconds. Not a duration |
| `SimClock.MaxStep`, `Interceptor.MaxFaithfulStep`, `IcbmProgram.MaxFaithfulStep`, `BusTrim.MaxFaithfulStep` | bounds *on* the step. The step is the subject, not the unit |
| `PostBoostAim._lastCycleSeconds = Math.Max(step, _elapsed - _cycleStartedAt)` | a one-frame floor under a measured cycle length, so a zero-length cycle cannot price the next one as free |
| KSA's own `nav.Time > LastThrusterPulseTime + 0.15` (`FlightComputer.ComputeRcsControl`) | the engine's pulse gate is **seconds**, so pulse authority is frame-independent — see below |

## CONVERGENCE — iteration and sample counts, unaffected

`BurnoutGuidance.RefinementPasses` 3 · `Lambert.MaxIterations` 48 · `Kepler.MaxIterations` 64 ·
`BallisticArc.ScanSamples` 96 · `BurnWindow.Revolutions` 16, `CoarseSamples` 256, `Candidates` 8,
`NearSamples` 32 · `ArrivalBudget.SteepestConsideredDeg` 80, `ResolutionDeg` 0.5 ·
`ImpactPredictor.TerrainCrossingSteps` 3, `MinRefineSeconds`, `CrossingToleranceMetres`,
`AtmosphericStepSeconds` 0.25 · `IcbmComputer.PredictStepSeconds` 2.0 ·
`ReleaseFocus.VelocityStepMetresPerSecond`, `PositionStepMetres`, `LeastAuthority` ·
`AimAuthority.ProbeMetres` 1000 · `HoldingCost.ProbeSeconds` 106 · `BombSight.MaxSteps` 2048 ·
`BallisticLead.MaxPasses` 32, `StallPasses` 4, `ReachHalvings` 12.

**And the pass counts that span frames belong here too**, because what they count is seconds-gated
rather than frame-gated: `PostBoostAim.MaxCycles` 20 and `PassesWithoutImprovement` 3, and
`AimCorrection.WorseBeforeStopping` 12. During the burn an observation is gated by
`_sincePredict >= IcbmComputer.PredictIntervalSeconds` (0.5 **simulated** seconds), and after cutoff
by `PostBoostAim`'s own settle and flown clocks, all of which accumulate `step` against a seconds
constant. What is left is the interval's quantisation — `ceil(0.5 / step) x step` is 0.51 s at
17 ms, 0.50 at 25 and 0.52 at 40, under 5%.

## What to fly, ranked

### 1. `HoldDirectionSeconds`, off the orbit plane

**Mechanism.** The freeze begins at `Frames x accel x step x throttle` of velocity still to gain and
burns off at `accel x throttle`, so it lasts `Frames x step` seconds. Whatever the required velocity
does in that span is left square to a line nothing can still thrust along.

**But `HoldDirectionBelow` caps it in flight, so there is no night to fly.** The threshold is
`min(5.0, 20 x accel x step x throttle)`, and at the 11% throttle and 113–115 m/s² these flights cut
off on, twenty frames is ~7 m/s and the 5.0 cap takes it — on **93% and 100%** of the rockets of the
two nights on disk. Capped, the threshold is a velocity, and a velocity over `accel x throttle` is a
duration the frame never reaches: the freeze measured **0.399 s at a 23 ms step and 0.395 s at
28 ms**. `HoldDirectionSeconds=0.35` is therefore a flat 12% shorter freeze at either frame rate — a
constant, not a step-independence fix. `docs/ACCURACY-PLAN.md` 3fl, and it withdraws 3fk's request
for this night.

**Which geometry exposes it.** **Off the plane, and nothing else does.** A shot aimed along the
track has no out-of-plane work left to freeze: 0–2% of the residual is square to the thrust line
there and grows exactly linearly with the step. At 26° off it the share is 59–93% and grows 5.9x
over the same 4x. Every cutoff **fixture** is equator to equator, where the term is identically zero;
the flown shots are not — they run 2.9–3.3° off plane and carry a 67–79% cross-track share.

**Standing gap underneath it.** `IcbmFlightRig` never leaves the plane. Until it does, no headless
fixture can see this class of fault at all.

### 2. `WarpPolicy.OverridesBeforeYielding`, off nights already flown

**Mechanism.** `++_overrides` fires on every frame in which the world's speed reads above the
request, not on every competing write, so three frames of contest is 51 ms at 120 fps and 120 ms at
25. A yield is permanent for the flight.

**Read off the nights on disk, and it never fires.** `timewarp not held` appears **0 times in 77
shots** across 2026-09-18-nopulse, 2026-09-19-fallback and 2026-09-20-shortfallback, against 74
`timewarp held at` lines on one of them — so the policy is active and never yields. The 9-of-12
measurement in `WarpPolicy`'s own comment is from some other context. **Latent, not live**: real in
the code, unreachable by the ballistic scenario as flown, and nothing to fly until something makes
it fire.

**Which geometry exposes it.** Not geometry — **the coast-to-window transition**, where KSA's own
auto-warp ramps while the mod is asking for a speed. A shot that never warps never reaches it.

### 3. `BusTrim.AccelerationGain` as a time constant

**Mechanism.** A fixed per-frame EWMA weight is a lag of `step / 0.25` seconds. `_accel` sizes the
stop band, charges the budget and decides whether a direction is struck off, and after every
direction change all three run on a stale reading for that long.

**What a night has to measure.** The frames between a direction change and `_accel` reaching its
plateau, which the trim's own debug line already prints. Then the same count at two frame rates. A
seconds-valued sibling is `gain = clamp(step / TimeConstantSeconds, 0, 1)`; 0.1 s reproduces today's
behaviour at 25 ms.

**Which geometry exposes it.** A bus that changes direction often — a **high separation debt**,
above the 1.5 m/s floor under which 3fd found no stall has ever happened — and a coarse frame. The
47 fps nights against the 63 fps ones are the existing contrast.

### 4. `SalvoProbe.MaxAgeSeconds` over a frame-counted salvo

**Mechanism.** The salvo goes one warhead per frame, so its span is `6 x step`. The bound on a
borrowed probe's age is 0.5 s. They cross at about 12 fps.

**What a night has to measure.** The count of `Source.TooOld` choices per flight against the block's
frame rate. Expect zero; the value is in knowing rather than in fixing.

**Which geometry exposes it.** Only where a warhead's *own* probe fails, which is 3ep's case — a
designation on the sea, or ground under 204 m where the step crossing it reads the ocean. A shot at
ordinary land never borrows at all.

### 5–7. Not worth a night

`_trimFloor` is masked twice over on the shipped bus and becomes real only if the jets are uprated
or `PulseTrim` is turned off — worth a line in `IcbmConfig` rather than a flight.
`FruitlessSearchesBeforeRetiring`, `SmoothedStep.Weight` and the two camera `GiveUpAfterFrames` cost
nothing observable at any frame rate anyone runs.

## Ruled out, with the engine's own source

**The pulse phase's authority is not frame-rate dependent.** `BusTrim` commands a pulse on every
frame of a refine phase, which looks like an actuator whose rate follows the frame rate — and the
obvious reading is that a fast machine nulls faster, which would make `StallSeconds`,
`PulseSecondsPerNull` and `MaxSeconds` all buy different amounts of work on different machines.
`FlightComputer.ComputeRcsControl` gates it on `nav.Time > LastThrusterPulseTime + 0.15`, in
**seconds of universe time**, so the delivered interval is `ceil(0.15 / step) x step`:

| step | delivered interval | authority on the shipped bus |
| --- | --- | --- |
| 17 ms | 0.153 s | 3.66 mm/s per second |
| 25 ms | 0.150 s | 3.73 |
| 40 ms | 0.160 s | 3.50 |
| 100 ms | 0.200 s | 2.80 |
| 300 ms | 0.300 s | 1.87 |

Flat to ±7% across the band a machine actually runs at. It degrades only past ~60 ms, and the trim
registers with `WarpPolicy` for `IcbmProgram.MaxFaithfulStep` — 0.3 s, twice the engine's gate — so
that regime is reachable under a warped coast and costs up to 2x of the phase's authority. That is
the one thing here worth a line in a log rather than a night.

**What that leaves.** The frame-rate-dependent stall rate — a third at 63 fps and two thirds at
47 — is not the pulse cadence and, per 3fk, not `StopBand` and not `Stalled`. The standing candidate
is the off-rails coast: KSA integrates the bus at the frame step while `BusTrim` compares against an
exact Kepler propagation, which is a recession proportional to the step — 0.25 mm/s per second at
28 ms against 0.19 at 21, against 3.7 mm/s per second of authority. `IcbmConfig.RailsDuringCoast` is
the switch and it is unassigned.
