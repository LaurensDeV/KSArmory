using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What one press of the trigger is, per gun. A burst runs to its end once started, so a burst sized
/// for a Phalanx turns a single press on a slow gun into a minute of firing.
/// </summary>
public class GunCadenceTests
{
    private const double Frame = 1.0 / 60.0;

    private static int Fire(LauncherProfile gun, double seconds, bool held)
    {
        var channel = new GunChannel();
        channel.Fill(gun.GunAmmo);
        int fired = channel.Step(Frame, true, gun);
        for (double t = Frame; t < seconds; t += Frame) fired += channel.Step(Frame, held, gun);
        return fired;
    }

    [Fact]
    public void OnePressOnTheMk42IsOneShell() => Assert.Equal(1, Fire(Arsenal.Mk42, 90.0, held: false));

    [Fact]
    public void HeldTheMk42FiresAtItsOwnRate()
    {
        // Forty a minute, the first on the press: a gap after each one-round burst would slow it.
        Assert.Equal(40, Fire(Arsenal.Mk42, 60.0 - Frame, held: true));
    }
}
