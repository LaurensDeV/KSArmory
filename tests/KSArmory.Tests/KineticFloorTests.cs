using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// How accurate a kinetic round could <em>possibly</em> be — the terms nothing in this mod can
/// tune away, each with a number.
///
/// <para>Distinct from <c>ErrorBudgetTests</c> and <c>MirvBudgetTests</c>, which measure what the
/// shot costs <em>today</em>. Everything here is a floor: an integrator's own truncation, an
/// engine constant, a texture's bit depth, a pixel. A term that turns out to be worth a millimetre
/// is as useful as one worth a kilometre, so the ruled-out ones are kept and say so.</para>
///
/// <para>Measurement only. Nothing asserts an improvement.</para>
///
/// <para><b>What this rig cannot see.</b> The planet is at the origin and does not move, which is
/// the one case where a frame carrier is identically zero — so no epoch fault is visible from here.
/// <see cref="TheEclipticIsWhereAKineticRoundKeepsItsPosition"/> deliberately breaks that
/// convention for one measurement, moving the planet out to 1 AU while leaving it <em>still</em>:
/// that isolates the arithmetic of large coordinates from the epoch question, which is a different
/// one and belongs to <c>docs/FRAMES-AND-EPOCHS.md</c>.</para>
/// </summary>
public class KineticFloorTests(ITestOutputHelper Out)
{
    private const double R = DeorbitShot.R;

    /// <summary>
    /// The quantum of KSA's shipped Earth height field: <c>R16_UNORM</c> over a declared range of
    /// -10.930 km to 8.631 km. <c>docs/KSA-TERRAIN.md</c> has the derivation.
    /// </summary>
    private const double HeightQuantumMetres = 19_561.0 / 65_535.0;

    /// <summary>How far the height field's base grid is apart on the ground at an Earth face centre.</summary>
    private const double HeightTexelMetres = 3_111.0;

    private static BallisticBody Earth => DeorbitShot.Earth;

    private static double GroundMetres(double3 a, double3 b) => DeorbitShot.GroundMetres(a, b);

    private static MunitionProfile Warhead => DeorbitShot.Warhead;

    // ---------------------------------------------------------------- integration

