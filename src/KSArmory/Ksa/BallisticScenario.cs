using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Flies one ballistic shot with nobody watching: designate, arm, stage, and report what the
/// warheads did.
///
/// <para>Reports as it goes: the SCENARIO lines are the report, and the verdict sums them up.</para>
/// <para>It does not fight <see cref="WarpPolicy"/>: a speed is requested at the commit, under what the
/// policy allows a burn, and the policy holds the world down where it must. It cannot place a
/// craft, so it waits for one the save already has.</para>
/// </summary>
internal sealed class BallisticScenario
{
    /// <summary>
    /// How fast the world runs while the shot is set up: slow enough that a vehicle resumed mid-flight
    /// is picked up in the same state on every run, and not paused, since the computer cannot sample
    /// a paused world.
    /// </summary>
    public const double SetupSpeed = 0.01;

    /// <summary>
    /// How fast the world is asked to run when the shot is committed: under the ceiling
    /// <see cref="WarpPolicy"/> allows a guided burn, about eleven times at 60 fps.
    /// </summary>
    public const double WarpFactor = 8.0;

    // A coast with nothing in the air is integrated by nothing this mod owns, so its step costs no accuracy.
    public const double CoastWarpFactor = 100.0;

    /// <summary>
    /// Simulated seconds after the last release before a salvo is called finished: a bus that gives
    /// up with warheads aboard is an outcome, not a hang.
    /// </summary>
    public const double SalvoOverSeconds = 120.0;

    private readonly ShotRequest _shot;
    // A power, not a distance: KSA scales the orbit camera by the followed craft's mean radius.
    private const double WatchZoomPower = 3.0;

    // Following the body, in mean radii from its centre: one puts the camera on the ground. Five is
    // past the detailed terrain and bought 31.7 -> 29.5 ms a frame, flown; a shot is CPU-bound.
    private const double FlightZoomPower = 5.0;

    private readonly Action<string> _say;
    private readonly ShotBoard _board;

    // Buffered rather than reported where it happens: the hook fires inside the system's round
    // loop, which is inside the engine's frame hook, and a scenario's output belongs in its own
    // pass where the ordering is the one the log shows.
    private readonly List<string> _landed = [];

    // Every round this flight has already counted away, so one release is counted once.
    //
    // Reference equality, stated rather than inherited: every IProjectile today is a class, and a
    // record one would give two identical-looking warheads one entry between them.
    private readonly HashSet<IProjectile> _seen = new(ReferenceEqualityComparer.Instance);

    private readonly Action<IProjectile> _onRoundEnded;

    private IcbmComputer? _computer;
    private WeaponSystem? _wired;
    private Vehicle? _flownFrom;

    private bool _committed;
    private int _loaded;
    private int _ammoWas = -1;
    private int _ended;
    private double _sinceRelease;
    private double _busLostFor;

    // How long a bus that let nothing go must stay gone before the shot is called. A separation
    // hands the computer to a new vehicle within the frame; this only rules out reading that frame.
    private const double BusLostSeconds = 2.0;
    private double _sinceComplaint;
    private Vehicle? _defendedSite;
    private bool _watchedTheTarget;
    private bool _onThePad;
    private bool _warped;

    // What this flight wants the world clock to run at, or NaN for no opinion. Asked for rather
    // than written, because several rockets share one world and one speed -- Sim/WorldSpeed.cs
    // arbitrates and ScenarioRunner is the only thing that writes it. A flight that wrote directly
    // would fly the others at a speed they did not choose.
    public double WantedSpeed { get; private set; } = double.NaN;

    // True when the request moved, so the say that explains it still happens exactly once.
    private bool AskForSpeed(double speed)
    {
        if (WantedSpeed.Equals(speed)) return false;

        WantedSpeed = speed;
        return true;
    }

    private IcbmPhase _reported = IcbmPhase.Idle;
    private bool _saidCutoff;
    private bool _saidStaging;

    // Whether the target's own weapons are being held off, and whether that has been said.
    private bool _disarmSite;

    // How far the camera's craft stands off the point the warheads are aimed at. The group lands
    // within tens of metres of that point, so this only has to clear the scatter -- and at 250 m
    // the site is still well inside a chase camera's view of the burst.
    private const double SiteStandoffMetres = 250.0;
    private bool _saidDisarm;
    private readonly List<WeaponSystems.Entry> _defences = [];
    private string _saidTrim = "";
    private bool _capturedDeployment;
    private bool _capturedImpact;

    // The computer this instance flies, when it is one of several sharing a world. Null is the
    // single-rocket case, where it takes whichever craft in the scene turns out to be viable.
    private readonly IcbmComputer? _bound;

    // The variant this rocket flies, when a run is comparing two inside one world. Applied at
    // arming rather than at construction, so it lands on top of the settings the harness forces.
    private readonly ShotArms.Arm? _arm;

    // Set when the arm would not apply, which ends this flight rather than flying it on settings
    // belonging to neither arm.
    private bool _armRefused;

    // Whose turn it is to point the player's camera. There is one view and every flight wants to
    // send it to the target when its salvo is away, so without a claim eight rockets move it eight
    // times -- the same shared-resource problem Sim/WorldSpeed.cs settles for the world clock.
    //
    // One-element array rather than a bool because it is shared by reference across the flights.
    private readonly bool[] _viewTaken;

    // Every craft this run is flying, shared by reference with the runner and with the other
    // flights. Read only here; the runner fills it as it binds them.
    private readonly IReadOnlyList<Vehicle> _shooters;

    /// <summary>Whether this flight has a craft and has committed to flying it.</summary>
    public bool Committed => _committed;

    /// <summary>The craft it is flying, once it has one — so a caller can tell N flights apart.</summary>
    public Vehicle? Craft => _computer?.Craft;

