using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Flares and chaff against a seeker: which decoy fools which seeker, how often, and that a seeker
/// no decoy can fool flies exactly as it did before there were any.
///
/// <para>Every seduction case is flown over many seeds and asserted as a rate, because a decoy takes a
/// seeker with a probability. Each has a partner that must <em>not</em> be decoyed on the same
/// geometry, so a rate proves the decoy did it rather than the geometry being unwinnable.</para>
/// </summary>
public class CountermeasureTests
{
    private static readonly double3 NoGravity = new(0, 0, 0);
    private static readonly double3 Down = new(0, 0, -9.80665);
    private const double Dt = 1.0 / 60.0;
    private const int Seeds = 40;

    private static MunitionProfile Missile(SeekerBand band, float resistance = 0f, float gate = 0f) => new()
    {
        Name = "cm",
        DisplayName = "cm",
        DragK = 0f,
        Guidance = GuidanceMode.Seeker,
        Band = band,
        CountermeasureResistance = resistance,
        DopplerGateMps = gate,
        SeekerFovDeg = 40f,
        NavConstant = 4f,
        MaxLateralG = 30f,
        LaunchSpeed = 300f,
        BoostSeconds = 2f,
        BoostAccel = 200f,
        SeparationSeconds = 0.2f,
        FuseRadius = 12f,
        FuseArmSeconds = 0.5f,
        MaxFlightSeconds = 30f,
    };

    /// <summary>A fighter's tail at military power.</summary>
    private const double TargetHeat = 3.0;

    /// <summary>A fighter-sized return on <see cref="RadarSignature"/>'s scale.</summary>
    private const double TargetCrossSection = 150.0;

    private sealed record Geometry(double3 Start, double3 Velocity, double3 Kick);

    /// <summary>Going straight away from the missile, dispensing downward.</summary>
    private static readonly Geometry TailChase = new(new(2500, 0, 0), new(200, 0, 0), new(0, 0, -30));

    /// <summary>Crossing square to the line of sight, which is what beaming is.</summary>
    private static readonly Geometry Beaming = new(new(3000, 0, 0), new(0, 220, 0), new(0, 0, -30));

    /// <summary>
    /// Flies one engagement, the target dispensing a cartridge of <paramref name="decoy"/> every
    /// <paramref name="interval"/> seconds from one second in. True if the round reached the target.
    /// </summary>
    private static (bool Hit, bool EverDecoyed) Fly(MunitionProfile munition, DecoyProfile? decoy, int seed,
                                                    Geometry g, double3 carrier = default,
                                                    double interval = 0.5, double dt = Dt)
    {
        var round = new Interceptor(default, carrier + new double3(munition.LaunchSpeed, 0, 0), "target",
                                    tube: 1, platformEcl: default, frameVelocityEcl: carrier) { Munition = munition };
        var seeker = new SeekerLock(new Random(seed));
        var decoys = new List<Decoy>();

        double t = 0.0, nextDrop = 1.0, closest = double.MaxValue;
        bool decoyed = false;
        double signature = munition.Band == SeekerBand.Infrared ? TargetHeat : TargetCrossSection;

        double3 TargetAt(double time) => g.Start + ((g.Velocity + carrier) * time);

        while (round.State == RoundState.Flying && t < 20.0)
        {
            closest = Math.Min(closest, Vec.Len(TargetAt(t) - round.PositionEcl));
            double next = t + dt;

            // Decoys are stepped to the step's end before the round is, as the world is.
            if (decoy is not null && t >= nextDrop)
            {
                decoys.Add(new Decoy(decoy, TargetAt(t), g.Velocity + carrier + g.Kick, "target"));
                nextDrop += interval;
            }

            foreach (Decoy d in decoys) d.Step(dt, Down, carrier, 1.0);
            decoys.RemoveAll(d => d.Spent);

            var target = new TargetState(TargetAt(next), g.Velocity + carrier, 5.0, "target");
            SeekerPick pick = seeker.Choose(munition, round.PositionEcl, round.VelocityEcl, round.VelocityLocal,
                                            target, signature, decoys, dt);
            decoyed |= pick == SeekerPick.Decoy;

            round.Update(dt, seeker.Steer(pick, target), NoGravity, carrier, default, munition);
            t = next;
        }

        return (closest < 25.0, decoyed);
    }

