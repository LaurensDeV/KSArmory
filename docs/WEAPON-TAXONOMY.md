# Weapon systems: what exists, what this mod can express, and what it cannot

A survey of real weapon systems by *functional architecture*, mapped onto this mod's vocabulary.
Its question is not "what else could we model" but "which real families share a data model with the
four we ship, and which need a different one".

**Relation to the other docs.** `docs/MODULARITY.md` asks how far the profile/registry split
stretches, from the inside. This asks the same question from the outside, starting from the
hardware rather than from the code. `docs/AUDIT-2026-08.md` TRACK 1 is a snapshot of the same
ground taken against v0.8.3, and a good deal of what it listed as missing has since landed;
where the two disagree, this file is the newer reading. `docs/BLOCKED-ON-KSA.md` remains the
authority on what the engine forbids, and nothing here overrides it.

**Cite symbols, never file and line**, per `docs/MODULARITY.md`. A line number is wrong within
months; a symbol survives edits above it and `grep` finds it.

**Provenance.** The real-world half is public encyclopaedic material. The structural claims are
well established, but the numbers in the directed-energy, electronic-warfare, ATGM and artillery
sections were not freshly cited and should be spot-checked before any of them becomes a constant
in `Arsenal.cs`.

---

## The skeleton every system shares

Almost every real system decomposes the same way, and five of the eight parts already have a home
here:

| Part | Question it answers | Where it lives in this mod |
| --- | --- | --- |
| Sensor chain | what can be seen, and how well | `SensorProfile`, one per launcher |
| Fire control | what to shoot at, when, where to point | `WeaponSystem`, `ThreatModel`, `FireGate` |
| Mount | how the effector is pointed and released | `LauncherProfile`, `Turret`, `TubeGeometry` |
| Munition | how it flies | `MunitionProfile`, `Interceptor`, `Slug` |
| Seeker | who holds the track *during* flight | `GuidanceMode`, `SeekerFovDeg` |
| Warhead and fuse | what ends the engagement | `ChargeKg`, `FuseRadius`, `TimedFuse`, `FuseArmSeconds` |
| Datalink | what reaches the round in flight | nothing of its own; implicit in `CommandLink` |
| Crew and automation | who authorises, and what happens without them | `SystemConfig` |

The parts list is not what separates families. These eight axes are:

| Axis | Values in the real world | Where this mod sits |
| --- | --- | --- |
| **A. Who holds the track in flight** | nobody, the launcher, the operator, the round, a third party | launcher or round |
| **B. What ends the engagement** | impact, proximity burst, dwell reached, probabilistic degradation, time fuse, nothing | impact or proximity |
| **C. Transit time** | zero (beams), sub-second (guns), seconds to minutes, hours (loiter, mines) | sub-second to seconds |
| **D. Launcher-to-aim coupling** | trainable, fixed rail, vertical with the aim decided later, gravity release, emplaced | trainable, rail, release |
| **E. Magazine topology** | belt, tubes, heterogeneous cells, rail, swappable pod, arm fed by a rotary magazine | tubes plus one belt |
| **F. Fire-unit autonomy** | self-contained, needs an external illuminator, an external track, or an external authority | self-contained only |
| **G. Trajectory shape** | direct, ballistic with two branches, lofted, vertical plus turnover, routed, boost-glide, loiter-then-dive | direct or ballistic |
| **H. Kill mechanism** | blast, hit-to-kill, shaped charge, penetrator, submunitions, burn-through, electronic upset, deception | blast |
| **I. Feedback loop** | none, between shots by human eye, **within the burst by radar**, in flight | in flight only |
| **J. Firing unit** | round, burst, ripple, **salvo with a shared impact time** | round and burst |
| **K. When the fuse is decided** | design time, load time, **at the muzzle after ignition**, by the round | design time |
| **L. Muzzle velocity** | constant, **a chosen discrete value**, **measured per round**, decaying with wear | constant |

That slice is coherent and well chosen. Every gap below is a family that falls outside it.

**Three things this mod already does that the taxonomy predicts are hard**, worth knowing before
anyone "improves" them:

- **The sight and the weapon are two pointing states.** A modern tank fire-control system holds the
  reticle on the target and drives the gun off it by superelevation and lead; the older
  disturbed-reticle design does the opposite and is worse. The mod is on the right side of that
  already: `Ksa/Sight.cs` paints the pipper where fire control actually sent the ring and a bracket
  on the target, so the gap between them is the lead being taken.
- **Rounds in flight are things a sensor can see.** `Ksa/RoundContact.cs` exists so a radar can hold
  somebody else's round. That is exactly the precondition for closed-loop spotting, which is
  otherwise the expensive part.
