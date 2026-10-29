# Pack API surface

Everything a KSArmory weapon pack binds to, read out of `Sim/PackReader.cs` and the entry
point by `tools/pack-api.py`. **Generated - do not edit.**

This is the checklist for changing KSArmory without breaking somebody else's mod: anything
here that changes shape is a breaking change for every pack, and anything not here cannot be.
A diff against this file is the only warning there is -- a pack lives in another repository,
never builds in CI, and an attribute this build stops knowing is refused by name rather than
ignored.

`docs/WEAPON-PACKS.md` is the same surface written for the author, with the reasons attached.

**Definition schema 1.** 14 elements, 227 attributes, 15 entry-point lines.

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
| `Guidance` | one of `Seeker`, `AntiRadiation`, `CommandLink`, `Inertial`, `None` | `CommandLink` |
| `DisplayName` | text, required | **required** |
| `BodyMarker` | text | *none* |
| `BodyModel` | text | *none* |
| `BurstEffect` | text | *none* |
| `PlumeEffect` | text | *none* |
| `FinMarker` | text | *none* |
| `BodyLength` | number | `3.10` |
| `TracerEvery` | whole number | `0` |
| `TracerBurnSeconds` | number | `3` |
| `FinDeploySeconds` | number | `0.18` |
| `FinDeflectionDeg` | number | `0` |
| `FinHingeStation` | number | `0` |
| `FinsPerRound` | whole number | `0` |
| `FinStowedScale` | number | `0.06` |
| `LaunchSpeed` | number | `45` |
| `BoostSeconds` | number | `2.4`, or `0` for `None` and `Inertial` |
| `BoostAccel` | number | `520`, or `0` for `None` and `Inertial` |
| `MaxFlightSeconds` | number | `30` |
| `MaxFaithfulStepSeconds` | number | *the flight model's own* |
| `PreferredStepSeconds` | number | `0` |
| `SubStepSeconds` | number | `0` |
| `MinRange` | number | `0` |
| `MaxRange` | number | `20000` |
| `NavConstant` | number | `4` |
| `MaxLateralG` | number | `35` |
| `SeekerFovDeg` | number | `55` |
| `SeekerBand` | one of `Radar`, `Infrared`, `None` | `Radar` |
| `CountermeasureResistance` | number | `0` |
| `ReacquiresAfterDecoy` | true or false | `true` |
| `DopplerGateMps` | number | `0` |
| `SeparationSeconds` | number | `0` |
| `GravityCompensation` | number | `1` |
| `NeutralDensityRatio` | number | `0` |
| `MassKg` | number | `0` |
| `CalibreMm` | number | `0` |
| `DragCoefficient` | number | `0` |
| `DragK` | number | `3.0e-5` |
| `FuseRadius` | number | `15` |
| `TimedFuse` | true or false | `false` |
| `FuseArmSeconds` | number | `0.6` |
| `ChargeKg` | number | `20` |
| `MaxChargeKg` | number | `0` |
| `FragmentCount` | whole number | `0` |
| `FragmentMassGrams` | number | `5` |
| `FragmentSpeed` | number | `1800` |
| `SprayAngleDeg` | number | `90` |
| `SprayHalfWidthDeg` | number | `10` |
| `IgnitionChance` | number | `0` |
| `FragmentIgnitionChance` | number | `0` |
| `HitsTerrain` | true or false | `false` |
| `BurstHeightMetres` | number | `0` |
| `ChuteSinkMetresPerSecond` | number | `0` |
| `ChuteOpensSeconds` | number | `1.5` |

### `<Sensor>` - what a launcher can see

