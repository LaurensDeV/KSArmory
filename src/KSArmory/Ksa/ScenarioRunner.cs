using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Flies a scripted engagement with nobody watching, and says in the log what happened.
///
/// <para>This exists because the gap between "the suite passes" and "it works" is a person
/// clicking things, and that person is the bottleneck on every behaviour change this mod makes.
/// KSA cannot run headless — it ships Windows-only natives and threads its simulation through a
/// Vulkan renderer — but it does not need to. It needs to run <em>unattended</em>, which is a
/// scenario file, a state machine and a line of output.</para>
///
/// <para>What it cannot do is judge appearance. Where a plume sits, whether a sight reads, whether
/// an explosion sounds right: those still need eyes. The <c>CAPTURE</c> markers exist for that —
/// the harness screenshots when it sees one, so the pictures at least arrive without anyone
/// sitting through the flight.</para>
///
/// <para>Two shapes of scenario, and this file owns the half they share: the request, the save, the
/// clocks and the verdict. An engagement is short enough to run inline; a ballistic shot is seven
/// minutes of flight with a state machine of its own, and lives in
/// <see cref="BallisticScenario"/>.</para>
/// </summary>
internal sealed class ScenarioRunner
{
    // Every line this writes starts with this, so a harness can grep for one thing.
    private const string Tag = "SCENARIO";

    private enum Phase
    {
        Idle,
        LoadingSave,
        WaitingForWorld,
        Arming,
        Engaging,
        Flying,
        Done,
    }

    private readonly Config _config;
    private Phase _phase = Phase.Idle;

    private string _name = string.Empty;
    private TestTarget.Profile _profile;
    private BallisticScenario? _ballistic;

    // Held rather than passed, because the flights are crewed once the world has loaded rather
    // than when the run is asked for.
    private ShotRequest _shot;

    // Which variant each rocket flies, when a batch is comparing two inside one world. Null is the
    // ordinary case: every rocket flies whatever was built.
    private ShotArms? _arms;
    private string _armSpec = string.Empty;
    private int _armPhase;

    private bool _isBallistic;
    private double _elapsed;
    private double _simElapsed;
    private double _budget = EngagementBudgetSeconds;
    private double _sinceSpawn;
    private bool _spawned;
    private bool _capturedLaunch;
    private double _lastComplaint;
    private string _save = string.Empty;

    // Longest an engagement may take before it is called a failure. Generous: a 20 km engagement at
    // 300 m/s closing is over a minute of flight before anything is decided.
    private const double EngagementBudgetSeconds = 90.0;

    // The same for a ballistic shot, which is a different order of thing: seven minutes of
    // simulated flight, and the warp it asks for can be refused. Wide enough to cover the whole
    // shot at one times speed, because a run that gives up early reports a timeout for a shot that
    // was going perfectly well.
    private const double BallisticBudgetSeconds = 2400.0;

    // And its budget in simulated seconds, which is the one that catches a flight that is stuck
    // rather than slow. A reentry vehicle expires at half an hour.
    //
    // Sized for a world holding several rockets, not one. Their releases are sequenced rather than
    // simultaneous, so eight flights need well over the hour a single shot resolves in -- measured
    // at 7 of 8 down on the hour, which reports a TIMEOUT carrying no per-flight line at all and
    // costs the whole shot. A budget an arm can fail on for being slower than the baseline is a
    // measurement of the budget.
    private const double BallisticSimBudgetSeconds = 5400.0;

    // The world needs a few seconds after load before a craft is flyable and a battery is crewed.
    private const double SettleSeconds = 4.0;

    public ScenarioRunner(Config config) => _config = config;

    // One flight per rocket, each with its own magazine, group and verdict, and one shared list of
    // which craft are ours so none of them aims at another.
    private readonly List<BallisticScenario> _flights = [];
    private readonly List<Vehicle> _shooters = [];

    // One view, claimed by whichever flight gets its salvo away first.
    private readonly bool[] _viewTaken = new bool[1];
    private readonly List<string?> _outcomes = [];

    // Which arm each flight drew, in the order they were crewed. Kept so the run's own summary can
    // say how the arms were spread rather than leaving a reader to count the per-craft lines.
    private readonly List<string> _armFlown = [];

