using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// One optical director, crewed on the craft carrying it.
///
/// <para>It finds its own targets and drives the player's view, with no weapon involved. That is
/// the whole difference from the head this replaces: a launcher's optic could only ever watch what
/// its own fire control was tracking, so a craft with no launcher had no sight and a craft with a
/// launcher had exactly one.</para>
///
/// <para>Implements <see cref="IOpticalHead"/>, which is the seam the sight, the chase camera and
/// the claim ladder already read. None of them needed changing when the head moved out from under
/// the launcher, which is the whole reason that interface exists.</para>
/// </summary>
internal sealed class OpticalHead(Config config, OpticConfig policy) : IOpticalHead
{
    private readonly Config _config = config;
    private readonly OpticConfig _policy = policy;

    private readonly List<(Part, OpticProfile)> _scratch = [];

    /// <summary>This head's own settings, which no weapons system shares.</summary>
    public OpticConfig Policy => _policy;

    /// <summary>Which director this is, and what it sees with.</summary>
    public OpticProfile Profile { get; private set; } = Arsenal.EoDirector;

    public SensorProfile Sensor { get; private set; } = Arsenal.EoSensor;

    /// <summary>The craft it is bolted to. Pinned on creation; a head does not re-home.</summary>
    public Vehicle? Platform { get; private set; }

    public double3 PlatformEcl { get; private set; }

    /// <summary>Which director on that craft, by part order rather than by reference.</summary>
    public int Ordinal { get; private set; }

    /// <summary>The director part, or null once it is no longer fitted.</summary>
    public Part? Director { get; private set; }

    /// <summary>The gimballed head. <see cref="IOpticalHead.OpticPart"/> is this.</summary>
    public Part? OpticPart { get; private set; }

    /// <summary>
    /// Where the base is, now. <see cref="MountFrame.Fixed"/> for a director bolted to a hull,
    /// and wherever a traverse or a hinge has carried it for one that rides something.
    ///
    /// <para>Read through to the part on every use rather than cached, so there is no stale copy
    /// to reason about. That is safe because exactly one thing writes a mount — the drive that
    /// owns it, from <c>WeaponSystem.Update</c> — and that runs before every head's
    /// <see cref="Update"/> and before anything draws. Reads either side of it all agree.</para>
    /// </summary>
    public MountFrame Mount
        => Director is { } director ? OpticParts.MountOf(director, Profile) : MountFrame.Fixed;

    /// <summary>Its own set. A director is a sensor in its own right, not a weapon's eye.</summary>
    public Radar Radar { get; } = new(config, policy);

    /// <summary>
    /// Local "up" at the director, which is what keeps the sight's horizon level and the camera
    /// the right way up. <see cref="IOpticalHead.Boresight"/>'s contract, and <em>not</em> where
    /// the set is looking — a director bolted to a hull's side has a mount normal pointing
    /// sideways, and a horizon drawn against that lies by however far the two differ.
    /// </summary>
    public double3 Boresight { get; private set; } = new(0, 0, 1);

    /// <summary>
    /// Where its search volume points, in Ecl: out of the surface it is bolted to.
    ///
    /// <para>Separate from <see cref="Boresight"/> because they answer different questions. This
    /// is the direction the sensor sweeps about; that one is which way is up.</para>
    /// </summary>
    public double3 SensorBoresight { get; private set; } = new(0, 0, 1);

    /// <summary>
    /// What the camera rolls against: the head's own up unless levelling is asked for.
    ///
    /// <para>Rigid with the head is the default because it is what a camera bolted to a craft
    /// does — it rolls with the vehicle and looking sideways stays sideways. It also has no
    /// singularity worth the name: the head's up is built to stay near the mount's normal and is
    /// continuous everywhere the travel allows, so there is nothing to carry and nothing to
    /// flip.</para>
    /// </summary>
    public double3 RollReferenceEcl { get; private set; } = new(0, 0, 1);

    /// <summary>
    /// What it is watching — the best contact on scope, not the best one a weapon may shoot.
    /// A director is an instrument: a passer-by that will never close is exactly the thing an
    /// operator wants the picture on, and a gun is right to ignore it.
    /// </summary>
    public Track? LockedTrack => Radar.Watched;

    /// <summary>True once the head has caught up with what it was told to look at.</summary>
    public bool OpticOnTarget => _drive.OnTarget && _driveWorks;

    // Where the head is actually looking, in the director's own part frame.
    private readonly PointingDrive _drive = new();

    // The drives' clock. The engine's step carries the display's frame pacing, which would turn
    // the head three times as far on alternate frames while the hull it sits on does not.
    private readonly SmoothedStep _driveStep = new();

    // Latched, so a head the engine has stopped accepting writes for cannot go on reporting that
    // it is on target while the picture points somewhere else.
    private bool _driveWorks = true;

    public OpticalHead(Config config, OpticConfig policy, Vehicle platform, int ordinal)
        : this(config, policy)
    {
        Platform = platform;
        Ordinal = ordinal;
    }

