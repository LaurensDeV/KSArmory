using Xunit;

namespace KSArmory.Tests;

/// <summary>The YAL-1's laser as a weapon: its registry entry, its fit, its ladder and its trigger.</summary>
[Collection("catalogue")]
public class BeamWeaponTests
{
    private static readonly LauncherProfile Yal = Catalogue.LauncherForPart(Arsenal.Yal1Turret.PartId)!;

    private static BeamConditions BeamReady() => new()
    {
        HeadResolved = true,
        SecondsLeft = 80.0,
        Masked = false,
        OnTarget = true,
        CloudInTheWay = false,
        ReachMetres = 300_000.0,
    };

    // Everything satisfied for a beam, so one condition put back is the only thing tested. The
    // tube and belt fields say a launcher with neither, as the YAL is.
    private static FireConditions Ready(BeamConditions? beam = null, TrackState? locked = null,
                                        bool solution = true) => new()
    {
        HasPlatform = true,
        IsOperational = true,
        HasTubes = false,
        MagazineEmpty = true,
        ReloadSeconds = 0.0,
        Ammo = 0,
        SalvoSeconds = 0.0,
        BeltEmpty = true,
        HasFiringSolution = solution,
        TrackCount = 1,
        IsLaid = true,
        GunsAreLaid = true,
        RingIsOnGunLead = false,
        RingIsOnCursor = false,
        LaunchAlongTube = true,
        Locked = locked ?? Hostile(),
        LockedIsEmitting = false,
        LockedWithinSeeker = true,
        LockedOffTubeDeg = 0.0,
        LockedName = "target",
        DesignatedName = null,
        Beam = beam ?? BeamReady(),
    };

    private static TrackState Hostile(double range = 50_000.0) => new()
    {
        Range = range,
        Allegiance = Allegiance.Hostile,
        RoundsAssigned = 5,
    };

    private static FireHold? Hold(FireConditions now, SystemConfig? policy = null)
        => FireLadder.Holding(now, policy ?? new SystemConfig(), MunitionProfile.None);

    [Fact]
    public void TheYalIsRegisteredAsALauncherFiringItsOwnDirectorsBeam()
    {
        Assert.NotNull(Yal);
        Assert.True(Yal.FiresBeam);
        Assert.Equal(0, Yal.TubeCount);
        Assert.False(Yal.HasCannon);
        Assert.False(Yal.Trains);
        Assert.NotNull(Catalogue.OpticForPart(Yal.PartId)?.Beam);
    }

    [Fact]
    public void EveryOpticWithABeamIsAlsoAWeapon()
    {
        foreach (OpticProfile optic in Catalogue.Optics)
        {
            if (optic.Beam is null) continue;
            Assert.True(Catalogue.LauncherForPart(optic.PartId) is { FiresBeam: true },
                        $"{optic.DisplayName} carries a beam no weapon fires");
        }
    }

    [Fact]
    public void ASurveyedYalIsAWeaponsSystemWithItsDirector()
    {
        ComponentProfile? yal = null;
        foreach (ComponentProfile c in Catalogue.Components)
        {
            if (c.PartId == Yal.PartId) { yal = c; break; }
        }

        Assert.NotNull(yal);
        Assert.Equal(WeaponRole.Launcher, yal!.Role);
        Assert.Contains(yal.Provides, p => p.Role == WeaponRole.Camera);
    }

    [Fact]
    public void ALauncherNamingNoRoundIsLoadedWithNoRoundRatherThanTheFirstRegistered()
    {
        (MunitionProfile munition, SensorProfile sensor) = Catalogue.LoadoutFor(Yal);

        Assert.Same(MunitionProfile.None, munition);
        Assert.Equal(Arsenal.BeamDirectorSensor.Name, sensor.Name);
    }

    [Fact]
    public void ItsFitIsOneBeamCountedInSecondsThatEngagesOnItsOwnAndDropsNothing()
    {
        WeaponFit fit = WeaponFit.Of(Yal, Catalogue.SensorNamed(Yal.Sensor));

        Armament arm = Assert.Single(fit.Armaments);
        Assert.Equal(ArmamentKind.Beam, arm.Kind);
        Assert.Equal((int)Arsenal.Yal1Turret.Beam!.ChemicalSeconds, arm.Capacity);
        Assert.Equal("73 s/100 s", arm.Tally(73));
        Assert.False(arm.Steers);
        Assert.True(fit.AutoEngages);
        Assert.False(fit.Drops);
        Assert.False(fit.Describes(ArmamentKind.Beam, Arsenal.Missile9J.DisplayName));
    }

