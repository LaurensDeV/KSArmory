namespace KSArmory;

/// <summary>
/// One director's own settings — what <em>this</em> head is doing.
///
/// <para>Separate from <see cref="SystemConfig"/> rather than folded into it, because a head is no
/// longer part of a weapons system. A craft can carry a director and no armament at all, and a
/// craft can carry two launchers and one director; neither shape works if the head's settings are
/// a weapon's.</para>
///
/// <para>The test is the same one <c>SystemConfig</c> applies: whether two of them could sensibly
/// disagree. Two directors in one world can disagree about where they are looking and how far in
/// their optics are wound, so all of that is here. What they cannot disagree about — the roster of
/// team names, what gets drawn — stays on <see cref="Config"/>.</para>
/// </summary>
public sealed class OpticConfig : ISensorPolicy
{
    /// <summary>
    /// Who this head considers what. It finds its own targets, so it needs its own answer.
    ///
    /// <para>Its own rather than borrowed from a weapons system on the same craft: a director is
    /// an instrument, and pointing one at a friendly to look at it is a normal thing to do.</para>
    /// </summary>
    public IffPolicy Iff { get; } = new();

    /// <summary>Never look at the vehicle the player is flying.</summary>
    public bool ProtectControlledVehicle;

    bool ISensorPolicy.ProtectControlledVehicle => ProtectControlledVehicle;

    /// <summary>
    /// Slew the head onto what it is holding. Off parks it looking along its own mount, which is
    /// also the fallback when the engine refuses the transform write.
    /// </summary>
    public bool Tracking = true;

    /// <summary>Drive the head by hand instead of from its own sensor.</summary>
    public bool Manual;
    public float ManualBearingDeg;
    public float ManualElevationDeg = 10f;

    /// <inheritdoc cref="SystemConfig.OpticViewport"/>
    public int Viewport = -1;

    /// <inheritdoc cref="SystemConfig.OpticMagnification"/>
    public float Magnification = 1f;

    /// <summary>Draw the sight's own symbology over the view the head is driving.</summary>
    public bool Symbology = true;

    /// <summary>
    /// Hold the picture level against the site's own vertical, rather than taking KSA's roll.
    ///
    /// <para>On by default because a sight that rolls with the ecliptic is disorienting to aim
    /// through: the engine derives up by crossing the view with the camera frame's +Z, so a site
    /// well off that pole gets a permanently canted horizon.</para>
    ///
    /// <para>Off is not a lesser setting. Stabilising has a real cost near the vertical, where a
    /// world up makes a poor roll reference and the picture is held by carrying the previous
    /// frame's — so it stays smooth and can come out inverted after passing through. KSA's own
    /// rule never does that, and on a craft that manoeuvres hard it can be the one you want.</para>
    /// </summary>
    public bool StabiliseHorizon = true;
}
