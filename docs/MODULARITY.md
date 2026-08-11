# Modularity: what generalises, what does not, and what it would take

The mod is built around three profile types and a registry so that a second weapon system is *data
plus art*. This is how far that holds, read out of the code rather than out of the design notes.

**Two of the four changes proposed below have landed, 2 is all but one step done, and 4 is still a
plan.** The test work they depend on is done, and the last section records where the coverage now
sits.

**Cite symbols here, never file and line.** A line citation is wrong within a few months, and a
rename deletes the file it names. A symbol survives edits above it, and `grep` finds it.

**Summary: modular for rounds, mostly modular for launchers, not modular for mounts.**

---

## Where it stands

### Different rounds — genuinely there

`Interceptor` never names a munition. Every number arrives as a `MunitionProfile` argument per
`Update`, and there is exactly **one** branch on round type in the whole flight model:

```csharp
SeekerInView = munition.Guidance == GuidanceMode.CommandLink || /* seeker cone check */;
```

`Interceptor.SeekerInView`. Boost, drag, nav constant, fuse, blast, body mesh and fin timing are all
profile fields; bodies and fins resolve by `BodyMarker` / `FinMarker`, so a new round brings its own
meshes with no code change. Profiles are mutable fields read *by reference* every frame, which is
what makes live panel tuning work.

The limit is the *class* of weapon, not the round. `Interceptor` is one concrete type with a
hardwired integrate → guide → fuse loop, so a second class of weapon is a second implementation of
`IProjectile` rather than another profile field — `Slug`, which is what the
2A38M and M61A2 fire. That is the shape to copy: a weapon *kind* is an implementation, and
`ProjectileContractTests` runs the frame and epoch rules against every one of them, so a new kind
inherits the whole trap list.

What still has no shape at all is a weapon with no discrete round: a hitscan, a beam, a
flamethrower. See *What the architecture genuinely cannot express* below.

### Different launchers — modular in count, rigid in articulation

Discovery is `Arsenal.LauncherForPart(part.Id)` (`LauncherPart.Find`); nothing hardcodes an
Id. Tube count is fully derived — `Magazine`, the `stackalloc` in `WeaponSystem.Fire` and
the body sync all size off `profile.TubeCount`, and `WeaponSystem.SampleWorld` re-sizes the
magazine when a *different* profile is recognised. A non-training launcher (`TurretMarker = null`)
is a supported shape.

**The hard assumption is that articulation is a fixed set of named subparts in one fixed kinematic
chain**: traverse about part +X, elevate about +Z at a trunnion offset, radar spinning about +X.
Those axes are `TubeGeometry.TraverseAxis` and `TubeGeometry.ElevationAxis`, composed by
`TubeGeometry.PodPose`, `GunPose` and `RadarPose`, the first two over the shared `ElevatingPose`,
and `LauncherProfile` offers exactly five markers with a pivot and a reference elevation each.
That covers another turret-and-pods system, a gun mount, or a fixed box. It does not cover a
rotating drum, a translating rail, per-tube articulation, or a radar that trains independently of
the turret. Two elevating groups are expressible — the Pantsir's pods and its cannon are two
trunnions — but they share one `Turret.ElevationRad`, so they cannot be laid on different
solutions.

The tubes themselves are not part of that limit. **`LauncherProfile.Tubes` is `Tube[]` — a position
*and* an optional direction** — and `TubeGeometry.TubeAxisPartFrame` falls back to the pod axis
only when `HasOwnDirection` is false. Splayed tubes, a VLS with divergence and an MLRS are all
expressible; what remains rigid is the chain those tubes ride on, not the tubes themselves.

### Different mounts — the weak axis

A **static site already works** — it is a landed vehicle that does not move. **On a rocket** it
works structurally, since a part on a vehicle is a part on a vehicle. What decides whether a mount
behaves is where its sensor looks, and one limit is still pinned:

- **Where the search cone points is the sensor's choice**, not a constant. `SensorProfile.BoresightSource`
  offers `LocalUp`, `PartForward` and `TurretAxis`, resolved by `TubeGeometry.TryBoresightPartFrame`. The
  Pantsir keeps `LocalUp`, because its set sweeps a hemisphere regardless of where the tubes are
  aimed; the LAU-7 rail uses `PartForward`, because a seeker head looks where the rail points, and
  that is what makes a launcher on something that manoeuvres work.
