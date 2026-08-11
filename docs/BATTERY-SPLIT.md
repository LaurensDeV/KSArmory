# Splitting `DefenceBattery`, and what to call it instead

> **Item 4 has landed.** `DefenceBattery` is now `WeaponSystem`, `BatteryRoster` is
> `WeaponSystems`, and `BatteryConfig` and `BatterySettings` are `SystemConfig` and
> `SystemSettings`. Item 8 landed with it: the consumers take roles from
> `Ksa/WeaponSystemRoles.cs` rather than the whole class. **The old names are kept below on
> purpose**: the argument is about why they are the wrong words, and rewriting them erases it.

A plan, not a refactor. It answers four questions about `Ksa/DefenceBattery.cs`: whether the class
is one thing or several, what the right word is if not "battery", which parts of a split would
move logic into `Sim/` and therefore become testable, and what a split could break without anyone
noticing.

**Nothing here is flown.** Every claim is a claim about the source, which by this repository's own
rule makes the two defects below diagnoses rather than fixes.

**Cite symbols, never file and line**, per `docs/MODULARITY.md`. A line citation is wrong within
months; a symbol survives the edits above it.

**Read `docs/MODULARITY.md` first.** It covers what the *data model* can express. This covers how
the *code* is arranged. The mod scores much better on the first than the second, and the two move
independently.

---

## What is measured

Counted from the source rather than estimated. The class has grown since these counts were taken,
which strengthens the argument rather than weakening it:

- `Ksa/DefenceBattery.cs` is 1,650 lines with a public surface of 52. It owns platform election
  and pinning, part discovery for five subparts, drive latching, the turret and optic drives,
  radar spin, the missile magazine, the gun channel, round-body placement, two fire-control
  ladders and their arbiter, the blast sweep, and the operator event log.
- **19 files under `src/` name the type.** Fourteen take one as a parameter or hold one as a
  field: `BatteryRoster`, `ChaseCamera`, `Designator`, `Diagnostics`, `GunSound`, `KSArmoryMod`,
  `MotorPlume`, `MotorSound`, `MuzzleFlash`, `ScenarioRunner`, `Sight`, `TracerTrail`, `Ui`,
  `Visuals`. Three of the remaining five are prose in `Sim/` rather than references; one is a
  cross-reference in `LauncherPart`; one is the class itself.
- **Five members carry most of the traffic.** Across `Ksa/`: `Platform` is read 35 times, `Radar`
  20, `Rounds` 17, `Sensor` 13, `Launcher` 11. Nothing else reaches double figures except
  `Turret`, `Profile` and `Ammo` at 8.
- **Four consumers share one slice exactly.** `MotorSound`, `MotorPlume`, `TracerTrail` and
  `ChaseCamera` between them touch five members and no others: `Platform`, `PlatformEcl`,
  `Launcher`, `Rounds`, `PlumesEnabled`. Four of the fourteen need about a tenth of the class.
- **The panel's system pane already calls it something else.** `SettingsStore` writes
  `systems.json` and carries a comment saying so outright: a fixed emplacement or a sensor mast is
  a weapons system and is not a battery. The rename is already half-decided on disk.

### Two defects

Both are consequences of the size, and both are worth fixing whatever happens to the structure.
**Both are fixed**, and both are stated here as the defect rather than as the repair, because the
structure that admits them is what the rest of this document is about.

1. **An armed Phalanx reports `Holding fire: out of rounds` while its cannon fires.** `Holding()`
   is the missile channel's ladder, but it is the only one, and `Hold` is what the panel prints
   and what `Announce` writes to the log. A gun-only profile has `Tubes = []`, so `Magazine.Ammo`
   is zero forever and the `Ammo <= 0` rung answers first, above every gun-related condition,
   which there are none of. The mod's single most-read line answers a question nobody asked about
   a weapon that is not fitted.
