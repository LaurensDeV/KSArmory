# Making a KSArmory weapon pack

A pack is an ordinary KSA mod that ships parts and art like any other, plus a file of weapon
definitions and about ten lines of C#. **KSArmory never looks for it.** It scans no folders, reads
no manifest and holds no list of packs — a pack depends on KSArmory and calls it, which is why
installing one is nothing but installing a mod.

> **Status.** Everything described here is implemented and covered by tests. Nothing has been
> flown yet, and three things named at the end are not built. `KSArmory.Armoury.Schema` is `1`.

**There is a complete worked example**: `KSArmory-example-mod` is this whole document as a folder
you can copy — a bomb rack, its art and its definitions, with no code in it at all. Everything
below is what it does and why.

If you have modded KSP, read `FROM-KSP-MODDING.md` first: there is no `PartModule` here, and that
is why the shape below is what it is.

`PACK-API-SURFACE.md` is this same contract generated from the code, and KSArmory's build fails if
it moves without being acknowledged — so a version of this page that disagrees with the reader is a
bug rather than something you have to work around.

---

## The five-minute version

```
mods/MyWeaponPack/
    mod.toml
    KSArmory/Weapons.xml      <- the weapons. KSArmory reads this; KSA never sees it
    MyWeaponPackAssets.xml    <- your parts, loaded by KSA and nothing to do with KSArmory
    MyWeaponPackGameData.xml
    Meshes/ Textures/
```

`mod.toml`, in full:

```toml
name = "My Weapon Pack"
assets = [ "MyWeaponPackAssets.xml", "MyWeaponPackGameData.xml" ]
```

That is a plain KSA content mod. **Nothing in it mentions KSArmory**, there is no `[StarMap]`
section, no assembly, and nothing to compile.

**A mod is a directory, so another mod's KSArmory content sits in a folder named after KSArmory
inside it.** That is the same reasoning that puts system settings at
`saves/<save>/KSArmory/systems.json`, and it buys the same things: several mods can do this without
agreeing on filenames, and uninstalling the pack takes its weapons with it.

KSArmory looks in that folder inside **every** installed mod and reads the `.xml` files it finds.
It holds no list of packs and never learns yours by name — the convention is the whole mechanism,
the same relationship KSA has with its own mods folder.

**`KSArmory/Weapons.xml` must not be in `mod.toml`'s `assets` array.** KSA hands every entry there
to its own asset-bundle loader, so a weapon file named there is parsed by the wrong reader.

A mod shipping only the two `*Assets.xml` files would still put your part in the editor. It just
would not be a weapon.

### If you do want code

A pack that needs to build definitions at runtime can ship an assembly instead, declare
`ModDependencies = [ { ModId = "KSArmory" } ]` under `[StarMap]`, and call
`KSArmory.Armoury.Register(xml, "MyWeaponPack")` from `[StarMapBeforeMain]`. StarMap holds it back
until KSArmory is up and shares KSArmory's assembly out of *its* load context, both by default.

`Register` takes **text**, not profiles, and that is worth preserving: constructing a
`LauncherProfile` in C# needs `double3` from `Brutal.Core.Numerics`, which is RocketWerkz's and not
redistributable, so a pack that built one would need a KSA install to compile. Passing a string
needs `KSArmory.dll` and `StarMap.API.dll` and nothing else.

Most packs will never need any of this.

---

## Weapons.xml

```xml
<WeaponPack Schema="1">
  <Munition Name="AIM-9X" DisplayName="AIM-9X Sidewinder II" BodyMarker="Aim9x"
            Guidance="Seeker" NavConstant="4" SeekerFovDeg="90"
            LaunchSpeed="25" BoostSeconds="2.0" BoostAccel="480" MaxFlightSeconds="60"
            DragK="2.4e-5" MinRange="300" MaxRange="18000"
            ChargeKg="9.4" FuseRadius="12" FuseArmSeconds="0.5" />

  <Sensor Name="Seeker9X" DisplayName="AIM-9X seeker" Range="18000" ConeDeg="90"
          BoresightSource="PartForward" LockSeconds="1.0" />

  <Launcher PartId="MyWeaponPack_Prefab_Lau7x" DisplayName="LAU-7 rail (AIM-9X)"
            Munition="AIM-9X" Sensor="Seeker9X"
            EjectAwayFromMount="1.5" ReloadSeconds="0" LaunchLoft="0">
    <Tube Position="0, 0, 0.9" Direction="1, 0, 0" />
  </Launcher>
</WeaponPack>
```

Four rules that between them explain most refusals:

- **An attribute you leave out takes the profile's own default**, not zero. State what differs and
  nothing else. It is also why a field added to KSArmory next year cannot change how your pack
  behaves — your file does not mention it.
