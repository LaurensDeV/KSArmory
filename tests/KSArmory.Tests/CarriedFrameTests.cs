using System;
using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Whether a flight survives its planet moving — which is the one thing this rig could not ask.
///
/// <para>KSA carries a planet at ~29.8 km/s and integrates rounds in <c>Ecl</c>, so every quantity a
/// round is differenced against carries that speed and cancels only when the two terms belong to the
/// <em>same instant</em>. Pair them a frame apart and the residue is 500 m, which is why
/// <c>docs/FRAMES-AND-EPOCHS.md</c> exists. With the planet at the origin that residue is
/// identically zero, so a rig without a carrier is not merely poor at finding those faults — it
/// cannot represent one.</para>
///
/// <para><b>The carrier is a pure translation at constant velocity</b>, so it changes no physics: a
/// correctly paired flight has to land in the same body-fixed place with it as without. That
/// invariance is the whole test, and its value is that it does not need to know which pairing is
/// wrong. Any lookup taken at the wrong instant breaks it.</para>
/// </summary>
public class CarriedFrameTests(ITestOutputHelper Out)
{
    private static BallisticBody Earth => DeorbitShot.Earth;

    private static (double3 GroundFixed, double Seconds) Fly(DeorbitShot.Carrier carrier, double dt)
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out double3 from, out _);

        return DeorbitShot.FlyTheRound(from, arc.RequiredVelocityCci, dt, carrier: carrier);
    }

    /// <summary>
    /// The invariance, and it is a bound rather than an equality.
    ///
    /// <para>A whole deorbit flown still and flown on a planet crossing the ecliptic at Earth's own
    /// speed lands within about a kilometre of the same place, against ~14,800 km of carrier travel
    /// over the flight — a residual of about 7 parts in 100,000. <b>That residual has not been run
    /// to ground.</b> The leading candidate is the ground sphere: <see cref="Slug"/> samples it once
    /// before its sub-step loop and holds it, so on a moving planet the surface the round stops
    /// against is up to a frame of carrier stale — a term every rig here has priced at zero because
    /// a still planet cannot have it.</para>
    ///
    /// <para>So the bound is set to catch gross breakage rather than to certify exactness: the same
    /// flight with the air's carrier left off measured 550 km and 99 s. Tightening it wants the
    /// residual understood first, and that is worth its own measurement.</para>
    /// </summary>
    [Theory]
    [InlineData(0.025)]
    [InlineData(0.200)]
    public void TheImpactDoesNotMoveWhenThePlanetDoes(double dt)
    {
        (double3 still, double stillSeconds) = Fly(DeorbitShot.Carrier.Still, dt);
        (double3 carried, double carriedSeconds) = Fly(DeorbitShot.Carrier.Earthlike, dt);

        double moved = DeorbitShot.GroundMetres(still, carried);

        Out.WriteLine($"{dt * 1000:F0} ms frame: the impact moved {moved:F2} m and the flight "
                      + $"{(carriedSeconds - stillSeconds) * 1000:F2} ms, "
                      + $"against a carrier of {DeorbitShot.Carrier.Earthlike.At(stillSeconds) / 1000.0:F0} km "
                      + "of ecliptic travel over the flight");

        double travelled = Vec.Len(DeorbitShot.Carrier.Earthlike.At(stillSeconds));

        Assert.True(moved / travelled < 1e-4,
                    $"the impact moved {moved:F0} m against {travelled / 1000.0:F0} km of carrier, "
                    + $"which is {moved / travelled:E1} of it -- the frame is no longer cancelling");
    }

    /// <summary>
    /// <b>And the test has teeth.</b> The same flight with one lookup deliberately taken at the
    /// frame's end rather than at the round's own instant — which is the shape of every epoch fault
    /// this repository has met — moves the impact by hundreds of metres.
    ///
    /// <para>Expressed as a displaced pull centre rather than by breaking the rig, because that is
    /// the same displacement <c>docs/KSA-FRAME-ORDER.md</c> section 5 describes: the game reads
    /// gravity at the round's pre-step position against a celestial sample from the frame's end.
    /// What is new is that the carrier is what <em>produces</em> the displacement rather than a
    /// number somebody typed.</para>
    /// </summary>
    [Fact]
    public void APairingTakenAtTheWrongInstantMovesTheImpact()
    {
        const double Dt = 0.200;

        BallisticArc.Solution arc = DeorbitShot.Shot(out double3 from, out _);

        // One frame of the carrier: exactly what a lookup pinned to the frame's end rather than
        // back-dated to the round would be reading against.
        double3 aFrameOfCarrier = DeorbitShot.Carrier.Earthlike.MetresPerSecond * Dt;

        (double3 paired, _) = DeorbitShot.FlyTheRound(from, arc.RequiredVelocityCci, Dt,
                                                      carrier: DeorbitShot.Carrier.Earthlike);

        (double3 mispaired, _) = DeorbitShot.FlyTheRound(from, arc.RequiredVelocityCci, Dt,
                                                         gravityCentreCci: aFrameOfCarrier,
                                                         carrier: DeorbitShot.Carrier.Earthlike);

        double moved = DeorbitShot.GroundMetres(paired, mispaired);

        Out.WriteLine($"one frame of carrier is {Vec.Len(aFrameOfCarrier):F0} m of displacement, "
                      + $"and it moves the impact {moved:F0} m");

        Assert.True(moved > 100.0,
                    $"a lookup a whole frame out of step moved the impact only {moved:F0} m, so "
                    + "the invariance above is not measuring what it claims to");
    }

    /// <summary>
    /// The carrier is shared by everything riding the body, so it must not meaningfully change how
    /// long the flight takes — a flight time that moved by seconds would mean the round had been
    /// given energy rather than merely a different origin.
    ///
    /// <para>Bounded, not equal, for the same unresolved reason as above: it lands about 0.4 s out
    /// on a ~400 s flight. Leaving the air's own carrier off moved it 99 s, which is the scale of
    /// breakage this is here to catch.</para>
    /// </summary>
    [Fact]
    public void TheCarrierDoesNotChangeTheFlightTime()
    {
        (_, double still) = Fly(DeorbitShot.Carrier.Still, 0.025);
        (_, double carried) = Fly(DeorbitShot.Carrier.Earthlike, 0.025);

        Out.WriteLine($"still {still:F2} s against carried {carried:F2} s");

        Assert.True(Math.Abs(carried - still) < 0.005 * still,
                    $"the flight took {carried:F2} s carried against {still:F2} s still");
    }
}
