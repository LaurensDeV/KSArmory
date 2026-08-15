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

    /// <summary>
    /// Point the head wherever the cursor is.
    ///
    /// <para>Ahead of everything else, including the tracking switch: with this on the operator
    /// <em>is</em> the sensor, so needing to turn tracking off first would be surprising. The
    /// drive stays rate-limited either way, so this aims <em>towards</em> the cursor rather than
    /// snapping to it, and the travel limits still apply.</para>
    /// </summary>
    public bool MouseAim;

    /// <summary>
    /// How far from the middle of the view the cursor must be before it moves the head (px).
    ///
    /// <para>Zero would make a millimetre of offset a standing order to drift, because a head
    /// driving its own picture chases the cursor and carries the view with it. See
    /// <see cref="CursorAim.OutsideDeadZone"/>.</para>
    /// </summary>
    public float MouseDeadZonePx = 60f;

    /// <summary>Drive the head by hand instead of from its own sensor.</summary>
    /// <summary>
    /// Whether this head's terrain map is open.
    ///
    /// <para>Its own rather than the session's, because the map is drawn around <em>this</em> head
    /// and marked with what <em>this</em> head can see. Two directors on one craft looking at
    /// different things want two maps, not one that changes under them.</para>
    /// </summary>
    public bool MapOpen;

    /// <summary>
    /// How far across the map is (m). A detent from <see cref="TerrainMap.Spans"/>; the panel
    /// steps it rather than offering a slider, for the reason the sight's magnification does.
    /// </summary>
    public float MapSpanMetres = 2000f;

    public bool Manual;
    public float ManualBearingDeg;
    public float ManualElevationDeg = 10f;

    /// <summary>Which viewport this head draws into. -1 is off; the main view is the only one
    /// that renders a planet, so a secondary is for watching rather than aiming.</summary>
    public int Viewport = -1;

    /// <summary>A factor on whatever field the player already had, so the same setting is the
    /// same instrument to two people with different preferences. See <see cref="SightZoom"/>.</summary>
    public float Magnification = 1f;

    /// <summary>Draw the sight's own symbology over the view the head is driving.</summary>
    public bool Symbology = true;

    /// <summary>
    /// Hold the picture level against the site's own vertical, rather than taking KSA's roll.
    ///
    /// <para><b>Off by default</b>, because a camera bolted to a craft rolls with it: looking
    /// sideways stays sideways, and nothing re-levels the picture underneath the operator. That is
    /// also the version with no singularity — the head's own up is continuous everywhere its
    /// travel reaches.</para>
    ///
    /// <para>On holds the picture against the site's true vertical, which is what a ground site
    /// wants and what makes a horizon reference mean something. The cost is near the vertical:
    /// world up is a poor roll reference there, so the roll is carried from frame to frame and the
    /// picture can come out inverted after passing through. That is the trade, and it is why this
    /// is a switch.</para>
    /// </summary>
    public bool StabiliseHorizon;
}