    private static double DecoyedRate(MunitionProfile munition, DecoyProfile? decoy, Geometry g, double3 carrier = default)
    {
        int missed = 0;
        for (int seed = 0; seed < Seeds; seed++)
        {
            if (!Fly(munition, decoy, seed, g, carrier).Hit) missed++;
        }

        return missed / (double)Seeds;
    }

    [Fact]
    public void AHeatSeekerWithNoFlaresHits()
        => Assert.Equal(0.0, DecoyedRate(Missile(SeekerBand.Infrared), null, TailChase));

    [Fact]
    public void AHeatSeekerIsUsuallyTakenByFlares()
    {
        double rate = DecoyedRate(Missile(SeekerBand.Infrared), Arsenal.FlareMju7, TailChase);
        Assert.True(rate >= 0.7, $"flares decoyed {rate:P0}");
    }

    [Fact]
    public void ARadarSeekerIgnoresFlares()
        => Assert.Equal(0.0, DecoyedRate(Missile(SeekerBand.Radar), Arsenal.FlareMju7, TailChase));

    [Fact]
    public void AHeatSeekerIgnoresChaff()
        => Assert.Equal(0.0, DecoyedRate(Missile(SeekerBand.Infrared), Arsenal.ChaffRr170, TailChase));

    [Fact]
    public void ARadarSeekerWithNoGateIsUsuallyTakenByChaff()
    {
        double rate = DecoyedRate(Missile(SeekerBand.Radar), Arsenal.ChaffRr170, TailChase);
        Assert.True(rate >= 0.7, $"chaff decoyed {rate:P0}");
    }

    [Fact]
    public void AResistantSeekerIsRarelyTaken()
    {
        double easy = DecoyedRate(Missile(SeekerBand.Infrared, resistance: 0.1f), Arsenal.FlareMju7, TailChase);
        double hard = DecoyedRate(Missile(SeekerBand.Infrared, resistance: 0.97f), Arsenal.FlareMju7, TailChase);

        Assert.True(hard <= 0.25 && hard < easy / 3.0, $"resistant {hard:P0} against {easy:P0}");
    }

    [Fact]
    public void ASeekerNoDecoyFoolsIgnoresBoth()
    {
        Assert.Equal(0.0, DecoyedRate(Missile(SeekerBand.None), Arsenal.FlareMju7, TailChase));
        Assert.Equal(0.0, DecoyedRate(Missile(SeekerBand.None), Arsenal.ChaffRr170, TailChase));
    }

    /// <summary>A command-link round has no seeker, whatever its band says.</summary>
    [Fact]
    public void ARoundWithNoSeekerIsNeverSeduced()
    {
        MunitionProfile command = Missile(SeekerBand.Infrared);
        command.Guidance = GuidanceMode.CommandLink;
        Assert.False(command.Seducible);

        MunitionProfile harm = Missile(SeekerBand.Radar);
        harm.Guidance = GuidanceMode.AntiRadiation;
        Assert.False(harm.Seducible);
    }

    /// <summary>
    /// The Doppler pair. Chaff stops dead, so against a target running away its closing speed is
    /// hundreds of m/s from the target's and the gate throws it out; against one beaming, both close at
    /// the missile's own speed and the gate cannot tell them apart.
    /// </summary>
    [Fact]
    public void ADopplerGateRejectsChaffUnlessTheTargetBeams()
    {
        double away = DecoyedRate(Missile(SeekerBand.Radar, gate: 40f), Arsenal.ChaffRr170, TailChase);
        double beam = DecoyedRate(Missile(SeekerBand.Radar, gate: 40f), Arsenal.ChaffRr170, Beaming);
        double beamUngated = DecoyedRate(Missile(SeekerBand.Radar), null, Beaming);

        Assert.Equal(0.0, away);
        Assert.Equal(0.0, beamUngated);
        Assert.True(beam >= 0.4, $"beaming chaff decoyed {beam:P0}");
    }