- **One weapon system per craft**, not per launcher part. `WeaponSystems` crews every craft
  carrying a recognised part and each carries its own `SystemConfig`, so a static site and a
  rocket-mounted launcher both run. What is still pinned is `WeaponSystem.LauncherOrdinal`, so
  a craft carrying *two* rails fires one of them and the other is scenery.

---

## Proposed changes, ranked

| # | Change | Size | Mostly lands in | Unlocks |
| --- | --- | --- | --- | --- |
| 1 | ~~`TubeOffsets` becomes `Tube(position, direction)`~~ | **landed** | `Sim/LauncherProfile.cs` | any launcher whose tubes are not parallel |
| 2 | `WeaponSystem` becomes a list, one per launcher part found | medium | `Ksa/WeaponSystems.cs` | static site + vehicle + rocket at once |
| 3 | ~~`BoresightMode` on `SensorProfile`~~ | **landed** | `Sim/SensorProfile.cs` | a launcher on anything that pitches |
| 4 | Articulation as a list of drives rather than three named roles | large | `Sim/TubeGeometry.cs`, `Sim/LauncherProfile.cs` | drums, rails, per-tube motion |

**4 is deliberately last and should not be attempted speculatively.** It is the one whose shape is
least knowable before a second launcher exists that actually needs it.

**1 and 3 are landed**, both cheaply, because the geometry they rewrite had already moved into
`Sim/` and was covered — see the section below. 4 stays last.

**2 has moved most of the way.** `Config` holds no launcher, round or sensor;
`WeaponSystem.Profile`/`.Munition`/`.Sensor` are the system's own, paired by `Arsenal.LoadoutFor`,
and `WeaponSystems` makes the class plural: every craft carrying a recognised part is crewed and
pinned there. Two craft can therefore be two *different* weapon systems, which is what the LAU-7
rail needs and what a shared `Config` makes impossible — with one, every reader outside a system's
own update gets whichever system resolved last.

What remains of 2 is **several launchers on one craft.** `WeaponSystem.LauncherOrdinal` is a
`const 0` and `WeaponSystems` keys on `Vehicle`, so a craft with two Sidewinder rails fires one of
them and the other is scenery. Fire control and the salvo timers are still KSA-facing and still
unreachable from the test project, so that half remains the riskiest of the four despite being the
smallest thing left on the row. `docs/BATTERY-SPLIT.md` items 5 and 6 are the route in.

Change 1 crosses the `tools/model/pantsir.py` → `muzzles.json` → `Arsenal` boundary that
`validate-parts.py` guards, so the generator and the validator move with it. Geometry duplicated
across a boundary drifts, and that validator is the only thing holding these two copies together —
see CLAUDE.md.

One minor item is still open: `Arsenal.MunitionNamed` falls back to `Munitions[0]` on an unknown
name with no warning, so a typo'd key silently flies the wrong round — a 30 mm barrel throwing
45 m/s SAMs. The fallback is pinned by `WeaponSystemSelectionTests` rather than merely noted.
`Ui.DrawStatus` names no system: it reads the fitted profile's `DisplayName`.

---

## Test coverage

Where coverage exists it is dense: eight offset/phase tests, each verified to fail against its
predecessor, 22 on the turret drive, 21 on the threat model, plus fuse and
guidance-discrimination suites.

**The coverage boundary is drawn at the file layout, not at the risk.** The test project links
`Sim/**` and references no KSA assembly, so `Ksa/` has zero coverage by construction. That is the
right design, and its failure mode is a body of *pure* logic sitting on the wrong side of it —
disproportionately the logic the changes above rewrite.

That logic is lifted into `Sim/`, the same way `FireGeometry` came out of `LauncherPart`, and the
`Ksa/` side keeps only the property writes. The extraction is worth **117 → 203 tests**.

