using System.Globalization;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Flies a craft carrying a released store off the ground, lets the store go, and says where it
/// landed against where the sight said it would.
///
/// <para>A miss has three sources and one flight separates them. At the release the sight is flown
/// twice: once as the ring is, off the rack, and once off the state the round actually left with.
/// Those two differ by what the sight assumes about the release; the second against the landing is
/// the flight itself. A guided store is designated onto the ring as it goes, which is what a player
/// does, so its miss from the ring is the tail kit's.</para>
/// </summary>
internal sealed class DropScenario
{
    /// <summary>
    /// <c>[metres above ground][,degrees off vertical][,guided|dumb][,seconds until the craft is
    /// destroyed]</c>, every field optional.
    /// </summary>
    public readonly record struct Request(double ReleaseAglMetres, double PitchDeg, bool Guided,
                                          double KillAfterSeconds)
    {
        public static bool TryParse(string text, out Request request, out string trouble)
        {
            request = new Request(1000.0, 0.0, Guided: true, double.NaN);
            trouble = string.Empty;

            string[] fields = text.Split(',', StringSplitOptions.TrimEntries);
            double agl = request.ReleaseAglMetres;
            double pitch = request.PitchDeg;
            double kill = request.KillAfterSeconds;
            bool guided = request.Guided;

            if (Given(0) && !TryNumber(fields[0], out agl)) trouble = $"'{fields[0]}' is not a height";
            else if (Given(1) && !TryNumber(fields[1], out pitch)) trouble = $"'{fields[1]}' is not an angle";
            else if (Given(2) && fields[2] is not ("guided" or "dumb")) trouble = $"'{fields[2]}' is neither guided nor dumb";
            else if (Given(3) && !TryNumber(fields[3], out kill)) trouble = $"'{fields[3]}' is not a time";
            else if (agl <= 0.0) trouble = "the release height has to be above the ground";
            else if (pitch is < 0.0 or >= 90.0) trouble = "the pitch is degrees off vertical, 0 to 90";

            if (trouble.Length > 0) return false;

            if (Given(2)) guided = fields[2] == "guided";

            request = new Request(agl, pitch, guided, kill);
            return true;

            bool Given(int i) => fields.Length > i && fields[i].Length > 0;
        }

        public string Describe()
            => $"release at {ReleaseAglMetres:F0} m, {PitchDeg:F0} deg off vertical, "
               + (Guided ? "guided onto the ring" : "unguided")
               + (double.IsFinite(KillAfterSeconds)
                      ? $", craft destroyed {KillAfterSeconds:F1} s after the release"
                      : "");

        private static bool TryNumber(string text, out double value)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && double.IsFinite(value);
    }

    private enum Phase
    {
        WaitingForWorld,
        Climbing,
        Falling,
        Lingering,
    }

    // Simulated seconds a store has to have been on a craft before it is flown: a craft is crewed a
    // few frames after a load, and its parts settle onto the ground after that.
    private const double SettleSeconds = 3.0;

    private const double PitchOverAglMetres = 150.0;
    private const double PitchRateDegPerSecond = 6.0;

    private const double LiftOffSeconds = 20.0;
    private const double ClimbBudgetSeconds = 150.0;
    private const double FallBudgetSeconds = 180.0;

    // Wall clock after the burst, so the chase's hold and hand-back reach the log before the harness
    // closes the game.
    private const double LingerSeconds = 6.0;

    private const double BarMetres = 30.0;
    private const double ProgressEverySeconds = 2.0;

    private readonly Request _request;
    private readonly Action<string> _report;
    private readonly Func<WeaponSystem, BombSightOverlay> _sightFor;

    // A load takes frames, and the scene it replaces can carry a store of its own. Flying that one
    // is a run about the wrong craft, which reads exactly like a run about the right one.
    private readonly HashSet<Vehicle> _fromBeforeTheLoad = [];
    private readonly List<Vehicle> _census = [];

    private Phase _phase = Phase.WaitingForWorld;
    private double _sim;
    private double _foundAt = double.NaN;
    private double _stagedAt;
    private double _saidAt = double.NegativeInfinity;
    private double _lingered;

    private WeaponSystem? _battery;
    private Vehicle? _craft;
    private string _craftName = string.Empty;
    private double _pitchDeg;

    private IProjectile? _round;
    private Celestial? _body;
    private double _releasedAt;
    private double3 _ringAnchor;
    private double3 _flownAnchor;
    private bool _haveRing;
    private bool _haveFlown;
    private bool _killed;
    private string _verdict = string.Empty;

    public DropScenario(Request request, Action<string> report,
                        Func<WeaponSystem, BombSightOverlay> sightFor)
    {
        _request = request;
        _report = report;
        _sightFor = sightFor;
    }

