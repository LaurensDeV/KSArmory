using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// One craft's ICBM computer: it reads the world, runs <see cref="IcbmProgram"/> and flies the
/// rocket to a place on a map.
///
/// <para>Everything that decides anything is in <c>Sim/</c> and tested there. What is here is the
/// two conversions that cannot be: the world into a <see cref="IcbmState"/>, and the program's
/// answer into writes on somebody else's vehicle.</para>
///
/// <para><b>Both conversions are into the parent body's inertial frame</b>, not the ecliptic. A
/// half-hour ballistic flight carries 54 million kilometres of the planet's own travel through
/// every ecliptic term, and a solve differencing two of them across even a fraction of a step leaks
/// a piece of it. Working in Cci removes the carrier exactly rather than approximately, because it
/// is the frame the engine's own orbital mechanics are written in.
/// See <c>docs/FRAMES-AND-EPOCHS.md</c>.</para>
/// </summary>
internal sealed class IcbmComputer
{
    // KSA's own value, so an orbit solved here and one drawn by the game agree.
    private const double GravitationalConstant = 6.6743e-11;

    // How often the impact prediction is re-flown. It is a readout, not a control loop.
    private const double PredictIntervalSeconds = 0.5;

    // Coarse enough to be cheap over half an hour, fine enough to land in the right place.
    private const double PredictStepSeconds = 2.0;

    private readonly List<double3> _path = [];
    private double _sincePredict = double.PositiveInfinity;
    private bool _driving;
    private double3 _rollReference;
    private readonly AimCorrection _aim = new();
    private readonly ReleaseSequence _sequence = new();
    private readonly BusTrim _trim = new();
    private ReleaseCommand _deploy;
    private readonly double3[] _tubeAxes = new double3[64];
    private bool _separated;
    private bool _awaitingSplit;
    private bool _didSplit;
    private bool _mayTrim = true;
    private double _owedAtSplit = double.NaN;
    private Vehicle? _separatedFrom;
    private double _sinceSplit;
    private string _saidTrim = "";

    private Vehicle? _viewWanted;
    private string _saidLast = "";
    private double3 _trueAimCci;
    private MunitionProfile? _warhead;
    private double3 _releaseOffsetCci;
    private double3 _releaseKickCci;
    private bool _releaseMeasured;
    private double _tubeSpinSpeed;
    private bool _warpIsOurs;
    private IcbmPhase _reported = IcbmPhase.Idle;
    private double _sinceProbe;
    private double3 _lastCommanded;

    // Often enough to see an oscillation, rare enough not to fill the log with a burn's worth.
    private const double ProbeIntervalSeconds = 0.5;

    // How much of the remaining wait each warp leaves for the next one, and the most it will leave.
    // A fraction rather than a constant because the span is what decides how fast KSA warps: a
    // fixed margin off a ninety-minute hold is still approached at thousands of times speed.
    private const double MarginFraction = 0.2;

    private const double MaxMarginSeconds = 900.0;
    private double _throttleAchieved = 1.0;

    public Vehicle Craft { get; private set; }

    public IcbmConfig Config { get; }

    public IcbmProgram Program { get; }

    /// <summary>Where it has been told to put the warheads. Nothing happens until this is set.</summary>
    public AimSite Target { get; private set; } = AimSite.None;

    /// <summary>The last command issued, which is what every readout on the panel is describing.</summary>
    public IcbmCommand Command { get; private set; }

    /// <summary>Where the vehicle would land if everything stopped now. Null when it would not.</summary>
    public ImpactPredictor.Impact? PredictedImpact { get; private set; }

    /// <summary>How far the predicted impact is from the aim point, along the ground.</summary>
    public double PredictedMissMetres { get; private set; } = double.NaN;

    /// <summary>
    /// The warhead aboard, or null for a vehicle carrying nothing that lets go. What the overlay
    /// sizes its aim ring from, so the circle on the ground is what one of these actually reaches.
    /// </summary>
    public MunitionProfile? Munition => _warhead;

    /// <summary>The body the flight is around, as the guidance sees it.</summary>
    public BallisticBody Body { get; private set; }

    /// <summary>Height over the mean sphere, for readouts that mean nothing on the ground.</summary>
    public double AltitudeMetres { get; private set; }

    /// <summary>How far off the plane the vehicle is flying in the target sits, in degrees.</summary>
    public double OffPlaneDegrees { get; private set; }

    /// <summary>Roughly what turning the orbit that far would cost on its own.</summary>
    public double PlaneChangeCost { get; private set; }

    /// <summary>
    /// Seconds until the warheads arrive, from now.
    ///
    /// <para>Taken from the flown prediction wherever there is one, and from the plan only before
    /// there is. The two disagree while the burn is still running — the plan assumes it finishes,
    /// the prediction assumes it stops now — and the plan is the honest answer to "when will this
    /// arrive" right up until the engines quit.</para>
    /// </summary>
    public double SecondsToArrival
        => Program.IsBurning || Program.Phase == IcbmPhase.Holding
               ? Program.SecondsToArrival
               : PredictedImpact?.Seconds ?? double.NaN;

    /// <summary>How far off its solution the bus still is, or NaN while nothing is trimming it.</summary>
    public double TrimToGainMetresPerSecond => _trim.Armed ? _trim.ToGainMetresPerSecond : double.NaN;

    /// <summary>
    /// What the trim is doing, or the residual it settled or gave up at. Empty before there is
    /// anything to say.
    ///
    /// <para>Not <see cref="BusTrim.Said"/>: most of the wait happens before the trim is even armed,
    /// while the bus coasts clear of the stack it dropped, and a readout that stays blank through it
    /// is indistinguishable from one that has stopped working. This is the last thing said about
    /// either, which is the same string the log carries.</para>
    /// </summary>
    public string TrimSaid => _saidTrim;

    /// <summary>
    /// Whether the world has to be kept slow, which is the burn plus the trim.
    ///
    /// <para>The trim stops on a frame boundary exactly as the burn does, so the velocity it leaves
    /// behind is what one step of its thrusters adds. At a tenth of a second that is centimetres a
    /// second and the whole point of doing it; at the steps high timewarp hands out it is metres a
    /// second, which is worse than never having trimmed at all.</para>
    /// </summary>
    public bool NeedsShortSteps => Program.NeedsShortSteps || TrimIsFiring;

