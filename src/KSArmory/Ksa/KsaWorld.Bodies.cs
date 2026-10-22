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
    /// The sea's level against the mean sphere, on a body that has one.
    ///
    /// <para>The same discriminator <see cref="MediumDensityRatioAt"/> uses: a body with no ocean
    /// hands back null, and one with an ocean has a density. Never its <c>IsValid()</c>, which
    /// tests a level of 0 m against an astronomical bar and is false wherever there is water.</para>
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

    // How far over the ground, in the charge's own blast radius, a burst's reflection is still worth
    // asking about. A stem loads the ground to eight times the incident front, which reaches twice
    // as far, so the blast radius alone would miss the edge of it.
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
    /// <para>Terrain and sea both against the mean sphere, and the terrain read accurately,
    /// because it is asked once per burst and the difference between a beach and the water beside
    /// it is a few metres. An unreadable height field is land.</para>
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
    /// <para>What this answers is why something is dark. Every LUT the atmosphere is sampled
    /// through is parameterised on the sun's zenith angle, so a cloud at civil twilight is lit by
    /// the sky and barely by the sun however bright the ground looks — terrain at that hour is lit
    /// by skylight and reads as daylight in a tonemapped picture, which is exactly how a correct
    /// dark cloud gets mistaken for a broken one.</para>
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

    /// <summary>
    /// Where the star is, for anything that has to know which way the light comes from.
    ///
    /// <para>The system's <c>StellarBody</c> rather than a name: <c>Celestial.Class</c> already
    /// separates a planet from a moon by asking whether its parent is one, so the type is the
    /// system's own answer to "which of these is the sun".</para>
    /// </summary>
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
    /// The bodies of the current system, by the Id <see cref="TryPlaceOnSurface"/> matches on.
    ///
    /// <para>For putting a craft somewhere the cursor cannot reach. <c>CraftMover</c> resolves its
    /// target off the pointer, so it can only ever set a craft down on the body already being
    /// looked at — which leaves anything airless untestable when no save has a craft there, and
    /// none does.</para>
    ///
    /// <para>Whether a body has an atmosphere comes back beside the name, because that is the one
    /// thing that decides which burst effect a test will get.</para>
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
    /// <para>This is the frame a round flies in — its airspeed, what its drag acts against, and
    /// what it points along. It equals the launching craft's velocity only while that craft sits
    /// still on the ground; a store released from something <em>moving</em> is what separates
    /// them. Taking the craft instead leaves a bomb with no airspeed at all at release: the only
    /// motion it then has relative to its launcher is gravity, so it points straight down the
    /// instant it lets go and the aircraft flies out from under it.</para>
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
            // The craft's own velocity, which is exactly right for a launcher standing on the
            // ground and the closest available answer for any other.
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

    /// <summary>
    /// The same ground frame, asked of the body directly.
    ///
    /// <para>Which is what it always was: the platform above is consulted only to reach its parent
    /// and as a fallback answer. A round outliving the craft that fired it keeps flying in this
    /// frame, and has to ask for it without one.</para>
    /// </summary>
    public static double3 GroundVelocityAt(Celestial body, double3 positionEcl)
    {
        try
        {
            // Cce, not the Cci that GetBodyRates answers with: the separation below is a Cce
            // vector, and the two frames differ by the body's axial tilt -- 23.4 degrees on Earth,
            // which at 465 m/s of surface speed invents up to 190 m/s of velocity out of nothing.
            double3 spin = ((IParentBody)body).GetAngularVelocityCce();
            double3 fromCentre = positionEcl - body.GetPositionEcl();

            // The body's own motion without the spin term, which is the honest answer when the
            // spin cannot be read: wrong by at most the surface speed, where guessing zero would
            // be wrong by the whole orbital velocity.
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

    // Inside another method's catch block, so it may not throw itself. VelocityEcl is a bare
    // property read with no guard of its own, and the case this exists for is a platform that has
    // just been disposed -- which is exactly when it throws.
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

    /// <summary>
    /// Gravitational acceleration at <paramref name="positionEcl"/> from the platform's parent body,
    /// in Ecl. Returns zero if the parent or its gravity parameter is unavailable.
    /// </summary>
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
    /// Everything accelerating a craft, in Ecl: the engine's measured acceleration with gravity put
    /// back.
    ///
    /// <para><c>AccelerationBody</c> is what an accelerometer aboard would read — thrust, drag and
    /// lift, with gravity integrated separately and left out — and on the ground it is the reaction
    /// holding the craft up. Adding the pull back is what makes a coasting drone read as falling and
    /// a parked craft read as still. Zero if any of it cannot be read.</para>
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
    /// What the parent body itself is doing — its own fall toward whatever it orbits, in the
    /// ecliptic frame. Zero for a body with no primary, and zero when anything cannot be read.
    ///
    /// <para><b>A round would not have this and the ground under it does.</b> A round is integrated
    /// against its parent body's gravity in <c>Ecl</c>, so without this it is left behind by exactly
    /// this — where a real warhead and a real planet fall toward the Sun together and the term
    /// cancels. KSA's Earth falls at 6.096 mm/s², measured: 428 m over a 375 s coast, and a shallow
    /// arrival multiplies whatever share of that lies along local up by <c>cot γ</c> — eight at 7°.
    /// <see cref="WeaponSystem"/> applies it; <c>docs/MIRV-NEXT.md</c> item 2 is the flown
    /// measurement, 2.88 km of miss to 0.72.</para>
    ///
    /// <para><b>Exact rather than approximate</b>, because KSA moves celestials on analytic Kepler
    /// rails whose conic is generated with the primary's own <c>Mu</c> — so a body's acceleration
    /// <em>is</em> <c>mu/r²</c> toward its primary rather than being modelled by it. Read
    /// <c>Orbit.StateVectors.AccelerationCci</c> instead and you get a hardcoded zero.</para>
    ///
    /// <para><b>One link only, which is a limit on other bodies.</b> A body's true ecliptic
    /// acceleration is the sum over every link up to the star, and this supplies the nearest. Earth's
    /// parent is Sol, so Earth is exact. Luna's parent is Earth, so a lunar shot gets
    /// <c>mu_earth/r²</c> and loses Earth's own 5.9 mm/s² toward Sol — more than twice the term it
    /// keeps, and worth some 417 m of drift over the same coast.</para>
    /// </summary>
    public static double3 BodyFallEcl(Celestial body)
    {
        try
        {
            // IParentBody, not Celestial: Celestial.Parent is declared as the interface, and a
            // planet's primary need not be one of the concrete type. Testing for Celestial compiles,
            // reads as a null check, and silently answers zero for every body in the game.
            if (body.Parent is not { } primary) return Vec.Zero;

            return PointGravity.Toward(primary.GetPositionEcl(), primary.Mu, body.GetPositionEcl());
        }
        catch
        {
            return Vec.Zero;
        }
    }

    /// <summary>
    /// The parent body's own travel through the ecliptic, which is a different question from
    /// <see cref="BodyFallEcl"/> and roughly square to it on a near-circular orbit.
    ///
    /// <para><b>It is the lever on a force sample taken a frame out of step.</b> A round's gravity is
    /// read at its pre-step position against a celestial sample from the frame's end, so the pull
    /// centre is displaced by this times the step — 516 m at 29.8 km/s on a 17 ms frame, which is a
    /// spurious transverse acceleration of <c>g·v·dt/r</c>, around 1 mm/s². That is larger than the
    /// body's fall. <c>docs/KSA-FRAME-ORDER.md</c> §5 and <c>docs/MIRV-NEXT.md</c> item 2.</para>
    ///
    /// <para>Reported only. What it is worth depends on where it lies against the arrival, and no
    /// shot has written that down.</para>
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
    /// The radius at which arriving begins on this body: the top of its atmosphere, or its highest
    /// ground where it has none. Zero when neither can be read, which callers take as "do not
    /// judge".
    ///
    /// <para>Deliberately not the mean radius. A trajectory that clears the mean sphere can still
    /// meet a mountain, and this is asked in order to <em>destroy</em> a round — so it errs high,
    /// where erring low reaps something that would have arrived.</para>
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

            // On an airless body the ground itself is the ceiling. This omits erosion and detail -
            // it is computed before the render data populates the modifiers - so it is a floor
            // under the real terrain rather than a bound on it, which is the wrong way round for a
            // test that destroys things. A body with neither is left unjudged.
            above = Math.Max(above, body.MaxTerrainHeightApprox);

            return above > 0.0 ? radius + above : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    /// <summary>
    /// The body's gravitational parameter, or zero when it cannot be read. What a round needs to
    /// re-aim its own gravity at a centre that moves rather than being handed a fixed vector.
    /// </summary>
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
    /// <para>1.0 at Earth's sea level, 0.0 in vacuum and above the atmosphere, and roughly 840 below
    /// Earth's waterline. One reference for every body, because a round's drag belongs to the round and
    /// the air: divided by each body's own sea level, a round would be dragged through thin air as hard
    /// as through Earth's. One scale covers air and water, so a torpedo simply carries a much smaller
    /// drag.</para>
    ///
    /// <para><b>A body with no atmosphere reads 0.0, and that is an answer rather than a
    /// failure.</b> Every airless body in the game hands back no reference, so reading that as the
    /// fallback would put Earth's sea-level air on the Moon.</para>
    ///
    /// <para>The fallback when the atmosphere genuinely <em>cannot be read</em> — a throw, or a
    /// craft with no body under it — is still 1.0: a round that keeps its tuned drag is a far less
    /// confusing failure than one that silently loses all of it and flies several times further.
    /// The two are distinguished because they are different things, and only one of them is a
    /// fault.</para>
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

    /// <summary>The same medium, asked of the body directly — for a round with no craft left.</summary>
    public static double MediumDensityRatioAt(Celestial body, double3 positionEcl)
        => MediumDensityRatioAt(body, positionEcl, withOcean: true);

    /// <summary>
    /// The air alone, with no ocean under it: what a gun's lead flies a shell through on its way to a target.
    ///
    /// <para>A place designated on the sea sits on the waterline, and a lay that reads the round where it truly is
    /// reads its last step a hair under it — the ocean, 840x the air, which stops the shell dead in the lay and
    /// leaves it unable to converge on a target in reach. A shell never flies through water to get somewhere.</para>
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

    /// <summary>The air alone, asked of the body directly — for a warhead's flight predicted from a bus.</summary>
    public static double AirDensityRatioAt(Celestial body, double3 positionEcl)
        => MediumDensityRatioAt(body, positionEcl, withOcean: false);

    /// <summary>
    /// Whether a body has an atmosphere at all.
    ///
    /// <para>Asked where a density at a point is the wrong question: what is wanted is whether the
    /// <em>body</em> has air for a buoyant cloud to rise through, which no altitude over one this
    /// answers false for can supply.</para>
    /// </summary>
    public static bool HasAtmosphere(Celestial body)
    {
        try
        {
            return body.GetAtmosphereReference()?.Physical is { } air && air.Height > 0.0;
        }
        catch
        {
            // Unreadable reads as having one, so a burst falls back to the cloud rather than to the
            // airless effect: a cloud that does not draw is a smaller fault than dust on Earth.
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
            // Altitude above the mean surface, the same measure KSA's own physics uses. Asked
            // before the air is, because whether a point is under water is its own question: a
            // body can have an ocean and no atmosphere, and resolving the air first returned
            // vacuum for a point at the bottom of one.
            double altitude = Vec.Len(positionEcl - body.GetPositionEcl()) - body.MeanRadius;

            // Below the waterline the medium is the ocean, which is ~840x sea-level air. The
            // ratio is therefore not bounded above by 1.
            // The ocean's IsValid() tests its level (0 m) and transparency depth (100 m) against
            // an astronomical 100 km bar, so it is false wherever there is water. A body with no
            // ocean hands back null, which is the discriminator that means it.
            OceanReference? ocean = body.GetOceanReference();
            if (withOcean && ocean is { } sea && sea.Density > 0.0 && altitude < sea.Level)
            {
                double water = sea.Density / Medium.ReferenceDensityKgPerM3;
                return double.IsFinite(water) && water > 0.0 ? water : 1.0;
            }

            // Never gate on KSA's own IsValid(). DistanceReference.IsValid requires a distance
            // over 100 km — an astronomical-scale sanity check — and the atmosphere's applies it
            // to the scale height, 8 km on Earth. So air.IsValid() is false for every realistic
            // atmosphere, and trusting it reports vacuum at ground level. Check the terms this
            // actually divides by instead.
            //
            // No reference, or one with nothing physical in it, is the model saying there is no
            // air — which is KNOWLEDGE, not a failed read, and every airless body in the game
            // answers this way. Read as the reference density it puts Earth's sea-level air on the
            // Moon: a bomb released 7 m over lunar ground reaches a terminal 113 m/s, and every gun
            // lay on an airless body is solved through drag that is not there.
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
    /// Why <see cref="MediumDensityRatioAt(Celestial, double3)"/> answered what it did, for the
    /// log. Every early return there is silent, and from outside a vacuum reading and an
    /// unreadable atmosphere are the same 0.0.
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
