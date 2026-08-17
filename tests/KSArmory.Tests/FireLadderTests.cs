using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The reason a system gives for not shooting.
///
/// <para>It is the mod's most-read line and had no test at all while it lived in <c>Ksa/</c>. What
/// matters is not that each rung works but that they are asked <b>in order</b>: the ladder's whole
/// job is to name the <em>first</em> gate that says no, so a rung that overtakes the one above it
/// reports a true statement that is not the answer — and the operator goes looking in the wrong
/// place.</para>
/// </summary>
public class FireLadderTests
{
    // Everything satisfied, so any single condition put back is the only thing being tested.
    private static FireConditions Ready(TrackState? locked = null) => new()
    {
        HasPlatform = true,
        IsOperational = true,
        HasTubes = true,
        MagazineEmpty = false,
        ReloadSeconds = 0.0,
        Ammo = 4,
        SalvoSeconds = 0.0,
        BeltEmpty = false,
        HasFiringSolution = true,
        TrackCount = 1,
        IsLaid = true,
        GunsAreLaid = true,
        RingIsOnGunLead = false,
        RingIsOnCursor = false,
        LaunchAlongTube = true,
        Locked = locked ?? Engageable(),
        LockedIsEmitting = true,
        LockedName = "target",
    };

    private static SystemConfig Armed() => new() { Armed = true };

    private static MunitionProfile Round() => new()
    {
        Name = "test",
        DisplayName = "test",
        Guidance = GuidanceMode.Seeker,
        MinRange = 1000f,
        MaxRange = 20000f,
    };

    private static TrackState Engageable(double range = 5000.0) => new()
    {
        Range = range,
        Allegiance = Allegiance.Hostile,
        RoundsAssigned = 0,
    };

    private static string? Hold(FireConditions now, SystemConfig? policy = null,
                                MunitionProfile? munition = null)
        => FireLadder.Holding(now, policy ?? Armed(), munition ?? Round());

    [Fact]
    public void EverythingSatisfiedIsNotHoldingFire()
    {
        Assert.Null(Hold(Ready()));
    }

    // ---- The order, which is the whole contract ---------------------------

    /// <summary>
    /// No platform outranks everything, including having no launcher. Both are true of a system
    /// that has come unmounted, and the launcher answer sends whoever reads it looking at a craft
    /// the system is no longer on.
    /// </summary>
    [Fact]
    public void NoPlatformOutranksNoLauncher()
    {
        Assert.Equal("no platform",
                     Hold(Ready() with { HasPlatform = false, IsOperational = false }));
    }

    /// <summary>
    /// Reloading outranks the master arm, so a magazine refilling while the system is safed still
    /// says so. Both are true; only one of them ends.
    /// </summary>
    [Fact]
    public void ReloadingOutranksTheMasterArm()
    {
        FireConditions now = Ready() with { MagazineEmpty = true, ReloadSeconds = 7.4 };

        Assert.Equal("reloading (7 s)", Hold(now, new SystemConfig { Armed = false }));
    }

    /// <summary>
    /// The master arm outranks every rung below it. It is the switch an operator is most likely to
    /// have left off, and reporting "no lock" while safed sends them to the radar.
    /// </summary>
    [Fact]
    public void TheMasterArmOutranksEverythingBelowIt()
    {
        FireConditions now = Ready() with
        {
            Ammo = 0,
            HasFiringSolution = false,
            TrackCount = 0,
            IsLaid = false,
            Locked = null,
        };

        Assert.Equal("safe -- master arm is off", Hold(now, new SystemConfig { Armed = false }));
    }

    /// <summary>
    /// A firing solution comes before the drives, because settling is measured against something to
    /// settle onto. With no solution the drives are parked and "drives still settling" is a reason
    /// that will never resolve on its own.
    /// </summary>
    [Fact]
    public void AFiringSolutionIsAskedForBeforeTheDrives()
    {
        Assert.Equal("nothing detected",
                     Hold(Ready() with { HasFiringSolution = false, TrackCount = 0, IsLaid = false }));
    }

    [Fact]
    public void TracksWithoutASolutionAreCounted()
    {
        Assert.Equal("no firing solution yet (3 track(s))",
                     Hold(Ready() with { HasFiringSolution = false, TrackCount = 3 }));
    }

    // ---- Which ladder a launcher is on ------------------------------------

    /// <summary>
    /// A gun-only launcher takes the belt's rungs. Its magazine is empty by construction, so
    /// running it down the missile rungs reports "out of rounds" forever while the cannon are
    /// audibly firing — which is the defect the two branches exist to prevent.
    /// </summary>
    [Fact]
    public void AGunOnlyLauncherIsNeverOutOfRounds()
    {
        FireConditions now = Ready() with { HasTubes = false, Ammo = 0, MagazineEmpty = true };

        Assert.Null(Hold(now));
    }

    [Fact]
    public void AGunOnlyLauncherReportsItsBelt()
    {
        Assert.Equal("belt empty", Hold(Ready() with { HasTubes = false, BeltEmpty = true }));
    }

