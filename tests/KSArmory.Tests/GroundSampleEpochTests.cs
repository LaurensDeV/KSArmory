using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Which instant the ground a round stops against is sampled at.
///
/// <para>Sibling of <see cref="AirSampleEpochTests"/>, and the same fault one lookup along.
/// <c>Ksa/GroundTest.cs</c> differences whatever position it is handed against
/// <c>nearest.GetPositionEcl()</c>, a body sample one applied step newer than the round's pre-step
/// position — so handing it the raw position reads the height field <c>bodyVelocity * dt</c> away.
/// Flown: 548 to 8,051 m of displacement against a within-frame ground track of 6 to 183 m.</para>
///
/// <para><b>It decides where the round stops.</b> The round then holds a surface radius belonging to
/// somewhere else, and that height error times <c>cot(gamma)</c> is its entire miss from its own
/// release probe — measured at r = 0.991 over eight flown warheads,
/// <c>docs/ACCURACY-PLAN.md</c> 3aj and 3al.</para>
/// </summary>
public class GroundSampleEpochTests
{
    /// <summary>
    /// The one seam that knows how far the body moves within a frame, as a rig supplies it. Positive
    /// seconds are forward from the sample, so a whole frame back is <c>-dt</c> — the same convention
    /// <see cref="Slug.AirDensityAt"/> is asked in.
    /// </summary>
    private static Func<double, double3> DriftOf(double3 bodyVelocityEcl)
        => seconds => bodyVelocityEcl * seconds;

    /// <summary>
    /// Records where it was asked, and answers a surface far below so nothing stops. What is under
    /// test is the question, not the answer.
    /// </summary>
    private sealed class Recorder : IGroundTest
    {
        public readonly List<double3> Asked = [];

        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double radiusMetres)
        {
            Asked.Add(positionEcl);
            centreEcl = Vec.Zero;
            radiusMetres = 1_000.0;
            return true;
        }
    }

    /// <summary>
    /// <b>The ground is asked at the round's own epoch, not at the frame's end.</b> Fails against a
    /// lookup handed <c>PositionEcl</c> raw, by exactly one frame of the body's travel — which at
    /// 30 km/s and a 33 ms frame is a kilometre of ground.
    /// </summary>
    [Theory]
    [InlineData(0.0167)]
    [InlineData(0.0333)]
    public void TheGroundIsSampledAtTheRoundsOwnEpochAndNotTheFramesEnd(double dt)
    {
        double3 bodyVelocityEcl = new(0, 30_000.0, 0);
        double3 start = new(6_500_000, 0, 0);
        Recorder ground = new();

        var slug = new Slug(start, new double3(0, 2_000, 0), null, 1, Vec.Zero, Vec.Zero)
        {
            Munition = Catalogue.MunitionNamed("MK21"),
            Ground = ground,
            GroundCentreDriftAt = DriftOf(bodyVelocityEcl),
        };

        slug.Update(dt, null, new double3(-9.0, 0, 0), Vec.Zero, Vec.Zero, slug.Munition, 0.5);

        Assert.NotEmpty(ground.Asked);

        // The correction is forward by a frame of the body's travel: putting the body back against a
        // held centre is putting the point on. Getting the sign wrong models the fault doubled.
        double3 wanted = start + bodyVelocityEcl * dt;

        Assert.True(Vec.Len(ground.Asked[0] - wanted) < 1.0,
                    $"the ground was asked at {Vec.Len(ground.Asked[0] - start):F0} m from the "
                    + $"round's raw position, wanted {Vec.Len(wanted - start):F0} m — a frame of "
                    + "the body's own travel, which is what GroundTest differences against");
    }

    /// <summary>
    /// And a rig that models no body motion is unaffected, which is what keeps every existing
    /// fixture meaning what it did: a null drift leaves the question exactly where it was.
    /// </summary>
    [Fact]
    public void ARigWithNoBodyMotionAsksExactlyWhereItAlwaysDid()
    {
        double3 start = new(6_500_000, 0, 0);
        Recorder ground = new();

        var slug = new Slug(start, new double3(0, 2_000, 0), null, 1, Vec.Zero, Vec.Zero)
        {
            Munition = Catalogue.MunitionNamed("MK21"),
            Ground = ground,
        };

        slug.Update(0.02, null, new double3(-9.0, 0, 0), Vec.Zero, Vec.Zero, slug.Munition, 0.5);

        Assert.NotEmpty(ground.Asked);
        Assert.True(Vec.Len(ground.Asked[0] - start) < 1e-6);
    }
}
