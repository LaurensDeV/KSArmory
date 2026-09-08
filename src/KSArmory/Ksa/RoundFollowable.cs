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

    // Where a burst was, in the frame of the body it happened over -- the only description of a
    // place that does not move. The fallback for a burst no body could be found for is the craft
    // that fired it, which is wrong in the same way but far less so than the ecliptic.
    private object? _burstBody;
    private double3 _burstAnchor;
    private Vehicle? _anchor;
    private double3 _anchorOffset;

    // The weapon that fired it, asked where the round's body is DRAWN. Not for its own sake: what
    // matters is that the camera and the mesh come out of one expression.
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
    public void HoldAgainst(Vehicle? platform, IProjectile round)
    {
        _round = null;
        LastPositionEcl = round.PositionEcl;

        // Body-fixed, because a burst happens over ground and ground turns. Against the launching
        // craft instead it flies away with it, which for a rocket still under thrust is hundreds
        // of metres across a three-second linger; as a bare ecliptic point the planet leaves it
        // behind at ~29.8 km/s, which is 89 km over the same three seconds.
        _burstBody = KsaWorld.TryAnchorToGround(round.PositionEcl, out object? body,
                                                out double3 anchor)
                     ? body
                     : null;
        _burstAnchor = anchor;

        _anchor = _burstBody is null ? platform : null;
        _anchorOffset = round.OffsetFromPlatform;
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
    /// <para><b>The camera is not trying to be right; it is trying to be paired.</b> Nothing here
    /// can be correct in absolute terms: the mod's reading of the round is a step behind the world
    /// the engine has just advanced, a craft's analytic orbit position is not where its parts are
    /// drawn, and the gap between those two opens and closes as the craft goes off rails and back
    /// — which is what lighting an engine does. None of that is visible. What is visible is the
    /// separation between the eye and the mesh, so every one of those terms cancels the moment
    /// both sides come out of <see cref="IEffectSource.TryRoundEffectEcl"/>, which is the call the
    /// body placement itself uses.</para>
    ///
    /// <para>Correcting one side alone is worse than leaving both wrong, and that is not a
    /// figure of speech: a term added here to put the camera in the engine's epoch takes it
    /// <em>away</em> from the mesh, which stayed in the mod's. The plume and the tracer hang on
    /// this same call for the same reason — a flame has to sit on the body rather than near it.</para>
    ///
    /// <para>It must also be the same answer all frame. <see cref="GetPositionEclFromCce"/> and
    /// <see cref="GetPositionCceFromEcl"/> come through here too and the engine converts at phases
    /// the mod does not choose, so an answer that moves when the mod steps disagrees with itself
    /// inside one frame. This one moves by the round's own flight, which is metres;
    /// <c>round.PositionEcl</c> would move by a whole step of the ecliptic's ~29.8 km/s, about
    /// 500 m, which puts the round out of the viewport entirely.</para>
    /// </summary>
    public double3 GetPositionEcl()
    {
        // Inside the engine's own frame pass, where an exception is the game rather than a log
        // line. The last good answer is always a better outcome than that.
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
            // Held where it was.
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