    /// <summary>
    /// The frame's own motion changes nothing: every line of sight is drawn between samples of one
    /// instant, and drag acts on the velocity through the air.
    /// </summary>
    [Fact]
    public void TheFramesMotionChangesNoOutcome()
    {
        double3 carrier = new(29_800, 1_200, 0);
        for (int seed = 0; seed < 12; seed++)
        {
            MunitionProfile m = Missile(SeekerBand.Infrared);
            Assert.Equal(Fly(m, Arsenal.FlareMju7, seed, TailChase),
                         Fly(m, Arsenal.FlareMju7, seed, TailChase, carrier));
        }
    }

    private sealed class AlwaysTaken : Random
    {
        public override double NextDouble() => 0.0;
    }

    /// <summary>
    /// The samples it is handed are a step ahead of the seeker, and a step of 29.8 km/s is half a
    /// kilometre. A flare at the edge of the field has to be judged where it is at the seeker's own
    /// instant, or the frame's motion alone swings it out of view.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(29_800.0)]
    public void AFlareAtTheEdgeOfTheFieldIsJudgedAtTheSeekersInstant(double frameSpeed)
    {
        double3 carrier = new(0, frameSpeed, 0);
        MunitionProfile m = Missile(SeekerBand.Infrared);
        double3 seekerLocal = new(300, 0, 0);

        // Where each is at the seeker's instant, then carried to the step's end as the world samples it.
        double3 flareNow = new(1000, 1000 * Math.Tan(float.DegreesToRadians(35f)), 0);
        var flare = new Decoy(Arsenal.FlareMju7, flareNow + (carrier * Dt), carrier, null);
        var target = new TargetState(new double3(4000, 0, 0) + (carrier * Dt), carrier, 5.0, "target");

        var seeker = new SeekerLock(new AlwaysTaken());
        Assert.Equal(SeekerPick.Decoy, seeker.Choose(m, Vec.Zero, carrier + seekerLocal, seekerLocal,
                                                     target, TargetHeat, [flare], Dt));
    }

    /// <summary>
    /// A radar seeker's range gate: chaff far from the target is not in the cell being tracked, however
    /// much stronger its return is from where the seeker stands. A flare the same distance off still counts.
    /// </summary>
    [Fact]
    public void ARadarSeekerIgnoresChaffOutsideTheTargetsCell()
    {
        double3 local = new(300, 0, 0);
        var target = new TargetState(new(3000, 0, 0), Vec.Zero, 5.0, "target");

        var farChaff = new Decoy(Arsenal.ChaffRr170, new(1500, 20, 0), Vec.Zero, null);
        farChaff.Step(1.0, Vec.Zero, Vec.Zero, 1.0);
        Assert.Equal(SeekerPick.Target, new SeekerLock(new AlwaysTaken())
            .Choose(Missile(SeekerBand.Radar), Vec.Zero, local, local, target, TargetCrossSection, [farChaff], Dt));

        var nearChaff = new Decoy(Arsenal.ChaffRr170, new(2950, 20, 0), Vec.Zero, null);
        nearChaff.Step(1.0, Vec.Zero, Vec.Zero, 1.0);
        Assert.Equal(SeekerPick.Decoy, new SeekerLock(new AlwaysTaken())
            .Choose(Missile(SeekerBand.Radar), Vec.Zero, local, local, target, TargetCrossSection, [nearChaff], Dt));

        var farFlare = new Decoy(Arsenal.FlareMju7, new(1500, 20, 0), Vec.Zero, null);
        Assert.Equal(SeekerPick.Decoy, new SeekerLock(new AlwaysTaken())
            .Choose(Missile(SeekerBand.Infrared), Vec.Zero, local, local, target, TargetHeat, [farFlare], Dt));
    }