    /// <summary>
    /// What the round's own integrator costs, as a function of the step it is given.
    ///
    /// <para><see cref="Slug"/> is symplectic Euler at <see cref="Interceptor.SubStep"/>, and a
    /// frame shorter than one sub-step is integrated in one — so handing <c>Update</c> a step under
    /// 5 ms <em>is</em> the round with a finer sub-step, with no production change to make it.</para>
    /// </summary>
    [Fact]
    public void TheSubStepIsTheWholeOfTheIntegratorsOwnError()
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out double3 from, out _);

        // The finest flight this can afford, as the thing every coarser one is measured against.
        // First order in the step, so the reference's own error is one twentieth of the 1 ms
        // flight's and a fiftieth of the shipped one's.
        (double3 reference, double refSeconds) = DeorbitShot.FlyTheRound(from, arc.RequiredVelocityCci, 0.00025);

        Out.WriteLine($"reference: 0.25 ms step, {refSeconds:F1} s of flight, "
                      + $"{refSeconds / 0.00025:N0} sub-steps");

        double coefficient = double.NaN;

        foreach (double h in new[] { 0.005, 0.0025, 0.001, 0.0005, 0.00025 })
        {
            (double3 landed, _) = DeorbitShot.FlyTheRound(from, arc.RequiredVelocityCci, h);
            double off = GroundMetres(landed, reference);

            // Symplectic Euler is first order, so the miss against a reference at h0 is
            // C * (h - h0) and every row gives the same C. Reporting C rather than the raw gap is
            // what turns five numbers into one, and what makes the h -> 0 answer sayable.
            double c = off / ((h - 0.00025) * 1000.0);
            if (h > 0.00025) coefficient = c;

            Out.WriteLine($"{h * 1000,6:F2} ms sub-step: {off,8:F1} m from the reference"
                          + (h > 0.00025 ? $"   ({c:F1} m per ms of step)" : ""));
        }

        Out.WriteLine($"first order and clean, so the shipped {Interceptor.SubStep * 1000:F0} ms sub-step is "
                      + $"{coefficient * Interceptor.SubStep * 1000:F0} m from a converged flight of the same round");
    }

    /// <summary>
    /// What a finer sub-step would cost, in the only currency that matters — sub-steps per round
    /// per frame.
    ///
    /// <para>The step is not free to shrink because <see cref="Interceptor.SubStep"/> is shared by
    /// every round in the air, and a CIWS burst is 150 shells. A warhead is the opposite case: six
    /// of them, for a few hundred seconds.</para>
    /// </summary>
    [Fact]
    public void WhatAFinerSubStepWouldCost()
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out double3 from, out _);
        (_, double seconds) = DeorbitShot.FlyTheRound(from, arc.RequiredVelocityCci, DeorbitShot.NominalFrame);

        Out.WriteLine($"one warhead flies {seconds:F0} s; the world is held to "
                      + $"{Medium.FaithfulStepInAir * 1000:F0} ms frames once there is air");

        foreach (double h in new[] { 0.005, 0.001, 0.0005, 0.0001 })
        {
            Out.WriteLine($"{h * 1000,5:F1} ms: {Medium.FaithfulStepInAir / h,6:N0} sub-steps per round per frame, "
                          + $"{seconds / h,11:N0} over the whole flight, "
                          + $"{6 * Medium.FaithfulStepInAir / h,7:N0} per frame for a six-warhead group");
        }

        Out.WriteLine($"a {Interceptor.MaxSubSteps}-sub-step clamp caps one Update at "
                      + $"{Interceptor.MaxFaithfulStep:F2} s, which is {Interceptor.MaxFaithfulStep / Medium.FaithfulStepInAir:F0}x "
                      + "the step the world is already held to in air — so the clamp never binds on entry");
    }

    /// <summary>
    /// Whether the integrator's error is a wrong <em>place</em> or a wrong <em>time</em>, because
    /// only the first one gets cheaper with a steeper arrival.
    ///
    /// <para>Symplectic Euler on a Kepler arc mostly accumulates phase, and a round arriving late at
    /// the same point on the same arc still misses — by the ground's own rotation, 465 m/s at the
    /// equator, whatever angle it comes in at. Separating the two is what decides whether steepening
    /// the arrival is worth anything against this term.</para>
    /// </summary>
    [Fact]
    public void WhetherTheIntegratorsErrorIsAPlaceOrATime()
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out double3 from, out _);

        (double3 fine, double tFine) = DeorbitShot.FlyTheRound(from, arc.RequiredVelocityCci, 0.00025);
        (double3 coarse, double tCoarse) = DeorbitShot.FlyTheRound(from, arc.RequiredVelocityCci,
                                                                   Interceptor.SubStep);

        double late = tCoarse - tFine;
        double carried = DeorbitShot.EarthSpin * R * Math.Abs(late);

        Out.WriteLine($"5 ms against 0.25 ms: {GroundMetres(coarse, fine):F1} m of ground, "
                      + $"arriving {late * 1000:F1} ms {(late > 0 ? "late" : "early")}");
        Out.WriteLine($"the ground turns {carried:F1} m under it in that time at the equator");
        Out.WriteLine("what is left is a genuinely different arc, and only that part answers to a "
                      + "steeper arrival");
    }

    /// <summary>
    /// The same sweep on a steep arrival, which is the shot a rod is actually thrown on.
    ///
    /// <para>The integrator's error is mostly along the flight path, and how much of an along-path
    /// error becomes <em>ground</em> is set by the arrival angle. A shallow arrival converts it
    /// almost entirely; a vertical one converts none of it.</para>
    /// </summary>
    [Theory]
    [InlineData(6.0)]
    [InlineData(15.0)]
    [InlineData(30.0)]
    [InlineData(45.0)]
    [InlineData(70.0)]
    [InlineData(89.0)]
    public void TheArrivalAngleDecidesHowMuchOfTheIntegratorsErrorIsGround(double arrivalDeg)
    {
        Arrival(arrivalDeg, out double3 r, out double3 v);

        (double3 reference, _) = FlyFrom(r, v, 0.00025);
        (double3 shipped, _) = FlyFrom(r, v, Interceptor.SubStep);

        double off = GroundMetres(shipped, reference);
        double ruler = R * Math.Sqrt(2.0 * Math.ScaleB(1.0, -52));

        Out.WriteLine($"{arrivalDeg,5:F1} deg arrival: the 5 ms round lands {off:F2} m from a 0.25 ms one"
                      + (off < ruler ? "  (below the ruler -- see TheHarnessCannotScore...)" : ""));
    }

    /// <summary>
    /// The whole shot as a rod would actually be thrown, from a deorbit burn that takes most of the
    /// orbital velocity out rather than one that only lowers the periapsis.
    ///
    /// <para><see cref="TheArrivalAngleDecidesHowMuchOfTheIntegratorsErrorIsGround"/> starts 60 km
    /// up, so it prices the entry alone. This one flies from the same 200 km pickup every other
    /// budget uses and lets the burn decide the angle, so the integrator gets the whole coast to
    /// accumulate in — which is where most of it comes from.</para>
    /// </summary>
    [Theory]
    [InlineData(0.90)]
    [InlineData(0.60)]
    [InlineData(0.30)]
    [InlineData(0.10)]
    [InlineData(0.00)]
    public void TheWholeShotAtTheAngleTheDeorbitBurnLeavesIt(double horizontalFraction)
    {
        double3 from = new(R + DeorbitShot.PickupAltitude, 0, 0);
        double circular = Math.Sqrt(DeorbitShot.Mu / Vec.Len(from));
        double3 velocity = new(0, circular * horizontalFraction, 0);

        (double3 fine, double tFine) = FlyFrom(from, velocity, 0.00025);
        (double3 coarse, double tCoarse) = FlyFrom(from, velocity, Interceptor.SubStep);

        double range = GroundMetres(fine, Vec.Unit(from) * R);
        double miss = GroundMetres(coarse, fine);

        // The angle the arc actually arrives at, off the fine flight's own last kilometre.
        double gamma = ArrivalDegrees(from, velocity);

        double turned = DeorbitShot.EarthSpin * R * Math.Abs(tCoarse - tFine);

        Out.WriteLine($"{horizontalFraction * 100,4:F0}% of circular: {range / 1000.0,7:N0} km downrange, "
                      + $"{gamma,5:F1} deg arrival, {tFine,6:F1} s of flight");
        Out.WriteLine($"    the 5 ms round lands {miss,8:F2} m from a 0.25 ms one, "
                      + $"{(tCoarse - tFine) * 1000:F1} ms apart — the ground turns {turned:F2} m in that");
    }

    /// <summary>The flight path angle below the horizon where the arc meets the ground.</summary>
    private static double ArrivalDegrees(double3 fromCci, double3 velocityCci)
    {
        Assert.True(ImpactPredictor.TryPredict(Earth, fromCci, velocityCci, 1.0, 20_000.0,
                                               out ImpactPredictor.Impact hit, null, null,
                                               new ImpactPredictor.Drag(DeorbitShot.DensityAt, Warhead)));

        double sine = Vec.Dot(Vec.Unit(hit.VelocityCci), Vec.Unit(hit.PointCci));
        return -Math.Asin(Math.Clamp(sine, -1.0, 1.0)) * 180.0 / Math.PI;
    }

    // ---------------------------------------------------------------- arithmetic

    /// <summary>
    /// What it costs to keep a round's position in the ecliptic, where the numbers are astronomical
    /// and the round is metres long.
    ///
    /// <para><see cref="Slug.PositionEcl"/> is a <c>double3</c> in <c>Ecl</c>, so at Earth's distance
    /// from the origin its representable spacing is coarser than a hair. Every sub-step adds a
    /// displacement of tens of metres to a number of ~1.5e11, which rounds; every ground test
    /// subtracts two such numbers, which cancels.</para>
    ///
    /// <para>The planet is moved out and left <b>still</b>. A moving one would confound this with the
    /// epoch question, which is a different fault with a different fix.</para>
    /// </summary>
    [Fact]
    public void TheEclipticIsWhereAKineticRoundKeepsItsPosition()
    {
        double au = 1.495978707e11;

        Out.WriteLine($"double spacing at 1 AU: {Ulp(au) * 1e6:F1} um; "
                      + $"at Earth's orbit that is {Ulp(au) * 1e3:F3} mm per stored coordinate");
        Out.WriteLine($"at Saturn's {Ulp(1.43e12) * 1e6:F0} um, at Neptune's {Ulp(4.5e12) * 1e6:F0} um");

        BallisticArc.Solution arc = DeorbitShot.Shot(out double3 from, out _);

        (double3 atOrigin, double t0) = FlyOffset(from, arc.RequiredVelocityCci, Interceptor.SubStep, Vec.Zero);

        foreach (double distance in new[] { 1.0e9, au, 1.43e12 })
        {
            double3 offset = new(distance * 0.6, distance * 0.8, 0.0);
            (double3 moved, double t1) = FlyOffset(from, arc.RequiredVelocityCci, Interceptor.SubStep, offset);

            // Straight-line, not great-circle: GroundMetres goes through an arc cosine and cannot
            // resolve anything under about 13 cm at this radius -- see
            // TheHarnessCannotScoreAMissUnderThirteenCentimetres, which is a floor on the ruler
            // rather than on the shot.
            Out.WriteLine($"planet at {distance:E1} m: lands {Vec.Len(moved - atOrigin) * 1000:F3} mm "
                          + $"from the origin flight, {(t1 - t0) * 1e6:F1} us apart");
        }
    }

    /// <summary>
    /// The floor under the ruler rather than under the shot.
    ///
    /// <para>Every budget in this repository scores a miss with <c>DeorbitShot.GroundMetres</c>,
    /// which is <c>R * Vec.AngleBetween</c>, which is an arc cosine. Near zero the cosine is flat,
    /// so a dot product one ulp under 1.0 is already <c>sqrt(2 * eps)</c> of angle — and no
    /// measurement taken through it can report a smaller miss than that, whatever the round did.
    /// </para>
    /// </summary>
    [Fact]
    public void TheHarnessCannotScoreAMissUnderThirteenCentimetres()
    {
        double floor = R * Math.Sqrt(2.0 * Math.ScaleB(1.0, -52));

        Out.WriteLine($"arc-cosine resolution at Earth's radius: {floor * 100:F1} cm");

        double3 a = new(R, 0, 0);
        double smallest = double.MaxValue;

        for (double metres = 1.0; metres > 1e-4; metres *= 0.5)
        {
            double3 b = new(R, metres, 0);
            double read = GroundMetres(a, b);
            if (read > 0.0) smallest = Math.Min(smallest, read);

            if (metres <= 0.5 && metres >= 0.03)
            {
                Out.WriteLine($"  {metres * 100,7:F2} cm apart reads as {read * 100,7:F2} cm");
            }
        }

        Out.WriteLine($"smallest non-zero reading anywhere in the sweep: {smallest * 100:F1} cm");
    }

    /// <summary>
    /// The engine's own clock quantum. <c>UniverseTime</c> is <c>Int128</c> nanoseconds, and
    /// <c>SimStep.DeltaTime</c> is the unrounded <c>double</c> that was added to it — so the mod
    /// integrates by one number while the world advanced by the other.
    /// </summary>
    [Fact]
    public void TheEngineClockIsQuantisedToTheNanosecond()
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out double3 from, out _);
        (_, double seconds) = DeorbitShot.FlyTheRound(from, arc.RequiredVelocityCci, DeorbitShot.NominalFrame);

        double frames = seconds / DeorbitShot.NominalFrame;
        double speed = 7_000.0;

        Out.WriteLine($"{frames:N0} frames over {seconds:F0} s of flight");
        Out.WriteLine($"worst case, every frame rounding the same way: {frames * 0.5e-9 * speed * 1000:F3} mm");
        Out.WriteLine($"as a random walk: {Math.Sqrt(frames) * 0.5e-9 * speed * 1e6:F1} um");
        // UniverseTime itself is Int128, so it does not decay; what decays is anything the mod
        // reads through GetElapsedSeconds(), which is one double for the whole universe clock.
        foreach (double universe in new[] { 497.0, 86_400.0, 3.15e7, 3.15e9 })
        {
            Out.WriteLine($"a double's spacing on elapsed seconds at {universe:E1} s of universe time: "
                          + $"{Ulp(universe) * 1e9:F4} ns, which at {speed / 1000.0:F0} km/s is "
                          + $"{Ulp(universe) * speed * 1e6:F3} um");
        }
    }

    // ---------------------------------------------------------------- the ground

    /// <summary>
    /// What one metre of disagreement about where the surface is costs, at each arrival angle — and
    /// what the two <em>fixed</em> vertical quanta are worth through it.
    ///
    /// <para>The height field's own bit depth and <see cref="ImpactPredictor.CrossingToleranceMetres"/>
    /// are both heights. Neither can be argued down without a different height field or a slower
    /// search; both become ground in proportion to <c>cot(gamma)</c>.</para>
    /// </summary>
    [Fact]
    public void TheTwoFixedVerticalQuantaAreGroundInProportionToCotangentGamma()
    {
        Out.WriteLine($"height field quantum {HeightQuantumMetres:F4} m, "
                      + $"predictor crossing tolerance {ImpactPredictor.CrossingToleranceMetres:F2} m");

        foreach (double deg in new[] { 5.0, 7.1, 15.0, 30.0, 45.0, 60.0, 80.0, 90.0 })
        {
            double cot = deg >= 90.0 ? 0.0 : 1.0 / Math.Tan(deg * Math.PI / 180.0);

            Out.WriteLine($"{deg,5:F1} deg: {cot,6:F2} m of ground per m of height  ->  "
                          + $"quantum {HeightQuantumMetres * cot,7:F2} m, "
                          + $"crossing {ImpactPredictor.CrossingToleranceMetres * cot,7:F2} m, "
                          + $"both together {(HeightQuantumMetres + ImpactPredictor.CrossingToleranceMetres) * cot,7:F2} m");
        }
    }

    /// <summary>
    /// What the round pays for sampling the ground once a frame and holding it as a sphere.
    ///
    /// <para><see cref="Slug"/> asks <see cref="IGroundTest"/> before the sub-step loop, so the
    /// surface it stops on is the height under where it was at the <em>top</em> of the frame, held
    /// concentric with the body for the whole of it. Over sloping ground that is a height error of
    /// slope times the ground track covered, which the arrival angle then multiplies.</para>
    /// </summary>
    [Theory]
    [InlineData(7.1)]
    [InlineData(30.0)]
    [InlineData(70.0)]
    public void TheGroundSphereIsSampledOnceAFrameAndHeldFlat(double arrivalDeg)
    {
        double gamma = arrivalDeg * Math.PI / 180.0;
        double speed = 2_713.0;

        Out.WriteLine($"{arrivalDeg:F1} deg arrival at {speed:F0} m/s");

        foreach (double frame in new[] { Medium.FaithfulStepInAir, DeorbitShot.NominalFrame, 0.32 })
        {
            double track = speed * Math.Cos(gamma) * frame;

            foreach (double slope in new[] { 0.01, 0.05, 0.20 })
            {
                // A stale height of slope*track, resolved along an arc arriving at gamma: the
                // round runs on until it meets the sphere it was given.
                double stopping = slope * track / (Math.Tan(gamma) + slope);
                Out.WriteLine($"  {frame * 1000,5:F0} ms frame, {track,7:F1} m of track, "
                              + $"{slope * 100,3:F0}% slope: {stopping,8:F2} m of stopping error");
            }
        }
    }

    /// <summary>
    /// The finest thing KSA's height field can be asked about, which is not the texture.
    ///
    /// <para>Earth's base cubemap is 1.5-3.1 km between texels, so every metre-scale feature comes
    /// from the procedural modifiers — and <c>Celestial.GetTerrainHeightFromDirCcf</c> packs the
    /// direction to a <c>float3</c> before evaluating them (<c>Celestial.cs:1637-1640</c>, through
    /// <c>float3.Pack</c>, whose default mode is a plain cast). A float unit vector has a coarser
    /// spacing than a rod is long, so below that the modifier stack answers with one value: the
    /// surface is a staircase, deterministic and identical for every caller, with treads measured
    /// here.</para>
    ///
    /// <para>The base bicubic is unaffected — it is <c>double</c> end to end and its texel samples
    /// are exact 16-bit integers. This bites the modifiers alone, which on Earth is everything
    /// finer than 3 km.</para>
    /// </summary>
    [Fact]
    public void TheProceduralTerrainIsEvaluatedOnAFloatDirection()
    {
        Out.WriteLine($"float spacing at unit magnitude puts a floor of "
                      + $"{R * Math.ScaleB(1.0, -24):F2} m to {R * Math.ScaleB(1.0, -23):F2} m of ground "
                      + "on how finely the modifier stack can be asked a question");

        // Walk a great circle in metre steps and count how often the packed direction changes. The
        // gap between changes is the tread, and the height across one tread is a single value.
        double3 axis = Vec.Unit(new double3(0.31, 0.62, 0.72));
        double3 step = Vec.Unit(Vec.PerpendicularTo(axis, new double3(0, 0, 1)));

        foreach (double metres in new[] { 0.1, 0.25, 0.5, 1.0, 2.0 })
        {
            int distinct = 1;
            float3 previous = float3.Pack(Vec.Unit(axis));

            for (int i = 1; i <= 200; i++)
            {
                float3 packed = float3.Pack(Vec.Unit(axis + step * (metres * i / R)));
                if (!packed.Equals(previous)) distinct++;
                previous = packed;
            }

            Out.WriteLine($"  stepping {metres,4:F2} m along the ground: "
                          + $"{distinct,3} distinct float directions in 200 steps "
                          + $"({200.0 * metres / distinct:F2} m per tread)");
        }

        double worst = 0.0;
        Random rng = new(1);

        for (int i = 0; i < 20_000; i++)
        {
            double3 d = Vec.Unit(new double3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5,
                                             rng.NextDouble() - 0.5));
            float3 packed = float3.Pack(d);
            double3 back = Vec.Unit(new double3(packed.X, packed.Y, packed.Z));

            worst = Math.Max(worst, R * Vec.Len(back - d));
        }

        Out.WriteLine($"worst displacement over 20,000 directions: {worst:F3} m of ground");
    }

    /// <summary>
    /// What the modifiers actually put there below the base grid, from the shipped
    /// <c>Astronomicals.xml</c> and <c>ErosionModifierReference.Evaluate</c>.
    ///
    /// <para>An <b>upper bound</b>, and deliberately labelled as one. Each octave's contribution is
    /// multiplied by the biome weight, by a gradient-falloff power of the angle between the texture
    /// normal and the surface normal, and by <c>1 - |dot|</c> of those two again — all of which are
    /// near zero over flat ground and none of which is reachable headlessly. What can be said
    /// without the game is the geometry: where the finest scale is, and what slope it would carry
    /// undamped.</para>
    /// </summary>
    [Fact]
    public void WhereTheProceduralTerrainRunsOutOfDetail()
    {
        // Astronomicals.xml, EarthErosion: Frequency 150, Amplitude 1000 m, Lacunarity 2,
        // Octaves 7. Evaluate samples at direction * Frequency * 4 * lacunarity^i and weights the
        // octave by 0.5^(i+1), so the argument is cycles across a unit direction vector.
        double frequency = 150.0 * 4.0;
        double amplitude = 1000.0;

        Out.WriteLine("EarthErosion, undamped — every row is an upper bound:");

        for (int i = 0; i < 7; i++)
        {
            double wavelength = R / (frequency * Math.Pow(2, i));
            double octave = amplitude * Math.Pow(0.5, i + 1);

            Out.WriteLine($"  octave {i}: {wavelength,9:N0} m wavelength, {octave,8:F1} m amplitude, "
                          + $"slope up to {2.0 * Math.PI * octave / wavelength:F2}");
        }

        // The four TilingDetail modifiers are what carries on below the erosion fractal. Each is a
        // 4096-square R16 texture whose UV is the packed direction times Frequency, so one tile
        // spans R/Frequency of ground and one texel is that over 4096.
        Out.WriteLine("EarthTilingDetail — one 4096-square texture per biome, bilinear:");

        foreach ((string biome, double f, double a) in new[]
                 {
                     ("GrassMountains", 80.0, 1900.0), ("DesertMountains", 76.0, 1500.0),
                     ("AlpineMountains", 209.0, 1400.0), ("Grass", 120.0, 225.0),
                 })
        {
            Out.WriteLine($"  {biome,-16}: {R / f / 1000.0,6:F1} km tile, "
                          + $"{R / f / 4096.0,5:F1} m per texel, {a,6:F0} m of amplitude across it");
        }

        Out.WriteLine($"so the finest real shape on Earth is about 7-19 m; below that the surface is a "
                      + "bilinear ramp between two texels, and below "
                      + $"{R * Math.ScaleB(1.0, -24):F2} m it is a staircase");
    }

    /// <summary>
    /// The terrain does not add a bias of its own — the round and the aim point read the same field
    /// through an exact round trip. What it does is <em>amplify</em> whatever miss is left, and on a
    /// shallow arrival over rough ground the amplification is not even bounded.
    ///
    /// <para>A residual miss <c>d</c> lands on ground <c>s.d</c> higher or lower than the aim point,
    /// which the arrival angle turns back into <c>s.d/tan(gamma)</c> of further miss. The loop's
    /// gain is <c>s/tan(gamma)</c>: below one it converges to <c>d/(1-s/tan gamma)</c>, at one or
    /// above there is no fixed point at all and where the round stops is decided by the terrain
    /// rather than by the shot. Same gain, and the same failure, as the cursor's ground-point
    /// iteration in <c>KsaWorld.TryCursorGroundPoint</c>.</para>
    /// </summary>
    [Fact]
    public void TheTerrainAmplifiesWhateverMissIsLeftAndAtAShallowArrivalWithoutBound()
    {
        Out.WriteLine("gain = slope / tan(arrival); 1.00 and above has no fixed point");

        foreach (double deg in new[] { 6.0, 15.0, 30.0, 45.0, 70.0 })
        {
            double tan = Math.Tan(deg * Math.PI / 180.0);
            string row = $"{deg,5:F1} deg:";

            foreach (double slope in new[] { 0.02, 0.05, 0.10, 0.30 })
            {
                double gain = slope / tan;
                row += gain >= 1.0
                    ? $"  {slope * 100,3:F0}% -> unbounded"
                    : $"  {slope * 100,3:F0}% -> {1.0 / (1.0 - gain),5:F2}x";
            }

            Out.WriteLine(row);
        }

        Out.WriteLine($"the base height grid is {HeightTexelMetres:N0} m apart at an Earth face centre, "
                      + "so metre-scale slope is the procedural modifiers rather than the texture");
    }

    // ---------------------------------------------------------------- the aim point

    /// <summary>
    /// How precisely a place can be <em>named</em>, which is a floor nothing downstream can beat.
    ///
    /// <para><c>Ksa/SiteDesignator.cs</c> takes a click, so the aim point starts as one pixel of the
    /// player's viewport resolved against the height field. The angle a pixel subtends times the
    /// range is a length across the line of sight; the depression angle turns that into ground.
    /// <see cref="AimSite"/> then stores the answer as <c>double</c> latitude and longitude, which
    /// costs nothing — but nothing recovers what the pixel threw away.</para>
    /// </summary>
    [Fact]
    public void WhatOnePixelOfDesignationIsWorthOnTheGround()
    {
        int height = 1080;

        foreach (double fovDeg in new[] { 60.0, 15.0, 3.0 })
        {
            double perPixel = (fovDeg * Math.PI / 180.0) / height;
            Out.WriteLine($"{fovDeg,5:F1} deg field over {height} px: {perPixel * 1e6:F1} urad per pixel"
                          + (fovDeg < 60.0 ? $"  ({60.0 / fovDeg:F0}x zoom)" : ""));

            foreach ((double range, double depressionDeg) in new[]
                     {
                         (2_000.0, 30.0), (20_000.0, 20.0), (200_000.0, 90.0), (2_000_000.0, 90.0),
                     })
            {
                double across = range * perPixel;
                double ground = across / Math.Sin(depressionDeg * Math.PI / 180.0);
                Out.WriteLine($"   from {range / 1000.0,7:N0} km at {depressionDeg,4:F0} deg depression: "
                              + $"{ground,10:N1} m of ground per pixel");
            }
        }

        Out.WriteLine($"0.001 deg of latitude — what AimSite.Describe prints — is {R * (0.001 * Math.PI / 180.0):F0} m, "
                      + "and is display only: the stored value is a double and is never round-tripped through it");
    }

    // ---------------------------------------------------------------- terminal guidance

    /// <summary>
    /// What a tail kit could still take out, given a perfect aim point.
    ///
    /// <para>The warhead is flown with <see cref="GuidanceMode.Inertial"/> — the same proportional
    /// navigation the bomb uses — from a state deliberately displaced, and the miss it converges to
    /// is what terminal guidance is worth against everything upstream of it. The lateral limit is
    /// swept because it is the one number a re-entry body genuinely cannot have much of.</para>
    /// </summary>
    [Theory]
    [InlineData(500.0)]
    [InlineData(5_000.0)]
    [InlineData(50_000.0)]
    public void WhatATerminallyGuidedRoundCanStillTakeOut(double displacement)
    {
        Arrival(30.0, out double3 r, out double3 v);

        (double3 unguided, _) = FlyFrom(r, v, Interceptor.SubStep);
        double3 target = unguided;

        // Displace the release across the track, so the whole of it has to be steered out.
        double3 across = Vec.Unit(Vec.Cross(r, v));
        double3 offset = r + across * displacement;

        Out.WriteLine($"released {displacement:N0} m across the track from a 30 deg arrival");

        foreach (float g in new[] { 0.5f, 2f, 6f, 20f })
        {
            MunitionProfile kit = Steered(g);
            (double3 landed, _) = FlyGuided(offset, v, kit, target);

            Out.WriteLine($"  {g,4:F1} g of fin authority: {GroundMetres(landed, target),10:N2} m from the aim point");
        }

        (double3 nothing, _) = FlyFrom(offset, v, Interceptor.SubStep);
        Out.WriteLine($"  unguided:                {GroundMetres(nothing, target),10:N2} m");
    }

    /// <summary>
    /// Whether the couple of metres a steered round settles on is the guidance law's own residue or
    /// the step it is flown at.
    ///
    /// <para>They are separable: a truncation error halves with the step and a law's residue does
    /// not. Which one it is decides whether terminal guidance has a floor worth quoting or is
    /// simply bounded by everything upstream of it.</para>
    /// </summary>
    [Fact]
    public void WhereTheSteeredRoundsLastFewMetresComeFrom()
    {
        Arrival(30.0, out double3 r, out double3 v);
        (double3 target, _) = FlyFrom(r, v, 0.00025);

        double3 across = Vec.Unit(Vec.Cross(r, v));
        double3 offset = r + across * 500.0;

        foreach (float nav in new[] { 3f, 4f, 6f })
        {
            string row = $"N={nav:F0}:";

            foreach (double h in new[] { 0.005, 0.001, 0.00025 })
            {
                (double3 landed, _) = FlyGuided(offset, v, Steered(20f, nav), target, h);
                row += $"   {h * 1000,5:F2} ms -> {GroundMetres(landed, target),7:F2} m";
            }

            Out.WriteLine(row);
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The spacing between representable doubles at a magnitude.</summary>
    private static double Ulp(double x)
    {
        double a = Math.Abs(x);
        return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(a) + 1) - a;
    }

    /// <summary>
    /// A terminal state on the way in: 60 km up at 7 km/s, at a stated flight path angle.
    ///
    /// <para>High enough that the whole of entry is flown, so drag and the integrator both get the
    /// part of the arc where they actually cost something.</para>
    /// </summary>
    private static void Arrival(double angleDeg, out double3 positionCci, out double3 velocityCci)
    {
        positionCci = new double3(R + 60_000.0, 0, 0);

        double3 up = new(1, 0, 0);
        double3 along = new(0, 1, 0);
        double a = angleDeg * Math.PI / 180.0;

        velocityCci = (along * Math.Cos(a) - up * Math.Sin(a)) * 7_000.0;
    }

    /// <summary>The warhead with a tail kit on it, at a stated lateral limit.</summary>
    private static MunitionProfile Steered(float lateralG, float navConstant = 4f)
    {
        MunitionProfile reference = DeorbitShot.Warhead;

        return new MunitionProfile
        {
            Name = reference.Name,
            DisplayName = reference.DisplayName,

            Guidance = GuidanceMode.Inertial,
            MaxLateralG = lateralG,
            NavConstant = navConstant,

            // The fall is what the round is riding; biasing it out would have the fins holding it
            // up rather than aiming it.
            GravityCompensation = 0f,

            DragK = reference.DragK,
            MaxRange = reference.MaxRange,
            MaxFlightSeconds = reference.MaxFlightSeconds,
            FuseRadius = 0f,
            FuseArmSeconds = reference.FuseArmSeconds,
            HitsTerrain = true,
        };
    }

    private static (double3 GroundFixed, double Seconds) FlyFrom(double3 fromCci, double3 velocityCci, double dt)
        => FlyOffset(fromCci, velocityCci, dt, Vec.Zero);

    /// <summary>
    /// The round as the game flies it, about a planet standing at <paramref name="centre"/>.
    ///
    /// <para>The offset is the whole point of this being separate from
    /// <c>DeorbitShot.FlyTheRound</c>: it puts the round's stored coordinates at an astronomical
    /// magnitude without changing anything else about the flight.</para>
    /// </summary>
    private static (double3 GroundFixed, double Seconds) FlyOffset(double3 fromCci, double3 velocityCci,
                                                                   double dt, double3 centre)
    {
        BallisticBody body = Earth;

        Slug round = new(fromCci + centre, velocityCci, null, 1, fromCci + centre, Vec.Zero)
        {
            Munition = Warhead,
            Ground = new ShiftedBall(centre),
            AirDensityAt = (pos, _) => DeorbitShot.DensityAt(pos - centre),
        };

        double elapsed = 0.0;

        for (int i = 0; i < (int)(20_000.0 / dt) && round.State == RoundState.Flying; i++)
        {
            double3 local = round.PositionEcl - centre;

            round.Update(dt, null, body.GravityCci(local), body.GroundVelocityCci(local),
                         fromCci + centre, Warhead, DeorbitShot.DensityAt(local));
            elapsed += dt;
        }

        Assert.NotEqual(RoundState.Flying, round.State);

        double seconds = elapsed + Math.Min(0.0, round.DetonationElapsedInFrame);
        return (body.UncarryCci(round.PositionEcl - centre, seconds), seconds);
    }

    /// <summary>
    /// The same round with a tail kit, steering at a fixed place on the ground.
    ///
    /// <para>The aim is re-carried every frame rather than stored, because it is a place on a
    /// turning planet — the rule <see cref="AimSite"/> exists for, at the flight time that makes it
    /// bite.</para>
    /// </summary>
    private static (double3 GroundFixed, double Seconds) FlyGuided(double3 fromCci, double3 velocityCci,
                                                                   MunitionProfile kit, double3 groundFixedTarget,
                                                                   double dt = Interceptor.SubStep)
    {
        BallisticBody body = Earth;

        Slug round = new(fromCci, velocityCci, new object(), 1, fromCci, Vec.Zero)
        {
            Munition = kit,
            Ground = new ShiftedBall(Vec.Zero),
            AirDensityAt = (pos, _) => DeorbitShot.DensityAt(pos),
        };

        double elapsed = 0.0;

        for (int i = 0; i < (int)(20_000.0 / dt) && round.State == RoundState.Flying; i++)
        {
            double3 aim = body.CarryCci(groundFixedTarget, elapsed);
            TargetState state = new(aim, body.GroundVelocityCci(aim), 0.0);

            round.Update(dt, state, body.GravityCci(round.PositionEcl),
                         body.GroundVelocityCci(round.PositionEcl),
                         fromCci, kit, DeorbitShot.DensityAt(round.PositionEcl));
            elapsed += dt;
        }

        Assert.NotEqual(RoundState.Flying, round.State);

        double seconds = elapsed + Math.Min(0.0, round.DetonationElapsedInFrame);
        return (body.UncarryCci(round.PositionEcl, seconds), seconds);
    }

    /// <summary>The mean sphere, about a planet that need not be at the origin.</summary>
    private sealed class ShiftedBall(double3 centre) : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = centre;
            surfaceRadius = R;
            return true;
        }
    }
}