| Attribute | Reads | Default |
| --- | --- | --- |
| `Name` | text, required | **required** |
| `DisplayName` | text, required | **required** |
| `Range` | number | `36000` |
| `ConeDeg` | number | `90` |
| `BoresightSource` | one of `LocalUp`, `PartForward`, `TurretAxis`, `MountNormal` | `LocalUp` |
| `Scope` | one of `None`, `Search`, `Emitters` | `None` |
| `ThreatRadius` | number | `8000` |
| `ThreatHorizonSeconds` | number | `40` |
| `LockSeconds` | number | `1.5` |
| `MinTargetSpeed` | number | `15` |
| `Emits` | true or false | `false` |
| `ReferenceCrossSectionM2` | number | `0` |
| `NotchSpeed` | number | `0` |
| `ChaffNotchMps` | number | `0` |
| `ChaffReacquireSeconds` | number | `3` |
| `OpticalBackup` | true or false | `false` |
| `ClutterFloorMetres` | number | `0` |
| `HorizonMasking` | true or false | `true` |
| `TerrainMarginMetres` | number | `0` |
| `TerrainSamples` | whole number | `0` |
| `TerrainClearanceMetres` | number | `30` |
| `CuedOnly` | true or false | `false` |
| `Infrared` | true or false | `false` |
| `IrThresholdWPerM2` | number | `6.0e-7` |
| `LookDownPenalty` | number | `1` |

### `<Launcher>` - the part, and what it does with the round

| Attribute | Reads | Default |
| --- | --- | --- |
| `PartId` | text, required | **required** |
| `FiresBeam` | true or false | `false` |
| `DisplayName` | text, required | **required** |
| `Munition` | text | **required** |
| `BeamArmamentLabel` | text | `Laser` |
| `Sensor` | name, required | **required** |
| `TubeArmamentLabel` | text | `Missiles` |
| `GunArmamentLabel` | text | `Cannon` |
| `TurretMarker` | text | *none* |
| `PodsMarker` | text | *none* |
| `RadarMarker` | text | *none* |
| `GunsMarker` | text | *none* |
| `OpticBaseMarker` | text | *none* |
| `GunBarrelMarker` | text | *none* |
| `GunCylinderMarker` | text | *none* |
| `GunRodMarker` | text | *none* |
| `GunFeedMarker` | text | *none* |
| `GunRotorMarker` | text | *none* |
| `EjectorArmMarker` | text | *none* |
| `CarriageMarker` | text | *none* |
| `TurretPivot` | three numbers | `0, 0, 0` |
| `PodPivotFromTurret` | three numbers | `0, 0, 0` |
| `RadarPivotFromTurret` | three numbers | `0, 0, 0` |
| `OpticBaseFromTurret` | three numbers | `0, 0, 0` |
| `GunPivotFromTurret` | three numbers | `0, 0, 0` |
| `GunCylinderPinFromTurret` | three numbers | `0, 0, 0` |
| `GunRodPinFromTurret` | three numbers | `0, 0, 0` |
| `GunFeedPivotFromTurret` | three numbers | `0, 0, 0` |
| `GunFeedRatio` | number | `0.0` |
| `GunRotorPivotFromTurret` | three numbers | `0, 0, 0` |
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
| `TraverseLimitDeg` | number | `180` |
| `MagazineDepth` | whole number | `0` |
| `SalvoSpacing` | number | `0.45` |
| `ReloadSeconds` | number | `0` |
| `LaunchAlongTube` | true or false | `true` |
| `LaunchLoft` | number | `0.35` |
| `EjectAwayFromMount` | number | `0` |
| `EjectSpeed` | number | `0` |
| `MotorDelaySeconds` | number | `0` |
| `EjectorStrokeMetres` | number | `0` |
| `EjectorReturnSeconds` | number | `0.8` |
| `MuzzleOffset` | number | `8` |
| `GunAmmo` | whole number | `480` |
| `StoreMassKg` | number | `0` |
| `GunRoundMassKg` | number | `0` |
| `GunRoundsPerMinute` | number | `2500` |
| `GunBurstRounds` | whole number | `12` |
| `GunBurstGapSeconds` | number | `0.55` |
| `GunReloadSeconds` | number | `0` |
| `GunRecoilMetres` | number | `0` |
| `GunRecoilSeconds` | number | `0.08` |
| `GunReturnSeconds` | number | `0.6` |
| `GunRotorSpinUpSeconds` | number | `0.2` |
| `GunRotorSpinDownSeconds` | number | `1.0` |
| `GunSoundId` | text | *none* |
| `GunshotSoundId` | text | *none* |
| `MuzzleFlashEffect` | text | *none* |
| `TrimAccelerationMetresPerSecond2` | number | `double.NaN` |
| `TrimWorstAxisExhaustVelocity` | number | `double.NaN` |
| `HopEngineThrustNewtons` | number | `double.NaN` |
| `HopEngineExhaustVelocity` | number | `double.NaN` |
| `HopEngineAccelerationMetresPerSecond2` | number | `double.NaN` |
| `FireControlLabel` | text | *none* |
| `MintRows` | true or false | `true` |

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
| `StowTuckDeg` | number | `0` |
| `CarriageMarker` | text | *none* |
| `MaxMagnification` | number | `16` |
| `HasLaser` | true or false | `false` |
| `LaserRangeMetres` | number | `20000` |