    /// <summary>Each decoy is judged once, so how often one wins does not follow the frame rate.</summary>
    [Fact]
    public void TheRateDoesNotFollowTheFrameRate()
    {
        int fast = 0, slow = 0;
        for (int seed = 0; seed < Seeds; seed++)
        {
            if (!Fly(Missile(SeekerBand.Infrared, 0.5f), Arsenal.FlareMju7, seed, TailChase, dt: 1.0 / 120.0).Hit) fast++;
            if (!Fly(Missile(SeekerBand.Infrared, 0.5f), Arsenal.FlareMju7, seed, TailChase, dt: 1.0 / 30.0).Hit) slow++;
        }

        Assert.True(Math.Abs(fast - slow) <= Seeds / 5, $"{fast} at 120 fps against {slow} at 30");
    }

    [Fact]
    public void ASeekerThatDoesNotLookAgainFliesOnBlindOnceTheFlareIsSpent()
    {
        MunitionProfile once = Missile(SeekerBand.Infrared);
        once.ReacquiresAfterDecoy = false;
        var seeker = new SeekerLock(new Random(1));
        var flare = new Decoy(Arsenal.FlareMju7, new(1000, 0, 0), Vec.Zero, null);
        var target = new TargetState(new(1000, 50, 0), Vec.Zero, 5.0, "target");

        SeekerPick pick = SeekerPick.Target;
        for (int i = 0; i < 20 && pick != SeekerPick.Decoy; i++)
        {
            seeker = new SeekerLock(new Random(i));
            pick = seeker.Choose(once, Vec.Zero, new(300, 0, 0), new(300, 0, 0), target, TargetHeat, [flare], Dt);
        }

        Assert.Equal(SeekerPick.Decoy, pick);
        flare.Step(Arsenal.FlareMju7.LifeSeconds + 0.1, Vec.Zero, Vec.Zero, 1.0);
        Assert.Equal(SeekerPick.Lost,
                     seeker.Choose(once, Vec.Zero, new(300, 0, 0), new(300, 0, 0), target, TargetHeat, [], Dt));
        Assert.Null(seeker.Steer(SeekerPick.Lost, target));
    }

    // ---- The decoys themselves ------------------------------------------

    [Fact]
    public void AFlareIsBrightAtOnceAndChaffBlooms()
    {
        Assert.Equal(Arsenal.FlareMju7.PeakSignature, Arsenal.FlareMju7.SignatureAt(0.0), 3);
        Assert.True(Arsenal.ChaffRr170.SignatureAt(0.1) < Arsenal.ChaffRr170.PeakSignature * 0.3);
        Assert.Equal(Arsenal.ChaffRr170.PeakSignature, Arsenal.ChaffRr170.SignatureAt(Arsenal.ChaffRr170.RiseSeconds), 3);
        Assert.Equal(0.0, Arsenal.FlareMju7.SignatureAt(Arsenal.FlareMju7.LifeSeconds));
        Assert.True(Arsenal.FlareMju7.PeakSignature > Signature.HeatOfCraft(75_000, 1.0) * 3.0);
    }

    [Fact]
    public void ChaffStopsDeadAndAFlareFallsBehind()
    {
        double3 air = new(0, 0, 0);
        var chaff = new Decoy(Arsenal.ChaffRr170, Vec.Zero, new(250, 0, 0), null);
        var flare = new Decoy(Arsenal.FlareMju7, Vec.Zero, new(250, 0, 0), null);

        for (int i = 0; i < 6; i++) chaff.Step(Dt, Down, air, 1.0);
        for (int i = 0; i < 60; i++) flare.Step(Dt, Down, air, 1.0);

        Assert.True(chaff.VelocityEcl.X < 10.0, $"chaff still at {chaff.VelocityEcl.X:F1} m/s after 0.1 s");
        Assert.True(flare.VelocityEcl.X is > 60.0 and < 160.0, $"flare at {flare.VelocityEcl.X:F1} m/s after 1 s");

        for (int i = 0; i < 120; i++) chaff.Step(Dt, Down, air, 1.0);
        Assert.True(chaff.VelocityEcl.Z is < 0.0 and > -3.0, $"chaff sinking at {chaff.VelocityEcl.Z:F2} m/s");
    }

