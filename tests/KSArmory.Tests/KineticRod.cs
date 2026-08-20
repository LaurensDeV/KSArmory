using Brutal.Numerics;

namespace KSArmory.Tests;

/// <summary>
/// A tungsten rod dropped from orbit, and the aerodynamics that decide what its fins can do.
///
/// <para>Shared by the measurements in <see cref="KineticRodTests"/> so the rod is one definition
/// rather than a constant block per test. The planet, the air and the ground come from
/// <see cref="DeorbitShot"/>, so a rod and a Mk 21 are flown through exactly the same world.</para>
///
/// <para><b>The planet sits at the origin and does not move.</b> Same limit as
/// <see cref="DeorbitShot"/>: a frame carrier is identically zero here, so nothing measured through
/// this rig can see an epoch fault in a term differenced against a body sample. The planet's
/// <em>spin</em> is real, so the steering law is still asked to lead a target moving at up to
/// 465 m/s.</para>
/// </summary>
internal static class KineticRod
{
    // ---- the rod ---------------------------------------------------------

    /// <summary>Project Thor's rod: 6.1 m by 0.3 m of tungsten, which is what fixes every number below.</summary>
    public const double LengthMetres = 6.1;

    /// <inheritdoc cref="LengthMetres"/>
    public const double DiameterMetres = 0.3;

    /// <summary>Tungsten, kg/m^3.</summary>
    public const double DensityKgPerM3 = 19_250.0;

    /// <summary>Sea-level air, kg/m^3 — what <c>DeorbitShot.DensityAt</c>'s ratio of 1.0 means.</summary>
    public const double SeaLevelAirKgPerM3 = 1.225;

    /// <summary>Axial drag coefficient on <see cref="FrontalAreaM2"/>: a slender body at hypersonic speed.</summary>
    public const double AxialDragCoefficient = 0.15;

    public static double MassKg => Math.PI * (DiameterMetres / 2.0) * (DiameterMetres / 2.0)
                                   * LengthMetres * DensityKgPerM3;

    public static double FrontalAreaM2 => Math.PI * (DiameterMetres / 2.0) * (DiameterMetres / 2.0);

    /// <summary>The side the rod presents at angle of attack, which is what its lift comes off.</summary>
    public static double PlanformAreaM2 => LengthMetres * DiameterMetres;

    /// <summary>
    /// <c>k</c> in <c>a = -k|v|v</c> at sea level, derived from the rod rather than chosen —
    /// <c>0.5 * rho * Cd * A / m</c>, which is what <see cref="MunitionProfile.DragK"/> means.
    /// </summary>
    public static double DragK
        => 0.5 * SeaLevelAirKgPerM3 * AxialDragCoefficient * FrontalAreaM2 / MassKg;

    /// <summary>Mass over drag area, the number that says how deep into the air a body gets before it slows.</summary>
    public static double BallisticCoefficient => MassKg / (AxialDragCoefficient * FrontalAreaM2);

    /// <summary>
    /// Lateral acceleration the rod can hold at <paramref name="alphaDeg"/> of angle of attack, in g.
    ///
    /// <para>Newtonian impact theory — <c>C_N = 2 sin^2(alpha)</c> on the planform area — which is
    /// the right model at these Mach numbers and is generous rather than conservative: it credits
    /// the whole flank as a flat plate. <b>Nothing in the mod computes this.</b>
    /// <see cref="MunitionProfile.MaxLateralG"/> is a fiat limit and
    /// <see cref="Interceptor.GuidanceAccel"/> applies it whatever the air is doing, so this is the
    /// physics the mod would need to acquire before a rod's authority meant anything.</para>
    /// </summary>
    public static double AvailableG(double dynamicPressurePa, double alphaDeg)
    {
        double sin = Math.Sin(alphaDeg * Math.PI / 180.0);
        double normalForce = dynamicPressurePa * PlanformAreaM2 * 2.0 * sin * sin;

        return normalForce / (MassKg * 9.80665);
    }

    /// <summary>Dynamic pressure where a round is, from the same air the round is flying through.</summary>
    public static double DynamicPressure(double densityRatio, double speed)
        => 0.5 * SeaLevelAirKgPerM3 * densityRatio * speed * speed;

