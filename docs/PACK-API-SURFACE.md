# Pack API surface

Everything a KSArmory weapon pack binds to, read out of `Sim/PackReader.cs` and the entry
point by `tools/pack-api.py`. **Generated - do not edit.**

This is the checklist for changing KSArmory without breaking somebody else's mod: anything
here that changes shape is a breaking change for every pack, and anything not here cannot be.
A diff against this file is the only warning there is -- a pack lives in another repository,
never builds in CI, and an attribute this build stops knowing is refused by name rather than
ignored.

`docs/WEAPON-PACKS.md` is the same surface written for the author, with the reasons attached.

**Definition schema 1.** 7 elements, 110 attributes, 15 entry-point lines.

## Entry point

What a pack calls. It takes text rather than profiles so that a pack needs no KSA assemblies
to build; widening it to take a profile type would put that back.

### KSArmory.Armoury
- `static int Schema { get; }`
- `static bool IsOpen { get; }`
- `static PackResult Register(string definitions, string source)`
### KSArmory.PackResult
- `string Source`
- `int Registered`
- `IReadOnlyList<PackFault> Faults`
- `bool Complete { get; }`
### KSArmory.PackFault
- `string Source`
- `string Element`
- `string Name`
- `string Reason`
- `override string ToString()`

## Definition format

### `<Munition>` - how a round flies, and what it does on arrival

| Attribute | Reads | Default |
| --- | --- | --- |
| `Name` | text, required | **required** |
| `DisplayName` | text, required | **required** |
| `BodyMarker` | text | *none* |
| `FinMarker` | text | *none* |
| `BodyLength` | number | `3.10` |
| `FinDeploySeconds` | number | `0.18` |
| `FinDeflectionDeg` | number | `0` |
| `FinHingeStation` | number | `0` |
| `FinsPerRound` | whole number | `0` |
| `FinStowedScale` | number | `0.06` |
| `LaunchSpeed` | number | `45` |
| `BoostSeconds` | number | `2.4` |
| `BoostAccel` | number | `520` |
| `MaxFlightSeconds` | number | `30` |
| `MaxFaithfulStepSeconds` | number | *the flight model's own* |
| `MinRange` | number | `0` |
| `MaxRange` | number | `20000` |
| `NavConstant` | number | `4` |
| `MaxLateralG` | number | `35` |
| `Guidance` | one of `Seeker`, `AntiRadiation`, `CommandLink`, `Inertial`, `None` | `CommandLink` |
| `SeekerFovDeg` | number | `55` |
| `SeparationSeconds` | number | `0` |
| `GravityCompensation` | number | `1` |
| `NeutralDensityRatio` | number | `0` |
| `DragK` | number | `3.0e-5` |
| `FuseRadius` | number | `15` |
| `TimedFuse` | true or false | `false` |
| `FuseArmSeconds` | number | `0.6` |
| `ChargeKg` | number | `20` |
| `HitsTerrain` | true or false | `false` |

### `<Sensor>` - what a launcher can see

| Attribute | Reads | Default |
| --- | --- | --- |
| `Name` | text, required | **required** |
| `DisplayName` | text, required | **required** |
| `Range` | number | `36000` |
| `ConeDeg` | number | `90` |
| `BoresightSource` | one of `LocalUp`, `PartForward`, `TurretAxis`, `MountNormal` | `LocalUp` |
| `ThreatRadius` | number | `8000` |
| `ThreatHorizonSeconds` | number | `40` |
| `LockSeconds` | number | `1.5` |
| `MinTargetSpeed` | number | `15` |
| `Emits` | true or false | `false` |
| `ReferenceCrossSectionM2` | number | `0` |
| `NotchSpeed` | number | `0` |
| `ClutterFloorMetres` | number | `0` |
| `HorizonMasking` | true or false | `true` |
| `TerrainMarginMetres` | number | `0` |
| `TerrainSamples` | whole number | `0` |
| `TerrainClearanceMetres` | number | `30` |