2. **`_gunFlightTime` is never assigned, and the compiler says so.** Every build emits
   `warning CS0649: Field 'DefenceBattery._gunFlightTime' is never assigned to, and will always
   have its default value 0`. It is declared, documented as the flight time from the gun's last
   lead solve, and read exactly once, in `FireGun`, to set `Slug.FuseSeconds` when
   `MunitionProfile.TimedFuse` is on. `Slug` gates its timed burst on `FuseSeconds > 0.0`, so the
   panel's **Timed airburst (flak)** tick box silently does nothing: every shell falls through to
   its proximity fuse. The producer of that field would be in `AimPointEcl`, roughly 450 lines
   above the consumer, and nothing connects them. That a build warning naming the exact fault
   sits unread in the output of every `build.sh` is itself part of what a file this size costs.

Neither is an argument for a split on its own. Both are what a class of this size costs: a field
whose two ends cannot be seen together, and a ladder that grew a second weapon without growing a
second ladder.

---

## Is it one thing or several?

The obvious reading is a *mount*, a *magazine and its rounds*, a *fire-control loop*, and an
*installation*. Against the code that is close, and wrong in one place that matters.

### The reading that survives is five parts, not four

| Part | What it owns today | The CIWS | The rail |
| --- | --- | --- | --- |
| **Mount** | `Launcher`, `TurretPart`, `PodsPart`, `RadarPart`, `GunsPart`, `OpticPart`, `Turret`, the optic `PointingDrive`, `DriveStatus`, `RadarSpinRad`, `MountEcl`, `UpdateTurret`, `ResolveBoresight`, `IsLaid`, `GunsAreLaid` | trains, with a turret and guns and no pods | degenerate: `Trains` false, no subparts, always laid |
| **Missile channel** | `Magazine`, the missile bodies and fins, `Commit`, `SyncRoundBodies`, the salvo and reload timers, `Holding()` | **absent**: `TubeCount` is zero and the magazine holds nothing | the whole weapon: one tube, no reload |
| **Gun channel** | `GunChannel`, `_nextBarrel`, `_burstTrack`, `_manualTrigger`, `FireGun`, `UpdateGunFireControl`, the belt timer | the whole weapon | **absent**: `HasCannon` is false |
| **The flight** | `_rounds`, `UpdateRounds`, `SampleTarget`, `Detonate`, the blast sweep, `_pendingKills`, `AttributeRoundsToTracks` | shells | missiles |
| **Installation** | `BatteryConfig`, `Platform`, `PlatformPinned`, `PlatformEcl`, `PlatformStepEcl`, `Radar`, `Boresight`, the profiles, the event log, `Reset`, `AbandonFlight`, `SafeAll` | one | one |

**The magazine and the rounds are two things, and the CIWS is what proves it.** That reading pairs
them; the code cannot. `_rounds` holds `Slug`s fired by the gun channel and
`Interceptor`s fired by the missile channel, in one list, deliberately: `AttributeRoundsToTracks`,
the blast sweep, the round-body `flying` span and the negative-tube-number convention all depend
on there being exactly one author of it. A CIWS has no magazine and a great many rounds in the
air. So the flight belongs to the installation and the magazine belongs to the missile channel,
and pairing them would either give each channel its own round list, which breaks four things at
once, or leave the gun channel owning a magazine it does not have.

**The mount survives both edge cases, and the rail is the reason it is a real part rather than a
convenience.** A rail declares no `TurretMarker` and no `PodsMarker`, so `Trains` is false, the
drives are skipped and `FireGate.IsLaid` answers true forever. That degenerate case is already a
supported shape with a test pinning it (`ArsenalTests.AFixedLauncherIsJustAProfileWithNothingThatMoves`),
and `DriveFailureTests` pins the difference between it and a drive the engine refused. A mount
type would inherit both, unchanged.

### The seam that is not where it looks

