namespace KSArmory;

/// <summary>What a click on the world does to the list of places a bus is aimed at.</summary>
internal enum TargetClick
{
    /// <summary>Take it as the whole list, and start the shot over.</summary>
    Designate,

    /// <summary>Put it after the ones already chosen, and leave the flight alone.</summary>
    Add,
}

/// <summary>
/// When a bus's target list may be edited, and what an edit is allowed to move.
///
/// <para>Designating resets the whole program: right before launch, and an un-flown bus half an hour
/// into its coast.</para>
/// </summary>
internal static class TargetEdit
{
    /// <remarks>
    /// <para>An empty list is a designation at any phase. During the coast an add is a divert stop on
    /// a fixed lead; before cutoff the reach is the release epoch's
    /// (<see cref="DivertFootprint.TryAtTheEpoch"/>) and the lead is re-elected as targets are placed.</para>
    ///
    /// <para>An add needs a reach that can refuse it; with none a click designates, since a set nothing
    /// priced is one the release loop then refuses whole.</para>
    /// </remarks>
    /// <param name="reachIsKnown">
    /// Whether there is a region to test the click against — <see cref="ReachDisplay.HasRegion"/>.
    /// </param>
    public static TargetClick ClickDoes(int targets, IcbmPhase phase, bool reachIsKnown)
        => targets > 0 && reachIsKnown && phase != IcbmPhase.NoSolution
               ? TargetClick.Add
               : TargetClick.Designate;

    /// <summary>
    /// Whether the booster's aim may still be moved onto a different entry of the list.
    /// </summary>
    /// <remarks>
    /// Only before the arrival is committed: until then guidance re-solves against the vehicle's state,
    /// so a move costs propellant, not accuracy. After, the bus diverts the whole way: 5-45 km against
    /// 140 m (<c>docs/MIRV-NEXT.md</c> 8z).
    /// </remarks>
    public static bool LeadMayMove(IcbmPhase phase, bool arrivalCommitted)
        => phase != IcbmPhase.Coast && !arrivalCommitted;

    /// <remarks>
    /// Never the lead: the arc, the aim correction and the trim are solved against it, so replacing it
    /// is a new shot, which is what designating is.
    /// </remarks>
    public static bool MayRemove(int index, int targets, int lead)
        => index >= 0 && index < targets && index != lead;

    /// <summary>How many warheads the list is planned against.</summary>
    /// <remarks>
    /// The magazine reloads a few seconds after a salvo, so once one has gone the salvo's size is the
    /// count. A rack not yet read is taken as full, so a scenario designating on its first frame still
    /// takes every warhead.
    /// </remarks>
    public static int WarheadsAboard(int loaded, int salvoSize)
        => salvoSize > 0 ? salvoSize
         : loaded > 0 ? loaded
         : TargetSet.MaxTargets;
}
