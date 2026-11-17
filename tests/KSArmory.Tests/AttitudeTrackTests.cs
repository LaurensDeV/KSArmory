using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A craft's attitude flown under its aerodynamic moment and its inertia.
/// </summary>
public class AttitudeTrackTests
{
    private static AeroTable Table(Func<double3, double3> momentPerQ)
    {
        var table = new AeroTable(3);
        Span<double3> channels = stackalloc double3[AeroTable.Channels];
        for (int i = 0; i < table.Count; i++)
        {
            channels.Clear();
            channels[1] = momentPerQ(table.Direction(i));
            table.Set(i, channels);
        }

        table.Seal();
        return table;
    }

    private static AttitudeTrack Track(AeroTable table, doubleQuat attitude, double3 rates, double3 inertia,
                                       double3 air, double density = 1.0)
        => new(table, attitude, rates, inertia, Vec.Zero,
               new double3(1.0 / inertia.X, 1.0 / inertia.Y, 1.0 / inertia.Z), Vec.Zero, 1.225,
               _ => new AttitudeTrack.Flow(air, density));

    [Fact]
    public void WithNoMomentItTurnsAsTheHeldRatesDo()
    {
        doubleQuat attitude = doubleQuat.CreateFromYawPitchRoll(0.4, -0.2, 0.9);
        var rates = new double3(0.0, 0.0, 0.7);
        var inertia = new double3(5e4, 5e4, 5e4);
        var held = new DragShape(Vec.Zero, Vec.Zero, 1.0, attitude, rates, 1000.0, 1.225, 0.0, 0.0);
        AttitudeTrack track = Track(Table(_ => Vec.Zero), attitude, rates, inertia, new double3(200, 0, 0));

        var nose = new double3(1, 0, 0);
        foreach (double t in new[] { 0.5, 1.7, 3.0 })
        {
            double3 expected = held.Carry(attitude * nose, t);
            Assert.True(Vec.Len(track.ToEcl(nose, t) - expected) < 1e-3, $"at {t} s");
        }
    }

    // Torque-free with an uneven inertia the rates wander, but the angular momentum in the world does not.
    [Fact]
    public void WithNoMomentItsAngularMomentumStaysPut()
    {
        var inertia = new double3(2e4, 5e4, 9e4);
        var rates = new double3(0.3, 0.05, 0.4);
        AttitudeTrack track = Track(Table(_ => Vec.Zero), doubleQuat.Identity, rates, inertia, new double3(200, 0, 0));

        double3 Momentum(double t)
        {
            double3 w = track.RatesAt(t);
            return track.ToEcl(new double3(inertia.X * w.X, inertia.Y * w.Y, inertia.Z * w.Z), t);
        }

        double3 start = Momentum(0.0);
        Assert.True(Vec.Len(Momentum(8.0) - start) < 0.01 * Vec.Len(start));
        Assert.True(Vec.Len(track.RatesAt(8.0) - rates) > 0.01, "the rates should wander when the inertia is uneven");
    }

    [Fact]
    public void AWeathervaningMomentTurnsTheNoseIntoTheWind()
    {
        // Air flowing along -X past a body pointing along +Y: a moment pulling the nose toward the oncoming air.
        AeroTable table = Table(flow => Vec.Cross(new double3(1, 0, 0), -flow) * 50.0);
        doubleQuat sideways = doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), Math.PI / 2);
        AttitudeTrack track = Track(table, sideways, Vec.Zero, new double3(2e4, 2e4, 2e4), new double3(200, 0, 0));

        var nose = new double3(1, 0, 0);
        double before = Vec.Dot(track.ToEcl(nose, 0.0), new double3(1, 0, 0));
        double after = Vec.Dot(track.ToEcl(nose, 0.8), new double3(1, 0, 0));
        Assert.True(after > before + 0.3, $"nose along the wind went from {before:F2} to {after:F2}");
    }
}
