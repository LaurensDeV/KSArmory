# Modularity: what generalises, what does not, and what it would take

The mod was built around three profile types and a registry so that a second weapon system would
be *data plus art*. This is an audit of how far that actually holds, read out of the code rather
than out of the design notes.

**The four changes it proposes are not implemented — they are a plan.** The test work that had to
come first *is* done, and the last section records what that turned up.

**Summary: modular for rounds, mostly modular for launchers, not modular for mounts.**

---

## Where it stands

### Different rounds — genuinely there

`Interceptor` never names a munition. Every number arrives as a `MunitionProfile` argument per
`Update`, and there is exactly **one** branch on round type in the whole flight model:

```csharp
SeekerInView = munition.Guidance == GuidanceMode.CommandLink || /* seeker cone check */;
```

`Sim/Interceptor.cs:352`. Boost, drag, nav constant, fuse, blast, body mesh and fin timing are all
profile fields; bodies and fins resolve by `BodyMarker` / `FinMarker`, so a new round brings its own
meshes with no code change. Profiles are mutable fields read *by reference* every frame, which is
what makes live panel tuning work.

The limit is the *class* of weapon, not the round. `Interceptor` is one concrete type with a
hardwired integrate → guide → fuse loop. `NavConstant = 0` approximates unguided and
`BoostSeconds = 0` a pure coast, but there is no shape for a gun burst, a hitscan, a beam or a
timed airburst. A CIWS cannon is not a `MunitionProfile`.

### Different launchers — modular in count, rigid in articulation

Discovery is `Arsenal.LauncherForPart(part.Id)` (`Ksa/LauncherPart.cs:41`); nothing hardcodes an
Id. Tube count is fully derived — `Magazine`, the `stackalloc` at `Ksa/DefenceBattery.cs:526` and
the body sync all size off `profile.TubeCount`, and `Ksa/DefenceBattery.cs:234` re-sizes the
magazine when a *different* profile is recognised. A non-training launcher (`TurretMarker = null`)
is a supported shape.

**The hard assumption is that articulation is exactly three named subparts in one fixed kinematic
chain**: traverse about part +X, elevate about +Z at a trunnion offset, radar spinning about +X.
Those axes are `TubeGeometry.TraverseAxis` and `ElevationAxis` (`Sim/TubeGeometry.cs:28-30`),
composed by `PodPose` (`:99`) and `RadarPose` (`:116`), and `LauncherProfile` offers exactly three
markers and three pivots. That covers another turret-and-pods system, or a fixed box. It does not
cover a rotating drum, a translating rail, per-tube articulation, two elevating groups, or a radar
that trains independently of the turret.

The sharper limit: **`TubeOffsets` is `double3[]` — positions only** (`Sim/LauncherProfile.cs:46`).
`TubeGeometry.TubeAxisPartFrame` (`Sim/TubeGeometry.cs:46`) derives one axis for the whole pod from
`PodReferenceElevationRad`, so every tube necessarily points the same way. Splayed tubes, a VLS with
divergence or an MLRS cannot be expressed at all.

### Different mounts — the weak axis

A **static site already works** — it is a landed vehicle that does not move. **On a rocket** it
works structurally, since a part on a vehicle is a part on a vehicle. Two things break in
behaviour:

- **`Boresight = KsaWorld.LocalUp(Platform)`** (`Ksa/DefenceBattery.cs:221`). The search cone points
  radially outward regardless of vehicle attitude. On a truck that is the sky; on a pitched-over
  booster or anything in orbit it is pointed at nothing. This is already listed under "Not done" in
  CLAUDE.md, but its significance changes completely once the launcher is on something that
  manoeuvres.
- **One battery per *world*, not per craft.** `DefenceBattery` is a single instance
  (`Ksa/AirDefenceMod.cs:35`) and `ResolvePlatform` (`Ksa/DefenceBattery.cs:323`) elects exactly one
  platform. A static site *and* a rocket-mounted launcher gives you one of them, silently. `Config`
  likewise holds one active profile set, re-`Select`ed every frame by whichever battery won.

---

## Proposed changes, ranked

| # | Change | Size | Mostly lands in | Unlocks |
| --- | --- | --- | --- | --- |
| 1 | `TubeOffsets` becomes `Tube(position, direction)`, direction defaulting to the pod axis | small | `Sim/TubeGeometry.cs` | any launcher whose tubes are not parallel |
| 2 | `DefenceBattery` becomes a list, one per launcher part found | medium | `Ksa/DefenceBattery.cs` | static site + vehicle + rocket at once |
| 3 | `BoresightMode` on `SensorProfile` (LocalUp / PartForward / TurretAxis) | small | `Sim/SensorProfile.cs`, `Ksa/DefenceBattery.cs` | a launcher on anything that pitches |
| 4 | Articulation as a list of drives rather than three named roles | large | `Sim/TubeGeometry.cs`, `Sim/LauncherProfile.cs` | drums, rails, per-tube motion |

