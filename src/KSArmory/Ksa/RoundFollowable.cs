using Brutal.Numerics;
using KSA;
using KSArmory.Sim;

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

    /// <summary>Points this at a round, or at nothing.</summary>
    public void Track(IProjectile? round)
    {
        _round = round;
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

    /// <summary>The round's analytic position, as <c>Vehicle.GetPositionEcl</c> also reports.</summary>
    public double3 GetPositionEcl()
    {
        if (_round is { } round) LastPositionEcl = round.PositionEcl;
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

    public void DrawAxes(Viewport viewport)
    {
    }
}