    /// <summary>
    /// The rod as a munition. Its authority is the only thing that varies between runs, because it
    /// is the whole question.
    /// </summary>
    /// <param name="maxLateralG">Fin authority ceiling, in g.</param>
    /// <param name="guidance">Whether it steers at all.</param>
    /// <param name="gravityCompensation">
    /// How much of local gravity the steering biases out before it commands anything. Zero is what
    /// the B61's tail kit uses, on the reasoning that a bomb's autopilot steers the fall rather than
    /// resisting it.
    /// </param>
    public static MunitionProfile Profile(double maxLateralG,
                                          GuidanceMode guidance = GuidanceMode.Inertial,
                                          double gravityCompensation = 0.0) => new()
    {
        Name = "ROD",
        DisplayName = "kinetic rod",

        Guidance = guidance,
        NavConstant = 3f,
        MaxLateralG = (float)maxLateralG,

        GravityCompensation = (float)gravityCompensation,

        LaunchSpeed = 0f,
        BoostSeconds = 0f,
        BoostAccel = 0f,

        MinRange = 0f,
        MaxRange = 20_000_000f,
        MaxFlightSeconds = 3600f,

        DragK = (float)DragK,

        // It kills by arriving. Nothing goes off.
        FuseRadius = 0f,
        FuseArmSeconds = 10f,
        ChargeKg = 0f,

        HitsTerrain = true,
    };

    // ---- flying it -------------------------------------------------------

    /// <summary>Where a rod is and what it is doing when it reaches the top of the air.</summary>
    /// <param name="Altitude">Height above the mean sphere (m).</param>
    /// <param name="Speed">Inertial speed (m/s).</param>
    /// <param name="GammaDeg">Flight path angle <em>below</em> the local horizontal (degrees).</param>
    public readonly record struct Entry(double Altitude, double Speed, double GammaDeg)
    {
        /// <summary>The state itself, in the plane z = 0, coming down along +y.</summary>
        public (double3 Position, double3 Velocity) StateCci()
        {
            double3 position = new(DeorbitShot.R + Altitude, 0, 0);
            double gamma = GammaDeg * Math.PI / 180.0;

            double3 up = new(1, 0, 0);
            double3 downrange = new(0, 1, 0);

            return (position, ((downrange * Math.Cos(gamma)) - (up * Math.Sin(gamma))) * Speed);
        }
    }

    /// <summary>What one flight did.</summary>
    /// <param name="GroundFixedCci">Where it came down, in the body's own rotating frame.</param>
    /// <param name="Seconds">How long it took.</param>
    /// <param name="ArrivalSpeed">How fast it was going when it got there.</param>
    /// <param name="ArrivalGammaDeg">How steeply, below the local horizontal.</param>
    /// <param name="PeakG">The most lateral acceleration the steering ever commanded, in g.</param>
    /// <param name="PeakDynamicPressurePa">The most dynamic pressure it saw.</param>
    /// <param name="SteeredSeconds">How long the steering was allowed to command anything.</param>
    public readonly record struct Flight(double3 GroundFixedCci, double Seconds,
                                         double ArrivalSpeed, double ArrivalGammaDeg,
                                         double PeakG, double PeakDynamicPressurePa,
                                         double SteeredSeconds);

    /// <summary>
    /// How much lateral acceleration the fins may command, given the air the rod is in.
    ///
    /// <para><see cref="Fiat"/> is what <see cref="Slug"/> does today: the profile's ceiling,
    /// everywhere, including vacuum. <see cref="FromDynamicPressure"/> is what a fin-steered body
    /// would actually have.</para>
    /// </summary>
    public abstract class Authority
    {
        public abstract double AvailableG(double dynamicPressurePa);

        /// <summary>The most it could ever command, whatever the air.</summary>
        public abstract double CeilingG { get; }

        /// <summary>The profile's number, whatever the air is doing.</summary>
        public static Authority Fiat(double g) => new FiatAuthority(g);

        /// <summary>Newtonian body lift at a trim angle, capped by a structural ceiling.</summary>
        public static Authority FromDynamicPressure(double alphaDeg, double ceilingG)
            => new AeroAuthority(alphaDeg, ceilingG);

        private sealed class FiatAuthority(double g) : Authority
        {
            public override double CeilingG => g;

            public override double AvailableG(double dynamicPressurePa) => g;
        }

        private sealed class AeroAuthority(double alphaDeg, double ceilingG) : Authority
        {
            public override double CeilingG => ceilingG;

            public override double AvailableG(double dynamicPressurePa)
                => Math.Min(ceilingG, KineticRod.AvailableG(dynamicPressurePa, alphaDeg));
        }
    }

