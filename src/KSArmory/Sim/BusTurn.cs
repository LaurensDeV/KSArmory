using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// One commanded turn of a bus, from the moment its new line is asked for until the nose has held on it --
/// or until the cap, which keeps whatever attitude was reached.
/// </summary>
/// <remarks>
/// Judged on the nose the engine reports, not on the command: the gates downstream of it (the release, the
/// post-boost passes) are asking whether the warheads would leave along the line, and only the nose says.
/// </remarks>
internal sealed class BusTurn
{
    /// <summary>The same band <see cref="PostBoostAim.SteadyWithinDegrees"/> holds a release direction to.</summary>
    public const double OnLineDegrees = 2.0;

    public const double SteadySeconds = 2.0;

    internal enum Stage { Idle, Turning, Settled, Capped }

    private double3 _targetCci;
    private double3 _lastNoseCci;
    private double _cap;
    private double _onLineFor;

    public Stage Now { get; private set; } = Stage.Idle;

    /// <summary>Whether anything that needs the bus pointed must still wait.</summary>
    public bool Holds => Now == Stage.Turning;

    public double ElapsedSeconds { get; private set; }

    /// <summary>The angle from nose to target last measured, degrees; NaN before any.</summary>
    public double OffDegrees { get; private set; } = double.NaN;

    /// <summary>How far the nose turned in the last step, degrees per second; NaN before two readings.</summary>
    public double RateDegPerSecond { get; private set; } = double.NaN;

    /// <summary>The angle the turn began with, degrees.</summary>
    public double StartDegrees { get; private set; } = double.NaN;

    /// <summary>When the nose first came inside the band, seconds into the turn; NaN until it has.</summary>
    public double FirstOnLineSeconds { get; private set; } = double.NaN;

    /// <summary>The nose when the cap ended the turn, which is the line to hold from then on.</summary>
    public double3 ReachedCci { get; private set; } = Vec.Zero;

    public void Begin(double3 targetCci, double capSeconds)
    {
        _targetCci = Vec.Unit(targetCci);
        _cap = capSeconds;
        _onLineFor = 0.0;
        _lastNoseCci = Vec.Zero;
        ElapsedSeconds = 0.0;
        OffDegrees = double.NaN;
        RateDegPerSecond = double.NaN;
        StartDegrees = double.NaN;
        FirstOnLineSeconds = double.NaN;
        ReachedCci = Vec.Zero;
        Now = _targetCci.Equals(Vec.Zero) ? Stage.Idle : Stage.Turning;
    }

    public void Reset() => Now = Stage.Idle;

    /// <summary>Step the turn on the nose the engine reports; a zero nose is a frame that would not resolve.</summary>
    public Stage Update(double stepSeconds, double3 noseCci)
    {
        if (Now != Stage.Turning || !(stepSeconds > 0.0)) return Now;

        ElapsedSeconds += stepSeconds;

        double3 nose = Vec.Unit(noseCci);
        if (!nose.Equals(Vec.Zero))
        {
            OffDegrees = Degrees(nose, _targetCci);
            if (double.IsNaN(StartDegrees)) StartDegrees = OffDegrees;
            if (!_lastNoseCci.Equals(Vec.Zero)) RateDegPerSecond = Degrees(nose, _lastNoseCci) / stepSeconds;
            _lastNoseCci = nose;

            if (OffDegrees <= OnLineDegrees)
            {
                if (double.IsNaN(FirstOnLineSeconds)) FirstOnLineSeconds = ElapsedSeconds;
                _onLineFor += stepSeconds;
            }
            else
            {
                _onLineFor = 0.0;
            }

            if (_onLineFor >= SteadySeconds)
            {
                Now = Stage.Settled;
                return Now;
            }
        }

        if (ElapsedSeconds >= _cap)
        {
            ReachedCci = _lastNoseCci;
            Now = Stage.Capped;
        }

        return Now;
    }

    private static double Degrees(double3 a, double3 b)
        => double.RadiansToDegrees(Math.Acos(Math.Clamp(Vec.Dot(a, b), -1.0, 1.0)));
}
