using System.Globalization;
using System.Xml.Linq;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Turns a pack's weapon definitions into profiles, or into the reasons they were refused.
///
/// <para>Text in, profiles out, and no file access: what to read is the caller's problem, which
/// is what keeps the whole of this — every rejection, every default, every parse — reachable from
/// a test with no game running. The registries it resolves references against arrive as
/// arguments for the same reason.</para>
///
/// <para><b>Numbers are read in the invariant culture, always.</b> A machine with a comma decimal
/// separator otherwise reads <c>2.4</c> as 24, which is a round with ten times its boost and
/// nothing anywhere to say so.</para>
/// </summary>
public static class PackReader
{
    /// <summary>
    /// The definition format this build understands. A file declaring a higher one is refused
    /// whole rather than read as far as it goes: reading a file written against a contract we do
    /// not have is how a weapon flies with half its fields silently defaulted.
    /// </summary>
    public const int Schema = 1;

    /// <summary>
    /// The qualifier a pack uses to name one of the built-ins — <c>KSArmory:30MM</c>. Built-ins
    /// carry bare keys, so this is the one prefix that is stripped rather than kept.
    /// </summary>
    public const string BuiltInSource = "KSArmory";

    public static PackContents Read(
        string definitions,
        string source,
        IReadOnlyList<MunitionProfile> knownMunitions,
        IReadOnlyList<SensorProfile> knownSensors,
        IReadOnlyList<DecoyProfile>? knownDecoys = null)
    {
        knownDecoys ??= [];
        List<PackFault> faults = [];

        XElement? root;
        try
        {
            root = XDocument.Parse(definitions).Root;
        }
        catch (System.Xml.XmlException e)
        {
            return Refused(source, faults, $"not readable as XML: {e.Message}");
        }

        if (root is null || root.Name.LocalName != "WeaponPack")
        {
            return Refused(source, faults, $"root element is <{root?.Name.LocalName ?? "nothing"}>, expected <WeaponPack>");
        }

        string? declared = root.Attribute("Schema")?.Value;
        if (declared is null)
        {
            return Refused(source, faults, $"<WeaponPack> declares no Schema; this build reads {Schema}");
        }

        if (!int.TryParse(declared, NumberStyles.Integer, CultureInfo.InvariantCulture, out int schema))
        {
            return Refused(source, faults, $"Schema=\"{declared}\" is not a number");
        }

        if (schema != Schema)
        {
            return Refused(source, faults,
                           $"written for schema {schema}; this build reads {Schema}. "
                           + (schema > Schema ? "Update KSArmory." : "Update the pack."));
        }

        // Family is how KSArmory's own files are switched off one at a time; a pack is switched off by
        // uninstalling it, so on a pack's root it means nothing and is refused.
        string? family = null;
        foreach (XAttribute a in root.Attributes())
        {
            if (a.IsNamespaceDeclaration) continue;
            string name = a.Name.LocalName;
            if (name == "Schema") continue;
            if (name == "Family" && source == BuiltInSource) { family = a.Value; continue; }

            faults.Add(new PackFault(source, "WeaponPack", "",
                                     name == "Family"
                                         ? "Family belongs to KSArmory's own files; a pack is switched off by uninstalling it"
                                         : $"'{name}' is not an attribute this build knows"));
        }

        // Rounds and sensors first, because a launcher names them and the reference is resolved
        // here rather than left to fail in flight.
        List<MunitionProfile> munitions = [];
        List<SensorProfile> sensors = [];
        List<LauncherProfile> launchers = [];
        List<OpticProfile> optics = [];
        List<SearchSetProfile> searchSets = [];
        List<DecoyProfile> decoys = [];
        List<DispenserProfile> dispensers = [];
        List<ComponentProfile> components = [];
        List<ComponentProfile> partRows = [];

        foreach (XElement el in root.Elements())
        {
            switch (el.Name.LocalName)
            {
                case "Munition": ReadMunition(el, source, faults, munitions); break;
                case "Sensor": ReadSensor(el, source, faults, sensors); break;
                case "Decoy": ReadDecoy(el, source, faults, decoys); break;
                case "Launcher" or "Optic" or "SearchSet" or "Dispenser": break;
                default:
                    faults.Add(new PackFault(source, el.Name.LocalName, "",
                                             "not a definition this build knows"));
                    break;
            }
        }

        foreach (XElement el in root.Elements())
        {
            switch (el.Name.LocalName)
            {
                case "Launcher":
                    ReadLauncher(el, source, faults, munitions, sensors,
                                 knownMunitions, knownSensors, launchers, components);
                    break;
                case "Optic":
                    ReadOptic(el, source, faults, sensors, knownSensors, optics);
                    break;
                case "SearchSet":
                    ReadSearchSet(el, source, faults, sensors, knownSensors, searchSets, partRows);
                    break;
                case "Dispenser":
                    ReadDispenser(el, source, faults, decoys, knownDecoys, dispensers, partRows);
                    break;
            }
        }

        // The director on its part is what aims and fires the beam, so one with no beam has nothing to fire.
        foreach (LauncherProfile laser in launchers.Where(l => l.FiresBeam).ToList())
        {
            if (optics.Exists(o => o.PartId == laser.PartId && o.Beam is not null)) continue;

            faults.Add(new PackFault(source, "Launcher", laser.PartId,
                                     "fires a beam, but no <Optic> in this pack on its part carries a <Beam>"));
            launchers.Remove(laser);
            components.RemoveAll(c => c.PartId == laser.PartId && c.Role == WeaponRole.Launcher);
        }

        // A director on a launcher's own part is that launcher's row, declared with <Provides>; one
        // standing alone is a part the survey has to recognise by itself.
        foreach (OpticProfile head in optics)
        {
            if (launchers.Exists(l => l.PartId == head.PartId)) continue;
            components.Add(new ComponentProfile
            {
                PartId = head.PartId,
                Role = WeaponRole.Camera,
                DisplayName = head.DisplayName,
            });
        }

        components.AddRange(partRows);

        return new PackContents
        {
            Source = source,
            Family = family,
            Munitions = munitions,
            Sensors = sensors,
            Launchers = launchers,
            Optics = optics,
            SearchSets = searchSets,
            Decoys = decoys,
            Dispensers = dispensers,
            Components = components,
            Faults = faults,
        };
    }

