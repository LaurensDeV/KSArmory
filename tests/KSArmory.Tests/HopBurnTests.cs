using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class HopBurnTests
{
    private static readonly double3 Hop = new(1, 0, 0);
    private static readonly double3 Entry = new(0, 1, 0);

    // A bus whose nose slews at 30 deg/s onto whatever line it is asked to hold and whose engine pushes along the nose.
    private sealed class Bus
    {
        public double3 Nose = Entry;
        public double3 Gained = Vec.Zero;
        public double EngineSeconds;
        public int Resolves;

        public HopBurn.Decision Step(HopBurn burn, double asked, double acceleration, double dt)
        {
            double3 toGain = (Hop * asked) - Gained;
            HopBurn.Decision d = burn.Update(dt, toGain, Nose, acceleration);
            if (d.EngineOn)
            {
                Gained += Nose * (acceleration * dt);
                EngineSeconds += dt;
            }
            if (d.ResolveNow) Resolves++;
            if (!d.LineCci.Equals(Vec.Zero)) Slew(d.LineCci, dt);
            return d;
        }

        private void Slew(double3 line, double dt)
        {
            double angle = Vec.AngleBetween(Nose, line);
            double most = double.DegreesToRadians(30.0) * dt;
            if (angle <= most) { Nose = Vec.Unit(line); return; }
            double3 axis = Vec.Unit(Vec.Cross(Nose, line));
            Nose = Vec.Unit(doubleQuat.CreateFromAxisAngle(axis, most) * Nose);
        }
    }

    [Fact]
    public void AHopTheNozzlesCanFlyNeverLightsTheEngine()
    {
        HopBurn burn = new();
        burn.Begin(Entry);
        Bus bus = new();

        bus.Step(burn, HopBurn.EngineAboveMetresPerSecond, 1.7, 0.02);

        Assert.Equal(HopBurn.Stage.Done, burn.Now);
        Assert.False(burn.Burnt);
        Assert.Equal(0.0, bus.EngineSeconds);
    }

    [Fact]
    public void ALargeHopIsBurntOnlyOnceTheNoseIsOnItAndLeavesTheNozzlesAMetreASecond()
    {
        HopBurn burn = new();
        burn.Begin(Entry);
        Bus bus = new();

        double firstBurnAt = double.NaN;
        double offWhenLit = double.NaN;
        for (double t = 0.0; t < 300.0 && burn.Now != HopBurn.Stage.Done; t += 0.02)
        {
            double3 noseBefore = bus.Nose;
            HopBurn.Decision d = bus.Step(burn, 100.0, 1.7, 0.02);
            if (d.EngineOn && double.IsNaN(firstBurnAt))
            {
                firstBurnAt = t;
                offWhenLit = double.RadiansToDegrees(Vec.AngleBetween(noseBefore, Hop));
            }
        }

        Assert.Equal(HopBurn.Stage.Done, burn.Now);
        Assert.True(burn.Burnt);
        Assert.True(offWhenLit <= BusTurn.OnLineDegrees, $"lit {offWhenLit:F1} deg off the hop");
        Assert.True(firstBurnAt >= 3.0 + BusTurn.SteadySeconds - 0.1, $"lit at {firstBurnAt:F1} s");

        double left = 100.0 - Vec.Dot(bus.Gained, Hop);
        Assert.InRange(left, HopBurn.LeaveForTheNozzlesMetresPerSecond - (1.7 * 0.02),
                       HopBurn.LeaveForTheNozzlesMetresPerSecond + (1.7 * 0.02));
        Assert.InRange(bus.EngineSeconds, 99.0 / 1.7 - 0.1, 99.0 / 1.7 + 0.1);
        Assert.InRange(bus.Resolves, (int)(bus.EngineSeconds / HopBurn.ResolveEverySeconds) - 1,
                       (int)(bus.EngineSeconds / HopBurn.ResolveEverySeconds));
        Assert.True(Vec.AngleBetween(bus.Nose, Entry) <= double.DegreesToRadians(BusTurn.OnLineDegrees),
                    "the bus did not turn back onto the line the warheads leave along");
    }

    [Fact]
    public void ABusThatCannotTurnOntoTheHopLeavesItToTheNozzles()
    {
        HopBurn burn = new();
        burn.Begin(Entry);

        for (double t = 0.0; t <= HopBurn.TurnCapSeconds + 1.0 && burn.Now != HopBurn.Stage.Done; t += 0.1)
        {
            HopBurn.Decision d = burn.Update(0.1, Hop * 100.0, Entry, 1.7);
            Assert.False(d.EngineOn);
        }

        Assert.Equal(HopBurn.Stage.Done, burn.Now);
        Assert.False(burn.Burnt);
    }

    [Fact]
    public void WhatIsLeftSwingingOffTheNoseStopsTheBurn()
    {
        HopBurn burn = new();
        burn.Begin(Vec.Zero);
        Bus bus = new() { Nose = Hop };

        for (int i = 0; i < 200 && burn.Now != HopBurn.Stage.Burning; i++) bus.Step(burn, 100.0, 1.7, 0.02);
        Assert.Equal(HopBurn.Stage.Burning, burn.Now);

        double3 swung = Vec.Unit(Hop + new double3(0, 0.5, 0)) * 60.0;
        HopBurn.Decision d = burn.Update(0.02, swung, Hop, 1.7);

        Assert.False(d.EngineOn);
        Assert.Equal(HopBurn.Stage.Done, burn.Now);
    }

    [Fact]
    public void TheBusHoldsItsLineUntilTheWarheadJustReleasedIsClearOfItsSweep()
    {
        HopBurn burn = new();
        burn.Begin(Entry);

        for (double metres = 0.0; metres < HopBurn.ClearMetres; metres += 0.5)
        {
            HopBurn.Decision held = burn.Update(0.02, Hop * 100.0, Entry, 1.7, metres);
            Assert.Equal(HopBurn.Stage.Pending, burn.Now);
            Assert.Equal(Entry, held.LineCci);
        }

        HopBurn.Decision turned = burn.Update(0.02, Hop * 100.0, Entry, 1.7, HopBurn.ClearMetres);
        Assert.Equal(HopBurn.Stage.Pointing, burn.Now);
        Assert.Equal(Hop, turned.LineCci);
    }
}
