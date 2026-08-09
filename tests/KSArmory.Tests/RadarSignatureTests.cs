using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Detection that depends on what a target <em>is</em>. Three rules, each off at zero and each
/// shipped that way, so the tests come in pairs: what it does when set, and that the set behaves
/// exactly as before when it is not.
/// </summary>
public class RadarSignatureTests
{
    private static SensorProfile Sensor() => new()
    {
        Name = "test",
        DisplayName = "test",
        Range = 20000f,
        ConeDeg = 90f,
        ThreatRadius = 5000f,
        ThreatHorizonSeconds = 40f,
        MinTargetSpeed = 15f,
    };

    private static readonly double3 Up = new(1, 0, 0);

    private static ThreatModel.ContactSignature Of(double radius, double height = double.PositiveInfinity)
        => new(radius, height);

    // ---- The fourth-root law --------------------------------------------

    /// <summary>
    /// The whole reason this is a function. Received power falls as the fourth power of range, so
    /// a target a hundredth the size is seen at a third of the range and not at a hundredth of it.
    /// Scaling linearly would make every small target invisible.
    /// </summary>
    [Fact]
    public void RangeGoesAsTheFourthRootOfCrossSection()
    {
        Assert.Equal(10000.0, RadarSignature.DetectionRange(10000.0, 10.0, 10.0), 6);

        // A sixteenth the cross-section is half the range: 16^(1/4) == 2.
        Assert.Equal(5000.0, RadarSignature.DetectionRange(10000.0, 10.0, 160.0), 6);

        // A hundredth is not a hundredth of the range.
        double hundredth = RadarSignature.DetectionRange(10000.0, 1.0, 100.0);
        Assert.Equal(3162.0, hundredth, 0);
    }

    [Fact]
    public void AnUnusableCrossSectionLeavesTheRangeAlone()
    {
        Assert.Equal(10000.0, RadarSignature.DetectionRange(10000.0, 0.0, 10.0), 6);
        Assert.Equal(10000.0, RadarSignature.DetectionRange(10000.0, 10.0, 0.0), 6);
        Assert.Equal(10000.0, RadarSignature.DetectionRange(10000.0, double.NaN, 10.0), 6);
        Assert.Equal(0.0, RadarSignature.DetectionRange(-1.0, 10.0, 10.0), 6);
    }

    [Fact]
    public void CrossSectionIsTheDiscASphereOfThatRadiusPresents()
    {
        Assert.Equal(Math.PI * 9.0, RadarSignature.CrossSectionFor(3.0), 9);
        Assert.Equal(0.0, RadarSignature.CrossSectionFor(0.0));
        Assert.Equal(0.0, RadarSignature.CrossSectionFor(-2.0));
        Assert.Equal(0.0, RadarSignature.CrossSectionFor(double.NaN));
    }

    /// <summary>
    /// The emergent result worth having: a round is a far smaller target than the aircraft that
    /// threw it, so a set sees it at a fraction of the range with nothing having to know a round
    /// from a craft.
    /// </summary>
    [Fact]
    public void ARoundIsSeenMuchCloserThanTheCraftThatLaunchedIt()
    {
        SensorProfile s = Sensor();
        s.ReferenceCrossSectionM2 = 700f;

        double craft = ThreatModel.DetectionRange(s, Of(15.0));
        double round = ThreatModel.DetectionRange(s, Of(1.0));

        Assert.True(round < craft * 0.35, $"a round was seen at {round:F0} m against a craft's {craft:F0} m");
        Assert.True(round > craft * 0.1, "and not so close as to be undetectable");
    }

    [Fact]
    public void WithNoReferenceTheSetReachesTheSameDistanceWhateverItLooksAt()
    {
        SensorProfile s = Sensor();

        Assert.Equal(s.Range, ThreatModel.DetectionRange(s, Of(1.0)), 6);
        Assert.Equal(s.Range, ThreatModel.DetectionRange(s, Of(500.0)), 6);
        Assert.Equal(s.Range, ThreatModel.DetectionRange(s, ThreatModel.ContactSignature.Unknown), 6);
    }

    [Fact]
    public void ASmallContactDropsOutBeyondItsOwnShorterRange()
    {
        SensorProfile s = Sensor();
        s.ReferenceCrossSectionM2 = 700f;

        double3 at15Km = new(15000, 0, 0);
        double3 closing = new(-300, 0, 0);

        Assert.True(ThreatModel.TryAssess(at15Km, closing, Up, s, Of(15.0), out _));
        Assert.False(ThreatModel.TryAssess(at15Km, closing, Up, s, Of(0.5), out _));
    }