    /// <summary>Reads where the world is, once a frame, before anything is drawn against it.</summary>
    public void SampleWorld()
    {
        if (Platform is not { IsDisposed: false } platform)
        {
            Director = null;
            OpticPart = null;
            return;
        }

        PlatformEcl = KsaWorld.PositionEcl(platform);

        if (OpticParts.FindNth(platform, Ordinal, _scratch) is { } found)
        {
            Director = found.Part;
            Profile = found.Profile;
            Sensor = Arsenal.SensorNamed(Profile.Sensor);
            OpticPart = OpticParts.FindHead(found.Part, found.Profile);
        }
        else
        {
            Director = null;
            OpticPart = null;
        }

        SensorBoresight = ResolveSensorBoresight();
        Boresight = Platform is { } up ? KsaWorld.LocalUp(up) : Boresight;
        RollReferenceEcl = ResolveRollReference();
    }

    /// <summary>Scans, slews and writes the head's transform. One simulated step.</summary>
    public void Update(double dt, IReadOnlyList<IContact>? airborne = null)
    {
        if (Platform is not { IsDisposed: false } platform || Director is null) return;

        Radar.Sensor = Sensor;
        Radar.Scan(platform, SensorBoresight, dt, airborne);

        // AimPartFrame first: it is what works out how hard the cursor is commanding, and the
        // rate below is that command.
        double3 aim = AimPartFrame();

        _drive.Update(_driveStep.Next(dt), aim, Profile.SlewRateRad * _mouseRate);

        // The travel again, on what the drive actually reached. Clamping only the command leaves
        // the head free to take the shortest rotation between two legal directions, and between
        // opposite bearings at low elevation that arc goes straight through the mast.
        _drive.Hold(OpticGeometry.ClampToTravel(Profile, Mount, _drive.Direction));

        if (OpticPart is not { } head || !_driveWorks) return;

        if (!OpticParts.TryApplyAim(head, Profile, Mount, _drive.Direction))
        {
            _driveWorks = false;
            Log.Warn("optic: the engine refused the head's transform; it is frozen where it stopped");
        }
    }

    /// <summary>Clears the refusal latch, because a new craft deserves a fresh assessment.</summary>
    public void Reset() => _driveWorks = true;

    /// <summary>
    /// Where the head is looking from and along what, both in Ecl. False when the director, its
    /// head or the pose cannot be resolved — the caller then draws and drives nothing rather than
    /// pointing a camera at the origin.
    /// </summary>
    public bool TryOpticViewEcl(out double3 eyeEcl, out double3 forwardEcl)
        => TryOpticViewEclAt(PlatformEcl, out eyeEcl, out forwardEcl);

    /// <summary>
    /// The same view, resolved against a platform position sampled by the caller.
    ///
    /// <para>For the one caller that runs inside the engine's viewport pass, where "now" is a
    /// different instant from the mod's own sample and the difference is a frame of the planet's
    /// motion.</para>
    /// </summary>
    public bool TryOpticViewEclAt(double3 platformEcl, out double3 eyeEcl, out double3 forwardEcl)
    {
        eyeEcl = forwardEcl = Vec.Zero;

        if (Platform is not { } platform || Director is not { } director) return false;

        if (!OpticParts.TryViewEcl(platform, director, Profile, Mount, _drive.Direction, platformEcl,
                                           out eyeEcl, out forwardEcl))
        {
            return false;
        }

        // While the head is settled it is tracking, so the view is re-solved onto the target's own
        // position at this instant rather than left along an axis turned to a frame ago. Same rule,
        // and the same reason, as the launcher's head had.
        if (!_drive.OnTarget || Radar.Locked is not { } locked) return true;
        if (!locked.Contact.TryDrawEgo(out double3 ego)) return true;
        if (!KsaWorld.TryEgoToEcl(ego, out double3 drawnEcl)) return true;

        double3 toTarget = drawnEcl - eyeEcl;
        if (Vec.Len2(toTarget) > 1.0) forwardEcl = Vec.Unit(toTarget);

        return true;
    }