    public BallisticScenario(ShotRequest shot, Action<string> say, IcbmComputer? bound = null,
                             IReadOnlyList<Vehicle>? shooters = null, bool[]? viewTaken = null,
                             ShotArms.Arm? arm = null)
    {
        _arm = arm;
        _bound = bound;
        _viewTaken = viewTaken ?? new bool[1];
        _shooters = shooters ?? [];
        _shot = shot;
        _say = say;
        _onRoundEnded = OnRoundEnded;

        // Sized from what was ASKED FOR rather than from what the flight reaches, so a target the
        // walk could not pay for fails with its number in the line instead of quietly not existing.
        _board = new ShotBoard(_shot.TargetCount);
    }

    /// <summary>What state a timeout would name, so a run that never got there says where it stuck.</summary>
    public string Where => _computer is null ? "waiting for a craft with a ballistic computer"
                         : !_committed ? "waiting for a launch solution"
                         : $"{_reported}, {_board.Released} released, {_board.Arrived} down";

    /// <summary>Whether the flight is waiting on a known instant: a burn window, or the release at the end of its coast.</summary>
    public bool IsWaiting => _computer?.Program.Phase is IcbmPhase.Holding
                             || (_computer?.Program.Phase == IcbmPhase.Coast && _board.Released == 0);

    /// <summary>Whatever this shot has come to so far, which is what a timeout reports as well.</summary>
    public ShotVerdict Judge() => _board.Judge(_shot.BarMetres);

    /// <summary>Let go of the system, so a finished scenario is not still being called back.</summary>
    public void Release()
    {
        if (_wired is not null) _wired.RoundEnded = null;
        _wired = null;
    }

    /// <summary>One frame of the shot. Returns the outcome once there is one, and null until then.</summary>
    public string? Update(WeaponSystems roster, IcbmComputers? icbms, double simStep, double playerStep)
    {
        _sinceComplaint += playerStep;

        if (icbms is null) return null;
        _icbms = icbms;

        // A rocket whose arm would not apply is not flown. Everything else about the run is still
        // valid -- the other rockets carry their own arms -- so this ends one flight rather than
        // the batch, and says which.
        if (_armRefused)
        {
            return $"FAIL the arm could not be applied to "
                   + $"{(_computer is null ? "the craft" : KsaWorld.DisplayName(_computer.Craft))}";
        }

        // Only while the craft is still being looked for. Past the commit the computer is the only
        // thing that knows where the warheads were sent, and it answers that from the body and the
        // designation rather than from the craft — so losing the launcher must not lose the score.
        if (!_committed && !KsaWorld.IsAlive(_computer?.Craft)) _computer = null;

        if (_computer is null)
        {
            Find(roster, icbms);
            return null;
        }

        WeaponSystem? system = roster.GuidedFrom(_computer.Craft)?.Weapon;
        Wire(system);

        if (!_committed)
        {
            // A refusal is an answer, not a wait: the phase machine reaches NoSolution only after
            // looking, and never runs backwards.
            if (_computer.Program.Phase == IcbmPhase.NoSolution)
            {
                return $"FAIL {_computer.Command.Hold} -- "
                       + $"{_shot.Describe()}{Downrange(_computer)}";
            }

            Commit();
            return null;
        }

        // Every frame and every launcher on the site: the save's settings can restore auto-engage
        // after the aim is taken, and any one left on intercepts.
        if (_disarmSite && _defendedSite is { } defended)
        {
            roster.AllOn(defended, _defences);
            bool disarmed = false;
            foreach (WeaponSystems.Entry defence in _defences)
            {
                if (!defence.Policy.AutoEngage) continue;
                defence.Policy.AutoEngage = false;
                disarmed = true;
            }

            if (disarmed && !_saidDisarm)
            {
                _saidDisarm = true;
                _say($"disarmed {KsaWorld.DisplayName(defended)}: a target that shoots down the "
                     + "accurate warheads measures the inaccurate ones");
            }
        }

        ReportPhase();
        ReportRefusedStaging(system);
        ReportCutoff();
        ReportSeparation();
        ReportTrim();
        ReportReleases(system, simStep);
        ReportImpacts();

        // A bus that died holding every warhead will release none, and nothing below ever ends a
        // salvo that never started: the flight would sit until the harness's wall clock ran out.
        if (_board.Released == 0)
        {
            _busLostFor = KsaWorld.IsAlive(_computer.Craft) ? 0.0 : _busLostFor + simStep;
            if (_busLostFor >= BusLostSeconds)
            {
                return $"FAIL the bus was destroyed with {_loaded} warhead(s) aboard and none released -- "
                       + $"{_computer.Command.Hold}";
            }
        }

        if (_board.Released == 0 || _ended < _board.Released) return null;
        if (_sinceRelease < SalvoOverSeconds) return null;

        ReportTargets();

        ShotVerdict verdict = _board.Judge(_shot.BarMetres);
        string held = _loaded > _board.Released ? $", {_loaded - _board.Released} still aboard" : "";

        return $"{(verdict.Pass ? "PASS" : "FAIL")} {verdict.Said}{held}";
    }

