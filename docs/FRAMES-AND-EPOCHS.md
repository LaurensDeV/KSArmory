# Frames, epochs and the ecliptic carrier

**Near Earth, every position and velocity carries ~29.8 km/s of ecliptic motion.** That is ~600 m
per frame at 60 fps. Any two quantities differenced across even a fraction of a step leak a piece
of it, and the result looks like a completely different bug each time — a jitter, a constant
offset, a guidance error, a drift. One cause, four disguises.

The arithmetic is always the same:

| discrepancy | error at 29.8 km/s |
| --- | --- |
| 0.1 ms | 3 m |
| 1 ms | 30 m |
| one 20 ms frame | 596 m |
| one 20 ms frame, at 10× warp | 6 km |

**A cause is identified when its magnitude divides out to a number of milliseconds that matches
something real in the frame.** A magnitude on its own identifies nothing: the division against the
carrier speed is the diagnosis.

## The epoch contract

This is what KSA does, read from the decompiled source rather than inferred:

- `Universe.ApplyVehicleSolvers` sets `_lastSimStep = _nextSimStep` and then calls
  `CurrentSystem.UpdatePerFrameData()` back to back (`Universe.cs:1699-1701`), which is where every
  `Vehicle._positionEcl` / `_velocityEcl` is written (`Vehicle.cs:2346-2352`).
- That runs inside `PrepareFrame` (`Program.cs:1985-1986`), ~80 lines before `OnDrawUiViewports`
  (`Program.cs:2068`), which is the mod's GUI hook.

Three consequences, all load-bearing:

1. **Every vehicle shares one epoch**, the *end* of the step just applied. A sample means "where
   this will be at the end of this step", not "where it is now".
2. **`GetLastSimStep().DeltaTime` is the interval that ended at that epoch** — the applied step,
   not a measurement of one. It cannot be a phase out from the world.
3. **KSA's frame order is** reset gizmos → `OnDrawUiViewports` (mod GUI hook) → render → postfix
   on `OnFrame` (mod frame hook). **The frame hook lands after the drawing it feeds.**

That third point is also a constraint on any *loader* this mod runs under, not just on where the
simulation is called from — see "Porting to a different loader" in `docs/KSA-MODDING-NOTES.md`.

## Rules that follow

**Simulate in the same pass that draws.** The simulation runs in `OnAfterGui`, immediately before
`Visuals.Draw`. Run from the frame hook instead, every draw uses an offset produced one frame
earlier against an anchor sampled now: 0.999 steps of ecliptic motion along the direction of
travel and 0.4 m across it, over 221 samples. Compensating at draw time cannot work — the drag is
one step of platform motion, so any correction carries a `dt` that changes and comes back as
jitter.

**The drawn offset is `PositionEcl - platformEcl`, measured after the step, no extrapolation.**
Both terms then advance in lockstep and the difference is the round's own flight. The two other
arrangements — differencing before the step, or extrapolating the platform sample forward — both
leak `v·dstep`: 30 m per ms of frame wobble, 507 m when the simulation speed changes. They agree
with each other to 0.6 m, because they are one error rather than two alternatives. See
`OffsetPhaseTests`.

**Back-date the target sample to the round's epoch before aiming at it.** The sample is
end-of-step; the round's pre-step position is start-of-step. Extrapolating the sample *forward*
from an already-forward value leaves every line of sight carrying `+V_target_ecl·dt`, and
proportional navigation then flies a clean intercept on a ghost 450–680 m away: correct guidance
onto the wrong point. The detonation instant must move with it, or the blast sweep breaks by `V·dt`
the other way. They are one change.

**Consume the step; never peek at it twice.** `GetLastSimStep()` answers "the last step", not "a
step since you last asked". `KsaWorld.ConsumeSimStep` deduplicates on the step's own `NextTime`.

**Gate on the applied step, never on `IsPaused`.** `Universe.IsPaused()` is
`simulationSpeed == 0.0` — a statement about the *setting*, not about whether the world moved.
On the frame the speed drops to zero the engine still applies one real step: the platform sample
advances, and a mod that skips on the flag leaves the round behind by a full step. Because the
offset is a difference of integrated positions, that step stays in **permanently**, and every
pause adds another. The symptom is a round that jumps further from its platform on every pause.

