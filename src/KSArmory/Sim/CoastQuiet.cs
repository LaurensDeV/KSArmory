namespace KSArmory;

/// <summary>
/// Whether to stop commanding a coasting bus's attitude for a while, and when to take it back.
///
/// <para>KSA puts a vehicle off rails for as long as an actuator is <em>commanded</em>, and off
/// rails it is integrated rather than propagated as a conic. A bus sharing a physics bubble is
/// harmless on rails — 224 to 241 coast probes of a divergent world cost nothing — and the free
/// ride ends the moment this mod's own hold commands a thruster: ~4 m/s per probe of
/// non-gravitational push, 90% across the plane, which walks the impact to 88 km.</para>
///
/// <para><b>The window is bounded at both ends, and flying it without either bound cost 89x on the
/// worlds that were already healthy.</b> Both bounds are about something that reads the attitude
/// while nothing is holding it:</para>
///
/// <list type="bullet">
/// <item><description><b>Not until the correction has finished.</b> The trim resolves onto the
/// vehicle's own control axes, taking the attitude to <em>be</em> the release line, so a bus left
/// to drift between passes thrusts along stale axes and the loop stops converging — measured as
/// the <c>clock</c> terminator on 55 of 56 flights against 8 for the control.</description></item>
/// <item><description><b>Not into the release approach.</b> The release sequence waits for the bus
/// to be <em>steady</em>, and a bus nobody is holding is perfectly steady while aimed somewhere
/// wrong.</description></item>
/// </list>
///
/// <para>Neither bound costs much of what the quiet is for: a coast to release runs ~980 s, the
/// correction is over inside 120 of them, and the margin is 60 — so ~80% of the exposure is still
/// quiet. And taking the line back is cheap precisely because going quiet does not move the bus:
/// on rails is exact propagation, so the drift is attitude and nothing else.</para>
/// </summary>
internal sealed class CoastQuiet
{
    private bool _quiet;

    /// <summary>Whether the hold is currently letting go.</summary>
    public bool IsQuiet => _quiet;

    /// <summary>
    /// Whether the release is near enough that the line has to be back under command.
    ///
    /// <para>An absent margin is an absent answer rather than "not yet": a shot with no committed
    /// arrival is not approaching a release, and reading an unknown as plenty of time would keep a
    /// bus quiet through its own deployment.</para>
    /// </summary>
    public static bool InReleaseApproach(double secondsToApproach, double marginSeconds)
        => !double.IsFinite(secondsToApproach) || secondsToApproach <= marginSeconds;

    /// <summary>
    /// Whether to be quiet this frame.
    ///
    /// <para>Latched with a band rather than a threshold: a bus settled to a hundredth of a degree
    /// would otherwise re-command every time the error crossed it, which is the actuator being
    /// commanded again and the whole cost back.</para>
    /// </summary>
    public bool Update(in CoastQuietState now, double quietDeg, double reacquireDeg)
    {
        if (!now.Enabled || !now.Coasting || now.Burning || now.Trimming || now.SalvoAway
            || !now.CorrectionFinished || now.InReleaseApproach
            || !double.IsFinite(now.PointingErrorDeg))
        {
            _quiet = false;
            return false;
        }

        _quiet = now.PointingErrorDeg <= (_quiet ? reacquireDeg : quietDeg);
        return _quiet;
    }

    /// <summary>Forget the latch, for a computer taking on a different flight.</summary>
    public void Reset() => _quiet = false;
}

/// <summary>What the coast hold reads to decide whether to let go.</summary>
/// <param name="Enabled">The operator asked for a quiet coast at all.</param>
/// <param name="Coasting">The program is in its coast phase.</param>
/// <param name="Burning">An engine is lit, which needs the attitude.</param>
/// <param name="Trimming">The bus trim is firing, which needs the attitude.</param>
/// <param name="SalvoAway">A warhead has left, after which there is nothing left to hold for.</param>
/// <param name="CorrectionFinished">The post-boost correction has stopped taking passes.</param>
/// <param name="InReleaseApproach">The release is near enough to need the line back.</param>
/// <param name="PointingErrorDeg">How far off the commanded attitude the bus currently is.</param>
internal readonly record struct CoastQuietState(
    bool Enabled,
    bool Coasting,
    bool Burning,
    bool Trimming,
    bool SalvoAway,
    bool CorrectionFinished,
    bool InReleaseApproach,
    double PointingErrorDeg);
