using Brutal.Numerics;
using Xunit;

namespace AirDefence.Tests;

/// <summary>
/// Tube bookkeeping. Two invariants matter most: a reload must not hand out a tube whose previous
/// round is still in the air, and a spent tube's body must still be seated before it is hidden.
/// </summary>
public class MagazineTests
{
    private static readonly object TargetHandle = new();

    /// <summary>A round occupying <paramref name="tube"/>, numbered from one as the battery does.</summary>
    private static Interceptor RoundInTube(int tube) =>
        new(new double3(0, 0, 0), new double3(100, 0, 0), TargetHandle, tube,
            platformEcl: default, frameVelocityEcl: default);

    private static Magazine Full(int tubes)
    {
        var magazine = new Magazine();
        magazine.Resize(tubes);
        return magazine;
    }

    // ---- Counting ------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(12)]
    public void AFreshMagazineIsFull(int tubes)
    {
        // Parameterised: a launcher with a different tube count is the shape a second weapon
        // system takes.
        Magazine magazine = Full(tubes);

        Assert.Equal(tubes, magazine.Capacity);
        Assert.Equal(tubes, magazine.Ammo);
        Assert.Equal(0, magazine.SpentCount);
        Assert.False(magazine.IsEmpty);
    }

    [Fact]
    public void TubesAreHandedOutInOrderAndTheCountFollows()
    {
        Magazine magazine = Full(4);
        var empty = new List<Interceptor>();

        for (int expected = 0; expected < 4; expected++)
        {
            Assert.True(magazine.TryTakeTube(empty, out int tube));
            Assert.Equal(expected, tube);
            Assert.Equal(3 - expected, magazine.Ammo);
            Assert.Equal(expected + 1, magazine.SpentCount);
        }

        Assert.True(magazine.IsEmpty);
        Assert.False(magazine.TryTakeTube(empty, out _));
    }

    /// <summary>Ammo is counted from the tube flags, so the two cannot disagree.</summary>
    [Fact]
    public void AmmoAlwaysAgreesWithTheTubesThatAreLoaded()
    {
        Magazine magazine = Full(6);
        var empty = new List<Interceptor>();

        for (int fired = 0; fired <= 6; fired++)
        {
            int loaded = 0;
            for (int i = 0; i < magazine.Capacity; i++) if (magazine.IsLoaded(i)) loaded++;

            Assert.Equal(loaded, magazine.Ammo);
            Assert.Equal(magazine.Capacity - loaded, magazine.SpentCount);

            magazine.TryTakeTube(empty, out _);
        }
    }

    // ---- The shipped bug -----------------------------------------------

    /// <summary>
    /// A reload refills every tube, including ones whose round has not landed. Handing one out
    /// again would give two rounds the same body subpart, which is written once per round per
    /// frame and would flip between their positions.
    /// </summary>
    [Fact]
    public void AReloadDoesNotHandOutATubeWhoseRoundIsStillFlying()
    {
        Magazine magazine = Full(4);
        var empty = new List<Interceptor>();

        Assert.True(magazine.TryTakeTube(empty, out int first));
        Assert.True(magazine.TryTakeTube(empty, out int second));

        // Both rounds are still in the air when the launcher reloads.
        var inFlight = new List<Interceptor> { RoundInTube(first + 1), RoundInTube(second + 1) };
        magazine.RefillAll();
        Assert.Equal(4, magazine.Ammo);

        // The next four shots must avoid the two occupied tubes entirely.
        var handedOut = new List<int>();
        while (magazine.TryTakeTube(inFlight, out int tube)) handedOut.Add(tube);

        Assert.DoesNotContain(first, handedOut);
        Assert.DoesNotContain(second, handedOut);
        Assert.Equal(handedOut.Count, handedOut.Distinct().Count());
    }

    [Fact]
    public void ATubeIsFreeAgainOnceItsRoundIsGone()
    {
        Magazine magazine = Full(2);
        var empty = new List<Interceptor>();

        Assert.True(magazine.TryTakeTube(empty, out int tube));

        var inFlight = new List<Interceptor> { RoundInTube(tube + 1) };
        magazine.RefillAll();

        // Occupied: the reloaded tube 0 is skipped and tube 1 is offered instead.
        Assert.True(magazine.TryTakeTube(inFlight, out int other));
        Assert.NotEqual(tube, other);

        // Round lands, tube frees up.
        magazine.RefillAll();
        Assert.True(magazine.TryTakeTube(empty, out int reused));
        Assert.Equal(tube, reused);
    }

    [Fact]
    public void EveryTubeBeingOccupiedRefusesTheShotRatherThanDoubleBooking()
    {
        Magazine magazine = Full(2);
        var inFlight = new List<Interceptor> { RoundInTube(1), RoundInTube(2) };

        Assert.False(magazine.TryTakeTube(inFlight, out int tube));
        Assert.Equal(-1, tube);

        // And nothing was consumed by the refusal.
        Assert.Equal(2, magazine.Ammo);
    }

