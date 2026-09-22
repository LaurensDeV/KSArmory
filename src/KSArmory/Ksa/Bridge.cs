using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Commands from outside the game, read from a folder beside the log and answered in another:
/// what lets an agent in a terminal pause, move the camera, set off a burst, photograph it and
/// reload the shaders in a game that stays running. <c>tools/ksa-mcp/server.py</c> is the other
/// end, and <c>docs/VISUAL-TESTING.md</c> is why.
///
/// <para><b>Files, not a socket.</b> The mod reaches the network exactly once, when a player clicks
/// Send, and <c>tools/check-network.sh</c> holds it to that; a folder needs no port and works across
/// the WSL boundary as it is.</para>
///
/// <para>One command at a time, oldest first. A command that takes frames — a step, a capture —
/// holds the queue until it answers, so a sequence of them is a script that runs in order.</para>
/// </summary>
internal sealed class Bridge
{
    private readonly Config _config;

    // A few times a second is quick enough for a person and costs one directory listing.
    private const double PollSeconds = 0.1;
    private double _sincePoll;

    private Func<double, double, Reply?>? _running;
    private BridgeCommand? _current;

    // The pose the bridge holds the view in, and the view it took, to hand back.
    private CloudWatch.Pose? _pose;
    private KsaWorld.MainView _saved;
    private Vehicle? _posedFrom;

    private static readonly string[] ShaderIds = ["KSArmoryCloudCompute", "KSArmoryCloudResolveCompute"];

    public Bridge(Config config) => _config = config;

    private static string Root => Path.Combine(Log.Folder, "bridge");
    private static string Inbox => Path.Combine(Root, "in");
    private static string Outbox => Path.Combine(Root, "out");

    private readonly record struct Reply(bool Ok, string Error, Dictionary<string, object?> Data);

    private static Reply Done(Dictionary<string, object?>? data = null) => new(true, string.Empty, data ?? []);
    private static Reply Failed(string why) => new(false, why, []);

    /// <summary>
    /// One frame: advances the command running, or takes the next. Called from the one hook KSA
    /// always calls, so it runs in the menu and with the UI hidden. Nothing here may throw.
    /// </summary>
    public void Update(double dtPlayer, double dtSim)
    {
        try
        {
            if (_running is not null && _current is not null)
            {
                if (_running(dtPlayer, dtSim) is { } reply) Answer(_current, reply);
                return;
            }

            _sincePoll += dtPlayer;
            if (_sincePoll < PollSeconds) return;
            _sincePoll = 0.0;

            if (!Directory.Exists(Inbox)) return;

            string? next = Directory.GetFiles(Inbox, "*.json").Order(StringComparer.Ordinal).FirstOrDefault();
            if (next is null) return;

            string text = File.ReadAllText(next);
            File.Delete(next);

            if (!BridgeCommand.TryParse(text, out BridgeCommand? command, out string trouble))
            {
                Log.Warn($"bridge: refused {Path.GetFileName(next)} -- {trouble}");
                return;
            }

            Start(command!);
        }
        catch (Exception e)
        {
            if (_current is { } command) Answer(command, Failed($"threw: {e.Message}"));
            else Log.Warn($"bridge: {e.Message}");
        }
    }

    /// <summary>Holds the view on the newest burst while a pose is set. From the camera pass.</summary>
    public void DriveCamera()
    {
        try
        {
            if (_pose is not { } pose || _posedFrom is not { } craft || !KsaWorld.IsAlive(craft)) return;

            CloudWatch.Update(craft, pose);
        }
        catch
        {
            // The view is the player's to take back; a pose that cannot be held is simply not held.
        }
    }

    private void Start(BridgeCommand command)
    {
        _current = command;
        Log.Info($"bridge: {command.Name} ({command.Id})");

        Reply? now = command.Name switch
        {
            "status" => Status(),
            "pause" => KsaWorld.SetPaused(true) ? Done() : Failed("the world would not pause"),
            "resume" => KsaWorld.SetPaused(false) ? Done() : Failed("the world would not resume"),
            "speed" => KsaWorld.SetSimulationSpeed(command.Number("x", 1.0)) ? Done() : Failed("speed refused"),
            "set" => Set(command),
            "get" => Get(command),
            "clear" => Clear(),
            "burst" => Burst(command),
            "camera" => Camera(command),
            "reload_shaders" => ReloadShaders(),
            "step" => BeginStep(command),
            "capture" => BeginCapture(command),
            "load" => BeginLoad(command),
            _ => Failed($"no command '{command.Name}'"),
        };

        if (now is { } reply) Answer(command, reply);
    }