    /// <summary>Which phase it is in, for a timeout to name.</summary>
    public string Where => _phase.ToString();

    /// <summary>Remembers every craft in the world, so none of them is flown once the save replaces it.</summary>
    public void NoteTheWorldBeforeTheLoad()
    {
        KsaWorld.CollectVehicles(_census);
        foreach (Vehicle v in _census) _fromBeforeTheLoad.Add(v);
    }

    /// <summary>Gives the craft back, however the run ends.</summary>
    public void Release()
    {
        if (_craft is { } craft && KsaWorld.IsAlive(craft)) AttitudeHook.Release(craft);
    }

    /// <summary>One frame. Null while the drop is still going, and the outcome once it is not.</summary>
    public string? Update(WeaponSystems roster, double dt, double playerStep)
    {
        _sim += dt;

        switch (_phase)
        {
            case Phase.WaitingForWorld:
                return Wait(roster);

            case Phase.Climbing:
                return Climb(dt);

            case Phase.Falling:
                return Fall(dt);

            case Phase.Lingering:
                _lingered += playerStep;
                return _lingered >= LingerSeconds ? _verdict : null;
        }

        return null;
    }

    private string? Wait(WeaponSystems roster)
    {
        WeaponSystems.Entry? found = null;

        if (KsaWorld.InFlight)
        {
            foreach (WeaponSystems.Entry e in roster.All)
            {
                if (e.Battery.Platform is not { } craft || e.Battery.Launcher is null) continue;
                if (_fromBeforeTheLoad.Contains(craft)) continue;
                if (e.Battery.Munition.Powered || !e.Battery.Munition.HitsTerrain) continue;
                if (e.Battery.Ammo <= 0) continue;

                found = e;

                // The chase rides whatever the panel is focused on, which is the controlled craft.
                if (ReferenceEquals(craft, KsaWorld.ControlledVehicle)) break;
            }
        }

        if (found is null)
        {
            _foundAt = double.NaN;

            if (_sim - _saidAt >= 10.0)
            {
                _saidAt = _sim;
                _report("waiting -- " + (KsaWorld.InFlight ? "no store on any craft yet" : "no craft in flight"));
            }

            return null;
        }

        if (double.IsNaN(_foundAt)) _foundAt = _sim;
        if (_sim - _foundAt < SettleSeconds) return null;

        _battery = found.Battery;
        _craft = found.Battery.Platform!;
        _craftName = KsaWorld.DisplayName(_craft);

        if (_request.Guided && !_battery.Munition.Steers)
        {
            return $"FAIL a guided drop was asked for, and the {_battery.Munition.DisplayName} does not steer";
        }

        found.Policy.ChaseRounds = true;
        found.Policy.DrawBombSight = true;

        _report($"flying {_craftName}"
                + (ReferenceEquals(_craft, KsaWorld.ControlledVehicle)
                       ? ""
                       : " -- not the controlled craft, so the chase will not ride its store")
                + $": {_battery.Ammo} x {_battery.Munition.DisplayName}, chase on");

        VehicleCommand.Stage(_craft);
        _stagedAt = _sim;
        _phase = Phase.Climbing;
        return null;
    }

    private string? Climb(double dt)
    {
        WeaponSystem battery = _battery!;
        Vehicle craft = _craft!;

        if (!KsaWorld.IsAlive(craft)) return "FAIL the craft was lost before the release";
        if (KsaWorld.ParentBody(craft) is not { } body) return "FAIL the craft has no body under it";

        if (!Steer(craft, body, dt, out double agl, out double3 up, out double3 wanted)) return null;

        double3 overGround = KsaWorld.VelocityEcl(craft)
                             - KsaWorld.GroundVelocityAt(craft, KsaWorld.PositionEcl(craft));
        double flying = _sim - _stagedAt;

        if (flying > LiftOffSeconds && agl < 20.0)
        {
            return $"FAIL the craft never left the ground: {agl:F0} m after {flying:F0} s";
        }

        if (flying > ClimbBudgetSeconds)
        {
            return $"FAIL the craft reached {agl:F0} m of the {_request.ReleaseAglMetres:F0} asked for "
                   + $"in {flying:F0} s";
        }

        if (_sim - _saidAt >= ProgressEverySeconds)
        {
            _saidAt = _sim;
            _report($"climbing: {agl:F0} m, {Vec.Dot(overGround, up):F0} m/s up, "
                    + $"{Vec.Len(overGround):F0} m/s over the ground, pitch {_pitchDeg:F0} deg, "
                    + $"nose {NoseOffDeg(craft, body, wanted):F0} deg off it");
        }

        if (agl < _request.ReleaseAglMetres || _pitchDeg < _request.PitchDeg) return null;

        return Drop(battery, craft, body, agl, up, overGround, wanted);
    }