    // The first craft that could fly this shot: a ballistic computer, a loaded launcher and the reach.
    // Whether it is on the ground is IcbmProgram's own test, so the two cannot drift apart.
    private void Find(WeaponSystems roster, IcbmComputers icbms)
    {
        // KSA loads a save paused, and the computer samples nothing while paused. Unpaused slowly:
        // at 1x one save was picked up 415 s and 450 s into its flight on two builds, 164 km apart.
        if (KsaWorld.IsPaused && AskForSpeed(SetupSpeed))
        {
            _say($"the world was paused; asked for {SetupSpeed:0.##}x while the shot is set up");
        }

        List<string> why = [];

        foreach (IcbmComputer computer in icbms.All)
        {
            // One instance per rocket when there are several, each with its own magazine and verdict.
            if (_bound is not null && !ReferenceEquals(computer, _bound)) continue;

            if (!KsaWorld.IsAlive(computer.Craft)) continue;

            string name = KsaWorld.DisplayName(computer.Craft);

            // Null until the computer has sampled the world once: "not looked yet", not "no".
            if (computer.Parent is not { } parent)
            {
                why.Add($"{name} has not sampled the world yet");
                continue;
            }

            WeaponSystems.Entry? entry = roster.GuidedFrom(computer.Craft);
            WeaponSystem? system = entry?.Weapon;

            if (system?.Launcher is null)
            {
                why.Add($"{name} carries no launcher the mod recognises");
                continue;
            }

            if (system.Ammo <= 0)
            {
                why.Add($"{name}'s launcher is empty");
                continue;
            }

            // A ballistic shot needs a launcher that can make the distance, and being crewed says
            // nothing about that: a computer goes wherever a part provides guidance.
            if (KsaWorld.TryCraftSurfacePoint(computer.Craft, out _, out double fromLat,
                                              out double fromLon, out _))
            {
                double reach = GroundMetresBetween(parent, fromLat, fromLon,
                                                   _shot.LatitudeDeg, _shot.LongitudeDeg);

                if (system.Munition.MaxRange < reach)
                {
                    why.Add($"{name}'s {system.Munition.DisplayName} reaches "
                            + $"{system.Munition.MaxRange / 1000.0:F0} km, "
                            + $"and the aim point is {reach / 1000.0:F0} km away");
                    continue;
                }
            }

            double airspeed = Vec.Len(KsaWorld.VelocityEcl(computer.Craft)
                                      - KsaWorld.GroundVelocityAt(parent, KsaWorld.PositionEcl(computer.Craft)));

            // Wherever it is: the phase machine joins a flight from the vehicle's state, and a save
            // resumed near apogee is the case most often tested.
            _onThePad = IcbmProgram.IsOnTheGround(computer.AltitudeMetres, airspeed,
                                                  computer.Config.TurnStartMetres);

            _computer = computer;
            _loaded = system.Ammo;
            _ammoWas = system.Ammo;
            _flownFrom = computer.Craft;

            _say(_onThePad
                     ? $"{name} on the ground at {WhereItStands(computer)}, "
                       + $"{system.Ammo} x {system.Munition.DisplayName} aboard"
                     : $"{name} already flying at {computer.AltitudeMetres / 1000.0:F0} km doing "
                       + $"{airspeed:F0} m/s, {system.Ammo} x {system.Munition.DisplayName} aboard");

            double aimLat = _shot.LatitudeDeg;
            double aimLon = _shot.LongitudeDeg;

            // At a defended site when there is one, which puts the impact somewhere with a camera on it.
            Vehicle? site = FindDefendedSite(roster, computer.Craft);

            if (site is not null
                && KsaWorld.TryCraftSurfacePoint(site, out _, out double siteLat, out double siteLon,
                                                 out string siteBody)
                && siteBody == parent.Id)
            {
                _defendedSite = site;

                // Disarmed, since a scoring run cannot let the target shoot at what it measures;
                // unconditionally and every frame, as the site arms itself after the aim is taken.
                _disarmSite = true;

                if (_shot.AimWasGiven)
                {
                    // The site moves beside the named point, not onto it: AimSpread puts seat 0 on the
                    // point, and warheads stopped on the site's roof. docs/ACCURACY-PLAN.md 3ci.
                    double standoffDeg = SiteStandoffMetres / parent.MeanRadius * 180.0 / Math.PI;

                    if (KsaWorld.TryPlaceOnSurface(site, parent.Id, aimLat + standoffDeg, aimLon))
                    {
                        _say($"moved {KsaWorld.DisplayName(site)} to within "
                             + $"{SiteStandoffMetres:F0} m of the aim point, so the impact lands "
                             + "somewhere with a camera on it and nothing stands where it falls");
                    }
                    else
                    {
                        _say($"could not move {KsaWorld.DisplayName(site)} to the aim point; "
                             + "it stays where it is");
                    }
                }
                else
                {
                    aimLat = siteLat;
                    aimLon = siteLon;

                    _say($"aiming at {KsaWorld.DisplayName(site)}, which is defended, "
                         + $"rather than at bare ground");
                }
            }

            // Not all at one point (SpreadAim): groups on one aim are inside each other's lethal radius.
            (aimLat, aimLon) = SpreadAim(parent, computer, system.Munition, aimLat, aimLon);

            computer.Designate(new AimSite(parent.Id, aimLat, aimLon, "scenario aim point"));

            _say($"aimed at {computer.Target.Describe()}{Downrange(computer)}");

            AddTheOtherTargets(computer, parent, aimLat, aimLon);

            // Nobody is watching this one, so there is no cost to the detail and no second chance
            // to ask for it: a shot that goes wrong unattended has only what it wrote down.
            if (Log.Threshold > Log.Level.Debug)
            {
                Log.Threshold = Log.Level.Debug;
                _say("verbose logging on for the flight");
            }

            // Everything is aimed and armed, so the vehicle may have the clock back.
            if (AskForSpeed(1.0)) _say("set up; running at 1x");

            return;
        }

        if (_sinceComplaint < 10.0) return;
        _sinceComplaint = 0.0;

        // Every craft's verdict, not the last one looked at: the interesting refusal is rarely the last.
        _say(why.Count > 0
                 ? $"waiting -- {string.Join("; ", why)}"
                 : "waiting -- nothing in the scene carries a ballistic computer");
    }

