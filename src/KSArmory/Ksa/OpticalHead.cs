using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// One optical director, crewed on the craft carrying it.
///
/// <para>Needs no weapon. Through <see cref="IOpticalHead"/> the sight, the chase camera and the claim
/// ladder never learn whether a head rides a launcher or a hull.</para>
/// </summary>
internal sealed class OpticalHead(Config config, OpticConfig policy) : IOpticalHead
{
    private readonly OpticConfig _policy = policy;
    private readonly Config _config = config;

    private readonly List<(Part, OpticProfile)> _scratch = [];

    /// <summary>This head's own settings, which no weapons system shares.</summary>
    public OpticConfig Policy => _policy;

    /// <summary>Which director this is, and what it sees with.</summary>
    public OpticProfile Profile { get; private set; } = OpticProfile.None;

    public SensorProfile Sensor { get; private set; } = SensorProfile.None;

    /// <summary>
    /// The craft it is bolted to. It moves for exactly one reason: <see cref="Rehome"/>, when a
    /// decoupler carries this director onto another craft where it may sit at a different ordinal.
    /// </summary>
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
    /// <para>Read through to the part on every use. Still a frame old in <c>SampleWorld</c> and the
    /// engine's viewport pass, which run before <c>WeaponSystem.Update</c> writes the mount.</para>
    ///
    /// <para>On the Pantsir that costs only the eye's position, because it traverses about
    /// <see cref="OpticGeometry.MountNormal"/>; a mount turning about any other axis would leave the
    /// aim and roll a frame stale too. Measured at 0.93 m off the axis and 70°/s: 19 cm of eye lag at a
    /// 165 ms step, 3.5 px at 1 km through a 3.3° field.</para>
    /// </summary>
    public MountFrame Mount
        => Director is { } director ? OpticParts.MountOf(director, Profile) : MountFrame.Fixed;

    /// <summary>
    /// Where the head points, for everything that draws it: the ball's transform, the camera's aim
    /// and the gunner's pipper.
    ///
    /// <para><b>Not extrapolated</b>: leading by the drive's last turn rate measured 0.35° of jitter at
    /// 10× against a steady 0.007° per unit of speed. CLAUDE.md, "Everything the mod draws is a step
    /// behind".</para>
    ///
    /// <para>One accessor because the ball, the camera and the pipper are three views of one
    /// direction, and from different instants they separate on screen.</para>
    /// </summary>
    public double3 AimWhenDrawn => _drive.Direction;

    /// <summary>
    /// What the operator told this head to watch, or <c>Aimpoint.Nothing</c>.
    ///
    /// <para>Beats the set's own pick while it lives. An aimpoint rather than a contact, so a place
    /// on the ground can be designated. Held here rather than on <see cref="Radar"/>, so it outlives
    /// the player taking another seat.</para>
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

        // The head's cursor and manual modes sit above the designation in AimPartFrame, so they are
        // switched off, and the tick boxes going out says so.
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
    /// Local "up" at the director, which keeps the sight's horizon level: <see cref="IOpticalHead.Boresight"/>'s
    /// contract, and not where the set is looking, which on a hull's side points sideways.
    /// </summary>
    public double3 Boresight { get; private set; } = new(0, 0, 1);

    /// <summary>
    /// Where its search volume points, in Ecl: out of the surface it is bolted to.
    ///
    /// <para>The direction the sensor sweeps about; <see cref="Boresight"/> is which way is up.</para>
    /// </summary>
    public double3 SensorBoresight { get; private set; } = new(0, 0, 1);

    /// <summary>
    /// What the camera rolls against: the head's own up unless levelling is asked for.
    ///
    /// <para>A mast head's up is continuous everywhere its travel allows. <b>A roll-nod head takes the
    /// forward side of the nod plane, which is derotation</b>: what the pod hangs from stays at the top
    /// of the picture however far the nose has rolled. Its singular directions, the keyhole and dead
    /// astern, are outside the travel.</para>
    ///
    /// <para>Resolved on every read, because it is used beside a forward the engine's pass re-solves;
    /// a frame apart, the two make a roll that shakes the whole sight under warp.</para>
    /// </summary>
    public double3 RollReferenceEcl => ResolveRollReference();