**4 is deliberately last and should not be attempted speculatively.** It is the one whose shape is
least knowable before a second launcher exists that actually needs it.

**1, 3 and 4 are now cheaper than this audit first estimated**, because the geometry they rewrite
has since moved into `Sim/` and is covered — see the section below. 1 and 4 are almost entirely
`TubeGeometry` edits with tests already standing behind them. **2 has not moved at all**: fire
control, platform election and the salvo timers are still KSA-facing and still unreachable from the
test project, so it remains the riskiest of the four despite being the middle-sized one.

Change 1 crosses the `tools/model/pantsir.py` → `muzzles.json` → `Arsenal` boundary that
`validate-parts.py` guards, so the generator and the validator move with it. That is the third
piece of geometry duplicated across a boundary in this repo and the first two both drifted — see
CLAUDE.md.

Two minor items worth folding in: `Ksa/Ui.cs:78` and `:83` hardcode "Pantsir-S1" in operator-facing
text where `_config.Launcher.DisplayName` is available, and `Arsenal.MunitionNamed` falls back to
`Munitions[0]` on an unknown name with no warning, so a typo'd key silently flies the wrong round.
The fallback is now pinned by `WeaponSystemSelectionTests` rather than merely noted.

---

## Test gaps — **closed**

The audit that prompted this work found 117 tests passing with good coverage where it existed:
eight offset/phase tests all verified to fail against their predecessors, 22 on the turret drive,
21 on the threat model, plus fuse and guidance-discrimination suites.

**The problem was that the coverage boundary was drawn at the file layout, not at the risk.** The
test project links `Sim/**` and references no KSA assembly, so `Ksa/` has zero coverage by
construction. That is the right design — but a body of *pure* logic was sitting on the wrong side
of it, and it was disproportionately the logic the refactors above rewrite.

That logic has been lifted into `Sim/`, the same way `FireGeometry` came out of `LauncherPart`.
The `Ksa/` side keeps only the property writes. **117 → 203 tests.**

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

Per the method below, every regression test was checked by reintroducing the bug it guards:

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

The lookup-returns-element-zero case is the instructive one: **every pre-existing `ArsenalTests`
assertion still passed against it.** So did `OnlyTheTravelIsRotatedIntoThePartFrame` on its first
draft, because its anchor sat on the rotation axis where rotating it is a no-op — the same way the
zigzag test cancelled its own error. It was rewritten with an off-axis anchor and then failed.

### Verified in flight

The extraction touches the live firing path, and the tests it added cover `Sim/` — not the `Ksa/`
side that was left behind. So it was flown before being committed: a full twelve-round salvo on one
target, distinct tube numbers with no double-booking warning, target destroyed, a sim-speed
excursion to 0.01x and back, and zero warnings across 2,246 log lines. That exercises all three
extractions — `Magazine` in the tube numbering, `TubeGeometry` in the seating and pose chain,
`StepGate` in the speed change.

### Still not reachable

Extraction has limits, and these remain untestable because they genuinely need a `Vehicle`, a
`Part` or a `Camera`. They are the target of any next round, not oversights:

- fire-control *sequencing* — salvo spacing and the reload timer. The magazine came out; the timing
  did not. The `IsLaid` *decision* did come out, into `FireGate`: it depends on four booleans and a
  settle time, none of them KSA types. What is still in `Ksa/` is the mode ladder above it —
  spin, manual, stow, track — and the ordering of the four transform writes.
- `ResolvePlatform`, and the platform-election order.
- `LauncherPart.Find` and subpart resolution by marker substring.
- the centre-of-mass correction in `TryGetTubeMuzzleEcl`, and `ResolveOriginEcl`'s camera round trip.
- `SyncRoundBodies`' loop over live rounds, and `Radar.Scan`'s vehicle iteration. The maths inside
  both — `TubeGeometry`, `ThreatModel` — is covered; the iteration is not.

---

## Reaching further: torpedoes, RPGs, aircraft, submarines

A second audit, against a much wider ambition than the first. Read out of the code and out of the
engine's decompiled source, not estimated.

**Summary: the weapon side reaches further than expected, the platform side is not the mod's
problem at all, and the two real ceilings are both KSA's rather than ours.**

### Platforms are not weapons

Aircraft and submarines are **craft the player builds**, not things this mod adds. The battery
mounts on any `Vehicle` carrying a registered launcher part and never asks what shape it is —
`BoresightMode` already lets a launcher on something that manoeuvres search forward rather than
along local "up".

So "can we support aeroplanes" has no weapon-side answer, because there is nothing to support.
What would actually make them interesting is **AI that flies them** and **IFF so they can fight
each other**, and neither is a weapon concern. IFF is cheap now and expensive after ten weapon
types exist; AI pilots are a project in their own right.

### Torpedoes — one small generalisation away