    private static PackContents Refused(string source, List<PackFault> faults, string reason)
    {
        faults.Add(new PackFault(source, "WeaponPack", "", reason));
        return new PackContents { Source = source, Faults = faults };
    }

    private static void ReadMunition(XElement el, string source, List<PackFault> faults,
                                     List<MunitionProfile> into)
    {
        Reader r = new(el, source, "Munition", faults);
        string name = r.Required("Name");
        r.Describe(name);

        // A released round carries a motor only when it says so: a shell or a bomb that never mentions
        // one must not be handed the 57E6's.
        GuidanceMode guidance = r.Choice("Guidance", GuidanceMode.CommandLink);
        bool released = guidance is GuidanceMode.None or GuidanceMode.Inertial;

        MunitionProfile round = new()
        {
            Name = Qualify(source, name),
            DisplayName = r.Required("DisplayName"),
            BodyMarker = r.Text("BodyMarker"),
            BodyModel = r.Text("BodyModel"),
            BurstEffect = r.Text("BurstEffect"),
            PlumeEffect = r.Text("PlumeEffect"),
            FinMarker = r.Text("FinMarker"),

            BodyLength = r.Number("BodyLength", 3.10f),
            TracerEvery = r.Count("TracerEvery", 0),
            TracerBurnSeconds = r.Number("TracerBurnSeconds", 3f),
            FinDeploySeconds = r.Number("FinDeploySeconds", 0.18f),
            FinDeflectionDeg = r.Number("FinDeflectionDeg", 0f),
            FinHingeStation = r.Number("FinHingeStation", 0f),
            FinsPerRound = r.Count("FinsPerRound", 0),
            FinStowedScale = r.Number("FinStowedScale", 0.06f),

            LaunchSpeed = r.Number("LaunchSpeed", 45f),
            BoostSeconds = r.Number("BoostSeconds", released ? 0f : 2.4f),
            BoostAccel = r.Number("BoostAccel", released ? 0f : 520f),
            MaxFlightSeconds = r.Number("MaxFlightSeconds", 30f),
            MaxFaithfulStepSeconds = r.Number("MaxFaithfulStepSeconds", (float)Interceptor.MaxFaithfulStep),
            PreferredStepSeconds = r.Number("PreferredStepSeconds", 0f),
            SubStepSeconds = r.Number("SubStepSeconds", 0f),
            MinRange = r.Number("MinRange", 0f),
            MaxRange = r.Number("MaxRange", 20000f),

            NavConstant = r.Number("NavConstant", 4f),
            MaxLateralG = r.Number("MaxLateralG", 35f),
            Guidance = guidance,
            SeekerFovDeg = r.Number("SeekerFovDeg", 55f),
            Band = r.Choice("SeekerBand", SeekerBand.Radar),
            CountermeasureResistance = r.Number("CountermeasureResistance", 0f),
            ReacquiresAfterDecoy = r.Flag("ReacquiresAfterDecoy", true),
            DopplerGateMps = r.Number("DopplerGateMps", 0f),
            SeparationSeconds = r.Number("SeparationSeconds", 0f),
            GravityCompensation = r.Number("GravityCompensation", 1f),
            NeutralDensityRatio = r.Number("NeutralDensityRatio", 0f),
            MassKg = r.Number("MassKg", 0f),
            CalibreMm = r.Number("CalibreMm", 0f),
            DragCoefficient = r.Number("DragCoefficient", 0f),
            DragK = r.Number("DragK", 3.0e-5f),

            FuseRadius = r.Number("FuseRadius", 15f),
            TimedFuse = r.Flag("TimedFuse", false),
            FuseArmSeconds = r.Number("FuseArmSeconds", 0.6f),
            ChargeKg = r.Number("ChargeKg", 20f),
            MaxChargeKg = r.Number("MaxChargeKg", 0f),
            FragmentCount = r.Count("FragmentCount", 0),
            FragmentMassGrams = r.Number("FragmentMassGrams", 5f),
            FragmentSpeed = r.Number("FragmentSpeed", 1800f),
            SprayAngleDeg = r.Number("SprayAngleDeg", 90f),
            SprayHalfWidthDeg = r.Number("SprayHalfWidthDeg", 10f),
            IgnitionChance = r.Number("IgnitionChance", 0f),
            HitsTerrain = r.Flag("HitsTerrain", false),
            BurstHeightMetres = r.Number("BurstHeightMetres", 0f),
            ChuteSinkMetresPerSecond = r.Number("ChuteSinkMetresPerSecond", 0f),
            ChuteOpensSeconds = r.Number("ChuteOpensSeconds", 1.5f),

            Stages = r.Stages(),
        };

        // A round given part of what its drag is computed from would fly on DragK without a word.
        int shape = (round.MassKg > 0f ? 1 : 0) + (round.CalibreMm > 0f ? 1 : 0) + (round.DragCoefficient > 0f ? 1 : 0);
        if (shape is 1 or 2) r.Fault("MassKg, CalibreMm and DragCoefficient go together: give all three, or none and DragK");

        if (round.CountermeasureResistance is < 0f or > 1f) r.Fault("CountermeasureResistance runs from 0 to 1");

        // Both are measured against the ground under the round, which only a round the ground stops has.
        if ((round.BurstHeightMetres > 0f || round.HasChute) && !round.HitsTerrain)
        {
            r.Fault("BurstHeightMetres and ChuteSinkMetresPerSecond need HitsTerrain=\"true\"");
        }

        if (!r.Sound()) return;
        if (Duplicate(round.Name, into, m => m.Name, r)) return;

        into.Add(round);
    }