    /// <summary>
    /// The uplink gate has to use the same reach the detection did, or a round goes on being
    /// steered at a contact its own launcher has lost.
    /// </summary>
    [Fact]
    public void TheSensorVolumeShrinksForASmallContactToo()
    {
        SensorProfile s = Sensor();
        s.ReferenceCrossSectionM2 = 700f;

        double3 at15Km = new(15000, 0, 0);

        Assert.True(ThreatModel.InSensorVolume(at15Km, Up, s, Of(15.0)));
        Assert.False(ThreatModel.InSensorVolume(at15Km, Up, s, Of(0.5)));
    }

    // ---- The Doppler notch ----------------------------------------------

    /// <summary>
    /// The cost of a notch, and the reason it ships off. CPA classification exists to engage a
    /// target passing by, and a notch is exactly what loses one.
    /// </summary>
    [Fact]
    public void TheNotchLosesTheCrossingTargetThatCpaClassificationExistsFor()
    {
        SensorProfile s = Sensor();
        double3 r = new(5000, 0, 0);
        double3 abeam = new(0, 400, 0);       // no radial component at all

        Assert.True(ThreatModel.TryAssess(r, abeam, Up, s, Of(5.0), out _));

        s.NotchSpeed = 40f;

        Assert.False(ThreatModel.TryAssess(r, abeam, Up, s, Of(5.0), out _));
    }

    [Fact]
    public void TheNotchKeepsAnythingWithRadialMotion()
    {
        SensorProfile s = Sensor();
        s.NotchSpeed = 40f;

        double3 r = new(5000, 0, 0);

        Assert.True(ThreatModel.TryAssess(r, new double3(-300, 0, 0), Up, s, Of(5.0), out _));
    }

    /// <summary>
    /// Absolute, because a set cannot tell an opening target from a closing one by how much
    /// Doppler it has. Rejecting only closers would leave a notch that catches half the clutter.
    /// </summary>
    [Fact]
    public void TheNotchIsOnTheSizeOfTheRadialSpeedNotItsSign()
    {
        SensorProfile s = Sensor();
        s.NotchSpeed = 40f;

        double3 r = new(5000, 0, 0);

        Assert.True(ThreatModel.TryAssess(r, new double3(300, 0, 0), Up, s, Of(5.0), out var opening));
        Assert.True(opening.ClosingSpeed < 0.0, "this target is opening, so the test is the right way round");
    }

    // ---- The clutter floor ----------------------------------------------

    [Fact]
    public void TheClutterFloorHidesWhatIsDownInTheGroundReturn()
    {
        SensorProfile s = Sensor();
        double3 r = new(5000, 0, 0);
        double3 v = new(-300, 0, 0);

        Assert.True(ThreatModel.TryAssess(r, v, Up, s, Of(5.0, 60.0), out _));

        s.ClutterFloorMetres = 200f;

        Assert.False(ThreatModel.TryAssess(r, v, Up, s, Of(5.0, 60.0), out _));
        Assert.True(ThreatModel.TryAssess(r, v, Up, s, Of(5.0, 400.0), out _));
    }

    /// <summary>
    /// A call site that cannot say how high a contact is must not have it silently swallowed.
    /// </summary>
    [Fact]
    public void AContactOfUnknownHeightIsNeverInTheClutter()
    {
        SensorProfile s = Sensor();
        s.ClutterFloorMetres = 2000f;

        Assert.True(ThreatModel.TryAssess(new double3(5000, 0, 0), new double3(-300, 0, 0), Up, s,
                                          ThreatModel.ContactSignature.Unknown, out _));
    }

    // ---- All three off ---------------------------------------------------

    /// <summary>
    /// The one that matters for everything already flown: with the three at their defaults, a
    /// contact is assessed exactly as it was before any of them existed.
    /// </summary>
    [Fact]
    public void AtTheirDefaultsNoneOfTheThreeChangesAnything()
    {
        SensorProfile s = Sensor();

        Assert.Equal(0f, s.ReferenceCrossSectionM2);
        Assert.Equal(0f, s.NotchSpeed);
        Assert.Equal(0f, s.ClutterFloorMetres);

        foreach (double3 v in new[] { new double3(-300, 0, 0), new double3(0, 400, 0), new double3(300, 0, 0) })
        {
            foreach (var signature in new[] { Of(0.5, 1.0), Of(500.0, 90000.0), ThreatModel.ContactSignature.Unknown })
            {
                Assert.True(ThreatModel.TryAssess(new double3(5000, 0, 0), v, Up, s, signature, out _));
            }
        }
    }
}
