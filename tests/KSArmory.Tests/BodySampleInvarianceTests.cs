using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What it costs when the round's lookups disagree about where the body is.
///
/// <para>A round is integrated in <c>Ecl</c> about a planet KSA is carrying at ~29.8 km/s, and its
/// force and ground samples are not all taken at the same instant: <c>WeaponSystem</c> reads gravity
/// at the round's <em>pre-step</em> position against a celestial sample from the frame's <em>end</em>,
/// so the pull centre sits <c>v·dt</c> away — 512 m on a 17.2 ms frame.
/// <c>docs/KSA-FRAME-ORDER.md</c> §5 states the offset; what nothing has ever asked is what it is
/// worth, because <c>DeorbitShot</c>'s planet does not move and the term is identically zero in
/// every rig.</para>
///
/// <para><b>These price the term; they do not propose correcting it.</b> Correcting gravity alone
/// has been flown three times and lost, because it moves the centre the round falls toward away from
/// the centre it measures its altitude against. What is worth knowing first is how much the
/// displacement is worth and which part of it — and the answer is that only the radial share
/// matters, by a factor of thirty.</para>
/// </summary>
public class BodySampleInvarianceTests(ITestOutputHelper Out)
{
    /// <summary>Earth's own ecliptic speed, and the frame the flown coast runs at.</summary>
    private const double CarrierMetresPerSecond = 29_800.0;

    private const double FlownFrameSeconds = 0.0172;

    /// <summary>What one frame of the carrier displaces a body sample by: 512 m.</summary>
    private static double Offset => CarrierMetresPerSecond * FlownFrameSeconds;

    private static double Downrange(double3 referenceCci, double3 pointCci, double3 alongCci)
    {
        double metres = DeorbitShot.GroundMetres(referenceCci, pointCci);
        return Vec.Dot(pointCci - referenceCci, alongCci) >= 0.0 ? metres : -metres;
    }

    /// <summary>One flight with the two centres placed where the caller asks.</summary>
    private static (double3 GroundFixed, double Seconds) Fly(double3 gravityCentre, double3 groundCentre)
    {
        BallisticArc.Solution shot = DeorbitShot.Shot(out double3 from, out _);

        return DeorbitShot.FlyTheRound(
            from, shot.RequiredVelocityCci, FlownFrameSeconds, DeorbitShot.Refresh.AsFlown,
            new DeorbitShot.Ball { CentreEcl = groundCentre }, default, gravityCentre);
    }

    /// <summary>How far a displaced sample moves the impact, and how much later it arrives.</summary>
    private static (double Moved, double Seconds) Against(double3 gravityCentre,
                                                          double3 groundCentre = default)
    {
        (double3 honest, double honestSeconds) = Fly(Vec.Zero, Vec.Zero);
        (double3 moved, double seconds) = Fly(gravityCentre, groundCentre);

        double3 up = Vec.Unit(honest);
        double3 along = Vec.RejectFrom(moved - honest, up);

        // Nowhere to measure "downrange" from when nothing moved; the caller asserts on zero.
        if (Vec.Len(along) <= 0.0) return (0.0, seconds - honestSeconds);

        return (Downrange(honest, moved, Vec.Unit(along)), seconds - honestSeconds);
    }

    /// <summary>
    /// The shipped shape: gravity's centre displaced, the ground's not. This is what the game does
    /// every frame, and it is worth hundreds of metres to kilometres depending only on which way the
    /// planet happens to be travelling.
    /// </summary>
    [Theory]
    [InlineData(1.0, 0.0, "radially outward")]
    [InlineData(-1.0, 0.0, "radially inward")]
    [InlineData(0.0, 1.0, "along the track")]
    [InlineData(0.0, -1.0, "against the track")]
    public void GravityAloneOnADisplacedCentreMovesTheImpact(double radial, double along, string what)
    {
        double3 offset = (new double3(radial, along, 0) * Offset);

        (double moved, double seconds) = Against(offset);

        Out.WriteLine($"{what,-20} {moved,8:F0} m downrange, {seconds,+7:F3} s");

        Assert.True(Math.Abs(moved) > 50.0,
                    $"a {Offset:F0} m displacement of the pull centre {what} moved the impact "
                    + $"only {moved:F0} m — the rig is not seeing the term");
    }