    /// <summary>
    /// What it is watching: the best contact on scope, not the best one a weapon may shoot.
    /// </summary>
    public Track? LockedTrack => Radar.Watched;

    /// <summary>True once the head has caught up with what it was told to look at.</summary>
    public bool OpticOnTarget => _drive.OnTarget && _driveWorks;

    // Where the head is actually looking, in the director's own part frame.
    private readonly PointingDrive _drive = new();

    // The drives' clock: the engine's step carries the display's frame pacing, three times as long
    // on alternate frames.
    private readonly SmoothedStep _driveStep = new();

    // Latched, so a head whose writes are refused cannot go on reporting that it is on target.
    private bool _driveWorks = true;

    // The shell's own latch: a refused roll is cosmetic, and must not freeze a working sight.
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
            Sensor = Catalogue.SensorNamed(Profile.Sensor);
            OpticPart = OpticParts.FindHead(found.Part, found.Profile);
            RollPart = OpticParts.FindRoll(found.Part, found.Profile);

            // Filled once, when first crewed, and not refilled by finding the part again.
            if (double.IsNaN(ChemicalSecondsLeft)) ChemicalSecondsLeft = Profile.Beam?.ChemicalSeconds ?? 0.0;
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

        // The one place it is held to this optic's own: the panel, the window and the bridge all write it.
        _policy.Magnification = Math.Clamp(_policy.Magnification, SightZoom.MinMagnification,
                                           SightZoom.Clamp(Profile.MaxMagnification));

        Radar.Sensor = Sensor;
        (Radar.Cue, CuedBy) = Designation.Kind == AimpointKind.Vehicle ? (Designation.Handle, CueSource.Designation)
                              : _cue.Kind == AimpointKind.Vehicle ? (_cue.Handle, CueSource.Launcher)
                              : _policy.Tracking && Pictured is not null ? (Pictured, CueSource.Picture)
                              : (null, CueSource.None);
        Radar.Scan(platform, SensorBoresight, dt, airborne);

        UpdateSpotSearch(platform, dt);

        // AimPartFrame first: it is what works out how hard the cursor is commanding, and the
        // rate below is that command.
        double3 aim = AimPartFrame();

        _drive.Update(_driveStep.Next(dt), aim, Profile.SlewRateRad * _mouseRate);

        // The travel again, on what the drive reached: the shortest rotation between two legal
        // directions can pass through the mast.
        _drive.Hold(OpticGeometry.ClampToTravel(Profile, Mount, _drive.Direction));

        // The shell whatever the head's latch says: a frozen head still has a roll the window sits at.
        if (RollPart is { } shell && _rollWorks
            && !OpticParts.TryApplyRoll(shell, Profile, Mount, AimWhenDrawn))
        {
            _rollWorks = false;
            Log.Warn("optic: the engine refused the roll gimbal's transform; the nose is frozen "
                     + "where it stopped and the head goes on aiming");
        }

        UpdateMask(platform);
        UpdateLaser(platform, dt);
        UpdateCueCloud(platform);

        // The weapon says both again every step it still wants them.
        _cue = Aimpoint.Nothing;
        _beamCommanded = false;

        if (OpticPart is not { } head || !_driveWorks) return;