**The missile channel and the gun channel are not symmetric today, and the asymmetry is the
work.** The gun channel is nearly self-contained: `UpdateGunFireControl` composes its own
`wantToFire`, runs its own belt timer and returns early on `!Profile.HasCannon`. The missile
channel is not a channel at all: it *is* the class. `Holding()`, `Commit`, `FireAtLock`,
`ReadyToFire`, `Reload` and `UpdateFireControl`'s reload gate all speak for the missiles by
default, and each one carries a `Profile.TubeCount == 0` or `Profile.TubeCount > 0` special case
so a gun-only launcher can slip past. The panel has none of that shape left: `Ui/UiSystem.cs`
reads `Sim/WeaponFit.cs` and asks the system what it is fitted with, which is the shape item 1
proposes for fire control.

That is a type test standing in for polymorphism, and it is the thing a third weapon would have to
be added to nine times. It is also what produced defect 1: the missile ladder answers for a
launcher that has no missiles because it is the only ladder there is.

### One thing genuinely is one thing

**The arbiter.** Only one traverse ring exists, so only one weapon can own the bearing, and
`AimPointEcl` plus `_ringIsOnGunLead` plus `FireGate.GunsHaveTheEngagement` and
`FireGate.MissilesMayFire` are the choice. That is not shared plumbing to be divided between
channels; it is a fifth thing that exists precisely because there are two. It stays with the
mount, which is what owns the bearing.

---

## What to call it

**The word has to be two words, because the class is two things.** "Battery" is not merely too
grand for a rail. It names the per-craft owner and the per-launcher weapon at the same time, and
that conflation is exactly why a craft carrying two LAU-7 rails fires one of them:
`LauncherOrdinal` is pinned to the first launcher found and `BatteryRoster` keys on `Vehicle`, so
the object that holds the policy and the object that runs the launcher have to be the same object.
Picking two words is therefore not cosmetic. It is the same decision as `docs/MODULARITY.md`
change 2.

### For the per-craft owner: `WeaponSystem`

**The case.** The mod already uses it everywhere except in the type name. `Ui/UiSystem.cs` is "the
panes that describe one weapons system"; `WeaponSurvey`, `WeaponInventory`, `WeaponRole` and
`ComponentProfile` are the survey's vocabulary; `BatteryRoster`'s own summary says "one battery per
weapons system in the world"; `SettingsStore` writes `systems.json` and explains in a comment that
it is named for what the panel calls them rather than for the class that happens to run one. A
rename to `WeaponSystem` imports no new vocabulary at all. It makes the code agree with prose that
is already there, and the on-disk format does not move: `BatterySettings` is serialised as
`Dictionary<string, BatterySettings>`, so the JSON carries property names and craft names and no
type name.

**The cost, stated plainly.** `Arsenal`'s `LauncherProfile` entries are also called weapon systems
in the docs, so the word would name both the design and the fitted instance. The answer is the
relation the mod already has and already documents: `MunitionProfile` is a round's design and
`Interceptor` is one in the air. `LauncherProfile` is a system's design and `WeaponSystem` is one
fitted to a craft. Say that once in `Arsenal`'s summary and the ambiguity is spent.

### For the per-launcher weapon: `FireUnit`

**The case.** It is the term of art for the smallest element that can engage a target on its own,
and it is true of all three without strain. A Pantsir vehicle is a fire unit. A LAU-7 rail is a
fire unit. A Phalanx mount is a fire unit. A *battery* is several fire units under one fire
control, which is precisely the word that was wrong. It also names the thing that has to become
plural for two rails on one craft to both work, so it arrives with a job rather than as a tidy-up.

**The cost.** Jargon. `docs/FROM-KSP-MODDING.md` exists for exactly this and would gain a line.

### The alternatives, and why they lose

| Candidate | True of a Pantsir | Of a rail | Of a CIWS | Verdict |
| --- | --- | --- | --- | --- |
| `Mount` | yes | yes | yes | but false of the thing owning the policy, the radar and the platform. **Keep it**, for the drives-and-parts part, where it is exactly right. |
| `Installation` / `Emplacement` | yes | no | yes | `BatteryConfig`'s doc already says "installation", so it has a foothold, and it reads badly for a rail on a booster at Mach 3. Good prose, poor type name. |
| `Armament` | yes | yes | yes | right for the per-craft owner, and the only strike against it is that nothing in the code says it today, where `WeaponSystem` is said in six places. |
| `Weapon` | understates | yes | yes | collides with `WeaponRole` and `WeaponSurvey`, which mean *components*. |
| `Turret` | | | | taken, and false of a rail. |