    /// <summary>A decoy on the ground lies there, carried with the ground, and still burns out on time.</summary>
    [Fact]
    public void ALandedDecoyLiesWhereItFellAndBurnsOut()
    {
        double3 ground = new(400, 0, 0);
        var flare = new Decoy(Arsenal.FlareMju7, Vec.Zero, new(250, 0, -20), null);
        flare.Land(ground, ground);

        flare.Step(1.0, Down, ground, 1.0);
        Assert.True(flare.Landed);
        Assert.True(Vec.Len(flare.PositionEcl - (ground * 2.0)) < 1e-9);
        Assert.Equal(ground, flare.VelocityEcl);

        flare.Step(Arsenal.FlareMju7.LifeSeconds, Down, ground, 1.0);
        Assert.True(flare.Spent);
    }

    [Fact]
    public void ADecoysFlightIsTheSameInAMovingFrame()
    {
        double3 carrier = new(29_800, -3_000, 0);
        var still = new Decoy(Arsenal.ChaffRr170, Vec.Zero, new(250, 10, 5), null);
        var moving = new Decoy(Arsenal.ChaffRr170, Vec.Zero, carrier + new double3(250, 10, 5), null);

        for (int i = 0; i < 90; i++)
        {
            still.Step(Dt, Down, Vec.Zero, 1.0);
            moving.Step(Dt, Down, carrier, 1.0);
        }

        double elapsed = 90 * Dt;
        Assert.True(Vec.Len(moving.PositionEcl - (carrier * elapsed) - still.PositionEcl) < 1e-6);
    }

    // ---- The dispenser ---------------------------------------------------

    [Fact]
    public void ASalvoIsSpacedOnSimulatedTimeAtAnyFrameRate()
    {
        foreach (double dt in new[] { 1.0 / 144.0, 1.0 / 30.0, 0.2 })
        {
            var d = new Dispenser(Arsenal.Ale47Flare);
            DispenseOrder.Request([d], DecoyKind.Flare);

            // Every step before the interval is up throws only the first.
            int thrown = 0;
            for (double t = 0.0; t + dt < Arsenal.Ale47Flare.SalvoIntervalSeconds; t += dt) thrown += d.Update(dt);
            Assert.Equal(1, thrown);

            for (int i = 0; i < 100; i++) thrown += d.Update(dt);

            Assert.Equal(Arsenal.Ale47Flare.SalvoSize, thrown);
            Assert.Equal(Arsenal.Ale47Flare.Count - Arsenal.Ale47Flare.SalvoSize, d.Remaining);
        }
    }

    [Fact]
    public void ADispenserStopsWhenItIsEmpty()
    {
        var d = new Dispenser(Arsenal.Ale47Chaff);
        int thrown = 0;
        for (int i = 0; i < 40; i++)
        {
            DispenseOrder.Request([d], DecoyKind.Chaff);
            thrown += d.Update(1.0);
        }

        Assert.Equal(0, d.Remaining);
        Assert.Equal(Arsenal.Ale47Chaff.Count, thrown);
    }