**Fire control runs after the round update.** A round integrated in its own launch frame is
differenced against a platform sample that has not moved yet, so one frame of ecliptic motion is
baked into `TravelSinceLaunch` for the rest of its life — 658.78 m of travel at an age of 0.04 s
on a round doing 124 m/s.

**A `Sim/` entry point takes both frame-carrying terms and differences them itself. It never
accepts a difference computed in `Ksa/`.** Every rule above is a subtraction that has to happen at
the right place and the right instant, and a signature taking `relativeVelocity` moves exactly
that subtraction to a call site no test can reach. A regression test written against such a solver
asserts that the *solver* is sensitive to the common term — which it always is — and so passes
unchanged while the caller is the thing that is wrong. `BallisticLead.TrySolve` is the shape to
avoid.

`Interceptor.Update` is the shape to copy: it takes `platformEcl` and computes the offset itself.
Test such a function for **invariance** — add the same arbitrary velocity to both inputs and
assert the answer does not move — with a sensitivity assertion beside it. One proves the common
term is removed, the other proves the relative term still matters; neither alone is worth much.

## Diagnosing

**Measure vectors, not magnitudes.** Comparing two *separations* mixes the error with the closing
geometry: one constant displacement reads as anything from −0.45 to +0.99 steps depending on where
the target is. Since Ego is a pure translation of Ecl, the round→target vector is identical in both
frames, so differencing them isolates the drawing error with the geometry removed — which is what
turns a wandering number into `0.999 steps along the motion, 0.4 m across`.

**Check which renderer is being judged.** Rounds are drawn twice: gizmo tracers from
`AnchorEgo + OffsetFromPlatform`, and missile bodies as subparts through the launcher's part
frame. They can disagree, and by a lot — the gizmo path correct to 0.000 m while the bodies are
650 m out. If a symptom and a measurement disagree, first ask whether they are even describing the
same object.

**Constant vs accumulating tells you where it lives.** A mismatched epoch gives a *fixed* offset.
Only re-applying something into an integrated quantity compounds. "It gets worse every time" is a
much stronger clue than the magnitude.

**`Interceptor.MissDistance` is not a miss distance.** It is a threshold crossing whose look-ahead
horizon is the integration sub-step (`Vec.TimeOfClosestApproach(r, v, h)`), so it is bounded by
the fuse radius whatever the round actually does, and it converges *upward* toward that radius as
the step shrinks. It reads 10–15 m while rounds miss by 450–680 m. `FuseRadius` sits below
`LethalRadius`, so every trigger is lethal and the log says "destroyed" regardless. **The honest
number is the `tgt` trace in `SyncRoundBodies`**, which is the one same-instant range in the
codebase.

## Tests

A test that never varies the step cannot see any of this: at a constant `dt` the right and wrong
phases are indistinguishable, so a suite can pass against a broken implementation indefinitely and
say nothing.

- Vary the step the way changing simulation speed does.
- Advance the platform sample *before* the update that uses it.
- Write target samples as end-of-step values (`position + velocity * dt`).
- **Check every regression test fails against the old code.** A test kept without that check is
  not evidence of anything.

## Handing a position to something that draws for itself

The particle emitters take a world position and place themselves, so they draw without going
through `DrawAnchor` — and the trap above then arrives from a new direction.

A warhead's burst is at `round.PositionEcl` — the analytic position the simulation integrates. The
round and its target are *drawn* against the platform's **physics** origin, which is not the same
place: `KsaWorld.TryVehicleEgo` says outright that deriving a draw position from `GetPositionEcl`
"visibly misses the craft". Placing the burst at the analytic position therefore puts the
explosion somewhere the engagement did not visibly happen.

Convert the *drawn* position back instead:

```csharp
KsaWorld.TryVehicleEgo(platform, out double3 platformEgo);          // where it is drawn
KsaWorld.TryEgoToEcl(platformEgo + round.OffsetFromPlatform, out double3 burst);
```

`WeaponSystem.DrawnBurstEcl` does this and logs the correction when it exceeds a metre. The rule
generalises: **anything handed to the engine to place must be derived from where things are drawn,
not from where the simulation says they are** — the two frames agree on directions and differ on
positions, which is the same asymmetry `TryCursorRayEcl` exists for.