    // Arm, and then light the first engine once the program has a trajectory to fly.
    //
    // Two frames: the phase machine returns "not armed" before running, so there is no launch
    // solution to gate the staging on until the computer is armed.
    private void Commit()
    {
        if (_computer is not { } computer) return;

        if (!computer.Config.Armed)
        {
            // A light upper stage on a full-sized motor is destroyed by its own acceleration, and the
            // harness flies craft it did not design.
            if (computer.Config.MaxAccelerationGee <= 0.0f)
            {
                computer.Config.MaxAccelerationGee = ScenarioAccelerationGee;
            }

            // Forced: ShotRequest carries only the aim and the bar. Zero is the shipped default.
            computer.Config.MinArrivalAngleDeg = ScenarioArrivalFloorDeg;
            computer.Config.CorrectAim = ScenarioCorrectsAim;

            // Last, so an arm has the final word: applied before the forced settings it would
            // silently fly the baseline.
            if (_arm is { } arm && !ShotArms.TryApply(arm, computer.Config, out string bad))
            {
                _say($"FAIL {bad}");
                _armRefused = true;
                return;
            }

            computer.Config.Armed = true;
            _say("armed");

            return;
        }

        ReportPhase();

        // Ignition is the program's own first stage request; staging here would spend a second
        // sequence and pass whether or not the computer can launch.
        if (_onThePad)
        {
            if (computer.Program.Phase != IcbmPhase.Rising) return;

            _say($"on the pad in {computer.Program.Phase}, reach {computer.Program.Reach}");
        }
        else
        {
            if (computer.Program.Phase is IcbmPhase.Idle or IcbmPhase.NoSolution) return;

            _say($"picked up in {computer.Program.Phase}, reach {computer.Program.Reach}");
        }

        _committed = true;

        // Park the view far out for the flight, where the engine renders no terrain detail, until
        // the impact. One flight does this, since the view is shared.
        if (!_viewTaken[0] && _computer?.Parent is { } parent
            && KsaWorld.WatchFrom(parent, FlightZoomPower))
        {
            _say($"the view is parked on {parent.Id} for the flight, which is cheaper to draw "
                 + "than the pad");
        }
    }

    // A stage the computer refuses because it would drop the launcher leaves the shot short for a
    // reason no phase line names.
    private void ReportRefusedStaging(WeaponSystem? system)
    {
        if (_saidStaging || system is null || _computer is not { } computer) return;
        if (!computer.RefusedAStage) return;

        _saidStaging = true;

        _say("the program wants a stage that would drop the launcher, and will not fire it -- "
             + "stage it by hand from here");
    }

    // Assigned rather than added to, and re-assigned whenever the system changes: a decoupler puts
    // the launcher on another craft mid-flight, and the rounds go with it.
    //
    // Nothing to wire is not a reason to let go. A system whose craft was destroyed goes on flying
    // what it had in the air off the roster's loose list — the same object, no longer answering to
    // For() — and those rounds are still the shot being scored.
    private void Wire(WeaponSystem? system)
    {
        if (system is null || ReferenceEquals(system, _wired)) return;

        if (_wired is not null) _wired.RoundEnded = null;
        _wired = system;
        system.RoundEnded = _onRoundEnded;
    }

    private void ReportPhase()
    {
        if (_computer is not { } computer) return;
        if (computer.Program.Phase == _reported) return;

        _reported = computer.Program.Phase;

        _say($"{_reported} at {computer.AltitudeMetres / 1000.0:F0} km, "
             + $"{computer.Command.VelocityToGain:F0} m/s to gain, "
             + $"{(computer.ArrivalIsIfReleasedNow ? "impact if released now in" : "impact in")} "
             + $"{IcbmProgram.Clock(computer.SecondsToArrival)} :: {computer.Command.Hold}");

    }

    // The coast is warped only up to IcbmProgram.SteadyBeforeReleaseSeconds before the release,
    // which carries the number and its measurement; the panel's coast warp stops at the same place.
    // A long step costs whatever stops on a frame boundary: the cutoff residual was 3.16 m/s at 8x
    // against 0.40 at 1x.

    private void CoastToTheReleasePoint()
    {
        // Off the release count: the magazine refills a few seconds after the salvo.
        if (_computer is not { } computer || _board.Released > 0) return;
        if (computer.Command.Phase != IcbmPhase.Coast) return;

        double toArrival = computer.Program.CommittedArrivalFromNow;

        // The gate the flight is actually holding to, not the setting. A walk of N stops starts
        // (N-1) x 65 s earlier, so reading the setting would warp the world straight past the first
        // release and the scenario would miss it. `ReleaseGate` is the setting when nothing walks; the split
        // comes the turn's allowance before it when the bus turns.
        double releaseAt = computer.Program.SplitGate;
        if (!double.IsFinite(toArrival) || !double.IsFinite(releaseAt)) return;

        bool roomToWarp = toArrival > releaseAt + IcbmProgram.SteadyBeforeReleaseSeconds;

        if (roomToWarp == _coasting) return;
        _coasting = roomToWarp;

        // The whole speed, which WarpPolicy takes back where something is integrated: asking for a
        // step instead would pin a cruise in which nothing is, at four times the wall clock.
        if (AskForSpeed(roomToWarp ? CoastWarpFactor : 1.0))
        {
            _say(roomToWarp
                     ? $"asked the world for {CoastWarpFactor:F0}x through the coast; "
                       + $"{(toArrival - releaseAt) / 60.0:F0} min before the warheads go"
                     : "back to 1x for the release, so every warhead leaves on the same frame");
        }
    }

    private bool _coasting;

    // A bus holding for its window is pressed through it as a player would press the panel's button, which is the only
    // way the wait passes: the computer never warps a hold by itself, so unpressed one sits at 1x for as long as it is.
    private void WarpToTheBurnWindow()
    {
        // Pressed again whenever a long hold is not warping, as a player would: a window that moves after a warp has ended
        // otherwise leaves the hold at 1x until the scenario times out.
        if (_computer is not { } computer || computer.Command.Phase != IcbmPhase.Holding) return;

        if (computer.TryWarpToWindow()) _say("pressed warp to the burn window");
    }

    private IcbmComputers? _icbms;

    // A walk goes quiet for longer than QuietAfterReleaseSeconds between its stops, and a stop's
    // trim at the warp's 88 ms steps spent a median 6.85 m/s per aim pass against 0.81 at 1x, so
    // warping there measures the step rather than the walk. docs/MIRV-TARGETS.md.
    private bool AnyBusStillWalking()
        => _icbms is { } icbms && icbms.All.Any(c => c.Walk.Walking);

