using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What a ground sample held for a whole frame costs, and what re-reading it per sub-step buys.
///
/// <para><c>docs/KINETIC-FLOOR.md</c> prices this term analytically as
/// <c>s*d / (tan y + s)</c> — slope, a frame's ground track and the arrival angle. Everything here
/// is the same term flown rather than derived, through the real <see cref="Slug"/> against a
/// surface that actually slopes.</para>
///
/// <para><b>The rig's planet sits at the origin and does not move</b>, the same limit
/// <see cref="DeorbitShot"/> carries: a frame carrier is identically zero here, so nothing measured
/// through this can see an epoch fault. That is deliberate for this measurement — the sample is not
/// back-dated, and why is on <see cref="IGroundTest"/>.</para>
/// </summary>
public class GroundSampleRateTests(ITestOutputHelper Out)
{
    /// <summary>The frame the world is held to once there is air, which is what a warhead re-enters on.</summary>
    private const double FrameInAir = Medium.FaithfulStepInAir;

    /// <summary>Fine enough that one frame is one sub-step, so the ground is sampled as often as the round steps.</summary>
    private const double ConvergedFrame = 0.001;

    // Ground that climbs linearly with the down-track angle. A constant gradient is what turns
    // "the sample is stale by the track it covered" into a number: every metre of track the round
    // gets ahead of its sample is `gradient` metres of height it is wrong about.
    private sealed class Slope(double gradient) : IGroundTest
    {
        public int Samples { get; private set; }

        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            Samples++;

            centreEcl = Vec.Zero;
            surfaceRadius = DeorbitShot.R
                            + (gradient * Math.Atan2(positionEcl.Y, positionEcl.X) * DeorbitShot.R);

