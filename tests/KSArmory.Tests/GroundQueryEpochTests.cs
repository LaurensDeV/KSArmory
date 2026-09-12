using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Which instant the ground a round stops against is asked <em>at the rotation of</em>.
///
/// <para>Sibling of <see cref="GroundSampleEpochTests"/>, which pins the translation half. A terrain
/// query is not a position: <c>Ksa/GroundTest.cs</c> resolves a <em>direction</em> from the point it
/// is handed and the engine answers it through <c>GetCcf2Cce()</c> — the frame's <b>end</b> rotation.
/// Back-dating the body's travel alone leaves its spin in, so the query lands on ground that has
/// turned under the round by <c>|omega x r|</c> times the sub-step's own distance into the frame.
/// <c>docs/ACCURACY-PLAN.md</c> 3cw.</para>
///
/// <para><b>The sign is the thing to pin.</b> The correction walks the query point the way the ground
/// is going; the rotation sense backwards subtracts what should be added and <em>doubles</em> the
/// term rather than removing it, which from outside reads like a fix that made things worse.</para>
/// </summary>
public class GroundQueryEpochTests
{
    // Earth's spin, and a point on it at the latitude the flown shots arrive at, where the spin
    // carries the ground at 415 m/s.
    private static readonly double3 Spin = new(0, 0, 7.2921e-5);

    // Square to the spin term below, which is the one thing about this rig that is arranged rather
    // than physical: it keeps the body's 29.8 km/s of travel off the east axis the ramp slopes
    // along, so a height read here is the rotation and nothing else. The travel is what
    // GroundSampleEpochTests pins, and it is pinned there against this same seam.
    private static readonly double3 BodyVelocityEcl = new(29_800.0, 0, 0);

    private const double BodyRadius = 6_371_000.0;

    // On the sphere rather than near it: the heights below are metres, so a radius out by a hundred
    // is the fixture and not the code.
    private static readonly double3 Start = Vec.Unit(new double3(0.8942, 0, 0.4477)) * BodyRadius;

    // The body is at the origin here, so a point's separation from the centre is the point.
    private static double3 SpinVelocityAt(double3 positionEcl) => Vec.Cross(Spin, positionEcl);

    private static double3 GroundVelocityAt(double3 positionEcl)
        => BodyVelocityEcl + SpinVelocityAt(positionEcl);

    private static Func<double, double3> CentreDrift => seconds => BodyVelocityEcl * seconds;

    private static Func<double3, double, double3> QueryDrift
        => (position, seconds) => GroundVelocityAt(position) * seconds;

    // The direction the spin carries the ground at Start, which is local east.
    private static readonly double3 East = Vec.Unit(SpinVelocityAt(Start));