    // Longer than the magazine's own reload, so a salvo still running is never mistaken for one
    // that has finished.
    private const double QuietAfterReleaseSeconds = 5.0;

    private int _releasedLastSeen = -1;
    private double _sinceLastRelease;

    private void WarpTheCoast()
    {
        if (_warped) return;
        _warped = true;

        if (AskForSpeed(WarpFactor))
        {
            _say($"asked the world for {WarpFactor:F0}x now the salvo is away; "
                 + "the warp policy holds it down again for the rounds in the air");
        }
    }

    // The two numbers that separate a shot that was never aimed right from one that was aimed right
    // and flown badly: what the engines left ungained, and how far the mod's own prediction of the
    // arc it cut off on lands from the target.
    //
    // Named, because a paired night runs several arms in one world. docs/ACCURACY-PLAN.md 3bo reads it per arm.
    private void ReportCutoff()
    {
        if (_saidCutoff || _computer is not { } computer) return;
        if (!double.IsFinite(computer.Program.ResidualAtCutoff)) return;

        _saidCutoff = true;

        string predicted = double.IsFinite(computer.PredictedMissMetres)
            ? $"{computer.PredictedMissMetres / 1000.0:F2} km off"
            : "nothing predicted";

        _say($"CAPTURE cutoff on {KsaWorld.DisplayName(computer.Craft)}: "
             + $"residual {computer.Program.ResidualAtCutoff:F2} m/s, "
             + $"own prediction {predicted}");
    }

    // The computer moves onto the craft the launcher is now riding, so the craft it is flying
    // changing is the split having landed. Nothing else can move it.
    private void ReportSeparation()
    {
        if (_computer is not { } computer) return;
        if (ReferenceEquals(computer.Craft, _flownFrom)) return;

        _flownFrom = computer.Craft;
        _say($"separated; the bus is {KsaWorld.DisplayName(computer.Craft)}");
    }

    private void ReportTrim()
    {
        if (_computer is not { } computer) return;
        if (computer.TrimSaid.Length == 0) return;

        // On what the trim is doing, not its figure, which changes every frame while it closes.
        string doing = computer.TrimSaid.Split(',')[0];
        if (doing == _saidTrim) return;

        _saidTrim = doing;

        _say($"trim: {computer.TrimSaid} -- owed "
             + $"{Rate(computer.TrimOwedAtSplitMetresPerSecond)} at the split, "
             + $"{Rate(computer.TrimOwedOnReleaseMetresPerSecond)} on release");
    }

    // Most of the trim's life is spent before either number exists, and "NaN m/s" in a report reads
    // as a fault rather than as a measurement that has not been taken yet.
    private static string Rate(double metresPerSecond)
        => double.IsFinite(metresPerSecond) ? $"{metresPerSecond:F2} m/s" : "nothing yet";

    // A round leaving is the magazine going down by one. The angle beside it is read from the
    // command that let it go, which is this frame's and no other: by the next one the sequencer has
    // moved on to the next tube and is reporting how far off the line *that* one is.
    private void ReportReleases(WeaponSystem? system, double simStep)
    {
        _sinceRelease += simStep;

        if (system is null || _computer is not { } computer) return;

        int ammo = system.Ammo;
        if (_ammoWas < 0) _ammoWas = ammo;

        // The rounds that appeared this frame, paired with the magazine's own count of what left.
        // A release and its arrival have to be scored against one target, so both read the same
        // round through TargetOf; counting the release off the bus's aim instead attributes the
        // last warhead of a stop to the next one, because the handover lands on the same frame.
        //
        // Only on a frame that let something go: a round appears in the same call the magazine is
        // counted down in, so there is nothing to pair on any other.
        List<IProjectile> fresh = ammo < _ammoWas ? RoundsNotSeenYet(system) : [];

        for (int i = 0; i < _ammoWas - ammo; i++)
        {
            int stop = i < fresh.Count ? TargetOf(fresh[i]) : computer.LeadTarget;

            _board.For(stop).Release();
            _sinceRelease = 0.0;

            ReleaseCommand deploy = computer.Deployment;
            string shot = $"warhead away from tube {deploy.Tube + 1}, "
                          + $"{deploy.OffLineDegrees:F2} deg off the salvo's line, "
                          + $"{ammo} left"
                          + (_board.Split ? $", for target {stop + 1}" : "");

            _say(_capturedDeployment ? shot : $"CAPTURE deployment: {shot}");
            _capturedDeployment = true;
        }

        _ammoWas = ammo;

        // Once the bus has nothing left, the interesting half of the flight is at the other end.
        // Only after the last one: moving the view mid-salvo takes the operator off the thing still
        // releasing, and the releases are the part that is over in a fraction of a second.
        if (ammo <= 0 && !_watchedTheTarget)
        {
            _watchedTheTarget = true;

            // First salvo away takes the view; the rest leave it alone. Moving it per flight walks
            // the camera to the same place once per rocket, which is worse than not moving it.
            if (!_viewTaken[0])
            {
                _viewTaken[0] = true;
                WatchTheTarget();
            }
        }

        // Warped once the releases go quiet, not on the first: a round's error accumulates against
        // the coast's frame, 0.06 m/s per millisecond, so every warhead must leave on the same one.
        _sinceLastRelease += simStep;

        // The long wait before the release point, warped through and given back before the first leaves.
        WarpToTheBurnWindow();
        CoastToTheReleasePoint();

        // Counted off the releases, which only increase: the launcher reloads inside
        // QuietAfterReleaseSeconds and the magazine's count comes back.
        if (_board.Released > 0)
        {
            if (_board.Released != _releasedLastSeen)
            {
                _releasedLastSeen = _board.Released;
                _sinceLastRelease = 0.0;
            }
            else if (_sinceLastRelease >= QuietAfterReleaseSeconds && !AnyBusStillWalking())
            {
                WarpTheCoast();
            }
        }
    }

