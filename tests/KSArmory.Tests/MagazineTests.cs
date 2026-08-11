using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

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
            platformEcl: default, frameVelocityEcl: default) { Munition = Arsenal.Missile57E6 };

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

    // ---- Tubes whose round is still in the air -------------------------

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

    // ---- Deep magazines: guns and rack-fed launchers ---------------------

    /// <summary>
    /// A tube is a place to fire from; the magazine is how much there is to fire. A gun has one
    /// barrel and hundreds of rounds, so the two cannot be the same number.
    /// </summary>
    [Fact]
    public void ADeepMagazineCarriesMoreRoundsThanItHasTubes()
    {
        var magazine = new Magazine();
        magazine.Resize(tubeCount: 2, depth: 500);

        Assert.Equal(2, magazine.TubeCount);
        Assert.Equal(500, magazine.Depth);
        Assert.Equal(500, magazine.Ammo);
    }

    /// <summary>Tubes cycle rather than empty, so a barrel is reusable once its round is clear.</summary>
    [Fact]
    public void ATubeIsReusableAsSoonAsItsRoundIsClear()
    {
        var magazine = new Magazine();
        magazine.Resize(tubeCount: 1, depth: 100);
        var empty = new List<IProjectile>();

        for (int shot = 0; shot < 20; shot++)
        {
            Assert.True(magazine.TryTakeTube(empty, out int tube), $"refused shot {shot}");
            Assert.Equal(0, tube);
        }

        Assert.Equal(80, magazine.Ammo);
    }

    /// <summary>
    /// Occupancy still applies: a body subpart is chosen by tube number, so two rounds on one
    /// tube would share a body however deep the magazine is.
    /// </summary>
    [Fact]
    public void ADeepMagazineStillRefusesAnOccupiedTube()
    {
        var magazine = new Magazine();
        magazine.Resize(tubeCount: 1, depth: 100);

        var inFlight = new List<IProjectile> { RoundInTube(1) };

        Assert.False(magazine.TryTakeTube(inFlight, out _));
        Assert.Equal(100, magazine.Ammo);
    }

    [Fact]
    public void ADeepMagazineRunsDryOnTheReserveNotTheTubes()
    {
        var magazine = new Magazine();
        magazine.Resize(tubeCount: 4, depth: 6);
        var empty = new List<IProjectile>();

        for (int i = 0; i < 6; i++) Assert.True(magazine.TryTakeTube(empty, out _), $"refused shot {i}");

        Assert.True(magazine.IsEmpty);
        Assert.False(magazine.TryTakeTube(empty, out _));
    }

    /// <summary>Every barrel is loaded while the belt has rounds, and none is ever "spent".</summary>
    [Fact]
    public void ADeepMagazineHasNoSpentTubes()
    {
        var magazine = new Magazine();
        magazine.Resize(tubeCount: 3, depth: 50);
        var empty = new List<IProjectile>();

        magazine.TryTakeTube(empty, out _);
        magazine.TryTakeTube(empty, out _);

        Assert.Equal(0, magazine.SpentCount);
        for (int i = 0; i < 3; i++)
        {
            Assert.True(magazine.IsLoaded(i));
            Assert.Equal(TubeVisual.Loaded, magazine.Plan(i, inFlight: false));
        }
    }

    [Fact]
    public void AnEmptyDeepMagazineShowsEveryTubeSpent()
    {
        var magazine = new Magazine();
        magazine.Resize(tubeCount: 2, depth: 2);
        var empty = new List<IProjectile>();

        magazine.TryTakeTube(empty, out _);
        magazine.TryTakeTube(empty, out _);

        Assert.True(magazine.IsEmpty);
        Assert.Equal(TubeVisual.Spent, magazine.Plan(0, inFlight: false));
    }

    [Fact]
    public void ReloadingRefillsTheReserve()
    {
        var magazine = new Magazine();
        magazine.Resize(tubeCount: 2, depth: 10);
        var empty = new List<IProjectile>();

        for (int i = 0; i < 10; i++) magazine.TryTakeTube(empty, out _);
        Assert.True(magazine.IsEmpty);

        magazine.RefillAll();
        Assert.Equal(10, magazine.Ammo);
    }

    /// <summary>
    /// A depth at or below the tube count is the missile case, and must behave exactly as omitting
    /// the depth does — tubes empty one by one and spend.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(2)]
    public void ADepthThatIsNotDeeperThanTheTubesIsTheOrdinaryCase(int depth)
    {
        var magazine = new Magazine();
        magazine.Resize(tubeCount: 4, depth: depth);
        var empty = new List<IProjectile>();

        Assert.Equal(0, magazine.Depth);
        Assert.Equal(4, magazine.Ammo);

        magazine.TryTakeTube(empty, out int tube);
        Assert.Equal(3, magazine.Ammo);
        Assert.Equal(1, magazine.SpentCount);
        Assert.False(magazine.IsLoaded(tube));
    }
}