- **An attribute this build does not know refuses the whole definition.** `NavConstnat="6"` is
  rejected by name rather than ignored, because an ignored typo is a number you can see in your
  file that nothing reads.
- **Numbers are read in the invariant culture.** `2.4` is two and two fifths on every machine.
  Exponents (`3.0e-5`) are fine. Vectors are three numbers with commas: `"0, 0, 0.9"`.
- **Angles are written in degrees.** Attributes ending `Deg` are degrees; KSArmory holds radians
  and converts.

### `<Munition>` — how a round flies and what it does on arrival

`Name` and `DisplayName` are required.

| | Default | |
| --- | --- | --- |
| `BodyMarker` | *none* | subpart holding the round's mesh; without one it draws as a tracer |
| `FinMarker` | *none* | subpart holding one fin blade |
| `Guidance` | `CommandLink` | `Seeker`, `AntiRadiation`, `CommandLink`, `Inertial`, `None` |
| `NavConstant` | `4` | proportional-navigation gain; `0` flies straight |
| `MaxLateralG` | `35` | how hard it may turn |
| `SeekerFovDeg` | `55` | how far off its nose a seeker still sees the target |
| `LaunchSpeed` | `45` | m/s imparted at release. A rail imparts almost nothing |
| `BoostSeconds` | `2.4` | motor burn |
| `BoostAccel` | `520` | m/s² while burning |
| `MaxFlightSeconds` | `30` | after which it self-destructs |
| `MinRange`, `MaxRange` | `0`, `20000` | engagement envelope, m |
| `DragK` | `3.0e-5` | drag over frontal area; larger bleeds speed faster |
| `GravityCompensation` | `1` | how much of gravity guidance cancels |
| `NeutralDensityRatio` | `0` | density it floats at. Near `840` and it swims — that is a torpedo |
| `SeparationSeconds` | `0` | coast before the motor lights |
| `FuseRadius` | `15` | proximity burst, m |
| `TimedFuse` | `false` | burst at the aimpoint's range instead of on approach |
| `FuseArmSeconds` | `0.6` | dead time after release |
| `ChargeKg` | `20` | **the warhead, as one number.** Lethal radius, blast radius and fireball are all derived from it by the cube-root law, so doubling it multiplies reach by 1.26 |
| `HitsTerrain` | `false` | stops at the ground. Costs a terrain sample per round per frame |
| `BodyLength` | `3.10` | m, for drawing |
| `FinDeploySeconds` | `0.18` | |
| `FinDeflectionDeg` | `0` | fins that visibly steer |
| `FinHingeStation` | `0` | where along the body the fins hinge |
| `FinsPerRound` | `0` | blades; `0` means the older single-set scheme |
| `FinStowedScale` | `0.06` | |
| `MaxFaithfulStepSeconds` | `0.32` | beyond this a step is too coarse to integrate honestly |

A staged motor adds stages **after** the first. `BoostSeconds` and `BoostAccel` are stage one, and
each `<Stage>` follows it in order:

```xml
<!-- boosts at 520 m/s² for 2.4 s, then sustains at 90 m/s² for 8 s, then coasts -->
<Munition Name="TwoStage" DisplayName="Two-stage" BoostSeconds="2.4" BoostAccel="520">
  <Stage Seconds="8.0" Accel="90" />
</Munition>
```

### `<Sensor>` — what a launcher can see

`Name` and `DisplayName` are required.

| | Default | |
| --- | --- | --- |
| `Range` | `36000` | m |
| `ConeDeg` | `90` | full width of the search volume |
| `BoresightSource` | `LocalUp` | `LocalUp`, `PartForward`, `TurretAxis`, `MountNormal`. A rail wants `PartForward`; a hemispheric search set wants `LocalUp` |
| `ThreatRadius` | `8000` | closest approach inside which a contact is a threat |
| `ThreatHorizonSeconds` | `40` | how far ahead closest approach is predicted |
| `LockSeconds` | `1.5` | dwell before a track matures |
| `MinTargetSpeed` | `15` | below which a contact is ignored |
| `Emits` | `false` | whether it transmits — the only thing anti-radiation rounds home on |
| `ReferenceCrossSectionM2` | `0` (off) | contact size scaling. Range goes as the **fourth** root, so a target a hundredth the size is seen at a third the range |
| `NotchSpeed` | `0` (off) | Doppler notch. Rejects clutter **and** loses a target crossing exactly abeam |
| `ClutterFloorMetres` | `0` (off) | height below which contacts are lost in ground return |
| `HorizonMasking` | `true` | whether the planet's bulk blocks line of sight |
| `TerrainMarginMetres` | `0` | inflates the masking sphere |
| `TerrainSamples` | `0` (off) | height-map lookups one contact may cost. Real terrain masking, at a real per-frame price |
| `TerrainClearanceMetres` | `30` | how far above the ground a contact must be |

