using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The operator's panel: master arm, radar and guidance tuning, the track list with
/// manual designation, and a rolling event log.
/// </summary>
internal sealed class Ui(Config config, DefenceBattery battery, WarpPolicy warp)
{
    private static readonly float4 Green = new(0.4f, 1.0f, 0.45f, 1f);
    private static readonly float4 Red = new(1.0f, 0.35f, 0.3f, 1f);
    private static readonly float4 Amber = new(1.0f, 0.78f, 0.25f, 1f);
    private static readonly float4 Grey = new(0.65f, 0.65f, 0.7f, 1f);

    // Warp above which a frame carries more simulated time than the interceptor can integrate, so
    // the battery stands rounds down. Indicative only: the real limit is per frame, so a lower
    // frame rate reaches it sooner. Assumes 60 fps.
    private const double MaxTrackableWarp = Interceptor.MaxFaithfulStep * 60.0;

    private readonly Config _config = config;
    private readonly DefenceBattery _battery = battery;
    private readonly WarpPolicy _warp = warp;
    private readonly List<int> _viewports = [];
    private string _ownTeamEntry = string.Empty;
    private string _newTeamEntry = string.Empty;

    public bool Visible = true;

    public void Draw()
    {
        if (!Visible)
        {
            // Closing the panel must not strand the operator with no way back.
            if (ImGui.Begin("KSArmory##reopen", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
            {
                if (ImGui.Button("KSArmory")) Visible = true;
            }
            ImGui.End();
            return;
        }

        if (!ImGui.Begin("KSArmory", ref Visible))
        {
            ImGui.End();
            return;
        }

        ImGui.TextDisabled($"KSArmory {Build.Version}");
        ImGui.Separator();

        DrawStatus();
        ImGui.Separator();
        DrawWeapons();
        ImGui.Separator();
        DrawTestTargets();
        ImGui.Separator();
        DrawTrackList();
        ImGui.Separator();
        DrawIff();
        ImGui.Separator();
        DrawTuning();
        ImGui.Separator();
        DrawLog();

        ImGui.End();
    }

    private void DrawStatus()
    {
        if (_battery.Platform is null)
        {
            ImGui.TextColored(Grey, "No platform - take control of a vehicle.");
            return;
        }

        string platform = KsaWorld.DisplayName(_battery.Platform);
        bool flyingIt = ReferenceEquals(_battery.Platform, KsaWorld.ControlledVehicle);
        ImGui.Text($"Platform: {platform}{(_battery.PlatformPinned ? " (pinned)" : "")}");
        if (!flyingIt) ImGui.TextDisabled("  (you are flying something else; the battery stays here)");

        if (_battery.Launcher is not null)
        {
            ImGui.TextColored(Green, $"Launcher: {_config.Launcher.DisplayName} fitted");
        }
        else if (_config.RequireLauncherPart)
        {
            ImGui.TextColored(Red, "Launcher: none fitted");
            ImGui.TextDisabled($"  Add the {_config.Launcher.DisplayName} in the editor,");
            ImGui.TextDisabled("  or untick 'Require launcher part' below.");
        }
        else
        {
            ImGui.TextColored(Amber, "Launcher: none (part requirement off)");
        }

        if (_config.Armed) ImGui.TextColored(Red, "MASTER ARM: ARMED");
        else ImGui.TextColored(Green, "MASTER ARM: SAFE");

        ImGui.SameLine();
        ImGui.Text($"   Rounds: {_battery.Ammo}/{_profile.TubeCount}");

        if (_profile.HasCannon)
        {
            ImGui.SameLine();
            if (_battery.GunsFiring) ImGui.TextColored(Red, $"   Cannon: {_battery.GunAmmo} FIRING");
            else ImGui.Text($"   Cannon: {_battery.GunAmmo}");
        }

        if (_battery.ReloadRemaining > 0.0)
        {
            ImGui.Text($"Reloading: {_battery.ReloadRemaining:F1}s");
            ImGui.ProgressBar(
                (float)(1.0 - _battery.ReloadRemaining / Math.Max(0.001f, _profile.ReloadSeconds)));
        }

        // The battery runs on simulated time, so a paused or heavily warped game is not a fault
        // but it does explain a silent battery. Saying so beats the report this came from,
        // which was "tracking is completely messed up".
        if (KsaWorld.IsPaused)
        {
            ImGui.Text("Paused - the battery is stopped with the world");
        }
        else if (_warp.Holding)
        {
            ImGui.TextColored(Amber,
                $"Warp held at {KsaWorld.SimulationSpeed:0.#}x - {_warp.HeldSpeed:F0}x returns "
                + "when the rounds land");
        }
        else if (_warp.Yielded)
        {
            ImGui.TextColored(Red, "Warp not held - something else is driving the speed control");
            ImGui.TextDisabled("  Rounds in flight will lag the world and miss.");
        }
        else if (KsaWorld.SimulationSpeed > 1.0)
        {
            double warp = KsaWorld.SimulationSpeed;
            ImGui.Text(warp > MaxTrackableWarp && !_config.LimitWarpInFlight
                ? $"Warp {warp:F0}x - too fast to guide; rounds will lag the world"
                : $"Warp {warp:F0}x");
        }

        DrawSlowMotion();

        // Should stay at zero. If it does not, the render rate is outrunning the simulation
        // clock and that is worth knowing, because it explains stuttering round bodies.
        if (_battery.FramesWithoutSimStep > 0)
        {
            ImGui.TextColored(Amber, $"Frames with no sim step: {_battery.FramesWithoutSimStep}");
        }

        DrawTurretLine();

        var locked = _battery.Radar.Locked;
        if (locked is null)
        {
            ImGui.TextColored(Grey, "Radar: no threat");
        }
        else
        {
            float4 colour = _battery.Radar.HasFiringSolution ? Red : Amber;
            ImGui.TextColored(colour, _battery.Radar.HasFiringSolution ? "LOCKED" : "acquiring...");
            ImGui.Text($"  {KsaWorld.DisplayName(locked.Vehicle)}");
            ImGui.Text($"  range {locked.Range / 1000.0:F2} km   closing {locked.ClosingSpeed:F0} m/s");
            ImGui.Text($"  CPA {locked.ClosestApproach:F0} m in {locked.TimeToClosestApproach:F1}s");
        }

        ImGui.Text($"In flight: {_battery.Rounds.Count}");
    }

    // Where the turret is pointing, and whether it is still swinging. Also the place the engine's
    // verdict on the transform write surfaces: if KSA refuses it, the drive gives up for the
    // session and this is where that gets said, rather than the turret just silently never moving.
    // The weapon system the panel is tuning. See Config.Select.
    private LauncherProfile _profile => _config.Launcher;
    private MunitionProfile _munition => _config.Munition;

    // Speeds worth a button. KSA's own roller stops at 0.1x; these go two decades below.
    private static readonly (string Label, double Speed)[] SlowMotionSpeeds =
    [
        ("0.01x", 0.01), ("0.05x", 0.05), ("0.1x", 0.1), ("0.25x", 0.25), ("1x", 1.0),
    ];

    // Slow motion, well below what the game's speed control reaches. An engagement is over in a
    // couple of seconds of real time and the interesting part — the round leaving the tube, the
    // endgame turn, the fuse — happens far faster than it can be watched. Nothing in KSA stops the
    // simulation running at a hundredth of real time; its roller is simply built in tenths.
    private void DrawSlowMotion()
    {
        ImGui.Text($"Sim speed: {KsaWorld.SimulationSpeed:0.###}x");

        foreach ((string label, double speed) in SlowMotionSpeeds)
        {
            ImGui.SameLine();
            if (ImGui.Button(label)) KsaWorld.SetSimulationSpeed(speed);
        }
    }

    // Which of the game's camera views the optical head drives. KSA opens the views; a mod can
    // only borrow one, so this is a picker rather than a switch.
    private void DrawOpticView()
    {
        if (_battery.OpticPart is null) return;

        // Only windows the player can actually see. KSA keeps offscreen viewports of its own -
        // the thumbnail renderer is one - and offering those means picking a view that shows
        // nothing, which is indistinguishable from the feature being broken.
        KsaWorld.CollectUsableViewports(_viewports);

        ImGui.Text("Optical head view:");
        ImGui.SameLine();

        if (_viewports.Count == 0)
        {
            ImGui.TextDisabled("open a camera window in KSA (View menu), then pick it here");
            _config.OpticViewport = -1;
            return;
        }

        if (ImGui.RadioButton("off", _config.OpticViewport < 0)) _config.OpticViewport = -1;

        foreach (int index in _viewports)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton(KsaWorld.DescribeViewport(index), _config.OpticViewport == index))
            {
                _config.OpticViewport = index;
            }
        }

        if (_config.OpticViewport >= 0)
        {
            ImGui.TextDisabled("  no sky or terrain detail here - KSA renders secondary views");
            ImGui.TextDisabled("  without the atmosphere pass. See docs/BLOCKED-ON-KSA.md");
        }

        // A window closed under us, so stop writing to something that is no longer shown.
        if (_config.OpticViewport >= 0 && !_viewports.Contains(_config.OpticViewport))
        {
            _config.OpticViewport = -1;
        }
    }

