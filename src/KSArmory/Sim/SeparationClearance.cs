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
    /// Far enough, in metres.
    ///
    /// <para>Sized against what gets released afterwards rather than against the vehicle: a store
    /// leaves its tube at a couple of metres a second and a spent stack's own bounding sphere is
    /// tens of metres across, so the standoff has to beat the coarse contact test rather than merely
    /// look separated. At the ~1.1 m/s a stock 3 m decoupler gives a six-tonne bus, this is about
    /// thirty-five seconds of a release window that runs to minutes.</para>
    /// </summary>
    public const double Metres = 50.0;

    /// <summary>
    /// The most it will wait for that.
    ///
    /// <para>A discarded stage that barely moved — a heavy one, or a decoupler with little in it —
    /// would otherwise hold the whole salvo for ever, and the release window closes on altitude
    /// rather than on this. Going ahead untrimmed is a worse shot; not shooting is no shot.</para>
    /// </summary>
    public const double TimeoutSeconds = 90.0;

    /// <param name="metresApart">
    /// How far apart the two are, or NaN when the discarded stage cannot be read.
    ///
    /// <para><b>Unreadable falls back to the clock, never to "clear".</b> A part tree mid-rebuild
    /// answers with nothing, which is indistinguishable from a stage that has genuinely gone — and
    /// reading that as clearance is the one case this exists to prevent, because it fires the
    /// manoeuvre at the instant the two are closest rather than at the moment they are furthest.
    /// </para>
    /// </summary>
    public static Clearance Check(double metresApart, double secondsSinceSplit)
    {
        bool known = double.IsFinite(metresApart) && metresApart >= 0.0;
        bool late = secondsSinceSplit >= TimeoutSeconds;

        if (known && metresApart >= Metres)
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
                                       + $"which stopped {Metres:F0} m short after {secondsSinceSplit:F0} s"
                                     : $"going ahead with no clearance reading after {secondsSinceSplit:F0} s");
        }

        return new Clearance(false, OnTheClock: !known,
                             known
                                 ? $"waiting to clear the spent stack, {metresApart:F0} m of {Metres:F0}"
                                 : "waiting to clear the spent stack, which cannot be read");
    }
}
