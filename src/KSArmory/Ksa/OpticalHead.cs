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
    /// The outer roll gimbal, on a head that has one. Null on every mast head, which is not a
    /// failure: there is no such body to find.
    /// </summary>
    public Part? RollPart { get; private set; }

    /// <summary>
    /// Where the base is, now. <see cref="MountFrame.Fixed"/> for a director bolted to a hull,
    /// and wherever a traverse or a hinge has carried it for one that rides something.
    ///
    /// <para>Read through to the part on every use rather than cached, so there is no stale copy of
    /// this <em>within</em> a frame. It is still a frame old in two places, and saying otherwise
    /// was wrong: <c>WeaponSystem.Update</c> writes the mount, but <c>SampleWorld</c> runs earlier
    /// in the same hook, and the engine's viewport pass — where the camera pose is re-solved —
    /// runs before the hook entirely. Both see the previous frame's traverse.</para>
    ///
    /// <para>What that costs, here, is only the eye's <em>position</em>: the Pantsir traverses
    /// about the part's +X, which is also <see cref="OpticGeometry.MountNormal"/>, so the mount's
    /// normal and the head's pivot offset are both invariant under it and the aim, the boresight
    /// and the roll are untouched. That is luck rather than design — a director on a hinge, an arm,
    /// or a traverse about any other axis would have all of them a frame stale. Measured at 0.93 m
    /// off the axis and 70°/s: 19 cm of eye lag at a 165 ms step, which is 3.5 px at 1 km through a
    /// 3.3° field and 18 px at 200 m. A translation, so it displaces near things and leaves the
    /// stars alone.</para>
    /// </summary>
    public MountFrame Mount
        => Director is { } director ? OpticParts.MountOf(director, Profile) : MountFrame.Fixed;

    /// <summary>
    /// Where the head points, for everything that draws it: the ball's transform, the camera's aim
    /// and the gunner's pipper.
    ///
    /// <para><b>Not extrapolated, and that was measured rather than assumed.</b> Everything drawn
    /// is a step behind the world it is drawn against, and carrying the aim one step forward at the
    /// drive's own last turn rate does remove that — but the rate is a per-frame report, so the
    /// lead varies frame to frame and the picture shakes. Measured through the sight at a range of
    /// speeds: the residual it was meant to remove is 0.007° per unit of simulation speed, steady;
    /// the lead it introduced was 0.35° at 10× against a target crossing 0.0037°, and noisy. A
    /// small steady offset reads as a slightly off-centre picture; a large varying one reads as
    /// jitter, which is worse at every speed above 1×.</para>
    ///
    /// <para>A lead taken from the <em>target's</em> angular rate rather than the drive's own turn
    /// would be the principled version, and is not built.</para>
    ///
    /// <para>One accessor because the ball, the camera and the pipper are three views of one
    /// direction: take two of them from different instants and they separate on screen, which is
    /// worse than the lag either would have had alone.</para>
    /// </summary>
    public double3 AimWhenDrawn => _drive.Direction;

    /// <summary>
    /// What the operator told this head to watch, or <c>Aimpoint.Nothing</c>.
    ///
    /// <para>Beats the set's own pick while it lives, because it is the one input that says the
    /// operator knows something the threat model does not. An aimpoint rather than a contact so a
    /// place on the ground can be designated at all — nothing reports a hillside, and that is
    /// exactly what is wanted when the interesting thing is a structure the engine does not
    /// model.</para>
    ///
    /// <para>Held here rather than on <see cref="Radar"/>: this is the head's own instruction, and
    /// it outlives the craft being flown. A director keeps watching what it was told to watch when
    /// the player takes another seat.</para>
    /// </summary>
    public Aimpoint Designation { get; private set; } = Aimpoint.Nothing;

    /// <summary>What the designation is, for the panel to name.</summary>
    public string DesignationName { get; private set; } = "nothing";

    /// <summary>Points the head at something, until told otherwise.</summary>
    public void Designate(Aimpoint aim, string what)
    {
        Designation = aim;
        DesignationName = what;
        _whyNotWatching = "";

        // A head has its own cursor and manual modes, separate from the launcher's -- and both sit
        // above the designation in AimPartFrame, so leaving them on is a designation that never
        // moves the head. Switched off rather than out-ranked, for the reason the system's are:
        // "follow this" replaces "follow my cursor", and the tick boxes going out is what says so.
        bool wasDriven = _policy.MouseAim || _policy.Manual;
        _policy.MouseAim = false;
        _policy.Manual = false;

        Log.Info($"director watching {what}"
                 + (wasDriven ? " (mouse aim and manual off: it now follows this)" : ""));
    }

    /// <summary>Hands the head back to its own set.</summary>
    public void ClearDesignation()
    {
        if (Designation.Kind == AimpointKind.None) return;

        Log.Info($"director released {DesignationName}");
        Designation = Aimpoint.Nothing;
        DesignationName = "nothing";
    }

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
    /// singularity worth the name: a mast head's up is built to stay near the mount's normal and
    /// is continuous everywhere the travel allows, so there is nothing to carry and nothing to
    /// flip.</para>
    ///
    /// <para><b>A roll-nod head takes the other end of its own up, and that is derotation.</b> Its
    /// nose rolls, so the scene turns in the focal plane — half a turn of roll and the picture is
    /// upside down — and every pod of the class counters it, optically with a prism or, as Litening
    /// does, in the video processor. Referencing the forward side of the nod plane is that counter:
    /// what the pod is bolted to stays at the top of the picture however far the nose has rolled.
    /// Its two singular directions are the keyhole and dead astern, and the travel excludes
    /// both.</para>
    /// </summary>
    /// <para>Resolved on every read rather than sampled once, because it is used <em>beside</em> a
    /// forward that the engine's own pass re-solves. Sampled in <c>SampleWorld</c> it came from the
    /// drive as it stood a frame earlier, so the camera's up and its forward were one frame apart —
    /// and what survives that mismatch is a <b>roll</b>, which turns the whole picture rather than
    /// nudging it. That grows with how far the head turned in the frame, so it scales with
    /// simulation speed and reads as the entire sight shaking under warp.</para>
    public double3 RollReferenceEcl => ResolveRollReference();

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

    // The shell's own latch. Separate because a refused roll is cosmetic -- the line of sight is
    // the head's, and it still points where it is told -- and sharing one latch would freeze a
    // working sight over a body nobody is looking through.
    private bool _rollWorks = true;

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
            RollPart = OpticParts.FindRoll(found.Part, found.Profile);
        }
        else
        {
            Director = null;
            OpticPart = null;
            RollPart = null;
        }

        SensorBoresight = ResolveSensorBoresight();
        Boresight = Platform is { } up ? KsaWorld.LocalUp(up) : Boresight;
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

        // The shell first, and unconditionally on the head's own latch: a frozen head still has a
        // roll the window is meant to sit at, and a shell left behind reads as the window hanging
        // outside its aperture rather than as a drive that stopped.
        if (RollPart is { } shell && _rollWorks
            && !OpticParts.TryApplyRoll(shell, Profile, Mount, AimWhenDrawn))
        {
            _rollWorks = false;
            Log.Warn("optic: the engine refused the roll gimbal's transform; the nose is frozen "
                     + "where it stopped and the head goes on aiming");
        }

        if (OpticPart is not { } head || !_driveWorks) return;

        if (!OpticParts.TryApplyAim(head, Profile, Mount, AimWhenDrawn))
        {
            _driveWorks = false;
            Log.Warn("optic: the engine refused the head's transform; it is frozen where it stopped");
        }
    }

    /// <summary>Clears the refusal latches, because a new craft deserves a fresh assessment.</summary>
    public void Reset() => _driveWorks = _rollWorks = true;

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

        if (!OpticParts.TryViewEcl(platform, director, Profile, Mount, AimWhenDrawn, platformEcl,
                                           out eyeEcl, out forwardEcl))
        {
            return false;
        }

        // While the head is settled it is tracking, so the view is re-solved onto the target's own
        // position at this instant rather than left along an axis turned to a frame ago. Same rule,
        // and the same reason, as the launcher's head had. Skipping it leaves the whole frame-late
        // term in, and that term scales with simulation speed.
        //
        // Against whatever the head is *following*, which is not always a radar lock. Asking for
        // Radar.Locked skipped the re-solve for a contact that is tracked but not a threat -- the
        // very case a director exists to watch -- and for every designation, since a designated
        // hillside is not a track at all. Both were introduced the day the head learned to follow
        // them, and both showed up as jitter under warp and nowhere else.
        if (!_drive.OnTarget) return true;
        if (!TryFollowedDrawnEcl(out double3 drawnEcl)) return true;

        double3 toTarget = drawnEcl - eyeEcl;
        if (Vec.Len2(toTarget) > 1.0) forwardEcl = Vec.Unit(toTarget);

        return true;
    }

    // Where the thing the head is following is *drawn*, now -- a designation first, then the set's
    // own pick. False when it is following nothing resolvable.
    //
    // The drawn position rather than the simulated one, because this decides where a camera points
    // and the target is drawn at the former. The two differ by metres on a landed craft.
    private bool TryFollowedDrawnEcl(out double3 drawnEcl)
    {
        drawnEcl = Vec.Zero;

        if (Designation.Kind != AimpointKind.None)
        {
            if (Designation.NeedsResampling)
            {
                return KsaWorld.TryGroundAnchorEcl(Designation.Handle, Designation.Anchor,
                                                   out drawnEcl, out _);
            }

            if (Designation.Handle is Vehicle craft && KsaWorld.IsAlive(craft))
            {
                drawnEcl = KsaWorld.PositionEcl(craft);
                return true;
            }

            return false;
        }

        return Radar.Watched is { } watched
               && watched.Contact.TryDrawEgo(out double3 ego)
               && KsaWorld.TryEgoToEcl(ego, out drawnEcl);
    }

    // Where the head is told to look, in the director's own part frame. Clamped to its travel
    // here rather than at the drive, so the drive's own settling is measured against a command it
    // can actually reach -- otherwise a head told to look through its mount slews to the floor and
    // reports itself forever unsettled.
    private double3 AimPartFrame()
    {
        double3 rest = OpticGeometry.RestAim(Profile, Mount);

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

        if (Platform is not { } platform || Director is null) return rest;

        // The operator's choice first. Deliberately ahead of the tracking switch: designating
        // something is itself the instruction to watch it, so needing tracking enabled as well
        // would be a click that silently does nothing.
        if (Designation.Kind != AimpointKind.None)
        {
            if (TryDesignatedAim(platform, out double3 designated)) return designated;

            // Gone. Dropped here rather than left to point at a hole, which would read as the head
            // sticking rather than as the target having left.
            ClearDesignation();
        }

        if (!_policy.Tracking) return rest;

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

    // Where the designation is now, in the head's part frame. False once it is gone.
    //
    // A place on a body is re-read every frame rather than kept as the coordinate it was: held in
    // the ecliptic it is left behind at ~29.8 km/s, so a head watching a hillside would slide off
    // it within a second. The same rule, for the same reason, that a round aimed at the ground
    // obeys -- see Sim/Designation.NeedsResampling.
    private bool TryDesignatedAim(Vehicle platform, out double3 partFrame)
    {
        partFrame = Vec.Zero;

        if (Designation.Kind == AimpointKind.Vehicle
            && (Designation.Handle is not Vehicle craft || !KsaWorld.IsAlive(craft)))
        {
            return false;
        }

        double3 targetEcl = Designation.PositionEcl;

        if (Designation.Kind == AimpointKind.Vehicle && Designation.Handle is Vehicle live)
        {
            targetEcl = KsaWorld.PositionEcl(live);
        }
        else if (Designation.NeedsResampling)
        {
            if (!KsaWorld.TryGroundAnchorEcl(Designation.Handle, Designation.Anchor,
                                             out double3 groundEcl, out double3 groundVel))
            {
                return false;
            }

            Designation = Designation.Resampled(groundEcl, groundVel);
            targetEcl = groundEcl;
        }

        // From the head's own pivot, the same correction the tracking branch makes.
        if (!LauncherPart.TryPartPointEcl(platform, Director!, Mount.ToPart(Profile.HeadPivot),
                                          PlatformEcl, out double3 pivotEcl))
        {
            return false;
        }

        if (!LauncherPart.TryDirectionToPartFrame(platform, Director!, targetEcl - pivotEcl,
                                                  out double3 toTarget)
            || Vec.Len2(toTarget) < 0.5)
        {
            WhyNotWatching("the direction would not convert into the head's frame");
            return false;
        }

        partFrame = OpticGeometry.ClampToTravel(Profile, Mount, toTarget);

        // Said once, so the log tells "following it" apart from "silently fell through to
        // something else". An absent warning alone cannot, and that ambiguity is what made the
        // turret's version of this hard to report.
        WhyNotWatching("following", $"at {Vec.Len(targetEcl - pivotEcl) / 1000.0:F2} km");

        return true;
    }

    // Why the designation is or is not driving the head, once per state.
    //
    // Keyed on the *state*, never on the message: a key carrying the range changes every frame, so
    // "say it once" becomes a line per frame -- 24,000 of them in one session, each a synchronous
    // file write on the frame thread. A diagnostic that costs frame time is measuring itself.
    private string _whyNotWatching = "";

    private void WhyNotWatching(string state, string detail = "")
    {
        if (_whyNotWatching == state) return;

        _whyNotWatching = state;

        string tail = detail.Length > 0 ? $" {detail}" : "";

        if (state == "following")
        {
            Log.Info($"director on {DesignationName} -- following{tail}");
            return;
        }

        Log.Warn($"director: {DesignationName} is not driving the head -- {state}{tail}");
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
        => OpticGeometry.ManualAim(Profile, Mount,
                                   _policy.ManualBearingDeg, _policy.ManualElevationDeg);

    // The head's own up, carried into Ecl -- or local vertical when the operator wants the
    // picture levelled. Falls back to local up rather than a zero vector, which the controller
    // reads as "no opinion" and would hand to KSA's rule.
    private double3 ResolveRollReference()
    {
        // Boresight rather than the last answer: this is a property now, so returning it would
        // recurse. Local up is the right shape for a fallback anyway -- a camera the right way up.
        if (Platform is not { } platform) return Boresight;

        if (_policy.StabiliseHorizon) return Boresight;

        double3 mesh = Profile.Gimbal == GimbalKind.RollNod
            ? -OpticGeometry.MountNormal
            : OpticGeometry.MountNormal;

        double3 headUp = OpticGeometry.Rotation(Profile, Mount, AimWhenDrawn) * mesh;

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