**Recommendation: `DefenceBattery` becomes `WeaponSystem`, `BatteryRoster` becomes
`WeaponSystems`, `BatteryConfig` and `BatterySettings` become `SystemConfig` and `SystemSettings`,
and `FireUnit` is introduced only in the commit that makes a craft able to carry two.** Renaming
`BatteryConfig` matters more than it looks: it is a `Sim/` type, so it is in the tests, and it is
the file whose summary already had to reach for the word "installation" because "battery" would
not carry it.

**Do not rename first.** A rename that lands before the seam is chosen renames one class into one
class and has to be done again. Ride it on whichever split commit makes it true.

---

## What buys tests, and what buys only tidiness

This is the ranking's real input. The test project links `Sim/**` wholesale and references no KSA
assembly, so anything moved there is covered the moment it exists, and anything rearranged inside
`Ksa/` is covered by nothing at all.

### Moves into `Sim/`: verification, per the `FireGeometry` precedent

| Move | Why it is pure | What the test buys |
| --- | --- | --- |
| **The hold ladder, one per channel.** `Holding()` reads about a dozen booleans, two counts, a range and profile fields. | Nothing in it is a KSA type; `TrackState` is already `Sim/`. | Defect 1 becomes a test, not a report. Every rung becomes assertable in the order it answers, which is the order the panel prints. A gun-only profile, a missile-only profile and both-fitted become three parameterised cases. |
| **The launch cycle.** Salvo spacing, the missile reload gate, the belt gate. | Two timers and a `TubeCount > 0` guard. | A CIWS reloading forever is this shape, and it is invisible to every existing test. `docs/MODULARITY.md` lists fire-control *sequencing* as the first thing extraction did not reach. |
| **The blast sweep.** Given a burst position, an elapsed-into-frame, the warhead radii, the protection policy and a list of (handle, position, velocity, radius), produce kills and near misses. | The world lookup is already hoisted into `_blastScratch` before the loop. Only the arithmetic moves. | This is the one operation in the class that is irreversible and has no test whatsoever. It also closes `docs/AUDIT-2026-08.md` defects 4 and 5 by construction: `MissDistance` defaulting to 0 becomes representable as "no fuse fired" rather than "a perfect hit". |
| **The turret mode ladder.** spin / manual / cursor / stow / track, producing a command rather than writing a part. | All four inputs are part-frame directions and policy booleans. `Turret` and `PointingDrive` are already `Sim/`. | `docs/MODULARITY.md` names this explicitly as what `FireGate` left behind. The ordering of the four transform writes stays in `Ksa/` and is untestable either way. |
| **The body plan.** Which round claims which body index, which index is double-booked, which is seated and which hidden. | `Magazine.Plan` is already there; the loop that assigns rounds to indices is not. | The double-booking warning is what stands in for a test today, which means the check runs in flight and nowhere else. |

### Rearrangements inside `Ksa/`: tidiness, and sometimes a precondition

| Move | What it actually buys |
| --- | --- |
| **A `Mount` type** holding the parts, the drives, the latches and the writes. | No coverage. It is worth doing anyway, because it is what makes the installation seam mechanical instead of exploratory: once the parts and drives are behind one object, making it plural is a list change rather than a rewrite. |
| **The installation / fire-unit seam** itself. | No coverage, and it is the biggest capability gate on the list. It wants a flight after it, alone, per `docs/MODULARITY.md`'s note on the same change. |
| **Narrowing the four effects consumers** to an interface carrying `Platform`, `PlatformEcl`, `Launcher`, `Rounds` and `PlumesEnabled`. | No coverage, small, and genuinely useful: four of the fourteen consumers stop depending on the whole class, and a fifth (`Visuals.DrawRounds`) is one member away. |
| **Splitting the file without splitting the type**, into `partial class` files. | Nothing. It hides the size rather than reducing it, and the field-across-450-lines problem that produced defect 2 gets worse, not better, when the two ends are in different files. **Do not.** |

