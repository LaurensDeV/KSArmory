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

    // Where a burst was, in the frame of the body it happened over. The fallback with no body is the
    // craft that fired it, wrong in the same way but far less so than the ecliptic.
    private object? _burstBody;
    private double3 _burstAnchor;
    private Vehicle? _anchor;
    private double3 _anchorOffset;

    // The weapon that fired it, asked where the round's body is drawn, so the camera and the mesh
    // come out of one expression.
    private IEffectSource? _source;
    private Vehicle? _platform;

    /// <summary>Points this at a round on the weapon that fired it, or at nothing.</summary>
    public void Track(IProjectile? round, IEffectSource? source)
    {
        _round = round;
        _source = round is null ? null : source;
        _platform = round is null ? null : source?.Platform;
        _burstBody = null;
        _anchor = null;
    }

    /// <summary>Holds where the round went off, for looking at the burst.</summary>
    /// <param name="burstEcl">
    /// The burst carried to the end of the frame, which is the instant the ground is anchored at —
    /// never the round's own position, which is taken at the instant inside the step it went off.
    /// </param>
    public void HoldAgainst(Vehicle? platform, IProjectile round, double3 burstEcl)
    {
        _round = null;
        LastPositionEcl = burstEcl;

        // Body-fixed: against the launching craft the hold flies away with it, and as a bare ecliptic
        // point the planet leaves it at ~29.8 km/s. It also survives the launching craft dying.
        _burstBody = KsaWorld.TryAnchorToGround(burstEcl, out object? body, out double3 anchor)
                     ? body
                     : null;
        _burstAnchor = anchor;

        _anchor = _burstBody is null ? platform : null;
        _anchorOffset = round.OffsetFromPlatform;
    }

    /// <summary>
    /// Holds wherever it is now, fixed to the ground under that point, with nothing tracked.
    ///
    /// <para>For a view handed back when the craft it was taken from has been destroyed.</para>
    /// </summary>
    public void HoldWhereItIs()
    {
        double3 here = GetPositionEcl();

        _round = null;
        _source = null;
        _platform = null;
        _anchor = null;

        _burstBody = KsaWorld.TryAnchorToGround(here, out object? body, out double3 anchor) ? body : null;
        _burstAnchor = anchor;
    }

    /// <summary>
    /// Holds a stated point, fixed to the ground under it, with nothing tracked: where a chase stops
    /// to watch its round arrive. Against the craft when no body will take it, as a burst is.
    /// </summary>
    public void HoldAt(Vehicle? platform, double3 pointEcl)
    {
        _round = null;
        _source = null;
        _platform = null;
        LastPositionEcl = pointEcl;

        _burstBody = KsaWorld.TryAnchorToGround(pointEcl, out object? body, out double3 anchor) ? body : null;
        _burstAnchor = anchor;

        _anchor = _burstBody is null ? platform : null;
        _anchorOffset = platform is not null ? pointEcl - KsaWorld.PositionEcl(platform) : Vec.Zero;
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
    /// Where the round's body is <em>drawn</em> — the one expression, shared with the mesh.
    ///
    /// <para><b>The camera is not trying to be right; it is trying to be paired</b> with the mesh,
    /// through <see cref="IEffectSource.TryRoundEffectEcl"/>. Correcting one side alone is worse than
    /// leaving both wrong. CLAUDE.md, "And what RoundFollowable reports".</para>
    ///
    /// <para>It must be the same answer all frame: the engine converts through
    /// <see cref="GetPositionEclFromCce"/> at phases the mod does not choose. This moves by the round's
    /// own flight; <c>round.PositionEcl</c> moves by a step of the ecliptic's ~29.8 km/s.</para>
    /// </summary>
    public double3 GetPositionEcl()
    {
        // Inside the engine's own frame pass: the last good answer rather than an exception.
        try
        {
            if (_round is { } round && _source is { } source
                && source.TryRoundEffectEcl(round, out double3 drawn))
            {
                LastPositionEcl = drawn;
            }
            else if (_round is { } fallback && _platform is { } craft && KsaWorld.IsAlive(craft))
            {
                LastPositionEcl = KsaWorld.PositionEcl(craft) + fallback.OffsetFromPlatform;
            }
            else if (_round is { } loose)
            {
                LastPositionEcl = loose.PositionEcl;
            }
            else if (_burstBody is not null
                     && KsaWorld.TryGroundAnchorEcl(_burstBody, _burstAnchor,
                                                    out double3 burst, out _))
            {
                LastPositionEcl = burst;
            }
            else if (_anchor is { } platform)
            {
                LastPositionEcl = KsaWorld.PositionEcl(platform) + _anchorOffset;
            }
        }
        catch
        {
        }

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