    /// <summary>Below this the fins are doing nothing anyone would notice.</summary>
    public const double UsableG = 0.01;

    /// <summary>
    /// Fly one rod from an entry state to the ground, aimed at a place fixed to the turning body.
    ///
    /// <para>The step is coarse in vacuum and fine once there is air, which is what
    /// <c>WarpPolicy</c> holds the world to through <see cref="IProjectile.FaithfulStepSeconds"/>.
    /// Authority is re-read per frame from the air the rod is actually in, which is the whole
    /// point of the exercise.</para>
    /// </summary>
    /// <param name="aimGroundFixed">
    /// Where it is told to go, as a point in the body's rotating frame — the same thing an
    /// <see cref="AimpointKind.Ground"/> designation is, and re-carried every frame the way
    /// <c>WeaponSystem.SampleTarget</c> re-reads one.
    /// </param>
    /// <param name="authority">Null steers with the profile's own ceiling and never varies it.</param>
    /// <param name="stepInAir">
    /// The frame the round is handed once there is air, which is what <c>WarpPolicy</c> holds the
    /// world to through <see cref="IProjectile.FaithfulStepSeconds"/>. A rod arrives four times
    /// faster than a Mk 21 does, so what that step costs is its own question.
    /// </param>
    public static Flight Fly(MunitionProfile munition, Entry entry, double3? aimGroundFixed,
                             Authority? authority = null, double stepInAir = 0.02)
    {
        (double3 from, double3 velocity) = entry.StateCci();

        return Fly(munition, from, velocity, aimGroundFixed, authority, stepInAir);
    }

    /// <inheritdoc cref="Fly(MunitionProfile, Entry, double3?, Authority, double)"/>
    /// <param name="from">Where the rod is let go, in the body's inertial frame.</param>
    /// <param name="velocity">What it is doing there.</param>
    public static Flight Fly(MunitionProfile munition, double3 from, double3 velocity,
                             double3? aimGroundFixed, Authority? authority = null,
                             double stepInAir = 0.02)
    {
        ArgumentNullException.ThrowIfNull(munition);

        BallisticBody body = DeorbitShot.Earth;

        Slug rod = new(from, velocity, null, 1, from, Vec.Zero)
        {
            Munition = munition,
            Ground = new DeorbitShot.Ball(),
            AirDensityAt = (pos, _) => DeorbitShot.DensityAt(pos),
        };

        double elapsed = 0.0;
        double peakG = 0.0;
        double peakQ = 0.0;
        double steered = 0.0;
        double ceiling = munition.MaxLateralG;

        while (rod.State == RoundState.Flying && elapsed < 3600.0)
        {
            double density = DeorbitShot.DensityAt(rod.PositionEcl);
            double3 air = body.GroundVelocityCci(rod.PositionEcl);
            double q = DynamicPressure(density, Vec.Len(rod.VelocityEcl - air));

            peakQ = Math.Max(peakQ, q);

            if (authority is not null) munition.MaxLateralG = (float)authority.AvailableG(q);

            // The step the world would be held to: fine where there is air, coarse above it.
            double dt = density > Medium.NoticeableDensity ? stepInAir : 0.5;

            TargetState? target = null;
            if (aimGroundFixed is { } aim && munition.MaxLateralG > UsableG)
            {
                // Sampled at the END of the step, which is the instant the world is read at. The
                // round back-dates it to its own sub-step from there.
                double3 at = body.CarryCci(aim, elapsed + dt);
                target = new TargetState(at, body.GroundVelocityCci(at), 0.0);
                steered += dt;
            }

            rod.Update(dt, target, body.GravityCci(rod.PositionEcl), air, from, munition, density);

            peakG = Math.Max(peakG, Vec.Len(rod.SteeringCommandEcl) / 9.80665);
            elapsed += dt;
        }

        munition.MaxLateralG = (float)ceiling;

        double seconds = elapsed + Math.Min(0.0, rod.DetonationElapsedInFrame);
        double3 arrivalAir = body.GroundVelocityCci(rod.PositionEcl);
        double3 arrivalVelocity = rod.VelocityEcl - arrivalAir;
        double gamma = 90.0 - (Vec.AngleBetween(rod.PositionEcl, arrivalVelocity) * 180.0 / Math.PI);

        return new Flight(body.UncarryCci(rod.PositionEcl, seconds), seconds,
                          Vec.Len(arrivalVelocity), -gamma, peakG, peakQ, steered);
    }