---

## What could break silently

Everything below has the shape that compiles, passes the suite and is wrong in flight — including
the shape where a regression test passes against the broken and the fixed code alike.

- **One world sample, one instant.** `SampleWorld` sets `PlatformEcl`, `PlatformStepEcl`,
  `MountEcl` and `Boresight`, and every consumer differences against them. If a split object
  calls `KsaWorld.PositionEcl(Platform)` for itself, the pair no longer describes one instant and
  the overlay slides off the craft by a frame of ecliptic motion, which is about 500 m at 60 fps
  and looks like a drawing bug rather than a structural one. `DrawAnchorTests` guards the anchor
  and not this. **Rule for any split:
  the installation samples, and hands the sample down. No other object reads the world.**
- **The order inside `Update` is load-bearing and undefended.** Radar, then track attribution,
  then the turret, then the rounds, then missile fire control, then gun fire control. Rounds
  before fire control is deliberate and costs 658.78 m of travel at an age of 0.04 s if reversed.
  Split into objects with their own `Update` and the ordering becomes the caller's business, with
  no test anywhere able to see it. If the sequencing does not move into `Sim/` with the ladders,
  it is protected by nothing but this paragraph.
- **`_ringIsOnGunLead` crosses the seam.** It is written by `AimPointEcl`, inside the turret
  update, and read by `Holding()`, inside missile fire control. Put those in two objects and the
  flag has to be carried across, and reading it one frame late reopens the roughly 18 degree
  off-axis missile launch that `FireGate.MissilesMayFire` exists to close. Proportional navigation
  recovers from that, so the only symptom is arithmetic: nothing visible in flight reports it.
- **Drive latches record a refusal by one vehicle's part tree.** `DriveStatus` is cleared by
  `Reset()` on purpose. A `Mount` that is *recreated* per platform rather than reset would clear
  latches every time the roster re-crews a craft, so a drive the engine is refusing would look
  healthy on a cadence nobody is watching. `DriveFailureTests` covers the latch, not its lifetime.
- **The profile fans out to three places at once.** When a launcher is first recognised,
  `Profile`, `Munition`, `Sensor`, `Radar.Sensor`, `Turret`'s limits, `Magazine.Resize` and
  `GunChannel.Fill` are all set inside one `if (changed)`. Split them across objects and missing
  one leaves a magazine sized for the previous system. The symptom is a launcher that fires the
  wrong number of rounds and nothing else: no error, no log line.
- **`_rounds` has exactly one author, and several things quietly rely on it.** Tube numbers are
  unique among rounds in the air, the gun uses negative ones so `Magazine.IsOccupied` never sees
  them, `SyncRoundBodies` allocates a `flying` span over `TubeCount`, and
  `docs/AUDIT-2026-08.md`'s cluster-dispenser costing depends on it. Giving each channel its own
  list breaks all four, and the visible symptom is body flicker, which is what the double-booking
  warning reports.
- **A regression test for any of this only counts if it fails against the old code.** Two of the
  moves above are pure reshuffles with identical arithmetic, so a test written after the move will
  pass against the move and against its absence. Check each one by reintroducing the shape it
  guards, the way `docs/MODULARITY.md` records for the nine bugs checked that way.

---

## Where the answer is "leave it alone"

- **`Radar` stays a class of its own and stays where it is.** It is already separate, its maths is
  already in `Sim/ThreatModel`, and it is the one collaborator with a clean boundary.
- **`Turret` and `PointingDrive` stay exactly as they are.** They are `Sim/`, tested, and the only
  thing in `Ksa/` is the transform write.