    private static void ReadSensor(XElement el, string source, List<PackFault> faults,
                                   List<SensorProfile> into)
    {
        Reader r = new(el, source, "Sensor", faults);
        string name = r.Required("Name");
        r.Describe(name);

        SensorProfile set = new()
        {
            Name = Qualify(source, name),
            DisplayName = r.Required("DisplayName"),

            Range = r.Number("Range", 36000f),
            ConeDeg = r.Number("ConeDeg", 90f),
            BoresightSource = r.Choice("BoresightSource", BoresightMode.LocalUp),
            Scope = r.Choice("Scope", ScopePresentation.None),
            ThreatRadius = r.Number("ThreatRadius", 8000f),
            ThreatHorizonSeconds = r.Number("ThreatHorizonSeconds", 40f),
            LockSeconds = r.Number("LockSeconds", 1.5f),
            MinTargetSpeed = r.Number("MinTargetSpeed", 15f),
            Emits = r.Flag("Emits", false),

            ReferenceCrossSectionM2 = r.Number("ReferenceCrossSectionM2", 0f),
            NotchSpeed = r.Number("NotchSpeed", 0f),
            ChaffNotchMps = r.Number("ChaffNotchMps", 0f),
            ChaffReacquireSeconds = r.Number("ChaffReacquireSeconds", 3f),
            OpticalBackup = r.Flag("OpticalBackup", false),
            ClutterFloorMetres = r.Number("ClutterFloorMetres", 0f),
            HorizonMasking = r.Flag("HorizonMasking", true),
            TerrainMarginMetres = r.Number("TerrainMarginMetres", 0f),
            TerrainSamples = r.Count("TerrainSamples", 0),
            TerrainClearanceMetres = r.Number("TerrainClearanceMetres", 30f),

            CuedOnly = r.Flag("CuedOnly", false),
            Infrared = r.Flag("Infrared", false),
            IrThresholdWPerM2 = r.Number("IrThresholdWPerM2", 6.0e-7f),
            LookDownPenalty = r.Number("LookDownPenalty", 1f),
        };

        if (!r.Sound()) return;
        if (Duplicate(set.Name, into, s => s.Name, r)) return;

        into.Add(set);
    }

    private static void ReadOptic(XElement el, string source, List<PackFault> faults,
                                  List<SensorProfile> own, IReadOnlyList<SensorProfile> known,
                                  List<OpticProfile> into)
    {
        Reader r = new(el, source, "Optic", faults);
        string partId = r.Required("PartId");
        r.Describe(partId);

        OpticProfile head = new()
        {
            PartId = partId,
            DisplayName = r.Required("DisplayName"),
            Sensor = r.Reference("Sensor", own, known, s => s.Name),
            Gimbal = r.Choice("Gimbal", GimbalKind.Mast),
            BaseMarker = r.Required("BaseMarker"),
            HeadMarker = r.Required("HeadMarker"),
            RollMarker = r.Text("RollMarker"),
            HeadPivot = r.Vector("HeadPivot", default),

            EyeForward = r.Number("EyeForward", 0.30f),
            SlewRateDeg = r.Number("SlewRateDeg", 90f),
            MinElevationDeg = r.Number("MinElevationDeg", -20f),
            MaxElevationDeg = r.Number("MaxElevationDeg", 85f),
            MaxOffBoresightDeg = r.Number("MaxOffBoresightDeg", 135f),
            KeyholeDeg = r.Number("KeyholeDeg", 4f),
            StowTuckDeg = r.Number("StowTuckDeg", 0f),
            CarriageMarker = r.Text("CarriageMarker"),
            MaxMagnification = r.Number("MaxMagnification", 16f),
            HasLaser = r.Flag("HasLaser", false),
            LaserRangeMetres = r.Number("LaserRangeMetres", 20000f),
            Beam = r.Beam(),
        };

        // A roll-nod head with no roll body cannot show what it is doing; a mast head having
        // none is the ordinary case, not an omission.
        if (head.Gimbal == GimbalKind.RollNod && head.RollMarker is null)
        {
            r.Fault("a RollNod head needs a RollMarker naming the body that rolls");
        }

        if (!r.Sound()) return;
        if (Duplicate(head.PartId, into, o => o.PartId, r)) return;

        into.Add(head);
    }

