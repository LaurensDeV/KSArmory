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
    private const double BallisticBudgetSeconds = 1500.0;

    // And its budget in simulated seconds, which is the one that catches a flight that is stuck
    // rather than slow. A reentry vehicle expires at half an hour; a shot that has not resolved in
    // an hour of world time is not going to.
    private const double BallisticSimBudgetSeconds = 3600.0;

    // The world needs a few seconds after load before a craft is flyable and a battery is crewed.
    private const double SettleSeconds = 4.0;

    public ScenarioRunner(Config config) => _config = config;

    /// <summary>
    /// The scenario the harness asked for, or null. A one-line file beside the log, consumed as
    /// it is read so a second launch does not silently re-run the last request.
    /// </summary>
    public static string? Requested()
    {
        try
        {
            string path = Path.Combine(Log.Folder, "scenario.txt");
            if (!File.Exists(path)) return null;

            string name = File.ReadAllText(path).Trim();
            File.Delete(path);

            return string.IsNullOrWhiteSpace(name) ? null : name;
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

        _ballistic = new BallisticScenario(shot, line => Report($"{_name}: {line}"));

        // Nobody is watching a scripted shot and there is no second chance to ask for the numbers,
        // which is the same reason BallisticScenario turns verbose logging on. Off everywhere else.
        _config.TraceWarhead = true;

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

        if (_ballistic is not null && _simElapsed > BallisticSimBudgetSeconds)
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

                _phase = _ballistic is null ? Phase.WaitingForWorld : Phase.Flying;
                return;

            case Phase.Flying:
                if (_ballistic!.Update(roster, icbms, dt, playerStep) is { } outcome) Finish(outcome);
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
    private string Stuck()
    {
        if (_ballistic is null || _phase != Phase.Flying) return _phase.ToString();

        return $"{_ballistic.Where}; {_ballistic.Judge().Said}";
    }

    private void Finish(string outcome)
    {
        _phase = Phase.Done;
        _ballistic?.Release();
        Report($"{_name}: {outcome}");
        Report($"{_name}: END");
    }

    private static void Report(string line) => Log.Info($"{Tag} {line}");
}