- **Do not introduce an `IWeapon` interface with a missile and a gun implementation yet.** Two
  instances, and `docs/AUDIT-2026-08.md` lists five features that already ship ahead of their
  second instance. Extract the ladders as functions first; the interface becomes obvious or
  unnecessary once a third channel actually exists, and either answer is cheaper than guessing now.
- **Do not split `_rounds`.** See above.
- **A fourth *mount* needs none of this.** Another gun, another rail, a naval SAM box: all of them
  are a profile plus art today, which is what the CIWS demonstrates — a Blender module, an XML
  block, a registry entry and a handful of guards. The guards are the only cost, and item 1 below
  is what removes them.

---

## Ranked

Ordered by what unblocks the next weapon system, not by how ugly the code looks. Items come off
this list one at a time as they land.

1. **Extract the fire-control ladder into `Sim/`, one per channel, with the launch cycle timers.**
   The largest testability gain on the list and the smallest structural risk: no object moves, a
   private method becomes a pure function over its inputs. It removes the `TubeCount == 0` special
   cases scattered through the class — a set that grows rather than shrinks while each gun-only
   case is patched where it surfaces — and gives fire control the
   per-armament answer `Sim/WeaponFit.cs` already gives the panel. It is also the half of a third
   weapon channel that can be built without touching `Ksa/` structure at all.
2. **Extract the blast sweep into `Sim/`.** The only irreversible thing the class does and the
   only one with no test. Closes `docs/AUDIT-2026-08.md` defects 4 and 5 by construction. Pure
   arithmetic over a list the class already collects into a scratch buffer.
3. ~~**Fix `_gunFlightTime`, or delete it and the `TimedFuse` control with it.**~~ **Done**, by
   taking the flight time from the same lead solve that produces the aim point. Kept here for the
   reasoning: defect 2 is a field one assignment away from working behind a panel control that
   does nothing, and item 1 is what stops the next one of these staying hidden as long.
4. ~~**Rename:**~~ **Done.** `DefenceBattery` to `WeaponSystem`, `BatteryRoster` to `WeaponSystems`,
   `BatteryConfig` and `BatterySettings` to `SystemConfig` and `SystemSettings`. Cheap,
   mechanical across 19 files, no on-disk format change. Ride it on item 1's commit so it lands
   with a change that makes it true rather than on its own.
5. **A `Mount` type in `Ksa/`: the parts, the drives, the latches, the writes and the arbiter.**
   Buys no coverage on its own. Its value is that it turns item 6 from a rewrite into a list
   change, and it is the natural home for the bearing arbitration that currently reaches across
   the whole class through one private bool.
6. **The installation / fire-unit seam: several fire units per weapon system.** Unpins
   `LauncherOrdinal` and makes a craft carrying two rails fire both. The biggest capability gate
   here, and deliberately below the two moves that make it safer to attempt.
   `docs/MODULARITY.md` change 2 and `docs/AUDIT-2026-08.md`'s per-craft weapon manager are the
   same item. **Alone, with a flight after it.**
7. **The turret mode ladder into `Sim/`.** Named in `docs/MODULARITY.md` as what the `FireGate`
   extraction left behind. Below the seam because the seam changes what its inputs are.
8. ~~**Narrow the effects consumers to a rounds-in-flight interface.**~~ **Done**, and wider than
   proposed: ten consumers now take one of six roles. See `Ksa/WeaponSystemRoles.cs`. `MotorSound`, `MotorPlume`,
   `TracerTrail` and `ChaseCamera` need five members; give them five. Tidiness, but it is the
   cheapest item here and it is what stops the next effect class taking a dependency on
   everything.
9. **The body plan into `Sim/`.** Smallest gain, since `Magazine.Plan` already covers most of it.

**Do nothing yet:** an `IWeapon` interface, a per-channel round list, splitting the file into
`partial class` pieces, or renaming before the seam is chosen. Every one of those is either an
abstraction with fewer instances than it has cases, or a change that hides the problem it claims
to solve.