    private static void ReadDecoy(XElement el, string source, List<PackFault> faults, List<DecoyProfile> into)
    {
        Reader r = new(el, source, "Decoy", faults);
        string name = r.Required("Name");
        r.Describe(name);

        DecoyProfile decoy = new()
        {
            Name = Qualify(source, name),
            DisplayName = r.Required("DisplayName"),
            Kind = r.Choice("Kind", DecoyKind.Flare),
            MassKg = r.Number("MassKg", 0f),
            PeakSignature = r.Number("PeakSignature", 0f),
            RiseSeconds = r.Number("RiseSeconds", 0f),
            LifeSeconds = r.Number("LifeSeconds", 4f),
            DragPerMetre = r.Number("DragPerMetre", 0f),
        };
        if (el.Attribute("Kind") is null) r.Fault("needs a Kind: Flare or Chaff");

        if (!r.Sound()) return;
        if (Duplicate(decoy.Name, into, d => d.Name, r)) return;

        into.Add(decoy);
    }

    // A dispenser's kind is its decoy's; stating it is allowed, and refused when the two disagree.
    private static void ReadDispenser(XElement el, string source, List<PackFault> faults,
                                      List<DecoyProfile> own, IReadOnlyList<DecoyProfile> known,
                                      List<DispenserProfile> into, List<ComponentProfile> rows)
    {
        Reader r = new(el, source, "Dispenser", faults);
        string partId = r.Required("PartId");
        r.Describe(partId);

        string decoy = r.Reference("Decoy", own, known, d => d.Name);
        DecoyKind loaded = Resolved(decoy, own, known, d => d.Name)?.Kind ?? DecoyKind.Flare;
        DecoyKind kind = r.Choice("Kind", loaded);
        if (kind != loaded) r.Fault($"Kind=\"{kind}\" but its Decoy is a {loaded}");

        DispenserProfile dispenser = new()
        {
            PartId = partId,
            DisplayName = r.Required("DisplayName"),
            Kind = kind,
            Decoy = decoy,
            Count = r.Count("Count", 30),
            SalvoSize = r.Count("SalvoSize", 2),
            SalvoIntervalSeconds = r.Number("SalvoIntervalSeconds", 0.25f),
            EjectDirection = r.Vector("EjectDirection", new(1, 0, 0)),
            EjectSpeed = r.Number("EjectSpeed", 30f),
        };

        if (!r.Sound()) return;
        if (Duplicate(dispenser.PartId, into, d => d.PartId, r)) return;

        into.Add(dispenser);
        rows.Add(new ComponentProfile
        {
            PartId = dispenser.PartId,
            Role = WeaponRole.Countermeasures,
            DisplayName = dispenser.DisplayName,
        });
    }

    private static void ReadSearchSet(XElement el, string source, List<PackFault> faults,
                                      List<SensorProfile> own, IReadOnlyList<SensorProfile> known,
                                      List<SearchSetProfile> into, List<ComponentProfile> rows)
    {
        Reader r = new(el, source, "SearchSet", faults);
        string partId = r.Required("PartId");
        r.Describe(partId);

        SearchSetProfile set = new()
        {
            PartId = partId,
            DisplayName = r.Required("DisplayName"),
            Sensor = r.Reference("Sensor", own, known, s => s.Name),
            HeadMarker = r.Text("HeadMarker"),
            HeadPivot = r.Vector("HeadPivot", default),
            HeadTravelDeg = r.Number("HeadTravelDeg", 60f),
            HeadSlewDegPerSecond = r.Number("HeadSlewDegPerSecond", 90f),
            ScanHalfWidthDeg = r.Number("ScanHalfWidthDeg", 45f),
            ScanPeriodSeconds = r.Number("ScanPeriodSeconds", 6f),
            ScanNodDeg = r.Number("ScanNodDeg", 8f),
        };
        string row = r.Text("PanelName") ?? set.DisplayName;

        if (!r.Sound()) return;
        if (Duplicate(set.PartId, into, x => x.PartId, r)) return;

        into.Add(set);
        rows.Add(new ComponentProfile { PartId = set.PartId, Role = WeaponRole.Sensor, DisplayName = row });
    }

