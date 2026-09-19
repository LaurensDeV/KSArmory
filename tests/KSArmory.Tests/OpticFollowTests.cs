using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Why a head is not looking at what its sight is bracketing.
///
/// <para>The ordering is the whole of it. Every one of these states can be true at once — a head
/// can be on the mouse, hand-aimed, designated, and have tracking on — and the reason drawn has to
/// be the rung the head actually stopped at, which is the precedence
/// <c>OpticalHead.AimPartFrame</c> applies. A classifier that reads the same flags in any other
/// order names a rung that was never reached.</para>
/// </summary>
public class OpticFollowTests
{
    [Fact]
    public void OnAxisSaysNothingAtAll()
    {
        // Every reason below is true, and none of them is worth drawing: the head is on it.
        OpticHold hold = OpticFollow.Why(onAxis: true, mouseAim: true, manual: true,
                                         designated: true, tracking: false, settled: false);

        Assert.False(hold.Holds);
        Assert.Equal(OpticHold.None, hold);
    }

    [Fact]
    public void ARestingHeadSaysWhyRatherThanNothing()
    {
        // The state in the screenshot that prompted this: a contact tracked and bracketed, and a
        // head sitting at rest because nothing told it to follow anything.
        OpticHold hold = OpticFollow.Why(onAxis: false, mouseAim: false, manual: false,
                                         designated: false, tracking: false, settled: true);

        Assert.True(hold.Holds);
        Assert.Equal("NOT TRACKING", hold.Reason);
    }

    /// <summary>
    /// The mouse is the first rung AimPartFrame takes: with it on, the operator is the sensor, so a
    /// contact off the axis is one they are choosing not to look at. Naming the tracking switch or a
    /// gimbal stop there sends them after a fault that is not there.
    /// </summary>
    [Fact]
    public void MouseAimOutranksEveryOtherReason()
    {
        Assert.Equal("MOUSE AIM",
                     OpticFollow.Why(onAxis: false, mouseAim: true, manual: true, designated: true,
                                     tracking: true, settled: true).Reason);

        Assert.Equal("MOUSE AIM",
                     OpticFollow.Why(onAxis: false, mouseAim: true, manual: false,
                                     designated: false, tracking: false, settled: true).Reason);
    }

    [Fact]
    public void HandAimingOutranksEverythingBelowIt()
    {
        // Below the mouse, AimPartFrame takes the manual branch before it looks at a designation or
        // the switch, so a hand-aimed head pointing away is aimed by hand -- not "not tracking",
        // whatever the switch says.
        Assert.Equal("AIMED BY HAND",
                     OpticFollow.Why(onAxis: false, mouseAim: false, manual: true, designated: true,
                                     tracking: true, settled: true).Reason);

        Assert.Equal("AIMED BY HAND",
                     OpticFollow.Why(onAxis: false, mouseAim: false, manual: true,
                                     designated: false, tracking: false, settled: true).Reason);
    }

    [Fact]
    public void ADesignationOutranksTheTrackingSwitch()
    {
        // Designating is itself the instruction to watch, so it is consulted ahead of the switch
        // -- and a head watching a designated hillside is not idle, it is watching elsewhere.
        Assert.Equal("WATCHING ELSEWHERE",
                     OpticFollow.Why(onAxis: false, mouseAim: false, manual: false,
                                     designated: true, tracking: false, settled: true).Reason);
    }

    [Fact]
    public void FollowingItAndNotOnItSeparatesMovingFromStuck()
    {
        // Both are "told to follow this contact and not pointing at it". Only the second is a
        // fault, and reporting them alike is what makes a gimbal stop unreadable.
        Assert.Equal("SLEWING",
                     OpticFollow.Why(onAxis: false, mouseAim: false, manual: false,
                                     designated: false, tracking: true, settled: false).Reason);

        Assert.Equal("GIMBAL LIMIT",
                     OpticFollow.Why(onAxis: false, mouseAim: false, manual: false,
                                     designated: false, tracking: true, settled: true).Reason);
    }
}