- **A weapon that engages what it cannot itself detect.** Half the families here have no organic
  sensor at all, and `SensorProfile.Range` of zero is already how `WeaponFit.Searches` says so.

---

## Families, clustered by whether they fit

### Cluster 1: aim, release, home, burst. **This model already serves them.**

Trainable gun-and-missile point defence (Pantsir, Phalanx, Kashtan, Goalkeeper, Gepard, C-RAM),
rail and pylon stores (AIM-9X, IRIS-T, R-73), gravity and glide stores, MANPADS, direct-fire
ballistic guns, and rocket artillery firing at a coordinate. A new entry here is data plus art,
which is the claim `docs/MODULARITY.md` makes and which holds.

Two things inside this cluster are still genuinely new:

- **Closed-loop spotting.** A Phalanx watches its *own outbound stream* on its tracking radar,
  measures where those rounds actually passed the target and drives that error to zero, typically
  by the third round. That is a feedback term, not a fresh lead solve, and it is the single most
  characteristic behaviour of a CIWS. `BallisticLead` has no notion of observing its own results.
  `AUDIT-2026-08` item 6 records that the solver ignores drag while the shell does not, worth 93 ms
  and 28 m against a 300 m/s crosser: closed-loop spotting is the mechanism that would absorb that
  error instead of accumulating it.
- **High off-boresight cueing and lock-on-after-launch.** With a helmet sight the aim line and the
  launcher axis are decoupled by more than 80 degrees, so the round turns hard off the rail rather
  than leaving a tube already pointed at the target. `LaunchAlongTube` is the switch for this and
  **no registered profile sets it false**, so the path exists and nothing selects it.

### Cluster 2: the sensors are not on the launcher. **Needs a new entity.**

Patriot, S-400, NASAMS, Iron Dome, Aegis, THAAD. A Patriot fire unit is seven vehicles; a NASAMS
is one radar, a fire distribution centre and launchers scattered kilometres apart, which works
only because the round is active-homing and the launcher needs no illuminator.

Here `SensorProfile` belonging to a launcher and boresighting off it (`LocalUp`, `PartForward`,
`TurretAxis`) is the blocker. Everything in this cluster needs a launcher that fires on a track it
did not generate.

Two behaviours inside it are worth stealing on their own merits:

- **Iron Dome propagates the incoming trajectory forward and declines to fire if it will land
  somewhere harmless.** The engage decision is a predicted-consequence test, not an envelope test.
  `ThreatModel` classifies on closest approach to the *launcher*, which is the same kind of
  reasoning pointed at a different question.
- **The illuminator as a scarce time-shared channel.** Aegis has three or four AN/SPG-62s and many
  semi-active rounds in the air, so each illuminator is scheduled across several engagements. This
  is the mechanism that makes saturation attacks work, and it is an occupancy problem rather than a
  geometry one. Nothing in `Magazine` or `FireGate` expresses it.

### Cluster 3: the launcher steers the round. **Needs a launcher-referenced guidance law.**

SACLOS by wire (TOW), laser beam riding (Kornet, Starstreak), track-via-missile (Patriot PAC-2).
In all three the steering law is an offset from a line the *launcher* owns, not a bearing to the
target, and the link has a break condition with a defined consequence. `GuidanceMode.CommandLink`
is the closest thing here and it is target-referenced.

### Cluster 4: navigate to a place. **Needs routes, and a target with no kinematics.**

Tomahawk, JASSM, Harpoon, GMLRS, JDAM, artillery fire missions. The round follows a route and does
not home; a JDAM's target is a latitude and longitude, not a track.

`Sim/Aimpoint.cs` already admits `Point` and `Ground`, and `WeaponSystem.FireAt(double3)` plus
`Designator` prove the round side works. What is missing is a *sequence* of them, terrain
following, and time-on-target control. **Auto-engage against a coordinate is still not possible**:
`Holding()` answers "no lock" and `UpdateFireControl` returns before firing, so a coordinate can
only be shot at by hand.

### Cluster 5: the weapon persists and has a mission state. **Needs a lifecycle.**

Loitering munitions (Switchblade, Lancet, Harop) transit, orbit for tens of minutes, are designated,
dive, and can be **waved off seconds before impact**. Mines have an arming delay of weeks and a
ship counter, and a CAPTOR is a mine that releases a torpedo.

`IProjectile` is `Flying → Detonated | Expired`, and `Interceptor.TargetRef` is
`{ get; private set; }`, so a round in the air cannot change its mind at all. `AUDIT-2026-08`
records three separate blockers for a mine, of which self-detonation on a `Point` aimpoint is the
one that bites first.

### Cluster 6: no projectile at all. **Needs a sibling to `IProjectile`.**

Lasers (LaWS, HELIOS, Iron Beam, DragonFire), high-power microwave, jammers, decoys.

