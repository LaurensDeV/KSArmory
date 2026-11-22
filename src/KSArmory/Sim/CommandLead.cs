using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// An attitude command carried ahead along its own turn, so a tracker that follows a ramp from behind ends up on it.
/// KSA's attitude hold takes a direction and no rate, and on 5901 the nose trailed a 13-14 deg/s swing by a steady
/// 11-13 deg: a type-1 loop on a ramp, whose lag is the rate times a time constant.
/// <para>A commanded step is not led. A ramp's rate is smooth from frame to frame; a step's is one frame of an
/// unbounded rate, and leading it would throw the nose past where it was sent.</para>
/// </summary>
public sealed class CommandLead
{
    /// <summary>A turn faster than this in one frame is a step in the command, which resets the estimate.</summary>
    public const double StepDegPerSecond = 40.0;

    /// <summary>The most the command is ever carried ahead.</summary>
    public const double MaxLeadDeg = 15.0;

    private const double SmoothingSeconds = 0.5;

    private double3 _last;
    private double3 _rate;
    private bool _primed;
    private double _fromRadPerSecond;

    /// <summary>The smoothed turn rate of the command, rad/s, about the axis it points along.</summary>
    public double3 RateRadPerSecond => _rate;

    public void Reset()
    {
        _last = default;
        _rate = default;
        _primed = false;
    }

    /// <summary>
    /// The command carried ahead by <paramref name="leadSeconds"/> of its own smoothed turn, after <paramref name="dt"/>
    /// seconds of simulated time since the last call, eased in between half and the whole of
    /// <paramref name="fromDegPerSecond"/> of turn so a slow turn is not led. Zero lead, a zero or unfinite step, or a
    /// direction that cannot be normalised returns the command unchanged.
    /// </summary>
    public double3 Lead(double3 commanded, double dt, double leadSeconds, double fromDegPerSecond = 0.0)
    {
        _fromRadPerSecond = Math.Max(fromDegPerSecond, 0.0) * Math.PI / 180.0;
        double3 now = Vec.Unit(commanded);
        if (now.Equals(Vec.Zero)) return commanded;

        if (!_primed || !(dt > 0.0) || !double.IsFinite(dt))
        {
            if (!_primed) _rate = default;
            _last = now;
            _primed = true;
            return Apply(now, leadSeconds);
        }

        double angle = Vec.AngleBetween(_last, now);
        double3 axis = Vec.Unit(Vec.Cross(_last, now));
        double3 frameRate = axis.Equals(Vec.Zero) ? default : axis * (angle / dt);
        _last = now;

        if (angle / dt > StepDegPerSecond * Math.PI / 180.0)
        {
            _rate = default;
            return now;
        }

        double blend = 1.0 - Math.Exp(-dt / SmoothingSeconds);
        _rate += (frameRate - _rate) * blend;
        return Apply(now, leadSeconds);
    }

    private double3 Apply(double3 now, double leadSeconds)
    {
        if (!(leadSeconds > 0.0)) return now;

        // Only the part of the rate that turns this direction; a component along it would roll, which a direction lacks.
        double3 turning = _rate - now * Vec.Dot(_rate, now);
        double rate = Vec.Len(turning);
        if (!(rate > 0.0)) return now;

        double share = _fromRadPerSecond > 0.0 ? Ramp.Smooth(0.5 * _fromRadPerSecond, _fromRadPerSecond, rate) : 1.0;
        double lead = Math.Min(rate * leadSeconds * share, MaxLeadDeg * Math.PI / 180.0);
        if (!(lead > 0.0)) return now;
        double3 axis = turning * (1.0 / rate);
        double3 ahead = Vec.Unit(Vec.Cross(axis, now));
        return Vec.Unit(now * Math.Cos(lead) + ahead * Math.Sin(lead));
    }
}