| Was stranded in `Ksa/` | Now | Tested by |
| --- | --- | --- |
| tube occupancy, `NextFreeTube`, refill | `Sim/Magazine.cs` | `MagazineTests` |
| the seat-then-hide decision | `Sim/Magazine.cs` (`TubeVisual`) | `MagazineTests` |
| tube muzzle / axis / seated maths | `Sim/TubeGeometry.cs` | `TubeGeometryTests` |
| `MuzzleEcl` ring fallback | `Sim/TubeGeometry.cs` | `TubeGeometryTests` |
| pod & radar pose composition | `Sim/TubeGeometry.cs` | `TubeGeometryTests` |
| travel → part-frame chain, fin span | `Sim/TubeGeometry.cs` | `TubeGeometryTests` |
| `ConsumeSimStep` dedup | `Sim/StepGate.cs` | `StepGateTests` |

And the `Sim/` holes: `WeaponSystemSelectionTests` runs the registry with **three** launchers,
rounds and sensors — where "picked the right one" and "picked the only one" finally differ — and
covers profile switching, stale turret limits and the fixed-launcher shape. `MunitionVarietyTests`
flies two genuinely different munitions through one `Interceptor` and asserts they diverge, which
is the modularity claim itself. `MagazineTests` is parameterised over tube count.

**`TubeVisual` deserves a note.** It has no value meaning "hide without seating" — the launch-flash
bug is unrepresentable rather than merely tested against. That is the preferred shape when the
option exists.

### Verified against the old code

Per the method below, every regression test is checked by reintroducing the bug it guards:

| Bug reintroduced | Tests that failed |
| --- | --- |
| occupancy check dropped from `TryTakeTube` | 3 |
| `RequiresSeating` false for spent tubes | 1 |
| pod elevation sign flipped | 3 |
| pod position not rewritten on traverse | 2 |
| anchor rotated along with travel | 2 |
| step dedup removed | 4 |
| launcher lookup returns element zero | 3 |
| fuse radius hardcoded | 1 |
| guidance mode ignored | 2 |

The lookup-returns-element-zero case is the instructive one: **a registry test written against a
single launcher passes against it**, because "picked the right one" and "picked the only one" are
then the same assertion. `OnlyTheTravelIsRotatedIntoThePartFrame` has the same shape — an anchor on
the rotation axis makes rotating it a no-op, so the anchor has to be off-axis for the test to have
any force at all.

### Verified in flight

The extraction touches the live firing path and its tests cover `Sim/`, not the `Ksa/` side left
behind, so it carries a flight record: a full twelve-round salvo on one target, distinct tube
numbers with no double-booking warning, target destroyed, a sim-speed excursion to 0.01x and back,
and zero warnings across 2,246 log lines. That exercises all three extractions — `Magazine` in the
tube numbering, `TubeGeometry` in the seating and pose chain, `StepGate` in the speed change.

### Still not reachable

Extraction has limits, and these remain untestable because they genuinely need a `Vehicle`, a
`Part` or a `Camera`. They are the target of any next round, not oversights:

- fire-control *sequencing* — salvo spacing and the reload timer. The magazine is out; the timing
  is not. The `IsLaid` *decision* is out too, into `FireGate`: it depends on four booleans and a
  settle time, none of them KSA types. What is still in `Ksa/` is the mode ladder above it —
  spin, manual, stow, track — and the ordering of the four transform writes.
- `ResolvePlatform`, and the platform-election order.
- `LauncherPart.Find` and subpart resolution by marker substring.
- the centre-of-mass correction in `TryGetTubeMuzzleEcl`, and `ResolveOriginEcl`'s camera round trip.
- `SyncRoundBodies`' loop over live rounds, and `Radar.Scan`'s vehicle iteration. The maths inside
  both — `TubeGeometry`, `ThreatModel` — is covered; the iteration is not.

---

## Reaching further: torpedoes, RPGs, aircraft, submarines

How far the architecture stretches against a much wider ambition. Read out of the code and out of
the engine's decompiled source, not estimated.

**Summary: the weapon side reaches a long way, the platform side is not this mod's problem at all,
and the two real ceilings are both KSA's.**

### Platforms are not weapons

Aircraft and submarines are **craft the player builds**, not things this mod adds. A weapon system
mounts on any `Vehicle` carrying a registered launcher part and never asks what shape it is —
`BoresightMode` already lets a launcher on something that manoeuvres search forward rather than
along local "up".