    [Fact]
    public void OccupancyIsMeasuredInTubeNumbersNotIndices()
    {
        // Rounds number tubes from one; the magazine indexes from zero. Confusing the two shifts
        // every occupancy check by a tube, which is a bug that would look like the salvo skipping.
        var inFlight = new List<Interceptor> { RoundInTube(1) };

        Assert.True(Magazine.IsOccupied(inFlight, 0));
        Assert.False(Magazine.IsOccupied(inFlight, 1));
    }

    // ---- Resizing ------------------------------------------------------

    [Fact]
    public void ResizingToADifferentLauncherRefillsToTheNewCount()
    {
        Magazine magazine = Full(12);
        var empty = new List<Interceptor>();
        for (int i = 0; i < 5; i++) magazine.TryTakeTube(empty, out _);

        magazine.Resize(4);

        Assert.Equal(4, magazine.Capacity);
        Assert.Equal(4, magazine.Ammo);
        Assert.Equal(0, magazine.SpentCount);
    }

    [Fact]
    public void ResizingToTheSameCountStillRefills()
    {
        Magazine magazine = Full(3);
        var empty = new List<Interceptor>();
        magazine.TryTakeTube(empty, out _);

        magazine.Resize(3);

        Assert.Equal(3, magazine.Ammo);
    }

    [Fact]
    public void ALauncherWithNoTubesIsInertRatherThanAnError()
    {
        var magazine = new Magazine();
        magazine.Resize(0);

        Assert.Equal(0, magazine.Capacity);
        Assert.True(magazine.IsEmpty);
        Assert.False(magazine.TryTakeTube([], out _));
        Assert.False(magazine.IsLoaded(0));
    }

    [Fact]
    public void OutOfRangeTubesAreNeverLoaded()
    {
        Magazine magazine = Full(2);

        Assert.False(magazine.IsLoaded(-1));
        Assert.False(magazine.IsLoaded(2));
        Assert.False(magazine.IsLoaded(int.MaxValue));
    }

    // ---- The launch flash ----------------------------------------------

    /// <summary>
    /// Every tube that is not in the air must have its body seated, spent or not.
    ///
    /// <para><c>HideMissile</c> writes <c>Scale</c> and nothing else, so a body that was never
    /// seated keeps whatever transform it had — and an unwritten one sits at the assembly origin,
    /// in the middle of the truck. That is invisible until the tube fires, at which point the
    /// engine has already sampled the cached matrix and draws a frame or two at the old transform
    /// with the new scale: the round flashes at the centre of the vehicle before snapping into
    /// its tube.</para>
    ///
    /// <para>Expressed over every tube and every magazine state rather than one case, because the
    /// flash only appears on tubes that have already fired.</para>
    /// </summary>
    [Fact]
    public void EveryTubeNotInFlightIsSeated_WhateverItsState()
    {
        const int tubes = 6;

        for (int spent = 0; spent <= tubes; spent++)
        {
            for (int tube = 0; tube < tubes; tube++)
            {
                TubeVisual plan = Magazine.Plan(tube, inFlight: false, spent);

                Assert.True(Magazine.RequiresSeating(plan),
                    $"tube {tube} with {spent} spent planned {plan}, which does not seat the body - " +
                    "an unseated body sits at the assembly origin and flashes there when it fires");
            }
        }
    }

    [Fact]
    public void ARoundInTheAirIsPlacedByItsFlightNotByItsTube()
    {
        TubeVisual plan = Magazine.Plan(tubeIndex: 0, inFlight: true, spentCount: 3);

        Assert.Equal(TubeVisual.InFlight, plan);
        Assert.False(Magazine.RequiresSeating(plan));
    }

    [Fact]
    public void SpentTubesAreTheFirstOnesAndAreSeatedButHidden()
    {
        // Fired tubes are the lowest-numbered, matching the order TryTakeTube hands them out.
        Assert.Equal(TubeVisual.Spent, Magazine.Plan(0, inFlight: false, spentCount: 2));
        Assert.Equal(TubeVisual.Spent, Magazine.Plan(1, inFlight: false, spentCount: 2));
        Assert.Equal(TubeVisual.Loaded, Magazine.Plan(2, inFlight: false, spentCount: 2));

        Assert.False(Magazine.IsVisible(TubeVisual.Spent));
        Assert.True(Magazine.IsVisible(TubeVisual.Loaded));
    }

    [Fact]
    public void ThePlanAgreesWithTheMagazineItCameFrom()
    {
        Magazine magazine = Full(4);
        var empty = new List<Interceptor>();
        magazine.TryTakeTube(empty, out int fired);

        // The fired tube reads spent; the rest read loaded. With the round still in the air the
        // instance method and the static must agree on everything but that one flag.
        Assert.Equal(TubeVisual.Spent, magazine.Plan(fired, inFlight: false));
        Assert.Equal(TubeVisual.InFlight, magazine.Plan(fired, inFlight: true));

        for (int i = 1; i < 4; i++) Assert.Equal(TubeVisual.Loaded, magazine.Plan(i, inFlight: false));
    }
}