The engine has water: `Celestial.GetOceanReference()` gives a density, and there is an ocean
radius and a splash event. Nothing is blocked there.

The mod's blocker is naming plus one resolver. `Sim/` already threads a **scalar medium density
ratio** through the flight model — the maths does not care what the medium is — but it is called
`airDensityRatio`, and `KsaWorld.AirDensityRatioAt` only reads the atmosphere, so it returns 0
below the waterline and a torpedo would coast frictionlessly.

What a torpedo needs:

| | |
| --- | --- |
| rename the ratio to a **medium** density | mechanical, ~24 references |
| resolver returns ocean density below the ocean radius | small, `KsaWorld` only |
| buoyancy | new, `Sim/` — a torpedo does not fall like a rock |
| surface-crossing behaviour | new, `Sim/` — and the interesting part |

Everything else it already has: `Slug` is unguided-kinetic, `Interceptor` is guided, both fuse on
proximity or contact, and both obey the frame rules by contract.

### RPGs — expressible today

An unguided rocket is `Slug` with a launch speed, or an `Interceptor` with `NavConstant = 0`.
`MunitionVarietyTests` and `ProjectileContractTests` already fly both shapes. No new code.

The gap is what it shoots *at*: see below.

### What the architecture genuinely cannot express

**Targets must be whole vehicles.** `TargetState` is medium-agnostic — position, velocity, radius —
but `TargetRef` is cast to `Vehicle` in five places in `DefenceBattery`. There is no way to aim at
a *part*, a *point on the ground*, or a static structure. An anti-tank RPG wanting a specific
component, or a bomb wanting a coordinate, cannot say so. Contained to those five places, but real.

**Continuous-effect weapons have no home.** `IProjectile` is a discrete object with a position, a
flight and a fuse. A laser has no flight time, a flamethrower has no discrete round. Those need a
sibling abstraction, not another `IProjectile`.

**Magazines are physical tubes.** Still the blocker for belt-fed guns, and it now blocks torpedo
tubes that reload from a rack too.

### The two ceilings that are not ours

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
| 5 | **Per-craft weapon manager** | **not started** |

**None of 1–4 has been flown.** They are covered by tests — 295 now — and every regression check
was verified against the bug it guards, but this repository has repeatedly shipped changes that
compiled, passed and were still wrong in flight. Treat them as unverified until a salvo says
otherwise.

What each unblocked, concretely:

- A torpedo is now an ordinary `MunitionProfile`: a small `DragK`, a `NeutralDensityRatio` near
  840, and it swims. No new flight model.
- An RPG or a bomb can name a coordinate or a component rather than a whole craft.
- A gun is a `LauncherProfile` with one or two tubes and a `MagazineDepth` in the hundreds.
- A battery can be told whose side it is on, and refuses friendlies.

**5 is deliberately still open.** It is the one piece that restructures `Ksa/` rather than adding
to `Sim/`, so it is the one with no test coverage to fall back on — and the two most recent
in-flight bugs both came out of exactly that region. It wants doing on its own, with a flight
after it, rather than at the end of a long change.

A continuous-effect abstraction (beams, flamethrowers) and AI pilots sit after all of that, and
neither should be attempted speculatively.

---

## Articulation: what an audit found, and what is left

Four assemblies are addressed by four hardcoded roles — marker, pivot and reference elevation as
three unrelated fields each — and `TubeGeometry.ElevatingPose` composes exactly **two** levels.
A gun on a turret on a hull is not expressible: it needs `P₀ + R₀·(P₁ + R₁·P₂)`, and adding one
assembly today touches eight places across `Sim/`, `Ksa/`, the XML, the Blender script and the
validator.

The shape that fixes it is one record — marker, pivot-from-parent, axis, reference angle, parent
index — with a `PoseOf` that walks the chain, collapsing four `Find*`, four `TryApply*Aim`, three
`*Pose` wrappers and ten profile fields. It also removes a silent failure by construction: a
profile can currently declare `GunsMarker` and omit `GunReferenceElevationRad`, and the default of
zero against a mesh modelled at 22° is a 22° error nothing reports.

**Not done, deliberately.** The guns made this the second elevating assembly rather than the
first, so it is no longer speculative — but it is a restructuring of the region where the two most
recent in-flight bugs came from, and it wants a flight after it rather than the end of a long
change. The same applies to per-channel elevation drives (a `TraverseDrive` plus N
`ElevationDrive`s, with per-channel `IsLaid`), which is the same refactor at a different scale and
should follow rather than precede the weapon manager.

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
The test written for the round-body zigzag passed against both implementations — it advanced the
platform by exactly the `v*dt` it was passed, so the error cancelled. It looked like proof and was
worth nothing.

And a test that never varies its inputs cannot see a phase error: at a constant `dt` the right and
wrong orderings are indistinguishable, which is how this suite passed against two broken
implementations for months.
