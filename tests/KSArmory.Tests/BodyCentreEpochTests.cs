using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// A round measured against a body that moves, which is every round this mod flies and which no
/// other rig here models.
///
/// <para>KSA hands out one celestial position per frame, taken at the frame's <em>end</em>, while
/// the round is integrated across the frame carrying the same ~30 km/s. So a sample used at a
/// sub-step belongs to a later instant than the round does, and the gap is
/// <c>bodyVelocity × (frame − elapsed)</c> — 513 m at the flown speed and frame.</para>
///
/// <para><b>The test is a co-moving orbit.</b> Put a round in a circular orbit about a body and give
/// both the same ecliptic travel: nothing about the orbit has changed, so its altitude must not
/// move. Anything that reads a stale body sample sees that pure translation as a change of height,
/// and on a shallow arrival an apparent metre of height is eight metres of ground.</para>
/// </summary>
public class BodyCentreEpochTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;
    private const double Altitude = 200_000.0;

    /// <summary>Earth's own ecliptic travel, pointed straight up at the round to make it bite.</summary>
    private static double3 Carrier => new(30_000.0, 0, 0);

    private const double Frame = 0.0172;

    /// <summary>The flown coast, so the term has the time it has in flight to accumulate.</summary>
    private const double CoastSeconds = 375.0;

    /// <summary>
    /// The ground as the engine reports it: one sample per frame, taken at the frame's end, so it
    /// is always one frame ahead of a round part-way through that frame.
    /// </summary>
    private sealed class MovingGround : IGroundTest
    {
        public double3 StartEcl;
        public double3 VelocityEcl;
        public double Frame;
        public int FramesIssued;

        public double3 SampleEcl => StartEcl + VelocityEcl * ((FramesIssued + 1) * Frame);

        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = SampleEcl;
            surfaceRadius = R;
            return true;
        }
    }

    private static MunitionProfile Coasting => new()
    {
        Name = "TEST",
        DisplayName = "a round that only falls",
        HitsTerrain = true,
        MaxFlightSeconds = 10_000f,
        SubStepSeconds = 0.005f,
        DragK = 0f,
        ChargeKg = 1f,
    };

    /// <summary>
    /// Fly a co-moving circular orbit for a real coast, and return where it ended relative to where
    /// the body honestly is by then.
    /// </summary>
    private static double3 EndOffset(bool correcting, double frame, double3 carrier)
    {
        double r0 = R + Altitude;

        MovingGround ground = new() { StartEcl = Vec.Zero, VelocityEcl = carrier, Frame = frame };

        double3 start = new double3(r0, 0, 0);
        MunitionProfile munition = Coasting;

        Slug round = new(start, new double3(0, Math.Sqrt(Mu / r0), 0) + carrier, null, 1, start,
                         Vec.Zero)
        {
            Munition = munition,
            Ground = ground,
            AirDensityAt = (_, _) => 0.0,
        };

        int frames = (int)(CoastSeconds / frame);

        for (int i = 0; i < frames && round.State == RoundState.Flying; i++)
        {
            // Exactly what WeaponSystem.GravityAtRound composes: one vector for the frame, aimed
            // either at the sample as taken or at where the body was half-way through the frame.
            double3 aimAt = correcting ? ground.SampleEcl - carrier * (0.5 * frame)
                                       : ground.SampleEcl;

            double3 toAim = aimAt - round.PositionEcl;

            round.Update(frame, null, Vec.Unit(toAim) * (Mu / Vec.Len2(toAim)), carrier, start,
                         munition, 0.0);
            ground.FramesIssued++;
        }

        return round.PositionEcl - carrier * (frames * frame);
    }

    /// <summary>
    /// <b>The fault and the fix.</b> A coarse frame must fly the same orbit a fine one does, and
    /// with a body sample a frame ahead of the round it does not.
    ///
    /// <para>Scored against a 1 ms reference, where the staleness is seventeen times smaller and
    /// everything else is unchanged — so what is left between the two is this term and nothing
    /// else.</para>
    /// </summary>
    [Fact]
    public void ACoarseFrameFliesTheFineFramesOrbitOnlyWhenTheAimFollowsTheBody()
    {
        double3 reference = EndOffset(correcting: true, 0.001, Carrier);

        double stale = Vec.Len(EndOffset(false, Frame, Carrier) - reference);
        double corrected = Vec.Len(EndOffset(true, Frame, Carrier) - reference);

        Out.WriteLine($"over {CoastSeconds:F0} s at {Frame * 1000:F1} ms against a 1 ms reference: "
                      + $"sample a frame ahead {stale:F0} m, aimed at the mid-frame centre {corrected:F0} m");

        Assert.True(stale > 50.0,
                    $"the stale sample only cost {stale:F0} m — the fault is not being reproduced");

        Assert.True(corrected < stale * 0.75,
                    $"correcting left {corrected:F0} m against {stale:F0} m stale");
    }

    /// <summary>
    /// With the body at rest the correction is identically nothing — not merely small.
    ///
    /// <para>That is the property that makes this one change rather than two. Aiming the frame's
    /// single gravity vector at the body's mid-frame position leaves the number of evaluations, the
    /// sub-step count and the held-for-the-frame convention exactly as they were; only where it
    /// points moves, and with a still body it does not move at all. Re-aiming <em>per sub-step</em>
    /// would correct another 16 m here and would bundle in gravity re-read per sub-step, which has
    /// been flown alone and lost — <c>docs/MIRV-NEXT.md</c> item 2d, priced by <c>ProbeGapTests</c>
    /// at -740 m on the deorbit arc.</para>
    /// </summary>
    [Fact]
    public void WithTheBodyAtRestNothingChangesAtAll()
    {
        double3 held = EndOffset(correcting: false, Frame, Vec.Zero);
        double3 aimed = EndOffset(correcting: true, Frame, Vec.Zero);

        Out.WriteLine($"body at rest, {CoastSeconds:F0} s at {Frame * 1000:F1} ms: "
                      + $"{Vec.Len(aimed - held):F3} m apart");

        Assert.Equal(0.0, Vec.Len(aimed - held), 6);
    }

    /// <summary>
    /// The round is not asked to know any of this. The choice of where to aim belongs to whoever
    /// composes the vector — which is why it lives in <c>WeaponSystem.GravityAtRound</c> beside the
    /// body's own fall, and why aiming it there cannot discard that term the way re-deriving it
    /// inside the round did.
    /// </summary>
    [Fact]
    public void TheRoundIsHandedAVectorAndKnowsNothingAboutBodies()
    {
        Assert.Null(typeof(Slug).GetProperty("BodyMu"));
        Assert.Null(typeof(Slug).GetProperty("BodyVelocityEcl"));
    }
}