    // Crewed once, from whatever the roster holds the first time it holds anything. A rocket that
    // appears later is not picked up: every rocket in a scripted world is on the pad at load, and a
    // flight joined mid-ascent is a differently conditioned shot rather than a spare one.
    private bool _crewed;

    private void CrewTheFlights(WeaponSystems roster, IcbmComputers? icbms)
    {
        if (_crewed || icbms is null) return;

        foreach (IcbmComputer computer in icbms.All)
        {
            if (!KsaWorld.IsAlive(computer.Craft)) continue;

            // The mod crews a ballistic computer on every craft it recognises, so the air-defence
            // site that is the *target* has one too. Flying it as an ICBM asks a SAM for twelve
            // thousand kilometres and holds the run open until the budget; counting it among our
            // shooters leaves the real rocket with nothing to aim at, and it falls back to bare
            // ground -- flown, and it moved the shot from 12,902 km to 6,261.
            if (!BallisticScenario.CouldReachTheAim(computer, roster.For(computer.Craft)?.Battery,
                                                    _shot))
            {
                continue;
            }

            // Drawn here and applied by the flight itself when it arms: the harness forces
            // settings of its own at that moment, and an arm applied before them is an arm that
            // silently flies the baseline.
            ShotArms.Arm? arm = _arms?.For(_shooters.Count, _armPhase);

            if (arm is { } drawn)
            {
                // Said per craft, because this is the only record of which rocket flew which
                // variant and the whole comparison is read back out of it afterwards.
                Report($"{_name}: {KsaWorld.DisplayName(computer.Craft)} flies arm {drawn.Describe()}");
                _armFlown.Add(drawn.Name);
            }

            _shooters.Add(computer.Craft);
            _flights.Add(new BallisticScenario(
                _shot, line => Report($"{_name}: {line}"), computer, _shooters, _viewTaken, arm));
            _outcomes.Add(null);
        }

        if (_flights.Count == 0) return;

        _crewed = true;
        _ballistic = _flights[0];

        // Said because trap 1's failure mode is silent: a run that flew one rocket and left the
        // rest on the pad looks exactly like a run that flew them all, and reports the idea as
        // free. The count here is what a batch checks against what it asked for.
        Report($"{_name}: crewed {_flights.Count} flight(s): "
               + string.Join(", ", _shooters.ConvertAll(KsaWorld.DisplayName)));

        // The split as flown, which is not always the split that was asked for: an odd number of
        // viable rockets gives one arm an extra, and a save whose rockets cannot all reach the aim
        // can give it several. A batch that reads this can drop a shot that came out lopsided
        // instead of pooling it.
        if (_arms is not null)
        {
            Report($"{_name}: arms " + string.Join(", ",
                _armFlown.GroupBy(n => n).Select(g => $"{g.Key} x{g.Count()}")));
        }
    }

    private void FlyThem(WeaponSystems roster, IcbmComputers? icbms, double dt, double playerStep)
    {
        if (_flights.Count == 0) return;

        bool allDone = true;

        for (int i = 0; i < _flights.Count; i++)
        {
            if (_outcomes[i] is not null) continue;

            _outcomes[i] = _flights[i].Update(roster, icbms, dt, playerStep);

            if (_outcomes[i] is null) allDone = false;
        }

        if (allDone) FinishAll();
    }

    // Every flight's verdict on its own line, because a batch scores them one by one. The run's own
    // outcome is the worst of them: a night that quietly lost a rocket must not read as a pass.
    private void FinishAll()
    {
        int flew = 0;

        for (int i = 0; i < _flights.Count; i++)
        {
            if (_flights[i].Committed) flew++;

            Report($"{_name}: FLIGHT {KsaWorld.DisplayName(_shooters[i])} :: {_outcomes[i]}");
        }

        string worst = _outcomes.Exists(o => o is not null && o.StartsWith("FAIL"))
                           ? "FAIL"
                           : "PASS";

        Finish($"{worst} {flew} of {_flights.Count} flight(s) flew");
    }