    // Whatever else in the scene can shoot back, which is what a ballistic shot is worth aiming at.
    // Anything crewed that is not the launching craft: the roster only holds craft the survey
    // recognised a weapon on, so there is nothing else it could be.
    // Great-circle distance on the body's mean sphere. Good enough to tell a SAM's twenty
    // kilometres from an intercontinental shot, which is all it is asked to do.
    private static double GroundMetresBetween(Celestial body, double aLat, double aLon,
                                              double bLat, double bLon)
        => AimSpread.GroundMetresBetween(aLat, aLon, bLat, bLon, body.MeanRadius);

    // The places after the first, for a shot at several. A request naming one adds nothing at all,
    // so a single-target flight is unchanged by it.
    private void AddTheOtherTargets(IcbmComputer computer, Celestial parent,
                                    double leadLat, double leadLon)
    {
        if (_shot.TrackTargets > 0)
        {
            LayTheTrack(computer, parent, leadLat, leadLon);
            return;
        }

        IReadOnlyList<ShotRequest.Aim> asked = _shot.Targets;
        if (asked.Count <= 1) return;

        // Every one of this rocket's targets carries the same seat displacement, along the one
        // bearing the group shares. Spread per target instead and the sets fan, which walks two
        // rockets' warheads back inside each other's lethal radius -- what AimSpread exists to open.
        double bearing = AimSpread.BearingDeg(asked[0].LatitudeDeg, asked[0].LongitudeDeg,
                                              leadLat, leadLon);
        double shift = GroundMetresBetween(parent, asked[0].LatitudeDeg, asked[0].LongitudeDeg,
                                           leadLat, leadLon);
        bool displaced = shift > 0.0 && double.IsFinite(bearing);

        for (int i = 1; i < asked.Count; i++)
        {
            var at = displaced
                         ? AimSpread.Along(asked[i].LatitudeDeg, asked[i].LongitudeDeg, bearing,
                                           shift, parent.MeanRadius)
                         : (LatitudeDeg: asked[i].LatitudeDeg, LongitudeDeg: asked[i].LongitudeDeg);

            if (computer.AddTarget(new AimSite(parent.Id, at.LatitudeDeg, at.LongitudeDeg,
                                               $"scenario target {i + 1}")))
            {
                continue;
            }

            _say($"target {i + 1} was refused; this flight goes to {computer.Targets.Count} place(s) "
                 + "and the rest are scored as never released");
            break;
        }

        computer.BalanceTargets();

        _say($"{computer.DescribeTargets()}; the booster flies to target {computer.LeadTarget + 1}, "
             + string.Join(", ", TargetLines(computer)));
    }

    // ShotRequest.TrackTargets, laid from the rocket's own pad: uprange is the bearing from the lead back to it.
    private void LayTheTrack(IcbmComputer computer, Celestial parent, double leadLat, double leadLon)
    {
        if (!KsaWorld.TryLatitudeLongitude(KsaWorld.PositionEcl(computer.Craft), out _, out double padLat,
                                           out double padLon))
        {
            _say("could not read where this rocket stands, so no track was laid; it flies to the first target alone");
            return;
        }

        double bearing = AimSpread.BearingDeg(leadLat, leadLon, padLat, padLon) + (_shot.TrackAcross ? 90.0 : 0.0);

        for (int i = 1; i <= _shot.TrackTargets; i++)
        {
            var at = AimSpread.Along(leadLat, leadLon, bearing, i * _shot.TrackSpacingMetres, parent.MeanRadius);
            if (computer.AddTarget(new AimSite(parent.Id, at.LatitudeDeg, at.LongitudeDeg, $"scenario target {i + 1}")))
            {
                continue;
            }

            _say($"target {i + 1} was refused; this flight goes to {computer.Targets.Count} place(s) "
                 + "and the rest are scored as never released");
            break;
        }

        computer.BalanceTargets();

        _say($"{computer.DescribeTargets()}; laid {(_shot.TrackAcross ? "across" : "along")} the track "
             + $"{_shot.TrackSpacingMetres / 1000.0:F0} km apart on a bearing of {bearing % 360.0:F1} deg from the "
             + $"pad at {padLat:F3},{padLon:F3}; the booster flies to target {computer.LeadTarget + 1}, "
             + string.Join(", ", TargetLines(computer)));
    }

    private static IEnumerable<string> TargetLines(IcbmComputer computer)
    {
        for (int i = 0; i < computer.Targets.Count; i++)
        {
            TargetSet.Entry entry = computer.Targets[i];
            yield return $"target {i + 1} {entry.Site.Coordinates} x{entry.Warheads}";
        }
    }

    // Where this rocket aims, the operator's point only for the first of a group. The bearing is the
    // group's reference shot's, so every displacement is along one line rather than fanning inwards.
    private (double LatitudeDeg, double LongitudeDeg) SpreadAim(
        Celestial parent, IcbmComputer computer, MunitionProfile munition,
        double baseLat, double baseLon)
    {
        int index = -1;

        for (int i = 0; i < _shooters.Count; i++)
        {
            if (ReferenceEquals(_shooters[i], computer.Craft)) { index = i; break; }
        }

        if (index <= 0) return (baseLat, baseLon);

        double bearing = double.NaN;

        if (KsaWorld.TryCraftSurfacePoint(_shooters[0], out _, out double padLat,
                                          out double padLon, out string padBody)
            && padBody == parent.Id)
        {
            bearing = AimSpread.CrossRangeBearingDeg(padLat, padLon, baseLat, baseLon);
        }

        var aim = AimSpread.For(baseLat, baseLon, index, _shooters.Count,
                                AimSpread.SpacingMetres(munition.LethalRadius),
                                bearing, parent.MeanRadius);

        // Said per rocket: nothing else in the log records which point each was scored against.
        double off = GroundMetresBetween(parent, baseLat, baseLon,
                                         aim.LatitudeDeg, aim.LongitudeDeg);

        if (off > 0.0)
        {
            _say($"aim spread {off / 1000.0:F0} km off the group's point, so an arriving warhead "
                 + "does not kill the ones behind it");
        }

        return aim;
    }

