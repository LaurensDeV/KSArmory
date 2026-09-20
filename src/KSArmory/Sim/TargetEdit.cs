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
    /// Only the coast adds, so before cutoff the list can never hold more than one place and the
    /// flight is a single-target one. An empty list is a designation at any phase, because there is
    /// no shot to disturb.
    /// </remarks>
    public static TargetClick ClickDoes(int targets, IcbmPhase phase)
        => targets > 0 && phase == IcbmPhase.Coast ? TargetClick.Add : TargetClick.Designate;

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
