using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Stepping round a craft's weapons.
///
/// <para>The roster itself is KSA-facing and unreachable from here, so this covers the half that
/// is not: the arithmetic. That is where the mistakes live — a selector that will not wrap
/// backwards, or one that throws on the first press of the previous button.</para>
/// </summary>
public class WeaponSelectionTests
{
    [Fact]
    public void SteppingForwardWrapsPastTheEnd()
    {
        Assert.Equal(1, WeaponSelection.Step(3, 0, +1));
        Assert.Equal(2, WeaponSelection.Step(3, 1, +1));
        Assert.Equal(0, WeaponSelection.Step(3, 2, +1));
    }

    /// <summary>
    /// The one that matters. C#'s <c>%</c> keeps the sign of its left operand, so the obvious
    /// <c>(at + by) % count</c> answers −1 on the first press of the previous button — which is a
    /// read off the front of the list rather than the last weapon.
    /// </summary>
    [Fact]
    public void SteppingBackWrapsRatherThanGoingNegative()
    {
        Assert.Equal(2, WeaponSelection.Step(3, 0, -1));
        Assert.Equal(0, WeaponSelection.Step(3, 1, -1));
        Assert.Equal(1, WeaponSelection.Step(3, 2, -1));
    }

    [Fact]
    public void EveryStepLandsOnARealIndex()
    {
        foreach (int count in new[] { 1, 2, 3, 7 })
        {
            for (int at = 0; at < count; at++)
            {
                foreach (int by in new[] { -9, -3, -1, 0, 1, 4, 11 })
                {
                    int next = WeaponSelection.Step(count, at, by);

                    Assert.InRange(next, 0, count - 1);
                }
            }
        }
    }

    /// <summary>A craft with one weapon has nowhere to step to, and must not be moved off it.</summary>
    [Fact]
    public void OneWeaponStaysWhereItIs()
    {
        Assert.Equal(0, WeaponSelection.Step(1, 0, +1));
        Assert.Equal(0, WeaponSelection.Step(1, 0, -1));
    }

    /// <summary>Nothing to select is a valid index, not an exception on a UI path.</summary>
    [Fact]
    public void AnEmptyCraftDoesNotThrow()
    {
        Assert.Equal(0, WeaponSelection.Step(0, 0, +1));
        Assert.Equal(0, WeaponSelection.Step(-1, 3, -1));
    }

    [Fact]
    public void AnOrdinalIsFoundWhereItSits()
    {
        int[] ordinals = [0, 1, 2];

        Assert.Equal(0, WeaponSelection.IndexOf(ordinals, 0));
        Assert.Equal(2, WeaponSelection.IndexOf(ordinals, 2));
    }

    /// <summary>
    /// A launcher can be shot off, and the ordinals then have a hole in them. Falling back to the
    /// first is what keeps the selector usable rather than stranded on a weapon that is gone.
    /// </summary>
    [Fact]
    public void AMissingOrdinalFallsBackToTheFirst()
    {
        int[] afterTheSecondWasLost = [0, 2];

        Assert.Equal(0, WeaponSelection.IndexOf(afterTheSecondWasLost, 1));
        Assert.Equal(1, WeaponSelection.IndexOf(afterTheSecondWasLost, 2));
    }

    /// <summary>
    /// Stepping through every weapon and back again lands where it started, for any list. A
    /// selector that drifts is one that quietly becomes unusable over a long flight.
    /// </summary>
    [Fact]
    public void AFullLapReturnsToTheStart()
    {
        foreach (int count in new[] { 1, 2, 5 })
        {
            int at = 0;
            for (int i = 0; i < count; i++) at = WeaponSelection.Step(count, at, +1);

            Assert.Equal(0, at);
        }
    }

    /// <summary>
    /// The behaviour a symmetric pair is for: stores leave alternate stations rather than one
    /// wing emptying first.
    /// </summary>
    [Fact]
    public void ASymmetricPairAlternates()
    {
        int[] ammo = [1, 1];

        int first = WeaponSelection.NextStation(ammo, lastFired: -1);
        ammo[first]--;

        int second = WeaponSelection.NextStation(ammo, first);

        Assert.NotEqual(first, second);
        Assert.Equal(0, ammo[second == 0 ? 1 : 0]);
    }

    /// <summary>An empty station is stepped over rather than being handed the trigger.</summary>
    [Fact]
    public void AnEmptyStationIsSkipped()
    {
        Assert.Equal(2, WeaponSelection.NextStation([1, 0, 3], lastFired: 0));
        Assert.Equal(0, WeaponSelection.NextStation([1, 0, 0], lastFired: 0));
    }

    /// <summary>
    /// A group with nothing left says so rather than naming a station, so the caller reports an
    /// empty weapon instead of firing at an empty rail and reporting nothing at all.
    /// </summary>
    [Fact]
    public void AGroupWithNoRoundsHasNoNextStation()
    {
        Assert.Equal(-1, WeaponSelection.NextStation([0, 0], lastFired: 1));
        Assert.Equal(-1, WeaponSelection.NextStation([], lastFired: 0));
    }

    /// <summary>
    /// The last full station keeps being chosen once its partner is dry, rather than the group
    /// reporting empty on every other press.
    /// </summary>
    [Fact]
    public void OneFullStationKeepsFiringAfterItsPartnerRunsDry()
    {
        int[] ammo = [0, 2];

        int a = WeaponSelection.NextStation(ammo, lastFired: 1);
        Assert.Equal(1, a);

        ammo[a]--;
        Assert.Equal(1, WeaponSelection.NextStation(ammo, a));
    }
}