    private void DrawTurretLine()
    {
        if (_battery.Launcher is null) return;

        if (_battery.TurretPart is null)
        {
            ImGui.TextColored(Amber, "Turret: subpart not found (fixed forward)");
            return;
        }

        if (_battery.AnyDriveRefused)
        {
            string frozen = string.Join(", ",
                Enum.GetValues<DriveChannel>()
                    .Where(c => !_battery.DriveWorks(c))
                    .Select(c => c.ToString().ToLowerInvariant()));
            ImGui.TextColored(Red, $"Drive: engine refused the transform write ({frozen})");
            if (!_battery.DriveWorks(DriveChannel.Turret) || !_battery.DriveWorks(DriveChannel.Pods))
            {
                ImGui.TextColored(Red, "Holding fire: the tubes cannot be laid");
                return;
            }
        }

        double bearing = float.RadiansToDegrees((float)_battery.Turret.BearingRad);
        if (bearing < 0.0) bearing += 360.0;
        double elevation = float.RadiansToDegrees((float)_battery.Turret.ElevationRad);
        string aim = $"Turret: {bearing:F0} deg, elev {elevation:F0} deg";

        if (!_config.TurretTracking)
        {
            ImGui.TextColored(Grey, $"{aim} (tracking off)");
        }
        else if (_battery.IsLaid)
        {
            ImGui.TextColored(Green, $"{aim} - laid");
        }
        else if (_battery.Turret.OnTarget)
        {
            ImGui.TextColored(Amber, $"{aim} - settling");
        }
        else
        {
            double error = Math.Abs(float.RadiansToDegrees((float)_battery.Turret.ErrorRad));
            double elevError = Math.Abs(float.RadiansToDegrees((float)_battery.Turret.ElevationErrorRad));
            ImGui.TextColored(Amber, $"{aim} - slewing ({Math.Max(error, elevError):F0} deg to go)");
        }
    }

