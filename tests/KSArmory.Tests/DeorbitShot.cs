using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The 3,459 km near-orbital shot every ballistic budget is measured on, and the one flight model
/// they all fly it with.
///
/// <para>Lifted out of the suites that share it so that two budgets cannot disagree about what the
/// shot <em>is</em> — the planet, the air, the warhead and the arc are one definition here rather
/// than a constant block copied per file.</para>
///
/// <para><b>The planet sits at the origin and does not move.</b> That is the one case where a frame
/// carrier is identically zero, so nothing measured through this rig can see an epoch fault in a
/// term differenced against a body sample. <c>docs/FRAMES-AND-EPOCHS.md</c> has why, and
/// <c>AirSampleEpochTests</c> is where that convention is pinned instead.</para>
/// </summary>
internal static class DeorbitShot
{
    public const double Mu = 3.986004418e14;
    public const double R = 6_371_000.0;
    public const double ScaleHeight = 8_000.0;
    public const double EarthSpin = 7.2921159e-5;

    /// <summary>How far downrange the aim point sits, along the track from the pickup.</summary>
    public const double RangeMetres = 3_459_000.0;

    /// <summary>Where the shot is picked up, which is what the flown scenario resumes into.</summary>
    public const double PickupAltitude = 200_000.0;

    public static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), EarthSpin);

    public static MunitionProfile Warhead => Arsenal.ReentryVehicleMk21;

    public static double DensityAt(double3 pointCci)
        => Math.Exp(-Math.Max(0.0, Vec.Len(pointCci) - R) / ScaleHeight);

    /// <summary>Ground distance between two places on the mean sphere.</summary>
    public static double GroundMetres(double3 a, double3 b) => R * Vec.AngleBetween(a, b);

    /// <summary>The mean sphere, as the thing a round asks where the ground is.</summary>
    public sealed class Ball : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Vec.Zero;
            surfaceRadius = R;
            return true;
        }
    }

    /// <summary>
    /// The shot: picked up at near-orbital speed 200 km up, aimed 3,459 km downrange.
    /// </summary>
    public static BallisticArc.Solution Shot(out double3 from, out double3 target)
    {
        from = new double3(R + PickupAltitude, 0, 0);
        target = new double3(R * Math.Cos(RangeMetres / R), R * Math.Sin(RangeMetres / R), 0);

        double3 circular = new(0, Math.Sqrt(Mu / (R + PickupAltitude)), 0);
        Assert.True(BallisticArc.TryCheapest(Earth, from, circular, target, out BallisticArc.Solution s));
        return s;
    }

    /// <summary>Where a warhead released from this state comes down, as a place on the ground.</summary>
    public static double3 Land(double3 fromCci, double3 velocityCci)
    {
        Assert.True(ImpactPredictor.TryPredict(Earth, fromCci, velocityCci, 1.0, 20_000.0,
                                               out ImpactPredictor.Impact hit, null, null,
                                               new ImpactPredictor.Drag(DensityAt, Warhead)));
        return hit.GroundFixedPointCci;
    }

    /// <summary>
    /// Which of the round's frame-level inputs are re-read per sub-step rather than held for the
    /// whole frame.
    ///
    /// <para>Neither is what the game does: <c>WeaponSystem</c> samples gravity and the air's motion
    /// once, at the round's position at the top of the frame, and <see cref="Slug"/> holds both
    /// across every 5 ms sub-step inside it.</para>
    ///
    /// <para>The ground under the round is sampled the same way and has no switch here, because
    /// this rig answers with a sphere at the origin: re-sampling it cannot change the answer. Only
    /// a real height field makes it a term.</para>
    /// </summary>
    /// <param name="Gravity">Re-evaluate gravity at the round's own position each sub-step.</param>
    /// <param name="AirMotion">Re-evaluate the air's own velocity each sub-step.</param>
    public readonly record struct Refresh(bool Gravity, bool AirMotion)
    {
        /// <summary>Nothing re-read: the round exactly as the game flies it.</summary>
        public static Refresh AsFlown => new(false, false);

        public bool Any => Gravity || AirMotion;
    }

    /// <summary>The round as the game flies it: sub-stepped, air re-read per sub-step, ground sphere.</summary>
    /// <param name="dt">The frame the round is handed, which is what the world is warped to.</param>
    /// <param name="refresh">Which frame-level inputs to re-read per sub-step instead of holding.</param>
    public static (double3 GroundFixed, double Seconds) FlyTheRound(double3 fromCci, double3 velocityCci,
                                                                   double dt,
                                                                   Refresh refresh = default)
    {
        BallisticBody body = Earth;

        Slug round = new(fromCci, velocityCci, null, 1, fromCci, Vec.Zero)
        {
            Munition = Warhead,
            Ground = new Ball(),
            AirDensityAt = (pos, _) => DensityAt(pos),
        };

        double elapsed = 0.0;

        for (int i = 0; i < (int)(20_000.0 / dt) && round.State == RoundState.Flying; i++)
        {
            // A frame split into 5 ms Updates re-reads every frame-level input, because each of
            // them is then one sub-step long. Holding the ones not being refreshed at the frame's
            // own sample is what leaves the difference being one named term rather than all of them.
            int n = refresh.Any
                    ? Math.Max(1, (int)Math.Ceiling(dt / Interceptor.SubStep))
                    : 1;

            double3 heldGravity = body.GravityCci(round.PositionEcl);
            double3 heldAir = body.GroundVelocityCci(round.PositionEcl);

            for (int k = 0; k < n && round.State == RoundState.Flying; k++)
            {
                double3 gravity = refresh.Gravity ? body.GravityCci(round.PositionEcl) : heldGravity;
                double3 air = refresh.AirMotion ? body.GroundVelocityCci(round.PositionEcl) : heldAir;

                round.Update(dt / n, null, gravity, air, fromCci, Warhead,
                             DensityAt(round.PositionEcl));
                elapsed += dt / n;
            }
        }

        return Arrived(body, round, elapsed);
    }

    /// <summary>
    /// Where the round stopped, as a place on the ground, and how long it really took to get there.
    ///
    /// <para>The flight time is the frames issued <em>less</em> the part of the last one the round
    /// did not need: a round stops on a sub-step, so counting whole frames overshoots by up to one
    /// of them. Un-carrying by that turns the overshoot into ground — 465 m a second at the equator,
    /// which on a 320 ms frame is enough to read as guidance error.</para>
    /// </summary>
    private static (double3 GroundFixed, double Seconds) Arrived(BallisticBody body, Slug round,
                                                                 double framesIssued)
    {
        Assert.NotEqual(RoundState.Flying, round.State);

        double seconds = framesIssued + Math.Min(0.0, round.DetonationElapsedInFrame);
        return (body.UncarryCci(round.PositionEcl, seconds), seconds);
    }

    /// <summary>
    /// The speed <c>Ksa/BallisticScenario.cs</c> asks for once the salvo is away, which is what
    /// sets the frame the coast is flown at. <c>WarpPolicy</c> then slows it for the entry.
    /// </summary>
    public const double ScenarioWarp = 8.0;

    /// <summary>A frame at 60 fps, which is what a warp factor multiplies.</summary>
    public const double NominalFrame = 1.0 / 60.0;

    /// <summary>
    /// The round at the step the world is actually held to: coarse through the vacuum coast, fine
    /// once there is air.
    ///
    /// <para>That is what <c>WarpPolicy</c> asks for through <c>IProjectile.FaithfulStepSeconds</c>,
    /// and it is not the same as either constant step — the coast runs at whatever warp the player
    /// (or the scenario) asked for, and the entry pulls it back to <see cref="Medium.FaithfulStepInAir"/>.
    /// </para>
    /// </summary>
    /// <param name="warp">The simulation speed held during the coast.</param>
    public static (double3 GroundFixed, double Seconds) FlyTheRoundAsWarped(
        double3 fromCci, double3 velocityCci, double warp, Refresh refresh = default)
    {
        BallisticBody body = Earth;

        Slug round = new(fromCci, velocityCci, null, 1, fromCci, Vec.Zero)
        {
            Munition = Warhead,
            Ground = new Ball(),
            AirDensityAt = (pos, _) => DensityAt(pos),
        };

        double elapsed = 0.0;
        double dt = NominalFrame;

        while (round.State == RoundState.Flying && elapsed < 20_000.0)
        {
            int n = refresh.Any
                    ? Math.Max(1, (int)Math.Ceiling(dt / Interceptor.SubStep))
                    : 1;

            double3 heldGravity = body.GravityCci(round.PositionEcl);
            double3 heldAir = body.GroundVelocityCci(round.PositionEcl);

            for (int k = 0; k < n && round.State == RoundState.Flying; k++)
            {
                double3 gravity = refresh.Gravity ? body.GravityCci(round.PositionEcl) : heldGravity;
                double3 air = refresh.AirMotion ? body.GroundVelocityCci(round.PositionEcl) : heldAir;

                round.Update(dt / n, null, gravity, air, fromCci, Warhead,
                             DensityAt(round.PositionEcl));
                elapsed += dt / n;
            }

            // What the mod asks the world for on the next frame, capped by the speed the scenario
            // runner asks for once the salvo is away.
            dt = Math.Clamp(round.FaithfulStepSeconds, NominalFrame, warp * NominalFrame);
        }

        return Arrived(body, round, elapsed);
    }

    /// <summary>The widest gap between any two of a group's impacts, on the ground.</summary>
    public static double Spread(IReadOnlyList<double3> landed)
    {
        double worst = 0.0;
        for (int a = 0; a < landed.Count; a++)
        {
            for (int b = a + 1; b < landed.Count; b++)
            {
                worst = Math.Max(worst, GroundMetres(landed[a], landed[b]));
            }
        }

        return worst;
    }

    /// <summary>How far the group's own centre sits from where it was aimed.</summary>
    public static double CommonBias(IReadOnlyList<double3> landed, double3 target)
    {
        double3 sum = Vec.Zero;
        foreach (double3 p in landed) sum += p;

        return GroundMetres(Vec.Unit(sum) * R, target);
    }
}
