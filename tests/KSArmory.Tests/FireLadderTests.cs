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
        DesignatedName = null,
    };

    private static SystemConfig Policy() => new();

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
        => FireLadder.Holding(now, policy ?? Policy(), munition ?? Round())?.Reason;

    private static bool BindsTrigger(FireConditions now, SystemConfig? policy = null,
                                     MunitionProfile? munition = null)
        => FireLadder.Holding(now, policy ?? Policy(), munition ?? Round())?.BindsTrigger ?? true;

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

    [Fact]
    public void AReloadingMagazineSaysSo()
    {
        FireConditions now = Ready() with { MagazineEmpty = true, ReloadSeconds = 7.4 };

        Assert.Equal("reloading (7 s)", Hold(now));
    }

    /// <summary>
    /// Nothing stands in front of the trigger on a system nobody has touched. A new player's first
    /// FIRE is the case: a safety they have to find first reads as a weapon that does not work.
    /// </summary>
    [Fact]
    public void AFreshSystemIsClearToFire()
    {
        Assert.Null(FireLadder.Holding(Ready(), new SystemConfig(), Round()));
    }

    /// <summary>
    /// Something to shoot at comes before the drives, because settling is measured against
    /// something to settle onto. With nothing locked the drives are parked and "drives still
    /// settling" is a reason that will never resolve on its own.
    /// </summary>
    [Fact]
    public void AFiringSolutionIsAskedForBeforeTheDrives()
    {
        Assert.Equal("nothing detected",
                     Hold(Ready() with
                     {
                         HasFiringSolution = false, TrackCount = 0, Locked = null, IsLaid = false,
                     }));
    }

    /// <summary>
    /// But a lock that is not yet a solution has the drives on it — a passer-by the operator
    /// shift-clicked, or a threat still inside its lock time — so a launcher still swinging onto it
    /// says so rather than blaming the target.
    /// </summary>
    [Fact]
    public void ALockWithoutASolutionStillWaitsForTheDrives()
    {
        FireConditions now = Ready() with { HasFiringSolution = false, IsLaid = false };

        Assert.Equal("drives still settling", Hold(now));
        Assert.True(BindsTrigger(now));
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

    /// <summary>
    /// A burst goes where the guns are laid, so nothing about the target stops the trigger. Said as
    /// a hold that binds, the line beside a working FIRE reads "Holding fire: no lock" — which sends
    /// an operator looking for a lock the cannon never needed.
    /// </summary>
    [Theory]
    [InlineData(0, false, "nothing detected")]
    [InlineData(2, false, "no firing solution yet (2 track(s))")]
    [InlineData(1, true, "no lock")]
    public void ACannonsTriggerNeedsNothingToShootAt(int tracks, bool solution, string reason)
    {
        FireConditions now = Ready() with
        {
            HasTubes = false,
            HasFiringSolution = solution,
            TrackCount = tracks,
            Locked = null,
        };

        Assert.Equal(reason, Hold(now));
        Assert.False(BindsTrigger(now));
    }

    /// <summary>And a friendly under the guns is auto-engage's refusal, not the trigger's.</summary>
    [Fact]
    public void ACannonsTriggerDoesNotAskTheIff()
    {
        TrackState friend = Engageable();
        friend.Allegiance = Allegiance.Friendly;

        FireConditions now = Ready(friend) with { HasTubes = false };

        Assert.Equal("target is not engageable (IFF)", Hold(now));
        Assert.False(BindsTrigger(now));
    }

    /// <summary>
    /// What the trigger does wait for is the drives, so they come before anything about a target —
    /// otherwise "nothing detected" calls the trigger clear over guns still swinging.
    /// </summary>
    [Fact]
    public void ACannonWaitsForItsDrivesBeforeATarget()
    {
        FireConditions now = Ready() with
        {
            HasTubes = false,
            HasFiringSolution = false,
            TrackCount = 0,
            Locked = null,
            GunsAreLaid = false,
        };

        Assert.Equal("drives still settling", Hold(now));
        Assert.True(BindsTrigger(now));
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
                     Hold(Ready(), new SystemConfig { MissilesEnabled = false }));

        Assert.Equal("cannon are switched off",
                     Hold(Ready() with { HasTubes = false },
                          new SystemConfig { GunsEnabled = false }));
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
    /// A craft the operator shift-clicked is what the trigger shoots, so while the set cannot see it
    /// the line names it. Counting the tracks the set does hold sends the operator looking at those.
    /// </summary>
    [Fact]
    public void ADesignatedCraftTheSetCannotSeeIsNamed()
    {
        FireConditions now = Ready() with
        {
            HasFiringSolution = false,
            TrackCount = 2,
            Locked = null,
            DesignatedName = "Drone 3",
        };

        Assert.Equal("Drone 3 is not on the radar", Hold(now));
        Assert.True(BindsTrigger(now));
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
        Assert.Null(Hold(Ready(), new SystemConfig { AutoEngage = false }));

        // And it does not mask a real reason either.
        Assert.Equal("no lock",
                     Hold(Ready() with { Locked = null },
                          new SystemConfig { AutoEngage = false }));
    }

    // ---- What binds the trigger, and what is only automatic fire's ---------

    /// <summary>
    /// The reported fault. The trigger consults neither the envelope nor the salvo count, so a
    /// panel saying "holding fire" about them describes a refusal that does not happen: the
    /// operator presses FIRE at a target inside the minimum, the round leaves, and it flies.
    /// </summary>
    [Theory]
    [InlineData(50.0)]      // inside the minimum, which is what closing on a target does
    [InlineData(90_000.0)]  // and beyond the maximum, the same gate from the other end
    public void BeingOutOfReachDoesNotBindTheTrigger(double range)
    {
        FireConditions now = Ready() with { Locked = Engageable(range) };

        Assert.Contains("out of reach", Hold(now), StringComparison.Ordinal);
        Assert.False(BindsTrigger(now));
    }

    /// <summary>
    /// A lock that is not a solution — a passer-by locked with a shift-click, or a threat inside its
    /// lock time — is fired at by the trigger, so it does not hold it. Reported as binding, the line
    /// says "Holding fire" beside a FIRE that launches.
    /// </summary>
    [Fact]
    public void ALockWithoutASolutionDoesNotBindTheTrigger()
    {
        FireConditions now = Ready() with { HasFiringSolution = false, TrackCount = 1 };

        Assert.Equal("no firing solution yet (1 track(s))", Hold(now));
        Assert.False(BindsTrigger(now));
    }

    [Fact]
    public void ACommittedSalvoDoesNotBindTheTriggerEither()
    {
        FireConditions now = Ready() with
        {
            Locked = new TrackState
            {
                Range = 5000.0,
                Allegiance = Allegiance.Hostile,
                RoundsAssigned = 99,
            },
        };

        Assert.Equal("salvo committed", Hold(now));
        Assert.False(BindsTrigger(now));
    }

    /// <summary>
    /// Everything above them does bind it: those are about whether the round can leave the rail at
    /// all, and the manual path refuses each of them in turn.
    /// </summary>
    [Fact]
    public void AnythingThatStopsTheRoundLeavingBindsIt()
    {
        Assert.True(BindsTrigger(Ready() with { HasPlatform = false }));
        Assert.True(BindsTrigger(Ready() with { IsOperational = false }));
        Assert.True(BindsTrigger(Ready() with { Ammo = 0 }));
        Assert.True(BindsTrigger(Ready() with { IsLaid = false }));
        Assert.True(BindsTrigger(Ready() with { Locked = null }));
    }

    /// <summary>Nothing holding is not a hold of either kind.</summary>
    [Fact]
    public void ClearToFireBindsNothing()
    {
        Assert.Null(FireLadder.Holding(Ready(), Policy(), Round()));
    }
}