    private void DrawWeapons()
    {
        ImGui.Checkbox("Master arm", ref _config.Armed);
        ImGui.SameLine();
        ImGui.Checkbox("Auto engage", ref _config.AutoEngage);
        ImGui.SameLine();
        ImGui.Checkbox("Missiles", ref _config.MissilesEnabled);
        if (_profile.HasCannon)
        {
            ImGui.SameLine();
            ImGui.Checkbox("Cannon", ref _config.GunsEnabled);
        }

        DrawOpticView();

        if (ImGui.Button("FIRE")) _battery.FireAtLock();
        ImGui.SameLine();
        if (ImGui.Button("Reload")) _battery.Reload();
        ImGui.SameLine();
        if (ImGui.Button("Safe all")) _battery.SafeAll();

        // The battery stays on the craft carrying the launcher by itself, so pinning is only
        // needed to override that - e.g. choosing between two launcher-equipped craft.
        if (_battery.PlatformPinned)
        {
            if (ImGui.Button("Release pin")) _battery.PinPlatform(null);
            ImGui.SameLine();
            ImGui.TextDisabled("overriding automatic choice");
        }
        else if (ImGui.Button("Pin to this vehicle"))
        {
            _battery.PinPlatform(KsaWorld.ControlledVehicle);
        }

        ImGui.Checkbox("Never target the vehicle I'm flying", ref _config.ProtectControlledVehicle);
        ImGui.Checkbox("Require launcher part", ref _config.RequireLauncherPart);

        ImGui.Checkbox("Hold timewarp down while rounds fly", ref _config.LimitWarpInFlight);
        ImGui.TextDisabled($"  Above ~{MaxTrackableWarp:F0}x a round cannot be simulated. Held only");
        ImGui.TextDisabled("  while something is in the air, and given back after.");
        if (!_config.LimitWarpInFlight)
        {
            ImGui.TextColored(Amber, "  Off: rounds under warp will lag the world and miss.");
        }
    }

