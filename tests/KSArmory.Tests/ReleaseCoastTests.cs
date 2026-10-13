using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The far off-plane coast flown from <c>ORBIT 300</c> to 40 N, 10 E: cut off at 668 km climbing at 5.8 km/s, apogee
/// 17,600 km, arrival 14,763 s later and the release 393 s before it. <c>docs/ICBM-OUTSTANDING.md</c> 1.14.
/// </summary>
public class ReleaseCoastTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;
    private const double EarthSpin = 7.2921159e-5;
    private const double Gate = 393.0;
    private const double Kick = 0.5;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), EarthSpin);

    private static (double3 Position, double3 Velocity) Cutoff()
    {
        double r0 = R + 668_500.0, speed = 9186.2, climb = 5803.4;
        double across = Math.Sqrt(speed * speed - climb * climb);
        double tilt = 50.0 * Math.PI / 180.0;
        double3 up = new(1, 0, 0);
        double3 along = new(0, Math.Cos(tilt), Math.Sin(tilt));
        return (up * r0, up * climb + along * across);
    }

    private static double3 Landing(double3 positionCci, double3 velocityCci, double secondsFromCutoff, out double flight)
    {
        Assert.True(ImpactPredictor.TryPredict(Earth, positionCci, velocityCci, 2.0, ImpactPredictor.DefaultMaxSeconds,
                                               out var hit));
        flight = hit.Seconds + secondsFromCutoff;
        return Earth.UncarryCci(hit.GroundFixedPointCci, secondsFromCutoff);
    }

    [Fact]
    public void AKickAddedAtTheReleaseLandsWhereTheReleaseWouldPutIt()
    {
        var (p0, v0) = Cutoff();
        double3 kick = Vec.Unit(v0) * Kick;

        Landing(p0, v0, 0.0, out double flight);

        Assert.True(Kepler.TryCoast(Mu, p0, v0, flight - Gate, out double3 pr, out double3 vr));
        double3 truth = Landing(pr, vr + kick, flight - Gate, out _);

        double3 at = ReleaseCoast.ToRelease(Earth, p0, v0, flight, Gate, null, out double3 vAt, out double coasted);
        double3 predicted = Landing(at, vAt + kick, coasted, out _);
        double3 atCutoff = Landing(p0, v0 + kick, 0.0, out _);

        double miss = Vec.Len(predicted - truth);
        double wasMiss = Vec.Len(atCutoff - truth);
        Out.WriteLine($"coasted {coasted:F0} s; kick at the release {miss:F2} m from the truth, at cutoff {wasMiss:F0} m");

        Assert.Equal(flight - Gate, coasted, 6);
        Assert.True(miss < 1.0, $"{miss:F2} m");
        Assert.True(wasMiss > 1_000.0, $"the kick at cutoff was only {wasMiss:F0} m out, so this geometry tests nothing");
    }

    [Fact]
    public void AFlightShorterThanTheGateIsNotCoasted()
    {
        var (p0, v0) = Cutoff();

        double3 at = ReleaseCoast.ToRelease(Earth, p0, v0, Gate - 1.0, Gate, null, out double3 vAt, out double coasted);

        Assert.Equal(0.0, coasted);
        Assert.Equal(p0, at);
        Assert.Equal(v0, vAt);
    }

    [Fact]
    public void ACoastEndingInAirIsNotFlownInVacuum()
    {
        var (p0, v0) = Cutoff();

        double3 at = ReleaseCoast.ToRelease(Earth, p0, v0, 5_000.0, Gate, _ => 1.0, out double3 vAt, out double coasted);

        Assert.Equal(0.0, coasted);
        Assert.Equal(p0, at);
        Assert.Equal(v0, vAt);
    }
}