    // The whole span the aim correction has to sit out, which is from the split to the trim being
    // done - not merely the part where thrusters are firing.
    //
    // Its only observer is a prediction of where a released warhead lands, and that prediction
    // carries the ejection kick along the live mean of the tube axes. A bus coasting clear of its
    // spent stack is also tumbling, so that vector swings, so the predicted impact swings, and the
    // correction chases it at half the error every half second. Measured across a 48 s wait: the
    // bus owed 0.21 m/s at the split and 228.97 m/s by the time it was let go, having been pushed
    // by nothing at all.
    private bool TrimIsFiring => _trim.Armed && !_trim.Done;

    public Celestial? Parent { get; private set; }

    public IcbmComputer(Vehicle craft, IcbmConfig config)
    {
        Craft = craft;
        Config = config;
        Program = new IcbmProgram(config);
    }

    public void Designate(AimSite site)
    {
        Target = site;
        Program.Reset();
        _reported = IcbmPhase.Idle;
        _aim.Reset();
        _sequence.Reset();
        _trim.Reset();
        _saidTrim = "";
        _separatedFrom = null;
        _didSplit = false;
        _sinceSplit = 0.0;
        _mayTrim = true;
        _owedAtSplit = double.NaN;
        _rollReference = Vec.Zero;
        PredictedImpact = null;
        PredictedMissMetres = double.NaN;
        Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} designated {site.Describe()}");
    }

    /// <summary>Forget the target and the flight, and hand the vehicle back.</summary>
    public void Abort(string why)
    {
        // A warp asked for on this shot's behalf outlives the shot otherwise, and the player is
        // left fast-forwarding towards a burn that is no longer going to happen.
        if (_warpIsOurs)
        {
            KsaWorld.StopAutoWarp();
            _warpIsOurs = false;
        }

        AttitudeHook.Release(Craft);

        if (_driving)
        {
            VehicleCommand.SetEngine(Craft, running: false);
            VehicleCommand.ReleaseAttitude(Craft);
            _driving = false;
        }

        // Outside that block, and before the reset that forgets what was being fired: the thruster
        // flags are held keys, so a stood-down computer that leaves one down hands the player a bus
        // translating on its own with nothing on screen saying why.
        VehicleCommand.DriveTranslation(Craft, TrimAxes.None);

        Program.Reset();
        _trim.Reset();
        _saidTrim = "";
        _separatedFrom = null;
        _didSplit = false;
        _sinceSplit = 0.0;
        _mayTrim = true;
        _owedAtSplit = double.NaN;
        Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} stood down: {why}");
    }

    /// <param name="release">
    /// The weapon aboard, as the one thing this needs of it: something that can be told to shoot at
    /// a place. Null for a vehicle carrying nothing that lets go, which flies the arc regardless.
    /// </param>
    public void Update(double simStep, double playerStep, IManualFire? release)
    {
        if (!KsaWorld.IsAlive(Craft)) return;

        // What the prediction is of. The bus cuts off above the air; the warheads it drops fly all
        // the way down through it, and they are the things that have to arrive.
        _warhead = release?.Munition;
        MeasureRelease(release);

        if (!Config.Armed)
        {
            // Standing down has to be an edge rather than a state. Writing "manual" every frame
            // would take the vehicle away from a player who is flying it by hand, on a computer
            // that is switched off.
            if (_driving) Abort("disarmed");
            Command = Program.Update(simStep, Sample(playerStep, out _));
            return;
        }

        IcbmState state = Sample(playerStep, out bool usable);
        if (!usable)
        {
            Command = Program.Update(simStep, state);
            return;
        }

        Command = Program.Update(simStep, state);

        // One line per phase change. Every gate in the program returns quietly, so a flight that
        // goes wrong leaves nothing behind saying which of them it went wrong at - and the panel
        // only shows the state it is in now, not the order it got there.
        if (Command.Phase != _reported)
        {
            _reported = Command.Phase;
            Log.Info($"{KsaWorld.DisplayName(Craft)} ICBM: {Command.Phase} at "
                     + $"{AltitudeMetres / 1000.0:F0} km, {Command.VelocityToGain:F0} m/s to gain, "
                     + $"burn in {IcbmProgram.Clock(Command.SecondsToBurn)}, "
                     + $"impact in {IcbmProgram.Clock(SecondsToArrival)}, "
                     + $"target {OffPlaneDegrees:F1} deg off plane ({PlaneChangeCost:F0} m/s), "
                     + $"reach {Command.Reach}"
                     + (double.IsFinite(Program.ResidualAtCutoff)
                            ? $", cut off {Program.ResidualAtCutoff:F2} m/s short{ResidualSaid()}"
                            : "")
                     // The mod's own prediction against its own aim. Near zero means the solution
                     // is self-consistent and whatever missed happened to the round afterwards;
                     // large means the arc never pointed at the target and the burn flying it
                     // perfectly was never going to help.
                     + PredictedImpactSaid()
                     + $" :: {Command.Hold}");
        }

        Predict(simStep, state);

        // Read before anything is written this frame. KSA replaces the whole flight computer from
        // its worker every frame, so this is what survived of last frame's command — and comparing
        // it with what is read straight after writing is the only way to tell a write that never
        // lands from one the engine reverts.
        FlightComputerAttitudeMode wasMode = Craft.FlightComputer.AttitudeMode;
        FlightComputerAttitudeTrackTarget wasTrack = Craft.FlightComputer.AttitudeTrackTarget;

        // At cutoff rather than at the first release, which on a nominal shot is the same frame:
        // the launcher has the whole coast to settle, and every kilogram of spent stack is mass its
        // own thrusters would otherwise have to turn between releases.
        //
        // Both of these run before anything decides whether a warhead may go, and the ordering is
        // the point. The decoupler's shove is about a metre a second and it arrives after the last
        // thing that could compensate for it; letting a round go on the same frame the split is
        // asked for sends one warhead on the attached stack's solution and the rest on the shoved
        // bus's. Measured in flight as a 163 m outlier inside a 3.6 km group.
        if (Command.ReadyToDeploy) SeparateOnce(release);

        DriveTrim(simStep, state, release);

        // Attitude is driven for every phase that is doing something, not only while an engine is
        // lit. A hold can be an hour long and the vehicle is pointed at the burn for all of it; and
        // after cutoff the bus has to keep the line it was cut off on for the warheads to leave
        // along. Both were left free before, which is a vehicle drifting when it should be settled.
        bool aimed = false;

        if (Command.Phase is not (IcbmPhase.Idle or IcbmPhase.NoSolution))
        {
            // Advanced on the *nominal* line, not on the sequencer's offset one. The carried
            // reference is about continuity, and a direction that steps by a cant six times would
            // re-flatten it six times for nothing.
            _rollReference = AimFrame.Advance(_rollReference, Command.ThrustDirectionCci,
                                              -Vec.Unit(state.PositionCci), RollFallback(state));

            _deploy = DriveDeployment(simStep, release, state);

            // Once per change of *state*, not per change of the number in it: the angle counts down
            // by a tenth of a degree a frame, and deduplicating on the whole sentence writes sixty
            // lines a turn. What is worth a line is that it started turning, started settling, or
            // gave up - and a sequence that stalls still looks exactly like one that has finished
            // if none of it is said at all.
            string stage = _deploy.Said.Length > 0 ? _deploy.Said.Split(',')[0] : "";

            if (stage != _saidLast)
            {
                _saidLast = stage;
                if (_deploy.Said.Length > 0) Log.Info($"deploying: {_deploy.Said}");
            }

            // Handed to the hook rather than written here. A write from this pass is discarded
            // before anything reads it - see AttitudeHook.
            AttitudeHook.Hold(Craft, _deploy.DirectionCci, _deploy.RollCci);
            aimed = AttitudeHook.Installed;
            if (aimed) _driving = true;
        }
        else
        {
            AttitudeHook.Release(Craft);
        }

        ProbeAttitude(playerStep, wasMode, wasTrack, aimed);

        if (Command.EngineOn)
        {
            _throttleAchieved = VehicleCommand.DriveThrottle(Craft, Command.Throttle);
            VehicleCommand.SetEngine(Craft, running: true);

            // Never past the launcher. A stage runs dry with the engines still commanded on and
            // the program asks for the next sequence every second and a half; if the joint holding
            // the launcher is the next thing in that list, a shot that fell short drops its rounds
            // instead of holding them.
            if (Command.RequestStage && !StagingWouldDropTheLauncher(release))
            {
                Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} staging: {Command.Hold}");
                VehicleCommand.Stage(Craft);
            }
        }
        else if (_driving)
        {
            VehicleCommand.SetEngine(Craft, running: false);
            _throttleAchieved = VehicleCommand.DriveThrottle(Craft, 1.0);
        }

        if (Config.AutoRelease && _deploy.ReleaseNow) Release(release);

        CarryOurWarp();
        CarryTheView();
    }

    /// <summary>
    /// Hand the wait to KSA's own warp-to-a-time. Pressed, never automatic.
    ///
    /// <para>Warping is an action rather than a setting, and taking the player's time control
    /// because a target happened to be designated is not a thing a weapon gets to do. They may have
    /// set a tenth speed to watch something.</para>
    ///
    /// <para>One press covers the whole wait, in hops. That is not tidiness — it is the only way
    /// the handover can work. KSA scales its warp rate to the <em>span</em> it is asked to cover, so
    /// a single jump to the end of a ninety-minute hold arrives doing thousands of times normal
    /// speed, where the last minute of it passes in under two frames and there is nowhere to hand
    /// over. Each hop leaves a margin, and the next one covers a shorter span and so runs gentler,
    /// until the approach is slow enough to be caught.</para>
    /// </summary>
    public bool TryWarpToWindow()
    {
        if (!CanWarpToWindow) return false;

        double wait = Command.SecondsToBurn;
        double margin = Math.Clamp(wait * MarginFraction, IcbmProgram.WarpHoldLeadSeconds, MaxMarginSeconds);

        if (!KsaWorld.TryAutoWarpTo(wait, margin)) return false;

        _warpIsOurs = true;
        Log.Info($"warping to within {IcbmProgram.Clock(margin)} of the burn window on "
                 + $"{KsaWorld.DisplayName(Craft)}, {IcbmProgram.Clock(wait)} to go");
        return true;
    }

    /// <summary>Whether the window is far enough away for warping to it to be worth offering.</summary>
    public bool CanWarpToWindow
        => Program.Phase == IcbmPhase.Holding
        && !KsaWorld.IsAutoWarpActive
        && ReferenceEquals(Craft, KsaWorld.ControlledVehicle)
        && double.IsFinite(Command.SecondsToBurn)
        && Command.SecondsToBurn > IcbmProgram.WarpHoldLeadSeconds * 2.0;

    // Carries a warp this computer started through to the window, and ends it if the shot stops
    // wanting one. Only ever a warp it started: one the player started is theirs.
    private void CarryOurWarp()
    {
        if (!_warpIsOurs) return;

        if (Program.Phase != IcbmPhase.Holding)
        {
            if (KsaWorld.IsAutoWarpActive)
            {
                Log.Info($"stopping the warp on {KsaWorld.DisplayName(Craft)}, the hold is over");
                KsaWorld.StopAutoWarp();
            }

            _warpIsOurs = false;
            return;
        }

        // Still running: leave it alone. It stops itself at the margin, which is the whole reason
        // for asking it to stop short rather than braking the world by hand.
        if (KsaWorld.IsAutoWarpActive) return;

        // A hop finished. Close the remaining gap with another, shorter and therefore slower one.
        if (CanWarpToWindow && TryWarpToWindow()) return;

        _warpIsOurs = false;
    }

    // Hand the wait to KSA's own warp-to-a-time. Only while holding, only for the craft being
    // flown, and only out to a margin short of the burn - the last minute belongs to WarpPolicy,
    // which cannot slow the world down at all while an auto-warp is running.

    /// <summary>
    /// Follow the weapon onto the craft that now carries it, after a decoupler split the stack.
    ///
    /// <para>The flight continues. The phase, the held cutoff line, the aim bias, the roll
    /// reference and the target are all about the shot rather than about the hull, and they come
    /// across by staying where they are — which is the argument for rehoming rather than building a
    /// fresh computer. A fresh one re-enters the phase machine at
    /// <see cref="IcbmPhase.Holding"/>, and only <c>Coast</c> ever sets <c>ReadyToDeploy</c>, so it
    /// would never release a warhead at all.</para>
    /// </summary>
    public void Rehome(Vehicle craft)
    {
        if (!KsaWorld.IsAlive(craft) || ReferenceEquals(craft, Craft)) return;

        Vehicle left = Craft;

        // Before anything else: the hook is keyed on the vehicle and is only ever cleared here, so
        // without this the spent stack is held on the cutoff line for the rest of the session.
        AttitudeHook.Release(left);

        // And hand it back the way a player expects to find it. Only what this mod switched on.
        if (_driving)
        {
            VehicleCommand.SetEngine(left, running: false);
            VehicleCommand.ReleaseAttitude(left);
        }

        // Unconditionally, and not with the rest. The rehome lands a frame after the split, so the
        // trim can already have commanded the half being left behind - and a thruster flag is a
        // held key, so nothing would ever let go of it again.
        VehicleCommand.DriveTranslation(left, TrimAxes.None);

        Craft = craft;

        Log.Info($"ICBM computer followed its weapon from {KsaWorld.DisplayName(left)} "
                 + $"onto {KsaWorld.DisplayName(craft)}");

        // And take the player with it, but only if they were watching the thing that just split.
        // Somebody flying an aircraft on the other side of the planet did not ask to be moved.
        //
        // Staged rather than done here, and no longer because the engine refuses it - GoTo stopped
        // rebuilding derived data, which was the thing it refused. What is left is ordering: a
        // handover is decided during the panel's own pass, and taking the player's camera in the
        // middle of it moves the craft out from under a panel that has already read which one it
        // is showing.
        if (KsaWorld.IsWatching(left)) _viewWanted = craft;

        // Held only until the trim has run, and only to measure a distance from. The stack is alive
        // rather than destroyed, so this is not the reference CLAUDE.md's rule about dead vehicles
        // is about — but it is still dropped the moment it has nothing left to answer.
        _separatedFrom = left;
    }

    // Deferred out of Rehome, which runs inside the engine's update pass.
    private void CarryTheView()
    {
        if (_viewWanted is not { } craft) return;
        _viewWanted = null;

        if (!KsaWorld.IsAlive(craft)) return;

        if (KsaWorld.GoTo(craft))
        {
            Log.Info($"view moved to {KsaWorld.DisplayName(craft)}, which is where the warheads are");
        }
    }

    // Whether the next sequence would fire the joint the launcher hangs on. The staging list is
    // the player's and the mod does not read it; asking whether the launcher could come off is
    // enough, because a launcher that cannot separate cannot be staged away either.
    private static bool StagingWouldDropTheLauncher(IManualFire? weapon)
        => weapon is { CanSeparate: true };

    // Once, and only where the part tree offers a joint to let go of. Nothing shipped declares one,
    // so this does nothing until a craft is built with a decoupler under its launcher.
    private void SeparateOnce(IManualFire? weapon)
    {
        if (_separated || weapon is null) return;

        if (!weapon.CanSeparate)
        {
            _separated = true;
            return;
        }

        // Latched before the call, not after: the split lands a frame later and the module does not
        // de-duplicate, so a second request queues a second decouple.
        _separated = true;

        if (weapon.Separate())
        {
            _awaitingSplit = true;
            _didSplit = true;

            Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} separating the launcher "
                     + "from the stack before deploying");
        }
    }

    // The world half of SeparationClearance: how far apart the two actually are. Both positions
    // come from the same instant, so the ecliptic motion they each carry cancels in the difference
    // - see docs/FRAMES-AND-EPOCHS.md. NaN rather than zero when the stack cannot be read, because
    // the two mean opposite things there.
    private Clearance Clear(double simStep)
    {
        _sinceSplit += simStep;

        double apart = double.NaN;

        if (_separatedFrom is { } stack && KsaWorld.IsAlive(stack))
        {
            double3 between = KsaWorld.PositionEcl(stack) - KsaWorld.PositionEcl(Craft);
            if (Vec.IsFinite(between)) apart = Vec.Len(between);
        }

        // An unreadable stack falls back to the clock rather than to "clear": a part tree
        // mid-rebuild reads as no distance at all, and treating that as clearance is exactly the
        // case this exists to prevent.
        return SeparationClearance.Check(apart, _sinceSplit);
    }

    // One line per change of state, which is all any of this is worth while nothing is happening
    // on screen. The detail rides along with it rather than driving it: a number that moves every
    // frame would otherwise log every frame.
    private void Say(string what, string detail = "")
    {
        if (what == _saidTrim) return;

        _saidTrim = what;
        Log.Info($"trimming the bus on {KsaWorld.DisplayName(Craft)}: {what}{detail}");
    }

    // Put the bus back on its solution with its own thrusters, before anything leaves it. All the
    // deciding is in BusTrim; what is here is the same two conversions as everywhere else in this
    // file - the world into a situation and the answer into writes on somebody else's vehicle -
    // plus the one thing only this side can know, which is whether the split has actually landed.
    private void DriveTrim(double simStep, in IcbmState state, IManualFire? weapon)
    {
        if (!Config.TrimBeforeRelease || !Command.ReadyToDeploy)
        {
            if (_trim.Firing != TrimAxes.None) VehicleCommand.DriveTranslation(Craft, TrimAxes.None);
            return;
        }

        // The error this exists to remove arrives *with* the split, and the split is deferred
        // through the engine's input buffer. Asking the joint again is the split itself rather than
        // a timer: the decoupler that was there is the one that just came apart, so the question
        // stops answering yes the moment it has. A launcher that never had one is past this
        // already, because SeparateOnce never set the flag.
        if (_awaitingSplit)
        {
            if (weapon is { CanSeparate: true }) return;
            _awaitingSplit = false;
        }

        // Armed at the split rather than at clearance, and held rather than skipped. It keeps
        // solving through the whole wait, so what the bus owes its solution is on record from the
        // moment the decoupler fired — which is the only thing that separates an error the
        // separation caused from one that grew while the vehicle coasted clear of it.
        Clearance clearance = _didSplit ? Clear(simStep) : new Clearance(true, false, "");
        _mayTrim = clearance.IsClear;

        _trim.Begin();

        double3 nose = Vec.Zero;
        double3 right = Vec.Zero;
        double3 down = Vec.Zero;

        // A frame that will not resolve is handed in as zeroes rather than skipped, because the
        // trim's own budget is what has to bound a part tree that never comes back - and it can
        // only run that budget down if it is being stepped.
        if (Parent is { } parent)
        {
            KsaWorld.TryControlFrameCci(Craft, parent, out nose, out right, out down);
        }

        TrimCommand trim = _trim.Update(simStep, new TrimSituation(
            state.Body, state.PositionCci, state.VelocityCci, state.AimNowCci,
            Program.CommittedArrivalFromNow, nose, right, down, _mayTrim));

        VehicleCommand.DriveTranslation(Craft, trim.Fire);

        if (!double.IsFinite(_owedAtSplit) && double.IsFinite(trim.ToGainMetresPerSecond))
        {
            _owedAtSplit = trim.ToGainMetresPerSecond;
        }

        // Nothing left to measure a distance from once the trim has run.
        if (trim.Done) _separatedFrom = null;

        // Said once per change. A trim that stalls looks exactly like one that has finished, and
        // the difference between them is kilometres on the ground.
        if (trim.Said.Length == 0) return;

        // Two audiences, one state. The panel gets a sentence it can fit; the log gets the numbers
        // that diagnose it, which are long enough to run off the edge of a narrow window.
        Say(_mayTrim ? trim.Said : clearance.Said + "; " + trim.Said,
            (trim.Acceleration > 0.0 ? $" (thrusters measured at {trim.Acceleration:F3} m/s2)" : "")
            + Grew()
            + Arrivals(trim));
    }

    // Which arrival the trim is solving to, beside when the flown prediction says the warheads
    // actually get there. Printed only when the answer is too large to be a separation, because
    // that is the one case where it is the first thing to check: what the trim nulls is
    // RequiredVelocity(arrival) - v, and on a deorbit that required velocity moves about 20 m/s
    // for every second the arrival is out. A trim asking for hundreds is a handful of seconds of
    // disagreement long before it is anything wrong with the vehicle.
    //
    // The two are not the same quantity and need not match: the arrival is when a vacuum transfer
    // reaches the aim point, the prediction is when a warhead with drag reaches the ground. Their
    // gap is the measurement, not an error on its face.
    private string Arrivals(in TrimCommand trim)
    {
        if (!(trim.ToGainMetresPerSecond > BusTrim.MaxMetresPerSecond)) return "";

        double committed = Program.CommittedArrivalFromNow;
        double flown = PredictedImpact?.Seconds ?? double.NaN;

        if (!double.IsFinite(committed)) return " [no committed arrival]";

        return double.IsFinite(flown)
            ? $" [solving to an arrival {committed:F0} s away; the flown prediction says {flown:F0} s]"
            : $" [solving to an arrival {committed:F0} s away; nothing predicted]";
    }

    // What coasting clear cost, said only when it cost something. The same number at the split and
    // at the release means the wait was free and the error came off the decoupler; a number that
    // has grown means something moved the vehicle or the aim while it waited, which is a different
    // fault with a different fix.
    private string Grew()
    {
        double atRelease = _trim.AtReleaseMetresPerSecond;

        if (!double.IsFinite(atRelease) || !double.IsFinite(_owedAtSplit)) return "";
        if (Math.Abs(atRelease - _owedAtSplit) < 0.05) return "";

        return $" [owed {_owedAtSplit:F2} m/s at the split, {atRelease:F2} after "
               + $"{_sinceSplit:F0} s of clearing]";
    }

    /// <summary>Let one warhead go at the aim point, if there is one to let go and it is ready.</summary>
    public bool Release(IManualFire? weapon)
    {
        if (weapon is null || !weapon.ReadyToFire) return false;
        if (TargetEcl() is not { } targetEcl) return false;

        bool away = weapon.FireAt(targetEcl);
        if (away) ProbeRelease();
        return away;
    }

    // What the prediction says about the state the warhead is actually leaving on, beside where the
    // round then lands. The phase line cannot answer this: it is printed before the frame's Predict
    // and while the engines are still lit, so it carries a prediction of the *solved* cutoff arc.
    // Only these two numbers isolate what the prediction and the round still disagree about, which
    // is the difference every remaining metre of the miss lives in.
    //
    // At INFO, and it earns it: this fires once per warhead released rather than per frame, and a
    // diagnostic nobody has switched on is one that is never there in the salvo that needed it.
    private void ProbeRelease()
    {
        if (Parent is not { } parent) return;
        if (_warhead is not { } warhead) return;

        try
        {
            doubleQuat cce2Cci = parent.GetCce2Cci();
            double3 positionCci = (KsaWorld.PositionEcl(Craft) - parent.GetPositionEcl()).Transform(cce2Cci)
                                  + ReleaseOffsetCci();
            double3 velocityCci = (KsaWorld.VelocityEcl(Craft) - parent.GetVelocityEcl()).Transform(cce2Cci)
                                  + ReleaseImpulseCci();

            if (!ImpactPredictor.TryPredict(Body, positionCci, velocityCci, PredictStepSeconds,
                                            ImpactPredictor.DefaultMaxSeconds,
                                            out ImpactPredictor.Impact hit, TerrainRadiusAt, null,
                                            new ImpactPredictor.Drag(DensityRatioAt, warhead)))
            {
                Log.Info("release probe: no impact predicted from the release state");
                return;
            }

            double3 cce = hit.GroundFixedPointCci.Transform(parent.GetCci2Cce());
            double miss = Body.SurfaceRadius * Vec.AngleBetween(hit.GroundFixedPointCci, _trueAimCci);

            // The angle this reports has to match the one the release line reports for the tube.
            // The prediction throws the warhead along the direction the vehicle was commanded to
            // hold; the round actually leaves along its tube. If those disagree, two metres a
            // second is being applied in the wrong direction, and radially that is 3.4 km per m/s.
            double3 impulse = ReleaseImpulseCci();
            string thrown = impulse.Equals(Vec.Zero)
                ? ""
                : $", {(_releaseMeasured ? "measured" : "assumed")} thrown "
                  + $"{Vec.AngleBetween(impulse, velocityCci) * 180.0 / Math.PI:F0} deg from the "
                  + $"platform's track, {Vec.Len(ReleaseOffsetCci()):F1} m off the orbit position";

            Log.Info($"release probe: predicted from the release state -> "
                      + $"{parent.GetLatitudeFromCce(cce):F3},{parent.GetLongitudeFromCce(cce):F3}, "
                      + $"{miss / 1000.0:F1} km from the target, {hit.Seconds:F0} s of flight{thrown}");
        }
        catch
        {
            // A probe that throws inside the frame hook is worse than one that says nothing.
        }
    }

    /// <summary>The trajectory, in the ecliptic, for drawing. Empty until a prediction has run.</summary>
    // What the flight computer makes of the attitude it is being given, which is the only way to
    // tell a command that is swinging from a vehicle that cannot hold a steady one. Both look like
    // tumbling from outside, and they want opposite fixes.
    private void ProbeAttitude(double playerStep, FlightComputerAttitudeMode wasMode,
                               FlightComputerAttitudeTrackTarget wasTrack, bool aimed)
    {
        if (!Program.IsBurning || Log.Threshold > Log.Level.Debug) return;

        _sinceProbe += playerStep;
        if (_sinceProbe < ProbeIntervalSeconds) return;
        _sinceProbe = 0.0;

        double3 wanted = Vec.Unit(Command.ThrustDirectionCci);
        double slew = _lastCommanded.Equals(Vec.Zero)
                          ? 0.0
                          : Vec.AngleBetween(_lastCommanded, wanted) * 180.0 / Math.PI;
        _lastCommanded = wanted;

        FlightComputer computer = Craft.FlightComputer;

        Log.Debug($"{KsaWorld.DisplayName(Craft)} attitude: aimed={aimed} "
                  + $"dir={(Vec.Len(Command.ThrustDirectionCci) > 0.0 ? "set" : "ZERO")} "
                  + $"slew {slew:F1} deg | before {wasMode}/{wasTrack} "
                  + $"-> after {computer.AttitudeMode}/{computer.AttitudeTrackTarget} | "
                  + $"error {computer.ErrorAngles} rates {computer.ErrorRates}");
    }

    // Where the mod thinks the arc lands, as a place rather than a distance. A distance says the
    // solution is wrong; the place says which way, and short-versus-sideways are different faults.
    private string PredictedImpactSaid()
    {
        if (!double.IsFinite(PredictedMissMetres) || PredictedImpact is not { } hit) return "";
        if (Parent is not { } parent) return "";

        try
        {
            // The prediction is un-carried to its own epoch, so it is a place on the ground in the
            // same terms the aim point is - which is what makes the two comparable at all.
            double3 cce = hit.GroundFixedPointCci.Transform(parent.GetCci2Cce());

            return $", own prediction {PredictedMissMetres / 1000.0:F1} km off "
                   + $"(lands {parent.GetLatitudeFromCce(cce):F3},{parent.GetLongitudeFromCce(cce):F3})";
        }
        catch
        {
            return $", own prediction {PredictedMissMetres / 1000.0:F1} km off";
        }
    }

    // Something square to the vertical for the roll to clock to when the planet cannot supply one,
    // which is the whole of a vertical rise. Downrange is horizontal by construction; before there
    // is one, the way the vehicle is already moving will do.
    private double3 RollFallback(in IcbmState state)
        => Program.DownrangeCci.Equals(Vec.Zero) ? state.VelocityCci : Program.DownrangeCci;

    public void PathEcl(List<double3> into)
    {
        into.Clear();
        if (Parent is null) return;

        doubleQuat cci2Cce = Parent.GetCci2Cce();
        double3 centre = Parent.GetPositionEcl();

        for (int i = 0; i < _path.Count; i++) into.Add(_path[i].Transform(cci2Cce) + centre);
    }

    /// <summary>Where the aim point is right now, in the ecliptic. Null when nothing is designated.</summary>
    public double3? TargetEcl()
    {
        if (Parent is null || !Target.IsSet) return null;
        return SurfacePointEcl(Parent, Target.LatitudeDeg, Target.LongitudeDeg);
    }

    private IcbmState Sample(double playerStep, out bool usable)
    {
        usable = false;

        Parent = KsaWorld.ParentBody(Craft);
        if (Parent is null) return default;

        double mu = Parent.Mass * GravitationalConstant;

        // The spin axis is exactly +Z in a body's own Cci: KSA builds Ccf from Cci by rotating
        // about UnitZ and nothing else, so there is no obliquity term to carry here. It is the
        // *ecliptic* that sees the tilt.
        Body = new BallisticBody(mu, Parent.MeanRadius, new double3(0, 0, 1), Parent.GetAngularVelocity());

        doubleQuat cce2Cci = Parent.GetCce2Cci();
        double3 positionCci = (KsaWorld.PositionEcl(Craft) - Parent.GetPositionEcl()).Transform(cce2Cci);
        double3 velocityCci = (KsaWorld.VelocityEcl(Craft) - Parent.GetVelocityEcl()).Transform(cce2Cci);

        double3 aimCci = default;
        bool hasAim = false;

        if (Target.IsSet && Target.BodyName == Parent.Id)
        {
            _trueAimCci = (SurfacePointEcl(Parent, Target.LatitudeDeg, Target.LongitudeDeg)
                           - Parent.GetPositionEcl()).Transform(cce2Cci);

            // Aimed at the target plus whatever the flown prediction says the arc is losing. The
            // solver is exact for a *point* in vacuum; the round stops where the ground actually
            // is, and on a shallow arrival over rising terrain that is tens of kilometres short of
            // a summit. Correcting the aim is the only thing that closes it, because there is
            // nothing wrong with the trajectory - it arrives exactly where it was asked to.
            aimCci = _aim.Apply(_trueAimCci);
            hasAim = true;
        }

        FlightComputer computer = Craft.FlightComputer;
        ActiveEnginePerformance engines = computer.ActiveEnginePerformanceMax;

        BoosterPerformance booster = new(engines.Thrust, engines.MassFlowRate,
                                         Craft.TotalMass, Craft.PropellantMass);

        double density = KsaWorld.MediumDensityRatioAt(Parent, KsaWorld.PositionEcl(Craft));

        usable = Body.IsUsable;
        AltitudeMetres = Body.AltitudeOf(positionCci);

        if (hasAim)
        {
            double off = OrbitPlane.OffPlaneRadians(positionCci, velocityCci, aimCci);
            OffPlaneDegrees = off * 180.0 / Math.PI;
            PlaneChangeCost = OrbitPlane.PlaneChangeCost(Vec.Len(velocityCci), off);
        }
        else
        {
            OffPlaneDegrees = 0.0;
            PlaneChangeCost = 0.0;
        }

        return new IcbmState(Body, positionCci, velocityCci, aimCci, hasAim, booster, density,
                             Craft.IsAnyEnginePropellantAvailable(), _throttleAchieved, playerStep);
    }

    // Where the ground actually is under a point on the arc. Without this the prediction flies
    // down to the mean sphere while the round it is predicting stops on terrain, and on a shallow
    // deorbit that gap is enormous: the arc covers about twelve kilometres of ground per kilometre
    // of height near the end, so a target four kilometres up - which is most of the Andes - puts
    // the prediction fifty kilometres past where anything actually lands.
    //
    // The point arrives un-carried to the prediction's own epoch, so the body-fixed frame to read
    // it in is the one at that epoch, which is the current one.
    private double TerrainRadiusAt(double3 pointCci)
    {
        if (Parent is not { } parent) return Body.SurfaceRadius;

        try
        {
            double3 dirCcf = Vec.Unit(pointCci).Transform(parent.GetCci2Ccf());
            if (!Vec.IsFinite(dirCcf) || dirCcf.Equals(Vec.Zero)) return Body.SurfaceRadius;

            // Accurate, because GroundTest is accurate and the round stops where *it* says. A
            // coarse sample is a different height field, and on a shallow arrival every metre of
            // disagreement is about eleven metres of ground. Affordable because ImpactPredictor
            // only asks near the surface.
            double height = parent.GetTerrainHeightFromDirCcf(dirCcf, accurate: true);
            return double.IsFinite(height) ? parent.MeanRadius + height : Body.SurfaceRadius;
        }
        catch
        {
            return Body.SurfaceRadius;
        }
    }

    // Which way the leftover points, because that is what decides what it costs: on a deorbit a
    // metre a second left along the track is about 1.8 km of miss and the same metre left radially
    // is about 3.4 km. The acceleration and step come with it because together they are the floor -
    // one frame of burning is accel x step x throttle, and a residual near that is a timing limit
    // rather than a guidance error, which wants a completely different fix.
    private string ResidualSaid()
    {
        double3 leftover = Program.ResidualVectorCci;
        if (leftover.Equals(Vec.Zero) || !Vec.IsFinite(leftover)) return "";

        double3 up = Vec.Unit(Program.CutoffPositionCci);
        double3 along = Vec.Unit(Vec.Cross(Vec.Cross(up, Program.Arc?.RequiredVelocityCci ?? up), up));

        if (along.Equals(Vec.Zero)) return "";

        double radial = Vec.Dot(leftover, up);
        double track = Vec.Dot(leftover, along);
        double cross = Vec.Len(leftover - up * radial - along * track);

        // The frame quantum at the throttle the stack actually had, beside the same quantum at
        // full thrust. A commanded ramp that never arrives makes those two equal, and is otherwise
        // indistinguishable from never having asked for one.
        double full = Program.AccelerationAtCutoff * Program.StepAtCutoff;
        double achieved = Program.ThrottleAtCutoff;

        return $" ({track:F2} along, {radial:F2} radial, {cross:F2} cross"
               + $"; one frame is {full * (double.IsFinite(achieved) ? achieved : 1.0):F3} m/s at "
               + $"{achieved:P0} throttle, {full:F2} at full)";
    }

    // A warhead does not leave on the bus's velocity. Each is ejected along its own tube at the
    // munition's LaunchSpeed, and a bus's tube cants cancel in the mean, so what survives is the
    // whole of it along the nose.
    //
    // On a deorbit that nose is held retrograde - it is the attitude the braking burn ended on - so
    // the ejection *slows* every warhead and they all fall short together. Predicting the bus's arc
    // rather than the round's leaves that invisible to the aim correction, and this trajectory
    // moves about two kilometres per metre per second.

    // What the launcher should be holding, and whether a round may go. The sequencer turns the
    // vehicle so the tube about to fire lies on the line the aim correction assumed - which for a
    // launcher whose tubes are not canted is the line it is already on, and costs nothing.
    private ReleaseCommand DriveDeployment(double simStep, IManualFire? weapon, in IcbmState state)
    {
        double3 held = Command.ThrustDirectionCci;
        double3 roll = _rollReference;

        // The trim is a precondition of being ready rather than a step inside the sequence, and
        // that is what keeps the sequencer's reference honest: it latches the tube axes on the
        // first frame the launcher is both ready and settled, and a reference latched before the
        // decoupler's shove has been taken back out describes a line no warhead will leave on.
        bool trimming = Config.TrimBeforeRelease && Command.ReadyToDeploy && !_trim.Done;

        if (weapon is null || !Command.ReadyToDeploy || trimming)
        {
            return new ReleaseCommand(held, roll, false, -1, 0.0, "");
        }

        int next = weapon.NextTube;

        // Latched at the attitude the aim correction converged against, which is this one: the
        // first frame the launcher is both ready to deploy and no longer turning.
        if (!_sequence.Begun && Config.RepointBetweenReleases && !(_tubeSpinSpeed > ReleaseSequence.SteadyMetresPerSecond))
        {
            int found = weapon.TubeAxesEcl(_tubeAxes);
            if (found > 0 && Parent is { } parent)
            {
                doubleQuat cce2Cci = parent.GetCce2Cci();
                for (int i = 0; i < found; i++) _tubeAxes[i] = _tubeAxes[i].Transform(cce2Cci);

                if (_sequence.Begin(_tubeAxes.AsSpan(0, found)))
                {
                    Log.Info($"aiming each of {found} tube(s) before it fires");
                }
            }
        }

        double3 nextAxis = Vec.Zero;
        if (next >= 0 && Parent is { } body && weapon.TubeAxesEcl(_tubeAxes) > next)
        {
            nextAxis = _tubeAxes[next].Transform(body.GetCce2Cci());
        }

        // How long the release window has left, from the descent rather than from the arrival: it
        // closes when the launcher falls through the deploy altitude, not when the rounds land.
        double descent = -Vec.Dot(state.VelocityCci, state.UpCci);
        double window = descent > 0.0
                            ? (AltitudeMetres - Config.DeployAltitudeMetres) / descent
                            : double.NaN;

        return _sequence.Update(simStep, new ReleaseSituation(
            ReadyToDeploy: true, NextTube: next, TubesLeft: Math.Max(1, weapon.TubesReadyToFire),
            NextTubeAxisCci: nextAxis, SweepMetresPerSecond: _tubeSpinSpeed,
            SecondsLeftToDeploy: window, HeldDirectionCci: held, HeldRollCci: roll));
    }

    // Where a released round starts, as a difference from where the craft is.
    //
    // A difference rather than a state, because the prediction is taken from two different places -
    // the solved cutoff while burning, the live state while coasting - and the tube's offset from
    // the craft applies to both. It carries all three things a prediction taken from the orbit
    // state gets wrong: the tube mouth is metres away, the lever arm is sweeping, and the round is
    // thrown along the tube rather than along whatever attitude was commanded.
    private void MeasureRelease(IManualFire? weapon)
    {
        _releaseMeasured = false;
        _releaseOffsetCci = Vec.Zero;
        _releaseKickCci = Vec.Zero;
        _tubeSpinSpeed = 0.0;

        if (weapon is null || Parent is not { } parent) return;

        try
        {
            if (!weapon.TryMeanReleaseStateEcl(out double3 positionEcl, out double3 velocityEcl,
                                               out double spinSpeed))
            {
                return;
            }

            _tubeSpinSpeed = spinSpeed;

            doubleQuat cce2Cci = parent.GetCce2Cci();
            double3 offset = (positionEcl - KsaWorld.PositionEcl(Craft)).Transform(cce2Cci);
            double3 kick = (velocityEcl - KsaWorld.VelocityEcl(Craft)).Transform(cce2Cci);

            if (!Vec.IsFinite(offset) || !Vec.IsFinite(kick)) return;

            _releaseOffsetCci = offset;
            _releaseKickCci = kick;
            _releaseMeasured = true;
        }
        catch
        {
            // A launcher whose tubes will not resolve falls back to the commanded attitude below.
        }
    }

    // The fallback when the tubes cannot be resolved: the munition's ejection speed along the
    // direction the vehicle was told to hold. Wrong by however far the vehicle settled off that
    // command, which is why it is second choice rather than the rule.
    private double3 ReleaseImpulseCci()
    {
        // Once the sequence is turning the vehicle, the line every round leaves on is the latched
        // reference and nothing else. The *live* mean of the tube axes swings by a full cant as
        // each tube is brought onto that line, so predicting with it describes a round nobody is
        // about to release - and feeds the aim correction a target that moves six times a salvo.
        //
        // It is also simply the right number: a re-pointed tube throws the whole LaunchSpeed along
        // the reference, not its cosine.
        if (_sequence.Begun && _warhead is { LaunchSpeed: > 0f } aimed)
        {
            return _sequence.ReferenceCci * aimed.LaunchSpeed;
        }

        if (_releaseMeasured) return _releaseKickCci;
        if (_warhead is not { LaunchSpeed: > 0f } warhead) return Vec.Zero;

        double3 nose = Command.ThrustDirectionCci;
        return nose.Equals(Vec.Zero) || !Vec.IsFinite(nose) ? Vec.Zero : Vec.Unit(nose) * warhead.LaunchSpeed;
    }

    private double3 ReleaseOffsetCci() => _releaseMeasured ? _releaseOffsetCci : Vec.Zero;

    // How thick the air is at a point on the arc. The same field the round's own drag is read from,
    // so the prediction and the round cannot disagree about the atmosphere they are flying through.
    private double DensityRatioAt(double3 pointCci)
    {
        if (Parent is not { } parent) return 0.0;

        try
        {
            double3 positionEcl = pointCci.Transform(parent.GetCci2Cce()) + parent.GetPositionEcl();
            double density = KsaWorld.MediumDensityRatioAt(parent, positionEcl);
            return double.IsFinite(density) && density > 0.0 ? density : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    // The aim point sits on the real ground rather than on the mean sphere, and that is not a
    // refinement. The whole solve is a transfer between two *points*, so a target standing five
    // kilometres up is hit by aiming at where it stands - no terrain model anywhere else in the
    // guidance, and no correction to apply afterwards.
    private static double3 SurfacePointEcl(Celestial body, double latitudeDeg, double longitudeDeg)
    {
        double3 dirCcf = body.GetDirCcfFromLatLon(latitudeDeg, longitudeDeg);
        double height = body.GetTerrainHeightFromDirCcf(dirCcf, accurate: true);
        return dirCcf.Transform(body.GetCcf2Cce()) * (body.MeanRadius + height) + body.GetPositionEcl();
    }

    private void Predict(double simStep, in IcbmState state)
    {
        _sincePredict += simStep;
        if (_sincePredict < PredictIntervalSeconds) return;
        _sincePredict = 0.0;

        // While the engines are running, predict from where the arc *departs* rather than from
        // where the vehicle is. The current state is mid-burn and describes a trajectory nobody
        // intends to fly, so a correction driven by it never sees the shot being aimed - which
        // leaves the aim uncorrected for the whole burn, and by the coast the arc is fixed and the
        // warheads are already going.
        bool fromCutoff = Program.IsBurning && Program.Arc is not null;

        double3 fromCci = fromCutoff ? Program.CutoffPositionCci : state.PositionCci;
        double3 alongCci = fromCutoff ? Program.Arc!.Value.RequiredVelocityCci : state.VelocityCci;

        fromCci += ReleaseOffsetCci();
        alongCci += ReleaseImpulseCci();

        // Predicted with the warhead's drag rather than in vacuum. On a shallow deorbit arrival a
        // vacuum arc lands tens of kilometres beyond anything that actually flies it, and the aim
        // correction reads its own drag-free prediction - so it converges, reports zero, and the
        // rounds go on falling short. Measured at 54.6 km.
        ImpactPredictor.Drag? air =
            _warhead is { } warhead ? new ImpactPredictor.Drag(DensityRatioAt, warhead) : null;

        if (ImpactPredictor.TryPredict(Body, fromCci, alongCci, PredictStepSeconds,
                                       ImpactPredictor.DefaultMaxSeconds, out ImpactPredictor.Impact hit,
                                       TerrainRadiusAt, _path, air))
        {
            PredictedImpact = hit;

            // Measured against the *target*, not against the biased aim: the bias is the correction
            // being applied, so scoring it against itself would report a perfect shot however far
            // the rounds actually land from the place the player picked.
            PredictedMissMetres = state.HasAim
                ? Body.SurfaceRadius * Vec.AngleBetween(hit.GroundFixedPointCci, _trueAimCci)
                : double.NaN;

            // Not while the trim is firing, and this is the whole reason the two can coexist. The
            // correction's only observer is this prediction, and the trim is actively moving the
            // vehicle it is taken from - so the bias absorbs a displacement the trim then reads as
            // a larger error and burns harder at. Same shape as the release sequence's latched
            // reference, and it runs away rather than merely drifting: flown, a shot 0.1 km off at
            // cutoff wound up by a factor of ten every ten cycles to 139 m/s of commanded trim.
            if (state.HasAim && !TrimIsFiring) _aim.Observe(hit.GroundFixedPointCci, _trueAimCci);
        }
        else
        {
            PredictedImpact = null;
            PredictedMissMetres = double.NaN;
        }
    }
}
