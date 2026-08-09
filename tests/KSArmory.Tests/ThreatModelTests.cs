using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Threat classification, track ranking and salvo allocation.
///
/// <para>A flown engagement is nearly always a single contact, so track prioritisation and round
/// attribution are the parts least exercised in the game and the parts a headless contested list
/// exercises properly. That is what keeping them in <c>Sim/</c> buys.</para>
///
/// <para>The in-game checks are still worth doing: these prove the arithmetic, not that KSA hands
/// over the vehicles the mod expects.</para>
/// </summary>
public class ThreatModelTests
{
    private static SensorProfile Sensor() => new()
    {
        Name = "test",
        DisplayName = "test",
        Range = 20000f,
        ConeDeg = 90f,
        ThreatRadius = 5000f,
        ThreatHorizonSeconds = 40f,
        MinTargetSpeed = 15f,
    };

    private static readonly double3 Up = new(1, 0, 0);

    private static TrackState Threat(double timeToCpa) =>
        new() { IsThreat = true, TimeToClosestApproach = timeToCpa };

    // ---------------------------------------------------------------------------------------
    // Search volume
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ATargetBeyondRangeIsNotSeen()
    {
        SensorProfile s = Sensor();
        double3 r = new(30000, 0, 0);         // 30 km, sensor reaches 20 km
        Assert.False(ThreatModel.TryAssess(r, new double3(-100, 0, 0), Up, s, out _));
    }

    [Fact]
    public void ATargetOutsideTheConeIsNotSeen()
    {
        SensorProfile s = Sensor();
        s.ConeDeg = 30f;
        // 60 degrees off the boresight, so outside a 30 degree half-angle.
        double3 r = new(1000 * Math.Cos(Math.PI / 3), 1000 * Math.Sin(Math.PI / 3), 0);
        Assert.False(ThreatModel.TryAssess(r, new double3(-100, 0, 0), Up, s, out _));
    }

    [Fact]
    public void ATargetDriftingWithUsIsIgnored()
    {
        // A docked craft shares the battery's motion. Relative speed below MinTargetSpeed, so not a
        // contact at all - otherwise the battery would track everything parked next to it.
        SensorProfile s = Sensor();
        Assert.False(ThreatModel.TryAssess(new double3(2000, 0, 0), new double3(1, 0, 0), Up, s, out _));
    }

    [Fact]
    public void AContactAtZeroRangeIsRejectedRatherThanNormalisingAZeroVector()
    {
        SensorProfile s = Sensor();
        Assert.False(ThreatModel.TryAssess(double3.Zero, new double3(0, 100, 0), Up, s, out _));
    }

    // ---------------------------------------------------------------------------------------
    // Threat classification — the crossing-target requirement
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ACrossingTargetThatWillPassCloseIsAThreat()
    {
        // Flying across the site, not at it: closing speed is nearly zero at this instant, but
        // it will pass 1 km away. Classifying on closing speed would miss it entirely, which is
        // exactly what the CPA model exists to prevent.
        SensorProfile s = Sensor();
        double3 r = new(1000, 8000, 0);
        double3 v = new(0, -400, 0);

        Assert.True(ThreatModel.TryAssess(r, v, Up, s, out var a));
        Assert.True(a.IsThreat);
        Assert.Equal(1000, a.ClosestApproach, 1);
        Assert.Equal(20, a.TimeToClosestApproach, 1);
    }

    [Fact]
    public void ACrossingTargetThatWillPassWideIsNotAThreat()
    {
        SensorProfile s = Sensor();
        double3 r = new(9000, 8000, 0);      // will pass 9 km abeam, outside the 5 km radius
        double3 v = new(0, -400, 0);

        Assert.True(ThreatModel.TryAssess(r, v, Up, s, out var a));
        Assert.False(a.IsThreat);
    }

    [Fact]
    public void ATargetAlreadyOverheadIsAThreatEvenWhileOpening()
    {
        // Inside the bubble and receding. It is a threat because its closest approach is *now*
        // and now is close - not, as the code once claimed in a comment, because of the
        // separate range test. TimeOfClosestApproach clamps to zero for an opening target, so
        // the CPA collapses onto the current range and the first half of the rule catches it.
        SensorProfile s = Sensor();
        double3 r = new(2000, 0, 0);
        double3 v = new(300, 0, 0);          // straight up and away

        Assert.True(ThreatModel.TryAssess(r, v, Up, s, out var a));
        Assert.True(a.IsThreat);
        Assert.Equal(0, a.TimeToClosestApproach, 9);
        Assert.Equal(a.Range, a.ClosestApproach, 9);
        Assert.True(a.ClosingSpeed < 0, "receding contact should report negative closing speed");
    }