    // Spawns a drone on a timed pass, so the system can be tested without building and flying a
    // second craft by hand.
    private void DrawTestTargets()
    {
        if (!ImGui.TreeNode("Test targets")) return;

        if (_battery.Platform is null)
        {
            ImGui.TextDisabled("no platform");
            ImGui.TreePop();
            return;
        }

        ImGui.SliderFloat("Time to pass (s)", ref _spawnSeconds, 10f, 180f);
        ImGui.SliderFloat("Speed (m/s)", ref _spawnSpeed, 50f, 2000f);
        ImGui.SliderFloat("Miss distance (m)", ref _spawnMiss, 0f, 5000f);

        // Spawning beyond radar range is the easiest way to see nothing happen at all.
        float spawnRange = _spawnSpeed * _spawnSeconds;
        if (spawnRange > _config.Sensor.Range)
        {
            ImGui.TextColored(Amber,
                $"spawns {spawnRange / 1000f:F1} km out - beyond {_config.Sensor.Range / 1000f:F1} km radar range");
            ImGui.TextDisabled("  it will be invisible until it closes to range");
        }
        else
        {
            ImGui.TextDisabled($"spawns {spawnRange / 1000f:F1} km out, inside radar range");
        }

        // Which craft to fly as the drone. A stock vessel reads as an actual intruder;
        // cloning the launcher means air-defence sites attacking air-defence sites.
        ImGui.SliderInt("Drone type", ref _spawnCraft, 0, TestTarget.StockCraft.Length);
        string craft = _spawnCraft < TestTarget.StockCraft.Length
            ? TestTarget.StockCraft[_spawnCraft]
            : "clone of this craft";
        ImGui.TextDisabled($"  {craft}");

        string? craftName = _spawnCraft < TestTarget.StockCraft.Length
            ? TestTarget.StockCraft[_spawnCraft]
            : null;

        if (ImGui.Button("Overhead"))
        {
            TestTarget.Spawn(_battery.Platform, TestTarget.Profile.Overhead,
                _spawnSeconds, _spawnSpeed, _spawnMiss, craftName);
        }
        ImGui.SameLine();
        if (ImGui.Button("Head-on"))
        {
            TestTarget.Spawn(_battery.Platform, TestTarget.Profile.HeadOn,
                _spawnSeconds, _spawnSpeed, _spawnMiss, craftName);
        }
        ImGui.SameLine();
        if (ImGui.Button("Passing by"))
        {
            TestTarget.Spawn(_battery.Platform, TestTarget.Profile.PassingBy,
                _spawnSeconds, _spawnSpeed, _spawnMiss, craftName);
        }

        ImGui.TextDisabled("Arm before they arrive.");
        ImGui.TextDisabled("Head-on dives steepest and holds its speed best in atmosphere.");

        ImGui.Separator();

        // Writes the battery's whole world view to KSArmory.log, including why each nearby
        // vehicle was or was not tracked. Far more useful than staring at an empty screen.
        if (ImGui.Checkbox("Verbose log", ref _config.VerboseLog))
        {
            Log.Threshold = _config.VerboseLog ? Log.Level.Debug : Log.Level.Info;
            Log.Info(_config.VerboseLog ? "verbose logging on" : "verbose logging off");
        }
        ImGui.TextDisabled("  developer detail; off in release builds");

        if (ImGui.Button("Write diagnostic dump"))
        {
            Diagnostics.Dump(_battery, _config);
        }
        ImGui.SameLine();
        if (ImGui.Checkbox("Keep dumping", ref _config.DiagnosticDump))
        {
            Diagnostics.ResetTimer();
        }
        ImGui.TextDisabled("  -> Logs/KSArmory.log");

        ImGui.TreePop();
    }