    private static void ReadLauncher(XElement el, string source, List<PackFault> faults,
                                     List<MunitionProfile> ownRounds, List<SensorProfile> ownSets,
                                     IReadOnlyList<MunitionProfile> knownRounds,
                                     IReadOnlyList<SensorProfile> knownSets,
                                     List<LauncherProfile> into, List<ComponentProfile> components)
    {
        Reader r = new(el, source, "Launcher", faults);
        string partId = r.Required("PartId");
        r.Describe(partId);

        // A beam weapon throws no round, and an empty name is what loadout resolution reads as none.
        bool firesBeam = r.Flag("FiresBeam", false);

        LauncherProfile launcher = new()
        {
            PartId = partId,
            DisplayName = r.Required("DisplayName"),
            Munition = firesBeam && el.Attribute("Munition") is null
                           ? r.Text("Munition") ?? ""
                           : r.Reference("Munition", ownRounds, knownRounds, m => m.Name),
            FiresBeam = firesBeam,
            BeamArmamentLabel = r.Text("BeamArmamentLabel") ?? "Laser",
            Sensor = r.Reference("Sensor", ownSets, knownSets, s => s.Name),

            TubeArmamentLabel = r.Text("TubeArmamentLabel") ?? "Missiles",
            GunArmamentLabel = r.Text("GunArmamentLabel") ?? "Cannon",

            TurretMarker = r.Text("TurretMarker"),
            PodsMarker = r.Text("PodsMarker"),
            RadarMarker = r.Text("RadarMarker"),
            GunsMarker = r.Text("GunsMarker"),
            OpticBaseMarker = r.Text("OpticBaseMarker"),
            GunBarrelMarker = r.Text("GunBarrelMarker"),
            GunCylinderMarker = r.Text("GunCylinderMarker"),
            GunRodMarker = r.Text("GunRodMarker"),
            GunFeedMarker = r.Text("GunFeedMarker"),
            GunRotorMarker = r.Text("GunRotorMarker"),
            EjectorArmMarker = r.Text("EjectorArmMarker"),
            CarriageMarker = r.Text("CarriageMarker"),

            Tubes = r.Tubes(),
            TurretPivot = r.Vector("TurretPivot", default),
            PodPivotFromTurret = r.Vector("PodPivotFromTurret", default),
            RadarPivotFromTurret = r.Vector("RadarPivotFromTurret", default),
            OpticBaseFromTurret = r.Vector("OpticBaseFromTurret", default),
            GunPivotFromTurret = r.Vector("GunPivotFromTurret", default),
            GunCylinderPinFromTurret = r.Vector("GunCylinderPinFromTurret", default),
            GunRodPinFromTurret = r.Vector("GunRodPinFromTurret", default),
            GunFeedPivotFromTurret = r.Vector("GunFeedPivotFromTurret", default),
            GunFeedRatio = r.Number("GunFeedRatio", 0.0),
            GunRotorPivotFromTurret = r.Vector("GunRotorPivotFromTurret", default),
            GunReferenceElevationRad = r.Angle("GunReferenceElevationDeg", 0.0),
            PodReferenceElevationRad = r.Angle("PodReferenceElevationDeg", 0.0),
            MuzzleForwardOffset = r.Number("MuzzleForwardOffset", 0.0),
            TubeRingRadius = r.Number("TubeRingRadius", 0.0),

            GunMunition = r.OptionalReference("GunMunition", ownRounds, knownRounds, m => m.Name),
            GunMuzzles = r.Muzzles(),

            SlewRateDeg = r.Number("SlewRateDeg", 70f),
            ElevationRateDeg = r.Number("ElevationRateDeg", 45f),
            SettleSeconds = r.Number("SettleSeconds", 0.35f),
            SearchRadarRpm = r.Number("SearchRadarRpm", 20f),
            SearchRadarFaces = r.Count("SearchRadarFaces", 1),

            MinElevationDeg = r.Number("MinElevationDeg", 0f),
            MaxElevationDeg = r.Number("MaxElevationDeg", 82f),
            ForwardMinElevationDeg = r.Number("ForwardMinElevationDeg", 15f),
            ForwardArcDeg = r.Number("ForwardArcDeg", 80f),
            ForwardPlateauDeg = r.Number("ForwardPlateauDeg", 62f),
            RestElevationDeg = r.Number("RestElevationDeg", float.NaN),
            TraverseLimitDeg = r.Number("TraverseLimitDeg", 180f),

            MagazineDepth = r.Count("MagazineDepth", 0),
            SalvoSpacing = r.Number("SalvoSpacing", 0.45f),
            ReloadSeconds = r.Number("ReloadSeconds", 0f),
            LaunchAlongTube = r.Flag("LaunchAlongTube", true),
            LaunchLoft = r.Number("LaunchLoft", 0.35f),
            EjectAwayFromMount = r.Number("EjectAwayFromMount", 0f),
            EjectSpeed = r.Number("EjectSpeed", 0f),
            MotorDelaySeconds = r.Number("MotorDelaySeconds", 0f),
            EjectorStrokeMetres = r.Number("EjectorStrokeMetres", 0f),
            EjectorReturnSeconds = r.Number("EjectorReturnSeconds", 0.8f),
            MuzzleOffset = r.Number("MuzzleOffset", 8f),

            GunAmmo = r.Count("GunAmmo", 480),
            StoreMassKg = r.Number("StoreMassKg", 0f),
            GunRoundMassKg = r.Number("GunRoundMassKg", 0f),
            GunRoundsPerMinute = r.Number("GunRoundsPerMinute", 2500f),
            GunBurstRounds = r.Count("GunBurstRounds", 12),
            GunBurstGapSeconds = r.Number("GunBurstGapSeconds", 0.55f),
            GunReloadSeconds = r.Number("GunReloadSeconds", 0f),
            GunRecoilMetres = r.Number("GunRecoilMetres", 0f),
            GunRecoilSeconds = r.Number("GunRecoilSeconds", 0.08f),
            GunReturnSeconds = r.Number("GunReturnSeconds", 0.6f),
            GunRotorSpinUpSeconds = r.Number("GunRotorSpinUpSeconds", 0.2f),
            GunRotorSpinDownSeconds = r.Number("GunRotorSpinDownSeconds", 1.0f),
            GunSoundId = r.Text("GunSoundId"),
            GunshotSoundId = r.Text("GunshotSoundId"),
            MuzzleFlashEffect = r.Text("MuzzleFlashEffect"),

            TrimAccelerationMetresPerSecond2 = r.Number("TrimAccelerationMetresPerSecond2", double.NaN),
            TrimWorstAxisExhaustVelocity = r.Number("TrimWorstAxisExhaustVelocity", double.NaN),
            HopEngineThrustNewtons = r.Number("HopEngineThrustNewtons", double.NaN),
            HopEngineExhaustVelocity = r.Number("HopEngineExhaustVelocity", double.NaN),
            HopEngineAccelerationMetresPerSecond2 = r.Number("HopEngineAccelerationMetresPerSecond2", double.NaN),
            ReleaseSpin = r.ReleaseSpin(),
        };

        List<BuiltInComponent> declared = r.Provides();
        string fireControl = r.Text("FireControlLabel") ?? $"{launcher.DisplayName} fire control";
        bool mintRows = r.Flag("MintRows", true);

        // A launcher that can shoot with nothing is a part fire control adopts and then holds
        // fire on for ever, with no gate reporting why.
        if (launcher.TubeCount == 0 && !launcher.HasCannon && !launcher.FiresBeam)
        {
            r.Fault("declares no Tube, no GunMunition and no FiresBeam, so it cannot shoot with anything");
        }

        if (launcher.EjectSpeed < 0f) r.Fault("EjectSpeed is a speed out of the mounting face; it cannot be negative");
        if (launcher.MotorDelaySeconds < 0f) r.Fault("MotorDelaySeconds cannot be negative");

        // Elevation gear with no trunnion offset swings the assembly about the turret's own
        // centre, which reads as a pod orbiting the vehicle.
        if (launcher.TurretMarker is not null)
        {
            if (launcher.PodsMarker is null && launcher.GunsMarker is null)
            {
                r.Fault("declares a TurretMarker but nothing that rides it");
            }

            if (launcher.PodsMarker is not null && Vec.Len(launcher.PodPivotFromTurret) <= 0.0)
            {
                r.Fault("declares a PodsMarker with no PodPivotFromTurret to elevate about");
            }

            if (launcher.GunsMarker is not null && Vec.Len(launcher.GunPivotFromTurret) <= 0.0)
            {
                r.Fault("declares a GunsMarker with no GunPivotFromTurret to elevate about");
            }
        }

        // A drawn assembly with nowhere to hinge spins about the turret's own centre.
        foreach ((string? marker, double3 pin, string names) in new[]
                 {
                     (launcher.GunCylinderMarker, launcher.GunCylinderPinFromTurret, "GunCylinderMarker with no GunCylinderPinFromTurret"),
                     (launcher.GunRodMarker, launcher.GunRodPinFromTurret, "GunRodMarker with no GunRodPinFromTurret"),
                     (launcher.GunFeedMarker, launcher.GunFeedPivotFromTurret, "GunFeedMarker with no GunFeedPivotFromTurret"),
                     (launcher.GunRotorMarker, launcher.GunRotorPivotFromTurret, "GunRotorMarker with no GunRotorPivotFromTurret"),
                 })
        {
            if (marker is not null && Vec.Len(pin) <= 0.0) r.Fault($"declares a {names} to turn about");
        }

        if (!r.Sound()) return;
        if (Duplicate(launcher.PartId, into, l => l.PartId, r)) return;

        into.Add(launcher);
        components.Add(mintRows
                           ? ComponentFor(launcher, fireControl, declared, ownRounds, ownSets, knownRounds, knownSets)
                           : new ComponentProfile
                           {
                               PartId = launcher.PartId,
                               Role = WeaponRole.Launcher,
                               DisplayName = launcher.DisplayName,
                               Provides = declared,
                           });
    }

