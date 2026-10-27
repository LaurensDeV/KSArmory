using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using KSA.Rendering.Water.Data;

namespace KSArmory;

internal static partial class KsaWorld
{
    /// <summary>
    /// The sea's level against the mean sphere, on a body that has one: an ocean reference with a
    /// density. Never its <c>IsValid()</c>, which is false wherever the level is 0 m.
    /// </summary>
    public static bool TrySeaLevel(Celestial body, out double level)
    {
        level = 0.0;

        try
        {
            if (body.GetOceanReference() is not { } sea || !(sea.Density > 0.0)) return false;

            level = sea.Level;
            return double.IsFinite(level);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// What the ground under a burst adds to its blast. Free air for one on a body with no air, and
    /// for one further over the highest ground than its reflection could matter -- which is every
    /// shell a gun bursts against an aircraft, so they pay no terrain lookup for it.
    /// </summary>
    public static GroundReflection GroundReflectionAt(Celestial? body, double3 burstEcl, double chargeKg)
    {
        if (body is null || !(chargeKg > 0.0) || !Vec.IsFinite(burstEcl)) return GroundReflection.FreeAir;

        try
        {
            if (!HasAtmosphere(body)) return GroundReflection.FreeAir;

            double3 fromCentre = burstEcl - body.GetPositionEcl();
            double altitude = Vec.Len(fromCentre) - body.MeanRadius;
            if (altitude - MaxTerrainHeightMetres(body) > ReflectionReachInBlastRadii * Warhead.BlastRadius(chargeKg))
            {
                return GroundReflection.FreeAir;
            }

            double height = BurstHeightOf(body, burstEcl);
            double kt = MushroomCloud.KilotonsFor(chargeKg);
            return new GroundReflection(burstEcl, Vec.Unit(fromCentre), height,
                                        MushroomCloud.GroundCoupling(kt, height));
        }
        catch
        {
            return GroundReflection.FreeAir;
        }
    }

    // A stem loads the ground to eight times the incident front, which reaches twice as far.
    private const double ReflectionReachInBlastRadii = 2.0;

    /// <summary>
    /// How high a burst stood over the ground under it, or over the sea where there is sea there.
    /// </summary>
    public static double BurstHeightOf(Celestial body, double3 positionEcl)
    {
        try
        {
            double3 cce = positionEcl - body.GetPositionEcl();
            double radius = Vec.Len(cce);
            if (!(radius > 0.0)) return 0.0;

            double ground = new TerrainHeights(body, accurate: true).TryHeight(cce / radius, out double h)
                                ? h
                                : double.NaN;
            bool hasSea = TrySeaLevel(body, out double seaLevel);

            return BurstSettings.HeightOver(radius - body.MeanRadius, ground, seaLevel, hasSea);
        }
        catch
        {
            return 0.0;
        }
    }

    /// <summary>
    /// What a burst at a place went off on or in: land, the sea's surface, or under it.
    ///
    /// <para>Read accurately: once per burst, and a beach and the water beside it are metres apart.
    /// An unreadable height field is land.</para>
    /// </summary>
    public static BurstSetting SettingOf(Celestial body, double3 positionEcl, double fireballRadius)
    {
        try
        {
            double3 cce = positionEcl - body.GetPositionEcl();
            double radius = Vec.Len(cce);
            if (!(radius > 0.0)) return BurstSetting.Land;

            if (!new TerrainHeights(body, accurate: true).TryHeight(cce / radius, out double ground))
            {
                return BurstSetting.Land;
            }

            bool hasSea = TrySeaLevel(body, out double seaLevel);

            return BurstSettings.Classify(radius - body.MeanRadius, ground, seaLevel, hasSea,
                                          fireballRadius);
        }
        catch
        {
            return BurstSetting.Land;
        }
    }

    /// <summary>
    /// How high the star stands over the horizon at a place, in degrees. Negative is below it.
    ///
    /// <para>Answers why something is dark: at civil twilight a cloud is lit by the sky however
    /// bright the tonemapped ground looks.</para>
    /// </summary>
    public static double SunElevationDeg(Celestial body, double3 positionEcl)
    {
        try
        {
            if (!TryStarPositionEcl(out double3 starEcl)) return double.NaN;

            double3 up = Vec.Unit(positionEcl - body.GetPositionEcl());
            double3 toSun = Vec.Unit(starEcl - positionEcl);
            if (!Vec.IsFinite(up) || !Vec.IsFinite(toSun)) return double.NaN;

            return Math.Asin(Math.Clamp(Vec.Dot(up, toSun), -1.0, 1.0)) * 180.0 / Math.PI;
        }
        catch
        {
            return double.NaN;
        }
    }

    /// <summary>The system's <c>StellarBody</c>, found by type rather than by name.</summary>
    public static bool TryStarPositionEcl(out double3 positionEcl)
    {
        positionEcl = default;

        try
        {
            if (Universe.CurrentSystem is not { } system) return false;

            for (int i = 0; i < system.Count; i++)
            {
                if (system.GetIndex(i) is not StellarBody star) continue;

                positionEcl = star.GetPositionEcl();

                return Vec.IsFinite(positionEcl);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The bodies of the current system, by the Id <see cref="TryPlaceOnSurface"/> matches on, for
    /// setting a craft down where the cursor cannot reach. Air decides which burst effect a test gets.
    /// </summary>
    public static void SystemBodies(List<(string Id, bool HasAir)> into)
    {
        into.Clear();

        try
        {
            if (Universe.CurrentSystem is not { } system) return;

            for (int i = 0; i < system.Count; i++)
            {
                if (system.GetIndex(i) is not Celestial body) continue;
                if (body.Id is not { Length: > 0 } id) continue;

                into.Add((id, HasAtmosphere(body)));
            }
        }
        catch
        {
            into.Clear();
        }
    }

    /// <summary>How far along a ray from a craft the body under it has cloud thick enough to stop a beam.</summary>
    public static bool TryCloudEntry(Vehicle platform, double3 originEcl, double3 directionEcl, double reach,
                                     out double distance)
    {
        distance = double.PositiveInfinity;
        try
        {
            return ParentBody(platform) is { } body
                   && CloudLayers.TryEntry(body, originEcl, directionEcl, reach, out distance);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// How fast the ground under a point is moving, in the ecliptic frame: the parent body's own
    /// motion plus its spin at that radius.
    ///
    /// <para>The frame a round flies in: airspeed, drag and pointing. It equals the launcher's
    /// velocity only while the launcher sits still on the ground.</para>
    /// </summary>
    public static double3 GroundVelocityAt(Vehicle platform, double3 positionEcl)
    {
        try
        {
            if (platform.Parent is not Celestial body) return SafeVelocityEcl(platform);

            double3 ground = GroundVelocityAt(body, positionEcl);

            return Vec.IsFinite(ground) ? ground : SafeVelocityEcl(platform);
        }
        catch
        {
            // Exact for a launcher on the ground, the closest answer for any other.
            return SafeVelocityEcl(platform);
        }
    }

    /// <summary>
    /// How that ground frame is accelerating: its spin carrying it round the axis.
    ///
    /// <para>What a round flown against the ground has to be told, or it falls under a gravity the
    /// ground does not feel. At 28.6 degrees on Earth that is 0.026 m/s² of lift, and a 5"/54 laid on
    /// flat ground without it landed 15 m long at 8 km and 33 m at 15 km.</para>
    /// </summary>
    public static double3 GroundAccelerationAt(Vehicle platform, double3 positionEcl)
    {
        try
        {
            if (platform.Parent is not Celestial body) return Vec.Zero;

            double3 spin = ((IParentBody)body).GetAngularVelocityCce();
            double3 fromCentre = positionEcl - body.GetPositionEcl();
            if (!Vec.IsFinite(spin) || !Vec.IsFinite(fromCentre)) return Vec.Zero;

            return Vec.Cross(spin, Vec.Cross(spin, fromCentre));
        }
        catch
        {
            return Vec.Zero;
        }
    }

    /// <summary>The same ground frame asked of the body, for a round outliving its craft.</summary>
    public static double3 GroundVelocityAt(Celestial body, double3 positionEcl)
    {
        try
        {
            // Cce, not GetBodyRates' Cci: the separation is Cce, and Earth's 23.4 deg tilt at
            // 465 m/s of surface speed would invent up to 190 m/s.
            double3 spin = ((IParentBody)body).GetAngularVelocityCce();
            double3 fromCentre = positionEcl - body.GetPositionEcl();

            // Without the spin: wrong by the surface speed, not the orbital velocity.
            if (!Vec.IsFinite(spin) || !Vec.IsFinite(fromCentre)) return body.GetVelocityEcl();

            return body.GetVelocityEcl() + Vec.Cross(spin, fromCentre);
        }
        catch
        {
            return Vec.Zero;
        }
    }

    /// <summary>
    /// The velocity of the body a craft's ground belongs to: <see cref="GroundVelocityAt(Vehicle, double3)"/>
    /// without the spin. The craft's own where there is no body or it cannot be read, which makes the ground and
    /// the body one motion and so carries nothing round.
    /// </summary>
    public static double3 BodyVelocityAt(Vehicle platform)
    {
        try
        {
            if (platform.Parent is not Celestial body) return SafeVelocityEcl(platform);

            double3 velocity = body.GetVelocityEcl();
            return Vec.IsFinite(velocity) ? velocity : SafeVelocityEcl(platform);
        }
        catch
        {
            return SafeVelocityEcl(platform);
        }
    }

    // Called from catch blocks, for a platform just disposed, which is when VelocityEcl throws.
    private static double3 SafeVelocityEcl(Vehicle platform)
    {
        try
        {
            return VelocityEcl(platform);
        }
        catch
        {
            return Vec.Zero;
        }
    }

    /// <summary>From the platform's parent body, in Ecl; zero if unavailable.</summary>
    public static double3 GravityAt(Vehicle platform, double3 positionEcl)
    {
        try
        {
            return platform.Parent is Celestial body ? GravityAt(body, positionEcl) : Vec.Zero;
        }
        catch
        {
            return Vec.Zero;
        }
    }

    /// <summary>
    /// Everything accelerating a craft, in Ecl: <c>AccelerationBody</c>, an accelerometer's reading,
    /// with gravity put back, so a parked craft reads as still. Zero if unreadable.
    /// </summary>
    public static double3 AccelerationEcl(Vehicle vehicle)
    {
        try
        {
            double3 measured = vehicle.AccelerationBody.Transform(vehicle.Body2Cce);
            double3 total = measured + GravityAt(vehicle, PositionEcl(vehicle));
            return Vec.IsFinite(total) ? total : Vec.Zero;
        }
        catch
        {
            return Vec.Zero;
        }
    }

    /// <summary>What an accelerometer aboard reads, in Ecl: everything but gravity. Zero if unreadable.</summary>
    public static double3 ProperAccelerationEcl(Vehicle vehicle)
    {
        try
        {
            double3 measured = vehicle.AccelerationBody.Transform(vehicle.Body2Cce);
            return Vec.IsFinite(measured) ? measured : Vec.Zero;
        }
        catch
        {
            return Vec.Zero;
        }
    }

    /// <summary>The same pull, asked of the body directly — for a round with no craft left.</summary>
    public static double3 GravityAt(Celestial body, double3 positionEcl,
                                   double3 bodyOffsetEcl = default)
    {
        try
        {
            return PointGravity.Toward(body.GetPositionEcl() + bodyOffsetEcl, ((IParentBody)body).Mu,
                                       positionEcl);
        }
        catch
        {
            return Vec.Zero;
        }
    }

    /// <summary>
    /// The pull a round is flown with (<see cref="PointGravity.OnRound"/>), read off its body.
    /// <paramref name="bodyVelocityEcl"/> is passed rather than read so it is the same sample the
    /// round was stepped against.
    /// </summary>
    public static double3 PullOnRound(Celestial body, double3 positionEcl, double3 bodyVelocityEcl,
                                      double secondsIntoFrame)
    {
        double mu = 0.0;
        double3 sample = Vec.Zero;
        try
        {
            mu = ((IParentBody)body).Mu;
            sample = body.GetPositionEcl();
        }
        catch
        {
            mu = 0.0;
        }

        return PointGravity.OnRound(mu, sample, bodyVelocityEcl, BodyFallEcl(body), positionEcl,
                                    secondsIntoFrame);
    }

    /// <summary>
    /// The parent body's own fall toward its primary, in Ecl; zero for no primary or unreadable.
    /// Without it a round integrated in Ecl is left behind: Earth falls at 6.096 mm/s², 428 m over a
    /// 375 s coast (<c>docs/MIRV-NEXT.md</c> item 2).
    ///
    /// <para>Exact, since KSA moves celestials on Kepler rails built with the primary's <c>Mu</c>
    /// (<c>Orbit.StateVectors.AccelerationCci</c> is a hardcoded zero). One link only: Luna loses
    /// Earth's 5.9 mm/s² toward Sol, some 417 m over the same coast.</para>
    /// </summary>
    public static double3 BodyFallEcl(Celestial body)
    {
        try
        {
            // IParentBody, not Celestial: a test for Celestial compiles and answers zero for every body.
            if (body.Parent is not { } primary) return Vec.Zero;

            return PointGravity.Toward(primary.GetPositionEcl(), primary.Mu, body.GetPositionEcl());
        }
        catch
        {
            return Vec.Zero;
        }
    }

    /// <summary>
    /// The parent body's travel through the ecliptic, reported only: the lever on a force sample a
    /// frame out of step, 516 m at 29.8 km/s on a 17 ms frame (<c>docs/KSA-FRAME-ORDER.md</c> §5).
    /// </summary>
    public static double3 BodyTravelEcl(Celestial body)
    {
        try
        {
            return body.GetVelocityEcl();
        }
        catch
        {
            return Vec.Zero;
        }
    }

    /// <summary>
    /// A radius the ground on this body is never below: the mean radius less as far as its highest ground
    /// stands above it. Zero where that cannot be read, which never lets a round off its clock.
    /// </summary>
    public static double LowestGroundRadius(Celestial body)
    {
        try
        {
            double radius = body.MeanRadius;
            double depth = MaxTerrainHeightMetres(body);

            return radius > 0.0 && double.IsFinite(depth) && depth >= 0.0 && radius > depth ? radius - depth : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    /// <summary>
    /// The top of the atmosphere, or the highest ground where there is none; zero, "do not judge",
    /// when neither reads. Not the mean radius: this decides whether to destroy a round.
    /// </summary>
    public static double ArrivalCeilingRadius(Celestial body)
    {
        try
        {
            double radius = body.MeanRadius;
            if (!(radius > 0.0)) return 0.0;

            double above = 0.0;

            if (body.GetAtmosphereReference()?.Physical is { } air && air.Height > 0.0)
            {
                above = air.Height;
            }

            // MaxTerrainHeightApprox omits erosion and detail, so it is under the real terrain:
            // the wrong way round for a test that destroys things (docs/KSA-TERRAIN.md).
            above = Math.Max(above, body.MaxTerrainHeightApprox);

            return above > 0.0 ? radius + above : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    /// <summary>Zero when it cannot be read.</summary>
    public static double BodyMu(Celestial body)
    {
        try
        {
            double mu = ((IParentBody)body).Mu;
            return double.IsFinite(mu) && mu > 0.0 ? mu : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    /// <summary>
    /// Density of whatever the round is flying through, as a multiple of
    /// <see cref="Medium.ReferenceDensityKgPerM3"/> — Earth's sea-level air, whatever body this is.
    ///
    /// <para>1.0 at Earth's sea level, 0.0 in vacuum, roughly 840 below the waterline. A body with no
    /// atmosphere reads 0.0, an answer; one that cannot be read reads 1.0, so a round keeps its drag.</para>
    /// </summary>
    public static double MediumDensityRatioAt(Vehicle platform, double3 positionEcl)
    {
        try
        {
            return platform.Parent is Celestial body ? MediumDensityRatioAt(body, positionEcl) : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    /// <summary>For a round with no craft left.</summary>
    public static double MediumDensityRatioAt(Celestial body, double3 positionEcl)
        => MediumDensityRatioAt(body, positionEcl, withOcean: true);

    /// <summary>
    /// The air alone, with no ocean under it: what a gun's lead flies a shell through on its way to a target.
    ///
    /// <para>A place on the sea sits on the waterline, and a lay's last step a hair under it would read
    /// the ocean and stop the shell dead.</para>
    /// </summary>
    public static double AirDensityRatioAt(Vehicle platform, double3 positionEcl)
    {
        try
        {
            return platform.Parent is Celestial body ? MediumDensityRatioAt(body, positionEcl, withOcean: false) : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    /// <summary>For a warhead's flight predicted from a bus.</summary>
    public static double AirDensityRatioAt(Celestial body, double3 positionEcl)
        => MediumDensityRatioAt(body, positionEcl, withOcean: false);

    /// <summary>Whether the body has any air, where a density at one point is the wrong question.</summary>
    public static bool HasAtmosphere(Celestial body)
    {
        try
        {
            return body.GetAtmosphereReference()?.Physical is { } air && air.Height > 0.0;
        }
        catch
        {
            // A cloud that does not draw is a smaller fault than airless dust on Earth.
            return true;
        }
    }

    /// <summary>
    /// A body's magnetic field: KSA's declared dipole, tilted and off centre, where it has one; else a centred
    /// one as its <c>Bodies.xml</c> entry gives it; else none.
    /// </summary>
    public static MagneticDipole DipoleOf(Celestial body)
    {
        try
        {
            if (body.BodyTemplate?.MagnetosphereReference is { } field && field.EquatorialFieldMicroTesla.Value > 0.0)
            {
                return new MagneticDipole(field.OffsetCcf, double3.UnitZ.Transform(field.Mag2Ccf),
                                          field.EquatorialFieldMicroTesla.Value * 1.0e-6, body.MeanRadius);
            }

            BodyTraits traits = BodyCatalogue.For(SafeId(body));
            if (!(traits.FieldTesla > 0.0)) return MagneticDipole.None;

            // Ccf's +Z is the spin axis and +X its zero longitude.
            double tilt = double.DegreesToRadians(traits.FieldTiltDeg);
            double azimuth = double.DegreesToRadians(traits.FieldAzimuthDeg);
            double3 axis = new(Math.Sin(tilt) * Math.Cos(azimuth), Math.Sin(tilt) * Math.Sin(azimuth), Math.Cos(tilt));
            return new MagneticDipole(Vec.Zero, axis, traits.FieldTesla, body.MeanRadius);
        }
        catch
        {
            return MagneticDipole.None;
        }
    }

    /// <summary>
    /// A body's air as numbers, for the Sim side: what KSA declares, and what its <c>Bodies.xml</c>
    /// entry says about the rest. Airless where the body declares no air, which is knowledge rather than
    /// a failed read.
    /// </summary>
    public static BodyAir BodyAirOf(Celestial body)
    {
        AuditBodiesOnce();

        BodyTraits traits = BodyCatalogue.For(SafeId(body));
        double radius = body.MeanRadius;
        double gravity = radius > 0.0 ? body.Mass * KSA.Constants.GRAVITATIONAL_CONSTANT / (radius * radius) : 0.0;

        try
        {
            // KSA's own IsValid() is an astronomical-scale check on the scale height and is false for
            // every real atmosphere (MediumDensityRatioAt), so the terms are checked instead.
            if (body.GetAtmosphereReference()?.Physical is not { } air) return BodyAir.Airless(gravity, radius, traits);

            return new BodyAir(air.GetAtmosphericPressureAtAltitude(0.0), air.GetAtmosphericDensityAtAltitude(0.0),
                               air.ScaleHeight.InMeters(), air.Height, gravity, radius, traits,
                               Wet: body.GetOceanReference() is { } ocean && ocean.Density > 0.0);
        }
        catch
        {
            return BodyAir.Airless(gravity, radius, traits);
        }
    }

    /// <summary>
    /// The air at a point over a body, against the fixed references; <see cref="AmbientAir.Unknown"/>
    /// where it cannot be read, never a silent sea level.
    /// </summary>
    public static AmbientAir AirAt(Celestial body, double3 positionEcl)
    {
        try
        {
            BodyAir air = BodyAirOf(body);
            return air.AirAt(Vec.Len(positionEcl - body.GetPositionEcl()) - body.MeanRadius);
        }
        catch
        {
            return AmbientAir.Unknown;
        }
    }

    private static string? SafeId(Celestial body)
    {
        try
        {
            return body.Id;
        }
        catch
        {
            return null;
        }
    }

    private static bool _bodiesAudited;

    /// <summary>
    /// Once the world has loaded, the Bodies.xml entries this solar system has no body for, in one line.
    /// Not a fault -- a system of three bodies leaves the rest unused -- but a body a KSA update renamed
    /// shows up here while the real one silently goes without its field and airglow.
    /// </summary>
    public static void AuditBodiesOnce()
    {
        if (_bodiesAudited || Universe.CurrentSystem is not { } system) return;
        _bodiesAudited = true;

        List<PackFault> unused = BodyCatalogue.Audit(id => system.Get(id) is not null);
        if (unused.Count > 0)
        {
            Log.Info($"bodies described but not in this solar system, so unused here: "
                     + string.Join(", ", unused.Select(f => f.Name)));
        }
    }

    private static double MediumDensityRatioAt(Celestial body, double3 positionEcl, bool withOcean)
    {
        try
        {
            // Over the mean surface, as KSA's physics measures it. The ocean is asked first: a body can
            // have an ocean and no atmosphere.
            double altitude = Vec.Len(positionEcl - body.GetPositionEcl()) - body.MeanRadius;

            // ~840x sea-level air, so the ratio is not bounded by 1. Not the ocean's IsValid(), which
            // tests its level against a 100 km bar; a body with no ocean hands back null.
            OceanReference? ocean = body.GetOceanReference();
            if (withOcean && ocean is { } sea && sea.Density > 0.0 && altitude < sea.Level)
            {
                double water = sea.Density / Medium.ReferenceDensityKgPerM3;
                return double.IsFinite(water) && water > 0.0 ? water : 1.0;
            }

            // Not IsValid(), which requires a scale height over 100 km and is false for every real
            // atmosphere. No physical reference is the model saying no air, so 0.0, never 1.0.
            AtmosphereReference? atmosphere = body.GetAtmosphereReference();
            if (atmosphere?.Physical is not { } air) return 0.0;

            double seaLevel = air.SeaLevelDensity;
            double scaleHeight = air.ScaleHeight.InMeters();
            if (!(seaLevel > 0.0) || !(scaleHeight > 0.0)) return 0.0;

            if (altitude < 0.0) altitude = 0.0;
            if (altitude >= air.Height) return 0.0;

            double ratio = air.GetAtmosphericDensityAtAltitude(altitude) / Medium.ReferenceDensityKgPerM3;
            return double.IsFinite(ratio) && ratio >= 0.0 ? ratio : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    /// <summary>
    /// Air pressure at a point over a body, in pascals: zero above the air, and where it cannot be
    /// read. What KSA gates an explosion's smoke and its air or vacuum debris on.
    /// </summary>
    public static float AtmosphericPressureAt(Celestial body, double3 positionEcl)
    {
        try
        {
            if (body.GetAtmosphereReference()?.Physical is not { } air) return 0f;

            double altitude = Math.Max(Vec.Len(positionEcl - body.GetPositionEcl()) - body.MeanRadius, 0.0);
            if (altitude >= air.Height) return 0f;

            double pressure = air.GetAtmosphericPressureAtAltitude(altitude);
            return double.IsFinite(pressure) && pressure > 0.0 ? (float)pressure : 0f;
        }
        catch
        {
            return 0f;
        }
    }

    /// <summary>
    /// Why <see cref="MediumDensityRatioAt(Celestial, double3)"/> answered what it did, for the log.
    /// </summary>
    public static string MediumDiagnosis(Celestial body, double3 positionEcl)
    {
        try
        {
            AtmosphereReference? atmosphere = body.GetAtmosphereReference();
            if (atmosphere is null) return "no atmosphere reference -- vacuum";
            if (atmosphere.Physical is not { } air) return "no physical atmosphere -- vacuum";

            double seaLevel = air.SeaLevelDensity;
            double top = air.Height;
            double radius = Vec.Len(positionEcl - body.GetPositionEcl());
            double altitude = radius - body.MeanRadius;

            return $"valid={air.IsValid()} seaLevel={seaLevel:F4} kg/m3 "
                 + $"scaleHeight={air.ScaleHeight.InMeters():F0} m top={top:F0} m | "
                 + $"radius={radius:F0} m meanRadius={body.MeanRadius:F0} m altitude={altitude:F0} m "
                 + $"-> {(altitude >= top ? "above the atmosphere" : "inside it")}";
        }
        catch (Exception e)
        {
            return $"threw {e.GetType().Name}: {e.Message}";
        }
    }
}
