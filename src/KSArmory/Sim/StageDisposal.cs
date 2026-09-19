namespace KSArmory;

/// <summary>
/// Whether a stage a vehicle has shed may be taken out of the world.
///
/// <para><b>This is throughput, not tidiness.</b> The engine's simulated step is capped at a
/// thirtieth of a second however long a frame takes, so <c>sim rate = 33.3 ms / frame time</c> and
/// frame time grows at about 2.0 ms per vehicle — `docs/METRE-LEVEL.md` §5b. One rocket sheds four
/// vehicles and keeps two, so on a world flying several of them the spent stages are most of what
/// is being simulated, and they are simulated for the whole coast while they fall.</para>
///
/// <para><b>The stack the clearance is watching is never disposable</b>, whatever it costs.
/// <see cref="SeparationClearance"/> and <see cref="ProximityWatch"/> both read the distance to it,
/// and an unreadable distance is not "clear" — it falls back to a blind clock, which is the trim
/// being authorised while the stack is still metres away. Disposing of it would buy one vehicle in
/// four and put the bus into the thing it just dropped.</para>
///
/// <para>So this covers the ascent stages, which nothing reads, and that is three of the four.</para>
/// </summary>
internal static class StageDisposal
{
    /// <summary>
    /// How far a shed stage has to have got before it is nothing but frame time, in metres.
    ///
    /// <para>Not a physical threshold — nothing reads an ascent stage at any distance. It is the
    /// margin against destroying the wrong vehicle: the census identifies a stage as "new in the
    /// world and nearest to the craft", and a kilometre is far enough that nothing adjacent to the
    /// craft can be mistaken for one. A rocket under power puts a dropped stage past it within
    /// seconds, so waiting for it costs nothing.</para>
    /// </summary>
    public const double ClearOfTheCraftMetres = 1000.0;

    /// <param name="enabled">Whether the operator asked for this at all. Off is the default.</param>
    /// <param name="watchedByTheClearance">
    /// Whether this is the half the separation clearance is still reading. Never disposable.
    /// </param>
    /// <param name="metresFromTheCraft">
    /// How far the stage has got, or NaN when it cannot be read. <b>Unreadable is never
    /// disposable</b> — the same rule <see cref="SeparationClearance"/> follows, and for a stronger
    /// reason: there the cost of guessing is a bad trim, here it is destroying something at random.
    /// </param>
    public static bool MayDispose(bool enabled, bool watchedByTheClearance, double metresFromTheCraft)
    {
        if (!enabled || watchedByTheClearance) return false;

        return double.IsFinite(metresFromTheCraft)
               && metresFromTheCraft >= ClearOfTheCraftMetres;
    }
}