    // The component row a launcher implies. Minted rather than asked for: the two registries have
    // to agree, and a launcher missing from this one is recognised by fire control and invisible to
    // the panel, which looks exactly like a part that never loaded.
    private static ComponentProfile ComponentFor(
        LauncherProfile launcher, string fireControl, List<BuiltInComponent> declared,
        List<MunitionProfile> ownRounds, List<SensorProfile> ownSets,
        IReadOnlyList<MunitionProfile> knownRounds, IReadOnlyList<SensorProfile> knownSets)
    {
        List<BuiltInComponent> provides =
        [
            new(WeaponRole.Sensor, Resolved(launcher.Sensor, ownSets, knownSets, s => s.Name)?.DisplayName ?? launcher.Sensor),
        ];

        if (launcher.GunMunition is { } shell)
        {
            provides.Add(new(WeaponRole.Gun,
                             Resolved(shell, ownRounds, knownRounds, m => m.Name)?.DisplayName ?? shell));
        }

        provides.Add(new(WeaponRole.FireControl, fireControl));

        // Whatever the launcher carries that no reader could infer -- a director on its turret
        // roof is a subpart, so the survey walks past it and only the profile knows it is there.
        provides.AddRange(declared);

        return new ComponentProfile
        {
            PartId = launcher.PartId,
            Role = WeaponRole.Launcher,
            DisplayName = launcher.DisplayName,
            Provides = provides,
        };
    }

    // A pack's own key, carrying the pack. Two packs shipping an "AIM-9X" are then two rounds
    // rather than a silent capture of whichever registered first.
    //
    // The built-ins keep bare keys, because they are the namespace every qualified reference is
    // resolved against, and a saved setting or a pack reference already holds those keys.
    private static string Qualify(string source, string name)
        => source == BuiltInSource ? name : $"{source}:{name}";

    private static T? Resolved<T>(string key, List<T> own, IReadOnlyList<T> known, Func<T, string> nameOf)
        where T : class
    {
        for (int i = 0; i < own.Count; i++)
        {
            if (nameOf(own[i]) == key) return own[i];
        }

        for (int i = 0; i < known.Count; i++)
        {
            if (nameOf(known[i]) == key) return known[i];
        }

        return null;
    }

    private static bool Duplicate<T>(string key, List<T> into, Func<T, string> nameOf, Reader r)
    {
        for (int i = 0; i < into.Count; i++)
        {
            if (nameOf(into[i]) != key) continue;

            r.Fault("declared twice in this pack");
            return true;
        }

        return false;
    }