    /// <summary>
    /// A craft's dispensers answer as one: a press reaches only the load it names, and every dispenser
    /// of that load throws together, so a symmetric pair puts its decoys either side of the craft.
    /// </summary>
    [Fact]
    public void ACraftsDispensersAnswerTogether()
    {
        var left = new Dispenser(Arsenal.Ale47Flare);
        var right = new Dispenser(Arsenal.Ale47Flare);
        var chaff = new Dispenser(Arsenal.Ale47Chaff);
        Dispenser[] craft = [left, right, chaff];
        int salvo = Arsenal.Ale47Flare.SalvoSize;

        Assert.Equal(2 * salvo, DispenseOrder.Request(craft, DecoyKind.Flare));
        Assert.Equal(salvo, left.Queued);
        Assert.Equal(salvo, right.Queued);
        Assert.Equal(0, chaff.Queued);

        // One side run dry: the other still answers, and one with a single cartridge throws what it has.
        while (left.Busy || right.Busy)
        {
            left.Update(1.0);
            right.Update(1.0);
        }

        left.Queue(left.Remaining);
        while (left.Busy) left.Update(1.0);
        right.Queue(right.Remaining - 1);
        while (right.Busy) right.Update(1.0);

        Assert.Equal(1, DispenseOrder.Request(craft, DecoyKind.Flare));
        Assert.Equal(0, left.Queued);
        Assert.Equal(1, right.Queued);

        Assert.Equal((Arsenal.Ale47Chaff.Count, Arsenal.Ale47Chaff.Count), DispenseOrder.Load(craft, DecoyKind.Chaff));
        Assert.Equal(0, DispenseOrder.Request([chaff], DecoyKind.Flare));
    }

    [Fact]
    public void AutoDispenseAnswersTheSeekerItSeesAndOnlyWhatIsClosing()
    {
        var auto = new AutoDispense();
        double3 craft = Vec.Zero;

        var ir = new IncomingMissile(new(3000, 0, 0), new(-600, 0, 0), SeekerBand.Infrared);
        Assert.Equal((true, false), auto.Decide(Dt, craft, Vec.Zero, [ir]));

        // Rate-limited while the warning holds.
        Assert.Equal((false, false), auto.Decide(Dt, craft, Vec.Zero, [ir]));

        var unknown = new IncomingMissile(new(3000, 0, 0), new(-600, 0, 0), null);
        Assert.Equal((true, true), auto.Decide(AutoDispense.RetriggerSeconds, craft, Vec.Zero, [unknown]));

        var opening = new IncomingMissile(new(3000, 0, 0), new(600, 0, 0), SeekerBand.Radar);
        var far = new IncomingMissile(new(60_000, 0, 0), new(-600, 0, 0), SeekerBand.Radar);
        Assert.Equal((false, false), auto.Decide(AutoDispense.RetriggerSeconds, craft, Vec.Zero, [opening, far]));
    }

    /// <summary>
    /// A gun's shell carries a munition whose guidance field defaults to a command link, so a test on the
    /// profile alone reads a cannon burst as a stream of missiles and dumps the dispensers into it.
    /// </summary>
    [Fact]
    public void AWarningReceiverAnswersMissilesAndNotShells()
    {
        MunitionProfile cannon = Catalogue.MunitionNamed("20MM");
        Assert.True(cannon.Powered);

        var shell = new Slug(Vec.Zero, new double3(1000, 0, 0), "target", tube: -1,
                             platformEcl: Vec.Zero, frameVelocityEcl: Vec.Zero) { Munition = cannon };
        var missile = new Interceptor(Vec.Zero, new double3(300, 0, 0), "target", tube: 1,
                                      platformEcl: Vec.Zero, frameVelocityEcl: Vec.Zero) { Munition = Arsenal.Missile9J };

        Assert.False(AutoDispense.Warns(shell));
        Assert.True(AutoDispense.Warns(missile));
    }

    [Fact]
    public void TheShippedMissilesSeeWithTheRightBand()
    {
        Assert.Equal(SeekerBand.Infrared, Arsenal.Missile9J.Band);
        Assert.True(Arsenal.Missile9J.Seducible);
        Assert.Equal(SeekerBand.Radar, Arsenal.Missile120C.Band);
        Assert.True(Arsenal.Missile120C.Seducible);
        Assert.False(Arsenal.MissileAgm88.Seducible);
        Assert.False(Catalogue.MunitionNamed("57E6").Seducible);
    }
}
