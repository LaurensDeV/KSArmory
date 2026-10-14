using Xunit;

namespace KSArmory.Tests;

public class ContactHoldTests
{
    // Contact as flown on a rocket sitting on a slope: four flips a second.
    [Fact]
    public void ABouncingCraftStaysResting()
    {
        ContactHold hold = new();
        for (int frame = 0; frame < 600; frame++)
        {
            bool touching = (frame / 7) % 2 == 0;
            Assert.True(hold.Update(touching, 1.0 / 60.0) || frame == 0, $"frame {frame} read as flying");
        }
    }

    [Fact]
    public void ACraftThatLeavesTheGroundFliesOnceTheHoldRunsOut()
    {
        ContactHold hold = new();
        hold.Update(true, 1.0 / 60.0);
        int frames = 0;
        while (hold.Update(false, 1.0 / 60.0)) frames++;
        Assert.InRange(frames / 60.0, ContactHold.HoldSeconds - 0.05, ContactHold.HoldSeconds + 0.05);
    }

    [Fact]
    public void NeverTouchingIsNeverResting() => Assert.False(new ContactHold().Update(false, 1.0 / 60.0));
}