    // Where the head is told to look, in the director's own part frame. Clamped to its travel
    // here rather than at the drive, so the drive's own settling is measured against a command it
    // can actually reach -- otherwise a head told to look through its mount slews to the floor and
    // reports itself forever unsettled.
    private double3 AimPartFrame()
    {
        double3 rest = OpticGeometry.RestDirection;

        // Mouse aim owns the head outright rather than being the first of several rungs: with it
        // on the operator is the sensor, so falling through to tracking -- or to the rest
        // direction -- would swing the head away the moment the cursor stopped commanding
        // anything. Holding is where it already is, which is the drive's own direction.
        if (_policy.MouseAim)
        {
            return TryCursorAimPartFrame(out double3 cursorFrame)
                ? OpticGeometry.ClampToTravel(Profile, Mount, cursorFrame)
                : _drive.Direction;
        }

        if (_policy.Manual)
        {
            return OpticGeometry.ClampToTravel(Profile, Mount, ManualAim());
        }

        if (!_policy.Tracking || Platform is not { } platform || Director is null) return rest;

        // From the head's own pivot, not from the part's origin. The two are 0.63 m apart, which
        // is a tenth of the picture at a few hundred metres -- see WeaponSystem.OpticOriginEcl,
        // which is the same correction for the same reason.
        if (Radar.Watched is not { } locked) return rest;

        if (!LauncherPart.TryPartPointEcl(platform, Director, Mount.ToPart(Profile.HeadPivot), PlatformEcl,
                                          out double3 pivotEcl))
        {
            return rest;
        }

        double3 targetEcl = locked.PositionEcl;
        if (locked.Contact.TryDrawEgo(out double3 ego) && KsaWorld.TryEgoToEcl(ego, out double3 drawn))
        {
            targetEcl = drawn;
        }

        return LauncherPart.TryDirectionToPartFrame(platform, Director, targetEcl - pivotEcl,
                                                    out double3 partFrame)
            ? OpticGeometry.ClampToTravel(Profile, Mount, partFrame)
            : rest;
    }

    // How much of the slew rate the cursor is asking for, from the last aim. One, and so no
    // limit of its own, whenever the head is not being dragged.
    private double _mouseRate = 1.0;

    // Where the cursor points, in the director's own part frame. False unless mouse aim is on and
    // the cursor is over a viewport whose camera gives a usable ray.
    //
    // From the head's pivot rather than from the part's origin, and as a *bearing* rather than a
    // ray: a cursor gives a direction from the camera, which coincides with the head only while
    // the head is driving the view. Watching a site from the orbit camera and pointing at
    // something on the ground is the case where the two differ, and there they differ by tens of
    // degrees. KsaWorld.TryCursorAimEcl resolves the ray to a point and does the subtraction.
    private bool TryCursorAimPartFrame(out double3 partFrame)
    {
        partFrame = default;

        if (!_policy.MouseAim || Platform is not { } platform || Director is not { } director)
        {
            return false;
        }

        // At rest in the middle of its own picture, and slower the nearer to resting it is.
        // Without the first the head chases a cursor that its own turning keeps off centre and
        // the view never settles; without the second it goes from still to full rate in a pixel.
        // Only while this head drives the view: pointing at a site from an orbit camera has no
        // such loop and no reason for either.
        _mouseRate = 1.0;

        if (_policy.Viewport == KsaWorld.MainViewportIndex)
        {
            if (!KsaWorld.TryCursorFromViewCentre(_policy.MouseDeadZonePx, out float2 fromCentre,
                                                  out bool commands, out float halfHeight)
                || !commands)
            {
                return false;
            }

            // Measured from the ring's edge, so the command starts at nothing wherever the ring
            // is drawn -- and against what is left of the screen, so the same drag means the same
            // thing at any window size or rest area.
            _mouseRate = CursorAim.CommandStrength(fromCentre, _policy.MouseDeadZonePx,
                                                   Math.Max(1f, halfHeight - _policy.MouseDeadZonePx));
        }

        if (!LauncherPart.TryPartPointEcl(platform, director, Mount.ToPart(Profile.HeadPivot), PlatformEcl,
                                          out double3 pivotEcl))
        {
            return false;
        }

        return KsaWorld.TryCursorAimEcl(pivotEcl, out double3 dirEcl)
               && LauncherPart.TryDirectionToPartFrame(platform, director, dirEcl, out partFrame);
    }

    // Bearing and elevation about the mount, for driving it by hand.
    private double3 ManualAim()
    {
        double bearing = float.DegreesToRadians(_policy.ManualBearingDeg);
        double elevation = float.DegreesToRadians(_policy.ManualElevationDeg);

        double across = Math.Cos(elevation);

        return new double3(Math.Sin(elevation), across * Math.Cos(bearing), across * Math.Sin(bearing));
    }

    // The head's own up, carried into Ecl -- or local vertical when the operator wants the
    // picture levelled. Falls back to local up rather than a zero vector, which the controller
    // reads as "no opinion" and would hand to KSA's rule.
    private double3 ResolveRollReference()
    {
        if (Platform is not { } platform) return RollReferenceEcl;

        if (_policy.StabiliseHorizon) return Boresight;

        double3 headUp = OpticGeometry.Rotation(Mount, _drive.Direction) * OpticGeometry.MountNormal;

        return Director is { } director
               && LauncherPart.TryLauncherDirectionEcl(platform, director, headUp, out double3 ecl)
            ? ecl
            : KsaWorld.LocalUp(platform);
    }

    // Out of the surface it is bolted to, which for a director on a deck is the sky. Every failure
    // falls back to local up rather than a zero vector: a cone with no direction sees nothing.
    private double3 ResolveSensorBoresight()
    {
        if (Platform is not { } platform) return SensorBoresight;

        if (Director is { } director
            && LauncherPart.TryLauncherDirectionEcl(platform, director, Mount.Normal,
                                                    out double3 ecl))
        {
            return ecl;
        }

        return KsaWorld.LocalUp(platform);
    }
}
