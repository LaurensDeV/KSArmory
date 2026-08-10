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

    /// <summary>Its own set. A director is a sensor in its own right, not a weapon's eye.</summary>
    public Radar Radar { get; } = new(config, policy);

    /// <summary>Where its search volume points, in Ecl.</summary>
    public double3 Boresight { get; private set; } = new(0, 0, 1);

    /// <summary>What it is watching.</summary>
    public Track? LockedTrack => Radar.Locked;

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

        Boresight = ResolveBoresight();
    }

    /// <summary>Scans, slews and writes the head's transform. One simulated step.</summary>
    public void Update(double dt, IReadOnlyList<IContact>? airborne = null)
    {
        if (Platform is not { IsDisposed: false } platform || Director is null) return;

        Radar.Sensor = Sensor;
        Radar.Scan(platform, Boresight, dt, airborne);

        _drive.Update(_driveStep.Next(dt), AimPartFrame(), Profile.SlewRateRad);

        if (OpticPart is not { } head || !_driveWorks) return;

        if (!OpticParts.TryApplyAim(head, Profile, _drive.Direction))
        {
            _driveWorks = false;
            Log.Warn("optic: the engine refused the head's transform; it is frozen where it stopped");
        }
    }

    /// <summary>Clears the refusal latch, because a new craft deserves a fresh assessment.</summary>
    public void Reset() => _driveWorks = true;

    /// <inheritdoc cref="WeaponSystem.TryOpticViewEcl"/>
    public bool TryOpticViewEcl(out double3 eyeEcl, out double3 forwardEcl)
        => TryOpticViewEclAt(PlatformEcl, out eyeEcl, out forwardEcl);

    /// <inheritdoc cref="WeaponSystem.TryOpticViewEclAt"/>
    public bool TryOpticViewEclAt(double3 platformEcl, out double3 eyeEcl, out double3 forwardEcl)
    {
        eyeEcl = forwardEcl = Vec.Zero;

        if (Platform is not { } platform || Director is not { } director) return false;

        if (!OpticParts.TryViewEcl(platform, director, Profile, _drive.Direction, platformEcl,
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
        double3 rest = TubeGeometry.OpticRestDirection;

        if (_policy.Manual)
        {
            return OpticGeometry.ClampToTravel(Profile, ManualAim());
        }

        if (!_policy.Tracking || Platform is not { } platform || Director is null) return rest;

        // From the head's own pivot, not from the part's origin. The two are 0.63 m apart, which
        // is a tenth of the picture at a few hundred metres -- see WeaponSystem.OpticOriginEcl,
        // which is the same correction for the same reason.
        if (Radar.Locked is not { } locked) return rest;

        if (!LauncherPart.TryPartPointEcl(platform, Director, Profile.HeadPivot, PlatformEcl,
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
            ? OpticGeometry.ClampToTravel(Profile, partFrame)
            : rest;
    }

    // Bearing and elevation about the mount, for driving it by hand.
    private double3 ManualAim()
    {
        double bearing = float.DegreesToRadians(_policy.ManualBearingDeg);
        double elevation = float.DegreesToRadians(_policy.ManualElevationDeg);

        double across = Math.Cos(elevation);

        return new double3(Math.Sin(elevation), across * Math.Cos(bearing), across * Math.Sin(bearing));
    }

    // Out of the surface it is bolted to, which for a director on a deck is the sky. Every failure
    // falls back to local up rather than a zero vector: a cone with no direction sees nothing.
    private double3 ResolveBoresight()
    {
        if (Platform is not { } platform) return Boresight;

        if (Director is { } director
            && LauncherPart.TryLauncherDirectionEcl(platform, director, OpticGeometry.MountNormal,
                                                    out double3 ecl))
        {
            return ecl;
        }

        return KsaWorld.LocalUp(platform);
    }
}