    // One element being read, and every way it can be wrong.
    //
    // Attributes are *consumed*: whatever is left when the element has been built is something this
    // build does not know, and the element is refused for it. Ignoring an unrecognised attribute is
    // how NavConstnat becomes a round flying on the default with the author looking at their number.
    private sealed class Reader(XElement element, string source, string kind, List<PackFault> faults)
    {
        private readonly HashSet<string> _taken = [];
        private string _name = "";
        private bool _sound = true;

        /// <summary>What to call this element in a fault, once it is known.</summary>
        public void Describe(string name) => _name = name;

        public void Fault(string reason)
        {
            _sound = false;
            faults.Add(new PackFault(source, kind, _name, reason));
        }

        /// <summary>True when the element may be registered: nothing wrong, nothing left over.</summary>
        public bool Sound()
        {
            foreach (XAttribute a in element.Attributes())
            {
                if (_taken.Contains(a.Name.LocalName)) continue;

                Fault($"'{a.Name.LocalName}' is not an attribute this build knows");
            }

            foreach (XElement child in element.Elements())
            {
                if (_taken.Contains(child.Name.LocalName)) continue;

                Fault($"<{child.Name.LocalName}> is not something a <{kind}> may contain");
            }

            return _sound;
        }

        public string? Text(string attribute)
        {
            _taken.Add(attribute);
            return element.Attribute(attribute)?.Value;
        }

        public string Required(string attribute)
        {
            if (Text(attribute) is { Length: > 0 } value) return value;

            Fault($"needs a {attribute}");
            return "";
        }

        public float Number(string attribute, float fallback)
        {
            if (Text(attribute) is not { } raw) return fallback;
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                return value;
            }

            Fault($"{attribute}=\"{raw}\" is not a number");
            return fallback;
        }

        public double Number(string attribute, double fallback)
        {
            if (Text(attribute) is not { } raw) return fallback;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                return value;
            }