    // What an unattended shot holds itself to. Around what a real ICBM pulls at burnout, and well
    // inside what an airframe is built for.
    private const float ScenarioAccelerationGee = 8.0f;

    // The arrival floor every scripted shot flies. Zero is the shipped behaviour.
    private const double ScenarioArrivalFloorDeg = 0.0;

    // Whether those shots carry the prediction's bias -- see IcbmConfig.CorrectAim.
    private const bool ScenarioCorrectsAim = true;

    /// <summary>
    /// Whether this craft's weapon could reach the aim point at all: a computer is crewed wherever a
    /// part provides guidance, whatever its weapon can reach. Unknowable is not viable; the caller
    /// asks again next frame. <see cref="Find"/> repeats the test inline, and lets an unreadable
    /// surface point through where this refuses it.
    /// </summary>
    public static bool CouldReachTheAim(IcbmComputer computer, WeaponSystem? system,
                                        in ShotRequest shot)
    {
        if (system?.Launcher is null || system.Ammo <= 0) return false;
        if (computer.Parent is not { } parent) return false;

        if (!KsaWorld.TryCraftSurfacePoint(computer.Craft, out _, out double fromLat,
                                           out double fromLon, out _))
        {
            return false;
        }

        double reach = GroundMetresBetween(parent, fromLat, fromLon,
                                           shot.LatitudeDeg, shot.LongitudeDeg);

        return system.Munition.MaxRange >= reach;
    }

    // Anything armed that is not one of this run's rockets, by the harness's own list: with several
    // rockets "not the launching craft" aims at another rocket (docs/METRE-LEVEL.md 5b, traps 2 and 3),
    // and which craft carry a computer depends on the panel having drawn.
    private Vehicle? FindDefendedSite(WeaponSystems roster, Vehicle launching)
    {
        foreach (WeaponSystems.Entry entry in roster.All)
        {
            Vehicle? craft = entry.Weapon.Platform;

            if (craft is null || ReferenceEquals(craft, launching)) continue;
            if (!KsaWorld.IsAlive(craft)) continue;
            if (IsOneOfOurs(craft)) continue;

            return craft;
        }

        return null;
    }

    // Empty for a single-rocket run, which is what keeps this a no-op there.
    private bool IsOneOfOurs(Vehicle craft)
    {
        for (int i = 0; i < _shooters.Count; i++)
        {
            if (ReferenceEquals(_shooters[i], craft)) return true;
        }

        return false;
    }

    // Put the operator where the warheads are going. The bus is finished — everything left happens
    // at the other end of the arc, minutes away and far too small to see from the launch site.
    private void WatchTheTarget()
    {

        if (_defendedSite is not { } site || !KsaWorld.IsAlive(site))
        {
            _say("nothing at the target to watch from; the view stays with the bus");
            return;
        }

        if (!KsaWorld.GoTo(site))
        {
            _say($"could not move the view to {KsaWorld.DisplayName(site)}");
            return;
        }

        // Pulled back far enough to see a warhead arrive rather than to inspect the site. The zoom
        // is a power rather than a distance — KSA scales it by the followed craft's mean radius, so
        // the same number frames a truck and a rocket alike.
        KsaWorld.SetOrbitZoomPower(WatchZoomPower);

        _say($"CAPTURE watching: the view is on {KsaWorld.DisplayName(site)}, "
             + "which is where they are coming down");
    }

    // The rounds in the air this flight has not counted away yet, in the launcher's own order.
    //
    // Object identity, because it is the only thing about a round that survives the coast: the
    // magazine reloads inside QuietAfterReleaseSeconds, so a tube number comes round again while a
    // walk is still running.
    private List<IProjectile> RoundsNotSeenYet(WeaponSystem system)
    {
        List<IProjectile> fresh = [];
        IReadOnlyList<IProjectile> rounds = system.Rounds;

        for (int i = 0; i < rounds.Count; i++)
        {
            if (_seen.Add(rounds[i])) fresh.Add(rounds[i]);
        }

        return fresh;
    }

    // Where this round was sent, as the computer recorded it: a handover lands on the frame a stop's
    // last warhead leaves, so a lead index read here attributes it a stop late.
    private int TargetOf(IProjectile round)
    {
        if (_computer?.TargetOfRound(round) is int said and >= 0) return said;

        return _computer?.LeadTarget ?? 0;
    }

    // One target's place, in the ecliptic. A set of one goes through TargetEcl(), as every accuracy
    // number was taken; past that the entry is named, since the lead is wherever the walk finished.
    private static double3? AimEclOf(IcbmComputer computer, int target)
        => computer.Targets.Count > 1 && target >= 0 && target < computer.Targets.Count
               ? computer.SiteEcl(computer.Targets[target].Site)
               : computer.TargetEcl();

    // One line per target, so a walk is read as several shots rather than as one scattered group.
    // Nothing is printed for a set of one: the verdict already carries that group's whole reading.
    private void ReportTargets()
    {
        if (!_board.Split || _computer is not { } computer) return;

        string whose = $" on {KsaWorld.DisplayName(computer.Craft)}";

        for (int i = 0; i < _board.Targets; i++)
        {
            string where = i < computer.Targets.Count
                               ? computer.Targets[i].Site.Coordinates
                               : "a place the flight never held";

            // The verdict's km to three places reads a millimetre group as 0.000; the tail is what a night is scored on.
            ShotGroup group = _board.For(i);
            string metres = group.Arrived > 0
                                ? $" | in metres: worst {group.Worst:F4}, best {group.Best:F4}, mean {group.Mean:F4}, "
                                  + $"spread {group.Spread:F4}"
                                : "";
            _say($"TARGET {i + 1} of {_board.Targets}{whose}: {where} -- "
                 + _board.JudgeTarget(i, _shot.BarMetres).Said + metres);
        }
    }

