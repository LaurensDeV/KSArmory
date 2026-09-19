using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The flown round reads the ocean in the sub-step it arrives on over ground at the waterline, and it
/// costs speed rather than a landing.
/// </summary>
/// <remarks>
/// <para>3ep sent the <i>prediction</i> through the air alone. The round itself still reads
/// <c>KsaWorld.MediumDensityRatioAt</c>, which answers with the ocean anywhere under the mean sphere,
/// and a second-order round reads it half a sub-step <i>ahead</i> of where it is — so at a sea-level aim
/// the arriving sub-step samples 837x the air under the surface it is about to stop on. Under the
/// Chaco's 204 m, or 50 m, that look-ahead never reaches the mean sphere at all, which is why no flight
/// has shown it.</para>
///
/// <para><b>It cannot move the landing, and the reason is geometric rather than small.</b> Drag is
/// anti-parallel to the step, so a spurious 837x shortens the step's chord without turning it, and the
/// crossing is found by interpolating along that chord — the same point on the same ray. What it moves
/// is the speed the round arrives at, by hundreds of m/s, which nothing scores and nothing draws. Both
/// halves are pinned here: a change that let the read turn the step instead would move a landing at
/// every sea-level target, and nothing else in the suite is over water.</para>
/// </remarks>
public class WaterlineDragProbe(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static double AirAlone(double3 p)
    {
        double altitude = Math.Max(0.0, Vec.Len(p) - R);
        return altitude >= 167_410.0 ? 0.0 : Math.Exp(-altitude / 8_000.0);
    }

    private sealed class Ground(double metres) : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Vec.Zero;
            surfaceRadius = R + metres;
            return true;
        }
    }

    [Fact]
    public void TheOceanIsReadOnlyAtTheWaterlineAndCostsSpeedRatherThanALanding()
    {
        Out.WriteLine("KSA's medium against the air alone, one flown release, 26 ms frames\n");
        Out.WriteLine("ground      apart    ocean reads    impact speed");

        foreach ((double groundMetres, int expected) in new[] { (203.9, 0), (50.0, 0), (0.0, 1) })
        {
            Landing wet = Fly(PredictorEndingTests.KsaMedium, groundMetres);
            Landing dry = Fly(AirAlone, groundMetres);

            Out.WriteLine($"{groundMetres,6:F1} m {Vec.Len(wet.At - dry.At) * 1000.0,9:F3} mm {wet.OceanReads,12}"
                          + $"    {wet.Speed - dry.Speed,8:F2} m/s");
            foreach (string line in wet.Said) Out.WriteLine(line);

            Assert.Equal(expected, wet.OceanReads);
            Assert.Equal(0, dry.OceanReads);

            // A micron, against the millimetre a night prints and the metres the chord moved.
            Assert.True(Vec.Len(wet.At - dry.At) < 1e-6, $"landing moved {Vec.Len(wet.At - dry.At) * 1000.0:F6} mm");

            if (expected == 0) Assert.Equal(dry.Speed, wet.Speed);
            else Assert.True(dry.Speed - wet.Speed > 100.0, $"speed lost {dry.Speed - wet.Speed:F2} m/s");
        }
    }

    private readonly record struct Landing(double3 At, double Speed, int OceanReads, List<string> Said);

    private static Landing Fly(Func<double3, double> medium, double groundMetres)
    {
        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;
        List<string> said = [];
        int wet = 0;

        Slug round = new(PredictorEndingTests.FlownPositionCci, PredictorEndingTests.FlownVelocityCci,
                         null, 1, PredictorEndingTests.FlownPositionCci, Vec.Zero)
        {
            Munition = warhead,
            SecondOrder = true,
            DragAtMidpointVelocity = true,
            StopOnTheTerrain = true,
            ResampleGroundNearImpact = true,
            GroundQueryAtOwnEpoch = true,
        };

        double Density(double3 p, double _)
        {
            double density = medium(p);
            if (density <= 100.0) return density;

            wet++;
            said.Add($"       read {density:F0}x the air {Vec.Len(p) - R - groundMetres:F3} m under the ground, "
                     + $"with the round {Vec.Len(round.PositionEcl) - R - groundMetres:F3} m over it "
                     + $"at {Vec.Len(round.VelocityEcl):F0} m/s");
            return density;
        }

        double3 Pull(double3 p, double _) => Vec.Unit(-p) * (Mu / Vec.Len2(p));

        RoundFields fields = new(
            GravityAt: Pull,
            AirDensityAt: Density,
            Ground: new Ground(groundMetres),
            GroundCentreDriftAt: _ => Vec.Zero,
            GroundQueryDriftAt: (_, _) => Vec.Zero);

        for (int i = 0; i < 100_000 && round.State == RoundState.Flying; i++)
        {
            RoundDriver.Fly(round, 0.026, null, Pull(round.PositionEcl, 0.0), Vec.Zero, Vec.Zero,
                            warhead, 0.0, fields);
        }

        Assert.True(round.HitGround);
        return new Landing(round.PositionEcl, Vec.Len(round.VelocityEcl), wet, said);
    }
}