    // 30 s at 300 m/s spawns 9 km out, comfortably inside the default 20 km radar range,
    // and keeps the ballistic arc shallow enough to stay well clear of terrain.
    private float _spawnSeconds = 30f;
    private float _spawnSpeed = 300f;
    private float _spawnMiss = 1500f;
    private int _spawnCraft;   // index into TestTarget.StockCraft; past the end = clone

    private void DrawTrackList()
    {
        if (!ImGui.TreeNode($"Tracks ({_battery.Radar.Tracks.Count})")) return;

        if (_battery.Radar.Tracks.Count == 0)
        {
            ImGui.TextDisabled("scope clear");
        }

        for (int i = 0; i < _battery.Radar.Tracks.Count; i++)
        {
            Track t = _battery.Radar.Tracks[i];
            bool isLock = ReferenceEquals(t, _battery.Radar.Locked);

            float4 colour = isLock ? Red : AllegianceColour(t.Allegiance);
            string mark = t.Allegiance == Allegiance.Friendly ? "F"
                : t.Allegiance == Allegiance.Neutral ? "N"
                : t.Allegiance == Allegiance.Hostile ? "H" : "?";

            ImGui.TextColored(colour,
                $"{(isLock ? ">" : " ")}[{mark}] {KsaWorld.DisplayName(t.Vehicle)}  " +
                $"{t.Range / 1000.0:F2} km  cpa {t.ClosestApproach:F0} m  " +
                $"{(t.RoundsAssigned > 0 ? $"[{t.RoundsAssigned} away]" : "")}");

            ImGui.SameLine();
            if (ImGui.Button($"designate##{i}"))
            {
                _battery.Radar.ManualDesignation = t.Vehicle;
            }
        }

        if (_battery.Radar.ManualDesignation is not null && ImGui.Button("Clear designation"))
        {
            _battery.Radar.ManualDesignation = null;
        }

        ImGui.TreePop();
    }

    // Teams and IFF. The whole subsystem shipped tested and unreachable: nothing outside
    // Config.cs ever wrote Config.Iff or Config.TeamNames, so every session anyone has played
    // ran with no teams declared and every contact Unknown.
    private void DrawIff()
    {
        if (!ImGui.TreeNode("Teams and IFF")) return;

        ImGui.TextDisabled("KSA has no team field. A craft joins a team when the team's name");
        ImGui.TextDisabled("appears anywhere in its name - so \"Red\" also matches \"Redstone\".");
        ImGui.TextDisabled("Longest match wins. Name teams distinctly.");
        ImGui.Separator();

        if (TextField("Own team", ref _ownTeamEntry))
        {
            _config.Iff.OwnTeam = string.IsNullOrWhiteSpace(_ownTeamEntry) ? null : _ownTeamEntry.Trim();
            Remember(_config.Iff.OwnTeam);
        }

        ImGui.SameLine();
        ImGui.TextDisabled(_config.Iff.OwnTeam is null ? "(none - everything is Unknown)" : "");

        if (TextField("Add team", ref _newTeamEntry) && !string.IsNullOrWhiteSpace(_newTeamEntry))
        {
            Remember(_newTeamEntry.Trim());
            _newTeamEntry = string.Empty;
        }

        if (_config.TeamNames.Count == 0)
        {
            ImGui.TextDisabled("  no teams declared; every contact classifies as Unknown");
        }

        for (int i = _config.TeamNames.Count - 1; i >= 0; i--)
        {
            string team = _config.TeamNames[i];
            bool own = string.Equals(team, _config.Iff.OwnTeam, StringComparison.OrdinalIgnoreCase);

            bool allied = _config.Iff.AlliedTeams.Contains(team);
            bool neutral = _config.Iff.NeutralTeams.Contains(team);

            ImGui.TextColored(AllegianceColour(_config.Iff.Classify(team)), $"  {team}");
            ImGui.SameLine();

            if (own)
            {
                ImGui.TextDisabled("own team");
            }
            else
            {
                if (ImGui.Checkbox($"allied##a{i}", ref allied))
                {
                    Toggle(_config.Iff.AlliedTeams, team, allied);
                    if (allied) _config.Iff.NeutralTeams.Remove(team);
                }
                ImGui.SameLine();
                if (ImGui.Checkbox($"neutral##n{i}", ref neutral))
                {
                    Toggle(_config.Iff.NeutralTeams, team, neutral);
                    if (neutral) _config.Iff.AlliedTeams.Remove(team);
                }
            }

            ImGui.SameLine();
            if (ImGui.Button($"remove##t{i}"))
            {
                _config.TeamNames.RemoveAt(i);
                _config.Iff.AlliedTeams.Remove(team);
                _config.Iff.NeutralTeams.Remove(team);
            }
        }

        ImGui.Separator();

        bool engageUnknown = _config.Iff.EngageUnknown;
        if (ImGui.Checkbox("Engage unknown contacts", ref engageUnknown)) _config.Iff.EngageUnknown = engageUnknown;

        bool engageNeutral = _config.Iff.EngageNeutral;
        if (ImGui.Checkbox("Engage neutrals", ref engageNeutral)) _config.Iff.EngageNeutral = engageNeutral;

        bool protectFriendly = _config.Iff.ProtectFriendly;
        if (ImGui.Checkbox("Never engage friendlies", ref protectFriendly)) _config.Iff.ProtectFriendly = protectFriendly;

        ImGui.TreePop();
    }

