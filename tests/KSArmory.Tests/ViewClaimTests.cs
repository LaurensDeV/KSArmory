using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The main view is the player's. Everything here is about giving it back, because that is the
/// half with a cost: a borrower that keeps it strands the player wherever the last good frame put
/// them, and one that hands it back over the top of a view they have already reclaimed drags them
/// off whatever they chose to look at.
/// </summary>
public class ViewClaimTests
{
    [Fact]
    public void NothingWantedMeansNothingTouched()
    {
        Assert.Equal(ViewAction.Idle,
                     ViewClaim.ForOptic(wanted: false, resolved: true, outranked: false,
                                        holding: false, stillOurs: true));
    }

    [Fact]
    public void WantingItAndBeingAbleToTakesIt()
    {
        Assert.Equal(ViewAction.Take,
                     ViewClaim.ForOptic(wanted: true, resolved: true, outranked: false,
                                        holding: false, stillOurs: true));
    }

    [Fact]
    public void HoldingItAndStillWantingItKeepsDrivingIt()
    {
        Assert.Equal(ViewAction.Hold,
                     ViewClaim.ForOptic(wanted: true, resolved: true, outranked: false,
                                        holding: true, stillOurs: true));
    }

    [Fact]
    public void SwitchingTheOpticOffHandsTheViewBack()
    {
        Assert.Equal(ViewAction.GiveBack,
                     ViewClaim.ForOptic(wanted: false, resolved: true, outranked: false,
                                        holding: true, stillOurs: true));
    }

    /// <summary>
    /// Losing the platform or the optical head mid-flight is the case that would otherwise leave
    /// the camera parked in space: the head cannot say where to look, so there is nothing to write
    /// and nothing would ever release it either.
    /// </summary>
    [Fact]
    public void LosingTheHeadHandsTheViewBackRatherThanFreezingIt()
    {
        Assert.Equal(ViewAction.GiveBack,
                     ViewClaim.ForOptic(wanted: true, resolved: false, outranked: false,
                                        holding: true, stillOurs: true));
    }

    /// <summary>
    /// Yield, not GiveBack. The stronger claim took the view this frame and recorded what it
    /// found — which is this borrower's own pose. Restoring the older recording on top undoes a
    /// takeover that has already happened, and leaves the chase holding a recording of the sight
    /// to hand back when it finishes: the player ends up in a borrowed pose nothing is driving.
    /// </summary>
    [Fact]
    public void AStrongerClaimIsWaitedOutRatherThanRestoredOverTheTopOf()
    {
        Assert.Equal(ViewAction.Yield,
                     ViewClaim.ForOptic(wanted: true, resolved: true, outranked: true,
                                        holding: true, stillOurs: true));
    }

    /// <summary>
    /// The release defers instead of firing. Switching the optic off while a round is being
    /// chased must not restore mid-chase; it waits, and the GiveBack lands the frame after the
    /// chase lets go. Losing the head mid-chase is the same shape.
    /// </summary>
    [Fact]
    public void GivingItBackWaitsForTheStrongerClaimToFinish()
    {
        Assert.Equal(ViewAction.Yield,
                     ViewClaim.ForOptic(wanted: false, resolved: true, outranked: true,
                                        holding: true, stillOurs: true));

        Assert.Equal(ViewAction.Yield,
                     ViewClaim.ForOptic(wanted: true, resolved: false, outranked: true,
                                        holding: true, stillOurs: true));

        // ...and lands the moment it does.
        Assert.Equal(ViewAction.GiveBack,
                     ViewClaim.ForOptic(wanted: false, resolved: true, outranked: false,
                                        holding: true, stillOurs: true));
    }

    /// <summary>
    /// Yielding is not holding on regardless: the player reclaiming the view outranks the
    /// stronger claim too, and both borrowers drop their recordings on the same frame.
    /// </summary>
    [Fact]
    public void ThePlayerOutranksTheStrongerClaimAsWell()
    {
        Assert.Equal(ViewAction.StandDown,
                     ViewClaim.ForOptic(wanted: true, resolved: true, outranked: true,
                                        holding: true, stillOurs: false));
    }