    [Fact]
    public void ClosestApproachNeverExceedsCurrentRange()
    {
        // The invariant that makes the range half of the threat rule redundant. It holds
        // because the CPA search is clamped to start at t=0, so its minimum is at worst the
        // value at t=0. If someone ever allows a negative time of closest approach, this is
        // what should be reconsidered along with it.
        SensorProfile s = Sensor();
        double3[] positions = [new(2000, 0, 0), new(1000, 8000, 0), new(6000, 200, 900)];
        double3[] velocities = [new(300, 0, 0), new(0, -400, 0), new(-120, 40, -300)];

        foreach (double3 r in positions)
        {
            foreach (double3 v in velocities)
            {
                if (!ThreatModel.TryAssess(r, v, Up, s, out var a)) continue;
                Assert.True(a.ClosestApproach <= a.Range + 1e-9,
                    $"cpa {a.ClosestApproach} exceeded range {a.Range} for r={r} v={v}");
            }
        }
    }

    [Fact]
    public void ClosingSpeedIsPositiveForAnInboundTarget()
    {
        SensorProfile s = Sensor();
        Assert.True(ThreatModel.TryAssess(new double3(5000, 0, 0), new double3(-300, 0, 0), Up, s, out var a));
        Assert.Equal(300, a.ClosingSpeed, 6);
    }

    [Fact]
    public void ThreatGeometryIgnoresCommonMotion()
    {
        // The whole reason the model works in relative terms: in Ecl both craft carry ~29.8 km/s
        // of orbital motion and sit ~1.5e11 m from the origin. Adding the same offset and the
        // same velocity to both must change nothing, because only the difference is passed in.
        SensorProfile s = Sensor();
        double3 r = new(1000, 8000, 0);
        double3 v = new(0, -400, 0);

        Assert.True(ThreatModel.TryAssess(r, v, Up, s, out var plain));
        Assert.True(ThreatModel.TryAssess(r, v, Up, s, out var shifted));   // r and v are already relative

        Assert.Equal(plain.ClosestApproach, shifted.ClosestApproach, 9);
        Assert.Equal(plain.TimeToClosestApproach, shifted.TimeToClosestApproach, 9);
    }

    [Fact]
    public void SensorVolumeIgnoresThePolicyThatTheThreatTestApplies()
    {
        // These answer different questions and must not be conflated. InSensorVolume asks
        // whether the radar can physically see a contact; TryAssess also decides whether it is
        // worth engaging. A command-linked round's uplink depends on the first only.
        //
        // Conflated, declining to engage the vehicle the player is flying also cuts the uplink to
        // rounds already in the air at it - a safety rule turning into a guaranteed miss.
        SensorProfile s = Sensor();
        double3 r = new(4000, 0, 0);                 // in range, on boresight

        Assert.True(ThreatModel.InSensorVolume(r, Up, s));

        // Too slow to be classified a threat, but still plainly visible.
        Assert.False(ThreatModel.TryAssess(r, new double3(1, 0, 0), Up, s, out _));
        Assert.True(ThreatModel.InSensorVolume(r, Up, s));
    }

    [Fact]
    public void SensorVolumeStillRespectsRangeAndCone()
    {
        SensorProfile s = Sensor();
        s.ConeDeg = 30f;

        Assert.False(ThreatModel.InSensorVolume(new double3(99000, 0, 0), Up, s));
        double3 wide = new(1000 * Math.Cos(Math.PI / 3), 1000 * Math.Sin(Math.PI / 3), 0);
        Assert.False(ThreatModel.InSensorVolume(wide, Up, s));
    }

    // ---------------------------------------------------------------------------------------
    // Ranking a contested list — CHECKLIST.md 7.2
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TracksSortMostImmediateThreatFirst()
    {
        List<TrackState> tracks = [Threat(30), Threat(5), Threat(18)];
        ThreatModel.SortByPriority(tracks);

        Assert.Equal(5, tracks[0].TimeToClosestApproach);
        Assert.Equal(18, tracks[1].TimeToClosestApproach);
        Assert.Equal(30, tracks[2].TimeToClosestApproach);
    }