### `<Launcher>` - the part, and what it does with the round

| Attribute | Reads | Default |
| --- | --- | --- |
| `PartId` | text, required | **required** |
| `DisplayName` | text, required | **required** |
| `Munition` | name, required | **required** |
| `Sensor` | name, required | **required** |
| `TubeArmamentLabel` | text | *none* |
| `GunArmamentLabel` | text | *none* |
| `TurretMarker` | text | *none* |
| `PodsMarker` | text | *none* |
| `RadarMarker` | text | *none* |
| `GunsMarker` | text | *none* |
| `OpticBaseMarker` | text | *none* |
| `TurretPivot` | three numbers | `0, 0, 0` |
| `PodPivotFromTurret` | three numbers | `0, 0, 0` |
| `RadarPivotFromTurret` | three numbers | `0, 0, 0` |
| `OpticBaseFromTurret` | three numbers | `0, 0, 0` |
| `GunPivotFromTurret` | three numbers | `0, 0, 0` |
| `GunReferenceElevationDeg` | degrees | `0.0` |
| `PodReferenceElevationDeg` | degrees | `0.0` |
| `MuzzleForwardOffset` | number | `0.0` |
| `TubeRingRadius` | number | `0.0` |
| `GunMunition` | name | *none* |
| `SlewRateDeg` | number | `70` |
| `ElevationRateDeg` | number | `45` |
| `SettleSeconds` | number | `0.35` |
| `SearchRadarRpm` | number | `20` |
| `SearchRadarFaces` | whole number | `1` |
| `MinElevationDeg` | number | `0` |
| `MaxElevationDeg` | number | `82` |
| `ForwardMinElevationDeg` | number | `15` |
| `ForwardArcDeg` | number | `80` |
| `ForwardPlateauDeg` | number | `62` |
| `RestElevationDeg` | number | *the modelled pose* |
| `MagazineDepth` | whole number | `0` |
| `SalvoSpacing` | number | `0.45` |
| `ReloadSeconds` | number | `12` |
| `LaunchAlongTube` | true or false | `true` |
| `LaunchLoft` | number | `0.35` |
| `EjectAwayFromMount` | number | `0` |
| `MuzzleOffset` | number | `8` |
| `GunAmmo` | whole number | `480` |
| `GunRoundsPerMinute` | number | `2500` |
| `GunBurstRounds` | whole number | `12` |
| `GunBurstGapSeconds` | number | `0.55` |
| `GunReloadSeconds` | number | `20` |

### `<Optic>` - a sighting head, needing no weapon on the craft

| Attribute | Reads | Default |
| --- | --- | --- |
| `PartId` | text, required | **required** |
| `DisplayName` | text, required | **required** |
| `Sensor` | name, required | **required** |
| `Gimbal` | one of `Mast`, `RollNod` | `Mast` |
| `BaseMarker` | text, required | **required** |
| `HeadMarker` | text, required | **required** |
| `RollMarker` | text | *none* |
| `HeadPivot` | three numbers | `0, 0, 0` |
| `EyeForward` | number | `0.30` |
| `SlewRateDeg` | number | `90` |
| `MinElevationDeg` | number | `-20` |
| `MaxElevationDeg` | number | `85` |
| `MaxOffBoresightDeg` | number | `135` |
| `KeyholeDeg` | number | `4` |

### `<Tube>` - child of `<Launcher>`

| Attribute | Reads | Default |
| --- | --- | --- |
| `Position` | three numbers | **required** |
| `Direction` | three numbers | `0, 0, 0` |

### `<Muzzle>` - child of `<Launcher>`

| Attribute | Reads | Default |
| --- | --- | --- |
| `At` | three numbers | **required** |

### `<Stage>` - child of `<Munition>`

| Attribute | Reads | Default |
| --- | --- | --- |
| `Seconds` | number | **required** |
| `Accel` | number | `0` |