    private string? Drop(WeaponSystem battery, Vehicle craft, Celestial body, double agl, double3 up,
                         double3 overGround, double3 wanted)
    {
        BombSightOverlay sight = _sightFor(battery);

        _haveRing = sight.TryPredictNow(battery, out double3 ringEcl)
                    && KsaWorld.TryAnchorToGround(ringEcl, out _, out _ringAnchor);

        if (_request.Guided)
        {
            if (!_haveRing) return "FAIL the sight had no solution to designate";

            if (!KsaWorld.TryAnchorToGround(ringEcl, out object? handle, out double3 anchor)
                || handle is null
                || !KsaWorld.TryGroundAnchorEcl(handle, anchor, out double3 aimEcl, out double3 aimVelocity))
            {
                return "FAIL the sight's impact could not be put on the ground";
            }

            battery.Designate(Aimpoint.OnGround(handle, anchor, aimEcl, aimVelocity), "the sight's impact");
        }

        int before = battery.Rounds.Count;
        if (!battery.Release() || battery.Rounds.Count <= before) return "FAIL the store would not release";

        IProjectile round = battery.Rounds[^1];
        _round = round;
        _body = body;
        _releasedAt = _sim;

        double3 groundVelocity = KsaWorld.GroundVelocityAt(craft, battery.PlatformEcl);
        _haveFlown = sight.TryPredictFrom(battery, round.PositionEcl, round.VelocityEcl - groundVelocity,
                                          out double3 flownEcl)
                     && KsaWorld.TryAnchorToGround(flownEcl, out _, out _flownAnchor);

        double3 spin = round is Slug slug ? slug.SpinVelocityEcl : Vec.Zero;
        double3 ejected = round.VelocityEcl - KsaWorld.VelocityEcl(craft) - spin;
        double rackDeg = battery.Launcher is { } launcher
                         && LauncherPart.TryGetTubeAxisEcl(craft, launcher, battery.PodsPart,
                                                           battery.Profile, 0, out double3 axis)
                             ? double.RadiansToDegrees(Vec.AngleBetween(ejected, axis))
                             : double.NaN;

        double speed = Vec.Len(overGround);
        double pathDeg = speed > 0.1
                             ? double.RadiansToDegrees(Math.Asin(Math.Clamp(Vec.Dot(overGround, up) / speed, -1.0, 1.0)))
                             : 90.0;

        _report("CAPTURE release");
        _report($"released at {agl:F0} m, {speed:F0} m/s over the ground at {pathDeg:F0} deg above the "
                + $"horizon, nose {NoseOffDeg(craft, body, wanted):F0} deg off the command; ejected "
                + $"{Vec.Len(ejected):F1} m/s at {rackDeg:F0} deg to the rack, spin {Vec.Len(spin):F2} m/s");

        _report(_haveRing && _haveFlown
                    ? $"the ring is {Offset(_flownAnchor, _ringAnchor)} from a flight off the release state"
                    : $"no comparison: the ring {(_haveRing ? "solved" : "did not solve")}, the flight off "
                      + $"the release state {(_haveFlown ? "solved" : "did not solve")}");

        _phase = Phase.Falling;
        return null;
    }

    private string? Fall(double dt)
    {
        IProjectile round = _round!;
        Vehicle craft = _craft!;
        double since = _sim - _releasedAt;

        if (KsaWorld.IsAlive(craft) && _body is { } body)
        {
            // Still flown, so a craft left to tumble does not fall back through the store's path.
            Steer(craft, body, dt, out _, out _, out _);

            if (double.IsFinite(_request.KillAfterSeconds) && !_killed && since >= _request.KillAfterSeconds)
            {
                _killed = true;
                AttitudeHook.Release(craft);
                KsaWorld.WaitForVehicleSolvers();
                KsaWorld.Destroy(craft, blastSeverity: 50f);
                _report($"destroyed {_craftName} {since:F1} s after the release, with the store still falling");
            }
        }

        if (round.State == RoundState.Flying)
        {
            if (since > FallBudgetSeconds) return $"FAIL the store was still falling {since:F0} s after the release";

            if (_sim - _saidAt >= ProgressEverySeconds && _body is { } under)
            {
                _saidAt = _sim;
                double3 overGround = round.VelocityEcl - KsaWorld.GroundVelocityAt(under, round.PositionEcl);
                _report($"falling: {since:F0} s, {Vec.Len(overGround):F0} m/s over the ground"
                        + (_battery!.Platform is null ? ", loose" : ""));
            }

            return null;
        }

        if (round.State != RoundState.Detonated) return $"FAIL the store ended {round.State} rather than landing";

        return Landed(round, since);
    }

