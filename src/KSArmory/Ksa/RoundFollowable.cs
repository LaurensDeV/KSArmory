using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// A round, presented to the engine as something a camera can follow.
///
/// <para>The engine resolves a followed object's position in its own frame pass, so following the
/// round directly removes the mismatch from a mod computing an offset in one pass and the engine
/// applying it in another. <c>KSA.WreckageMarker</c> is the engine's own proof that an
/// <see cref="IFollowable"/> need not be a vehicle or be registered anywhere.</para>
/// </summary>
internal sealed class RoundFollowable : IFollowable
{
    // Not zero: the engine divides by it when a camera changes focus.
    private const double Radius = 1.0;

    private readonly OrbitView _orbitView = new(CameraReferenceFrame.Stars);

    private IProjectile? _round;

    // What to hold still against once the round has gone off. Not an ecliptic position: that frame
    // carries ~29.8 km/s the whole world shares, so a camera pinned to a point in it drifts.
    private Vehicle? _anchor;
    private double3 _anchorOffset;

    // The craft the round left, so its position can be resolved the way a round *body* is: this
    // is read by the engine in its own frame pass, and re-reading the platform there is what puts
    // the answer in the engine's epoch rather than the mod's.
    private Vehicle? _platform;

    /// <summary>Points this at a round on the craft that fired it, or at nothing.</summary>
    public void Track(IProjectile? round, Vehicle? platform)
    {
        _round = round;
        _platform = round is null ? null : platform;
        _anchor = null;
    }

    /// <summary>
    /// Holds where the round was, relative to the craft that fired it, for looking at a burst.
    /// </summary>
    public void HoldAgainst(Vehicle? platform, IProjectile round)
    {
        _round = null;
        _anchor = platform;
        _anchorOffset = round.OffsetFromPlatform;
        LastPositionEcl = round.PositionEcl;
    }

    /// <summary>Where the round was last seen, for when it stops existing mid-frame.</summary>
    public double3 LastPositionEcl { get; private set; }

    public string Id => "KSArmory.Round";

    public KeyHash Hash => KeyHash.Make(Id.AsSpan());

    public string Class => "KSArmoryRound";

    public double MeanRadius => Radius;

    public OrbitView OrbitView => _orbitView;

    public bool ShowAxes { get; set; }

    /// <summary>
    /// Where the round is, resolved the way a round <em>body</em> is: the platform re-read here,
    /// plus the round's offset from it.
    ///
    /// <para>Not <c>round.PositionEcl</c>. The engine calls this in its own frame pass, before the
    /// mod has stepped anything, and the mod's integrated position belongs to a different instant
    /// from every celestial and vehicle the engine has just placed. A camera on it therefore sits
    /// one simulated step out of register with the scene — 715 m on a 24 ms frame against 238 m on
    /// a 9 ms one, alternating with the display's pacing, which swings the camera's height over
    /// the ground by ±145 m every frame.</para>
    ///
    /// <para>Round bodies are placed from exactly these two terms and hold 0.0 m of drift out to
    /// 79.5 km, which is what this pairing buys.</para>
    /// </summary>
    public double3 GetPositionEcl()
    {
        if (_round is { } round && _platform is { } craft && KsaWorld.IsAlive(craft))
        {
            LastPositionEcl = KsaWorld.PositionEcl(craft) + round.OffsetFromPlatform;
        }
        else if (_round is { } loose) LastPositionEcl = loose.PositionEcl;
        else if (_anchor is { } platform) LastPositionEcl = KsaWorld.PositionEcl(platform) + _anchorOffset;

        return LastPositionEcl;
    }

    public double3 GetVelocityEcl() => _round?.VelocityEcl ?? Vec.Zero;

    public double3 GetPositionEclFromCce(double3 positionCce) => GetPositionEcl() + positionCce;

    public double3 GetPositionCceFromEcl(double3 positionEcl) => positionEcl - GetPositionEcl();

    /// <summary>Identity, so a camera offset does not turn with the round.</summary>
    public doubleQuat GetBodyFixed2Ecl() => doubleQuat.Identity;

    public double3 GetBodyRates() => double3.Zero;

    // Only read for a followable that is a Vehicle, which this is not.
    public doubleQuat? GetEnu2Cce() => null;

    public doubleQuat? GetLvlh2Cce() => null;

    public bool IsMoon() => false;

    public bool IsStar() => false;

    public bool HasOrbit() => false;

    public void DrawAxes(IViewport viewport)
    {
    }
}