    private static void Toggle(HashSet<string> set, string team, bool wanted)
    {
        if (wanted) set.Add(team);
        else set.Remove(team);
    }

    private void Remember(string? team)
    {
        if (string.IsNullOrWhiteSpace(team)) return;
        if (!_config.TeamNames.Contains(team, StringComparer.OrdinalIgnoreCase))
        {
            _config.TeamNames.Add(team);
        }
    }

    private static float4 AllegianceColour(Allegiance a) => a switch
    {
        Allegiance.Friendly => Green,
        Allegiance.Hostile => Red,
        Allegiance.Neutral => Grey,
        _ => Amber,
    };

    // ImGui.InputText wants a fixed byte buffer, so each field owns one and the string is
    // marshalled either side of the call.
    private static bool TextField(string label, ref string value)
    {
        Span<byte> buffer = stackalloc byte[64];
        int written = System.Text.Encoding.UTF8.GetBytes(value.AsSpan(), buffer);
        buffer[Math.Min(written, buffer.Length - 1)] = 0;

        if (!ImGui.InputText(label, buffer, ImGuiInputTextFlags.EnterReturnsTrue, null, default))
        {
            return false;
        }

        int end = buffer.IndexOf((byte)0);
        value = System.Text.Encoding.UTF8.GetString(buffer[..(end < 0 ? buffer.Length : end)]);
        return true;
    }

    private void DrawTuning()
    {
        if (!ImGui.TreeNode("Radar")) { DrawGuidanceNode(); return; }

        ImGui.SliderFloat("Range (m)", ref _config.Sensor.Range, 500f, 40000f);
        ImGui.SliderFloat("Cone half-angle (deg)", ref _config.Sensor.ConeDeg, 5f, 180f);
        ImGui.SliderFloat("Threat radius (m)", ref _config.Sensor.ThreatRadius, 100f, 10000f);
        ImGui.SliderFloat("Threat horizon (s)", ref _config.Sensor.ThreatHorizonSeconds, 5f, 120f);
        ImGui.SliderFloat("Lock time (s)", ref _config.Sensor.LockSeconds, 0f, 5f);
        ImGui.SliderFloat("Min target speed (m/s)", ref _config.Sensor.MinTargetSpeed, 0f, 200f);
        ImGui.TreePop();

        if (ImGui.TreeNode("Turret"))
        {
            ImGui.Checkbox("Track with turret", ref _config.TurretTracking);
            ImGui.SliderFloat("Traverse rate (deg/s)", ref _profile.SlewRateDeg, 5f, 180f);
            ImGui.SliderFloat("Elevation rate (deg/s)", ref _profile.ElevationRateDeg, 5f, 120f);
            ImGui.SliderFloat("Settle before firing (s)", ref _profile.SettleSeconds, 0f, 2f);
            ImGui.Checkbox("Eject along the tube", ref _profile.LaunchAlongTube);
            ImGui.TextDisabled("  off: slew to the target on launch, plus loft");

            ImGui.Separator();
            ImGui.TextDisabled("Drive it by hand - neither needs a target:");
            ImGui.Checkbox("Spin continuously", ref _config.TurretSpin);
            ImGui.Checkbox("Manual aim", ref _config.TurretManual);
            ImGui.SliderFloat("Bearing (deg)", ref _config.TurretManualBearingDeg, -180f, 180f);
            ImGui.SliderFloat("Elevation (deg)", ref _config.TurretManualElevationDeg, 0f, 82f);
            ImGui.TextDisabled("  Elevation applies to spin as well as manual aim.");

            ImGui.Separator();
            ImGui.SliderFloat("Search array (rpm)", ref _profile.SearchRadarRpm, 0f, 60f);
            ImGui.Checkbox("Stop the search array", ref _config.SearchRadarStopped);
            ImGui.TreePop();
        }

        DrawGuidanceNode();
    }

