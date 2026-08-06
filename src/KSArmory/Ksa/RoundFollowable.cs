using Brutal.Numerics;
using KSA;
using KSArmory.Sim;

namespace KSArmory;

/// <summary>
/// A round, presented to the engine as something a camera can follow.
///
/// <para>The engine asks whatever the camera follows for its position during its own frame pass,
/// so following the round directly is what removes the whole class of bug that comes from a mod
/// computing a camera offset in one pass and the engine applying it in another. Nothing else here
/// does any work: the position is the round's, and every other member is the least the interface
/// will accept.</para>
///
/// <para>Modelled on <c>KSA.WreckageMarker</c>, which is the engine's own proof that an
/// <see cref="IFollowable"/> need not be a vehicle, need not be registered anywhere, and can be
/// handed straight to <c>Camera.SetFollow</c>.</para>
/// </summary>
internal sealed class RoundFollowable : IFollowable
{
    // Not zero: the engine divides by it when a camera changes focus, and a NaN camera is not a
    // recoverable state. A metre is about the size of the thing being watched.
    private const double Radius = 1.0;

    private readonly OrbitView _orbitView = new(CameraReferenceFrame.Stars);

    private IProjectile? _round;

    // What to hold still against, once the round has gone off. Not an ecliptic position: that
    // frame carries ~29.8 km/s that the whole world shares, so a camera pinned to a point in it
    // watches the ground slide away. Held against the platform instead, which moves with
    // everything else.
    private Vehicle? _anchor;
    private double3 _anchorOffset;

    /// <summary>Points this at a round, or at nothing.</summary>
    public void Track(IProjectile? round)
    {
        _round = round;
        _anchor = null;
    }

    /// <summary>
    /// Stops following the round and holds where it was, relative to the craft that fired it.
    ///
    /// <para>For looking at a burst. The position stays put on the ground rather than in the
    /// ecliptic, which is the difference between the view holding still and the world sliding
    /// out from under it.</para>
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
    /// The round's analytic position — the same thing <c>Vehicle.GetPositionEcl</c> reports, so
    /// the camera sits in the frame everything else here is computed in.
    /// </summary>
    public double3 GetPositionEcl()
    {
        if (_round is { } round) LastPositionEcl = round.PositionEcl;
        else if (_anchor is { } platform) LastPositionEcl = KsaWorld.PositionEcl(platform) + _anchorOffset;

        return LastPositionEcl;
    }

    public double3 GetVelocityEcl() => _round?.VelocityEcl ?? Vec.Zero;

    public double3 GetPositionEclFromCce(double3 positionCce) => GetPositionEcl() + positionCce;

    public double3 GetPositionCceFromEcl(double3 positionEcl) => positionEcl - GetPositionEcl();

    /// <summary>
    /// Identity, so a camera offset stays in ecliptic axes rather than turning with the round.
    /// </summary>
    public doubleQuat GetBodyFixed2Ecl() => doubleQuat.Identity;

    public double3 GetBodyRates() => double3.Zero;

    // Null rather than a made-up frame. Both are only read for a followable that is a Vehicle,
    // which this is not, and inventing one would be a claim about orientation this cannot support.
    public doubleQuat? GetEnu2Cce() => null;

    public doubleQuat? GetLvlh2Cce() => null;

    public bool IsMoon() => false;

    public bool IsStar() => false;

    public bool HasOrbit() => false;

    public void DrawAxes(Viewport viewport)
    {
    }
}
