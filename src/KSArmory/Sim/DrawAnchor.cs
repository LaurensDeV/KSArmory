using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Maps world geometry into the render frame, reconciling the two different instants involved.
///
/// <para><b>The invariant, which is easy to break and impossible to see on screen until it is:</b>
/// the two fields are sampled at <i>different times on purpose</i>.</para>
///
/// <list type="bullet">
/// <item><see cref="Ego"/> — where the platform is being drawn <b>this frame</b>.</item>
/// <item><see cref="Ecl"/> — the platform position the geometry was measured against, captured
/// during the simulation update, i.e. <b>one update earlier</b>.</item>
/// </list>
///
/// <para><see cref="ToEgo"/> is <c>Ego + (posEcl - Ecl)</c>, which carries geometry from the
/// older epoch onto the current render position. The gap between the two instants is exactly the
/// frame's ecliptic motion — near Earth about 500 m at 60 fps — and differencing against the
/// older reference is what cancels it.</para>
///
/// <para><b>Do not "simplify" this by using one instant for both.</b> Re-reading the platform at
/// draw time, or deriving <see cref="Ego"/> from <see cref="Ecl"/>, both look like tidier code and
/// both put the whole overlay 500 m beside the launcher.</para>
///
/// <para>Kept free of KSA types so the mapping can be tested headlessly — see
/// <c>DrawAnchorTests</c>.</para>
/// </summary>
internal readonly struct DrawAnchor(double3 ego, double3 ecl)
{
    /// <summary>The platform's position in the render frame, sampled this frame.</summary>
    public double3 Ego { get; } = ego;

    /// <summary>The platform's ecliptic position at the instant the geometry was measured.</summary>
    public double3 Ecl { get; } = ecl;

    public bool IsValid => Vec.IsFinite(Ego) && Vec.IsFinite(Ecl);

    /// <summary>Maps an ecliptic position from the geometry's epoch into the render frame.</summary>
    public double3 ToEgo(double3 posEcl) => Ego + (posEcl - Ecl);
}