    [Fact]
    public void TheBeamHasItsOwnLiveSwitch()
    {
        SystemConfig policy = new();
        Armament.EnabledIn(policy, ArmamentKind.Beam) = false;

        Assert.False(policy.BeamEnabled);
        Assert.True(policy.MissilesEnabled);
        Assert.True(policy.GunsEnabled);
    }

    [Fact]
    public void TheTriggerFallsToWhatALauncherHas()
    {
        Assert.Equal(ArmamentKind.Beam, WeaponFit.TriggerOn(Yal, ArmamentKind.Tubes));
        Assert.Equal(ArmamentKind.Beam, WeaponFit.TriggerOn(Yal, ArmamentKind.Belt));

        LauncherProfile ciws = Catalogue.LauncherForPart("KSArmory_Prefab_Ciws")!;
        Assert.Equal(ArmamentKind.Belt, WeaponFit.TriggerOn(ciws, ArmamentKind.Tubes));
        Assert.Equal(ArmamentKind.Belt, WeaponFit.TriggerOn(ciws, ArmamentKind.Beam));

        LauncherProfile rail = Catalogue.LauncherForPart(Arsenal.HarmRail.PartId)!;
        Assert.Equal(ArmamentKind.Tubes, WeaponFit.TriggerOn(rail, ArmamentKind.Belt));
    }

    [Fact]
    public void EverythingSatisfiedTheBeamIsClear()
    {
        Assert.Null(Hold(Ready()));
    }

    [Fact]
    public void TheBeamsOwnRungsBindTheTriggerInOrder()
    {
        BeamConditions all = BeamReady() with { HeadResolved = false, SecondsLeft = 0, Masked = true, OnTarget = false };
        Assert.Equal("no beam director resolved on this part", Hold(Ready(all))?.Reason);

        all = all with { HeadResolved = true };
        Assert.Equal("laser is switched off", Hold(Ready(all), new SystemConfig { BeamEnabled = false })?.Reason);
        Assert.Equal("out of laser chemicals", Hold(Ready(all))?.Reason);

        all = all with { SecondsLeft = 10 };
        Assert.Equal("own craft in the way (MASK)", Hold(Ready(all))?.Reason);

        all = all with { Masked = false };
        FireHold? slewing = Hold(Ready(all));
        Assert.Equal("director still slewing", slewing?.Reason);
        Assert.True(slewing?.BindsTrigger);
    }

    [Fact]
    public void EverythingAboutTheTargetIsAutoEngagesNotTheTriggers()
    {
        TrackState friend = Hostile();
        friend.Allegiance = Allegiance.Friendly;
        SystemConfig policy = new();

        FireHold? iff = Hold(Ready(locked: friend), policy);
        Assert.Equal("target is not engageable (IFF)", iff?.Reason);
        Assert.False(iff?.BindsTrigger);

        FireHold? cloud = Hold(Ready(BeamReady() with { CloudInTheWay = true }));
        Assert.Equal("cloud between the beam and the target", cloud?.Reason);
        Assert.False(cloud?.BindsTrigger);

        FireHold? far = Hold(Ready(locked: Hostile(400_000.0)));
        Assert.StartsWith("target out of reach", far?.Reason);
        Assert.False(far?.BindsTrigger);

        FireHold? none = Hold(Ready() with { Locked = null });
        Assert.False(none?.BindsTrigger);
    }

    [Fact]
    public void ABeamIsNotRationedPerTargetNorBoundedByTheRoundsEnvelope()
    {
        // Five rounds already "committed" and a round whose reach is zero: neither means anything to
        // a beam, and asking either would hold it for ever.
        Assert.Null(Hold(Ready(locked: Hostile(250_000.0))));
    }

    [Fact]
    public void APressLasesForOneDwellOnlyWhileItMay()
    {
        BeamTrigger trigger = new();
        trigger.Press(5.0);

        // Slewing: the dwell waits rather than being spent on nothing.
        Assert.False(trigger.Step(1.0, autoWants: false, mayLase: false));
        Assert.Equal(5.0, trigger.DwellLeft);

        for (int i = 0; i < 5; i++) Assert.True(trigger.Step(1.0, autoWants: false, mayLase: true));
        Assert.False(trigger.Step(1.0, autoWants: false, mayLase: true));
    }

    [Fact]
    public void AutoEngageLasesForAsLongAsItAsksAndStopEndsAPress()
    {
        BeamTrigger trigger = new();
        for (int i = 0; i < 20; i++) Assert.True(trigger.Step(1.0, autoWants: true, mayLase: true));
        Assert.False(trigger.Step(1.0, autoWants: false, mayLase: true));

        trigger.Press(5.0);
        trigger.Stop();
        Assert.False(trigger.Step(1.0, autoWants: false, mayLase: true));
    }
}
