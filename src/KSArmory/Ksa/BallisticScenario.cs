using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Flies one ballistic shot with nobody watching: designate, arm, stage, and report what the
/// warheads did.
///
/// <para>The engagement scenarios beside this one settle in a minute; a ballistic shot is seven
/// minutes of simulated flight with five events in it worth knowing about, spread across four of
/// them. So this reports as it goes rather than only at the end — <b>the SCENARIO lines are the
/// report</b>, and the verdict at the bottom is a summary of them, not the whole output.</para>
///
/// <para><b>It does not fight <see cref="WarpPolicy"/>.</b> One speed is asked for, once, at the
/// moment the shot is committed, and nothing here writes the speed again: the policy holds the
/// world down for the burn, the trim and the rounds in the air, and hands the rest back. Asking for
/// a speed the policy is holding is a loop neither side wins, which is why the number asked for
/// sits under what the policy would allow a burn to run at anyway.</para>
///
/// <para>What it cannot do is put a rocket on the pad — see <c>CLAUDE.md</c>, "A fully
/// self-contained scenario is not possible from a mod". It waits for a craft that already has one
/// aboard.</para>
/// </summary>
internal sealed class BallisticScenario
{
    /// <summary>
    /// How fast the world runs while the shot is being set up.
    ///
    /// <para>Slow enough that a vehicle resumed mid-flight moves a negligible distance across the
    /// frames arming takes, so the state it is picked up in is the same on every run and two builds
    /// can be compared. Not paused, because the computer cannot sample a paused world and the
    /// scenario would wait for a craft it can never see.</para>
    /// </summary>
    public const double SetupSpeed = 0.01;

    /// <summary>
    /// How fast the world is asked to run, once, when the shot is committed.
    ///
    /// <para>Under the ceiling <see cref="WarpPolicy"/> allows a guided burn — one
    /// <see cref="IcbmProgram.MaxFaithfulStep"/> less its margin, about eleven times normal at
    /// sixty frames a second — so the ascent runs at the speed asked for and the policy never has
    /// to take it away. The descent is a different matter: a reentry vehicle in thick air needs a
    /// far shorter step than that, and the policy slows the world for it and gives this back
    /// afterwards.</para>
    /// </summary>
    public const double WarpFactor = 8.0;

    // Far higher than the burn's, and it can be: a ballistic coast with nothing in the air is not
    // being integrated by anything this mod owns, so the step it runs at costs no accuracy. Half an
    // hour at eight times is still nearly four minutes of sitting watching it.
    public const double CoastWarpFactor = 100.0;

    /// <summary>
    /// How long after the last release, in simulated seconds, before a salvo is called finished.
    ///
    /// <para>A bus that gives up with warheads still aboard is a real outcome rather than a hang,
    /// and waiting for a magazine that will never empty is how a harness turns one into the
    /// other.</para>
    /// </summary>
    public const double SalvoOverSeconds = 120.0;

    private readonly ShotRequest _shot;
    // How far back the view sits once it moves to the target. A power rather than a distance:
    // KSA scales the orbit camera by the followed craft's mean radius, so one number frames a
    // ground vehicle and a rocket alike, and this is far enough that a warhead is in shot before
    // it arrives rather than after.
    private const double WatchZoomPower = 3.0;

    private readonly Action<string> _say;
    private readonly ShotGroup _group = new();

    // Buffered rather than reported where it happens: the hook fires inside the battery's round
    // loop, which is inside the engine's frame hook, and a scenario's output belongs in its own
    // pass where the ordering is the one the log shows.
    private readonly List<string> _landed = [];

    private readonly Action<IProjectile> _onRoundEnded;

    private IcbmComputer? _computer;
    private WeaponSystem? _wired;
    private Vehicle? _flownFrom;

    private bool _committed;
    private int _loaded;
    private int _ammoWas = -1;
    private int _ended;
    private double _sinceRelease;
    private double _sinceComplaint;
    private Vehicle? _defendedSite;
    private bool _watchedTheTarget;
    private bool _onThePad;
    private SystemConfig? _policy;
    private bool _warped;

    private IcbmPhase _reported = IcbmPhase.Idle;
    private bool _saidCutoff;
    private bool _saidStaging;
    private string _saidTrim = "";
    private bool _capturedDeployment;
    private bool _capturedImpact;