So "does it support aeroplanes" has no weapon-side answer, because there is nothing to support.
What would make them interesting is **AI that flies them** and **IFF so they can fight each
other**, and neither is a weapon concern. IFF is cheap now and expensive after ten weapon types
exist; AI pilots are a project in their own right.

### Torpedoes — a profile, not a generalisation

The engine has water: `Celestial.GetOceanReference()` gives a density, and there is an ocean
radius and a splash event. Nothing is blocked there.

The medium is generalised. `Sim/` threads a scalar
**medium** density ratio through both flight models — the maths does not care what the medium is —
`KsaWorld.MediumDensityRatioAt` returns ocean density below the waterline instead of zero, and
`MunitionProfile.NeutralDensityRatio` buys buoyancy, so a round denser than its medium sinks and
one at neutral density holds depth. A torpedo is now an ordinary `MunitionProfile`: a small
`DragK`, a `NeutralDensityRatio` near 840, and it swims.

Everything else it already has: `Slug` is unguided-kinetic, `Interceptor` is guided, both fuse on
proximity or contact, and both obey the frame rules by contract. What is untried rather than
missing is **surface crossing** — the medium ratio is sampled once per frame and passed as a
constant to every sub-step, so a frame is integrated wholly in air or wholly in water and the cost
of a crossing is metres of overshoot rather than divergence.

### RPGs — expressible today

An unguided rocket is `Slug` with a launch speed, or an `Interceptor` with `NavConstant = 0`.
`MunitionVarietyTests` and `ProjectileContractTests` already fly both shapes. No new code.

The gap is what it shoots *at*: see below.

### What the architecture genuinely cannot express

**Nothing upstream of a round can name a coordinate.** `Sim/Aimpoint.cs` covers the half of this
that matters for the *round* — it can be aimed at a craft, a component or a point, and the
designator proves it. What has not moved is the path that produces one: `Track` is a
`required Vehicle`, `Radar.Scan` builds only from loaded vehicles, and the fire-control entry
points refuse without a lock. A howitzer or an MLRS wants a target that was never a craft, and
that is a retype across `Radar`, `Track`, `WeaponSystem` and `Ui` rather than a profile field.

**Continuous-effect weapons have no home.** `IProjectile` is a discrete object with a position, a
flight and a fuse. A laser has no flight time, a flamethrower has no discrete round. Those need a
sibling abstraction, not another `IProjectile`, and the cost is a parallel lifecycle in `Ksa/`
across the reap switch, `Detonate`, round-body placement keyed on tube number, `Magazine.IsOccupied`,
`Visuals.DrawRounds` and `Diagnostics`.

### The two ceilings that are KSA's

- **No damage below destruction.** KSA exposes `DestroyVehicleFromEvent` and nothing else. Armour,
  penetration and component damage — most of what makes an RPG interesting against a tank — are
  not expressible at any level of mod cleverness.
- **No real part modules.** Registering a module type into the engine's update lists needs Harmony
  patching. Modules are faked by scanning part Ids, which works but means every new module type
  needs mod-side wiring rather than being declarative.

### Status

| | | |
| --- | --- | --- |
| 1 | **IFF and teams** — `Sim/Iff.cs` | **done** |
| 2 | **Target abstraction** — `Sim/Aimpoint.cs`, vehicle / part / point | **done** |
| 3 | **Medium generalisation** — density ratio covers vacuum, air and water, plus buoyancy | **done** |
| 4 | **Magazine decoupled from tubes** — `LauncherProfile.MagazineDepth` | **done** |
| 5 | **Per-craft weapon manager** — `Ksa/WeaponSystems.cs` | **done, for one launcher per craft** |

"Done" here means shipped and covered, with every regression check verified against the bug it
guards. It does not mean flown: `CHECKLIST.md` is where in-game confirmation is recorded, and a
change that compiles and passes the suite can still be wrong in flight.

What each unblocked, concretely:

- A torpedo is now an ordinary `MunitionProfile`: a small `DragK`, a `NeutralDensityRatio` near
  840, and it swims. No new flight model.
- An RPG or a bomb can name a coordinate or a component rather than a whole craft — a *round* can,
  at least; see above for what still cannot hand it one.
- A gun is a `LauncherProfile` with no tubes at all, firing through `GunMunition` and `GunMuzzles`.
  The Mk 15 Phalanx is that shape.
