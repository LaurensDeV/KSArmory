using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// <see cref="ChaffNotch"/>: a tracking set losing a beaming target to chaff beside it. Each case that
/// breaks the track has a partner on the same geometry that must not, so a break proves the rule and
/// not the scene.
/// </summary>
public class ChaffNotchTests
{
    private const double Dt = 1.0 / 60.0;
    private const double TargetCrossSection = 150.0;
    private static readonly object Target = new();

    private static SensorProfile Set(bool optical = true) => new()
    {
        Name = "set",
        DisplayName = "set",
        ChaffNotchMps = 30f,
        ChaffReacquireSeconds = 3f,
        OpticalBackup = optical,
    };

    /// <summary>A bloomed chaff cloud 40 m behind the target, stopped in the air.</summary>
    private static Decoy Cloud(double3 targetEcl, double3 carrier, double age = 1.0)
    {
        // Started where the frame's motion over its age leaves it 40 m behind the target.
        var d = new Decoy(Arsenal.ChaffRr170, targetEcl + new double3(0, -40, 0) - (carrier * age), carrier, null);
        d.Step(age, Vec.Zero, carrier, 1.0);
        return d;
    }

    // 5 km out along +X; beaming is flying along +Y, running is flying along +X.
    private static readonly double3 At = new(5000, 0, 0);
    private static readonly double3 BeamVelocity = new(0, 220, 0);
    private static readonly double3 RunVelocity = new(220, 0, 0);

    private static bool Hides(ChaffNotch notch, SensorProfile set, double3 velocity, IReadOnlyList<Decoy> decoys,
                              double3 carrier = default, double dt = Dt)
    {
        notch.BeginScan(dt);
        bool hidden = notch.Hides(set, Target, Vec.Zero, carrier, At, velocity + carrier,
                                  TargetCrossSection, decoys);
        notch.EndScan();
        return hidden;
    }

    [Fact]
    public void ABeamingTargetWithChaffBesideItIsLost()
    {
        Assert.True(Hides(new ChaffNotch(), Set(), BeamVelocity, [Cloud(At, Vec.Zero)]));
    }

    [Fact]
    public void ARunningTargetIsNotLostToTheSameChaff()
    {
        Assert.False(Hides(new ChaffNotch(), Set(), RunVelocity, [Cloud(At, Vec.Zero)]));
    }

    [Fact]
    public void ABeamingTargetWithNoChaffIsHeld()
    {
        Assert.False(Hides(new ChaffNotch(), Set(), BeamVelocity, []));
    }

    [Fact]
    public void ChaffStillBloomingOrOutOfTheCellTakesNothing()
    {
        var young = new Decoy(Arsenal.ChaffRr170, At + new double3(0, -40, 0), Vec.Zero, null);
        Assert.False(Hides(new ChaffNotch(), Set(), BeamVelocity, [young]));

        var far = new Decoy(Arsenal.ChaffRr170, At + new double3(0, -400, 0), Vec.Zero, null);
        far.Step(1.0, Vec.Zero, Vec.Zero, 1.0);
        Assert.False(Hides(new ChaffNotch(), Set(), BeamVelocity, [far]));
    }

    [Fact]
    public void FlaresNeverBreakARadarTrack()
    {
        var flare = new Decoy(Arsenal.FlareMju7, At + new double3(0, -40, 0), Vec.Zero, null);
        Assert.False(Hides(new ChaffNotch(), Set(), BeamVelocity, [flare]));
    }

    [Fact]
    public void ASetChaffNeverBreaksIsUnaffected()
    {
        SensorProfile set = Set();
        set.ChaffNotchMps = 0f;
        Assert.False(Hides(new ChaffNotch(), set, BeamVelocity, [Cloud(At, Vec.Zero)]));
    }

