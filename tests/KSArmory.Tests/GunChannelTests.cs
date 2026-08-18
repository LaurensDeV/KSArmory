using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The cannon's belt and burst timing. All the failure modes here are arithmetic and all of them
/// look like the gun behaving oddly rather than like an error: a burst that never ends, a rate
/// silently capped at the frame rate, a belt that banks credit while it waits.
/// </summary>
public class GunChannelTests
{
    private static LauncherProfile Profile(int burst = 6, float rpm = 2400f, float gap = 0.5f) => new()
    {
        PartId = "Test_Prefab_Gun",
        DisplayName = "test gun",
        Munition = "57E6",
        Sensor = "1RS1",
        Tubes = [new(1, 0, 0)],
        GunMunition = "30MM",
        GunMuzzles = [new(1, 0, 0)],
        GunBurstRounds = burst,
        GunRoundsPerMinute = rpm,
        GunBurstGapSeconds = gap,
    };

    /// <summary>
    /// 2400 rounds/minute is a round every 25 ms, so a 100 ms frame owes four. Firing one and
    /// dropping the rest caps the cannon at the frame rate — which reads as a feeble gun, not as
    /// a bug, and would change with the player's hardware.
    /// </summary>
    [Fact]
    public void AStepLongerThanTheIntervalFiresEveryRoundItOwes()
    {
        LauncherProfile profile = Profile(burst: 12);
        var gun = new GunChannel();
        gun.Fill(100);

        Assert.Equal(4, gun.Step(0.100, wantToFire: true, profile));
    }

    [Fact]
    public void ABurstNeverExceedsItsLength()
    {
        LauncherProfile profile = Profile(burst: 6);
        var gun = new GunChannel();
        gun.Fill(100);

        Assert.Equal(6, gun.Step(10.0, wantToFire: true, profile));
        Assert.False(gun.Firing);
    }

    [Fact]
    public void WaitingBanksNoCredit()
    {
        LauncherProfile profile = Profile(burst: 12);
        var gun = new GunChannel();
        gun.Fill(100);

        // A minute of holding fire must not become a minute's worth of rounds in one frame.
        for (int i = 0; i < 600; i++) Assert.Equal(0, gun.Step(0.1, wantToFire: false, profile));

        Assert.Equal(1, gun.Step(0.02, wantToFire: true, profile));
    }

    [Fact]
    public void ABurstAlreadyStartedRunsToItsEnd()
    {
        LauncherProfile profile = Profile(burst: 6);
        var gun = new GunChannel();
        gun.Fill(100);

        int fired = gun.Step(0.01, wantToFire: true, profile);
        Assert.True(gun.Firing);

        // The track flickers; the burst carries on rather than stuttering.
        while (gun.Firing) fired += gun.Step(0.05, wantToFire: false, profile);

        Assert.Equal(6, fired);
    }

    [Fact]
    public void TheGunPausesBetweenBursts()
    {
        LauncherProfile profile = Profile(burst: 4, gap: 0.5f);
        var gun = new GunChannel();
        gun.Fill(100);

        Assert.Equal(4, gun.Step(1.0, wantToFire: true, profile));
        Assert.Equal(0, gun.Step(0.2, wantToFire: true, profile));
        Assert.Equal(0, gun.Step(0.2, wantToFire: true, profile));
        Assert.True(gun.Step(0.3, wantToFire: true, profile) > 0);
    }

    [Fact]
    public void TheBeltRunsOutRatherThanGoingNegative()
    {
        LauncherProfile profile = Profile(burst: 20);
        var gun = new GunChannel();
        gun.Fill(5);

        Assert.Equal(5, gun.Step(10.0, wantToFire: true, profile));
        Assert.True(gun.IsEmpty);
        Assert.Equal(0, gun.Ammo);
        Assert.Equal(0, gun.Step(10.0, wantToFire: true, profile));
    }