    /// <summary>Each weapon settles on its own gear, so neither may be asked about the other's.</summary>
    [Fact]
    public void EachWeaponSettlesOnItsOwnDrives()
    {
        Assert.Null(Hold(Ready() with { HasTubes = true, GunsAreLaid = false }));
        Assert.Null(Hold(Ready() with { HasTubes = false, IsLaid = false }));

        Assert.Equal("drives still settling", Hold(Ready() with { HasTubes = true, IsLaid = false }));
        Assert.Equal("drives still settling",
                     Hold(Ready() with { HasTubes = false, GunsAreLaid = false }));
    }

    /// <summary>
    /// A switched-off armament is reported as its own kind. One message for both reads as the
    /// wrong weapon being off on a system carrying two.
    /// </summary>
    [Fact]
    public void EachArmamentNamesItsOwnSwitch()
    {
        Assert.Equal("missiles are switched off",
                     Hold(Ready(), new SystemConfig { Armed = true, MissilesEnabled = false }));

        Assert.Equal("cannon are switched off",
                     Hold(Ready() with { HasTubes = false },
                          new SystemConfig { Armed = true, GunsEnabled = false }));
    }

    // ---- Who owns the bearing ---------------------------------------------

    /// <summary>
    /// Only one weapon can own the bearing, and rounds leave along the tube — so a missile released
    /// while the turret is laid on the cannon's ballistic lead departs well off the target.
    /// </summary>
    [Fact]
    public void MissilesHoldWhileTheCannonHasTheBearing()
    {
        Assert.Equal("the cannon has the bearing", Hold(Ready() with { RingIsOnGunLead = true }));
        Assert.Equal("the cursor has the bearing", Hold(Ready() with { RingIsOnCursor = true }));
    }

    /// <summary>
    /// And neither holds a launcher whose rounds do not leave along the tube: where the ring points
    /// says nothing about where a round off that rail will go.
    /// </summary>
    [Fact]
    public void ALauncherThatDoesNotFireAlongItsTubeIgnoresTheRing()
    {
        FireConditions now = Ready() with
        {
            RingIsOnGunLead = true,
            RingIsOnCursor = true,
            LaunchAlongTube = false,
        };

        Assert.Null(Hold(now));
    }

    // ---- The target ------------------------------------------------------

    [Fact]
    public void NoLockIsReportedAfterTheDrivesAreSettled()
    {
        Assert.Equal("no lock", Hold(Ready() with { Locked = null }));
    }

    /// <summary>
    /// An anti-radiation round is gated on launching at something that transmits, not only on
    /// homing. Emission is read in flight and nowhere before it, so without this rung the weapon
    /// locks a silent contact, fires, and the round flies straight past everything.
    /// </summary>
    [Fact]
    public void AnAntiRadiationRoundWillNotLaunchAtSomethingSilent()
    {
        MunitionProfile harm = Round();
        harm.Guidance = GuidanceMode.AntiRadiation;

        Assert.Equal("Site 4 is not radiating",
                     Hold(Ready() with { LockedIsEmitting = false, LockedName = "Site 4" },
                          munition: harm));

        Assert.Null(Hold(Ready() with { LockedIsEmitting = true }, munition: harm));
    }

    /// <summary>And no other guidance mode reads emission at all.</summary>
    [Fact]
    public void EveryOtherRoundIgnoresWhetherTheTargetTransmits()
    {
        Assert.Null(Hold(Ready() with { LockedIsEmitting = false }));
    }

    [Fact]
    public void AFriendlyIsNotEngageable()
    {
        TrackState friend = Engageable();
        friend.Allegiance = Allegiance.Friendly;

        Assert.Equal("target is not engageable (IFF)", Hold(Ready(friend)));
    }

    /// <summary>
    /// Out of reach says which way, with both numbers. "Out of reach" alone reads as too far, and
    /// the usual cause is a target that has come inside the minimum instead.
    /// </summary>
    [Fact]
    public void OutOfReachCarriesTheRangeAndTheEnvelope()
    {
        Assert.Equal("target out of reach (0.4 km, envelope 1.0-20.0 km)",
                     Hold(Ready(Engageable(400.0))));

        Assert.Equal("target out of reach (31.0 km, envelope 1.0-20.0 km)",
                     Hold(Ready(Engageable(31000.0))));
    }

    /// <summary>
    /// Auto-engage is deliberately not a rung. It decides whether fire control shoots on its own,
    /// not whether a round can leave the rail, and no manual fire path consults it — so reporting
    /// it stops the ladder at the one switch that blocks nothing the operator asked for, hiding
    /// every gate below it from the panel beside the trigger.
    /// </summary>
    [Fact]
    public void AutoEngageIsNotAGate()
    {
        Assert.Null(Hold(Ready(), new SystemConfig { Armed = true, AutoEngage = false }));

        // And it does not mask a real reason either.
        Assert.Equal("no lock",
                     Hold(Ready() with { Locked = null },
                          new SystemConfig { Armed = true, AutoEngage = false }));
    }
}