    /// <summary>
    /// How far a rod entering like this can still be steered, walked up a ladder of offsets rather
    /// than bisected.
    ///
    /// <para>Bisection would assume the correctable offsets are an interval, and there is no reason
    /// they should be: a rod at 7.4 km/s a hundred kilometres up is very nearly in orbit, so a
    /// small pitch change moves the impact enormously and the relation between what is asked and
    /// what arrives need not be monotone. Walking up and stopping at the first failure answers the
    /// question that was actually asked — <em>every</em> miss up to here is removed — and says what
    /// the first one it could not do was.</para>
    /// </summary>
    /// <param name="lateral">
    /// Which way to displace the aim from where the fall was going: <c>true</c> across the
    /// trajectory plane, <c>false</c> along it. The two are not the same problem — a rod near
    /// orbital speed extends its range by pitching up, which costs nothing like a turn.
    /// </param>
    /// <param name="tolerance">How close counts as arrived (m).</param>
    /// <param name="ladder">Offsets to try, smallest first.</param>
    public static (double Corrected, double FirstFailure, double FailureResidual) Envelope(
        Entry entry, Authority authority, bool lateral,
        double tolerance = 25.0, IReadOnlyList<double>? ladder = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ladder ??= Ladder();

        double3 unguided = Fly(Profile(0.0, GuidanceMode.None), entry, null).GroundFixedCci;
        double corrected = 0.0;

        foreach (double offset in ladder)
        {
            double3 aim = Displace(unguided, offset, lateral);
            Flight flight = Fly(Profile(authority.CeilingG), entry, aim, authority);
            double residual = DeorbitShot.GroundMetres(flight.GroundFixedCci, aim);

            if (residual > tolerance) return (corrected, offset, residual);

            corrected = offset;
        }

        return (corrected, double.PositiveInfinity, 0.0);
    }

    /// <summary>Offsets to walk, 25 m to half a thousand kilometres in even ratios.</summary>
    public static IReadOnlyList<double> Ladder()
    {
        List<double> rungs = [];
        for (double d = 25.0; d <= 500_000.0; d *= 1.35) rungs.Add(d);

        return rungs;
    }

    /// <summary>What one offset costs, for a reader who wants the shape rather than the edge.</summary>
    public static double Residual(Entry entry, Authority authority, bool lateral, double offset,
                                  double stepInAir = 0.02)
    {
        ArgumentNullException.ThrowIfNull(authority);

        double3 unguided = Fly(Profile(0.0, GuidanceMode.None), entry, null, null, stepInAir)
            .GroundFixedCci;
        double3 aim = Displace(unguided, offset, lateral);
        Flight flight = Fly(Profile(authority.CeilingG), entry, aim, authority, stepInAir);

        return DeorbitShot.GroundMetres(flight.GroundFixedCci, aim);
    }

    /// <summary>
    /// Where a rod landed relative to where it was aimed, split into the two axes that have
    /// different causes: along the trajectory plane, and across it. Down-track is positive
    /// <b>long</b>.
    ///
    /// <para>One distance hides the whole diagnosis. A steering law that flattens the arc lands
    /// tens of kilometres short while removing the cross-track error perfectly, and the scalar
    /// miss reads as though nothing worked.</para>
    /// </summary>
    public static (double DownTrack, double CrossTrack) MissComponents(double3 landed, double3 aim)
    {
        double3 up = Vec.Unit(aim);
        double3 normal = new(0, 0, 1);
        double3 downTrack = Vec.Unit(Vec.Cross(normal, up));

        double3 offset = landed - aim;

        return (Vec.Dot(offset, downTrack), Vec.Dot(offset, normal));
    }

    /// <summary>
    /// Move a point on the sphere by <paramref name="metres"/>, across the trajectory plane or
    /// along it. The plane is z = 0 by construction of <see cref="Entry.StateCci"/>, so across it
    /// is +z and along it is the remaining tangent.
    /// </summary>
    public static double3 Displace(double3 groundPoint, double metres, bool lateral)
    {
        double3 up = Vec.Unit(groundPoint);
        double3 normal = new(0, 0, 1);

        // The plane's normal is +z and every ground point here lies in it, so +z is already the
        // out-of-plane tangent and the in-plane one is what is left.
        double3 direction = lateral ? normal : Vec.Unit(Vec.Cross(normal, up));

        return Vec.Unit(groundPoint + (direction * metres)) * DeorbitShot.R;
    }
}
