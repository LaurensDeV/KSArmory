# Making a KSArmory weapon pack

A weapon pack is an ordinary KSA mod. It ships parts and art the way any mod does, plus one or more
XML files that tell KSArmory which of those parts are weapons. There is no code to write and nothing
to compile.

KSArmory looks for a folder called `KSArmory/` inside every enabled mod and reads every `.xml` file
in it. Installing a pack is the same as installing any other mod, and uninstalling it removes its
weapons.

The [example mod](https://github.com/LaurensDeV/KSArmory-example-mod) is a pack to start from. Its
own weapon, a Mk 82 bomb rack, is the smallest complete pack: one part, its art and the definitions
in the Quick start below. It also carries a generated copy of every weapon KSArmory ships, for
reference.

Coming from Kerbal Space Program? Read [Coming from KSP modding](FROM-KSP-MODDING.md) first. There
is no `PartModule` here, and that explains the layout below.

The definition format is version 1 (`<WeaponPack Schema="1">`).

## Quick start

### 1. The folder

```
mods/MyWeaponPack/
    mod.toml
    MyWeaponPackAssets.xml     your parts, meshes and materials (read by KSA)
    MyWeaponPackGameData.xml   your parts' mass and colliders (read by KSA)
    Meshes/
    Textures/
    KSArmory/
        Weapons.xml            your weapons (read by KSArmory, never by KSA)
```

### 2. mod.toml

```toml
name = "My Weapon Pack"
assets = [ "MyWeaponPackAssets.xml", "MyWeaponPackGameData.xml" ]
```

Do not list `KSArmory/Weapons.xml` in `assets`. KSA would try to load it as an asset file and fail.
There is no `[StarMap]` section and no assembly.

### 3. The part

Declare the part in your asset files as you would for any KSA part. Two things matter to KSArmory:

- The `<Part Id>` is what your weapon definition names as its `PartId`.
- Each moving or drawn piece (a turret, a round's body) is a `<SubPart>`. KSArmory finds them by
  name, using the markers you give it (see [How markers match](#how-markers-match)).

The example mod's `ExampleModAssets.xml` is a working part with a round seated on it.

### 4. The weapon

This is the example mod's `KSArmory/Weapons.xml`, which defines a bomb, a bombsight and the rack
that carries them:

```xml
<WeaponPack Schema="1">

  <Munition Name="Mk82" DisplayName="Mk 82 500 lb bomb"
            BodyMarker="Mk82" BodyLength="2.22"
            BurstEffect="ExampleMod_Mk82Burst"
            Guidance="None"
            LaunchSpeed="4" BoostSeconds="0" BoostAccel="0"
            MinRange="0" MaxRange="20000" MaxFlightSeconds="120"
            DragK="1.25e-4"
            FuseRadius="0" FuseArmSeconds="1.5"
            ChargeKg="87" HitsTerrain="true" />

  <Sensor Name="BombSight" DisplayName="Mk 9 bombsight"
          Range="8000" ConeDeg="60" BoresightSource="MountNormal"
          ThreatRadius="3000" ThreatHorizonSeconds="20" LockSeconds="0.8" />

  <Launcher PartId="ExampleMod_Prefab_BombRack" DisplayName="Mk 82 bomb rack"
            Munition="Mk82" Sensor="BombSight" TubeArmamentLabel="Bombs"
            MuzzleForwardOffset="0.407" MuzzleOffset="2"
            LaunchAlongTube="true" LaunchLoft="0"
            EjectAwayFromMount="1.2"
            ReloadSeconds="0" SettleSeconds="0">
    <Tube Position="0.40650, 1.11000, 0.00000" Direction="0, 1, 0" />
  </Launcher>

</WeaponPack>
```

`BodyMarker="Mk82"` matches the subpart `ExampleMod_Rack_Mk8200` on the rack, which is the bomb's
mesh. `BurstEffect` names an explosion declared in the example's `ExampleModEffects.xml`; leave it
out and KSArmory picks one to suit the charge.

### 5. Try it

1. Enable the mod. KSA adds a new mod to `manifest.toml` with `enabled = false`, so copying the
   folder in is not enough.
2. Start the game. Your part should be in the editor. If it is not, the problem is in your asset
   files, not in the weapon definition.
3. Read `<KSA user dir>/Logs/KSArmory.log`. Every definition KSArmory refuses gets one line naming
   the pack, the definition and the reason.

## Rules for every definition

- **Leave out what you do not change.** A missing attribute takes the default listed in the tables
  below. A default is not necessarily zero.
- **Unknown attributes are errors.** A misspelt attribute (`NavConstnat="6"`) refuses the whole
  definition, and the log names it.
- **Numbers use a dot as the decimal separator** on every machine, whatever the system locale.
  Exponents are fine (`3.0e-5`). `TracerEvery`, `FinsPerRound` and `TerrainSamples` take whole
  numbers only.
- **Vectors are three numbers separated by commas:** `"0, 0, 0.9"`. Positions are in part space,
  in metres.
- **Angles are in degrees.** Every attribute ending in `Deg` is degrees.
- **Flags are `true` or `false`.**
- **A missing required attribute refuses the definition.**
- **The Values column says what an attribute takes.** *text* is any string, such as a subpart name or
  an asset Id; *name* is the `Name` of a definition, with an owner in front for someone else's (see
  below); *degrees* is a number in degrees; a list in backticks is every value accepted.

You can split a pack over as many files as you like, with any names. All the files in one mod's
`KSArmory/` folder are read together, so a launcher in one file can name a round in another. Files
in subfolders are not read. A file whose root is a bodies file is read as body data, not as weapons.

## Names and references

Every `Name` you declare is filed under your mod's Id, which is the `id` KSA gives it in
`manifest.toml` (the name of its folder under `mods/`). `Name="AIM-9X"` in the mod `MyWeaponPack`
is registered as `MyWeaponPack:AIM-9X`, so two packs can both ship an `AIM-9X` without clashing.

Inside your own files, a bare name means one of yours:

```xml
<Launcher ... Munition="AIM-9X" Sensor="Seeker9X">
```

To use a round, sensor or decoy from somewhere else, put the owner in front. `KSArmory:` means the
built-ins:

```xml
<Launcher PartId="MyWeaponPack_Prefab_Gun" DisplayName="My mount"
          Munition="KSArmory:20MM" Sensor="KSArmory:VPS2" GunMunition="KSArmory:20MM">
  <Muzzle At="0, 0.1, 1.2" />
</Launcher>
```

That launcher declares no rounds or sensors of its own.

Built-in names you can use:

| | |
| --- | --- |
| Munitions | `57E6`, `30MM`, `AIM9J`, `AIM120C`, `AGM88`, `20MM`, `5IN54`, `B61`, `MK21`, `ZUNI` |
| Sensors | `1RS1`, `AIM9SEEK`, `AIM120SEEK`, `AGM88SEEK`, `VPS2`, `MK68`, `TSU`, `EO`, `Litening`, `Irst`, `Yal1BeamDirector`, `BOMBSIGHT`, `MIRVBUS`, `ZUNISIGHT` |
| Decoys | `MJU7` (flare), `RR170` (chaff) |

References are checked when the file is read, not at the first shot. A name that matches nothing
is refused. So is a name that is already taken, including by your own pack if it is registered
twice. A name from another pack resolves only if that pack registered first, which for packs
without code is the order of `manifest.toml`.

## Replacing a built-in weapon

A pack may define a weapon on one of KSArmory's own parts. While that part's built-in family is
switched on, KSArmory keeps the part and the log says which family holds it. Once the player turns
the family off under *KSArmory > Built-in weapons* (and restarts), your definition is used and the
part stays in the editor. The family's rounds, sensors and decoys stay available either way, so your
replacement can still name `KSArmory:20MM`.

## The root element

```xml
<WeaponPack Schema="1"> ... </WeaponPack>
```

`Schema` is required and must be `1`. Any other attribute is reported in the log, and the rest of
the file is still read. (KSArmory's own files carry a
`Family` attribute, which is refused on a pack.)

Inside it you can put any number of `<Munition>`, `<Sensor>`, `<Decoy>`, `<Launcher>`, `<Optic>`,
`<SearchSet>` and `<Dispenser>` elements, in any order. Any other element is refused and logged.

## `<Munition>`: a round

How a round flies and what it does when it arrives. **Required:** `Name`, `DisplayName`.

**Drawing**

| Attribute | Values | Default | Meaning |
| --- | --- | --- | --- |
| `BodyMarker` | text | none | Subpart on the launcher that holds the round's mesh. A gun round with no body is drawn as a streak. |
| `BodyLength` | number | `3.10` | Length of the round in metres. Used for drawing and for how far the chase camera sits back. |
| `BodyModel` | text | none | For a gun round: the Id of a `<SubPart>` template to draw for each shell in the air. It does not need a subpart on the launcher, so any number of shells can be shown. |
| `FinMarker` | text | none | Subpart holding one fin blade. |
| `FinsPerRound` | whole number | `0` | Number of fin blades. `0` treats the fin subparts as one set per round, scaled open as it deploys. |
| `FinDeploySeconds` | number | `0.18` | Time for the fins to unfold after release; until then they are folded in the tube. |
| `FinDeflectionDeg` | number | `0` | How far the fins visibly swing when steering. |
| `FinHingeStation` | number | `0` | Where the fins hinge, in metres along the body from its centre. |
| `FinStowedScale` | number | `0.06` | Scale of a folded fin. |
| `TracerEvery` | whole number | `0` | For a gun round: one round in this many is a tracer. The others are drawn as faint grey streaks that fade out at night. `0` means no tracers. |
| `TracerBurnSeconds` | number | `3` | How long a tracer glows. The round keeps flying after it goes out. |
| `BurstEffect` | text | picked from the charge | An `<Explosion>` declared in your asset files. Changes only the look of the burst, not its damage. |
| `PlumeEffect` | text | KSArmory's | A `<ParticleEmitter>` declared in your asset files, shown at the nozzle while the motor burns. |

**Motor and flight**

| Attribute | Values | Default | Meaning |
| --- | --- | --- | --- |
| `LaunchSpeed` | number | `45` | Speed added at release, m/s. A rail adds very little. |
| `SeparationSeconds` | number | `0` | Time the round flies straight before it starts steering, counted from when its motor lights. It does not delay the motor; a launcher's `MotorDelaySeconds` does that. |
| `BoostSeconds` | number | `2.4`, or `0` for `Guidance="None"` and `"Inertial"` | Motor burn time (first stage). On an unguided round it makes a rocket: fired without a lock and pushed along the velocity it has gained since release, and the launcher's sight flies the burn. The Zuni in KSArmory's `KSArmory/Rockets.xml` is one. |
| `BoostAccel` | number | `520`, or `0` for `Guidance="None"` and `"Inertial"` | Acceleration while the motor burns, m/s². |
| `MaxFlightSeconds` | number | `30` | The round self-destructs after this. For a round with `HitsTerrain`, only time on a path that might not land is counted, so a long fall is never cut short. |
| `MinRange`, `MaxRange` | number | `0`, `20000` | Engagement range in metres. Automatic fire stays inside it. |
| `MassKg`, `CalibreMm`, `DragCoefficient` | number | none | Drag worked out from mass in flight (kg), diameter (mm) and a shape coefficient (about `0.3` for a supersonic shell). Give all three or none. |
| `DragK` | number | `3.0e-5` | Drag set by hand: k in a = k·v² in sea-level air. Use it when no fixed coefficient fits, such as a bomb passing the speed of sound. Ignored when the three above are given. |
| `NeutralDensityRatio` | number | `0` | Density at which the round floats, as a multiple of sea-level air (1.225 kg/m³). Around `840` makes it behave like a torpedo in water. |

A motor with more than one stage lists the extra stages as `<Stage>` children. `BoostSeconds` and
`BoostAccel` describe the first stage; each `<Stage>` follows in order. `Seconds` is required,
`Accel` defaults to `0`.

```xml
<!-- 520 m/s² for 2.4 s, then 90 m/s² for 8 s, then coasts -->
<Munition Name="TwoStage" DisplayName="Two-stage" BoostSeconds="2.4" BoostAccel="520">
  <Stage Seconds="8.0" Accel="90" />
</Munition>
```

**Guidance**

| Attribute | Values | Default | Meaning |
| --- | --- | --- | --- |
| `Guidance` | `Seeker` or `AntiRadiation` or `CommandLink` or `Inertial` or `None` | `CommandLink` | `Seeker`, `AntiRadiation`, `CommandLink`, `Inertial` or `None`. |
| `NavConstant` | number | `4` | Proportional navigation gain. `0` flies straight. |
| `MaxLateralG` | number | `35` | Hardest turn the round can pull, in g. |
| `GravityCompensation` | number | `1` | How much of gravity the guidance cancels. An `Inertial` tail kit ignores it. |
| `SeekerFovDeg` | number | `55` | Half-angle off the round's flight direction within which a seeker still sees its target. Not used by `CommandLink`. |
| `SeekerBand` | `Radar` or `Infrared` or `None` | `Radar` | `Radar`, `Infrared` or `None`. Chaff decoys radar seekers, flares decoy infrared ones, nothing decoys `None`. Read only when `Guidance` is `Seeker`. |
| `CountermeasureResistance` | number | `0` | From 0 to 1: how much of a decoy's chance of pulling the seeker away is ignored. |
| `ReacquiresAfterDecoy` | `true` or `false` | `true` | Whether a decoyed seeker looks for its target again once the decoy burns out. |
| `DopplerGateMps` | number | `0` | A radar seeker only sees chaff within this many m/s of the target's closing speed, which in practice means only when the target flies at right angles to it. `0` turns the gate off. |

**Fuse and warhead**

| Attribute | Values | Default | Meaning |
| --- | --- | --- | --- |
| `ChargeKg` | number | `20` | Explosive charge in kg. Lethal radius, blast radius and fireball size are all worked out from it, by the cube root: doubling the charge multiplies the reach by 1.26. |
| `MaxChargeKg` | number | `0` | Top of the yield slider in the panel. `0` uses the slider's own maximum. |
| `FuseRadius` | number | `15` | Proximity fuse distance, m. `0` means no proximity fuse. |
| `FuseArmSeconds` | number | `0.6` | Time after release before the proximity fuse works. The round still explodes if it hits something earlier. |
| `TimedFuse` | `true` or `false` | `false` | For flak: also burst at the flight time the gun's aiming solution asked for. The proximity fuse still works. |
| `HitsTerrain` | `true` or `false` | `false` | A shell, bomb or rocket stops at the ground. Missiles always do. Costs a terrain lookup per round per frame. |
| `BurstHeightMetres` | number | `0` | Air burst: go off this high above the ground on the way down. `0` bursts on impact. Needs `HitsTerrain`. |
| `ChuteSinkMetresPerSecond` | number | `0` | A parachute, given as the sea-level sink speed once it is open. `0` means no parachute. Needs `HitsTerrain`. |
| `ChuteOpensSeconds` | number | `1.5` | When the parachute opens after release. It takes about a second to fill. |

**Simulation step** (rarely needed)

| Attribute | Values | Default | Meaning |
| --- | --- | --- | --- |
| `MaxFaithfulStepSeconds` | number | `0.32` | Longest game frame the round can be simulated across accurately. Longer frames slow time warp while it flies. |
| `PreferredStepSeconds` | number | `0` | Frame length time warp is held to while this round flies. `0` uses `MaxFaithfulStepSeconds`. |
| `SubStepSeconds` | number | `0` | The round's own integration step. `0` means 5 ms. |

## `<Sensor>`: what a launcher or sight sees

**Required:** `Name`, `DisplayName`.

| Attribute | Values | Default | Meaning |
| --- | --- | --- | --- |
| `Range` | number | `36000` | Detection range, m. |
| `ConeDeg` | number | `90` | Half-angle of the search cone around the boresight. `90` is a hemisphere. |
| `BoresightSource` | `LocalUp` or `PartForward` or `TurretAxis` or `MountNormal` | `LocalUp` | Where the cone points: `LocalUp` (straight up from the planet), `PartForward` (along the part's +Y, use this for a rail), `TurretAxis` (follows the turret) or `MountNormal` (out of the part's mounting face). |
| `LockSeconds` | number | `1.5` | How long a contact must be tracked before it can be engaged. |
| `MinTargetSpeed` | number | `15` | Contacts moving slower than this relative to the system, m/s, are ignored. |
| `ThreatRadius` | number | `8000` | A contact predicted to pass closer than this, m, counts as a threat. |
| `ThreatHorizonSeconds` | number | `40` | How far ahead that closest approach is predicted. |
| `Emits` | `true` or `false` | `false` | Whether the sensor transmits. Anti-radiation missiles can only home on a sensor that does. |
| `Scope` | `None` or `Search` or `Emitters` | `None` | Which display the operator gets: `None` (no scope tab, right for a seeker or a sight), `Search` (a radar scope) or `Emitters` (a list of who is transmitting, for an anti-radiation seeker). `Search` on a set that is not `CuedOnly` also feeds what it holds to the craft's picture for its directors. |
| `CuedOnly` | `true` or `false` | `false` | The sensor finds nothing by itself and holds only the contact it is given, which then counts as a threat whether or not it is closing. The built-in directors' sensors set it. |
| `Infrared` | `true` or `false` | `false` | Passive heat detection. `Range` then only sets a maximum. |
| `IrThresholdWPerM2` | number | `6e-7` | Faintest heat an `Infrared` sensor detects, W/m². |
| `LookDownPenalty` | number | `1` | Multiplies that threshold for a contact seen against the ground. |
| `ReferenceCrossSectionM2` | number | `0` (off) | Scales range by target size. Range goes with the fourth root of size, so a target a hundredth the size is seen at a third of the range. |
| `NotchSpeed` | number | `0` (off) | Doppler notch, m/s. Rejects ground clutter, but also loses a target crossing exactly side-on. |
| `ChaffNotchMps` | number | `0` (off) | A target closing slower than this, with a bigger chaff return next to it, breaks the track. |
| `ChaffReacquireSeconds` | number | `3` | Time to find a target again after chaff broke the track. |
| `OpticalBackup` | `true` or `false` | `false` | After reacquiring, an optical channel keeps the track while the target stays in the notch, so chaff only breaks it once per pass. |
| `ClutterFloorMetres` | number | `0` (off) | Contacts below this height are lost in ground return. |
| `HorizonMasking` | `true` or `false` | `true` | Whether the planet blocks line of sight. |
| `TerrainMarginMetres` | number | `0` | Adds this much to the planet's radius when checking line of sight. |
| `TerrainSamples` | whole number | `0` (off) | Height-map lookups allowed per contact for real terrain masking. Costs frame time. |
| `TerrainClearanceMetres` | number | `30` | How far terrain must rise above the line of sight to block it. Keeps a site on a slope from being blind along the ground. |

## `<Launcher>`: the weapon part

Ties a part to a round and a sensor. **Required:** `PartId`, `DisplayName`, `Sensor`, and
`Munition` (except when `FiresBeam="true"`). `PartId` must be a `<Part>` declared in your asset
files.

A launcher must be able to fire something: at least one `<Tube>`, a `GunMunition`, or
`FiresBeam="true"`. Otherwise it is refused.

**What it uses**

| Attribute | Values | Default | Meaning |
| --- | --- | --- | --- |
| `Sensor` | name | required | The `<Sensor>` it searches and tracks with. |
| `Munition` | text | required, except with `FiresBeam="true"` | The `<Munition>` its tubes fire. |

**Tubes and release**

| Attribute | Values | Default | Meaning |
| --- | --- | --- | --- |
| `<Tube Position="x, y, z" Direction="x, y, z" />` | child element |  | One per tube. `Position` is required. Without `Direction` the tube points along the pod axis. Splayed tubes, vertical launch cells and rocket pods are all just lists of tubes. |
| `MagazineDepth` | whole number | `0` | Rounds carried. `0` means one per tube. |
| `SalvoSpacing` | number | `0.45` | Seconds between launches. |
| `ReloadSeconds` | number | `0` | Seconds after the last round before the launcher refills itself. `0` means never; only the panel's Reload button refills it. |
| `LaunchAlongTube` | `true` or `false` | `true` | `false` throws the round off-axis toward a target well off the boresight. |
| `LaunchLoft` | number | `0.35` | Bias toward the launcher's boresight. Only used when `LaunchAlongTube` is `false`. |
| `EjectSpeed` | number | `0` | An ejector: m/s the round is pushed out of the mounting face (+X), in place of the munition's `LaunchSpeed` along the tube. `0` slides it off the tube as a rail does. |
| `MotorDelaySeconds` | number | `0` | Seconds from release until the motor lights, so an ejected round falls clear first. On the launcher, so one munition can be carried on a rail that lights at once and an ejector that waits. |
| `CarriageMarker` | text | none | A store that brings its own rail or ejector: every subpart whose Id contains this is shown only while the part is attached to something, so the part list and a part being dragged show the store alone. Declare those subparts at `<Scale>` 0.001 so the part list's picture starts without them. A part may carry several carriages under different markers, and this picks the one that shows: the built-in missiles carry an ejector (`Carriage`) and a launch rail (`LaunchRail`). |
| `EjectorArmMarker` | text | none | Subparts an ejector pushes its round off with: every one whose Id contains this. They run out of the mounting face on release and come back. Drawn only; `EjectSpeed` is what throws the round. |
| `EjectorStrokeMetres` | number | `0` | How far the arms push, m. The round is accelerated evenly over it and leaves at `EjectSpeed` as the arms reach the end, so they stay on it the whole way: 0.10 m at 6 m/s is 33 ms. `0` moves no arm and pushes the round all at once. |
| `EjectorReturnSeconds` | number | `0.8` | Seconds for the arms to come back, after holding at full travel for 0.3 s. |
| `EjectAwayFromMount` | number | `0` | Tilts the launch direction toward the launcher's boresight: a fraction of the tube axis, not a speed. Only applies with `LaunchAlongTube`, and does nothing where the boresight is the tube axis, as on a rail whose sensor uses `PartForward`. |
| `MuzzleOffset` | number | `8` | Fallback only: distance along the boresight from the mount where a round appears if no launcher part is found, m. |
| `MuzzleForwardOffset` | number | `0` | Fallback only: distance from the part origin to the muzzle ring along the boresight, used with `TubeRingRadius` when the subpart carrying the tubes cannot be found, m. |
| `TubeRingRadius` | number | `0` | Radius of the ring launch positions fall back to if the subpart carrying the tubes cannot be found, m. |
| `StoreMassKg` | number | `0` | Mass of one tube round as carried, kg. Declare the part's mass with the full load; each round fired subtracts this and a reload adds it back. `0` changes nothing. |
| `<ReleaseSpin>` | child element | none | See [Spinning rounds](#spinning-rounds). |

**Panel**

| Attribute | Values | Default | Meaning |
| --- | --- | --- | --- |
| `TubeArmamentLabel` | text | `Missiles` | What the panel calls the tube weapon. A rack might say `Bombs`. |
| `FireControlLabel` | text | DisplayName + " fire control" | Name of the part's fire-control row. |
| `MintRows` | `true` or `false` | `true` | `false` shows only the rows your `<Provides>` children declare, with no sensor, gun or fire-control row added. |
| `FiresBeam` | `true` or `false` | `false` | Fires the beam of an `<Optic>` on the same part. Needs no `Munition`. Refused unless an `<Optic>` in your pack on this part has a `<Beam>`. |
| `BeamArmamentLabel` | text | `Laser` | What the panel calls the beam weapon. |

**Turret** (leave all of these out for a launcher that does not move)

| Attribute | Values | Default | Meaning |
| --- | --- | --- | --- |
| `TurretMarker` | text | none | Subpart that traverses. |
| `PodsMarker`, `GunsMarker`, `RadarMarker`, `OpticBaseMarker` | text | none | Subparts that ride the turret. Pods and guns also elevate. A `TurretMarker` needs at least a `PodsMarker` or a `GunsMarker`. |
| `TurretPivot` | three numbers, `x, y, z` | `0, 0, 0` | Point the turret turns about, in part space. |
| `PodPivotFromTurret`, `GunPivotFromTurret`, `RadarPivotFromTurret`, `OpticBaseFromTurret` | three numbers, `x, y, z` | `0, 0, 0` | Where each assembly hinges, measured from the turret pivot. Pods and guns need a non-zero pivot. |
| `PodReferenceElevationDeg`, `GunReferenceElevationDeg` | degrees | `0` | Elevation the mesh was modelled at. |
| `SlewRateDeg`, `ElevationRateDeg` | number | `70`, `45` | Traverse and elevation speed, °/s. |
| `SettleSeconds` | number | `0.35` | Time both axes must be on target before firing. |
| `MinElevationDeg`, `MaxElevationDeg` | number | `0`, `82` | Elevation limits. |
| `ForwardMinElevationDeg`, `ForwardArcDeg`, `ForwardPlateauDeg` | number | `15`, `80`, `62` | A raised elevation floor over a forward arc, so the guns do not swing through the vehicle's own bodywork. |
| `RestElevationDeg` | number | modelled pose | Elevation when stowed. |
| `TraverseLimitDeg` | number | `180` | Traverse either side of the modelled bearing. Below 180 the turret goes the long way round instead of through whatever is behind it. |
| `SearchRadarRpm`, `SearchRadarFaces` | number; whole number | `20`, `1` | Spin rate of the search array on `RadarMarker`, and how many faces it has. `0` faces: no spinning array. |

**Cannon.** Add `GunMunition` and at least one `<Muzzle At="x, y, z" />` (`At` is required). A
launcher can have tubes, a cannon, or both.

| Attribute | Values | Default | Meaning |
| --- | --- | --- | --- |
| `GunMunition` | name | none | The round the cannon fires. |
| `GunAmmo` | whole number | `480` | Rounds in the belt. |
| `GunRoundsPerMinute` | number | `2500` | Rate of fire. |
| `GunBurstRounds`, `GunBurstGapSeconds` | whole number; number | `12`, `0.55` | Rounds per automatic burst, and the pause between bursts. |
| `GunReloadSeconds` | number | `0` | Seconds after the belt runs out before it refills. `0` means never. |
| `GunRoundMassKg` | number | `0` | Mass of one complete round in the belt, kg. Subtracted from the part as it fires, like `StoreMassKg`. |
| `GunArmamentLabel` | text | `Cannon` | What the panel calls the cannon. |
| `GunSoundId` | text | KSArmory's cannon loop | A `<CompoundSound>` from your asset files, looped while firing. |
| `GunshotSoundId` | text | none | A `<Sound>` played once per round, for a slow-firing gun. On its own it replaces the loop. |
| `MuzzleFlashEffect` | text | KSArmory's | A `<ParticleEmitter>` from your asset files, shown at the muzzles. |

**Moving gun parts (visual only).** These ride the elevating gun and do not affect where rounds
go. The ram, feed and rotor markers each need their pin or pivot, measured from the turret pivot, or
the launcher is refused.

| Attribute | Values | Default | Meaning |
| --- | --- | --- | --- |
| `GunBarrelMarker`, `GunRecoilMetres`, `GunRecoilSeconds`, `GunReturnSeconds` | text; number; number; number | none, `0`, `0.08`, `0.6` | A barrel that recoils when the gun fires and then returns. |
| `GunCylinderMarker` + `GunCylinderPinFromTurret`, `GunRodMarker` + `GunRodPinFromTurret` | text; three numbers, `x, y, z`; text; three numbers, `x, y, z` | none | An elevation ram: a cylinder on the turret and a rod on the gun. |
| `GunFeedMarker` + `GunFeedPivotFromTurret`, `GunFeedRatio` | text; three numbers, `x, y, z`; number | none, `0` | An ammunition feed that turns by this fraction of the gun's elevation. |
| `GunRotorMarker` + `GunRotorPivotFromTurret`, `GunRotorSpinUpSeconds`, `GunRotorSpinDownSeconds` | text; three numbers, `x, y, z`; number; number | none, `0.2`, `1.0` | A rotary barrel cluster that spins while firing. |

**Post-boost bus.** For a missile bus that carries warheads to several targets, as the built-in
Heavy MIRV Bus does. Read only when the matching ballistic-computer setting is on: the hop engine
under *Hops burn the engine*, the trim figures under *Hops size their own slots*. Both are off by
default.

| Attribute | Values | Default | Meaning |
| --- | --- | --- | --- |
| `TrimAccelerationMetresPerSecond2` | number | none | Acceleration the bus's attitude thrusters give it when loaded. |
| `TrimWorstAxisExhaustVelocity` | number | none | Exhaust velocity of the weakest thruster axis. |
| `HopEngineThrustNewtons`, `HopEngineExhaustVelocity`, `HopEngineAccelerationMetresPerSecond2` | number | none | The bus's main engine, used to move between targets. Without these it never fires. |

### Spinning rounds

A `<ReleaseSpin>` child spins each round as it leaves (visual only). `RateDegPerSecond` and
`SpinUpSeconds` are required; `IgnitionSeconds` defaults to `0`. Add one `<Nozzle Mouth="x, y, z"
Exhaust="x, y, z" />` per spin motor, in the round's own frame (both attributes required). A
launcher may have only one `<ReleaseSpin>`.

```xml
<ReleaseSpin RateDegPerSecond="-180" SpinUpSeconds="0.6" IgnitionSeconds="2.0">
  <Nozzle Mouth="-0.876,  0.10607,  0.10607" Exhaust="-0.70711, -0.5,  0.5" />
  <Nozzle Mouth="-0.876, -0.10607, -0.10607" Exhaust="-0.70711,  0.5, -0.5" />
</ReleaseSpin>
```

### Gear inside the launcher part

KSArmory lists a craft's weapons by walking its parts. Equipment that is a subpart of your launcher
(a sight on a turret roof, for example) is not seen unless you declare it with a `<Provides>` child.
The sensor, gun and fire-control rows are added for you; do not declare those.

| Attribute | Values | Default | Meaning |
| --- | --- | --- | --- |
| `Role` | `FireControl` or `Launcher` or `Sensor` or `Camera` or `Gun` or `Guidance` or `Countermeasures` | `Sensor` | `FireControl`, `Launcher`, `Sensor`, `Camera`, `Gun`, `Guidance` or `Countermeasures`. |
| `DisplayName` | text | required | The row's name in the panel. |

```xml
<Provides Role="Camera" DisplayName="1TPP1 director" />
```

`Guidance` gives the craft a ballistic computer. The built-in MIRV bus uses it.

## `<Optic>`: a sighting head

A camera head that can be its own part, with no weapon on the craft. **Required:** `PartId`,
`DisplayName`, `Sensor`, `BaseMarker`, `HeadMarker`. Give it a sensor with `CuedOnly="true"`.

An `<Optic>` on its own part gets a camera row in the panel. One on a launcher's part does not; the
launcher declares it with `<Provides Role="Camera">`, as the Pantsir does.

| Attribute | Values | Default | Meaning |
| --- | --- | --- | --- |
| `Sensor` | name | required | The `<Sensor>` it looks with. |
| `BaseMarker` | text | required | The subpart for the fixed flange and mast. |
| `HeadMarker` | text | required | The subpart for the head that turns. |
| `Gimbal` | `Mast` or `RollNod` | `Mast` | `Mast`: a ball that elevates over its mounting face. `RollNod`: a targeting pod whose whole nose rolls about the pod's axis while the sight nods within it. |
| `HeadPivot` | three numbers, `x, y, z` | `0, 0, 0` | Point the head turns about, in part space. |
| `RollMarker` | text | none | The subpart that rolls. Required for `RollNod`, unused on `Mast`. |
| `EyeForward` | number | `0.30` | Distance from the pivot to the lens, m. |
| `SlewRateDeg` | number | `90` | Turn speed, °/s. |
| `MinElevationDeg`, `MaxElevationDeg` | number | `-20`, `85` | Elevation limits of a `Mast` head. |
| `MaxOffBoresightDeg` | number | `135` | Nod limit of a `RollNod` head, its only travel limit. |
| `KeyholeDeg` | number | `4` | A `RollNod` head will not aim within this angle of its roll axis, where tracking would need impossible roll rates. |
| `StowTuckDeg` | number | `0` | How far a parked `RollNod` ball nods past its rest once settled, so its windows face into the nose. It untucks before it slews. |
| `CarriageMarker` | text | none | A pod that brings its own ejector, as a `<Launcher>`'s `CarriageMarker`: every subpart whose Id contains this shows only while the part is attached. Declare those subparts at `<Scale>` 0.001. |
| `MaxMagnification` | number | `16` | Maximum zoom against KSA's 50° default view. Zoom steps ×1, ×2, ×4 and so on, ending at this value. `53` gives a 1° field. The limit for any optic is `1024`. |
| `HasLaser` | `true` or `false` | `false` | A laser rangefinder and designator, fired with the LASER button in the camera window. Another director with Spot on can lock onto its spot by code. |
| `LaserRangeMetres` | number | `20000` | Maximum laser range, m. |

### `<Beam>`: a high-energy laser

A `<Beam>` inside an `<Optic>` turns the head into a laser weapon. It fires along the sight's line
of sight when a `<Launcher FiresBeam="true">` on the same part triggers it. One `<Beam>` per optic.
An empty `<Beam />` gives the YAL-1's values.

| Attribute | Values | Default | Meaning |
| --- | --- | --- | --- |
| `PowerWatts` | number | `1e6` | Beam power, W. |
| `WavelengthMetres`, `ApertureMetres` | number | `1.315e-6`, `1.5` | Together these set the smallest spot the beam can focus to. |
| `JitterRad` | number | `2e-7` | Pointing wobble, which spreads the spot. |
| `BurnThroughJoulesPerM2` | number | `6e7` | Energy per square metre that destroys a part from full health. |
| `ExtinctionPerMetre` | number | `2e-5` | Fraction lost to the air per metre at sea-level density. |
| `ChemicalSeconds`, `ShotSeconds` | number | `100`, `5` | Total lasing time in a full load, and how long one press fires. |

## `<SearchSet>`: a search-only sensor part

A part that only searches, such as an IRST on a hull. What it finds is shared with the sights and
any beam weapon on the same craft. **Required:** `PartId`, `DisplayName`, `Sensor`. It gets a
sensor row in the panel.

| Attribute | Values | Default | Meaning |
| --- | --- | --- | --- |
| `Sensor` | name | required | The `<Sensor>` it searches with. |
| `PanelName` | text | DisplayName | Name of its row in the panel. |
| `HeadMarker` | text | none | A head that turns behind the window. Visual only: the set sees its whole cone wherever the head points. |
| `HeadPivot` | three numbers, `x, y, z` | `0, 0, 0` | Point the head turns about, in part space. |
| `HeadTravelDeg` | number | `60` | How far the head may turn off the part's +Y, °. |
| `HeadSlewDegPerSecond` | number | `90` | Head turn speed, °/s. |
| `ScanHalfWidthDeg`, `ScanPeriodSeconds`, `ScanNodDeg` | number | `45`, `6`, `8` | The figure-of-eight the head sweeps when it has nothing to look at. |

## `<Decoy>`: a flare or chaff

What a dispenser throws. **Required:** `Name`, `DisplayName`, `Kind` (`Flare` or `Chaff`). Flares
decoy infrared seekers; chaff decoys radar seekers.

| Attribute | Values | Default | Meaning |
| --- | --- | --- | --- |
| `Kind` | `Flare` or `Chaff` | required | `Flare` decoys infrared seekers, `Chaff` radar seekers. |
| `PeakSignature` | number | `0` | Flare: heat in kW/sr. Chaff: radar return in m², on a scale where a craft of 11.3 m mean radius reads 400. |
| `RiseSeconds` | number | `0` | Time to reach peak signature. |
| `LifeSeconds` | number | `4` | Time until it has faded out. |
| `MassKg` | number | `0` | Mass of one decoy, subtracted from the dispenser part as it is thrown. |
| `DragPerMetre` | number | `0` | Quadratic drag at sea-level air, per metre: deceleration is this times speed squared, scaled by air density. |

## `<Dispenser>`: a decoy launcher part

**Required:** `PartId`, `DisplayName`, `Decoy`. It gets a countermeasures row in the panel. Its
kind comes from its decoy. You may also write `Kind`, but it is refused if it does not match the
decoy. Every dispenser of the same kind on a craft fires when the button is pressed.

| Attribute | Values | Default | Meaning |
| --- | --- | --- | --- |
| `Decoy` | name | required | The `<Decoy>` it throws. |
| `Kind` | `Flare` or `Chaff` | its decoy's | Refused if it does not match the decoy's kind. |
| `Count` | whole number | `30` | Decoys loaded. |
| `SalvoSize`, `SalvoIntervalSeconds` | whole number; number | `2`, `0.25` | Decoys thrown per press, and the time between them. |
| `EjectDirection` | three numbers, `x, y, z` | `1, 0, 0` | Direction they leave in, part space. |
| `EjectSpeed` | number | `30` | Ejection speed, m/s. |

## Your own effects

A burst, a motor plume and a muzzle flash are ordinary KSA assets. Declare an `<Explosion>` or a
`<ParticleEmitter>` with your own Id in an asset file listed in `mod.toml`, then name it with
`BurstEffect`, `PlumeEffect` or `MuzzleFlashEffect`. KSArmory's own effects in
[`KSArmoryParticles.xml`](../src/KSArmory/KSArmoryParticles.xml) are good starting points.

If no mod declares the Id you name, `KSArmory.log` says so when the world loads. The weapon then uses
KSArmory's plume or muzzle flash, and a burst goes off as KSA's own explosion for its charge.

## Bodies.xml: describing a planet

Any mod, such as a solar-system mod, can tell KSArmory what a nuclear burst or a laser needs to know
about a body. Put a file with a `<Bodies>` root in your `KSArmory/` folder. It is read from every
enabled mod.

```xml
<Bodies>
  <Body Id="MyMoon" FieldTesla="5e-6" FieldTiltDeg="10" Airglow="CarbonDioxide" Gamma="1.3"
        Condensation="false" Surface="true" XRayOpacity="1" />
</Bodies>
```

| Attribute | Values | Meaning | If left out |
| --- | --- | --- | --- |
| `Id` | text | The body's Id in KSA's system. | required |
| `FieldTesla` | number | Magnetic dipole strength at the surface equator, T. Used only for a body KSA gives no field. Not negative. | no field, so no aurora |
| `FieldTiltDeg`, `FieldAzimuthDeg` | degrees | The dipole's tilt from the spin axis, and the longitude it tilts toward. Used only with `FieldTesla`. | `0` |
| `Airglow` | `OxygenNitrogen` or `CarbonDioxide` or `Hydrogen` or `Neutral` | Case-sensitive. | `Neutral` (dim, uncoloured) |
| `Condensation` | `true` or `false` | Whether a burst raises white condensation. | follows the body's weather and ocean |
| `Gamma` | number | The air's ratio of specific heats, for the speed of sound. Greater than 1 and less than 2. | `1.4` |
| `Surface` | `true` or `false` | Whether the body has solid ground. | `true` |
| `XRayOpacity` | number | X-ray stopping per kg of air, relative to nitrogen and oxygen. Not negative. | `1` |
| `ThinCloud` | Ids, comma-separated | The body's cloud layers that a laser passes through. | none |

Pressure, density, scale height and the top of the atmosphere come from KSA and are not repeated
here. If two mods describe the same body, the later one wins and the log says so. After the world
loads, the log lists any entry whose Id matches no body, which is how a misspelt Id shows up.

## When something does not appear

**Before starting the game**, run KSArmory's validator over your folder. It is in the
[KSArmory repository](https://github.com/LaurensDeV/KSArmory), so you need a copy of that:

```bash
./tools/validate-parts.py --mod-root ../MyWeaponPack     # add --offline without a KSA install
```

It catches problems the game would otherwise fail on without a message: `mod.toml` listing a
missing file or a weapon file, an asset file `mod.toml` does not list, a texture or mesh path that
differs only in upper/lower case, a `<Launcher>` or `<Optic>` `PartId` no `<Part>` declares, a drive
or sight marker that matches no subpart or more than one, a stacking connector with no `<Scale>`, and
a round's body or fin marker that matches nothing on its launcher or both match the same subpart. It does not check attribute values; the
log does that.

**In game**, every refusal is written to `<KSA user dir>/Logs/KSArmory.log`, one line each, with
the pack, the definition and the reason. Check in this order:

1. **Is the mod enabled?** KSA adds new mods to `manifest.toml` with `enabled = false`. KSArmory
   logs a warning when it finds definitions in a disabled mod.
2. **Is the file in a `KSArmory/` folder inside your mod folder?** If the log never mentions your
   pack, KSArmory did not find it.
3. **Is your part in the editor?** If not, the problem is in your asset files.

After the world loads, KSArmory also warns about a `PartId` that no part declares, and a marker
that matches no subpart or more than one.

### How markers match

A marker such as `PodsMarker="Pods"` matches any subpart whose Id contains that text, ignoring
case, and the first match wins. Against subparts `Rail_Pods` and `Rail_PodsCover`, `Pods` drives
whichever is listed first in your XML, and reordering the XML changes which one moves. Name your
subparts so that no marker is contained in another subpart's name. The example mod names its bomb
subpart `Mk8200` so that `BodyMarker="Mk82"` matches it and nothing else.

## Part rules that come from KSA

- **Never remove a `<SubPart>` from a part you have released.** KSA matches a saved part's subparts
  to the current definition by position, so a removed one makes KSA drop every craft holding the part
  when a save loads, and the next save makes that permanent. Adding subparts at the end is safe. To retire one, leave a stub in its place.
- **Do not rename a released `<Part>` Id.** A save refers to parts by Id, and KSA drops any craft
  that names a part that no longer exists. Treat `<SubPart>` template Ids the same way.
- **Do not give your part instances their own names.** KSArmory recognises a part by `Part.Id`,
  which is the instance name if there is one and the template Id otherwise.
- **Give a stacking connector its diameter as `<Scale>`.** Otherwise the part sinks onto a Core
  tank's internal node, and the validator fails it.
- **A part with any `ToSurface` or `FromSurface` connector cannot be the first part of a craft.** A
  part either stacks or surface-attaches.
- **Core's `Radial` editor tag stops anything being mounted on the part carrying it.** Leave it off a
  launcher that a sight should fit onto.
- **File names are case-sensitive on Linux.** A path that differs only in case works on Windows and
  fails on Linux.

## Registering from code

Most packs do not need this. A pack that has to build its definitions at runtime can ship an
assembly instead:

1. In `mod.toml`, add a `[StarMap]` section with `ModDependencies = [ { ModId = "KSArmory" } ]`, and
   `EntryAssembly = "<your assembly>"` if your DLL is not named after your mod's Id.
2. From a `[StarMapBeforeMain]` method, call `KSArmory.Armoury.Register(xml, "MyWeaponPack")`,
   using your mod's Id as the second argument.

Because of that dependency, StarMap loads your mod after KSArmory, and KSArmory registers its own
weapons first, so `KSArmory:20MM` is already available when your code runs, unless the player has
switched that built-in family off.

If one text names something another declares, register them together so they are read as one
unit, the way a folder is:

```csharp
Armoury.Register([("rounds", roundsXml), ("launchers", launchersXml)], "MyWeaponPack");
```

Register by code or by folder, not both. If your mod has registered from code under its own Id,
KSArmory skips its `KSArmory/` folder and logs a line saying so.

`Register` takes XML text rather than C# objects, so your assembly needs only `KSArmory.dll` and
`StarMap.API.dll` to compile, and no KSA game assemblies. `Armoury.Schema` returns the format
version this KSArmory reads. `Register` returns a `PackResult` saying how many definitions
registered and why each refusal happened, and `Armoury.IsOpen` says whether there is still time to
register.

## What a pack cannot do

A pack can set every number for flight, warhead, sensor and optics, tube positions and directions,
magazine and belt, moving parts, and which round and sensor a launcher uses. It cannot add new
behaviour:

- New weapon kinds. Every weapon fires discrete rounds, except the laser beam.
- New guidance laws. A pack can tune the five that exist.
- New sight mechanisms beyond `Mast` and `RollNod`.
- Launcher movement other than traverse and then elevate. There is no way to describe a drum, a
  sliding rail, tubes that move on their own, or a radar aimed separately from the turret. The visual
  parts above (recoil, rams, feeds, rotary barrels, ejector arms, a spinning search array) and the
  sight and search-set heads are supported.
- Armour and penetration. Parts take damage individually and keep it between hits, but every part
  takes damage the same way. A pack can set the size of a charge, not what a part is made of.

These need changes to KSArmory itself. See [MODULARITY.md](MODULARITY.md),
[EXTENSIBILITY.md](EXTENSIBILITY.md) and, for porting from a KSP weapon mod,
[PACK-PORTING.md](PACK-PORTING.md).

KSA has no part modules, so a part cannot declare what it is. KSArmory recognises it by its part
Id, which is why the Ids in your asset files and your weapon file have to match.

## Not built yet

- There is no in-game list of refused definitions. They are only in the log.

For KSArmory's developers: [PACK-API-SURFACE.md](PACK-API-SURFACE.md) is this contract generated
from the code, and the build fails if it changes without being acknowledged, or if a table here
names an attribute the reader does not take or gives it the wrong Values. `Bodies.xml` is not in
it.