### `<Launcher>` — the part, and what it does with the round

`PartId`, `DisplayName`, `Munition` and `Sensor` are required. `PartId` must be a part you declared
in your own `Assets.xml`.

**A launcher must be able to shoot with something** — at least one `<Tube>` or a `GunMunition` —
or it is refused. A launcher that can shoot with nothing is a part fire control adopts and then
holds fire on for ever, with no gate reporting why.

| | Default | |
| --- | --- | --- |
| `<Tube Position="x, y, z" Direction="x, y, z" />` | — | one per tube. `Direction` is optional; without it the tube points along the pod axis. Splayed tubes, a VLS and an MLRS are all just tube lists |
| `MagazineDepth` | `0` (= tube count) | rounds carried |
| `SalvoSpacing` | `0.45` | s between launches |
| `ReloadSeconds` | `12` | `0` means no reload — a rail is spent |
| `LaunchAlongTube` | `true` | false throws the round off-axis toward a high off-boresight target |
| `LaunchLoft` | `0.35` | how much the round is pitched up on release |
| `EjectAwayFromMount` | `0` | m/s pushing the round clear. What a rail and a rack use |
| `MuzzleOffset` | `8` | m ahead of the tube the round appears |
| `TubeArmamentLabel` | `Missiles` | what the panel calls it. A rack says "Bombs" |

**Trainable launchers** name the subparts that move. Leave them all out and the launcher is fixed:
`Trains` is false, the drives are skipped, and fire control never waits for it to settle.

| | Default | |
| --- | --- | --- |
| `TurretMarker` | *none* | the assembly that traverses |
| `PodsMarker`, `GunsMarker`, `RadarMarker`, `OpticBaseMarker` | *none* | assemblies that ride it |
| `TurretPivot` | `0,0,0` | where the traverse turns, in part space |
| `PodPivotFromTurret`, `GunPivotFromTurret`, `RadarPivotFromTurret`, `OpticBaseFromTurret` | `0,0,0` | trunnions, measured from the turret pivot |
| `PodReferenceElevationDeg`, `GunReferenceElevationDeg` | `0` | the elevation the mesh was modelled at |
| `SlewRateDeg`, `ElevationRateDeg` | `70`, `45` | °/s |
| `SettleSeconds` | `0.35` | how long both axes must be on target before firing |
| `MinElevationDeg`, `MaxElevationDeg` | `0`, `82` | |
| `ForwardMinElevationDeg`, `ForwardArcDeg`, `ForwardPlateauDeg` | `15`, `80`, `62` | a raised floor over an arc, so the gear does not swing through its own bodywork |
| `RestElevationDeg` | *modelled pose* | where it stows |
| `SearchRadarRpm`, `SearchRadarFaces` | `20`, `1` | a search array turns off the clock, never off the track |

Declaring elevating gear with **no trunnion offset is refused**: an assembly pivoting about the
turret's own centre reads as a pod orbiting the vehicle.

**A cannon** needs `GunMunition` and at least one `<Muzzle At="x, y, z" />`. A launcher may have
tubes, a cannon, or both; the Phalanx has no tubes at all.

| | Default | |
| --- | --- | --- |
| `GunAmmo` | `480` | belt |
| `GunRoundsPerMinute` | `2500` | |
| `GunBurstRounds`, `GunBurstGapSeconds` | `12`, `0.55` | |
| `GunReloadSeconds` | `20` | |
| `GunArmamentLabel` | `Cannon` | |

### `<Optic>` — a sighting head

Its own part, needing no weapon on the craft at all: a hull with one director on it is an
observation post. `PartId`, `DisplayName`, `Sensor`, `BaseMarker`, `HeadMarker` are required.

| | Default | |
| --- | --- | --- |
| `Gimbal` | `Mast` | `Mast` elevates over its mounting face; `RollNod` rolls its whole nose about the pod centreline and nods within it |
| `HeadPivot` | `0,0,0` | where the head turns, in part space |
| `RollMarker` | *none* | **required for `RollNod`**, meaningless on a `Mast` |
| `EyeForward` | `0.30` | m from the pivot to the aperture |
| `SlewRateDeg` | `90` | °/s |
| `MinElevationDeg`, `MaxElevationDeg` | `-20`, `85` | mast heads |
| `MaxOffBoresightDeg` | `135` | roll-nod heads: the nod stop, which is the only travel limit a rolling head has |
| `KeyholeDeg` | `4` | cone about the roll axis the aim is held out of. Dead along it there is no roll angle, and a target crossing the nose asks for unbounded roll rate |