- A weapon system can be told whose side it is on, and refuses friendlies.

**5 reaches as far as the craft and stops there.** `WeaponSystems` crews every craft carrying a
recognised part; `WeaponSystem.LauncherOrdinal` is still pinned to the first launcher on it. That
last step is the one piece that restructures `Ksa/` rather than adding to `Sim/`, so it is the one
with no test coverage to fall back on. It wants doing on its own, with a flight after it, rather
than at the end of a long change — `docs/BATTERY-SPLIT.md` item 6.

A continuous-effect abstraction (beams, flamethrowers) and AI pilots sit after all of that, and
neither should be attempted speculatively.

---

## Articulation: what is expressible, and what is left

Each assembly is addressed by a hardcoded role — marker, pivot and reference elevation as three
unrelated fields — and `TubeGeometry.ElevatingPose` composes exactly **two** levels:
`P₀ + R₀·P₁`, rotating by `R₀·R₁`. A gun on a turret on a hull is exactly that shape and works
today; the 2A38M cannon are the proof. What is not expressible is a **third** level — turret,
then cradle, then gun — which needs `P₀ + R₀·(P₁ + R₁·P₂)`. Adding one assembly at the level that
does work still touches eight places across `Sim/`, `Ksa/`, the XML, the Blender script and the
validator.

The shape that fixes it is one record — marker, pivot-from-parent, axis, reference angle, parent
index — with a `PoseOf` that walks the chain, collapsing four `Find*`, four `TryApply*Aim`, three
`*Pose` wrappers and ten profile fields. It also removes a silent failure by construction: a
profile can currently declare `GunsMarker` and omit `GunReferenceElevationRad`, and the default of
zero against a mesh modelled at 22° is a 22° error nothing reports.

**Not done, deliberately, and the third level should not be built at all yet.** Every mount costed
so far — naval, howitzer, mortar, remote weapon station — is traverse-then-elevate or does not
articulate, so the chain-walking record would ship ahead of its first instance, which is the
pattern `docs/AUDIT-2026-08.md` names.

**One passenger already generalises, and it is worth seeing why it did not need any of the above.**
The Pantsir's director rides the traverse, and neither the launcher nor the head composes the
other's motion: the traverse writes the base's transform along with everything else riding it, and
the head *reads* that transform through `Sim/OpticGeometry.MountFrame`. So the coupling is a pose
in the engine rather than a call between two systems, and it does not care what moved the base or
how many joints away it was — a hinge, an arm, or a chain nobody has built yet all work unchanged.

That is the cheap half of the chain-walking record, available without it: a passenger reading its
parent's finished pose needs no model of the chain, where a passenger *driven* from the chain needs
the whole thing. Reach for the record when something has to be **positioned** through several
joints; a passenger that can ask where it ended up does not. What *is* earned is per-channel
elevation (a `TraverseDrive` plus N `ElevationDrive`s, with per-channel `IsLaid`): two real
trunnions exist and share one angle. It is still a restructuring of the region where a mistake
shows up only in flight, so it wants a flight after it and should follow `docs/BATTERY-SPLIT.md`
item 6 rather than precede it.

### Geometry that is known wrong

`tools/model/checkswept.py` sweeps the drives and reports it, so these are measured rather than
suspected. What it still allows, and should not forever: the pods' inner tube column occupies the
same Z band as the turret cheeks and runs through them near the trunnion at every elevation — a
full tube diameter. It is hidden rather than visibly clipping, because the column is narrower than
the cheek it is inside, but the allowance that lets the sweep pass is wide enough to mask a real
defect between those two bodies. Designing it out means moving the columns or the cheeks in Z,
which is the axis both drives preserve.

---

## Method

From CLAUDE.md, and it applies to every test written for this work:

**A regression test only counts if it fails against the old code. Check that it does, every time.**
A test that advances the platform by exactly the `v*dt` it passes in cancels its own error, and
then passes against the right implementation and the wrong one alike. That looks like proof and is
worth nothing.

And a test that never varies its inputs cannot see a phase error: at a constant `dt` the right and
wrong orderings are indistinguishable, so a suite of them can hold a broken implementation green
indefinitely.
