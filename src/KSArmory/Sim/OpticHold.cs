namespace KSArmory;

/// <summary>
/// Why a head is not looking at the contact its sight is bracketing.
///
/// <para><b>The bracket and the aim are two different questions, and the sight draws both.</b> A
/// director brackets whatever its sensor is holding; it only *slews* onto that contact if
/// something told it to. So a head sitting at rest with a contact tracked paints a bracket well
/// off the boresight and looks, from the picture alone, like a camera that has stopped working —
/// which is exactly what a quiet gate always looks like. This is the director's half of
/// <see cref="FireHold"/>: the reason, in the operator's words, drawn where they are already
/// looking.</para>
/// </summary>
internal readonly record struct OpticHold(string Reason)
{
    /// <summary>The head is on it, and there is nothing to say.</summary>
    public static readonly OpticHold None = new(string.Empty);

    /// <summary>Whether there is a reason worth drawing.</summary>
    public bool Holds => Reason.Length > 0;
}

/// <summary>What decides that, kept apart from the drawing so it can be tested.</summary>
internal static class OpticFollow
{
    /// <summary>
    /// How far off the view axis a contact may sit and still count as being looked at.
    ///
    /// <para>Generous on purpose. A drive settles inside its own deadband and a magnified picture
    /// makes a fraction of a degree obvious, so a tighter figure would nag through every normal
    /// engagement; the state this is for is a head pointing somewhere else entirely.</para>
    /// </summary>
    public const double OnAxisDeg = 3.0;

    /// <summary>
    /// The reason, or <see cref="OpticHold.None"/>.
    ///
    /// <para>The order is not a preference — it mirrors the precedence in
    /// <c>OpticalHead.AimPartFrame</c>, which takes the mouse first, then the sliders, then a
    /// designation, then the tracking switch. A reason listed in any other order names a rung the
    /// head never reached, which is worse than saying nothing.</para>
    /// </summary>
    /// <param name="onAxis">The contact is within <see cref="OnAxisDeg"/> of where the head looks.</param>
    /// <param name="settled">The drive has stopped moving.</param>
    public static OpticHold Why(bool onAxis, bool mouseAim, bool manual, bool designated,
                                bool tracking, bool settled)
    {
        if (onAxis) return OpticHold.None;

        if (mouseAim) return new OpticHold("MOUSE AIM");
        if (manual) return new OpticHold("AIMED BY HAND");
        if (designated) return new OpticHold("WATCHING ELSEWHERE");
        if (!tracking) return new OpticHold("NOT TRACKING");

        // Told to follow this one and not on it. Still moving is the ordinary case; stopped short
        // means the gimbal has run out of travel, which is the answer to "why will it not come
        // round any further".
        return settled ? new OpticHold("GIMBAL LIMIT") : new OpticHold("SLEWING");
    }
}