    private void Answer(BridgeCommand command, Reply reply)
    {
        _running = null;
        _current = null;

        Directory.CreateDirectory(Outbox);

        Dictionary<string, object?> body = new()
        {
            ["id"] = command.Id,
            ["cmd"] = command.Name,
            ["ok"] = reply.Ok,
            ["error"] = reply.Ok ? null : reply.Error,
            ["data"] = reply.Data,
        };

        // Written aside and moved, so the other end never reads half a reply.
        string final = Path.Combine(Outbox, command.Id + ".json");
        string partial = final + ".part";
        File.WriteAllText(partial, JsonSerializer.Serialize(body, JsonOptions));
        File.Move(partial, final, overwrite: true);

        if (!reply.Ok) Log.Warn($"bridge: {command.Name} ({command.Id}) failed -- {reply.Error}");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // ---- commands that answer at once -------------------------------------------------------

    private Reply Status()
    {
        Dictionary<string, object?> data = new()
        {
            ["build"] = Build.Version,
            ["in_flight"] = KsaWorld.InFlightScene,
            ["craft"] = KsaWorld.ControlledVehicle is { } craft ? KsaWorld.DisplayName(craft) : null,
            ["paused"] = KsaWorld.IsPaused,
            ["speed"] = KsaWorld.SimulationSpeed,
            ["clouds"] = NuclearClouds.Count,
            ["marks"] = NuclearClouds.ScorchCount,
            ["camera_held"] = _pose is not null,
        };

        if (NuclearClouds.TryNewest(out double age, out double charge))
        {
            data["newest_burst"] = new Dictionary<string, object?>
            {
                ["age_s"] = Math.Round(age, 3),
                ["kt"] = MushroomCloud.KilotonsFor(charge),
            };
        }

        return Done(data);
    }

    private Reply Set(BridgeCommand command)
    {
        string field = command.String("name");
        if (!command.TryRaw("value", out JsonElement value)) return Failed("set needs a name and a value");

        return BridgeCommand.TrySetField(_config, field, value, out string trouble)
                   ? Done(new() { [field] = BridgeCommand.FieldText(_config, field) })
                   : Failed(trouble);
    }

    private Reply Get(BridgeCommand command)
    {
        string field = command.String("name");
        return BridgeCommand.FieldText(_config, field) is { } text
                   ? Done(new() { [field] = text })
                   : Failed($"no field '{field}' on Config");
    }

    private static Reply Clear()
    {
        NuclearClouds.Clear();
        return Done();
    }

    // A burst where the panel's burst tool would put one, but placed by numbers: metres east and
    // north of the craft being flown, on the ground there. The explosion and the cloud, and no
    // damage -- that is fire control's, and a test shot should not break the scene it is testing.
    private static Reply Burst(BridgeCommand command)
    {
        if (KsaWorld.ControlledVehicle is not { } craft) return Failed("no craft is being flown");
        if (Detonation.BodyFor(craft) is not { } body) return Failed("no body under the craft");

        double kt = command.Number("kt", 0.3);
        if (!(kt > 0.0)) return Failed("kt must be positive");

        double3 craftEcl = KsaWorld.PositionEcl(craft);
        if (MapFrame.TryAt(body.GetPositionEcl(), craftEcl, body.GetRotationAxisCce()) is not { } frame)
        {
            return Failed("no local frame at the craft");
        }

        double3 above = craftEcl + (frame.East * command.Number("east_m", 2000.0))
                        + (frame.North * command.Number("north_m", 0.0));
        if (!KsaWorld.TrySnapToGround(above, out double3 ground)) return Failed("no ground under that point");

        double charge = kt * 1.0e6;

        // Lifted by the fireball, as the burst tool lifts it, so the ball is not drawn half-buried.
        double3 lifted = ground + (frame.Up * Math.Max(MushroomCloud.PeakFireballRadius(kt), 2.0));
        Detonation.Explode(lifted, charge, craft);
        NuclearClouds.Begin(ground, craft, charge);

        return Done(new()
        {
            ["kt"] = kt,
            ["range_m"] = Math.Round(Vec.Len(ground - craftEcl)),
        });
    }

    private Reply Camera(BridgeCommand command)
    {
        if (command.Flag("release", false))
        {
            if (_pose is null) return Done();

            _pose = null;
            KsaWorld.TryHandBackMainView(_saved, _posedFrom, out _, out _);
            _posedFrom = null;
            return Done();
        }

        if (KsaWorld.ControlledVehicle is not { } craft) return Failed("no craft is being flown");

        if (_pose is null)
        {
            _saved = KsaWorld.RememberMainView();
            _posedFrom = craft;
        }

        _pose = new CloudWatch.Pose(command.Number("azimuth_deg", 0.0), command.Number("elevation_deg", 14.0),
                                    command.Number("distance_m", 0.0), command.Number("aim", 0.45));

        return Done(new() { ["held"] = true });
    }

    // KSA's own reloader cannot reach a mod's shader: it maps a path by finding "Content" in it.
    // What it does is ShaderReference.DoLoad, which is internal, and the pass's pipelines are then
    // dropped so they rebuild against the new module. A file that is missing makes DoLoad destroy
    // the old module and keep nothing, so that is checked first.
    private static Reply ReloadShaders()
    {
        MethodInfo? doLoad = typeof(FileReference).GetMethod("DoLoad", BindingFlags.NonPublic | BindingFlags.Instance);
        if (doLoad is null) return Failed("FileReference.DoLoad did not resolve");

        List<string> reloaded = [];
        foreach (string id in ShaderIds)
        {
            if (!ModLibrary.TryGet<ShaderReference>(id, out ShaderReference? shader) || shader is null)
            {
                return Failed($"no shader '{id}'");
            }

            if (!File.Exists(shader.ModPath)) return Failed($"'{shader.ModPath}' is not there");

            try
            {
                doLoad.Invoke(shader, null);
            }
            catch (TargetInvocationException e)
            {
                return Failed($"{id} did not compile: {e.InnerException?.Message ?? e.Message}");
            }

            reloaded.Add(id);
        }

        CloudPass.Release();
        return Done(new() { ["reloaded"] = reloaded });
    }

    // ---- commands that take frames ----------------------------------------------------------

    // Runs the world for so many simulated seconds and stops it again, so what is photographed next
    // is at an age that was asked for rather than one that happened.
    private Reply? BeginStep(BridgeCommand command)
    {
        double seconds = command.Number("seconds", 1.0);
        if (!(seconds > 0.0)) return Failed("seconds must be positive");

        double advanced = 0.0;
        double waited = 0.0;
        KsaWorld.SetPaused(false);

        _running = (dtPlayer, dtSim) =>
        {
            waited += dtPlayer;
            advanced += Math.Max(dtSim, 0.0);

            if (advanced >= seconds)
            {
                KsaWorld.SetPaused(true);
                return Done(new() { ["advanced_s"] = Math.Round(advanced, 3) });
            }

            return waited > (seconds * 20.0) + 30.0 ? Failed($"only {advanced:F2} s passed") : null;
        };

        return null;
    }

    private Reply? BeginLoad(BridgeCommand command)
    {
        string save = command.String("save");
        if (save.Length == 0) return Failed("load needs a save");

        try
        {
            GameSaves.LoadSaveGame(save);
        }
        catch (Exception e)
        {
            return Failed($"could not load '{save}': {e.Message}");
        }

        _pose = null;
        double waited = 0.0;

        _running = (dtPlayer, _) =>
        {
            waited += dtPlayer;

            // A beat past the craft appearing, for the world to settle round it.
            if (waited > 3.0 && KsaWorld.InFlight)
            {
                return Done(new() { ["craft"] = KsaWorld.DisplayName(KsaWorld.ControlledVehicle!) });
            }

            return waited > 60.0 ? Failed("no craft after 60 s") : null;
        };

        return null;
    }

    // Photographs through KSA's own capture, which names files to the second: so each one is moved
    // aside and renamed before the next is asked for, and two can never be one file. Every picture
    // gets a manifest beside it -- the age, the camera and where the burst is on screen -- so nothing
    // has to be matched up by its time afterwards.
    private Reply? BeginCapture(BridgeCommand command)
    {
        string label = command.String("label");
        if (label.Length == 0) label = "shot";
        label = string.Concat(label.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

        int frames = Math.Clamp((int)command.Number("frames", 1.0), 1, 120);
        double everySim = Math.Max(command.Number("every_s", 0.0), 0.0);
        int everyFrames = Math.Max((int)command.Number("every_frames", 1.0), 1);

        string folder = Path.Combine(Outbox, command.Id);
        Directory.CreateDirectory(folder);

        string shots = Path.Combine(KSA.Constants.DocumentsFolderPath, "exports", "screenshots");

        List<Dictionary<string, object?>> taken = [];
        int index = 0;
        bool waiting = false;
        DateTime askedAt = default;
        Dictionary<string, object?>? manifest = null;
        double sinceSim = 0.0;
        int sinceFrames = 0;
        double waited = 0.0;

        // Paused with a spacing asked for, the world is run that far and stopped again before each
        // picture, so the ages are exactly the ones asked for however long a command takes to arrive.
        bool stepping = KsaWorld.IsPaused && everySim > 0.0;
        int settle = 0;

        _running = (dtPlayer, dtSim) =>
        {
            if (!waiting)
            {
                sinceSim += Math.Max(dtSim, 0.0);
                sinceFrames++;

                if (stepping && index > 0)
                {
                    if (sinceSim < everySim)
                    {
                        if (KsaWorld.IsPaused) KsaWorld.SetPaused(false);
                        return null;
                    }

                    if (!KsaWorld.IsPaused)
                    {
                        KsaWorld.SetPaused(true);
                        settle = 0;
                    }

                    // A frame or two for the paused world to be the one drawn.
                    if (++settle < 3) return null;
                }

                bool due = index == 0 || stepping
                           || (everySim > 0.0 ? sinceSim >= everySim : sinceFrames >= everyFrames);
                if (!due) return null;

                manifest = Manifest(label, index);
                askedAt = DateTime.UtcNow.AddSeconds(-0.5);
                if (!KsaWorld.TryRequestScreenshot()) return Failed("KSA would not take a screenshot");

                waiting = true;
                waited = 0.0;
                return null;
            }

            waited += dtPlayer;
            if (waited > 10.0) return Failed($"screenshot {index} never arrived");

            string? file = Directory.Exists(shots)
                               ? new DirectoryInfo(shots).GetFiles("ksa_*.png")
                                                         .Where(f => f.LastWriteTimeUtc >= askedAt && f.Length > 0)
                                                         .OrderByDescending(f => f.LastWriteTimeUtc)
                                                         .FirstOrDefault()?.FullName
                               : null;
            if (file is null) return null;

            string name = $"{index:D2}-{label}";
            string target = Path.Combine(folder, name + ".png");

            try
            {
                File.Move(file, target, overwrite: true);
            }
            catch (IOException)
            {
                // Still being written; the next frame will find it finished.
                return null;
            }

            manifest!["file"] = target;
            File.WriteAllText(Path.Combine(folder, name + ".json"), JsonSerializer.Serialize(manifest, JsonOptions));
            taken.Add(manifest);

            index++;
            waiting = false;
            sinceSim = 0.0;
            sinceFrames = 0;

            return index >= frames ? Done(new() { ["folder"] = folder, ["frames"] = taken }) : null;
        };

        return null;
    }

    // What a picture was of, recorded when it was asked for.
    private Dictionary<string, object?> Manifest(string label, int index)
    {
        Dictionary<string, object?> m = new()
        {
            ["label"] = label,
            ["index"] = index,
            ["wall"] = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            ["paused"] = KsaWorld.IsPaused,
            ["speed"] = KsaWorld.SimulationSpeed,
            ["whiteout"] = Math.Round(BurstFlash.Whiteout, 3),
            ["glare"] = Math.Round(BurstFlash.Glare, 3),
            ["fov_deg"] = Math.Round(KsaWorld.MainViewFovDeg(), 2),
            ["shader_pass"] = _config.ShaderPass,
            ["nuclear_blackout"] = _config.NuclearBlackout,
        };

        if (NuclearClouds.TryNewest(out double age, out double charge))
        {
            double kt = MushroomCloud.KilotonsFor(charge);
            MushroomCloud.Shape shape = MushroomCloud.At(charge, age);

            m["burst_age_s"] = Math.Round(age, 3);
            m["kt"] = kt;
            m["cap_centre_m"] = Math.Round(shape.CapCentre);
            m["cap_radius_m"] = Math.Round(shape.CapRadius);
        }

        if (NuclearClouds.TryWatch(out double3 burstEcl, out double3 up, out _, out double top, out _))
        {
            m["burst_screen"] = Screen(burstEcl);
            m["cap_screen"] = NuclearClouds.TryNewest(out double a, out double c)
                                  ? Screen(burstEcl + (Vec.Unit(up) * MushroomCloud.At(c, a).CapCentre))
                                  : null;
            m["top_screen"] = Screen(burstEcl + (Vec.Unit(up) * top));

            if (NuclearClouds.TryNewest(out double nowAge, out double nowCharge))
            {
                m["cloud_box"] = CloudBox(burstEcl, Vec.Unit(up), MushroomCloud.At(nowCharge, nowAge));
            }

            if (KsaWorld.TryMainCameraPose(out double3 eye, out _))
            {
                double3 toEye = eye - burstEcl;
                double range = Vec.Len(toEye);
                m["camera_range_m"] = Math.Round(range);
                m["camera_elevation_deg"] = Math.Round(
                    90.0 - double.RadiansToDegrees(Vec.AngleBetween(toEye, up)), 2);
            }

            if (Detonation.BodyFor(KsaWorld.ControlledVehicle) is { } body)
            {
                m["sun_elevation_deg"] = Math.Round(KsaWorld.SunElevationDeg(body, burstEcl), 2);
            }
        }

        return m;
    }

    // The cloud as it stands now, as a box on screen: the stem's flared foot to the cap's crown, and
    // the cap's width either side. What a crop wants, where the final top is kilometres of sky above
    // a cloud still rising.
    private static double[]? CloudBox(double3 burstEcl, double3 up, MushroomCloud.Shape shape)
    {
        if (!KsaWorld.TryMainCameraPose(out _, out double3 forward)) return null;

        double3 right = Vec.Unit(Vec.Cross(forward, up));
        if (!Vec.IsFinite(right)) return null;

        double wide = Math.Max(shape.CapRadius + shape.CapTube, shape.StemRadius * 3.5);
        double crown = shape.CapCentre + (2.1 * shape.CapTube);

        double3[] corners =
        [
            burstEcl + (right * wide), burstEcl - (right * wide),
            burstEcl + (up * crown) + (right * wide), burstEcl + (up * crown) - (right * wide),
        ];

        double[]? box = null;
        foreach (double3 corner in corners)
        {
            if (Screen(corner) is not { } p) return null;

            box = box is null ? [p[0], p[1], p[0], p[1]]
                              : [Math.Min(box[0], p[0]), Math.Min(box[1], p[1]),
                                 Math.Max(box[2], p[0]), Math.Max(box[3], p[1])];
        }

        return box;
    }

    // Where a point falls in the picture, as fractions from the top left, or null when it is behind
    // the camera. The same matrix the frame was drawn with, so a crop taken off it is exact.
    private static double[]? Screen(double3 pointEcl)
    {
        if (Program.GetMainCamera() is not { } camera) return null;

        double4 clip = camera.EgoToClipDouble(pointEcl - camera.PositionEcl);
        if (!(clip.W > 1e-6)) return null;

        return [Math.Round((clip.X / clip.W * 0.5) + 0.5, 4), Math.Round((clip.Y / clip.W * 0.5) + 0.5, 4)];
    }
}