    [Fact]
    public void ANonPositiveStepFiresNothing()
    {
        LauncherProfile profile = Profile();
        var gun = new GunChannel();
        gun.Fill(100);

        Assert.Equal(0, gun.Step(0.0, wantToFire: true, profile));
        Assert.Equal(0, gun.Step(-1.0, wantToFire: true, profile));
        Assert.Equal(0, gun.Step(double.NaN, wantToFire: true, profile));
        Assert.Equal(100, gun.Ammo);
    }

    [Fact]
    public void ALauncherWithNoCannonDeclaresNone()
    {
        var noGuns = new LauncherProfile
        {
            PartId = "Test_Prefab_Rail",
            DisplayName = "test rail",
            Munition = "57E6",
            Sensor = "1RS1",
            Tubes = [new(1, 0, 0)],
        };

        Assert.False(noGuns.HasCannon);
        Assert.True(Profile().HasCannon);
    }

    [Fact]
    public void TheCannonRoundIsRegisteredAndUnguided()
    {
        MunitionProfile shell = Catalogue.MunitionNamed("30MM");

        Assert.Equal(0f, shell.BoostSeconds);
        Assert.True(shell.LaunchSpeed > 500f, "a shell leaves at muzzle velocity, not a launch nudge");
    }

    /// <summary>
    /// The cannon exist to cover what the missiles cannot. A gap between the gun's reach and the
    /// missile's minimum leaves a band nothing can be engaged in, which is invisible until
    /// something flies through it.
    /// </summary>
    [Fact]
    public void TheCannonEnvelopeOverlapsTheMissileMinimum()
    {
        LauncherProfile pantsir = Arsenal.PantsirS1;
        MunitionProfile missile = Catalogue.MunitionNamed(pantsir.Munition);
        MunitionProfile shell = Catalogue.MunitionNamed(pantsir.GunMunition!);

        Assert.True(pantsir.HasCannon);

        // Each round carries its own reach, so the two envelopes are directly comparable.
        Assert.True(shell.MaxRange >= missile.MinRange,
                    $"cannon reach {shell.MaxRange} m leaves a hole below the missile's "
                    + $"{missile.MinRange} m minimum");
    }

    /// <summary>
    /// A burst cannot outlive the belt.
    ///
    /// <para>The firing loop stops on ammunition, so a burst interrupted by the last round leaves
    /// <c>BurstRemaining</c> standing with nothing left to fire it. <c>Firing</c> then stays true
    /// for the rest of the session, and everything that asks the gun whether it is firing stays on
    /// with it: the muzzle flash is never handed back and sits on the mount as a permanent
    /// fireball.</para>
    /// </summary>
    [Fact]
    public void ABurstInterruptedByAnEmptyBeltStopsFiring()
    {
        LauncherProfile profile = Profile(burst: 60, rpm: 4500f);
        var gun = new GunChannel();
        gun.Fill(3);

        // Long enough to owe more rounds than the belt holds, which is the whole case.
        Assert.Equal(3, gun.Step(0.05, wantToFire: true, profile));

        Assert.True(gun.IsEmpty);
        Assert.Equal(0, gun.BurstRemaining);
        Assert.False(gun.Firing, "the gun reports firing with an empty belt and no burst to run");

        // And it stays stopped rather than reporting a burst it can never deliver.
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(0, gun.Step(0.05, wantToFire: true, profile));
            Assert.False(gun.Firing);
        }
    }

    /// <summary>
    /// Refilling after that leaves the gun able to fire again, rather than resuming the burst the
    /// empty belt cut short.
    /// </summary>
    [Fact]
    public void ARefilledBeltStartsAFreshBurstRatherThanResumingTheOldOne()
    {
        LauncherProfile profile = Profile(burst: 60, rpm: 4500f);
        var gun = new GunChannel();
        gun.Fill(3);
        gun.Step(0.05, wantToFire: true, profile);

        gun.Fill(120);
        gun.Reset();

        Assert.False(gun.Firing);
        Assert.True(gun.Step(0.05, wantToFire: true, profile) > 0, "a refilled gun did not fire");
        Assert.True(gun.BurstRemaining is > 0 and < 60, "the burst did not restart from full");
    }
}