### `<SearchSet>` - a part that only searches

| Attribute | Reads | Default |
| --- | --- | --- |
| `PartId` | text, required | **required** |
| `DisplayName` | text, required | **required** |
| `Sensor` | name, required | **required** |
| `HeadMarker` | text | *none* |
| `HeadPivot` | three numbers | `0, 0, 0` |
| `HeadTravelDeg` | number | `60` |
| `HeadSlewDegPerSecond` | number | `90` |
| `ScanHalfWidthDeg` | number | `45` |
| `ScanPeriodSeconds` | number | `6` |
| `ScanNodDeg` | number | `8` |
| `PanelName` | text | `set.DisplayName` |

### `<Decoy>` - a flare or a chaff cloud

| Attribute | Reads | Default |
| --- | --- | --- |
| `Name` | text, required | **required** |
| `DisplayName` | text, required | **required** |
| `Kind` | one of `Flare`, `Chaff` | **required** |
| `MassKg` | number | `0` |
| `PeakSignature` | number | `0` |
| `RiseSeconds` | number | `0` |
| `LifeSeconds` | number | `4` |
| `DragPerMetre` | number | `0` |

### `<Dispenser>` - a part that throws decoys

| Attribute | Reads | Default |
| --- | --- | --- |
| `PartId` | text, required | **required** |
| `Decoy` | name, required | **required** |
| `Kind` | one of `Flare`, `Chaff` | *worked out from the rest of the definition* |
| `DisplayName` | text, required | **required** |
| `Count` | whole number | `30` |
| `SalvoSize` | whole number | `2` |
| `SalvoIntervalSeconds` | number | `0.25` |
| `EjectDirection` | three numbers | `new(1` |
| `EjectSpeed` | number | `30` |

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

### `<Provides>` - child of `<Launcher>`

| Attribute | Reads | Default |
| --- | --- | --- |
| `Role` | one of `FireControl`, `Launcher`, `Sensor`, `Camera`, `Gun`, `Guidance`, `Countermeasures` | `Sensor` |
| `DisplayName` | text, required | **required** |

### `<Beam>` - child of `<Optic>`

| Attribute | Reads | Default |
| --- | --- | --- |
| `PowerWatts` | number | `1.0e6` |
| `WavelengthMetres` | number | `1.315e-6` |
| `ApertureMetres` | number | `1.5` |
| `JitterRad` | number | `0.2e-6` |
| `BurnThroughJoulesPerM2` | number | `6.0e7` |
| `ExtinctionPerMetre` | number | `2.0e-5` |
| `ChemicalSeconds` | number | `100` |
| `ShotSeconds` | number | `5` |

### `<ReleaseSpin>` - child of `<Launcher>`

| Attribute | Reads | Default |
| --- | --- | --- |
| `RateDegPerSecond` | number | **required** |
| `SpinUpSeconds` | number | **required** |
| `IgnitionSeconds` | number | `0.0` |

### `<Nozzle>` - child of `<ReleaseSpin>`

| Attribute | Reads | Default |
| --- | --- | --- |
| `Mouth` | three numbers | **required** |
| `Exhaust` | three numbers | **required** |
