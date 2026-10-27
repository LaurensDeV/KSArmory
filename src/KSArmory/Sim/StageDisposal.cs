namespace KSArmory;

/// <summary>
/// Whether a stage a vehicle has shed may be taken out of the world.
///
/// <para>Throughput: <c>sim rate = 33.3 ms / frame time</c>, and frame time grows about 2.0 ms per vehicle
/// (<c>docs/METRE-LEVEL.md</c> §5b). A rocket sheds four vehicles and keeps two.</para>
///
/// <para>The stack <see cref="SeparationClearance"/> and <see cref="ProximityWatch"/> read is never
/// disposable: unreadable falls back to a blind clock. So this covers the three ascent stages.</para>
/// </summary>
internal static class StageDisposal
{
    /// <summary>
    /// How far a shed stage has to have got before it is disposed of, in metres: a margin against taking
    /// something adjacent to the craft for the stage, which a rocket under power clears in seconds.
    /// </summary>
    /// <remarks>
    /// No guard against another rocket's stages: the census's one-frame window is. Over 62,013 removals
    /// none was seen alive again.
    /// </remarks>
    public const double ClearOfTheCraftMetres = 1000.0;

    /// <param name="enabled"><see cref="Config.DisposeSpentStages"/>, off by default.</param>
    /// <param name="watchedByTheClearance">
    /// Whether this is the half the separation clearance is still reading. Never disposable.
    /// </param>
    /// <param name="metresFromTheCraft">
    /// How far the stage has got, or NaN when it cannot be read, which is never disposable.
    /// </param>
    public static bool MayDispose(bool enabled, bool watchedByTheClearance, double metresFromTheCraft)
    {
        if (!enabled || watchedByTheClearance) return false;

        return double.IsFinite(metresFromTheCraft)
               && metresFromTheCraft >= ClearOfTheCraftMetres;
    }
}