    // One world, one clock, and every flight in it has an opinion -- so the requests are collected
    // and the slowest wins rather than each flight writing the speed and the last one winning.
    // Sim/WorldSpeed.cs holds the rule. With one rocket this is exactly what the scenario used to
    // do to itself; with several it is the difference between a shot flown at the speed it chose
    // and one flown at whichever speed another rocket happened to want.
    private readonly List<double> _wantedSpeeds = [];

    private void ApplyWorldSpeed()
    {
        _wantedSpeeds.Clear();
        for (int i = 0; i < _flights.Count; i++) _wantedSpeeds.Add(_flights[i].WantedSpeed);

        double speed = WorldSpeed.Slowest(_wantedSpeeds);

        if (!double.IsNaN(speed) && !speed.Equals(_speedAsked))
        {
            _speedAsked = KsaWorld.SetSimulationSpeed(speed) ? speed : double.NaN;
        }
    }

    private double _speedAsked = double.NaN;

    /// <summary>
    /// The scenario the harness asked for, or null. A short file beside the log, consumed as it is
    /// read so a second launch does not silently re-run the last request.
    ///
    /// <para>Line one is the request. Line two, if there is one, is the arm spec — its own line
    /// because <see cref="ShotArms"/> separates arms with the same <c>|</c> that separates the
    /// request from the save, and a channel that cannot express the thing it carries is a channel
    /// that mangles it silently. Line three is the phase.</para>
    /// </summary>
    public static string? Requested()
    {
        try
        {
            string path = Path.Combine(Log.Folder, "scenario.txt");
            if (!File.Exists(path)) return null;

            string text = File.ReadAllText(path).TrimEnd();
            File.Delete(path);

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True once a scenario has been asked for, whatever it has done since.</summary>
    public bool Active => _phase != Phase.Idle;

    /// <summary>
    /// Reads the scenario file, if the harness left one. Deliberately a file rather than an
    /// environment variable: the game is a Windows process launched from WSL and the environment
    /// does not survive that reliably, while a path both sides agree on always does.
    /// </summary>
    public void Begin(string? request)
    {
        if (_phase != Phase.Idle || string.IsNullOrWhiteSpace(request)) return;

        // The request is the first line; the arm spec and its phase are the two after it, and a
        // one-line file is still the whole of the single-arm case.
        // Trimmed per line rather than over the whole text: the file is written from WSL and read
        // by a Windows process, so a line ending can arrive as CRLF and a stray carriage return on
        // the request would go into the save name.
        string[] lines = request.Split('\n');
        request = lines[0].Trim();
        _armSpec = lines.Length > 1 ? lines[1].Trim() : string.Empty;
        _armPhase = lines.Length > 2 && int.TryParse(lines[2].Trim(), out int phase) ? phase : 0;

        // "name" or "name|save". Skipping the configuration dialog gets the game past a dialog,
        // not into a scene: settings.toml's startVehicle is only ever read *by* that dialog, so
        // without one the game sits at a menu and nothing is ever in flight. Loading a save is
        // the only way in that does not need a click, and GameSaves.LoadSaveGame is public.
        string[] parts = request.Split('|', 2);
        _save = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        // "name" or "name:arguments". Only the ballistic scenario carries any, and it carries them
        // in the name rather than in a second file because the harness already has one channel to
        // the game and a second one is a second thing that can go stale.
        string[] named = parts[0].Trim().Split(':', 2);
        _name = named[0].Trim();

        if (_name == "mirv")
        {
            BeginBallistic(named.Length > 1 ? named[1].Trim() : string.Empty);
            return;
        }

        _profile = _name switch
        {
            "overhead" => TestTarget.Profile.Overhead,
            "passing" => TestTarget.Profile.PassingBy,
            _ => TestTarget.Profile.HeadOn,
        };

        _budget = EngagementBudgetSeconds;
        _phase = Phase.LoadingSave;
        Report($"{_name}: START profile={_profile} save='{_save}'");
    }

    private void BeginBallistic(string arguments)
    {
        if (!ShotRequest.TryParse(arguments, out ShotRequest shot, out string trouble))
        {
            Finish($"FAIL the request could not be read -- {trouble}");
            return;
        }

        // Refused rather than flown on one arm, and refused *here* rather than at crewing: a
        // batch that spends seven minutes discovering its spec was mistyped has bought a shot
        // belonging to neither arm, and a typo that silently flies the baseline twice is worse
        // still -- it reports a dead heat.
        if (_armSpec.Length > 0)
        {
            if (!ShotArms.TryParse(_armSpec, out ShotArms arms, out string bad))
            {
                Finish($"FAIL the arms could not be read -- {bad}");
                return;
            }

            _arms = arms;
        }

        // Crewed later, once the save has loaded and the roster holds something: with several
        // rockets there is one flight each, and none of them exists yet.
        _shot = shot;
        _isBallistic = true;

        // Nobody is watching a scripted shot and there is no second chance to ask for the numbers,
        // which is the same reason BallisticScenario turns verbose logging on. Off everywhere else.
        _config.TraceWarhead = true;

        // A scripted world lives for eight minutes with nobody looking at it, so a spent stage
        // arcing back down is pure frame time -- and frame time is the only thing that buys
        // simulation rate. It is what makes several rockets in one world affordable.
        //
        // It changes the step every shot is integrated at, which is a fidelity change rather than
        // a guidance one: no part of the flight reads an ascent stage. The baseline is re-flown
        // every night, so what it must not do is differ *between arms* -- and it cannot, being set
        // here rather than by anything an arm can reach.
        _config.DisposeSpentStages = true;

        _budget = BallisticBudgetSeconds;
        _phase = Phase.LoadingSave;
        Report($"{_name}: START {shot.Describe()} save='{_save}'");
    }

    /// <summary>
    /// One frame of the scenario. Does nothing unless one was asked for.
    ///
    /// <para>Two clocks, and which is which matters. Everything about the <em>world</em> runs on
    /// <paramref name="simStep"/>, because a scenario that accumulates while the game is paused or
    /// under timewarp measures something nobody is watching — the same rule fire control obeys. The
    /// budgets are the exception and are wall clock on purpose: they are what stops an unattended
    /// run hanging, and a run that waits on simulated time waits for ever on a paused game.</para>
    /// </summary>
    public void Update(WeaponSystems roster, IcbmComputers? icbms, double simStep, double playerStep)
    {
        if (_phase is Phase.Idle or Phase.Done) return;
        if (!double.IsFinite(playerStep) || playerStep <= 0.0) return;

        double dt = double.IsFinite(simStep) && simStep > 0.0 ? simStep : 0.0;

        _elapsed += playerStep;
        _simElapsed += dt;

        if (_elapsed > _budget)
        {
            Finish($"TIMEOUT after {_elapsed:F0} s of wall clock -- {Stuck()}");
            return;
        }

        if (_isBallistic && _simElapsed > BallisticSimBudgetSeconds)
        {
            Finish($"TIMEOUT after {_simElapsed / 60.0:F0} minutes of world time -- {Stuck()}");
            return;
        }

        WeaponSystems.Entry? entry = null;
        foreach (WeaponSystems.Entry e in roster.All)
        {
            if (e.Battery.Platform is not null && e.Battery.Launcher is not null) { entry = e; break; }
        }

        switch (_phase)
        {
            case Phase.LoadingSave:
                // A beat after load: asking the game to swap scenes while it is still building
                // the first one is not a case anything here can recover from.
                if (_elapsed < 3.0) return;

                if (_save.Length > 0)
                {
                    try
                    {
                        GameSaves.LoadSaveGame(_save);
                        Report($"{_name}: asked for save '{_save}'");
                    }
                    catch (Exception e)
                    {
                        Finish($"FAIL could not load '{_save}': {e.Message}");
                        return;
                    }
                }

                _phase = _isBallistic ? Phase.Flying : Phase.WaitingForWorld;
                return;

            case Phase.Flying:
                CrewTheFlights(roster, icbms);
                FlyThem(roster, icbms, dt, playerStep);
                ApplyWorldSpeed();
                return;

            case Phase.WaitingForWorld:
                if (!KsaWorld.InFlight || entry is null)
                {
                    // Every few seconds, because "TIMEOUT in WaitingForWorld" says which state it
                    // died in and nothing about which of the two things it was missing.
                    if (_elapsed - _lastComplaint > 10.0)
                    {
                        _lastComplaint = _elapsed;
                        Report($"{_name}: waiting -- "
                               + (KsaWorld.InFlight ? "in flight" : "NO CRAFT IN FLIGHT")
                               + ", " + (entry is null ? "NO BATTERY CREWED" : "battery crewed"));
                    }
                    return;
                }

                if (_elapsed < SettleSeconds) return;

                Report($"{_name}: crewed {KsaWorld.DisplayName(entry.Battery.Platform!)} "
                       + $"with {entry.Battery.Profile.DisplayName}");
                _phase = Phase.Arming;
                return;

            case Phase.Arming:
                if (entry is null) return;

                entry.Policy.Armed = true;
                entry.Policy.AutoEngage = true;
                entry.Policy.MissilesEnabled = true;
                _config.DrawOverlays = true;

                Report($"{_name}: armed, {entry.Battery.Ammo} rounds");
                _phase = Phase.Engaging;
                return;

            case Phase.Engaging:
                if (entry is null) return;
                Engage(entry, dt);
                return;
        }
    }

    private void Engage(WeaponSystems.Entry entry, double dt)
    {
        WeaponSystem battery = entry.Battery;

        if (!_spawned)
        {
            // The same numbers the panel's buttons use, so a scenario reproduces what a person
            // would have clicked rather than a case only the harness can produce.
            if (TestTarget.Spawn(battery.Platform!, _profile, 30.0, 300.0, 1500.0, "Gemini7") is null)
            {
                Finish("FAIL could not spawn a target");
                return;
            }

            _spawned = true;
            Report($"{_name}: target away, {_profile}");
            return;
        }

        _sinceSpawn += dt;

        // The first round leaving is the moment worth a picture: it shows the launcher, the round
        // on its way and the plume, which is most of what a screenshot can settle.
        if (!_capturedLaunch && battery.Rounds.Count > 0)
        {
            _capturedLaunch = true;
            Report($"{_name}: CAPTURE launch");
        }

        foreach (IProjectile round in battery.Rounds)
        {
            if (round.State == RoundState.Detonated)
            {
                Finish($"PASS detonated {round.MissDistance:F1} m from the target "
                       + $"after {round.Age:F1} s, {battery.Ammo} rounds left");
                return;
            }
        }

        // Rounds are reaped, so a detonation can be missed between frames. The battery's own
        // count falling with nothing in the air is the same news arriving late.
        if (_sinceSpawn > 15.0 && battery.Rounds.Count == 0 && battery.Ammo < battery.Profile.TubeCount)
        {
            Finish($"PASS engagement over, {battery.Ammo} rounds left "
                   + "(outcome from the battery, not a round -- see the lines above)");
        }
    }

    // What a timeout was waiting for. The phase names a state and says nothing about which of the
    // things that state needed was missing, which is the whole of what a run that never finished
    // has to answer. For a shot that got some of its warheads away it carries the group too: those
    // are the numbers the run was for, and a bare "TIMEOUT" throws them away.
    // The flight that is actually holding the run up, which on a multi-rocket save is rarely the
    // first one. Reporting _flights[0] regardless describes a flight that has usually finished, so
    // a timeout reads as a healthy shot and points the reader at the wrong rocket.
    private string Stuck()
    {
        if (_ballistic is null || _phase != Phase.Flying) return _phase.ToString();

        for (int i = 0; i < _flights.Count; i++)
        {
            if (_outcomes[i] is not null) continue;

            return $"{KsaWorld.DisplayName(_shooters[i])}: {_flights[i].Where}; "
                   + $"{_flights[i].Judge().Said}";
        }

        return $"{_ballistic.Where}; {_ballistic.Judge().Said}";
    }

    private void Finish(string outcome)
    {
        _phase = Phase.Done;
        for (int i = 0; i < _flights.Count; i++) _flights[i].Release();
        Report($"{_name}: {outcome}");
        Report($"{_name}: END");
    }

    private static void Report(string line) => Log.Info($"{Tag} {line}");
}