    private string? Landed(IProjectile round, double since)
    {
        if (_body is not { } body) return "FAIL the store landed with no body recorded";

        // The burst is placed at an instant inside the frame and the body sample is at its end, so
        // the ground's own travel across that gap comes off before anchoring. At ~30 km/s it is
        // hundreds of metres.
        double3 burst = round.PositionEcl;
        double3 carried = KsaWorld.GroundVelocityAt(body, burst) * round.DetonationElapsedInFrame;

        if (!KsaWorld.TryAnchorToGround(burst - carried, out _, out double3 landed))
        {
            return "FAIL the burst could not be put on the ground";
        }

        bool craftLived = _craft is { } craft && KsaWorld.IsAlive(craft);

        _report($"landed {since:F1} s after the release{(craftLived ? "" : ", its craft already gone")}: "
                + (_haveRing ? Offset(_ringAnchor, landed) : "unknown")
                + $" from the ring{(_request.Guided ? " it was designated onto" : "")}, "
                + (_haveFlown ? Offset(_flownAnchor, landed) : "unknown")
                + " from the flight off the release state");

        double miss = _haveRing ? Vec.Len(landed - _ringAnchor) : double.PositiveInfinity;

        _verdict = miss <= BarMetres
                       ? $"PASS {miss:F0} m from the ring, inside {BarMetres:F0} m"
                       : $"FAIL {(double.IsFinite(miss) ? $"{miss:F0} m" : "no ring")} from the ring, "
                         + $"past {BarMetres:F0} m";

        _phase = Phase.Lingering;
        return null;
    }

    // Engine lit, full throttle, and the nose held on the pitch programme. Every frame, because an
    // attitude hold is dropped the frame it is not restated.
    private bool Steer(Vehicle craft, Celestial body, double dt, out double agl, out double3 up,
                       out double3 wanted)
    {
        VehicleCommand.SetEngine(craft, running: true);
        VehicleCommand.DriveThrottle(craft, 1.0);

        wanted = Vec.Zero;
        if (!TryLocalFrame(craft, body, out up, out double3 east, out double3 north, out agl)) return false;

        if (agl > PitchOverAglMetres)
        {
            _pitchDeg = Math.Min(_request.PitchDeg, _pitchDeg + (PitchRateDegPerSecond * dt));
        }

        double pitch = double.DegreesToRadians(_pitchDeg);
        wanted = (up * Math.Cos(pitch)) + (east * Math.Sin(pitch));

        // North is square to every direction the programme asks for, so the roll never has to
        // be re-derived through the vertical.
        doubleQuat toCci = body.GetCce2Cci();
        AttitudeHook.Hold(craft, wanted.Transform(toCci), north.Transform(toCci));
        return true;
    }

    private static bool TryLocalFrame(Vehicle craft, Celestial body, out double3 up, out double3 east,
                                      out double3 north, out double agl)
    {
        double3 here = KsaWorld.PositionEcl(craft);
        up = Vec.Unit(here - body.GetPositionEcl());

        double3 axis = Vec.Unit(body.GetBodyFixed2Ecl() * new double3(0, 0, 1));
        east = Vec.Unit(Vec.Cross(axis, up));
        north = Vec.Cross(up, east);

        agl = KsaWorld.TrySnapToGround(here, out double3 ground, out double3 centre)
                  ? Vec.Len(here - centre) - Vec.Len(ground - centre)
                  : double.NaN;

        return Vec.Len2(east) > 0.5 && double.IsFinite(agl);
    }

    private static double NoseOffDeg(Vehicle craft, Celestial body, double3 wantedEcl)
        => KsaWorld.TryControlFrameCci(craft, body, out double3 nose, out _, out _)
               ? double.RadiansToDegrees(Vec.AngleBetween(nose, wantedEcl.Transform(body.GetCce2Cci())))
               : double.NaN;

    // Metres between two ground anchors on one body, with the east and north of it: a distance says
    // a drop missed, and only the direction says whether the release or the fall moved it.
    private static string Offset(double3 from, double3 to)
    {
        double3 d = to - from;
        double3 up = Vec.Unit(from);
        double3 east = Vec.Unit(Vec.Cross(new double3(0, 0, 1), up));
        double3 north = Vec.Cross(up, east);

        return $"{Vec.Len(d):F0} m (E {Math.Round(Vec.Dot(d, east)):+0;-0;0}, "
               + $"N {Math.Round(Vec.Dot(d, north)):+0;-0;0})";
    }
}
