namespace KSArmory;

/// <summary>What the closest approach to a discarded stage was, and whether it was too close.</summary>
/// <param name="MetresApart">
/// The closest the two ever came, or <see cref="double.PositiveInfinity"/> if no reading was ever
/// taken. Infinity and zero mean opposite things, so the unread case cannot be a number.
/// </param>
/// <param name="AtSeconds">How long after the split that happened.</param>
/// <param name="KeepOutMetres">What it needed to stay outside, as of that closest reading.</param>
/// <param name="Readings">How many frames contributed. Zero is the whole of "this says nothing".</param>
internal readonly record struct ClosestApproach(double MetresApart, double AtSeconds,
                                                double KeepOutMetres, int Readings)
{
    /// <summary>Whether anything was ever measured. A watch with no readings makes no claim.</summary>
    public bool Known => Readings > 0 && double.IsFinite(MetresApart);

    /// <summary>Whether the bus came inside the distance a released store would be scored against.</summary>
    public bool Breached => Known && MetresApart < KeepOutMetres;

    /// <summary>One line for the log, and the same line whether or not anything went wrong.</summary>
    public string Said =>
        !Known
            ? "closest approach to the spent stack: never read"
            : $"closest approach to the spent stack: {MetresApart:F1} m at +{AtSeconds:F1} s, "
              + $"keep-out {KeepOutMetres:F1} m"
              + (Breached ? " -- INSIDE THE KEEP-OUT" : "");
}

/// <summary>
/// How near the bus ever came to the stage it dropped, measured every frame of the coast.
///
/// <para><b>Measurement, not protection.</b> Nothing reads this to decide anything — that is
/// <see cref="SeparationClearance"/>'s job, and the interlock inside <see cref="BusTrim"/>'s. This
/// exists because a collision has happened once and was <em>inferred</em> from a thrashing trim
/// rather than observed: on 2026-08-25 a clearance latch let the trim run on a stale reading and
/// the bus hit its own spent stack, and the only trace was 28 s of direction changes ending in
/// <c>nothing left aboard moves the bus</c>. A shot that grazes the stack and survives leaves no
/// trace at all.</para>
///
/// <para>So it runs on every flight whether or not anything is wrong, and reports one line. What
/// makes it worth the frame is that the interesting number is a <em>minimum over the whole
/// coast</em>, which no sample taken at the end can recover and no gate consulted at the start
/// ever sees.</para>
/// </summary>
internal sealed class ProximityWatch
{
    private double _closest = double.PositiveInfinity;
    private double _at;
    private double _keepOut = double.NaN;
    private double _elapsed;
    private int _readings;

    /// <summary>What the watch has seen so far, which is the whole of its output.</summary>
    public ClosestApproach Closest => new(_closest, _at, _keepOut, _readings);

    /// <summary>Forget everything. A new split is a new pair of bodies.</summary>
    public void Reset()
    {
        _closest = double.PositiveInfinity;
        _at = 0.0;
        _keepOut = double.NaN;
        _elapsed = 0.0;
        _readings = 0;
    }

    /// <param name="metresApart">
    /// How far apart the two are, or NaN when the discarded stage cannot be read. An unreadable
    /// frame advances the clock and contributes no reading — the same rule
    /// <see cref="SeparationClearance"/> follows, and for the same reason: a part tree mid-rebuild
    /// answers with nothing, and recording that as a distance of zero would report a collision on
    /// every flight.
    /// </param>
    /// <param name="stageRadiusMetres">
    /// The discarded stage's own bounding sphere, or NaN. The keep-out is derived from it exactly
    /// as the clearance gate derives what it waits for, so the two cannot drift apart and disagree
    /// about what "too close" means.
    /// </param>
    public void Update(double stepSeconds, double metresApart, double stageRadiusMetres)
    {
        if (double.IsFinite(stepSeconds) && stepSeconds > 0.0) _elapsed += stepSeconds;

        if (!double.IsFinite(metresApart) || metresApart < 0.0) return;

        _readings++;

        if (metresApart >= _closest) return;

        _closest = metresApart;
        _at = _elapsed;
        _keepOut = KeepOutFor(stageRadiusMetres);
    }

    /// <summary>
    /// The distance a released store would be scored against, which is what "too close" has to
    /// mean. Same derivation as <see cref="SeparationClearance"/>'s, so a change to one is a change
    /// to both.
    /// </summary>
    public static double KeepOutFor(double stageRadiusMetres) =>
        double.IsFinite(stageRadiusMetres) && stageRadiusMetres > 0.0
            ? stageRadiusMetres + SeparationClearance.ClearOfTheSphereMetres
            : SeparationClearance.FallbackMetres;
}