    private void ReportImpacts()
    {
        for (int i = 0; i < _landed.Count; i++)
        {
            _say(_capturedImpact ? _landed[i] : $"CAPTURE impact: {_landed[i]}");
            _capturedImpact = true;
        }

        _landed.Clear();
    }

    // Where the round ended, against the place it was sent. Nothing here may throw: it runs inside
    // the system's round loop, which is inside the engine's frame hook.
    private void OnRoundEnded(IProjectile round)
    {
        try
        {
            _ended++;

            string what = RoundLabel.For(round.Tube);
            int target = TargetOf(round);

            if (round.State != RoundState.Detonated)
            {
                _landed.Add($"{what} {round.State} after {round.Age:F0} s without arriving");
                return;
            }

            double miss = MissFromAim(round, target, out string resolved);
            _board.For(target).Arrive(miss);

            // Named: the only line carrying where each warhead landed, in a log several rockets share.
            string whose = _computer is { } owner ? $" on {KsaWorld.DisplayName(owner.Craft)}" : "";

            // Only when there is more than one place to name. A single-target flight's line is what
            // every landing-line endpoint in tools/shot-report.py was calibrated against.
            string sent = _board.Split ? $" for target {target + 1}" : "";

            _landed.Add(double.IsFinite(miss)
                ? $"{what}{whose}{sent} down {Distance.Measure(miss)} from the aim point after {round.Age:F0} s{resolved}"
                : $"{what}{whose}{sent} down after {round.Age:F0} s, and where could not be measured");
        }
        catch
        {
            // A harness that takes the game down is worse than one that loses a number.
        }
    }

    // The burst against the aim point, both at one instant. The round's position is back-dated into
    // the frame it burst in and the aim point is a place on a turning planet, so the aim has to be
    // carried to the same instant - up to half a kilometre of the frame's own motion otherwise, in
    // one direction, which reads as a common bias on every warhead of the group.
    //
    // Resolved into the arrival frame as well, so a group reads as a dispersion rather than as six
    // distances: downrange carries the fall and the timing, cross carries the plane. Only the miss
    // needs its instants paired; the frame's axes are directions, and a frame's travel moves no
    // direction.
    private double MissFromAim(IProjectile round, int target, out string resolved)
    {
        resolved = "";

        if (_computer is not { } computer || computer.Parent is not { } parent) return double.NaN;
        if (AimEclOf(computer, target) is not { } aimEcl) return double.NaN;

        double3 aimAtBurst = InFrame.AtBurst(aimEcl, KsaWorld.GroundVelocityAt(parent, aimEcl),
                                             round.DetonationElapsedInFrame);

        double3 missEcl = round.PositionEcl - aimAtBurst;
        string struck = "";

        // A warhead that struck a craft burst above the ground it was aimed at, so it is carried down its own
        // path to that ground: a strike on a structure standing on the aim scores the guidance, not its height.
        if (round.StruckBody is not null)
        {
            double3 up = Vec.Unit(aimEcl - parent.GetPositionEcl());
            double3 along = Vec.Unit(round.VelocityEcl - KsaWorld.GroundVelocityAt(parent, aimEcl));
            double height = Vec.Dot(missEcl, up);
            double descent = -Vec.Dot(along, up);
            if (height > 0.0 && descent > 0.0)
            {
                missEcl += along * (height / descent);
                struck = $", struck a craft {height:F2} m up and carried to the ground";
            }
        }

        double miss = Vec.Len(missEcl);
        if (!double.IsFinite(miss)) return double.NaN;

        try
        {
            doubleQuat cce2Cci = parent.GetCce2Cci();
            double3 aimCci = (aimEcl - parent.GetPositionEcl()).Transform(cce2Cci);
            double3 arrivingCci = (round.VelocityEcl - parent.GetVelocityEcl()).Transform(cce2Cci);

            if (ArrivalFrame.TryAt(aimCci, arrivingCci, out ArrivalFrame frame))
            {
                double3 parts = frame.Resolve(missEcl.Transform(cce2Cci));
                resolved = $" ({Signed(parts.Y)} downrange, {Signed(parts.Z)} cross)";
            }
        }
        catch
        {
            // The distance stands on its own; losing the components loses no reading anything scores.
        }

        resolved += struck;
        return miss;
    }

    // Signed in the unit that shows it, so a component parses the same way the distance beside it does.
    private static string Signed(double metres)
        => metres >= 0.0 ? "+" + Distance.Measure(metres) : Distance.Measure(metres);

    private static string WhereItStands(IcbmComputer computer)
    {
        if (computer.Parent is not { } parent) return "an unreadable place";

        try
        {
            double3 cce = KsaWorld.PositionEcl(computer.Craft) - parent.GetPositionEcl();
            return $"{parent.GetLatitudeFromCce(cce):F3},{parent.GetLongitudeFromCce(cce):F3} "
                   + $"on {parent.Id}";
        }
        catch
        {
            return "an unreadable place";
        }
    }

    // How far the shot has to go, which is the one number that says whether the aim point makes
    // sense for the pad the craft happens to be standing on.
    private static string Downrange(IcbmComputer computer)
    {
        if (computer.Parent is not { } parent) return "";
        if (computer.TargetEcl() is not { } aimEcl) return "";

        try
        {
            double3 centre = parent.GetPositionEcl();
            double angle = Vec.AngleBetween(KsaWorld.PositionEcl(computer.Craft) - centre, aimEcl - centre);
            return $" ({parent.MeanRadius * angle / 1000.0:F0} km downrange)";
        }
        catch
        {
            return "";
        }
    }
}
