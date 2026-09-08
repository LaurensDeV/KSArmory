using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Whether a round the ground stops can still get to it. The rule that replaces "has it been
/// flying a while" for a store that ends by arriving.
/// </summary>
public class RoundReachTests
{
    // Earth-like, so the numbers below are the ones a B61 actually meets.
    private const double Mu = 3.986e14;
    private const double Surface = 6.371e6;
    private const double Ceiling = Surface + 100_000.0;

    private static double3 At(double altitude) => new(Surface + altitude, 0, 0);

    /// <summary>Circular speed at a radius, which is what a store dropped in orbit inherits.</summary>
    private static double3 Circular(double altitude)
        => new(0, Math.Sqrt(Mu / (Surface + altitude)), 0);

    [Fact]
    public void AStoreDroppedInOrbitNeverArrives()
    {
        // The whole point. It keeps the craft's orbital velocity and flies alongside it, so no
        // amount of waiting brings it down and nothing else would ever reap it.
        Assert.Equal(Approach.Impossible, RoundReach.Classify(Mu, At(400_000), Circular(400_000), Ceiling));
    }

    [Fact]
    public void AStoreOnASuborbitalArcDoes()
    {
        // Released at 250 km with a fraction of circular speed: the arc comes down, and it takes
        // 246 s to do it - twice what the old two-minute self-destruct allowed.
        double3 slow = Circular(250_000) * 0.5;

        Assert.Equal(Approach.Coasting, RoundReach.Classify(Mu, At(250_000), slow, Ceiling));
    }

    /// <summary>
    /// Exactly radial has no conic — the angular momentum is zero and Kepler cannot answer — so it
    /// is not classified and the clock reaps it. That is the honest outcome rather than a guess:
    /// radial covers both a store dropped straight down, which always arrives, and one thrown
    /// straight out on an escape, which never does.
    /// </summary>
    [Fact]
    public void ExactlyRadialIsNotJudged()
    {
        Assert.Equal(Approach.Unknown,
                     RoundReach.Classify(Mu, At(250_000), new double3(-100, 0, 0), Ceiling));
    }

    /// <summary>
    /// Nothing real is exactly radial: a craft in orbit or standing on a turning surface always
    /// carries some transverse speed, and a metre a second of it is enough for the conic to answer.
    /// </summary>
    [Fact]
    public void NearlyRadialIsJudgedNormally()
    {
        Assert.Equal(Approach.Coasting,
                     RoundReach.Classify(Mu, At(250_000), new double3(-100, 1, 0), Ceiling));
    }

    [Fact]
    public void GrazingTheAtmosphereCounts()
    {
        // The boundary is the ceiling and not the surface, because a round that reaches air is
        // arriving: drag from there only lowers it. Just inside it is a hit; just outside is not.
        double3 justInside = Speed(At(400_000), periapsis: Ceiling - 1_000.0);
        double3 justOutside = Speed(At(400_000), periapsis: Ceiling + 1_000.0);

        Assert.Equal(Approach.Coasting, RoundReach.Classify(Mu, At(400_000), justInside, Ceiling));
        Assert.Equal(Approach.Impossible, RoundReach.Classify(Mu, At(400_000), justOutside, Ceiling));
    }

    /// <summary>
    /// The transverse speed at apoapsis that puts periapsis exactly where asked — vis-viva, so the
    /// test states the geometry it means rather than a velocity somebody has to trust.
    /// </summary>
    private static double3 Speed(double3 apoapsis, double periapsis)
    {
        double ra = Vec.Len(apoapsis);
        double a = 0.5 * (ra + periapsis);

        return new double3(0, Math.Sqrt(Mu * ((2.0 / ra) - (1.0 / a))), 0);
    }

    [Theory]
    [InlineData(0.0)]                 // no ceiling could be read
    [InlineData(double.NaN)]
    public void WithNoCeilingItRefusesToJudge(double ceiling)
    {
        // Destroying a round on a maybe is the one outcome with no way back, so an unanswerable
        // question leaves the age limit standing rather than reaping.
        Assert.Equal(Approach.Unknown, RoundReach.Classify(Mu, At(400_000), Circular(400_000), ceiling));
    }

    [Fact]
    public void AnEscapingStoreIsNotJudgedEither()
    {
        // Open trajectories and purely radial ones both come back NaN from Kepler, and they want
        // opposite verdicts - so neither is decided here, and the age limit catches them.
        double3 escaping = Circular(400_000) * 2.0;

        Assert.Equal(Approach.Unknown, RoundReach.Classify(Mu, At(400_000), escaping, Ceiling));
    }

    [Fact]
    public void AlreadyInTheAirIsPastTheQuestion()
    {
        // Below the ceiling the conic no longer describes the flight - there is drag now - and the
        // round is arriving regardless of what a vacuum trajectory would have said.
        Assert.Equal(Approach.Arriving, RoundReach.Classify(Mu, At(20_000), Circular(20_000), Ceiling));
    }

    [Fact]
    public void GarbageIsRefusedRatherThanActedOn()
    {
        var nan = new double3(double.NaN, 0, 0);

        Assert.Equal(Approach.Unknown, RoundReach.Classify(Mu, nan, Circular(400_000), Ceiling));
        Assert.Equal(Approach.Unknown, RoundReach.Classify(Mu, At(400_000), nan, Ceiling));
        Assert.Equal(Approach.Unknown, RoundReach.Classify(0.0, At(400_000), Circular(400_000), Ceiling));
    }
}