    /// <summary>
    /// <b>Only the radial share costs anything</b>, by a factor of thirty.
    ///
    /// <para>That is what makes the flown geometry the whole question rather than the displacement's
    /// size: the body's travel is 29.8 km/s whichever way it points, and what it is worth depends
    /// only on how much of it lies along local up at the arrival. A displacement along the track is
    /// very nearly free, and one straight up or down is worth kilometres.</para>
    ///
    /// <para>It is also why the log line in <c>WarheadTrace</c> resolves the body's travel onto the
    /// arrival's own axes rather than reporting its speed: the speed has never been the unknown.
    /// </para>
    /// </summary>
    [Fact]
    public void OnlyTheRadialShareOfTheDisplacementCosts()
    {
        (double outward, _) = Against(new double3(1, 0, 0) * Offset);
        (double inward, _) = Against(new double3(-1, 0, 0) * Offset);
        (double along, _) = Against(new double3(0, 1, 0) * Offset);

        Out.WriteLine($"{Offset:F0} m of displacement: outward {outward:F0} m, inward {inward:F0} m, "
                      + $"along the track {along:F0} m");

        Assert.True(Math.Abs(along) < Math.Abs(outward) / 10.0,
                    $"along-track cost {along:F0} m against {outward:F0} m radial — the anisotropy "
                    + "this term is diagnosed by has gone");

        // Radial in and out are the same size; the arrival simply moves the other way.
        Assert.InRange(Math.Abs(inward) / Math.Abs(outward), 0.9, 1.1);
    }

    /// <summary>
    /// Linear in the displacement, so one measurement prices every frame rather than each needing
    /// its own flight.
    /// </summary>
    [Fact]
    public void TheCostIsLinearInTheDisplacement()
    {
        double3 unit = new(-1, 0, 0);

        (double one, _) = Against(unit * Offset);
        (double two, _) = Against(unit * (2.0 * Offset));

        Out.WriteLine($"{Offset:F0} m -> {one:F0} m, {2.0 * Offset:F0} m -> {two:F0} m "
                      + $"({two / one:F3}x)");

        Assert.InRange(two / one, 1.8, 2.2);
    }

    /// <summary>
    /// Both lookups are worth kilometres per frame of displacement, which is the argument for
    /// holding them to one centre rather than for correcting either.
    ///
    /// <para><b>The ground figure here is an upper bound, not the shipped term.</b> This displaces
    /// the sphere for the whole flight; the game's staleness is a sawtooth that <em>vanishes exactly
    /// at the last sub-step of every frame</em>, because the round and the celestial sample are both
    /// at the frame's end there — so the crossing can only fire where the test is honest. Modelling
    /// that needs a centre per sub-step, which this rig does not have, and is the next thing to
    /// build if the flown geometry turns out to be radial.</para>
    /// </summary>
    [Fact]
    public void BothLookupsAreWorthKilometresPerFrameOfDisplacement()
    {
        (double gravity, _) = Against(new double3(-1, 0, 0) * Offset);
        (double ground, _) = Against(Vec.Zero, new double3(-1, 0, 0) * Offset);

        Out.WriteLine($"{Offset:F0} m inward: gravity {gravity:F0} m, "
                      + $"ground sphere {ground:F0} m (an upper bound on the shipped sawtooth)");

        Assert.True(Math.Abs(gravity) > 1000.0, $"gravity moved only {gravity:F0} m");
        Assert.True(Math.Abs(ground) > 1000.0, $"the ground sphere moved only {ground:F0} m");
    }

    /// <summary>Nothing displaced is the baseline, and it has to come back to itself.</summary>
    [Fact]
    public void AnUndisplacedSampleCostsNothing()
    {
        (double moved, double seconds) = Against(Vec.Zero);

        Assert.Equal(0.0, moved, 6);
        Assert.Equal(0.0, seconds, 9);
    }
}