    private void DrawGuidanceNode()
    {
        if (ImGui.TreeNode("Guidance"))
        {
            ImGui.SliderFloat("Nav constant N", ref _munition.NavConstant, 1f, 8f);
            ImGui.SliderFloat("Max lateral (g)", ref _munition.MaxLateralG, 5f, 80f);
            ImGui.SliderFloat("Seeker FOV (deg)", ref _munition.SeekerFovDeg, 10f, 90f);
            ImGui.SliderFloat("Gravity compensation", ref _munition.GravityCompensation, 0f, 1.5f);
            ImGui.SliderFloat("Boost accel (m/s2)", ref _munition.BoostAccel, 0f, 800f);
            ImGui.SliderFloat("Boost time (s)", ref _munition.BoostSeconds, 0f, 10f);
            ImGui.SliderFloat("Launch speed (m/s)", ref _munition.LaunchSpeed, 5f, 300f);
            ImGui.SliderFloat("Max flight time (s)", ref _munition.MaxFlightSeconds, 3f, 90f);
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Warhead"))
        {
            ImGui.SliderFloat("Fuse radius (m)", ref _munition.FuseRadius, 2f, 200f);
            ImGui.SliderFloat("Fuse arm delay (s)", ref _munition.FuseArmSeconds, 0f, 5f);
            ImGui.SliderFloat("Lethal radius (m)", ref _munition.LethalRadius, 2f, 300f);
            ImGui.SliderFloat("Blast radius (m)", ref _munition.BlastRadius, 5f, 600f);
            ImGui.SliderInt("Rounds per target", ref _config.RoundsPerTarget, 1, _profile.TubeCount);
            ImGui.SliderFloat("Salvo spacing (s)", ref _profile.SalvoSpacing, 0.05f, 3f);
            ImGui.SliderFloat("Reload time (s)", ref _profile.ReloadSeconds, 0f, 60f);
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Display"))
        {
            ImGui.Checkbox("Radar volume", ref _config.DrawRadarVolume);
            ImGui.SliderFloat("Cone draw length (m)", ref _config.ConeDisplayMetres, 200f, 20000f);
            ImGui.TextDisabled("  cosmetic only; detection range is set under Radar");
            ImGui.Checkbox("Tracks", ref _config.DrawTracks);
            ImGui.Checkbox("Track marker spheres", ref _config.DrawTrackMarkers);
            ImGui.TextDisabled("  large ball on each contact; scales with range");
            ImGui.Checkbox("Predicted pass point", ref _config.DrawClosestApproach);
            ImGui.TextDisabled("  where a threat will pass if it holds course");
            ImGui.Checkbox("Rounds", ref _config.DrawMissiles);
            ImGui.Checkbox("Round tracer spheres", ref _config.DrawRoundMarkers);
            // Bodies and tracers are placed by entirely separate paths, so toggling this while
            // watching a round in flight says which of the two is misbehaving.
            ImGui.Checkbox("Round bodies (off = tracers only)", ref _config.UseRoundBodies);
            ImGui.Checkbox("Tube markers (debug)", ref _config.DrawTubeMarkers);
            ImGui.TextDisabled(_battery.RoundBodyCount > 0 && _battery.RoundBodiesWork
                ? "  rounds have real bodies; the tracer hides them up close"
                : "  no round bodies available - tracers are all there is");
            ImGui.TreePop();
        }
    }

    private void DrawLog()
    {
        if (!ImGui.TreeNode("Log")) return;

        var events = _battery.Events;
        for (int i = events.Count - 1; i >= 0; i--)
        {
            ImGui.TextDisabled($"[{events[i].AtSeconds:F1}] {events[i].Message}");
        }

        ImGui.TreePop();
    }
}