A laser has no flight time. What replaces it is **dwell**: the beam holds the same few square
centimetres of a moving target for seconds, and anything that breaks the hold restarts the clock.
Its magazine is thermal and electrical rather than a round count, weather is a hard gate, and it
engages strictly one target at a time. High-power microwave inverts that with an area effect
against everything in a cone, which is why it is the answer to a swarm.

Electronic warfare is stranger still: it produces no object, destroys nothing, and its output is a
*degradation of somebody else's sensor* measured as burn-through range, which shrinks as the victim
closes. A jammer also reveals its own bearing by transmitting, which is what makes jam-strobe
triangulation work. Decoys spawn attractive-looking nothings and turn a seeker's lock into a
*choice among candidates*.

`docs/MODULARITY.md` costs this correctly and, if anything, understates it: the seam wanted is a
sibling of `IProjectile` at the *armament* level, and `WeaponFit` caps at two armaments by
construction.

---

## What is missing, ranked by leverage

Ranked by how many families each unlocks, with the repo's own discipline applied: **an abstraction
with zero real instances should not be built.** `docs/AUDIT-2026-08.md` names shipping ahead of an
instance as the pattern to stop repeating, and `Tube.Direction`, `BoresightMode` and
`NeutralDensityRatio` are the evidence.

| # | Gap | Unlocks | Instances today |
| --- | --- | --- | --- |
| 1 | **Sensor as a placed entity, separate from its launcher** | all of cluster 2 | **one**: the standalone EO director already senses without a weapon |
| 2 | **`GuidanceMode` as a sequence, not a constant** | AMRAAM, SM-6, Patriot, Mk 48, AARGM | zero |
| 3 | **Hit-to-kill as a kill mode** | PAC-3, THAAD, SM-3, every exo interceptor | zero, but nearly free |
| 4 | **A finite engagement-channel resource** | Aegis, and saturation as a mechanic | zero |
| 5 | **Heterogeneous magazine cells** | Mk 41, S-400, HIMARS pod swap | zero |
| 6 | **Vertical launch with turnover, and a launcher with no aim** | all naval VLS | zero |
| 7 | **Dwell-time effectors** | lasers, HPM | zero |
| 8 | **Soft kill: jammers and decoys** | EW, chaff, flares, Nulka | zero |
| 9 | **A route rather than a point** | cruise missiles, GMLRS, fire missions | zero |
| 10 | **Bearing-only contacts** | IRST, ESM, passive sonar, jam strobes | zero |
| 11 | **Per-round fuse and charge selected at firing** | AHEAD airburst, artillery charge zones | **one**: `TimedFuse` is wired end to end and unused |
| 12 | **Trajectory shape as a profile choice** | top attack, high-angle fire, boost-glide | zero |
| 13 | **Closed-loop spotting** | every CIWS, and it is what a CIWS *is* | **one**: `RoundContact` already makes rounds visible |
| 14 | **Firing-unit patterns beyond round and burst** | ripple, MRSI, time on target | zero |
| 15 | **Muzzle velocity as chosen or measured rather than constant** | artillery charge zones, AHEAD | zero |

**Only 1, 3, 11 and 13 have an instance, and only those four should be built now.**

- **1 is the highest leverage item on the list and its instance already exists.** `Ksa/OpticalHead.cs`
  is crewed per director, finds its own targets through its own `SensorProfile`, and needs no weapon
  on the craft. The generalisation that made it cheap is `Sim/OpticGeometry.MountFrame`: the head
  *reads* its base's finished pose rather than being handed the mover's angles. A launcher reading a
  track from a sensor it does not own is the same move at the fire-control level. Both rosters
  now key plurally and follow their part across a decoupler split through the same
  `Sim/PlatformHandover.cs` decision, which is the shape a third would take too.
- **3 is nearly free and lands in a seam that already exists.** `Slug` asks `Sim/IHullTest.cs` and
  `Interceptor` never does, deliberately. A hit-to-kill round is precisely an `Interceptor` that
  does. What blocks it is `Sim/Warhead.cs`: every radius derives from `ChargeKg` by a cube root, so
  a warhead of zero cannot be expressed, and `Detonate` scores the kill on
  `MissDistance <= LethalRadius + target MeanRadius`, which means the target's own bounding sphere
  would be doing all the work.
- **13 is the cheapest of the four and the most characteristic.** A Phalanx's tracker watches its
  own outbound stream, computes its closest approach to the target and drives that to zero, usually
  by the third round. Both halves are already here: `RoundContact` makes a round something a sensor
  can hold, and closest approach is the primitive `ThreatModel` already computes, pointed at a
  different pair of objects and fed to the drives instead of to a priority queue. It is also the
  honest answer to `AUDIT-2026-08` item 6, where `BallisticLead` ignores drag the shell obeys and
  costs 28 m against a 300 m/s crosser: a loop that observes its own error absorbs that rather than
  accumulating it.
