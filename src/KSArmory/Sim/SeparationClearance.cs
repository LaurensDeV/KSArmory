namespace KSArmory;

/// <summary>Whether the thing that let go has got far enough away to act, and what to say about it.</summary>
internal readonly record struct Clearance(bool IsClear, bool OnTheClock, string Said);

/// <summary>
/// Whether a vehicle has coasted far enough from the stage it just dropped to start manoeuvring.
///
/// <para><b>The decoupler's shove is the separation velocity.</b> It is the only thing carrying the
/// two halves apart, so anything that nulls it — a post-boost vehicle trimming itself back onto its
/// solution, which is what <see cref="BusTrim"/> is for — stops the separation as well. Do that
/// immediately and the pair co-move a stack length apart for the whole coast, with whatever is
/// released afterwards let go into the same few metres.</para>
///
/// <para>So the manoeuvre waits, and this is the whole of the decision. Measured rather than timed
/// wherever the discarded stage can still be read: how fast two halves part depends on the
/// decoupler's impulse and on what each half weighs, and nothing in this mod knows either.</para>
/// </summary>
internal static class SeparationClearance
{
    /// <summary>
    /// How much further than the discarded stage's own bounding sphere counts as clear.
    ///
    /// <para>Sized against the thing it is protecting against rather than picked: that sphere is
    /// what the coarse contact test uses, so a store released inside it can be scored against the
    /// stage. Beyond it, and the stores are leaving their tubes at a couple of metres a second and
    /// fly for minutes, so they open the gap themselves.</para>
    /// </summary>
    public const double ClearOfTheSphereMetres = 10.0;

    /// <summary>When the stage's own size cannot be read, which is most of the first frames.</summary>
    public const double FallbackMetres = 25.0;

    /// <summary>
    /// The most it will wait for that, and it is deliberately short.
    ///
    /// <para>Waiting is not free. The aim correction absorbs what the fall loses to drag and to real
    /// terrain, and that changes as the release point descends — so a salvo held back arrives on a
    /// correction tuned for a release it no longer is. Measured in flight: a ninety-second hold on
    /// a shot whose cutoff prediction was 0.1 km put the release probe 6.8 km out.</para>
    ///
    /// <para>A stage that barely moved therefore gets a short grace and then the salvo goes anyway.
    /// A decoupler with little in it separates at a quarter of a metre a second, which is metres in
    /// this window — going ahead that close is a worse shot than going ahead clear, and both beat
    /// arriving late on a stale correction.</para>
    /// </summary>
    public const double TimeoutSeconds = 20.0;

    /// <param name="metresApart">
    /// How far apart the two are, or NaN when the discarded stage cannot be read.
    ///
    /// <para><b>Unreadable falls back to the clock, never to "clear".</b> A part tree mid-rebuild
    /// answers with nothing, which is indistinguishable from a stage that has genuinely gone — and
    /// reading that as clearance is the one case this exists to prevent, because it fires the
    /// manoeuvre at the instant the two are closest rather than at the moment they are furthest.
    /// </para>
    /// </param>
    /// <param name="stageRadiusMetres">
    /// The discarded stage's own bounding sphere, or NaN when that cannot be read either. It is
    /// what the coarse contact test uses, so it is the distance that has to be beaten rather than
    /// any number somebody picked.
    /// </param>
    public static Clearance Check(double metresApart, double stageRadiusMetres, double secondsSinceSplit)
    {
        double wanted = double.IsFinite(stageRadiusMetres) && stageRadiusMetres > 0.0
                            ? stageRadiusMetres + ClearOfTheSphereMetres
                            : FallbackMetres;

        bool known = double.IsFinite(metresApart) && metresApart >= 0.0;
        bool late = secondsSinceSplit >= TimeoutSeconds;

        if (known && metresApart >= wanted)
        {
            return new Clearance(true, OnTheClock: false,
                                 $"clear of the spent stack at {metresApart:F0} m "
                                 + $"after {secondsSinceSplit:F0} s");
        }

        if (late)
        {
            return new Clearance(true, OnTheClock: true,
                                 known
                                     ? $"going ahead {metresApart:F0} m from the spent stack, "
                                       + $"which stopped {wanted:F0} m short after {secondsSinceSplit:F0} s"
                                     : $"going ahead with no clearance reading after {secondsSinceSplit:F0} s");
        }

        return new Clearance(false, OnTheClock: !known,
                             known
                                 ? $"waiting to clear the spent stack, {metresApart:F0} m of {wanted:F0}"
                                 : "waiting to clear the spent stack, which cannot be read");
    }
}