            return true;
        }
    }

    // A Mk 21 built here rather than taken from the arsenal: the profiles are shared mutable
    // instances, so switching a field on one would reach every other test in the run.
    private static MunitionProfile Warhead(bool perSubStep) => new()
    {
        Name = "MK21",
        DisplayName = "Mk 21 reentry vehicle",

        Guidance = GuidanceMode.None,

        LaunchSpeed = 0f,
        BoostSeconds = 0f,
        BoostAccel = 0f,

        MinRange = 0f,
        MaxRange = 20_000_000f,
        MaxFlightSeconds = 3600f,

        DragK = 1.5e-5f,

        FuseRadius = 0f,
        FuseArmSeconds = 10f,
        ChargeKg = 0f,

        HitsTerrain = true,
        SamplesGroundPerSubStep = perSubStep,
    };

    private readonly record struct Arrival(double3 GroundFixed, double GammaDeg, double Speed,
                                           int Samples, int Frames);

    // One re-entry, flown to the ground. Coarse in vacuum and `frameInAir` once there is air,
    // which is what WarpPolicy holds the world to through IProjectile.FaithfulStepSeconds.
    private static Arrival Fly(MunitionProfile munition, KineticRod.Entry entry,
                               double frameInAir, double slope)
    {
        BallisticBody body = DeorbitShot.Earth;
        Slope ground = new(slope);

        (double3 from, double3 velocity) = entry.StateCci();

        Slug round = new(from, velocity, null, 1, from, Vec.Zero)
        {
            Munition = munition,
            Ground = ground,
            AirDensityAt = (pos, _) => DeorbitShot.DensityAt(pos),
        };

        double elapsed = 0.0;
        int frames = 0;

        while (round.State == RoundState.Flying && elapsed < 3600.0)
        {
            double density = DeorbitShot.DensityAt(round.PositionEcl);
            double3 air = body.GroundVelocityCci(round.PositionEcl);
            double dt = density > Medium.NoticeableDensity ? frameInAir : 0.5;

            round.Update(dt, null, body.GravityCci(round.PositionEcl), air, from, munition, density);

            elapsed += dt;
            frames++;
        }

        Assert.NotEqual(RoundState.Flying, round.State);

        double seconds = elapsed + Math.Min(0.0, round.DetonationElapsedInFrame);
        double3 arrivalVelocity = round.VelocityEcl - body.GroundVelocityCci(round.PositionEcl);
        double gamma = (Vec.AngleBetween(round.PositionEcl, arrivalVelocity) * 180.0 / Math.PI) - 90.0;

        return new Arrival(body.UncarryCci(round.PositionEcl, seconds), gamma,
                           Vec.Len(arrivalVelocity), ground.Samples, frames);
    }

    /// <summary>Entries that arrive at roughly 7, 30 and 88 degrees, which is the span the budget is written over.</summary>
    public static TheoryData<double, double> Entries => new()
    {
        { 7.5, 7_500.0 },
        { 30.0, 4_000.0 },
        { 88.0, 1_000.0 },
    };

    // ---------------------------------------------------------------- the regression

    /// <summary>
    /// <b>The regression.</b> A warhead must stop on the ground under where it <em>crosses</em>,
    /// not under where it was at the top of the frame.
    ///
    /// <para>Flown three ways against one sloping surface: held for the frame, re-read per sub-step,
    /// and a converged reference whose frame is one sub-step long. Re-reading has to remove most of
    /// the gap to the reference — with the flag ignored the two flights are the same flight and this
    /// cannot pass.</para>
    /// </summary>
    [Fact]
    public void ResamplingTheGroundStopsTheRoundOnTheHeightUnderTheCrossing()
    {
        KineticRod.Entry entry = new(200_000.0, 7_500.0, 7.5);
        const double slope = 0.05;

        Arrival held = Fly(Warhead(false), entry, FrameInAir, slope);
        Arrival resampled = Fly(Warhead(true), entry, FrameInAir, slope);
        Arrival reference = Fly(Warhead(true), entry, ConvergedFrame, slope);

        double heldOff = DeorbitShot.GroundMetres(held.GroundFixed, reference.GroundFixed);
        double resampledOff = DeorbitShot.GroundMetres(resampled.GroundFixed, reference.GroundFixed);

        Out.WriteLine($"arrival {held.GammaDeg:F1} deg at {held.Speed:N0} m/s, {slope * 100:F0}% slope");
        Out.WriteLine($"  held for the frame:  {heldOff,8:F1} m from the converged flight");
        Out.WriteLine($"  re-read per sub-step:{resampledOff,8:F1} m");
        Out.WriteLine($"  moved: {DeorbitShot.GroundMetres(held.GroundFixed, resampled.GroundFixed):F1} m");

        Assert.True(resampledOff < heldOff * 0.5,
                    $"re-reading the ground per sub-step left {resampledOff:F1} m against "
                    + $"{heldOff:F1} m held for the frame; it has to remove most of that");
    }

    /// <summary>
    /// The flag is what decides it, and off is what every round in the arsenal but one gets.
    ///
    /// <para>One <c>Update</c> spanning ten sub-steps asks the terrain once with the flag off and
    /// once per sub-step with it on. A round that says nothing about it therefore costs exactly what
    /// it always did.</para>
    /// </summary>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 10)]
    public void TheProfileDecidesHowOftenOneFrameAsksTheTerrain(bool perSubStep, int expected)
    {
        MunitionProfile munition = Warhead(perSubStep);
        Slope ground = new(0.0);

        double3 from = new(DeorbitShot.R + 10_000.0, 0, 0);
        Slug round = new(from, new double3(0, 1_000.0, 0), null, 1, from, Vec.Zero)
        {
            Munition = munition,
            Ground = ground,
        };

        // Ten sub-steps: FaithfulStepInAir is 50 ms and Interceptor.SubStep is 5 ms.
        round.Update(FrameInAir, null, DeorbitShot.Earth.GravityCci(from), Vec.Zero, from,
                     munition, 1.0);

        Assert.Equal(RoundState.Flying, round.State);
        Assert.Equal(expected, ground.Samples);
    }

    /// <summary>The shipped arsenal asks for it where a frame's ground track is metres, and nowhere else.</summary>
    [Fact]
    public void OnlyTheReentryVehicleAsksForIt()
    {
        Assert.True(Arsenal.ReentryVehicleMk21.SamplesGroundPerSubStep);

        foreach (MunitionProfile munition in Arsenal.Munitions)
        {
            if (ReferenceEquals(munition, Arsenal.ReentryVehicleMk21)) continue;

            Assert.False(munition.SamplesGroundPerSubStep,
                         $"{munition.Name} asks for a terrain sample per sub-step; only a round that "
                         + "covers hundreds of metres of ground in one frame is worth it");
        }
    }

    // ---------------------------------------------------------------- what it buys

    /// <summary>
    /// What it is worth, by arrival angle. Measurement, not an assertion.
    ///
    /// <para>Every one of these terms is a <em>height</em>, and a height becomes ground in
    /// proportion to <c>cot(gamma)</c> — so the same stale sample is tens of metres on the deorbit
    /// the mod flies and centimetres on a vertical drop.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Entries))]
    public void WhatAStaleGroundSampleCostsByArrivalAngle(double entryGammaDeg, double entrySpeed)
    {
        KineticRod.Entry entry = new(200_000.0, entrySpeed, entryGammaDeg);

        Arrival reference = Fly(Warhead(true), entry, ConvergedFrame, 0.05);
        Out.WriteLine($"entry {entryGammaDeg:F1} deg at {entrySpeed:N0} m/s "
                      + $"-> arrival {reference.GammaDeg:F1} deg at {reference.Speed:N0} m/s");

        foreach (double slope in new[] { 0.01, 0.05, 0.20 })
        {
            Arrival converged = Fly(Warhead(true), entry, ConvergedFrame, slope);
            Arrival held = Fly(Warhead(false), entry, FrameInAir, slope);
            Arrival resampled = Fly(Warhead(true), entry, FrameInAir, slope);

            Out.WriteLine($"  {slope * 100,3:F0}% slope: held {DeorbitShot.GroundMetres(held.GroundFixed, converged.GroundFixed),8:F2} m,"
                          + $" re-read {DeorbitShot.GroundMetres(resampled.GroundFixed, converged.GroundFixed),7:F2} m,"
                          + $" apart {DeorbitShot.GroundMetres(held.GroundFixed, resampled.GroundFixed),8:F2} m");
        }
    }

    /// <summary>
    /// And by the frame the world is running at, which is what timewarp moves.
    ///
    /// <para>The term is linear in the ground track a frame covers, so it is linear in the step —
    /// which is the same reason <c>WarpPolicy</c> exists at all.</para>
    /// </summary>
    [Fact]
    public void WhatAStaleGroundSampleCostsByFrameSize()
    {
        KineticRod.Entry entry = new(200_000.0, 7_500.0, 7.5);
        const double slope = 0.05;

        Arrival converged = Fly(Warhead(true), entry, ConvergedFrame, slope);
        Out.WriteLine($"7.5 deg entry, {slope * 100:F0}% slope, arriving {converged.GammaDeg:F1} deg "
                      + $"at {converged.Speed:N0} m/s");

        foreach (double frame in new[] { 1.0 / 60.0, FrameInAir, 0.16, 0.32 })
        {
            Arrival held = Fly(Warhead(false), entry, frame, slope);
            Arrival resampled = Fly(Warhead(true), entry, frame, slope);

            Out.WriteLine($"  {frame * 1000,5:F0} ms frame: held "
                          + $"{DeorbitShot.GroundMetres(held.GroundFixed, converged.GroundFixed),8:F2} m,"
                          + $" re-read {DeorbitShot.GroundMetres(resampled.GroundFixed, converged.GroundFixed),7:F2} m"
                          + $"   ({held.Samples:N0} -> {resampled.Samples:N0} terrain queries over the flight)");
        }
    }

    // ---------------------------------------------------------------- what it costs

    /// <summary>
    /// What it costs, counted rather than timed.
    ///
    /// <para><b>Wall-clock cost has not been measured</b> — a terrain query here is a lambda over a
    /// sphere, and the real one is a bicubic over a cubemap plus Earth's whole modifier stack. What
    /// can be said headlessly is how many of them there are, which is the number the CIWS objection
    /// is actually about.</para>
    /// </summary>
    [Fact]
    public void WhatResamplingTheGroundCostsInTerrainQueries()
    {
        int perFrame = (int)Math.Ceiling(FrameInAir / Interceptor.SubStep);

        Out.WriteLine($"one round, one {FrameInAir * 1000:F0} ms frame: 1 query held, "
                      + $"{perFrame} re-read");
        Out.WriteLine($"  six warheads:      6 -> {6 * perFrame}");
        Out.WriteLine($"  a 150-shell burst: 150 -> {150 * perFrame}   (never paid: a shell's "
                      + "HitsTerrain is false, so it asks nothing at all)");

        KineticRod.Entry entry = new(200_000.0, 7_500.0, 7.5);

        Arrival held = Fly(Warhead(false), entry, FrameInAir, 0.05);
        Arrival resampled = Fly(Warhead(true), entry, FrameInAir, 0.05);

        Out.WriteLine($"over one {resampled.Frames:N0}-frame re-entry: {held.Samples:N0} queries held, "
                      + $"{resampled.Samples:N0} re-read; six of them {6L * resampled.Samples:N0}");

        // The coast is above the air and stepped at half a second, so it costs the same either way.
        // What multiplies is only the part of the flight the world has been slowed for.
        Assert.True(resampled.Samples > held.Samples);
    }
}
