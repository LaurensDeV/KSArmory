using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class EntryDirectionTests
{
    private const double Mu = 3.986004418e14;
    private const double R = 6.371e6;
    private const double EntryRadius = R + 130_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);
    private static BallisticBody Still => new(Mu, R, new double3(0, 0, 1), 0.0);

    // A suborbital arc out of the equatorial plane, climbing at 20 deg from 300 km.
    private static (double3 Position, double3 Velocity) Coast()
    {
        double3 position = new(R + 300_000.0, 0, 0);
        double gamma = double.DegreesToRadians(20.0);
        double3 velocity = new double3(Math.Sin(gamma), Math.Cos(gamma) * 0.9, Math.Cos(gamma) * Math.Sqrt(1 - 0.81)) * 6_500.0;
        return (position, velocity);
    }

    // Steps the conic forward until it falls through the radius, the slow way.
    private static (double3 Position, double3 Velocity) FlownTo(double3 position, double3 velocity, double radius)
    {
        double3 p = position, v = velocity;
        for (int i = 0; i < 400_000; i++)
        {
            Assert.True(Kepler.TryCoast(Mu, p, v, 0.05, out double3 next, out double3 nextV));
            if (Vec.Len(next) <= radius && Vec.Dot(nextV, next) < 0.0) return (next, nextV);
            p = next;
            v = nextV;
        }

        throw new InvalidOperationException("never reached the radius");
    }

    private static double Degrees(double3 a, double3 b)
        => double.RadiansToDegrees(Math.Acos(Math.Clamp(Vec.Dot(Vec.Unit(a), Vec.Unit(b)), -1, 1)));

    [Fact]
    public void ItLandsWhereTheFlownConicFallsThroughTheEntryRadius()
    {
        (double3 position, double3 velocity) = Coast();
        Assert.True(EntryDirection.TryAt(Still, position, velocity, EntryRadius, out double3 airflow, out double3 at));

        (double3 flownAt, double3 flownV) = FlownTo(position, velocity, EntryRadius);
        Assert.True(Vec.Len(at - flownAt) < 500.0, $"{Vec.Len(at - flownAt):F0} m apart");
        Assert.True(Degrees(airflow, flownV) < 0.01, $"{Degrees(airflow, flownV):F4} deg");
    }

    [Fact]
    public void EveryPointOfOneCoastGivesTheSameEntry()
    {
        (double3 position, double3 velocity) = Coast();
        Assert.True(EntryDirection.TryAt(Earth, position, velocity, EntryRadius, out double3 first, out _));

        foreach (double later in new[] { 300.0, 900.0 })
        {
            Assert.True(Kepler.TryCoast(Mu, position, velocity, later, out double3 p, out double3 v));
            Assert.True(EntryDirection.TryAt(Earth, p, v, EntryRadius, out double3 again, out _));
            Assert.True(Degrees(first, again) < 1e-4, $"{later} s on: {Degrees(first, again)} deg");
        }
    }

    [Fact]
    public void TheAirflowIsTheVelocityLessTheGroundsSpin()
    {
        (double3 position, double3 velocity) = Coast();
        Assert.True(EntryDirection.TryAt(Still, position, velocity, EntryRadius, out double3 inertial, out double3 at));
        Assert.True(EntryDirection.TryAt(Earth, position, velocity, EntryRadius, out double3 airflow, out _));

        Assert.Equal(0.0, Vec.Len(inertial - Earth.GroundVelocityCci(at) - airflow), 6);
        Assert.True(Degrees(inertial, airflow) > 0.5);
    }

    [Fact]
    public void ItRefusesAConicThatNeverReachesTheAir()
    {
        double3 position = new(R + 400_000.0, 0, 0);
        double3 circular = new(0, Math.Sqrt(Mu / Vec.Len(position)) * 1.01, 0);
        Assert.False(EntryDirection.TryAt(Earth, position, circular, EntryRadius, out _, out _));
    }

    [Fact]
    public void ItRefusesARadialAnOpenAndAnAlreadyEnteredConic()
    {
        double3 position = new(R + 300_000.0, 0, 0);
        Assert.False(EntryDirection.TryAt(Earth, position, new double3(-1_000, 0, 0), EntryRadius, out _, out _));
        Assert.False(EntryDirection.TryAt(Earth, position, new double3(0, 12_000, 0), EntryRadius, out _, out _));
        Assert.False(EntryDirection.TryAt(Earth, new double3(R + 100_000.0, 0, 0), new double3(-500, 7_000, 0),
                                          EntryRadius, out _, out _));
    }

    [Fact]
    public void TheAirFirstTurnsAnArrivingWarheadNearAHundredAndThirtyKilometres()
    {
        BodyAir earth = new(101_325, 1.225, 8_000, 167_000, 9.81, R, BodyTraits.Default);
        double h = EntryDirection.AuthorityAltitude(earth, 7_200.0);
        Assert.InRange(h, 120_000.0, 140_000.0);
        Assert.Equal(0.0, BodyAttitude.Authority(new double3(7_200.0, 0, 0), Math.Exp(-(h + 500.0) / 8_000.0)));
        Assert.True(BodyAttitude.Authority(new double3(7_200.0, 0, 0), Math.Exp(-(h - 500.0) / 8_000.0)) > 0.0);
    }

    [Fact]
    public void ABodyWithNoAirHasNoEntry()
    {
        Assert.True(double.IsNaN(EntryDirection.AuthorityAltitude(BodyAir.Airless(1.62, 1.737e6, BodyTraits.Default), 2_000.0)));
    }
}