---

## Names, and using somebody else's round

Every name your pack declares is filed under your pack. `Name="AIM-9X"` in `MyWeaponPack`
registers as `MyWeaponPack:AIM-9X`, so a pack that ships an `AIM-9X` and a pack that ships another
are two rounds rather than one silently beating the other.

Inside your own file, **bare means yours**:

```xml
<Launcher … Munition="AIM-9X" Sensor="Seeker9X">
```

To use something else, qualify it. `KSArmory:` is the built-ins:

```xml
<Launcher PartId="MyWeaponPack_Prefab_Gun" DisplayName="My mount"
          Munition="KSArmory:20MM" Sensor="Seeker9X" GunMunition="KSArmory:20MM">
  <Muzzle At="0, 0.1, 1.2" />
</Launcher>
```

That is a whole launcher and no munition at all. The built-in keys are `57E6`, `30MM`, `AIM9J`,
`AIM120C`, `AGM88`, `20MM`, `B61`, `MK21`, and the sensors `1RS1`, `AIM9SEEK`,
`AIM120SEEK`, `AGM88SEEK`, `VPS2`, `BOMBSIGHT`, `MIRVBUS`, `EO`, `Litening`.

**A name that resolves to nothing is refused when the file is read**, not at the first shot. So is a
name something else already claims — including your own round, if you register the same pack twice.
That case matters more than it looks: the name is then in the catalogue carrying a *different*
profile, so a launcher naming it would load, fly, and throw a weapon you never shipped.

---

## When something does not appear

Everything refused is written to `<KSA user dir>/Logs/KSArmory.log`, one line per fault, naming
your pack, the definition and what was wrong. Check in this order:

1. **Is the mod enabled?** KSA writes a newly discovered mod into `manifest.toml` with
   `enabled = false` and says nothing about it, so dropping the folder in is not enough. KSArmory
   warns about a pack it can see whose mod is switched off, so this one at least announces itself.
2. **Is the file at `KSArmory/Weapons.xml` inside your mod folder?** That exact path is how it is
   found. A pack KSArmory never mentions at all is one it never found.
3. **Is your part in the editor?** If not, the problem is your `Assets.xml`, not KSArmory.

Once the world has loaded, KSArmory checks each registered part against what actually exists and
warns about two things the log will name: a `PartId` nothing declared, and a marker matching **no**
subpart or **more than one**. The second is worth reading twice — resolution is a case-insensitive
substring taking the first hit, so `PodsMarker="Pods"` against subparts `Rail_Pods` and
`Rail_PodsCover` drives whichever your XML happens to list first, and reordering it changes which
assembly moves.

---

## Traps that are not yours, and will still bite you

- **Never remove a `<SubPart>` from a shipped part.** KSA pairs a saved part with its current
  definition positionally, so removing one throws out of `Popup.DrawAll` and **terminates the
  game** on every save holding that part. Adding and renaming are free. Leave a stub to hold the
  count.
- **Markers match by case-insensitive substring, first hit wins.** A marker `Missile` also matches
  a subpart called `MissileRail`. Name subparts so no marker is a prefix of another.
- **Do not name your part *instances*.** Recognition matches `Part.Id`, which is the instance name
  when there is one and the template Id only when there is not.
- **Case matters on Linux.** A texture path that differs only in case loads on Windows and fails
  elsewhere.

---

## What a pack cannot do

Data covers a great deal: every flight, warhead, sensor and optic number, tube positions and
directions, magazine and belt, articulation markers, and which round and sensor a launcher pairs
with. What it cannot do is add a *kind*:

- **a new weapon kind** — a beam, a hitscan, anything without a discrete round.
- **a new guidance law**, as opposed to new numbers for one of the five.
- **a new gimbal**, beyond `Mast` and `RollNod`.
- **articulation beyond traverse-then-elevate.** A drum, a translating rail, per-tube motion or a
  radar that trains independently of the turret has no expression here.

Those are C# changes to KSArmory itself. If you want one, `MODULARITY.md` is the argument about
what each would cost and `EXTENSIBILITY.md` is where a registration seam for them would go.

Two more limits are KSA's rather than ours: there is **no damage below destruction**, so armour and
penetration are not expressible at any level of cleverness; and there is **no part module**, so a
part cannot *say* what it is — KSArmory recognises it by part Id, which is why the Ids in your two
files have to agree.

## Not built yet

Named so you do not go looking:

- **No offline validator.** `tools/validate-parts.py` checks KSArmory's own art and does not yet
  take a `--mod-root`.
- **No in-game list.** Refusals are in the log; there is no panel window showing them.