- **11 wants `TimedFuse` to become a value rather than a bool.** AHEAD and Bofors 3P measure each
  round's *actual* muzzle velocity as it leaves the barrel and program the fuse in flight-time terms
  at the muzzle. `WeaponSystem` already sets `slug.FuseSeconds` from the same lead solve that
  produces the aim, so the machinery is there and no shipped shell selects it.

### Reskins: say these out loud and stop costing them

Extending the same list in `AUDIT-2026-08`. All are a profile entry plus art:

- **Autocannon, heavy MG, quad AA, remote weapon station, standalone CIWS**: a gun-only
  `LauncherProfile`. The CIWS is the worked example.
- **Anti-tank gun, recoilless rifle, RPG, railgun**: `Slug` with a different `LaunchSpeed`. What
  would make them interesting, penetration and armour, is downstream of damage below destruction,
  which KSA does not have.
- **Naval mount, howitzer carriage, mortar bipod**: `MinElevationDeg` / `MaxElevationDeg` /
  `ForwardArcDeg`. Roll stabilisation is free, because the command is recomputed in the part frame
  every frame.
- **Torpedo**: a small `DragK` and `NeutralDensityRatio` near 840. No new flight model.
- **Multi-stage rounds**: `BoostStage[]` exists and is used by nothing. See the defects below.
- **Mechanically scanned radar**: the mod already behaves as an AESA, tracking everything in the
  field of view with no scan penalty or revisit interval. The buildable thing is the *penalty*, and
  `RadarSpinRad` already advances on simulated time and currently feeds only the mesh.

### The one-line version

The model is a good implementation of cluster 1. The three axes it does not have, **sensors that
are not on the launcher**, **guidance that changes during flight**, and **effectors with no flight
time**, are each the defining feature of a whole tier of real systems, and each is worth more than
any number of additional rows in `Arsenal.cs`.

---

## Defects found while writing this

Not candidates. Things that are wrong now, diagnosed from the source and **not flown**, so per
CLAUDE.md they are diagnoses rather than fixes.

1. **`Arsenal.Named` falls back to element zero** on an unknown key, so a typo'd munition name
   silently flies the first round in the registry. Still true, still pinned by
   `WeaponSystemSelectionTests`.
2. **`Aimpoint.OnPart` has no production call site.** Component-level aiming is read by
   `WeaponSystem` and produced by nobody, which is a KSA ceiling rather than a mod one: there is no
   damage below destruction to apply it to.

Three others recorded here have since been fixed and are gone rather than annotated: a guided round
flying the 57E6's profile whatever launcher fired it, the Mk 82 rack labelling its bombs "Missiles",
and `MunitionProfile.Stages` having no instance — the HARM's dual-thrust motor is one.

Four capabilities are off by default and that is **deliberate**, recorded in CLAUDE.md rather than
missing: `ReferenceCrossSectionM2`, `NotchSpeed`, `ClutterFloorMetres` and `TerrainSamples`. They
are real capabilities with real costs, not upgrades.

## Where `docs/MODULARITY.md` is now wrong

Checked against the working tree, not assumed:

- "`Interceptor` never names a munition. Every number arrives as a `MunitionProfile` argument per
  `Update`" is **wrong**: a round carries its launcher's profile in a `required` field, and only
  `WeaponSystem` reads it. The parameter is still how every test supplies one, so nothing in `Sim/`
  can see a wrong value there.
- "exactly **one** branch on round type in the whole flight model" is stale: `SeekerInView` now has
  three terms, the added one being a `Ground` aimpoint.
- "**Nothing upstream of a round can name a coordinate** ... `Track` is a `required Vehicle`" is
  stale and the retype has landed. `Track.Contact` is `required IContact`, `Radar.Scan` takes
  contacts rather than vehicles, and `Sim/Iff.cs`, `Sim/Aimpoint.cs` and `Designator` all work. What
  genuinely remains is narrower: **auto-engage** still requires a lock.
- "the `stackalloc` in `WeaponSystem.Fire`" is in `SyncRoundBodies`.
- "worth 117 → 203 tests" is stale; 941 pass today.
- The articulation section's counts are stale in the direction that strengthens its argument: five
  `Find*`, five `TryApply*`, four `*Pose` and twelve profile fields, since `OpticBaseMarker` landed.

`docs/BATTERY-SPLIT.md` is in better shape. Its two named defects are genuinely fixed, and its
consumer-role item landed as `Ksa/WeaponSystemRoles.cs`; only the line count is stale.
