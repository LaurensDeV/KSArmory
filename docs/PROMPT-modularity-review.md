# Prompt: structural modularity review

**Executed. Kept for the record, and deliberately not renamed**: it quotes what was observed
before the work, so rewriting `DefenceBattery` to `WeaponSystem` in it would make it describe a
state that never existed. `docs/BATTERY-SPLIT.md` is what came out of it.

Paste this into a fresh session. It is deliberately a *review* brief, not an instruction to
refactor: the point is to find out what the coupling costs before anything is moved.

---

Read `CLAUDE.md` and `docs/MODULARITY.md` first.

**`docs/MODULARITY.md` does not cover this.** That file is about what the data model can
*express* — different rounds, launchers, mounts, torpedoes, RPGs. This is about how the code is
*structured*: what depends on what, and what a new weapon system has to touch. The two are
independent, and the mod currently scores much better on the first than the second.

## What prompted it

Three observations from play, in the user's words:

1. `BatteryRoster` is mentioned in a lot of places.
2. **Not every weapons system is a battery.**
3. The UI for every weapon is written against the battery, when it should depend on what the
   weapon system actually *is*.

The third is the sharpest. A *battery* is an air-defence fire unit: search radar, launchers,
fire control, all one installation. That was the right word when the Pantsir was the only thing
this mod had. It is now also the word for a LAU-7 rail bolted to the side of a rocket and for a
CIWS on a stack node, and neither is a battery in any sense a reader would recognise.

## What is already measured

Do not re-derive these; confirm and extend them.

- `Ksa/DefenceBattery.cs` is **1650 lines with 52 public members**. It owns platform resolution
  and pinning, part discovery for five subparts, drive status, the turret and optic drives, radar
  spin, the missile magazine, the gun channel, round-body placement, fire control and its gates,
  warhead effects, and the event log.
- **19 files name `DefenceBattery`.** Almost every KSA-facing class takes one: `MotorSound`,
  `MotorPlume`, `MuzzleFlash`, `TracerTrail`, `GunSound`, `Visuals`, `Sight`, `Designator`,
  `Diagnostics`, `ChaseCamera`, `ScenarioRunner`, `Ui`. Most need a small slice of it.
- `Ksa/Ui/UiSystem.cs` branches on `_profile.TubeCount > 0` and `_profile.HasCannon` in four
  places. That is a type test standing in for polymorphism.
- `MotorPlume`, `MuzzleFlash` and `TracerTrail` are near-identical: acquire pooled emitters,
  rewrite the origin each frame, release. The `ParticleEmitter.Kill()` contract had to be fixed
  in all three separately, which is the cost of that duplication made concrete.
- `Sim/Config.cs`, `Sim/ThreatModel.cs` and `Sim/Interceptor.cs` name `DefenceBattery` in
  comments. The boundary is enforced for code by the test project and leaks in prose.

## What to produce

A written review, not a refactor. For each finding:

- **The evidence.** File, line, count. This repository's own history says a plausible mechanism
  is not a diagnosis; the same applies to a design smell.
- **What it costs today**, concretely: what a third weapon system has to touch, what a bug in one
  place means for the other copies, what a reader has to hold in their head.
- **What it would cost to fix**, honestly, including the risk that a split makes things worse.
- **Whether a test could hold the seam** afterwards. `Sim/` is testable and `Ksa/` largely is
  not, so a split that moves logic into `Sim/` buys verification and one that only rearranges
  `Ksa/` buys nothing but tidiness.

Rank by *what unblocks the next weapon system*, not by how ugly the code looks. Some of this may
be worth leaving exactly as it is; say so where that is the answer, and why.

## Questions worth answering explicitly

- **Is `DefenceBattery` one thing or several?** A candidate reading: a *mount* (parts, drives,
  laying), a *magazine and its rounds*, a *fire-control loop*, and an *installation* that owns a
  policy and a platform. Does that split survive contact with the CIWS, which has no magazine,
  and the rail, which has no drives?
- **What is the right word?** If not "battery", then what covers a fire unit, a rail and a gun
  mount without lying about any of them. The rename is cheap; picking the concept is not.
- **What should the UI be given?** Today it gets a battery and asks it questions about profile
  fields. What would it take for a weapon system to describe its own panel, and is that worth it
  for three systems, or only past some larger number?
- **Should the effects classes share a base?** They differ only in which emitter, which anchor,
  and when to stop. Weigh a shared pooled-emitter helper against the fact that each one's
  anchoring rule is genuinely different and the shared part is short.
- **Which consumers actually need a battery?** For each of the 19, name the smallest thing that
  would do: a round list, a platform and a muzzle, a policy. An interface per need may beat one
  class passed everywhere.

## Constraints

- `Sim/` must stay free of KSA types. The test project links `Sim/**` wholesale, so this
  enforces itself.
- Do not rename or move anything in this pass. The deliverable is the review.
- If the review argues for changes, it should end with a ranked list in the shape
  `docs/AUDIT-2026-08.md` uses, so items can be taken off it one at a time.