            Fault($"{attribute}=\"{raw}\" is not a number");
            return fallback;
        }

        public int Count(string attribute, int fallback)
        {
            if (Text(attribute) is not { } raw) return fallback;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                return value;
            }

            Fault($"{attribute}=\"{raw}\" is not a whole number");
            return fallback;
        }

        public bool Flag(string attribute, bool fallback)
        {
            if (Text(attribute) is not { } raw) return fallback;
            if (bool.TryParse(raw, out bool value)) return value;

            Fault($"{attribute}=\"{raw}\" is not true or false");
            return fallback;
        }

        /// <summary>An angle written in degrees, for a profile field held in radians.</summary>
        public double Angle(string attribute, double fallbackRad)
        {
            if (Text(attribute) is not { } raw) return fallbackRad;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double deg))
            {
                return double.DegreesToRadians(deg);
            }

            Fault($"{attribute}=\"{raw}\" is not a number");
            return fallbackRad;
        }

        public double3 Vector(string attribute, double3 fallback)
        {
            if (Text(attribute) is not { } raw) return fallback;
            if (TryVector(raw, out double3 value)) return value;

            Fault($"{attribute}=\"{raw}\" is not three numbers separated by commas");
            return fallback;
        }

        public T Choice<T>(string attribute, T fallback) where T : struct, Enum
        {
            if (Text(attribute) is not { } raw) return fallback;
            if (Enum.TryParse(raw, ignoreCase: true, out T value) && Enum.IsDefined(value)) return value;

            Fault($"{attribute}=\"{raw}\" is not one of: {string.Join(", ", Enum.GetNames<T>())}");
            return fallback;
        }

        /// <summary>
        /// A key naming a round or a sensor, resolved now rather than in flight. Bare means this
        /// pack's own; <c>Other:Name</c> means somebody else's, and <c>KSArmory:</c> the built-ins.
        /// </summary>
        public string Reference<T>(string attribute, List<T> own,
                                   IReadOnlyList<T> known, Func<T, string> nameOf) where T : class
        {
            string raw = Required(attribute);
            if (raw.Length == 0) return "";

            string key = KeyFor(source, raw);
            if (Resolved(key, own, known, nameOf) is not null) return key;

            Fault(Unresolved<T>(attribute, raw));
            return key;
        }

        /// <inheritdoc cref="Reference{T}"/>
        public string? OptionalReference<T>(string attribute, List<T> own,
                                            IReadOnlyList<T> known, Func<T, string> nameOf) where T : class
        {
            if (Text(attribute) is not { Length: > 0 } raw) return null;

            string key = KeyFor(source, raw);
            if (Resolved(key, own, known, nameOf) is null)
            {
                Fault(Unresolved<T>(attribute, raw));
            }

            return key;
        }

        // A reference to this pack's own definition that was refused is the refusal's fault, not
        // the reference's; naming nothing registered sends the author looking for a typo.
        private string Unresolved<T>(string attribute, string raw)
        {
            string definition = typeof(T) == typeof(SensorProfile) ? "Sensor"
                                : typeof(T) == typeof(DecoyProfile) ? "Decoy"
                                : "Munition";
            foreach (PackFault f in faults)
            {
                if (f.Source == source && f.Element == definition && f.Name == raw)
                {
                    return $"{attribute}=\"{raw}\" was refused above: {f.Reason}";
                }
            }

            return $"{attribute}=\"{raw}\" names nothing registered";
        }

        private static string KeyFor(string source, string raw)
        {
            int split = raw.IndexOf(':');
            if (split < 0) return Qualify(source, raw);

            string owner = raw[..split];
            string name = raw[(split + 1)..];
            return owner == BuiltInSource ? name : raw;
        }

        // Gear this launcher carries as subparts. The survey walks *parts*, so a role that lives
        // inside one has to be declared or the system reports as not having it at all.
        public RoundSpin? ReleaseSpin()
        {
            _taken.Add("ReleaseSpin");

            RoundSpin? spin = null;
            foreach (XElement el in element.Elements("ReleaseSpin"))
            {
                Reader s = new(el, source, "ReleaseSpin", faults);
                s.Describe(_name);

                if (spin is not null)
                {
                    s.Fault("a launcher spins its rounds one way");
                    _sound = false;
                    continue;
                }

                if (el.Attribute("RateDegPerSecond") is null) s.Fault("needs a RateDegPerSecond");
                if (el.Attribute("SpinUpSeconds") is null) s.Fault("needs a SpinUpSeconds");

                double rate = s.Number("RateDegPerSecond", 0.0);
                double spinUp = s.Number("SpinUpSeconds", 0.0);
                double ignition = s.Number("IgnitionSeconds", 0.0);
                List<SpinNozzle> nozzles = [];

                s._taken.Add("Nozzle");
                foreach (XElement n in el.Elements("Nozzle"))
                {
                    Reader z = new(n, source, "Nozzle", faults);
                    z.Describe(_name);

                    if (n.Attribute("Mouth") is null) z.Fault("needs a Mouth");
                    if (n.Attribute("Exhaust") is null) z.Fault("needs an Exhaust");
                    SpinNozzle nozzle = new(z.Vector("Mouth", default), z.Vector("Exhaust", default));

                    if (z.Sound()) nozzles.Add(nozzle);
                    else s._sound = false;
                }

                if (s.Sound())
                {
                    spin = new RoundSpin
                    {
                        RateDegPerSecond = rate,
                        SpinUpSeconds = spinUp,
                        IgnitionSeconds = ignition,
                        Nozzles = [.. nozzles],
                    };
                }
                else _sound = false;
            }

            return spin;
        }

        public BeamProfile? Beam()
        {
            _taken.Add("Beam");

            BeamProfile? beam = null;
            foreach (XElement el in element.Elements("Beam"))
            {
                Reader b = new(el, source, "Beam", faults);
                b.Describe(_name);

                if (beam is not null)
                {
                    b.Fault("a head carries one beam");
                    _sound = false;
                    continue;
                }

                BeamProfile read = new()
                {
                    PowerWatts = b.Number("PowerWatts", 1.0e6f),
                    WavelengthMetres = b.Number("WavelengthMetres", 1.315e-6f),
                    ApertureMetres = b.Number("ApertureMetres", 1.5f),
                    JitterRad = b.Number("JitterRad", 0.2e-6f),
                    BurnThroughJoulesPerM2 = b.Number("BurnThroughJoulesPerM2", 6.0e7f),
                    ExtinctionPerMetre = b.Number("ExtinctionPerMetre", 2.0e-5f),
                    ChemicalSeconds = b.Number("ChemicalSeconds", 100f),
                    ShotSeconds = b.Number("ShotSeconds", 5f),
                };

                if (b.Sound()) beam = read;
                else _sound = false;
            }

            return beam;
        }

        public List<BuiltInComponent> Provides()
        {
            _taken.Add("Provides");

            List<BuiltInComponent> rows = [];
            foreach (XElement el in element.Elements("Provides"))
            {
                Reader p = new(el, source, "Provides", faults);
                p.Describe(_name);

                WeaponRole role = p.Choice("Role", WeaponRole.Sensor);
                string label = p.Required("DisplayName");

                if (p.Sound()) rows.Add(new BuiltInComponent(role, label));
                else _sound = false;
            }

            return rows;
        }

        public Tube[] Tubes()
        {
            _taken.Add("Tube");

            List<Tube> tubes = [];
            foreach (XElement el in element.Elements("Tube"))
            {
                Reader t = new(el, source, "Tube", faults);
                t.Describe(_name);

                double3 position = t.Vector("Position", default);
                if (el.Attribute("Position") is null) t.Fault("needs a Position");

                double3 direction = t.Vector("Direction", default);

                if (t.Sound()) tubes.Add(new Tube(position, direction));
                else _sound = false;
            }

            return [.. tubes];
        }

        public double3[] Muzzles()
        {
            _taken.Add("Muzzle");

            List<double3> muzzles = [];
            foreach (XElement el in element.Elements("Muzzle"))
            {
                Reader m = new(el, source, "Muzzle", faults);
                m.Describe(_name);

                double3 at = m.Vector("At", default);
                if (el.Attribute("At") is null) m.Fault("needs an At");

                if (m.Sound()) muzzles.Add(at);
                else _sound = false;
            }

            return [.. muzzles];
        }

        public BoostStage[] Stages()
        {
            _taken.Add("Stage");

            List<BoostStage> stages = [];
            foreach (XElement el in element.Elements("Stage"))
            {
                Reader s = new(el, source, "Stage", faults);
                s.Describe(_name);

                float seconds = s.Number("Seconds", 0f);
                float accel = s.Number("Accel", 0f);
                if (el.Attribute("Seconds") is null) s.Fault("needs a Seconds");

                if (s.Sound()) stages.Add(new BoostStage(seconds, accel));
                else _sound = false;
            }

            return [.. stages];
        }

        private static bool TryVector(string raw, out double3 value)
        {
            value = default;

            string[] parts = raw.Split(',');
            if (parts.Length != 3) return false;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
            {
                return false;
            }

            value = new double3(x, y, z);
            return true;
        }
    }
}
