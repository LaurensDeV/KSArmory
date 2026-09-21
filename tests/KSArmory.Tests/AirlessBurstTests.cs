using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What a nuclear burst leaves where there is no air. These pin the two things the drawing cannot
/// be trusted to get right on its own: that the throw is ballistic in the body's own gravity, and
/// that nothing holds a camera on a burst that left nothing behind.
/// </summary>
public class AirlessBurstTests
{
    private const double Kt = 1.0e6;          // kg of TNT equivalent in a kilotonne

    private const double Earth = 9.81;
    private const double Moon = 1.62;

    /// <summary>
    /// A surface burst is one whose fireball reaches the ground, which is the textbook definition
    /// and is also the only thing that could throw any. Nothing else decides it, so a burst high
    /// enough leaves a crater nowhere and a burst at the surface always does.
    /// </summary>
    [Theory]
    [InlineData(0.0, true)]
    [InlineData(20.0, true)]
    [InlineData(1000.0, false)]
    public void OnlyABurstWhoseFireballTouchesTheGroundThrowsAny(double altitude, bool throws)
        => Assert.Equal(throws, AirlessBurst.ThrowsEjecta(0.3 * Kt, altitude));

    /// <summary>A charge too small for a cloud is too small for this too, at any altitude.</summary>
    [Fact]
    public void AConventionalChargeThrowsNothing()
        => Assert.False(AirlessBurst.ThrowsEjecta(MushroomCloud.ThresholdKg - 1.0, 0.0));

    /// <summary>
    /// The arc is the body's, not a constant. Reach is <c>v²/g</c> at 45°, so a device that throws
    /// its dust the same distance on a sixth of the gravity throws it at <c>1/√6</c> of the speed
    /// and keeps it up <c>√6</c> times as long — which is why the flight time is read off the body
    /// rather than typed, and why both ratios are the same square root rather than one being six.
    /// </summary>
    [Fact]
    public void TheThrowIsBallisticInTheBodysOwnGravity()
    {
        AirlessBurst.Ejecta moon = AirlessBurst.EjectaAt(0.3 * Kt, Moon);
        AirlessBurst.Ejecta earth = AirlessBurst.EjectaAt(0.3 * Kt, Earth);

        // The reach is the yield's, so it does not move with gravity at all.
        Assert.Equal(moon.ReachMetres, earth.ReachMetres, 3);

        // Flying it there is what costs less and takes longer.
        Assert.Equal(Math.Sqrt(Earth / Moon), earth.SpeedMetresPerSecond / moon.SpeedMetresPerSecond, 3);
        Assert.Equal(Math.Sqrt(Earth / Moon), moon.FlightSeconds / earth.FlightSeconds, 3);

        // And it lands where it was aimed: the 45-degree range equation, back out of the answer.
        Assert.Equal(moon.ReachMetres,
                     moon.SpeedMetresPerSecond * moon.SpeedMetresPerSecond / Moon, 3);
    }

    /// <summary>The throw scales with the fireball that made it, so one dial moves all of it.</summary>
    [Fact]
    public void TheReachFollowsTheFireball()
    {
        double small = AirlessBurst.EjectaAt(0.3 * Kt, Moon).ReachMetres;
        double large = AirlessBurst.EjectaAt(340.0 * Kt, Moon).ReachMetres;

        Assert.Equal(MushroomCloud.PeakFireballRadius(340.0) / MushroomCloud.PeakFireballRadius(0.3),
                     large / small, 3);
    }

    /// <summary>
    /// Nothing to throw and nowhere to throw it are both answered rather than divided by.
    /// </summary>
    [Theory]
    [InlineData(0.0, Moon)]
    [InlineData(0.3 * Kt, 0.0)]
    public void AThrowWithNothingBehindItIsSpent(double chargeKg, double gravity)
        => Assert.True(AirlessBurst.EjectaAt(chargeKg, gravity).Spent);

    /// <summary>
    /// The camera is held for what is actually happening, which is three different things. Holding
    /// for the rise regardless is what left it on an empty sky over a body that grows no cloud.
    /// </summary>
    [Fact]
    public void TheWatchIsAsLongAsThereIsSomethingToWatch()
    {
        double inAir = AirlessBurst.WatchSeconds(0.3 * Kt, hasAir: true, Earth, 0.0);
        double onTheMoon = AirlessBurst.WatchSeconds(0.3 * Kt, hasAir: false, Moon, 0.0);
        double highOverIt = AirlessBurst.WatchSeconds(0.3 * Kt, hasAir: false, Moon, 1.0e4);

        Assert.Equal(MushroomCloud.RiseSeconds, inAir, 3);
        Assert.Equal(AirlessBurst.EjectaAt(0.3 * Kt, Moon).FlightSeconds, onTheMoon, 3);

        // Nothing was thrown, so what is left is the shell, and it is over in well under a second.
        Assert.Equal(AirlessBurst.ShellSeconds(0.3 * Kt), highOverIt, 3);
        Assert.True(highOverIt < onTheMoon);
    }

    /// <summary>A charge that makes nothing is watched for no time at all.</summary>
    [Fact]
    public void AConventionalChargeIsNotWatched()
        => Assert.Equal(0.0, AirlessBurst.WatchSeconds(MushroomCloud.ThresholdKg - 1.0,
                                                       hasAir: false, Moon, 0.0), 6);

    /// <summary>
    /// And the linger is that, floored. The floor is what a conventional burst gets, so a nuclear
    /// one on an airless body can never be held for less than an ordinary shell.
    /// </summary>
    [Fact]
    public void TheLingerNeverFallsUnderTheFloor()
    {
        // High over an airless body: the shell alone, which is shorter than the floor.
        Assert.True(AirlessBurst.ShellSeconds(0.3 * Kt) < ChaseView.MinLingerSeconds);
        Assert.Equal(ChaseView.MinLingerSeconds,
                     ChaseView.LingerSeconds(0.3 * Kt, hasAir: false, Moon, 1.0e4), 6);

        // And a conventional charge, which made nothing at all.
        Assert.Equal(ChaseView.MinLingerSeconds,
                     ChaseView.LingerSeconds(20.0, hasAir: false, Moon, 0.0), 6);

        // The thrown dust clears it, so an airless surface burst is watched down rather than cut.
        Assert.Equal(AirlessBurst.EjectaAt(0.3 * Kt, Moon).FlightSeconds,
                     ChaseView.LingerSeconds(0.3 * Kt, hasAir: false, Moon, 0.0), 6);
    }

    /// <summary>
    /// The shell is the flash's own duration, so the two cannot drift apart and leave a shell
    /// expanding out of a fireball that has already gone dark.
    /// </summary>
    [Fact]
    public void TheShellLastsAsLongAsTheFlash()
    {
        Assert.Equal(MushroomCloud.FlashSeconds(0.3), AirlessBurst.ShellSeconds(0.3 * Kt), 6);
        Assert.True(AirlessBurst.ShellRadius(0.3 * Kt) > MushroomCloud.PeakFireballRadius(0.3));
    }
}