    /// <summary>
    /// Records where it was asked and answers a surface far below, so nothing stops. What is under
    /// test is the question.
    /// </summary>
    private sealed class Recorder : IGroundTest
    {
        public readonly List<double3> Asked = [];

        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double radiusMetres)
        {
            Asked.Add(positionEcl);
            centreEcl = Vec.Zero;
            radiusMetres = BodyRadius - 100_000.0;
            return true;
        }
    }

    /// <summary>
    /// Ground that rises to the east — along the direction the body's spin carries a point — so the
    /// displacement under test is the one a gradient can be read off.
    /// </summary>
    private sealed class EastwardRamp : IGroundTest
    {
        public required double Gradient { get; init; }

        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double radiusMetres)
        {
            centreEcl = Vec.Zero;
            radiusMetres = BodyRadius + Gradient * Vec.Dot(positionEcl - Start, East);
            return true;
        }
    }

    private static Slug Round(double3 startEcl, double3 velocityEcl, IGroundTest ground,
                              bool atOwnEpoch, bool resample = false)
        => new(startEcl, velocityEcl, null, 1, Vec.Zero, Vec.Zero)
        {
            Munition = Catalogue.MunitionNamed("MK21"),
            Ground = ground,
            GroundCentreDriftAt = CentreDrift,
            GroundQueryDriftAt = QueryDrift,
            GroundQueryAtOwnEpoch = atOwnEpoch,
            ResampleGroundNearImpact = resample,
        };

    // A round riding the body rather than standing still in the ecliptic while the planet leaves:
    // it starts at a stated height above Start as measured from the centre at the FRAME'S START, and
    // carries the body's own travel. Without both, the centre's back-date shows up as ~900 m of
    // spurious altitude and the round never reaches the ground at all.
    private static Slug Falling(double heightMetres, double downwardSpeed, double dt,
                                IGroundTest ground, bool atOwnEpoch, bool resample = false)
    {
        double3 up = Vec.Unit(Start);

        return Round(Start + up * heightMetres - BodyVelocityEcl * dt,
                     BodyVelocityEcl - up * downwardSpeed,
                     ground, atOwnEpoch, resample);
    }

    private static void Fly(Slug slug, double dt)
    {
        double3 up = Vec.Unit(Start);

        slug.Update(dt, null, up * -9.8, BodyVelocityEcl, Vec.Zero, slug.Munition, 0.0);
    }

    /// <summary>
    /// <b>The query is walked forward by the whole ground velocity, spin included.</b> Fails against
    /// the shipped code — which walks it by the body's centre alone — by exactly the spin term over
    /// one frame.
    /// </summary>
    [Theory]
    [InlineData(0.0167)]
    [InlineData(0.0333)]
    public void TheGroundQueryCarriesTheBodysSpinAndNotOnlyItsTravel(double dt)
    {
        Recorder ground = new();
        Slug slug = Round(Start, BodyVelocityEcl, ground, atOwnEpoch: true);

        double spinStep = Vec.Len(SpinVelocityAt(Start)) * dt;

        // The regime, before the rule. A fixture whose spin term is nothing passes against every
        // implementation here, including the one this exists to refuse.
        Assert.True(spinStep > 5.0,
                    $"the spin carries the ground {spinStep:F2} m over this frame, too little for "
                    + "the test to be about anything");

        Fly(slug, dt);

        Assert.NotEmpty(ground.Asked);

        double3 wanted = Start + GroundVelocityAt(Start) * dt;
        double3 centreOnly = Start + BodyVelocityEcl * dt;

        Assert.True(Vec.Len(ground.Asked[0] - wanted) < 0.01,
                    $"the ground was asked {Vec.Len(ground.Asked[0] - centreOnly):F2} m from where "
                    + $"the centre-only back-date puts it, wanted {spinStep:F2} m — the spin the "
                    + "query's own frame turns through between the sub-step and the frame's end");
    }

    /// <summary>
    /// <b>The rotation sense, pinned.</b> Three points are available and only one is right: the
    /// shipped centre-only back-date, the correction, and the correction applied backwards. The last
    /// sits twice as far from the answer as doing nothing at all, which is what makes getting the
    /// sense wrong worse than leaving the term in.
    /// </summary>
    [Fact]
    public void TheRotationSenseIsPinnedByWhichOfThreePointsIsAsked()
    {
        const double dt = 0.0333;

        Recorder ground = new();
        Slug slug = Round(Start, BodyVelocityEcl, ground, atOwnEpoch: true);

        Fly(slug, dt);

        double3 spin = SpinVelocityAt(Start);
        double spinStep = Vec.Len(spin) * dt;

        Assert.InRange(spinStep, 12.0, 16.0);

        double3 forward = Start + (BodyVelocityEcl + spin) * dt;
        double3 centreOnly = Start + BodyVelocityEcl * dt;
        double3 backward = Start + (BodyVelocityEcl - spin) * dt;

        double3 asked = Assert.Single(ground.Asked);

        Assert.True(Vec.Len(asked - forward) < 0.01,
                    $"asked {Vec.Len(asked - forward):F2} m from the point the ground's own velocity "
                    + $"carries it to, {Vec.Len(asked - centreOnly):F2} m from the shipped one and "
                    + $"{Vec.Len(asked - backward):F2} m from the reversed one");

        // What each of the other two reads, stated rather than implied: doing nothing leaves one
        // spin-step of error and reversing the sense leaves two.
        Assert.Equal(spinStep, Vec.Len(forward - centreOnly), 2);
        Assert.Equal(2.0 * spinStep, Vec.Len(forward - backward), 2);
    }

    /// <summary>
    /// <b>And the seam arrives through the driver</b>, which is the only way it reaches a round in
    /// game. A round set up by hand proves the arithmetic and nothing about the wiring, and the seam
    /// is one line in <see cref="RoundDriver"/> away from being supplied to nothing.
    /// </summary>
    [Fact]
    public void TheQuerySeamIsOneOfTheFieldsTheDriverHandsOver()
    {
        const double dt = 0.0333;

        Recorder ground = new();

        Slug slug = new(Start, BodyVelocityEcl, null, 1, Vec.Zero, Vec.Zero)
        {
            Munition = Catalogue.MunitionNamed("MK21"),
            GroundQueryAtOwnEpoch = true,
        };

        RoundFields fields = new(null, null, ground, CentreDrift, QueryDrift);

        RoundDriver.Fly(slug, dt, null, Vec.Unit(Start) * -9.8, BodyVelocityEcl, Vec.Zero,
                        slug.Munition, 0.0, fields);

        double3 asked = Assert.Single(ground.Asked);

        Assert.True(Vec.Len(asked - (Start + GroundVelocityAt(Start) * dt)) < 0.01,
                    $"asked {Vec.Len(asked - (Start + BodyVelocityEcl * dt)):F2} m from the "
                    + "centre-only back-date, so the driver handed over the centre drift and not "
                    + "the query drift");
    }

    /// <summary>
    /// <b>Off, the question is exactly the one it always was</b> — even with the query seam supplied,
    /// which in game it always is. The switch is what a paired night flies, so a leak either way
    /// makes both arms one arm.
    /// </summary>
    [Fact]
    public void OffTheQueryIsExactlyWhereTheCentreOnlyBackDatePutsIt()
    {
        const double dt = 0.0333;

        Recorder ground = new();
        Slug slug = Round(Start, BodyVelocityEcl, ground, atOwnEpoch: false);

        Fly(slug, dt);

        double3 asked = Assert.Single(ground.Asked);

        Assert.True(Vec.Len(asked - (Start + BodyVelocityEcl * dt)) < 1e-6);
        Assert.True(Vec.Len(asked - (Start + GroundVelocityAt(Start) * dt))
                    > 0.5 * Vec.Len(SpinVelocityAt(Start)) * dt);
    }

    /// <summary>
    /// <b>And the near-impact re-reads are back-dated the same way.</b> Those are the queries that
    /// decide where a warhead stops, and each runs at its own distance into the frame rather than at
    /// a whole one — so the term is smaller there and signed the same.
    /// </summary>
    [Fact]
    public void TheCrossingsOwnQueryIsBackDatedToTheCrossingsOwnInstant()
    {
        const double dt = 0.0333;

        EastwardRamp flat = new() { Gradient = 0.0 };
        Slug slug = Falling(60.0, 4_000.0, dt, flat, atOwnEpoch: true, resample: true);

        Fly(slug, dt);

        Assert.True(slug.HitGround, "the round has to reach the ground for this to be about anything");
        Assert.InRange(-slug.DetonationElapsedInFrame, 0.001, dt);

        double3 wanted = slug.PositionEcl
                         - GroundVelocityAt(slug.PositionEcl) * slug.DetonationElapsedInFrame;

        Assert.True(Vec.Len(slug.GroundSampledAtEcl - wanted) < 0.01,
                    "the crossing's own query was recorded "
                    + $"{Vec.Len(slug.GroundSampledAtEcl - wanted):F3} m from where the ground's "
                    + "velocity over the crossing puts it");
    }

    /// <summary>
    /// <b>The outcome: on a ground that slopes, the round is told about a different height.</b> The
    /// query moves east by a spin-step, so a ramp rising east answers that much higher — gradient
    /// times displacement, with nothing else in the flight moving.
    /// </summary>
    [Fact]
    public void AGroundRisingEastIsReadHigherByTheGradientTimesTheSpinStep()
    {
        const double dt = 0.0333;
        const double gradient = 0.3;

        double spinStep = Vec.Len(SpinVelocityAt(Start)) * dt;
        Assert.InRange(spinStep, 12.0, 16.0);

        double Read(bool atOwnEpoch)
        {
            EastwardRamp ramp = new() { Gradient = gradient };
            Slug slug = Round(Start, BodyVelocityEcl, ramp, atOwnEpoch);

            Fly(slug, dt);

            return slug.GroundRadiusUsed;
        }

        double shipped = Read(atOwnEpoch: false);
        double corrected = Read(atOwnEpoch: true);

        Assert.True(double.IsFinite(shipped) && double.IsFinite(corrected));

        // Signed: east is where the ground is going, so the corrected query reads the higher ground.
        // Reversing the sense reads it lower by the same amount, which is the tell.
        Assert.Equal(gradient * spinStep, corrected - shipped, 2);
        Assert.InRange(corrected - shipped, 3.5, 4.5);
    }

    /// <summary>
    /// <b>And that is where the warhead stops.</b> Same ramp, flown into it: the round holds the
    /// radius it was told about, so it bursts that much higher up — which is the per-seat step from
    /// the last re-fly to the landing that <c>docs/ACCURACY-PLAN.md</c> 3cw measures.
    /// </summary>
    [Fact]
    public void TheWarheadStopsOnTheGroundItWasToldAbout()
    {
        const double dt = 0.0333;
        const double gradient = 0.3;

        double spinStep = Vec.Len(SpinVelocityAt(Start)) * dt;

        double Stop(bool atOwnEpoch)
        {
            EastwardRamp ramp = new() { Gradient = gradient };
            Slug slug = Falling(60.0, 4_000.0, dt, ramp, atOwnEpoch);

            Fly(slug, dt);

            Assert.True(slug.HitGround,
                        "the round has to reach the ground for this to be about anything");

            // Against the centre at the instant it stopped, which is the only radius that means
            // anything while the body is travelling.
            return Vec.Len(slug.PositionEcl - BodyVelocityEcl * slug.DetonationElapsedInFrame);
        }

        double shipped = Stop(atOwnEpoch: false);
        double corrected = Stop(atOwnEpoch: true);

        double wanted = gradient * spinStep;

        Assert.InRange(corrected - shipped, wanted - 0.05, wanted + 0.05);
    }
}
