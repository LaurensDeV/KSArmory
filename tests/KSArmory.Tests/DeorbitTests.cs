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
    [InlineData("in plane, ahead", 0.0, 0.0, 1.2, 300_000.0, 2_000.0)]
    [InlineData("in plane, far ahead (a grazing entry)", 0.0, 0.0, 2.6, 300_000.0, 6_000.0)]
    [InlineData("in plane, just ahead", 0.0, 0.0, 0.3, 300_000.0, 2_000.0)]
    [InlineData("twenty degrees off the track", 0.0, 0.35, 1.2, 300_000.0, 2_000.0)]
    [InlineData("forty-five degrees off it", 0.0, 0.79, 1.2, 300_000.0, 2_000.0)]
    [InlineData("inclined orbit, equatorial target", 0.89, 0.0, 1.2, 300_000.0, 2_000.0)]
    [InlineData("inclined orbit, northern target", 0.89, 0.79, 1.2, 300_000.0, 2_000.0)]
    [InlineData("from 800 km", 0.0, 0.0, 1.2, 800_000.0, 2_000.0)]
    [InlineData("from 150 km", 0.0, 0.0, 1.2, 150_000.0, 2_000.0)]
    public void ItDeorbitsOntoTheTarget(string label, double inclination, double targetLat,
                                        double targetLon, double altitude, double tolerance)
    {
        IcbmFlightRig rig = InOrbit(altitude, inclination);
        double3 aim = At(targetLat, targetLon);

        IcbmProgram program = new(new IcbmConfig { Armed = true });
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 1800.0);

        Assert.True(flight.Reached, $"{label}: never reached cutoff - {flight.Hold}");
        Assert.DoesNotContain("short of the solution", flight.Hold);

        double miss = MissMetres(rig, flight, aim);
        Assert.True(miss < tolerance, $"{label} missed by {miss:F0} m");
    }

    /// <summary>
    /// The one geometry that does not work, pinned so it is a known shape rather than a surprise.
    ///
    /// <para>A target <em>behind</em> the vehicle has no single ballistic arc to it. Going forward
    /// the short way means reversing seven kilometres a second of orbital velocity; going the long
    /// way round passes through the planet, so the solver refuses it. The real answer is to stay in
    /// orbit until the target comes round, which is a phase this program does not have — so it
    /// commits to the expensive arc and burns the tank dry.</para>
    ///
    /// <para>It does say so, which is the only reason this is a limitation rather than a bug.</para>
    /// </summary>
    [Fact]
    public void ATargetBehindTheVehicleCannotBeReachedAndSaysSo()
    {
        IcbmFlightRig rig = InOrbit(300_000.0, 0.0);
        double3 aim = At(0.0, -0.6);

        IcbmProgram program = new(new IcbmConfig { Armed = true });
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.05, 1800.0);

        Assert.Contains("short of the solution", flight.Hold);

        IcbmState after = new(Earth, rig.PositionCci, rig.VelocityCci,
                              Earth.CarryCci(aim, flight.CutoffSeconds), HasAim: true,
                              rig.Performance(), 0.0, PropellantAvailable: false);
        Assert.False(program.Update(0.02, after).ReadyToDeploy,
                     "a shot that fell short must not release warheads");
    }
}