    /// <summary>
    /// The break lasts the reacquire time and no longer; after it the optical channel holds the target
    /// through fresh chaff for as long as it stays in the notch.
    /// </summary>
    [Fact]
    public void TheOpticalChannelHoldsTheTargetOnceFoundAgain()
    {
        var notch = new ChaffNotch();
        SensorProfile set = Set(optical: true);
        Decoy[] chaff = [Cloud(At, Vec.Zero)];

        double t = 0.0;
        while (t < set.ChaffReacquireSeconds - Dt)
        {
            Assert.True(Hides(notch, set, BeamVelocity, chaff));
            Assert.True(notch.IsBroken(Target));
            t += Dt;
        }

        for (int i = 0; i < 5; i++) Hides(notch, set, BeamVelocity, chaff);

        for (int i = 0; i < 120; i++) Assert.False(Hides(notch, set, BeamVelocity, chaff));
        Assert.False(notch.IsBroken(Target));
    }

    /// <summary>
    /// The hold is about the target staying in the notch, not about the chaff: a cloud that has dropped
    /// behind by the time the track is found again, followed by a fresh one on the same pass, must not
    /// break it twice. Leaving the notch is what lets the next pass work.
    /// </summary>
    [Fact]
    public void FreshChaffOnTheSamePassDoesNotBreakTheTrackAgain()
    {
        var notch = new ChaffNotch();
        SensorProfile set = Set(optical: true);
        Decoy[] chaff = [Cloud(At, Vec.Zero)];

        Assert.True(Hides(notch, set, BeamVelocity, chaff));
        for (double t = 0.0; t < set.ChaffReacquireSeconds + 0.5; t += Dt) Hides(notch, set, BeamVelocity, []);
        Assert.False(notch.IsBroken(Target));

        for (int i = 0; i < 60; i++) Assert.False(Hides(notch, set, BeamVelocity, chaff));

        // Out of the notch and back into it: the next pass works.
        Hides(notch, set, RunVelocity, []);
        Assert.True(Hides(notch, set, BeamVelocity, chaff));
    }

    [Fact]
    public void WithoutAnOpticalChannelChaffBreaksTheTrackAgain()
    {
        var notch = new ChaffNotch();
        SensorProfile set = Set(optical: false);
        Decoy[] chaff = [Cloud(At, Vec.Zero)];

        int hidden = 0;
        for (int i = 0; i < (int)(10.0 / Dt); i++) if (Hides(notch, set, BeamVelocity, chaff)) hidden++;

        Assert.True(hidden > 0.9 * (10.0 / Dt), $"hidden {hidden} of {(int)(10.0 / Dt)} scans");
    }

    /// <summary>The break is timed on the scans' own clock, so it lasts the same at any frame rate.</summary>
    [Theory]
    [InlineData(1.0 / 144.0)]
    [InlineData(1.0 / 20.0)]
    public void TheBreakLastsTheReacquireTimeAtAnyFrameRate(double dt)
    {
        var notch = new ChaffNotch();
        SensorProfile set = Set();
        Decoy[] chaff = [Cloud(At, Vec.Zero)];

        double hiddenFor = 0.0;
        for (double t = 0.0; t < 6.0; t += dt)
        {
            if (Hides(notch, set, BeamVelocity, chaff, dt: dt)) hiddenFor += dt;
        }

        Assert.InRange(hiddenFor, set.ChaffReacquireSeconds - 0.06, set.ChaffReacquireSeconds + 0.06);
    }

    /// <summary>Every speed is a difference of two samples of one instant, so the frame's motion cancels.</summary>
    [Fact]
    public void TheFramesMotionChangesNothing()
    {
        double3 carrier = new(29_800, 1_500, -400);

        Assert.True(Hides(new ChaffNotch(), Set(), BeamVelocity, [Cloud(At, carrier)], carrier));
        Assert.False(Hides(new ChaffNotch(), Set(), RunVelocity, [Cloud(At, carrier)], carrier));
    }

    [Fact]
    public void ThePantsirsSetIsBrokenByChaffAndHeldOptically()
    {
        SensorProfile set = Catalogue.SensorNamed("1RS1");
        Assert.Equal("1RS1", set.Name);
        Assert.True(set.ChaffNotchMps > 0f);
        Assert.True(set.OpticalBackup);
    }
}
