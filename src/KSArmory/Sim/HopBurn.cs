using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// One hop flown on the bus's main engine (<see cref="IcbmConfig.HopsBurnTheEngine"/>): turned onto the velocity the hop
/// asks for, burnt until what is left is the nozzles' to take, and turned back onto the line the warheads leave along.
/// The trim, the passes and the release wait while it <see cref="Holds"/>; <c>docs/WIDE-SPREAD.md</c> step 5, C.
/// </summary>
/// <remarks>
/// The engine pushes along the nose alone, so the burn steers the nose onto the velocity still to gain each frame and
/// stops on the part of it the nose can still deliver. The arc is solved again every <see cref="ResolveEverySeconds"/>
/// from where the burn has carried the bus: a burn is not an impulse, and the bus drifts off an arc solved at its start.
/// </remarks>
internal sealed class HopBurn
{
    /// <summary>A hop asking no more than this is the nozzles', as every hop was before.</summary>
    public const double EngineAboveMetresPerSecond = BusTrim.MaxMetresPerSecond;

    /// <summary>What the burn leaves for the nozzles, which null it on the way into the passes.</summary>
    public const double LeaveForTheNozzlesMetresPerSecond = 1.0;

    public const double ResolveEverySeconds = 2.0;

    /// <summary>
    /// What turning onto a hop and back adds to its slot (s): twice the 9.0 s the post-boost bus took to settle a 164 deg
    /// turn onto the entry airflow, every one of 48 flown on 2026-10-17 and 18, after <see cref="ClearSeconds"/>.
    /// </summary>
    public const double TurnsSeconds = ClearSeconds + 18.0;

    /// <summary>
    /// How far the warhead just released must be from the bus before it turns (m): twice the 4.4 m its other warheads
    /// sweep about its centre -- tubes 2.64 m up the axis and a 1.8 m Mk 21 -- so the turn cannot carry them through it.
    /// </summary>
    public const double ClearMetres = 10.0;

    /// <summary>
    /// What waiting for <see cref="ClearMetres"/> adds to a hop's slot (s), at the Mk 21's 0.5 m/s separation kick.
    /// </summary>
    public const double ClearSeconds = 20.0;

    /// <summary>The longest either turn may take before the engine is given up on or the line is held where it got.</summary>
    public const double TurnCapSeconds = 60.0;

    /// <summary>
    /// How far the velocity still to gain may swing off the nose while burning before the burn stops: past it the
    /// engine is pushing the bus somewhere the hop is not asking for.
    /// </summary>
    public const double OffLineDegrees = 20.0;

    internal enum Stage { Idle, Pending, Pointing, Burning, TurningBack, Done }

    /// <summary>What the bus is asked to do this frame: the line to hold, and whether the engine burns.</summary>
    internal readonly record struct Decision(double3 LineCci, bool EngineOn, bool ResolveNow);

    private readonly BusTurn _turn = new();
    private double3 _backToCci;
    private double3 _lineCci;
    private double _sinceResolve;

    public Stage Now { get; private set; } = Stage.Idle;

    /// <summary>Whether anything that needs the bus still and on its line must wait.</summary>
    public bool Holds => Now is Stage.Pending or Stage.Pointing or Stage.Burning or Stage.TurningBack;

    /// <summary>Whether the engine flew this hop, which is false for one the nozzles took.</summary>
    public bool Burnt { get; private set; }

    public double AskedMetresPerSecond { get; private set; } = double.NaN;

    public double BurnSeconds { get; private set; }

    /// <summary>How long the two turns took between them (s).</summary>
    public double TurnedSeconds { get; private set; }

    /// <summary>The velocity still to gain when the engine stopped; NaN until it has.</summary>
    public double LeftMetresPerSecond { get; private set; } = double.NaN;

    /// <summary>Why it ended where it did, for the line a hop writes.</summary>
    public string Said { get; private set; } = "";

    /// <summary>
    /// A hop handed over: decided on the first <see cref="Update"/>, which sees the arc solved for it.
    /// </summary>
    /// <param name="backToCci">The line to turn back onto once the engine stops; zero holds wherever the burn ended.</param>
    public void Begin(double3 backToCci)
    {
        _backToCci = Vec.Unit(backToCci);
        _lineCci = Vec.Zero;
        _sinceResolve = 0.0;
        Burnt = false;
        AskedMetresPerSecond = double.NaN;
        BurnSeconds = 0.0;
        TurnedSeconds = 0.0;
        LeftMetresPerSecond = double.NaN;
        Said = "";
        _turn.Reset();
        Now = Stage.Pending;
    }