    /// <summary>
    /// Waiting rather than queueing. The chase releases the view on its own, so the next frame's
    /// rung is <see cref="ViewAction.Take"/> with no state carried between them.
    /// </summary>
    [Fact]
    public void ItDoesNotTakeTheViewOutFromUnderAStrongerClaim()
    {
        Assert.Equal(ViewAction.Idle,
                     ViewClaim.ForOptic(wanted: true, resolved: true, outranked: true,
                                        holding: false, stillOurs: true));
    }

    /// <summary>
    /// The rung that has to answer first. With the order reversed this returns
    /// <see cref="ViewAction.GiveBack"/>, and the restore fires against a camera the player is
    /// already flying — the mod yanking the view back one frame after they took it.
    /// </summary>
    [Fact]
    public void ThePlayerTakingTheViewIsADecisionNotAFault()
    {
        Assert.Equal(ViewAction.StandDown,
                     ViewClaim.ForOptic(wanted: false, resolved: true, outranked: false,
                                        holding: true, stillOurs: false));

        // And it outranks every other reason to stop, including still wanting it.
        Assert.Equal(ViewAction.StandDown,
                     ViewClaim.ForOptic(wanted: true, resolved: true, outranked: false,
                                        holding: true, stillOurs: false));
    }

    /// <summary>
    /// Not holding it means <c>stillOurs</c> describes somebody else's camera and must not be
    /// read. Ranking it above <c>holding</c> answers StandDown for a borrower that never took
    /// anything, which then forgets a recording it does not have and never takes the view again.
    /// </summary>
    [Fact]
    public void TheModeOfAViewWeDoNotHoldSaysNothingAboutUs()
    {
        Assert.Equal(ViewAction.Take,
                     ViewClaim.ForOptic(wanted: true, resolved: true, outranked: false,
                                        holding: false, stillOurs: false));
    }

    /// <summary>
    /// Driving a picture and annotating it are the same permission. A sight that yields the view
    /// and keeps painting puts its bracket over a picture of something else, stacked under the
    /// bracket the stronger claim is drawing.
    /// </summary>
    [Fact]
    public void TheSightStopsPaintingWhenItIsNotTheOneDrivingTheView()
    {
        Assert.False(ViewClaim.SightPaints(opticSelected: true, onMainView: true,
                                           holding: true, outranked: true));

        Assert.True(ViewClaim.SightPaints(opticSelected: true, onMainView: true,
                                          holding: true, outranked: false));
    }

    /// <summary>
    /// Standing down releases the view once; it does not stop the setting asking for it. With the
    /// request left on, the frame after a stand-down is a plain Take and the view blinks straight
    /// back — which reads as the mod refusing to let go. The caller has to clear the request, and
    /// this pins why.
    /// </summary>
    [Fact]
    public void StandingDownIsNotEnoughOnItsOwnToGiveTheViewUp()
    {
        // The frame after: the borrower no longer holds it, but is still being asked for it.
        Assert.Equal(ViewAction.Take,
                     ViewClaim.ForOptic(wanted: true, resolved: true, outranked: false,
                                        holding: false, stillOurs: true));

        // Only clearing the request settles it.
        Assert.Equal(ViewAction.Idle,
                     ViewClaim.ForOptic(wanted: false, resolved: true, outranked: false,
                                        holding: false, stillOurs: true));
    }

    /// <summary>Nothing is annotated before the view has actually been taken.</summary>
    [Fact]
    public void TheSightDoesNotPaintOnAMainViewItHasNotTakenYet()
    {
        Assert.False(ViewClaim.SightPaints(opticSelected: true, onMainView: true,
                                           holding: false, outranked: false));
    }

    /// <summary>
    /// A secondary window is driven outright and the chase never touches it, so the overlay there
    /// follows the optic being switched on and nothing else. Gating it on <c>holding</c> — which
    /// only ever describes the main view — would leave that window permanently unmarked.
    /// </summary>
    [Fact]
    public void ASecondaryWindowIsNeverContestedSoItsOverlayAlwaysPaints()
    {
        Assert.True(ViewClaim.SightPaints(opticSelected: true, onMainView: false,
                                          holding: false, outranked: true));

        Assert.False(ViewClaim.SightPaints(opticSelected: false, onMainView: false,
                                           holding: false, outranked: false));
    }
}
