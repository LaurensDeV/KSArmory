using Brutal.Numerics;

namespace KSArmory;

/// <summary>Whether the thing that let go has got far enough away to act, and what to say about it.</summary>
internal readonly record struct Clearance(bool IsClear, bool OnTheClock, string Said,
                                         bool Abandoned = false);

/// <summary>
/// Whether a vehicle has coasted far enough from the stage it just dropped to start manoeuvring.
///
/// <para><b>The decoupler's shove is the separation velocity</b>, so a <see cref="BusTrim"/> that nulls
/// it at once leaves the pair co-moving a stack length apart for the whole coast.</para>
///
/// <para>Measured rather than timed wherever the stage can be read: how fast two halves part depends
/// on the decoupler's impulse and the halves' masses, which nothing in this mod knows.</para>
///
/// <para><b>Clearance is never latched.</b> Once this answers clear the trim runs and nulls the
/// separation, so the pair can close again and every pass has to ask.</para>
/// </summary>
internal static class SeparationClearance
{
    /// <summary>
    /// How much further than the discarded stage's own bounding sphere counts as clear.
    ///
    /// <para>That sphere is what the coarse contact test uses, so a store released inside it can be
    /// scored against the stage; beyond it the stores open the gap themselves.</para>
    /// </summary>
    public const double ClearOfTheSphereMetres = 10.0;

    /// <summary>When the stage's own size cannot be read, which is most of the first frames.</summary>
    public const double FallbackMetres = 25.0;

    /// <summary>
    /// The most it will wait for that, deliberately short: a held salvo arrives on an aim correction
    /// tuned for an earlier release point. Flown, a 90 s hold on a shot whose cutoff prediction was
    /// 0.1 km put the release probe 6.8 km out.
    /// </summary>
    public const double TimeoutSeconds = 20.0;

    /// <summary>What a coast must leave the trim before the release, once it waits for the stack to open.</summary>
    public const double TrimNeedsSeconds = 30.0;

    /// <summary>
    /// How long past the expected release the gap must still hold: the passes can outrun their estimate, and a gap
    /// sized to reach the keep-out exactly at the release was crossed 0.1 m inside it in <c>PastCutoffTests</c>.
    /// </summary>
    public const double PastTheReleaseSeconds = 20.0;

    /// <summary>
    /// How far a bus must have coasted from its stack before it pays what the trim owes, when paying it closes the
    /// pair: far enough that, closing at the speed the trim leaves, they reach the keep-out no sooner than the release.
    /// Zero when the trim opens the pair or the release is not known.
    ///
    /// <para>A bus braking nose-retrograde has its stack directly behind it, so what it owes its solution points back
    /// at the stack. Flown from a 300 km orbit, 0.73 and 1.04 m/s owed brought the pair to 10.6 m and into contact ~30 s
    /// after the split; 0.27-0.57 m/s did not. <c>docs/ICBM-OUTSTANDING.md</c> 1.8.</para>
    /// </summary>
    public static double ForTheTrimMetres(double keepOutMetres, double closingAfterTrim, double secondsToRelease)
        => closingAfterTrim > 0.0 && double.IsFinite(keepOutMetres) && double.IsFinite(secondsToRelease)
           && secondsToRelease > 0.0
               ? keepOutMetres + (closingAfterTrim * (secondsToRelease + PastTheReleaseSeconds))
               : 0.0;

    /// <summary>
    /// The same distance from the pair's geometry: the bus's position and velocity relative to the stack, what the trim
    /// owes, and the time to the release. Zero inside <see cref="TrimNeedsSeconds"/> of the release, which must leave the
    /// trim time to fly.
    /// </summary>
    public static double ForTheTrimMetres(double keepOutMetres, double3 fromStackCci, double3 relativeCci,
                                          double3 trimOwesCci, double secondsToRelease)
    {
        if (!(secondsToRelease > TrimNeedsSeconds)) return 0.0;
        if (!Vec.IsFinite(fromStackCci) || !Vec.IsFinite(relativeCci) || fromStackCci.Equals(Vec.Zero)) return 0.0;

        double closing = -Vec.Dot(relativeCci + trimOwesCci, Vec.Unit(fromStackCci));
        return ForTheTrimMetres(keepOutMetres, closing, secondsToRelease);
    }

    /// <param name="metresApart">
    /// How far apart the two are, or NaN when the stage cannot be read. Unreadable falls back to the
    /// clock, never to clear: a part tree mid-rebuild reads like a stage that has gone.
    /// </param>
    /// <param name="stageRadiusMetres">
    /// The stage's bounding sphere, the one the coarse contact test uses, or NaN when unreadable.
    /// </param>
    public static Clearance Check(double metresApart, double stageRadiusMetres, double secondsSinceSplit,
                                  double forTheTrimMetres = 0.0)
    {
        double wanted = double.IsFinite(stageRadiusMetres) && stageRadiusMetres > 0.0
                            ? stageRadiusMetres + ClearOfTheSphereMetres
                            : FallbackMetres;

        bool known = double.IsFinite(metresApart) && metresApart >= 0.0;
        bool late = secondsSinceSplit >= TimeoutSeconds;

        if (known && metresApart >= wanted && metresApart < forTheTrimMetres)
        {
            return new Clearance(false, OnTheClock: false,
                                 $"letting the spent stack open to {forTheTrimMetres:F0} m before trimming back "
                                 + $"toward it, {metresApart:F1} m now");
        }

        if (known && metresApart >= wanted)
        {
            return new Clearance(true, OnTheClock: false,
                                 $"clear of the spent stack at {metresApart:F0} m "
                                 + $"after {secondsSinceSplit:F0} s");
        }

        // Out of patience and still too close: give up the trim, not the distance. Trimming here
        // nulls the shove and drives the bus back into the stack; releasing untrimmed costs kilometres,
        // hitting the stack costs the shot. Only a readable gap can make that trade.
        if (late && known && metresApart < wanted)
        {
            // One decimal: at F0 a near miss of the gate reads "still 15 m ... inside the 15 m".
            return new Clearance(false, OnTheClock: true,
                                 $"still {metresApart:F1} m from the spent stack after "
                                 + $"{secondsSinceSplit:F0} s, which is inside the {wanted:F1} m it "
                                 + "needs -- releasing without trimming rather than manoeuvring into it",
                                 Abandoned: true);
        }

        if (late)
        {
            return new Clearance(true, OnTheClock: true,
                                 $"going ahead with no clearance reading after {secondsSinceSplit:F0} s");
        }

        return new Clearance(false, OnTheClock: !known,
                             known
                                 ? $"waiting to clear the spent stack, {metresApart:F1} m of {wanted:F1}"
                                 : "waiting to clear the spent stack, which cannot be read");
    }
}