    public void Reset()
    {
        _turn.Reset();
        Now = Stage.Idle;
    }

    /// <summary>One frame.</summary>
    /// <param name="toGainCci">The velocity the arc asks for less the bus's own, now.</param>
    /// <param name="noseCci">The nose the engine reports; zero is a frame that would not resolve.</param>
    /// <param name="accelerationMetresPerSecond2">What the engine pushes the bus at now.</param>
    /// <param name="releasedMetres">How far the warhead just released is from the bus; NaN where none is.</param>
    public Decision Update(double stepSeconds, double3 toGainCci, double3 noseCci, double accelerationMetresPerSecond2,
                           double releasedMetres = double.NaN)
    {
        double toGain = Vec.Len(toGainCci);

        switch (Now)
        {
            case Stage.Pending:
                AskedMetresPerSecond = toGain;
                if (!(toGain > EngineAboveMetresPerSecond) || !(accelerationMetresPerSecond2 > 0.0))
                {
                    Finish(toGain > 0.0 ? $"{toGain:F1} m/s is the nozzles'" : "nothing to gain");
                    return Holding();
                }

                if (releasedMetres < ClearMetres) return Holding();

                _lineCci = Vec.Unit(toGainCci);
                _turn.Begin(_lineCci, TurnCapSeconds);
                Now = Stage.Pointing;
                return new Decision(_lineCci, false, false);

            case Stage.Pointing:
                TurnedSeconds += Math.Max(0.0, stepSeconds);
                if (_turn.Update(stepSeconds, noseCci) == BusTurn.Stage.Turning) return new Decision(_lineCci, false, false);

                if (_turn.Now == BusTurn.Stage.Capped)
                {
                    Finish($"not on the hop's line after {TurnCapSeconds:F0} s, so the nozzles fly it");
                    return Holding();
                }

                Now = Stage.Burning;
                Burnt = true;
                goto case Stage.Burning;

            case Stage.Burning:
            {
                double3 nose = Vec.Unit(noseCci);
                double along = nose.Equals(Vec.Zero) ? toGain : Vec.Dot(toGainCci, nose);
                double off = nose.Equals(Vec.Zero) || !(toGain > 0.0)
                                 ? 0.0
                                 : double.RadiansToDegrees(Vec.AngleBetween(nose, toGainCci));
                double next = Math.Max(0.0, accelerationMetresPerSecond2) * Math.Max(0.0, stepSeconds);

                if (along - next <= LeaveForTheNozzlesMetresPerSecond || off > OffLineDegrees)
                {
                    LeftMetresPerSecond = toGain;
                    Said = off > OffLineDegrees
                               ? $"stopped {off:F0} deg off what is left to gain"
                               : $"stopped with {toGain:F2} m/s for the nozzles";
                    return TurnBack();
                }

                BurnSeconds += Math.Max(0.0, stepSeconds);
                _sinceResolve += Math.Max(0.0, stepSeconds);
                bool resolve = _sinceResolve >= ResolveEverySeconds;
                if (resolve) _sinceResolve = 0.0;

                if (toGain > 0.0) _lineCci = Vec.Unit(toGainCci);
                return new Decision(_lineCci, true, resolve);
            }

            case Stage.TurningBack:
                TurnedSeconds += Math.Max(0.0, stepSeconds);
                if (_turn.Update(stepSeconds, noseCci) == BusTurn.Stage.Turning) return new Decision(_backToCci, false, false);
                if (_turn.Now == BusTurn.Stage.Capped) _backToCci = _turn.ReachedCci;
                Now = Stage.Done;
                return Holding();

            default:
                return Holding();
        }
    }

    // Where the bus ends up pointing once the hop is no longer steering it.
    private Decision Holding() => new(_backToCci, false, false);

    private Decision TurnBack()
    {
        if (_backToCci.Equals(Vec.Zero))
        {
            Now = Stage.Done;
            return Holding();
        }

        _turn.Begin(_backToCci, TurnCapSeconds);
        Now = Stage.TurningBack;
        return new Decision(_backToCci, false, false);
    }

    private void Finish(string why)
    {
        Said = why;
        Now = Stage.Done;
    }
}