        if (!OpticParts.TryApplyAim(head, Profile, Mount, AimWhenDrawn))
        {
            _driveWorks = false;
            Log.Warn("optic: the engine refused the head's transform; it is frozen where it stopped");
        }
    }

    /// <summary>
    /// Whether the head's own craft blocks its line of sight: to what it follows when it follows
    /// something, along its axis otherwise. Only worked out while it is showing a view.
    /// </summary>
    public bool Masked { get; private set; }

    private void UpdateMask(Vehicle platform)
    {
        Masked = false;
        if (_policy.Viewport < 0 && !LaserWanted && !_beamCommanded && _cue.Kind == AimpointKind.None) return;
        if (!TryOpticViewEcl(out double3 eye, out double3 forward)) return;

        double reach = 2.0 * KsaWorld.MeanRadius(platform) + 10.0;
        double3 direction = forward;

        if (TryFollowedDrawnEcl(-1, out double3 target))
        {
            direction = target - eye;
            reach = Math.Min(reach, Vec.Len(direction));
        }

        if (KsaWorld.TryCraftMasks(platform, PlatformEcl, eye, direction, reach, out bool masked))
        {
            Masked = masked;
        }
    }

    public bool HasLaser => Profile.HasLaser;

    /// <summary>
    /// Whether a roll-nod gimbal is at its nod stop or in the keyhole straight ahead, where a head at
    /// its limit and a head with nothing to look at would otherwise look the same.
    /// </summary>
    public bool AtGimbalLimit
    {
        get
        {
            if (Profile.Gimbal != GimbalKind.RollNod) return false;

            double off = double.RadiansToDegrees(OpticGeometry.OffBoresightRad(Mount, AimWhenDrawn));
            return off >= Profile.MaxOffBoresightDeg - 0.5 || off <= Profile.KeyholeDeg + 0.5;
        }
    }

    private bool LaserWanted => HasLaser && _policy.Lasing;

    /// <summary>The laser is wanted and held off because the craft itself is in the way.</summary>
    public bool LaserInhibited { get; private set; }

    /// <summary>Where the laser is landing this frame, or null: off, inhibited, or out of range.</summary>
    public LaserSpot? Spot { get; private set; }

    /// <summary>What the spot is on, as a player would call it: a craft's name, or the ground.</summary>
    public string SpotOn { get; private set; } = string.Empty;

    // Along the line of sight the picture is centred on, to the first craft or ground it meets.
    // Its own craft is left out of the cast: MASK has already said whether that is in the way.
    private void UpdateLaser(Vehicle platform, double dt)
    {
        Spot = null;
        Burning = null;
        BeamOn = false;
        InCloud = false;
        LaserInhibited = (LaserWanted || _beamCommanded) && Masked;

        // The designator and the beam leave by the same window and are switched apart: the
        // operator's toggle marks a spot, and only the weapon on this part fires the beam.
        bool designating = LaserWanted && LaserCode.IsValid(_policy.LaserCode);
        bool firing = _beamCommanded && Profile.Beam is not null && ChemicalSecondsLeft > 0.0;

        if (LaserInhibited || !(designating || firing)) return;
        if (!TryOpticViewEcl(out double3 eye, out double3 forward)) return;

        if (firing) ChemicalSecondsLeft = Math.Max(0.0, ChemicalSecondsLeft - Math.Max(dt, 0.0));

        double reach = Profile.LaserRangeMetres;
        bool clouded = KsaWorld.TryCloudEntry(platform, eye, forward, reach, out double cloud);
        if (clouded) reach = cloud;

        bool onCraft = KsaWorld.TryRayPartHit(eye, forward, platform, reach, out Vehicle? craft, out Part? part,
                                              out double range);

        if (KsaWorld.TryRayGround(eye, forward, onCraft ? range : reach, out Celestial? body, out double ground)
            && (!onCraft || ground < range))
        {
            onCraft = false;
            range = ground;
        }
        else if (!onCraft)
        {
            // Nothing in reach, but a beam fired at the sky is still a beam: drawn out to its range, or
            // into the cloud that stops it. A spot on a cloud designates nothing.
            InCloud = clouded;
            if (firing)
            {
                BeamOn = true;
                BeamFromEcl = eye;
                BeamToEcl = eye + (Vec.Unit(forward) * reach);
            }

            return;
        }

        double3 at = eye + (Vec.Unit(forward) * range);
        SpotOn = onCraft ? KsaWorld.DisplayName(craft!) : "the ground";

        if (designating)
        {
            double3 velocity = onCraft ? KsaWorld.VelocityEcl(craft!) : KsaWorld.GroundVelocityAt(body!, at);
            Spot = new LaserSpot(platform, _policy.LaserCode, at, velocity, range);
            LaserDesignators.Add(Spot.Value);
        }

        if (!firing) return;

        BeamOn = true;
        BeamFromEcl = eye;
        BeamToEcl = at;
        Burning = onCraft && part is not null ? part : null;
        if (Burning is not null) Burn(platform, craft!, part!, eye, at, range, dt);
    }

    /// <summary>Seconds of lasing left in the chemicals aboard; nothing for a head with no beam.</summary>
    public double ChemicalSecondsLeft { get; private set; } = double.NaN;

    /// <summary>A full load of chemicals again, for a test that needs to fire longer than one load.</summary>
    public void RefillChemicals() => ChemicalSecondsLeft = Profile.Beam?.ChemicalSeconds ?? 0.0;

    /// <summary>Whether the beam is firing and landing somewhere this frame.</summary>
    public bool BeamOn { get; private set; }

    /// <summary>The part the beam is on this frame, or null: not firing, or landing on the ground.</summary>
    public Part? Burning { get; private set; }

    /// <summary>Where the beam leaves and lands this frame, from one sample of the platform.</summary>
    public double3 BeamFromEcl { get; private set; }

    public double3 BeamToEcl { get; private set; }

    /// <summary>Whether the laser ends in cloud this frame, which a beam glows in rather than crossing.</summary>
    public bool InCloud { get; private set; }

    // What the weapon on this part wants: where to look and whether to lase, said each step.
    private Aimpoint _cue = Aimpoint.Nothing;
    private Aimpoint _cueAimed = Aimpoint.Nothing;
    private bool _cueCommands;
    private bool _beamCommanded;
    private double _aimRangeMetres = double.NaN;

    /// <summary>
    /// Points the head at what the weapon on its part is engaging, for this step: always when that
    /// weapon fires through it, otherwise only with the head's own tracking on.
    /// </summary>
    public void Cue(Aimpoint aim, bool commands)
    {
        _cue = aim;
        _cueCommands = commands;
    }

    /// <summary>Fires the beam this step, if there are chemicals and nothing is in the way.</summary>
    public void CommandBeam(bool on) => _beamCommanded = on;

    /// <summary>Settled on what it was told to look at, by the operator, the weapon or its own set.</summary>
    public bool OnTarget => OpticOnTarget && _aimed is Aimed.Designation or Aimed.Cue or Aimed.Track;

    public bool FollowsCue => _aimed is Aimed.Cue or Aimed.Spot;

    /// <summary>Cloud lies between the head and what the weapon cued it at.</summary>
    public bool CueBehindCloud { get; private set; }

    private void UpdateCueCloud(Vehicle platform)
    {
        CueBehindCloud = false;
        if (_aimed is not (Aimed.Cue or Aimed.Designation) || !double.IsFinite(_aimRangeMetres)) return;
        if (!TryOpticViewEcl(out double3 eye, out double3 forward)) return;

        CueBehindCloud = KsaWorld.TryCloudEntry(platform, eye, forward, _aimRangeMetres, out _);
    }

    private readonly HashSet<Part> _burntThrough = new(ReferenceEqualityComparer.Instance);

    // Spends the struck part's health for the dwell this step. A part burnt through goes to the
    // engine's failure queue once, as a shell's struck part does, or takes the craft with it when
    // parts are not damaged one by one.
    private void Burn(Vehicle platform, Vehicle craft, Part part, double3 eye, double3 at, double range, double dt)
    {
        if (!(dt > 0.0) || _burntThrough.Contains(part)) return;

        double share = Beam.HealthPerSecond(Profile.Beam!, range, KsaWorld.AirDensityRatioAt(platform, eye),
                                            KsaWorld.AirDensityRatioAt(platform, at)) * dt;
        if (!PartHealth.World.Hit(part, share, _config.DamageScale)) return;

        _burntThrough.Add(part);
        KsaWorld.WaitForVehicleSolvers();

        if (_config.DamageIndividualParts && KsaWorld.TryQueuePartFailure(craft, [part]))
        {
            Log.Info($"beam burnt through {part.Id} on {KsaWorld.DisplayName(craft)} at {Distance.Say(range)}");
            return;
        }

        Log.Info($"beam destroyed {KsaWorld.DisplayName(craft)} at {Distance.Say(range)}");
        KsaWorld.Destroy(craft, blastSeverity: 50f);
    }

    /// <summary>Clears the refusal latches, because a new craft deserves a fresh assessment.</summary>
    public void Reset() => _driveWorks = _rollWorks = true;

    /// <summary>
    /// Follow this director onto the craft that now carries it, after a decoupler split it off the
    /// one it was on.
    ///
    /// <para>The settings, designation and aim come across by being this same object. The drive is
    /// not disturbed: it holds a direction in the director's own part frame, and the part is the same.</para>
    /// </summary>
    public void Rehome(Vehicle craft, int ordinal)
    {
        if (!KsaWorld.IsAlive(craft)) return;

        double3 toEcl = KsaWorld.PositionEcl(craft);
        if (!Vec.IsFinite(toEcl)) return;

        double moved = Vec.Len(toEcl - PlatformEcl);

        Platform = craft;
        Ordinal = ordinal;
        PlatformEcl = toEcl;

        // These belong to the part tree the director has left; found again in the new one.
        Director = null;
        OpticPart = null;
        RollPart = null;

        Reset();

        Log.Info($"director decoupled onto {KsaWorld.DisplayName(craft)} as director {ordinal + 1}, "
                 + $"{moved:F0} m away");
    }

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
    /// <para>For the caller inside the engine's viewport pass, a frame of the planet's motion from the
    /// mod's own sample.</para>
    /// </summary>
    /// <param name="viewIndex">The viewport the view is for, whose camera says where the target is drawn.</param>
    public bool TryOpticViewEclAt(double3 platformEcl, out double3 eyeEcl, out double3 forwardEcl,
                                  int viewIndex = -1)
    {
        eyeEcl = forwardEcl = Vec.Zero;

        if (Platform is not { } platform || Director is not { } director) return false;

        if (!OpticParts.TryViewEcl(platform, director, Profile, Mount, AimWhenDrawn, platformEcl,
                                           out eyeEcl, out forwardEcl))
        {
            return false;
        }

        // While settled, the view is re-solved onto the target at this instant, removing the
        // frame-late term. Only onto what AimPartFrame aimed at (_aimed): a head held by the mouse
        // or the sliders is not re-solved onto the radar's contact.
        if (!_drive.OnTarget) return true;
        if (!TryFollowedDrawnEcl(viewIndex, out double3 drawnEcl)) return true;

        double3 toTarget = drawnEcl - eyeEcl;
        if (Vec.Len2(toTarget) > 1.0) forwardEcl = Vec.Unit(toTarget);

        return true;
    }

    // Where what the head is aimed at is drawn now, whichever rung AimPartFrame took. False under the
    // mouse or the sliders. Drawn rather than simulated: metres apart on a landed craft.
    private bool TryFollowedDrawnEcl(int viewIndex, out double3 drawnEcl)
    {
        drawnEcl = Vec.Zero;

        if (_aimed is Aimed.Designation or Aimed.Cue or Aimed.Spot)
        {
            Aimpoint aim = _aimed == Aimed.Designation ? Designation : _cueAimed;
            if (aim.NeedsResampling)
            {
                return KsaWorld.TryGroundAnchorEcl(aim.Handle, aim.Anchor, out drawnEcl, out _);
            }

            if (aim.Handle is Vehicle craft && KsaWorld.IsAlive(craft))
            {
                if (!KsaWorld.TryDrawnMiddleEcl(craft, out drawnEcl, viewIndex)) drawnEcl = KsaWorld.CentreEcl(craft);
                return true;
            }

            if (aim.Kind == AimpointKind.Point)
            {
                drawnEcl = aim.PositionEcl;
                return true;
            }

            return false;
        }

        if (_aimed != Aimed.Track || Radar.Watched is not { } watched) return false;

        if (watched.Contact is VehicleContact craftContact)
        {
            return KsaWorld.TryDrawnMiddleEcl(craftContact.Vehicle, out drawnEcl, viewIndex);
        }

        return watched.Contact.TryDrawEgo(out double3 ego) && KsaWorld.TryEgoToEcl(ego, out drawnEcl);
    }

    /// <summary>
    /// What a camera window's aim is doing, for the bridge: the rung taken, the drive's error, and
    /// how far the window's camera is off what it follows, as that window draws it.
    /// </summary>
    public Dictionary<string, object?> DescribeAim(int viewIndex)
    {
        Dictionary<string, object?> row = new()
        {
            ["aimed"] = _aimed.ToString(),
            ["drive_error_deg"] = double.RadiansToDegrees(_drive.ErrorRad),
            ["on_target"] = _drive.OnTarget,
            ["masked"] = Masked,
            ["lasing"] = Spot is not null,
            ["laser_inhibited"] = LaserInhibited,
            ["laser_range_m"] = Spot?.RangeMetres,
            ["watching"] = Radar.Watched?.Contact.DisplayName,
            ["designated"] = Designation.Kind.ToString(),
        };

        if (TryFollowedDrawnEcl(viewIndex, out double3 targetEcl)
            && KsaWorld.TryReadViewportPose(viewIndex, out double3 eye, out double3 forward, out _))
        {
            row["camera_off_target_deg"] = double.RadiansToDegrees(Vec.AngleBetween(forward, targetEcl - eye));
            row["range_m"] = Vec.Len(targetEcl - eye);
        }

        return row;
    }

    // Which rung the last AimPartFrame took. The view re-solve reads it rather than choosing again,
    // because two places deciding what the head follows can disagree -- see TryOpticViewEclAt.
    private enum Aimed { Rest, Cursor, Hand, Designation, Cue, Spot, Track }

    private Aimed _aimed;

    // Where the head is told to look, in the director's own part frame, clamped to its travel here
    // so settling is measured against a command it can reach.
    private double3 AimPartFrame()
    {
        double3 rest = OpticGeometry.RestAim(Profile, Mount);
        _aimed = Aimed.Rest;

        // Mouse aim owns the head outright: falling through would swing it away the moment the
        // cursor stopped commanding. Holding is the drive's own direction.
        if (_policy.MouseAim)
        {
            _aimed = Aimed.Cursor;
            return TryCursorAimPartFrame(out double3 cursorFrame)
                ? OpticGeometry.ClampToTravel(Profile, Mount, cursorFrame)
                : _drive.Direction;
        }

        if (_policy.Manual)
        {
            _aimed = Aimed.Hand;
            return OpticGeometry.ClampToTravel(Profile, Mount, ManualAim());
        }

        if (Platform is not { } platform || Director is null) return rest;

        // The operator's designation, ahead of the tracking switch: designating is the instruction to watch.
        if (Designation.Kind != AimpointKind.None)
        {
            Aimpoint designation = Designation;
            bool held = TryAimpointAim(platform, ref designation, report: true, out double3 designated);
            Designation = designation;

            if (held)
            {
                _aimed = Aimed.Designation;
                return designated;
            }

            // Gone: dropped rather than left pointing at a hole.
            ClearDesignation();
        }

        // What the weapon on this part is engaging, below the operator's own designation: a
        // shift-click is the operator choosing, and fire control follows the same click anyway.
        if (_cue.Kind != AimpointKind.None && _cueCommands && TryCued(platform, out double3 commanded)) return commanded;

        // A spot somebody else is lasing on this head's code, which the operator switched the search
        // on for: ahead of a launcher's own cue, which only stands in for the tracking switch.
        if (FoundSpot is { } spot)
        {
            Aimpoint at = Aimpoint.AtPoint(spot);
            if (TryAimpointAim(platform, ref at, report: false, out double3 spotted))
            {
                _aimed = Aimed.Spot;
                _cueAimed = at;
                return spotted;
            }
        }

        if (_cue.Kind != AimpointKind.None && _policy.Tracking && TryCued(platform, out double3 cued)) return cued;

        if (!_policy.Tracking) return rest;

        // From the head's own pivot, not the part's origin: 0.63 m apart on the EO director, a tenth
        // of the picture at a few hundred metres. As WeaponSystem.AimOriginEcl.
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

        if (!LauncherPart.TryDirectionToPartFrame(platform, Director, targetEcl - pivotEcl,
                                                  out double3 partFrame))
        {
            return rest;
        }

        _aimed = Aimed.Track;
        return OpticGeometry.ClampToTravel(Profile, Mount, partFrame);
    }

    private bool TryCued(Vehicle platform, out double3 partFrame)
    {
        if (!TryAimpointAim(platform, ref _cue, report: false, out partFrame)) return false;

        _aimed = Aimed.Cue;
        _cueAimed = _cue;
        return true;
    }

    /// <summary>
    /// What the craft's picture hands this head, held only under the tracking switch and below a
    /// designation and its launcher's cue.
    /// </summary>
    public object? Pictured { get; set; }

    /// <summary>What handed this step's cue to the head's set, which holds nothing it is not handed.</summary>
    public CueSource CuedBy { get; private set; }

    /// <summary>The laser spot this head's tracker has found this step, or null.</summary>
    public double3? FoundSpot { get; private set; }

    // How far off a direction may be from where the travel puts it and still count as reachable.
    private const double SpotReachSlackDeg = 1.0;

    // Ground nearer than the spot by more than this hides it; a spot lies on the ground the ray meets.
    private const double SpotGroundSlackMetres = 30.0;

    private void UpdateSpotSearch(Vehicle platform, double dt)
    {
        FoundSpot = null;
        if (!_policy.SpotSearch || Director is null) return;
        if (!TryOpticViewEcl(out double3 eye, out _)) return;
        if (!LauncherPart.TryPartPointEcl(platform, Director, Mount.ToPart(Profile.HeadPivot), PlatformEcl,
                                          out double3 pivot))
        {
            return;
        }

        bool CanSee(double3 at)
        {
            double3 toSpot = at - pivot;
            if (!LauncherPart.TryDirectionToPartFrame(platform, Director, toSpot, out double3 inPart)
                || Vec.Len2(inPart) < 0.5)
            {
                return false;
            }

            double3 reach = OpticGeometry.ClampToTravel(Profile, Mount, inPart);
            if (double.RadiansToDegrees(Vec.AngleBetween(reach, inPart)) > SpotReachSlackDeg) return false;

            double range = Vec.Len(at - eye);
            return range <= SpotGroundSlackMetres
                   || !KsaWorld.TryRayGround(eye, Vec.Unit(at - eye), range - SpotGroundSlackMetres, out _, out _);
        }

        if (SpotSearch.TryChoose(LaserDesignators.LastStep, _policy.LaserCode, platform, eye, Sensor.Range, dt,
                                 CanSee, out double3 found))
        {
            FoundSpot = found;
        }
    }

    // Where the designation is now, in the head's part frame. False once it is gone.
    //
    // A place on a body is re-read every frame: held in the ecliptic it is left behind at ~29.8 km/s.
    // See Sim/Aimpoint.NeedsResampling.
    private bool TryAimpointAim(Vehicle platform, ref Aimpoint aim, bool report, out double3 partFrame)
    {
        partFrame = Vec.Zero;

        if (aim.Kind == AimpointKind.Vehicle && (aim.Handle is not Vehicle craft || !KsaWorld.IsAlive(craft)))
        {
            return false;
        }

        double3 targetEcl = aim.PositionEcl;

        if (aim.Kind == AimpointKind.Vehicle && aim.Handle is Vehicle live)
        {
            if (!KsaWorld.TryDrawnMiddleEcl(live, out targetEcl)) targetEcl = KsaWorld.CentreEcl(live);
        }
        else if (aim.NeedsResampling)
        {
            if (!KsaWorld.TryGroundAnchorEcl(aim.Handle, aim.Anchor, out double3 groundEcl, out double3 groundVel))
            {
                return false;
            }

            aim = aim.Resampled(groundEcl, groundVel);
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
            if (report) WhyNotWatching("the direction would not convert into the head's frame");
            return false;
        }

        partFrame = OpticGeometry.ClampToTravel(Profile, Mount, toTarget);
        _aimRangeMetres = Vec.Len(targetEcl - pivotEcl);
        if (!report) return true;

        // Said once, so the log tells following it apart from falling through to something else.
        WhyNotWatching("following", $"at {Vec.Len(targetEcl - pivotEcl) / 1000.0:F2} km");

        return true;
    }

    // Why the designation is or is not driving the head, once per state.
    //
    // Keyed on the state, never the message: a key carrying the range changes every frame.
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
    // From the head's pivot, as a bearing rather than a ray: a cursor's ray is from the camera, which
    // coincides with the head only while it drives the view. KsaWorld.TryCursorAimEcl resolves it.
    private bool TryCursorAimPartFrame(out double3 partFrame)
    {
        partFrame = default;

        if (!_policy.MouseAim || Platform is not { } platform || Director is not { } director)
        {
            return false;
        }

        // At rest in the middle of its own picture, and slower nearer rest, or the head chases a cursor
        // its own turning keeps off centre. Only while this head drives the view.
        _mouseRate = 1.0;

        if (_policy.Viewport == KsaWorld.MainViewportIndex)
        {
            if (!KsaWorld.TryCursorFromViewCentre(_policy.MouseDeadZonePx, out float2 fromCentre,
                                                  out bool commands, out float halfHeight)
                || !commands)
            {
                return false;
            }

            // From the ring's edge, against what is left of the screen, so a drag means the same at any size.
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
        // Boresight rather than this property, which would recurse.
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