    public BallisticScenario(ShotRequest shot, Action<string> say)
    {
        _shot = shot;
        _say = say;
        _onRoundEnded = OnRoundEnded;
    }

    /// <summary>What state a timeout would name, so a run that never got there says where it stuck.</summary>
    public string Where => _computer is null ? "waiting for a craft with a ballistic computer"
                         : !_committed ? "waiting for a launch solution"
                         : $"{_reported}, {_group.Released} released, {_group.Arrived} down";

    /// <summary>Whatever this shot has come to so far, which is what a timeout reports as well.</summary>
    public ShotVerdict Judge() => _group.Judge(_shot.BarMetres);

    /// <summary>Let go of the battery, so a finished scenario is not still being called back.</summary>
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

        // Only while the craft is still being looked for. Past the commit the computer is the only
        // thing that knows where the warheads were sent, and it answers that from the body and the
        // designation rather than from the craft — so losing the launcher must not lose the score.
        if (!_committed && !KsaWorld.IsAlive(_computer?.Craft)) _computer = null;

        if (_computer is null)
        {
            Find(roster, icbms);
            return null;
        }

        WeaponSystem? battery = roster.For(_computer.Craft)?.Battery;
        Wire(battery);

        if (!_committed)
        {
            // A refusal is an answer, not a wait. The phase machine only reaches NoSolution after
            // looking, and it never runs backwards — so a scenario that keeps waiting there spends
            // a whole timeout to report something the first cycle already knew, which for an
            // unattended run is the difference between a minute and a quarter of an hour.
            if (_computer.Program.Phase == IcbmPhase.NoSolution)
            {
                return $"FAIL {_computer.Command.Hold} -- "
                       + $"{_shot.Describe()}{Downrange(_computer)}";
            }

            Commit();
            return null;
        }

        ReportPhase();
        ReportRefusedStaging(battery);
        ReportCutoff();
        ReportSeparation();
        ReportTrim();
        ReportReleases(battery, simStep);
        ReportImpacts();

        if (_group.Released == 0 || _ended < _group.Released) return null;
        if (_sinceRelease < SalvoOverSeconds) return null;

        ShotVerdict verdict = _group.Judge(_shot.BarMetres);
        string held = _loaded > _group.Released ? $", {_loaded - _group.Released} still aboard" : "";

