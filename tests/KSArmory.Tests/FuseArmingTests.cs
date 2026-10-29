using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A round that meets something before its fuze arms is a dud: it stops there and its warhead never
/// fires. The arming delay is what keeps a launcher out of its own round's blast.
/// </summary>
public class FuseArmingTests
{
    private const double Dt = 1.0 / 60.0;
    private const double PlanetRadius = 6_000_000.0;

    private sealed class Struck : IHullTest
    {
        public HullVerdict Judge(object? body, double3 separation, double3 travel, out double fraction)
        {
            fraction = 0.5;
            return HullVerdict.Struck;
        }
    }

    private sealed class Ball : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Vec.Zero;
            surfaceRadius = PlanetRadius;
            return true;
        }
    }

    private static MunitionProfile Shell(float armSeconds)
    {
        MunitionProfile shell = BuiltIns.Cannon20Mm.Copy();
        shell.FuseArmSeconds = armSeconds;
        return shell;
    }

    private static MunitionProfile Store() => new()
    {
        Name = "store",
        DisplayName = "store",
        DragK = 0f,
        Guidance = GuidanceMode.None,
        // A profile built in code keeps the 57E6's motor; the reader zeroes it for an unguided round.
        BoostSeconds = 0f,
        BoostAccel = 0f,
        FuseRadius = 0f,
        FuseArmSeconds = 4f,
        MaxFlightSeconds = 120f,
        HitsTerrain = true,
    };

    // A hull 300 m downrange of a 1,100 m/s shell is reached 0.27 s out.
    private static Slug FireAtHull(MunitionProfile munition, object craft)
    {
        Slug shell = new(Vec.Zero, new double3(1100, 0, 0), null, -1, Vec.Zero, Vec.Zero)
        {
            Munition = munition,
            Hull = new Struck(),
            Contacts = [new TargetState(new double3(300, 0, 0), Vec.Zero, 25.0, craft)],
        };

        for (int i = 0; i < 200 && shell.State == RoundState.Flying; i++)
        {
            shell.Update(Dt, null, Vec.Zero, Vec.Zero, Vec.Zero, munition);
        }

        return shell;
    }

    private static Slug DropOnTheGround(MunitionProfile munition, double altitude, double3 velocity)
    {
        Slug store = new(new double3(0, 0, PlanetRadius + altitude), velocity, null, -1, Vec.Zero, Vec.Zero)
        {
            Munition = munition,
            Ground = new Ball(),
        };

        for (int i = 0; i < 3000 && store.State == RoundState.Flying; i++)
        {
            store.Update(Dt, null, new double3(0, 0, -9.81), Vec.Zero, Vec.Zero, munition);
        }

        return store;
    }

    [Fact]
    public void AShellThatMeetsAHullBeforeItArmsIsADudAndSaysWhatItStruck()
    {
        object craft = new();
        Slug shell = FireAtHull(Shell(armSeconds: 1.0f), craft);

        Assert.Equal(RoundState.Dud, shell.State);
        Assert.Same(craft, shell.StruckBody);
        Assert.True(shell.Age < 1.0);
    }

    [Fact]
    public void AShellThatMeetsAHullOnceArmedDetonates()
    {
        Slug shell = FireAtHull(Shell(armSeconds: 0.1f), new object());

        Assert.Equal(RoundState.Detonated, shell.State);
    }

    [Fact]
    public void AStoreThatLandsBeforeItArmsIsADud()
    {
        MunitionProfile store = Store();

        // 30 m up at 100 m/s down is under half a second of fall.
        Slug landed = DropOnTheGround(store, 30.0, new double3(50, 0, -100));

        Assert.True(landed.HitGround);
        Assert.Equal(RoundState.Dud, landed.State);
    }

    [Fact]
    public void AStoreThatLandsOnceArmedDetonates()
    {
        MunitionProfile store = Store();

        // 2 km up at 100 m/s down is about 11 s of fall.
        Slug landed = DropOnTheGround(store, 2000.0, new double3(50, 0, -100));

        Assert.True(landed.HitGround);
        Assert.Equal(RoundState.Detonated, landed.State);
    }

    [Fact]
    public void AMissileThatHitsTheGroundBeforeItArmsIsADud()
    {
        MunitionProfile munition = new()
        {
            Name = "missile",
            DisplayName = "missile",
            DragK = 0f,
            Guidance = GuidanceMode.Seeker,
            FuseRadius = 16f,
            FuseArmSeconds = 0.5f,
            MaxFlightSeconds = 30f,
        };
        var round = new Interceptor(new double3(0, 0, PlanetRadius + 30.0), new double3(250, 0, -150), new object(),
                                    tube: 0, platformEcl: default, frameVelocityEcl: default)
        {
            Munition = munition,
            Ground = new Ball(),
        };

        for (int i = 0; i < 600 && round.State == RoundState.Flying; i++)
        {
            round.Update(Dt, null, Vec.Zero, frameVelocityEcl: default, platformEcl: default, munition);
        }

        Assert.True(round.HitGround);
        Assert.Equal(RoundState.Dud, round.State);
    }
}
