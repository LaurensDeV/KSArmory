using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The state a release prediction was flown from, what it said, and the aim it said it against in that
/// same frame. The mean mouth, never a tube: the probe line's miss is the aim loop's own reading.
/// </summary>
internal readonly record struct ReleaseProbe(double3 PositionCci, double3 VelocityCci,
                                             ImpactPredictor.Impact Impact, double3 TargetCci);

/// <summary>
/// The release probe a warhead's separation is solved against: its own, or — when its own finds no
/// impact — the last one of its salvo that did.
///
/// <para>Unkicked, a warhead lands where its tube's ring and the spin it was thrown with put it: 1.73 m
/// and 2.03 m from the aim, flown, beside siblings kicked to millimetres. A sibling's probe a frame
/// earlier is the same arc a frame further back, and every quantity in it is from that one instant, so
/// the spin and the travel cancel and the kick it solves lands 0.09 mm from where the warhead's own
/// would (<c>SalvoProbeTests</c>). What it cannot know is anything that moved the bus since.</para>
/// </summary>
internal sealed class SalvoProbe
{
    /// <summary>
    /// How old a borrowed probe may be, in simulated seconds.
    ///
    /// <para>Across a flown salvo the release probe's miss moved at most 3 mm a frame, so a probe this
    /// old is under 7 cm stale, and its shorter lever arm costs 1.8 mm more, against the 1.7–2.0 m an
    /// unkicked warhead lands. Past it the release has stalled on something, and whatever stalled it may
    /// have moved the bus.</para>
    /// </summary>
    public const double MaxAgeSeconds = 0.5;

    internal enum Source
    {
        /// <summary>The warhead's own probe landed.</summary>
        Own,

        /// <summary>Its own found no impact, and a sibling's is young enough to solve against.</summary>
        Borrowed,

        /// <summary>Its own found no impact, and the sibling's is older than <see cref="MaxAgeSeconds"/>.</summary>
        TooOld,

        /// <summary>Its own found no impact, and nothing earlier in the salvo did.</summary>
        None,
    }

    /// <param name="FromTube">The tube whose release the probe was flown for.</param>
    /// <param name="AgeSeconds">Simulated time since that probe was flown.</param>
    internal readonly record struct Choice(ReleaseProbe? Probe, Source Source, int FromTube, double AgeSeconds);

    private ReleaseProbe? _last;
    private int _lastTube;
    private double _age;

    /// <summary>A new salvo, or a new aim: nothing before this may be borrowed.</summary>
    public void Forget() => _last = null;

    public void Advance(double simSeconds)
    {
        if (_last is not null && simSeconds > 0.0 && double.IsFinite(simSeconds)) _age += simSeconds;
    }

    /// <param name="own">This warhead's own probe, null where it found no impact.</param>
    /// <param name="tube">The tube this warhead left.</param>
    public Choice Choose(ReleaseProbe? own, int tube)
    {
        if (own is { } fresh)
        {
            _last = fresh;
            _lastTube = tube;
            _age = 0.0;
            return new Choice(fresh, Source.Own, tube, 0.0);
        }

        if (_last is not { } last) return new Choice(null, Source.None, 0, double.NaN);

        return _age <= MaxAgeSeconds
                   ? new Choice(last, Source.Borrowed, _lastTube, _age)
                   : new Choice(null, Source.TooOld, _lastTube, _age);
    }
}