        return $"{(verdict.Pass ? "PASS" : "FAIL")} {verdict.Said}{held}";
    }

    // The first craft that could fly this shot: a ballistic computer, a launcher with something in
    // it, and still on the ground. The last of those is asked of IcbmProgram rather than answered
    // here, because the phase machine picks a vehicle up by exactly that test and two of them would
    // drift apart silently.
    private void Find(WeaponSystems roster, IcbmComputers icbms)
    {
        // KSA loads a save paused, and everything below is read from a computer that only samples
        // the world inside the mod's clock gate — so on a paused world nothing is ever true, and a
        // scenario that only waits waits for ever. Unpausing is the scenario's business rather than
        // the computer's: a player who paused did so on purpose.
        // Slowly, not at 1x. A save resumed mid-flight has a vehicle that keeps moving while this
        // runs, so the state it is picked up in depends on how many frames arming happened to take
        // - and two builds compared against each other are then two different shots. Measured: the
        // same save picked up at 415 s of flight on one build and 450 s on another, which is 164 km
        // of difference that belongs to the harness rather than to anything being tested.
        if (KsaWorld.IsPaused && KsaWorld.SetSimulationSpeed(SetupSpeed))
        {
            _say($"the world was paused; asked for {SetupSpeed:0.##}x while the shot is set up");
        }

        List<string> why = [];

        foreach (IcbmComputer computer in icbms.All)
        {
            if (!KsaWorld.IsAlive(computer.Craft)) continue;

            string name = KsaWorld.DisplayName(computer.Craft);

            // Null until the computer has sampled the world once, which it cannot do while the
            // clock gate is shut. Named separately from the tests below because it means "not
            // looked yet" rather than "looked and the answer was no".
            if (computer.Parent is not { } parent)
            {
                why.Add($"{name} has not sampled the world yet");
                continue;
            }

            WeaponSystems.Entry? entry = roster.For(computer.Craft);
            WeaponSystem? battery = entry?.Battery;

            if (battery?.Launcher is null)
            {
                why.Add($"{name} carries no launcher the mod recognises");
                continue;
            }

            if (battery.Ammo <= 0)
            {
                why.Add($"{name}'s launcher is empty");
                continue;
            }

            // A ballistic shot needs a launcher that can make the distance. Every craft the mod
            // recognises carries a computer, so an air-defence site with a full magazine is a
            // candidate on the two tests above and will be flown as an ICBM if it happens to
            // iterate first -- which is a SAM asked for twelve thousand kilometres.
            if (KsaWorld.TryCraftSurfacePoint(computer.Craft, out _, out double fromLat,
                                              out double fromLon, out _))
            {
                double reach = GroundMetresBetween(parent, fromLat, fromLon,
                                                   _shot.LatitudeDeg, _shot.LongitudeDeg);

                if (battery.Munition.MaxRange < reach)
                {
                    why.Add($"{name}'s {battery.Munition.DisplayName} reaches "
                            + $"{battery.Munition.MaxRange / 1000.0:F0} km, "
                            + $"and the aim point is {reach / 1000.0:F0} km away");
                    continue;
                }
            }

            double airspeed = Vec.Len(KsaWorld.VelocityEcl(computer.Craft)
                                      - KsaWorld.GroundVelocityAt(parent, KsaWorld.PositionEcl(computer.Craft)));

            // Wherever it is. The phase machine joins a flight by looking at the vehicle rather
            // than by assuming a pad — low and still is the launch sequence, dynamic pressure is an
            // ascent already under way, above the air is a hold — so a scenario that insists on a
            // pad refuses the case the operator most often tests, which is a save resumed near
            // apogee with the boost already flown.
            _onThePad = IcbmProgram.IsOnTheGround(computer.AltitudeMetres, airspeed,
                                                  computer.Config.TurnStartMetres);

            _computer = computer;
            _policy = entry?.Policy;
            _loaded = battery.Ammo;
            _ammoWas = battery.Ammo;
            _flownFrom = computer.Craft;

            _say(_onThePad
                     ? $"{name} on the ground at {WhereItStands(computer)}, "
                       + $"{battery.Ammo} x {battery.Munition.DisplayName} aboard"
                     : $"{name} already flying at {computer.AltitudeMetres / 1000.0:F0} km doing "
                       + $"{airspeed:F0} m/s, {battery.Ammo} x {battery.Munition.DisplayName} aboard");

            double aimLat = _shot.LatitudeDeg;
            double aimLon = _shot.LongitudeDeg;

            // Shoot at whatever is defending, when there is something. A shot at bare ground proves
            // the guidance and nothing else; a shot at a site that can see it coming is the
            // engagement worth flying, and it puts the impact somewhere with a camera on it.
            Vehicle? site = FindDefendedSite(roster, computer.Craft);

            if (site is not null
                && KsaWorld.TryCraftSurfacePoint(site, out _, out double siteLat, out double siteLon,
                                                 out string siteBody)
                && siteBody == parent.Id)
            {
                _defendedSite = site;

                if (_shot.AimWasGiven)
                {
                    // The operator named a point, so the site goes to it rather than the aim coming
                    // here. Moving what is already in the world beats spawning a second one: two
                    // identical launchers is a scene nobody can read, and the shot stays at the
                    // coordinates every other run was measured at.
                    if (KsaWorld.TryPlaceOnSurface(site, parent.Id, aimLat, aimLon))
                    {
                        _say($"moved {KsaWorld.DisplayName(site)} to the aim point, so the impact "
                             + "lands somewhere with a camera on it");
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

            computer.Designate(new AimSite(parent.Id, aimLat, aimLon, "scenario aim point"));

            _say($"aimed at {computer.Target.Describe()}{Downrange(computer)}");

            // Nobody is watching this one, so there is no cost to the detail and no second chance
            // to ask for it: a shot that goes wrong unattended has only what it wrote down.
            if (Log.Threshold > Log.Level.Debug)
            {
                Log.Threshold = Log.Level.Debug;
                _say("verbose logging on for the flight");
            }

            // Everything is aimed and armed, so the vehicle may have the clock back.
            if (KsaWorld.SetSimulationSpeed(1.0)) _say("set up; running at 1x");

            return;
        }

        if (_sinceComplaint < 10.0) return;
        _sinceComplaint = 0.0;

        // Every craft's verdict, not the last one looked at. A scene has several weapon-carrying
        // craft and the interesting refusal is rarely the final one - reporting a single reason
        // hides the launcher behind whatever happened to be iterated after it.
        _say(why.Count > 0
                 ? $"waiting -- {string.Join("; ", why)}"
                 : "waiting -- nothing in the scene carries a ballistic computer");
    }

    // Arm, and then light the first engine once the program has a trajectory to fly.
    //
    // Two frames, and that ordering is forced rather than tidy: the phase machine returns "not
    // armed" before running any of itself, so there is no launch solution to gate the staging on
    // until the master arm is set. Staging without one lights a rocket with nothing steering it.
    private void Commit()
    {
        if (_computer is not { } computer) return;

        if (!computer.Config.Armed)
        {
            // A stack flown wide open is destroyed by its own acceleration once the boosters are
            // gone and a light upper stage is left on a full-sized motor. The harness flies craft
            // it did not design, so it holds them to something an airframe survives rather than
            // trusting whatever the stack happens to be capable of.
            if (computer.Config.MaxAccelerationGee <= 0.0f)
            {
                computer.Config.MaxAccelerationGee = ScenarioAccelerationGee;
            }

            computer.Config.Armed = true;

            // Two interlocks, and arming one is not arming the other. The computer's flies the
            // rocket; the weapon system's is what lets a round leave a tube. With only the first
            // set, the sequencer decides to release over and over and fire control answers
            // "holding fire: safe -- master arm is off" every time, so the bus carries the whole
            // salvo into the ground.
            if (_policy is { } policy && !policy.Armed)
            {
                policy.Armed = true;
                _say("armed: the ballistic computer and the weapon's master arm");
            }
            else
            {
                _say("armed");
            }

            return;
        }

        ReportPhase();

        // The harness does not light the rocket. Ignition is the program's own first stage request
        // and firing a second sequence here would spend one of the player's on top of it -- and a
        // harness that staged by hand is a harness that passes whether or not the computer can
        // launch at all, which is how a computer with no ignition flew every shot in the suite.
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
    }

    // The computer never stages past a launcher that could come off, because the next sequence on
    // somebody's craft might be the joint holding the warheads. So a stack that needs a second
    // stage will not get one, and the shot falls short for a reason no phase line names.
    private void ReportRefusedStaging(WeaponSystem? battery)
    {
        if (_saidStaging || battery is null || _computer is not { } computer) return;
        if (!computer.Command.RequestStage || !battery.CanSeparate) return;

        _saidStaging = true;

        _say("the program wants a stage and will not take one past a launcher that can separate -- "
             + "a multi-stage stack has to be staged by hand from here");
    }

    // Assigned rather than added to, and re-assigned whenever the battery changes: a decoupler puts
    // the launcher on another craft mid-flight, and the rounds go with it.
    //
    // Nothing to wire is not a reason to let go. A system whose craft was destroyed goes on flying
    // what it had in the air off the roster's loose list — the same object, no longer answering to
    // For() — and those rounds are still the shot being scored.
    private void Wire(WeaponSystem? battery)
    {
        if (battery is null || ReferenceEquals(battery, _wired)) return;

        if (_wired is not null) _wired.RoundEnded = null;
        _wired = battery;
        battery.RoundEnded = _onRoundEnded;
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

    // Warp only once the salvo is away, and nothing before it.
    //
    // WarpPolicy bounds the *step* rather than the speed, so anything whose step stays inside
    // MaxFaithfulStep is allowed and the policy never intervenes — 8x at sixty frames is 0.13 s,
    // comfortably inside it. What that costs is whatever stops on a frame boundary: the cutoff
    // residual measured 3.16 m/s a frame at 8x against 0.40 at normal speed, and the trim settles
    // at 0.098 m/s against 0.017. Neither is a policy failure; both are the price of a long step,
    // and the only part of this flight that has no such price is the coast after the last warhead
    // has gone.
    // FORTY-FIVE KEEPS THE GATE SHUT ON THIS SHOT, AND THAT IS WORTH 470 m. The warheads are held
    // until the arrival is inside ReleaseBeforeArrivalSeconds, 420 s, and the coast is entered with
    // about 464 s to run -- so the margin asks for 465 and the coast is flown at 1x throughout. That
    // was discovered by accident and then measured on purpose; IcbmProgram.SteadyBeforeReleaseSeconds
    // carries the number and the measurement, because the panel's own coast warp stops at the same
    // place and two copies of it would drift.
    //
    // The scenario's note above prices the same effect at 8x -- a cutoff residual of 3.16 m/s a
    // frame against 0.40 -- and a hundred is far past it. So the wall clock this could save, about
    // forty seconds a shot, costs an order of magnitude of accuracy. It was the right trade when a
    // shot missed by 2.9 km and it is not one at 50 m.

    private void CoastToTheReleasePoint()
    {
        // Off the release count for the same reason the post-release warp is: the magazine refills
        // a few seconds after the salvo, so `ammo < _loaded` stops distinguishing "the warheads have
        // gone" from "they never left".
        if (_computer is not { } computer || _group.Released > 0) return;
        if (computer.Command.Phase != IcbmPhase.Coast) return;

        double toArrival = computer.Program.CommittedArrivalFromNow;
        double releaseAt = computer.Config.ReleaseBeforeArrivalSeconds;
        if (!double.IsFinite(toArrival)) return;

        bool roomToWarp = toArrival > releaseAt + IcbmProgram.SteadyBeforeReleaseSeconds;

        if (roomToWarp == _coasting) return;
        _coasting = roomToWarp;

        if (KsaWorld.SetSimulationSpeed(roomToWarp ? CoastWarpFactor : 1.0))
        {
            _say(roomToWarp
                     ? $"asked the world for {CoastWarpFactor:F0}x through the coast; "
                       + $"{(toArrival - releaseAt) / 60.0:F0} min before the warheads go"
                     : "back to 1x for the release, so every warhead leaves on the same frame");
        }
    }

    private bool _coasting;

    // Longer than the magazine's own reload, so a salvo still running is never mistaken for one
    // that has finished.
    private const double QuietAfterReleaseSeconds = 5.0;

    private int _releasedLastSeen = -1;
    private double _sinceLastRelease;

    private void WarpTheCoast()
    {
        if (_warped) return;
        _warped = true;

        if (KsaWorld.SetSimulationSpeed(WarpFactor))
        {
            _say($"asked the world for {WarpFactor:F0}x now the salvo is away; "
                 + "the warp policy holds it down again for the rounds in the air");
        }
    }

    // The two numbers that separate a shot that was never aimed right from one that was aimed right
    // and flown badly: what the engines left ungained, and how far the mod's own prediction of the
    // arc it cut off on lands from the target.
    private void ReportCutoff()
    {
        if (_saidCutoff || _computer is not { } computer) return;
        if (!double.IsFinite(computer.Program.ResidualAtCutoff)) return;

        _saidCutoff = true;

        string predicted = double.IsFinite(computer.PredictedMissMetres)
            ? $"{computer.PredictedMissMetres / 1000.0:F2} km off"
            : "nothing predicted";

        _say($"CAPTURE cutoff: residual {computer.Program.ResidualAtCutoff:F2} m/s, "
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

        // On what it is doing rather than on the number it is doing it at. The trim reports a fresh
        // figure every frame while it closes, and a report that echoes each of them is a hundred
        // lines saying one thing - the numbers that matter are the two it carries either side.
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
    private void ReportReleases(WeaponSystem? battery, double simStep)
    {
        _sinceRelease += simStep;

        if (battery is null || _computer is not { } computer) return;

        int ammo = battery.Ammo;
        if (_ammoWas < 0) _ammoWas = ammo;

        for (int i = 0; i < _ammoWas - ammo; i++)
        {
            _group.Release();
            _sinceRelease = 0.0;

            ReleaseCommand deploy = computer.Deployment;
            string shot = $"warhead away from tube {deploy.Tube + 1}, "
                          + $"{deploy.OffLineDegrees:F2} deg off the salvo's line, "
                          + $"{ammo} left";

            _say(_capturedDeployment ? shot : $"CAPTURE deployment: {shot}");
            _capturedDeployment = true;
        }

        _ammoWas = ammo;

        // Once the bus has nothing left, the interesting half of the flight is at the other end.
        // Only after the last one: moving the view mid-salvo takes the operator off the thing still
        // releasing, and the releases are the part that is over in a fraction of a second.
        if (ammo <= 0 && !_watchedTheTarget) WatchTheTarget();

        // Warped once the releases have stopped, not on the first one. The coast's frame is what a
        // round's error accumulates against -- 0.06 m/s of drift per millisecond of frame -- so
        // warping between releases gives the warheads that have already left a different frame from
        // the ones still aboard. Flown: the first warhead out landed 140.86 km off while the five
        // released after the change grouped at 3.67-3.75 km.
        //
        // Keyed on the releases going quiet rather than on the magazine emptying, so a shot that
        // holds warheads back still warps -- which is what tying it to a full salvo got wrong, and
        // what tying it to the first release was trying to avoid.
        _sinceLastRelease += simStep;

        // The long wait before the release point, which is most of the flight now the warheads are
        // held until the arrival is close. Warped through, and given back well before the first one
        // leaves: every warhead has to see the same frame in its opening seconds, which is what
        // warping between releases got wrong.
        CoastToTheReleasePoint();

        // Counted off the releases rather than off the magazine, because the magazine refills.
        // `ammo < _loaded` reads as "the salvo has gone" for about three seconds and then stops:
        // the launcher reloads inside QuietAfterReleaseSeconds, ammo returns to its loaded count,
        // and the branch is never entered again. Measured 2026-08-25 -- `holding fire: reloading
        // (3 s)` lands 34 ms after the sixth warhead leaves, so this never fired at all and the
        // whole 381 s coast ran at 1x. ShotGroup.Released only ever increases.
        if (_group.Released > 0)
        {
            if (_group.Released != _releasedLastSeen)
            {
                _releasedLastSeen = _group.Released;
                _sinceLastRelease = 0.0;
            }
            else if (_sinceLastRelease >= QuietAfterReleaseSeconds)
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
    {
        const double Rad = Math.PI / 180.0;

        double sinHalfLat = Math.Sin((bLat - aLat) * Rad * 0.5);
        double sinHalfLon = Math.Sin((bLon - aLon) * Rad * 0.5);

        double h = sinHalfLat * sinHalfLat
                   + Math.Cos(aLat * Rad) * Math.Cos(bLat * Rad) * sinHalfLon * sinHalfLon;

        return 2.0 * body.MeanRadius * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
    }

    // What an unattended shot holds itself to. Around what a real ICBM pulls at burnout, and well
    // inside what an airframe is built for.
    private const float ScenarioAccelerationGee = 8.0f;

    private static Vehicle? FindDefendedSite(WeaponSystems roster, Vehicle launching)
    {
        foreach (WeaponSystems.Entry entry in roster.All)
        {
            Vehicle craft = entry.Battery.Platform;

            if (craft is null || ReferenceEquals(craft, launching)) continue;
            if (!KsaWorld.IsAlive(craft)) continue;

            return craft;
        }

        return null;
    }

    // Put the operator where the warheads are going. The bus is finished — everything left happens
    // at the other end of the arc, minutes away and far too small to see from the launch site.
    private void WatchTheTarget()
    {
        _watchedTheTarget = true;

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
    // the battery's round loop, which is inside the engine's frame hook.
    private void OnRoundEnded(IProjectile round)
    {
        try
        {
            _ended++;

            string what = RoundLabel.For(round.Tube);

            if (round.State != RoundState.Detonated)
            {
                _landed.Add($"{what} {round.State} after {round.Age:F0} s without arriving");
                return;
            }

            double miss = MissFromAim(round);
            _group.Arrive(miss);

            _landed.Add(double.IsFinite(miss)
                ? $"{what} down {miss / 1000.0:F2} km from the aim point after {round.Age:F0} s"
                : $"{what} down after {round.Age:F0} s, and where could not be measured");
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
    private double MissFromAim(IProjectile round)
    {
        if (_computer is not { } computer || computer.Parent is not { } parent) return double.NaN;
        if (computer.TargetEcl() is not { } aimEcl) return double.NaN;

        double3 aimAtBurst = aimEcl + KsaWorld.GroundVelocityAt(parent, aimEcl)
                                      * round.DetonationElapsedInFrame;

        double miss = Vec.Len(round.PositionEcl - aimAtBurst);
        return double.IsFinite(miss) ? miss : double.NaN;
    }

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
