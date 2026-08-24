using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The same computer flown from orbit rather than from a pad — a deorbit rather than a launch.
///
/// <para>Nothing in <see cref="IcbmProgram"/> was written for this, which is exactly why it is
/// worth pinning: the phase machine starts at the pad every time, so from orbit it runs the
/// vertical rise and the pitch programme in the space of two frames and hands straight over. That
/// it arrives anyway is a property of the guidance being terminal rather than scheduled, and it is
/// the sort of property that stops holding the moment somebody adds a phase.</para>
/// </summary>
public class DeorbitTests
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    private static double3 At(double lat, double lon, double alt = 0.0)
    {
        double r = R + alt;
        return new(r * Math.Cos(lat) * Math.Cos(lon), r * Math.Cos(lat) * Math.Sin(lon), r * Math.Sin(lat));
    }

    private static IcbmFlightRig InOrbit(double altitude, double inclination)
    {
        double3 position = new(R + altitude, 0, 0);
        double speed = Math.Sqrt(Mu / (R + altitude));

        return new IcbmFlightRig
        {
            Body = Earth,
            PositionCci = position,
            VelocityCci = new double3(0, speed * Math.Cos(inclination), speed * Math.Sin(inclination)),
            Stages = [new() { DryMassKg = 3_000, PropellantKg = 40_000, ThrustNewtons = 300_000, ExhaustVelocity = 3_100 }],
        };
    }

    private static double MissMetres(IcbmFlightRig rig, in IcbmFlightRig.Flight flight, double3 aim)
    {
        Assert.True(ImpactPredictor.TryPredict(rig.Body, flight.CutoffPositionCci, flight.CutoffVelocityCci,
                                               2.0, 20_000.0, out ImpactPredictor.Impact hit),
                    "it never came down");

        return R * Vec.AngleBetween(hit.GroundFixedPointCci, rig.Body.CarryCci(aim, flight.CutoffSeconds));
    }

    /// <param name="tolerance">
    /// Not one number for every case, because the geometry does not allow one. A shallow deorbit
    /// is intrinsically sensitive to burnout velocity — the arc grazes the atmosphere for thousands
    /// of kilometres, so a centimetre a second at cutoff is kilometres at the far end. That is the
    /// same reason a real re-entry corridor is narrow, and it is a property of the trajectory
    /// rather than anything guidance can close.
    /// </param>
    [Theory]
    [InlineData("in plane, ahead", 0.0, 0.0, 1.2, 300_000.0, 2_000.0, 6_000.0)]
    [InlineData("in plane, far ahead (a grazing entry)", 0.0, 0.0, 2.6, 300_000.0, 6_000.0, 6_000.0)]
    [InlineData("in plane, just ahead", 0.0, 0.0, 0.3, 300_000.0, 2_000.0, 6_000.0)]
    [InlineData("twenty degrees off the track", 0.0, 0.35, 1.2, 300_000.0, 2_000.0, 6_000.0)]
    [InlineData("forty-five degrees off it", 0.0, 0.79, 1.2, 300_000.0, 2_000.0, 6_000.0)]
    // From a steeply inclined orbit an equatorial target is reached at a node, and the cheapest
    // node can be some way round — so this one is given room to wait for it.
    [InlineData("inclined orbit, equatorial target", 0.89, 0.0, 1.2, 300_000.0, 2_000.0, 30_000.0)]
    [InlineData("inclined orbit, northern target", 0.89, 0.79, 1.2, 300_000.0, 2_000.0, 6_000.0)]
    [InlineData("from 800 km", 0.0, 0.0, 1.2, 800_000.0, 2_000.0, 6_000.0)]
    [InlineData("from 150 km", 0.0, 0.0, 1.2, 150_000.0, 2_000.0, 6_000.0)]
    public void ItDeorbitsOntoTheTarget(string label, double inclination, double targetLat,
                                        double targetLon, double altitude, double tolerance,
                                        double horizonSeconds)
    {
        IcbmFlightRig rig = InOrbit(altitude, inclination);
        double3 aim = At(targetLat, targetLon);

        IcbmProgram program = new(new IcbmConfig { Armed = true });
        // Long enough for the computer to wait out a window. It searches across a day's worth of
        // revolutions, because the planet turning is what brings a target under the track, so a
        // horizon of one orbit would be asserting that it never waits.
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, horizonSeconds);

        Assert.True(flight.Reached, $"{label}: never reached cutoff - {flight.Hold}");
        Assert.DoesNotContain("short of the solution", flight.Hold);

        double miss = MissMetres(rig, flight, aim);
        Assert.True(miss < tolerance, $"{label} missed by {miss:F0} m");
    }

    /// <summary>
    /// A target the vehicle has just passed over, which has no arc to it from where it is.
    ///
    /// <para>Forward the short way means reversing seven kilometres a second of orbital velocity;
    /// the long way round passes through the planet, so the solver refuses it. A computer that can
    /// only leave <em>now</em> therefore takes the first of those, at eleven kilometres a second,
    /// burns the tank dry and lands on the wrong continent.</para>
    ///
    /// <para>Searching over departure time finds the same target for a couple of hundred metres a
    /// second, most of a revolution later. So it holds, and then it goes — and the assertions below
    /// are that it held, that it still has its propellant, and that it arrived.</para>
    /// </summary>
    [Fact]
    public void ATargetBehindTheVehicleIsReachedByWaitingForTheWindow()
    {
        IcbmFlightRig rig = InOrbit(300_000.0, 0.0);
        double3 aim = At(0.0, -0.6);

        // No arrival floor: the point of the test is that waiting finds an affordable window, and
        // a floor is a second constraint on the same search.
        IcbmProgram program = new(new IcbmConfig { Armed = true, MinArrivalAngleDeg = 0.0 });
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 9000.0);

        Assert.True(flight.Reached, $"never reached cutoff - {flight.Hold}");
        Assert.DoesNotContain("short of the solution", flight.Hold);

        // The load-bearing part is that it waited at all rather than burning at once. Exactly how
        // long is the geometry's business: the window is wherever the cheapest departure falls.
        Assert.True(flight.CutoffSeconds > 1000.0,
                    $"it should have held for the window, not burnt at {flight.CutoffSeconds:F0} s");
        Assert.True(flight.PropellantLeftKg > 30_000.0,
                    $"only {flight.PropellantLeftKg:F0} kg left - it burnt the expensive arc after all");

        // Looser than the rest of this file, and the geometry is why. Waiting most of a revolution
        // puts the departure nearly opposite the target, so the arc is both very long and very
        // shallow — the most sensitive shape there is to a centimetre a second at cutoff, and the
        // same reason a real re-entry corridor is narrow. Twelve kilometres on a twelve-thousand
        // kilometre delivery is the geometry, not the guidance.
        double miss = MissMetres(rig, flight, aim);
        Assert.True(miss < 25_000.0, $"missed by {miss / 1000.0:F1} km");
    }

    /// <summary>
    /// And it says so while it waits, with a time. A computer sitting there doing nothing for an
    /// hour and a half is indistinguishable from a broken one without it.
    /// </summary>
    [Fact]
    public void WhileItWaitsItSaysHowLongFor()
    {
        IcbmFlightRig rig = InOrbit(300_000.0, 0.0);
        double3 aim = At(0.0, -0.6);

        IcbmProgram program = new(new IcbmConfig { Armed = true });

        IcbmState state = new(Earth, rig.PositionCci, rig.VelocityCci, aim, HasAim: true,
                              rig.Performance(), 0.0, PropellantAvailable: true);

        IcbmCommand command = program.Update(0.0, state);

        Assert.Equal(IcbmPhase.Holding, command.Phase);
        Assert.False(command.EngineOn);
        Assert.Contains("holding for the burn window", command.Hold);
        Assert.True(command.SecondsToBurn > 60.0, "the window is not seconds away");
        Assert.True(double.IsFinite(command.SecondsToArrival),
                    "and it still knows when the warheads land");
    }
}
