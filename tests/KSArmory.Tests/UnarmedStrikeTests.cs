using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A shell runs into what is in its way whether or not its fuze has armed; only the proximity fuse's
/// reach waits. The 5"/54 arms 363 m out, and a tank stack 180 m from the mount was flown straight
/// through while the ground behind it stopped every shell.
/// </summary>
public class UnarmedStrikeTests
{
    private const double Frame = 1.0 / 60.0;

    private static MunitionProfile Shell => Arsenal.Shell5In54;

    private static Slug Fire(TargetState body)
    {
        var slug = new Slug(Vec.Zero, new double3(Shell.LaunchSpeed, 0, 0), null, -1, Vec.Zero, Vec.Zero)
        {
            Munition = Shell,
            Contacts = [body],
        };

        for (int i = 0; i < 40 && slug.State == RoundState.Flying; i++)
        {
            slug.Update(Frame, null, Vec.Zero, Vec.Zero, Vec.Zero, Shell, 0.0);
        }

        return slug;
    }

    [Fact]
    public void AShellStrikesACraftItReachesBeforeItsFuzeArms()
    {
        Assert.True(180.0 / Shell.LaunchSpeed < Shell.FuseArmSeconds,
                    "the craft should be inside the arming distance, or this proves nothing");

        object tanks = new();
        Slug slug = Fire(new TargetState(new double3(180, 0, 0), Vec.Zero, 3.0, tanks));

        Assert.Equal(RoundState.Detonated, slug.State);
        Assert.Same(tanks, slug.StruckBody);
        Assert.InRange(slug.PositionEcl.X, 170.0, 180.0);
    }

    [Fact]
    public void BeforeItArmsItsProximityFuseStillReachesNothing()
    {
        // 8 m off a 3 m body: well inside the 12 m fuse radius, and clear of the body itself.
        Slug slug = Fire(new TargetState(new double3(180, 8, 0), Vec.Zero, 3.0, new object()));

        Assert.Equal(RoundState.Flying, slug.State);
        Assert.Null(slug.StruckBody);
    }
}
