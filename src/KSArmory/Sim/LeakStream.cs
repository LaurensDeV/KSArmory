using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The stream a leaking tank throws: what its liquid is like leaving the hole, and the points it is
/// drawn through. The liquid itself has already been taken off the tank by <see cref="TankLeak"/>;
/// <see cref="LeakJet"/> is what flies it.
/// </summary>
internal static class LeakStream
{
    /// <summary>Points along one stream, and so segments drawn for it less one.</summary>
    public const int Points = 32;

    /// <summary>How long a stream's liquid is followed from the hole; the ground or the hull hides the rest.</summary>
    public const double FollowSeconds = 2.0;

    /// <summary>
    /// How many of its own diameters a jet runs whole before it breaks into drops — Rayleigh–Plateau
    /// breakup, which for a slow jet like a tank's is a few tens.
    /// </summary>
    public const double WholeForDiameters = 30.0;

    /// <summary>
    /// One point on a stream: where it is, how thick, how far along the stream from the hole, and how
    /// long the liquid there has been out — what a pattern moving with the liquid is keyed on.
    /// </summary>
    public readonly record struct Point(double3 Position, double Radius, double AlongMetres, double OutSeconds);

    /// <summary>The jet's radius leaving a hole: it necks down to the discharge coefficient's share of the area.</summary>
    public static double ExitRadius(double holeRadius) => holeRadius * Math.Sqrt(TankLeak.DischargeCoefficient);

    /// <summary>How far the stream runs whole before it is drops.</summary>
    public static double WholeForMetres(double exitRadius) => WholeForDiameters * 2.0 * exitRadius;
}

/// <summary>
/// One hole's stream, flown: a parcel of liquid leaves every <see cref="EmitSeconds"/> and each is
/// moved from where it is under what the craft feels now.
///
/// <para><b>Flown, not solved.</b> Drawn as the arc for the acceleration of the moment, a stream
/// re-bends whole the instant a throttle moves, liquid two seconds out included. Liquid already out
/// is free: relative to the craft every parcel feels the change at once, but in how it accelerates,
/// not in where it is — so the stream bends from the hole outward, swings behind a turning craft,
/// and falls away from the hole when the leak stops.</para>
///
/// <para><b>In a frame riding with the craft but not turning with it</b>, where a free parcel's
/// acceleration is the pull the craft feels reversed: straight down on the pad, aft of a rocket
/// accelerating away from its own leak, nothing at all in a coast.</para>
/// </summary>
internal sealed class LeakJet
{
    /// <summary>How often a parcel leaves the hole: the stream's length over its points.</summary>
    public const double EmitSeconds = LeakStream.FollowSeconds / (LeakStream.Points - 1);

    private readonly record struct Parcel(double3 Position, double3 Velocity, double Age, double LeftAt);

    // Newest first.
    private readonly List<Parcel> _parcels = [];
    private double _sinceEmitted;

    /// <summary>Whether any of its liquid is still in the air.</summary>
    public bool Empty => _parcels.Count == 0;

    /// <summary>
    /// Moves every parcel on by <paramref name="dt"/> under <paramref name="acceleration"/>, drops the
    /// ones followed long enough, and — while it is <paramref name="flowing"/> — lets new ones go from
    /// <paramref name="exit"/> at <paramref name="exitVelocity"/>.
    /// </summary>
    public void Step(double dt, double3 exit, double3 exitVelocity, double3 acceleration, bool flowing)
    {
        if (!(dt > 0.0) || !double.IsFinite(dt)) return;

        for (int i = 0; i < _parcels.Count; i++)
        {
            Parcel p = _parcels[i];
            double3 velocity = p.Velocity + (acceleration * dt);
            _parcels[i] = p with
            {
                Position = p.Position + ((p.Velocity + velocity) * (0.5 * dt)),
                Velocity = velocity,
                Age = p.Age + dt,
            };
        }

        while (_parcels.Count > 0 && _parcels[^1].Age > LeakStream.FollowSeconds) _parcels.RemoveAt(_parcels.Count - 1);

        if (!flowing)
        {
            _sinceEmitted = 0.0;
            return;
        }

        double speed = Vec.Len(exitVelocity);
        _sinceEmitted = Math.Min(_sinceEmitted + dt, LeakStream.FollowSeconds);
        while (_sinceEmitted >= EmitSeconds)
        {
            // Left that long ago in the step, so it has flown that far already.
            _sinceEmitted -= EmitSeconds;
            double age = _sinceEmitted;
            _parcels.Insert(0, new Parcel(exit + (exitVelocity * age) + (acceleration * (0.5 * age * age)),
                                          exitVelocity + (acceleration * age), age, speed));
        }
    }

    /// <summary>
    /// The points to draw the stream through, from the hole while it is <paramref name="flowing"/> and
    /// from its newest liquid when it is not, into <paramref name="into"/>; how many were written. Each
    /// thins as it speeds up, because the same flow passes every section: <c>r ∝ 1/√v</c>.
    /// </summary>
    public int Draw(double3 exit, double exitRadius, bool flowing, Span<LeakStream.Point> into)
    {
        int count = 0;
        double along = 0.0;
        double3 previous = exit;

        if (flowing && into.Length > 0)
        {
            into[count++] = new LeakStream.Point(exit, exitRadius, 0.0, 0.0);
        }

        foreach (Parcel p in _parcels)
        {
            if (count >= into.Length) break;

            // A tail cut loose is measured from where its liquid would have been had it stayed whole.
            along = count == 0 ? p.Age * p.LeftAt : along + Vec.Len(p.Position - previous);
            previous = p.Position;

            double speed = Vec.Len(p.Velocity);
            double radius = exitRadius * Math.Sqrt(p.LeftAt / Math.Max(speed, p.LeftAt > 0.0 ? p.LeftAt : 1.0));
            into[count++] = new LeakStream.Point(p.Position, radius, along, p.Age);
        }

        return count;
    }
}
