using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Trimming the bus between releases so each tube's own ejection kick lands on the arc, which is
/// what a real post-boost vehicle does about a canted magazine and is the velocity-side alternative
/// to turning the whole vehicle — <c>docs/MIRV-NEXT.md</c> item 5.
///
/// <para><b>Measurement, and it does not win.</b> Nothing here is a feature under test: the
/// arithmetic is the deliverable, and it says the shipped bus cannot fly this at all. The tests
/// stay so that a bus with different thrusters, a different cant or a different trajectory can be
/// priced again without redoing the reasoning.</para>
///
/// <para><b>The trajectory here is the idealised one</b> — the cheapest arc from a 200 km circular
/// pickup, held retrograde — which <c>MirvBudgetTests</c> measures as about twice as sensitive to a
/// metre a second as the one the guidance actually leaves the bus on. The ratios between the rows
/// are what this file is for; the metres belong to this arc.</para>
/// </summary>
public class PerTubeTrimTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;
    private const double ScaleHeight = 8_000.0;

    /// <summary>What the magazine already paces a salvo at — <c>Arsenal.MirvBus.ReloadSeconds</c>.</summary>
    private const double ReloadSeconds = 3.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    private static MunitionProfile Warhead => Arsenal.ReentryVehicleMk21;

    private static double DensityAt(double3 pointCci)
        => Math.Exp(-Math.Max(0.0, Vec.Len(pointCci) - R) / ScaleHeight);

    private static double GroundMetres(double3 a, double3 b) => R * Vec.AngleBetween(a, b);

    /// <summary>The flown shot: picked up near-orbital 200 km up, aimed 3,459 km downrange.</summary>
    private static BallisticArc.Solution Shot(out double3 from, out double3 target)
    {
        from = new double3(R + 200_000.0, 0, 0);
        const double range = 3_459_000.0;
        target = new double3(R * Math.Cos(range / R), R * Math.Sin(range / R), 0);

        double3 circular = new(0, Math.Sqrt(Mu / (R + 200_000.0)), 0);
        Assert.True(BallisticArc.TryCheapest(Earth, from, circular, target, out BallisticArc.Solution s));
        return s;
    }

    /// <summary>The bus holds the line its burn ended on, which on a deorbit is retrograde.</summary>
    private static doubleQuat Attitude(double3 velocityCci)
        => Vec.RotationFromTo(new double3(1, 0, 0), -Vec.Unit(velocityCci));

    /// <summary>The six tube axes in the frame the bus is holding.</summary>
    private static double3[] Axes(doubleQuat attitude)
    {
        Tube[] tubes = Arsenal.MirvBus.Tubes;
        double3[] axes = new double3[tubes.Length];

        for (int i = 0; i < tubes.Length; i++) axes[i] = Vec.Unit(attitude * tubes[i].Direction);
        return axes;
    }

    /// <summary>
    /// <b>The finding.</b> Putting one tube's kick where the last one's was is a velocity change
    /// with nothing along the bus's nose in it.
    ///
    /// <para>A cant is a cone, so every tube's kick has the same axial share and the difference
    /// between any two of them is perpendicular to the axis. The shipped bus is four clusters of
    /// four laid out for pitch, yaw, roll and axial thrust, with no lateral translation — so the
    /// one direction it can push is the one direction this correction has nothing in.</para>
    /// </summary>
    [Fact]
    public void WhatOneTubesTrimIsAndWhichAxisItLiesOn()
    {
        double3 nose = new(1, 0, 0);
        double3[] axes = Axes(doubleQuat.Identity);
        double eject = Warhead.LaunchSpeed;

        double3 mean = ReleasePointing.ReferenceAxis(axes);
        Out.WriteLine($"the mean of the six axes is {Vec.AngleBetween(mean, nose) * 180.0 / Math.PI:F4} "
                      + $"deg off the nose; ejection {eject:F1} m/s, cant "
                      + $"{Vec.AngleBetween(axes[0], mean) * 180.0 / Math.PI:F2} deg");

        // From the line the aim correction converged on to what one tube actually needs. The axial
        // share is the same for all six, so it is a bias the correction has already taken out.
        for (int i = 0; i < axes.Length; i++)
        {
            double3 fromMean = (axes[i] - mean) * eject;
            Out.WriteLine($"  tube {i + 1}: {Vec.Len(fromMean):F4} m/s from the mean, "
                          + $"{Math.Abs(Vec.Dot(fromMean, nose)):F4} axial, "
                          + $"{Vec.Len(fromMean - nose * Vec.Dot(fromMean, nose)):F4} lateral");
        }

        // And between two tubes, which is what a sequence of trims actually spends.
        double ringPath = 0.0;
        double worstAxial = 0.0;

        for (int i = 1; i < axes.Length; i++)
        {
            double3 step = (axes[i] - axes[i - 1]) * eject;
            ringPath += Vec.Len(step);
            worstAxial = Math.Max(worstAxial, Math.Abs(Vec.Dot(step, nose)));
        }

        double3 first = (axes[0] - mean) * eject;

        Out.WriteLine($"the whole sequence costs {Vec.Len(first) + ringPath:F3} m/s, of which "
                      + $"{Math.Abs(Vec.Dot(first, nose)):F4} m/s is axial and every tube-to-tube "
                      + $"step is at most {worstAxial:E1} m/s axial");

        // Under a thousandth of what the trim stops inside, so a bus with only the axial pair has
        // nothing to fire at: the correction is entirely in the two directions it does not have.
        Assert.True(worstAxial < 0.001 * BusTrim.SettledMetresPerSecond,
                    $"the tube-to-tube trim carries {worstAxial:E2} m/s along the nose, so the cant "
                    + "is no longer a cone and the actuator argument has to be re-made");
    }

    /// <summary>
    /// What the trim would win, if something aboard could fly it: the six land on one point.
    ///
    /// <para>Trimming the bus by <c>-(kick_i - kick_mean)</c> before tube <c>i</c> goes is exactly
    /// the same departure velocity as releasing every tube on the mean, so the win is measured that
    /// way rather than by simulating a null that reaches zero by construction. Released together,
    /// so the only term in it is the cant.</para>
    /// </summary>
    [Fact]
    public void WhatThePerTubeTrimWouldWin()
    {
        BallisticArc.Solution arc = Shot(out double3 from, out double3 target);
        double3 v = arc.RequiredVelocityCci;
        double3[] axes = Axes(Attitude(v));

        double canted = Group(from, v, axes, 0.0, trimmed: false, target, "as canted");
        double trimmed = Group(from, v, axes, 0.0, trimmed: true, target, "per-tube trim");

        Out.WriteLine($"the cant alone is worth {canted - trimmed:F0} m of spread");

        Assert.True(trimmed < 50.0,
                    $"per-tube trimming left {trimmed:F0} m of spread, so it is not removing the "
                    + "term it exists to remove");
    }

    /// <summary>
    /// And what it costs: every extra second a warhead is held spends the leverage its ejection has
    /// along the arc, so a paced salvo walks its impacts down a ramp.
    ///
    /// <para>The two terms are not in one direction — the cant is a ring in the lateral plane and
    /// the ramp is along-track — so they are measured together through the predictor rather than
    /// added. Both columns are scored against the salvo the mod flies today, which already paces
    /// itself at the magazine's reload.</para>
    ///
    /// <para><b>Read the spread, not the mean.</b> This shot is flown uncorrected, so the whole
    /// group sits about a hundred kilometres out and the mean column is that bias — which is the
    /// aim correction's job and not the cant's. Only the scatter is what re-pointing or trimming
    /// could remove.</para>
    /// </summary>
    [Fact]
    public void WhatHoldingTheSalvoToDoItCosts()
    {
        BallisticArc.Solution arc = Shot(out double3 from, out double3 target);
        double3 v = arc.RequiredVelocityCci;
        double3[] axes = Axes(Attitude(v));

        double baseline = Group(from, v, axes, ReloadSeconds, false, target, "as canted, 3 s pace");
        Out.WriteLine("");

        foreach (double pace in new[] { 3.0, 5.0, 8.0, 12.0, 20.0, 30.0, 40.0, 60.0 })
        {
            double trimmed = Group(from, v, axes, pace, true, target, $"trimmed, {pace,4:F0} s per tube");
            double canted = Group(from, v, axes, pace, false, target, $"canted,  {pace,4:F0} s per tube");

            Out.WriteLine($"      -> trimming takes {baseline - trimmed:+0;-0} m off the spread; the "
                          + $"same wait with nothing trimmed takes {baseline - canted:+0;-0} m");
        }
    }

    /// <summary>
    /// The actuator. The shipped bus's thruster layout is asked to fly one tube's trim and cannot
    /// move it at all — it strikes off each lateral direction in turn and gives up with the whole
    /// error still on the vehicle.
    ///
    /// <para>Both cases are run so the difference is the thrusters and nothing else, and the one
    /// with lateral jets is where the honest per-tube cycle time comes from.</para>
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(1.5)]
    public void WhetherTheBusCanFlyOneTubesTrim(double lateralAcceleration)
    {
        BallisticArc.Solution arc = Shot(out double3 from, out double3 _);
        double3 v = arc.RequiredVelocityCci;

        double3 nose = -Vec.Unit(v);
        double3[] axes = Axes(Attitude(v));
        double3 mean = ReleasePointing.ReferenceAxis(axes);

        // What the bus must be doing for tube 1 to depart on the arc the correction converged
        // against: the solution less that tube's own deviation from the mean kick.
        double3 owed = (axes[0] - mean) * Warhead.LaunchSpeed;
        double3 wanted = v - owed;

        TrimBus bus = new()
        {
            PositionCci = from,
            VelocityCci = v,
            NoseCci = nose,
            RightCci = Vec.Unit(Vec.Cross(from, nose)),
            DownCci = -Vec.Unit(from),
            AxialAcceleration = 3.0,
            LateralAcceleration = lateralAcceleration,
        };

        BusTrim trim = new();
        trim.Begin();

        const double step = 1.0 / 60.0;
        double elapsed = 0.0;
        TrimCommand last = default;

        while (elapsed < 120.0)
        {
            last = trim.Update(step, new TrimSituation(
                Earth, bus.PositionCci, bus.VelocityCci, from, wanted, elapsed,
                bus.NoseCci, bus.RightCci, bus.DownCci));

            if (last.Done) break;

            bus.Step(Earth, last.Fire, step);
            elapsed += step;
        }

        // Against the reference carried forward, not the vector it started as: the bus is falling
        // the whole time, and gravity is not something the trim is meant to have removed.
        Assert.True(Kepler.TryCoast(Mu, from, wanted, elapsed, out _, out double3 shouldBeDoing));
        double left = Vec.Len(shouldBeDoing - bus.VelocityCci);

        Out.WriteLine($"lateral jets at {lateralAcceleration:F1} m/s2: {elapsed:F1} s, "
                      + $"{left:F4} m/s of the {Vec.Len(owed):F4} left, "
                      + $"{(trim.GaveUp ? "gave up" : "finished")} - {last.Said}");

        Assert.True(last.Done, "it never stopped");

        if (lateralAcceleration <= 0.0)
        {
            // The shipped layout. Nothing it can fire moves the number, so what it started owing is
            // what it releases on, and the seconds it spent finding that out are spent on every tube.
            Assert.True(trim.GaveUp, "a bus with no lateral jets should not report a trimmed arc");
            Assert.True(left > 0.9 * Vec.Len(owed),
                        $"it removed {Vec.Len(owed) - left:F4} m/s of a purely lateral error with an "
                        + "axial pair, which it cannot do");
        }
        else
        {
            Assert.False(trim.GaveUp, last.Said);
            Assert.True(left < 0.05, $"{left:F4} m/s left with jets that can push it");
        }
    }

    /// <summary>
    /// Where the six come down, released one every <paramref name="paceSeconds"/> from a bus that
    /// is coasting the whole time.
    ///
    /// <para>Every impact is un-carried by its own release delay as well as by its flight, so all
    /// six are places on the ground measured from one epoch. Carrying it the other way reports the
    /// planet's own turn as a miss, which is 465 m a second at the equator.</para>
    /// </summary>
    private double Group(double3 from, double3 v, double3[] axes, double paceSeconds, bool trimmed,
                         double3 target, string what)
    {
        double3 mean = ReleasePointing.ReferenceAxis(axes);
        double3[] landed = new double3[axes.Length];

        for (int i = 0; i < axes.Length; i++)
        {
            double delay = i * paceSeconds;

            Assert.True(Kepler.TryCoast(Mu, from, v, delay, out double3 r, out double3 vv));

            // A perfectly trimmed bus puts every tube's kick on the mean, which is the line the aim
            // correction assumed a round is thrown along.
            double3 kick = (trimmed ? mean : axes[i]) * Warhead.LaunchSpeed;

            Assert.True(ImpactPredictor.TryPredict(Earth, r, vv + kick, 2.0, 20_000.0,
                                                   out ImpactPredictor.Impact hit, null, null,
                                                   new ImpactPredictor.Drag(DensityAt, Warhead)),
                        $"tube {i + 1} never came down");

            landed[i] = Earth.UncarryCci(hit.GroundFixedPointCci, delay);
        }

        double spread = 0.0;
        for (int a = 0; a < landed.Length; a++)
        {
            for (int b = a + 1; b < landed.Length; b++)
            {
                spread = Math.Max(spread, GroundMetres(landed[a], landed[b]));
            }
        }

        double closest = double.MaxValue, furthest = 0.0, average = 0.0;
        foreach (double3 p in landed)
        {
            double m = GroundMetres(p, target);
            closest = Math.Min(closest, m);
            furthest = Math.Max(furthest, m);
            average += m / landed.Length;
        }

        Out.WriteLine($"  {what,-24}: spread {spread,6:F0} m, misses "
                      + $"{closest / 1000.0:F2}-{furthest / 1000.0:F2} km, mean {average / 1000.0:F2} km");

        return spread;
    }
}
