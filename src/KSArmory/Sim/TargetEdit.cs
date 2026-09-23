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
/// <para>These are the rules the panel and the world-click actuate, and each is a state transition
/// that can be wrong in flight: designating resets the whole program, which is right for a shot
/// nobody has launched and would un-fly a bus half an hour into its coast.</para>
/// </summary>
internal static class TargetEdit
{
    /// <summary>What clicking the world does, given what the list holds and where the flight is.</summary>
    /// <remarks>
    /// <para>An empty list is a designation at any phase, because there is no shot to disturb.</para>
    ///
    /// <para><b>Both sides of cutoff add, and they are not the same add.</b> During the coast the
    /// reach is the columns the bus has flown and the lead is fixed — the arc is committed, so a
    /// later target is a divert stop and nothing else. Before cutoff the reach is the release
    /// epoch's (<see cref="DivertFootprint.TryAtTheEpoch"/>) and the aim is still a free variable,
    /// so the lead is re-elected as targets are placed and the booster ends up flying to the
    /// farthest — which is the one thing <c>ReleaseLoop</c> cannot arrange for itself.</para>
    ///
    /// <para><b>An add needs a reach that can refuse it.</b> With none, a click designates, which is
    /// what every click did before the region existed: adding a target nothing can price is how a
    /// set is assembled that the release loop then refuses whole.</para>
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
    /// <b>Before the arrival is committed, and not one frame after.</b> Until then the aim is a free
    /// variable: guidance re-solves velocity-to-be-gained against the vehicle's actual state every
    /// cycle, so moving it costs propellant and not accuracy. Once the arrival is latched the arc is
    /// pinned to an instant chosen for somewhere else, and after cutoff there is no engine left —
    /// the bus would have to divert the whole way, which is the 5–45 km ending rather than the 140 m
    /// one.
    /// </remarks>
    public static bool LeadMayMove(IcbmPhase phase, bool arrivalCommitted)
        => phase != IcbmPhase.Coast && !arrivalCommitted;

    /// <summary>Whether an entry may be taken out of the list.</summary>
    /// <remarks>
    /// Never the lead. The booster flew to it and the arc, the aim correction and the trim are all
    /// solved against it, so replacing it is a new shot rather than an edit — and a new shot is what
    /// designating is. Removing it would move the aim with none of that reset behind it. It is the
    /// lead rather than the first chosen because the release schedule may fly them in another order.
    /// </remarks>
    public static bool MayRemove(int index, int targets, int lead)
        => index >= 0 && index < targets && index != lead;

    /// <summary>How many warheads the list is planned against.</summary>
    /// <remarks>
    /// The magazine reloads a few seconds after a salvo, so its loaded count only says what the bus
    /// carries until the first warhead has gone; the salvo's own size is the truth after that. A rack
    /// nobody has read yet is taken as full, so a designation made before the magazine has been
    /// sampled — which is what a scenario does on its first frame — still takes every warhead.
    /// </remarks>
    public static int WarheadsAboard(int loaded, int salvoSize)
        => salvoSize > 0 ? salvoSize
         : loaded > 0 ? loaded
         : TargetSet.MaxTargets;
}