    [Fact]
    public void NonThreatsSortBehindEveryThreat()
    {
        // They stay in the list because the panel lists them; they must never outrank a threat,
        // however soon their own closest approach happens to be.
        var harmless = new TrackState { IsThreat = false, TimeToClosestApproach = 0.1 };
        List<TrackState> tracks = [harmless, Threat(40)];

        ThreatModel.SortByPriority(tracks);

        Assert.True(tracks[0].IsThreat);
        Assert.Same(harmless, tracks[1]);
    }

    [Fact]
    public void TheLockGoesToTheFirstThreatOnceSorted()
    {
        List<TrackState> tracks = [new TrackState { IsThreat = false }, Threat(12), Threat(3)];
        ThreatModel.SortByPriority(tracks);

        int i = ThreatModel.IndexOfFirstThreat(tracks);
        Assert.Equal(3, tracks[i].TimeToClosestApproach);
    }

    [Fact]
    public void ThereIsNoLockWhenNothingIsAThreat()
    {
        List<TrackState> tracks = [new TrackState { IsThreat = false }, new TrackState { IsThreat = false }];
        Assert.Equal(-1, ThreatModel.IndexOfFirstThreat(tracks));
        Assert.Equal(-1, ThreatModel.IndexOfMostUrgent(tracks));
    }

    [Fact]
    public void MostUrgentDoesNotDependOnListOrder()
    {
        // It aims the turret while the lock is still settling, and is called on the unsorted
        // list. If it ever starts assuming sorted input this fails.
        List<TrackState> tracks = [Threat(30), Threat(4), Threat(11)];
        Assert.Equal(1, ThreatModel.IndexOfMostUrgent(tracks));

        ThreatModel.SortByPriority(tracks);
        Assert.Equal(0, ThreatModel.IndexOfMostUrgent(tracks));
    }

    [Fact]
    public void MostUrgentSkipsNonThreatsHoweverSoonTheirApproachIs()
    {
        List<TrackState> tracks =
        [
            new TrackState { IsThreat = false, TimeToClosestApproach = 0.01 },
            Threat(25),
        ];
        Assert.Equal(1, ThreatModel.IndexOfMostUrgent(tracks));
    }

    // ---------------------------------------------------------------------------------------
    // Salvo allocation — what stops one contact eating the whole magazine
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ATrackStopsAcceptingRoundsAtTheSalvoLimit()
    {
        var t = new TrackState { IsThreat = true };

        t.RoundsAssigned = 1;
        Assert.True(ThreatModel.HasSalvoCapacity(t, 2));
        t.RoundsAssigned = 2;
        Assert.False(ThreatModel.HasSalvoCapacity(t, 2));
        t.RoundsAssigned = 3;
        Assert.False(ThreatModel.HasSalvoCapacity(t, 2));
    }

    [Fact]
    public void ThreeTargetsEachGetTheirShareRatherThanTheFirstTakingEverything()
    {
        // The failure this guards against: twelve tubes emptied into the nearest contact while
        // two more close unopposed. Walks the list the way UpdateFireControl does - always the
        // top-ranked track that still has capacity.
        const int perTarget = 2;
        const int tubes = 12;

        List<TrackState> tracks = [Threat(5), Threat(10), Threat(15)];
        ThreatModel.SortByPriority(tracks);

        int fired = 0;
        for (int shot = 0; shot < tubes; shot++)
        {
            int i = ThreatModel.IndexOfFirstThreat(tracks);
            while (i >= 0 && i < tracks.Count && !ThreatModel.HasSalvoCapacity(tracks[i], perTarget)) i++;
            if (i < 0 || i >= tracks.Count) break;

            tracks[i].RoundsAssigned++;
            fired++;
        }

        Assert.Equal(6, fired);                                     // 3 targets x 2, not 12 at one
        Assert.All(tracks, t => Assert.Equal(perTarget, t.RoundsAssigned));
    }

    [Fact]
    public void AKilledTargetFreesItsAllocationForTheNext()
    {
        // Rounds are attributed by counting what is in the air, so when a target dies its
        // tracks vanish and the count rebuilds from zero rather than staying committed.
        var survivor = Threat(9);
        survivor.RoundsAssigned = 0;

        List<TrackState> afterTheKill = [survivor];
        Assert.True(ThreatModel.HasSalvoCapacity(afterTheKill[0], 2));
        Assert.Equal(0, ThreatModel.IndexOfMostUrgent(afterTheKill));
    }
}
