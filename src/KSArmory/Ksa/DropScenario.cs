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
    /// destroyed][,warp]</c>, every field optional.
    ///
    /// <para><c>warp</c> is a factor to set once the store is away, or <c>auto</c> for KSA's own
    /// warp-to-a-time. They are not the same test: the engine <em>refuses</em> a speed change while
    /// an auto-warp runs, and that refusal is what <see cref="WarpPolicy"/> used to abandon the
    /// store over.</para>
    ///
    /// <para><c>again</c> is <c>&lt;seconds&gt;@&lt;metres&gt;</c> — send the store somewhere else
    /// that long after the release, that far north of the ring — or <c>&lt;seconds&gt;@clear</c> to
    /// drop the designation instead. A store sent somewhere is scored against the new place; one
    /// whose designation is cleared is still scored against the old, because clearing is not how a
    /// store is recalled.</para>
    /// </summary>
    public readonly record struct Request(double ReleaseAglMetres, double PitchDeg, bool Guided,
                                          double KillAfterSeconds, double WarpFactor, bool AutoWarp,
                                          double AgainAfterSeconds, double AgainOffsetMetres)
    {
        public static bool TryParse(string text, out Request request, out string trouble)
        {
            request = new Request(1000.0, 0.0, Guided: true, double.NaN, double.NaN, AutoWarp: false,
                                  double.NaN, double.NaN);
            trouble = string.Empty;

            string[] fields = text.Split(',', StringSplitOptions.TrimEntries);
            double agl = request.ReleaseAglMetres;
            double pitch = request.PitchDeg;
            double kill = request.KillAfterSeconds;
            double warp = request.WarpFactor;
            bool auto = false;
            bool guided = request.Guided;
            double againAt = double.NaN;
            double againBy = double.NaN;

            if (Given(0) && !TryNumber(fields[0], out agl)) trouble = $"'{fields[0]}' is not a height";
            else if (Given(1) && !TryNumber(fields[1], out pitch)) trouble = $"'{fields[1]}' is not an angle";
            else if (Given(2) && fields[2] is not ("guided" or "dumb")) trouble = $"'{fields[2]}' is neither guided nor dumb";
            else if (Given(3) && !TryNumber(fields[3], out kill)) trouble = $"'{fields[3]}' is not a time";
            else if (Given(4) && fields[4] != "auto" && !TryNumber(fields[4], out warp)) trouble = $"'{fields[4]}' is neither a warp factor nor 'auto'";
            else if (agl <= 0.0) trouble = "the release height has to be above the ground";
            else if (pitch is < 0.0 or >= 90.0) trouble = "the pitch is degrees off vertical, 0 to 90";
            else if (Given(4) && fields[4] != "auto" && warp < 1.0) trouble = "the warp factor has to be at least 1";
            else if (Given(5) && !TryAgain(fields[5], out againAt, out againBy)) trouble = $"'{fields[5]}' is not <seconds>@<metres> or <seconds>@clear";

            if (trouble.Length > 0) return false;

            if (Given(2)) guided = fields[2] == "guided";
            if (Given(4) && fields[4] == "auto") auto = true;

            request = new Request(agl, pitch, guided, kill, warp, auto, againAt, againBy);
            return true;

            bool Given(int i) => fields.Length > i && fields[i].Length > 0;

            // NaN metres means clear rather than re-send, which is a different question: whether a
            // store already steering keeps its aim when the installation stops pointing at anything.
            static bool TryAgain(string text, out double at, out double by)
            {
                at = double.NaN;
                by = double.NaN;

                string[] halves = text.Split('@');
                if (halves.Length != 2 || !TryNumber(halves[0], out at) || at < 0.0) return false;
                if (halves[1] == "clear") return true;

                return TryNumber(halves[1], out by);
            }
        }

        public string Describe()
            => $"release at {ReleaseAglMetres:F0} m, {PitchDeg:F0} deg off vertical, "
               + (Guided ? "guided onto the ring" : "unguided")
               + (double.IsFinite(KillAfterSeconds)
                      ? $", craft destroyed {KillAfterSeconds:F1} s after the release"
                      : "")
               + (AutoWarp ? ", then KSA's own warp-to-a-time"
                           : double.IsFinite(WarpFactor) ? $", then {WarpFactor:F0}x timewarp" : "")
               + (double.IsFinite(AgainAfterSeconds)
                      ? double.IsFinite(AgainOffsetMetres)
                            ? $", sent {AgainOffsetMetres:F0} m north {AgainAfterSeconds:F0} s after the release"
                            : $", designation cleared {AgainAfterSeconds:F0} s after the release"
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
    // closes the game. Sized off the hold itself rather than fixed: a nuclear burst is watched for
    // as long as it leaves something moving, and a six-second wait closed the game a sixth of the
    // way into it -- which is the hand-back, the part worth checking, never happening in any run.
    private double LingerSeconds
        => (_round is { } round
                ? BurstEjecta.LingerSeconds(round.PositionEcl, null, round.Munition.ChargeKg)
                : ChaseView.MinLingerSeconds)
           + 4.0;

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
    private bool _againDone;
    private object? _ringBody;
    private double3 _againAnchor;
    private bool _haveAgain;

    // What the ring claimed at the moment of the send, and where the store would have come down
    // untouched. A send the kit cannot reach is scored against these rather than against the aim:
    // see WalkedFarEnough.
    private bool _sentWasInReach;
    private double _sentReachMetres;
    private double3 _sentImpactAnchor;
    private bool _haveSentImpact;
    private bool _warpSet;
    private double _warpObserved;
    private string _verdict = string.Empty;

    public DropScenario(Request request, Action<string> report,
                        Func<WeaponSystem, BombSightOverlay> sightFor, bool watchTheCloud = false)
    {
        _request = request;
        _report = report;
        _sightFor = sightFor;
        _watchTheCloud = watchTheCloud;
    }

    // Whether this run exists to be looked at rather than scored. It keeps the cloud drawn; the
    // chase stays ON, because the chase is the only thing that holds the view on the burst. Turned
    // off, the view follows the LAUNCHING CRAFT, which is climbing away at 300 m/s and is 7 km up
    // by 0.6 of the rise -- flown, with the cloud out of frame in every capture.
    private readonly bool _watchTheCloud;

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
                CaptureCloud();
                return _lingered >= LingerSeconds ? _verdict : null;
        }

        return null;
    }

    // Fractions of the rise to photograph the cloud at. They are the rows of the tracking table in
    // docs/NUCLEAR-EFFECT.md, so a shot can be held against the measured shape rather than judged
    // on its own -- which is the whole difficulty with a cloud: every version of it looks like a
    // cloud, and only the shape at a stated age says which one is right.
    //
    // The last is 0.95 rather than 1.00 because the chase hands the view back at exactly the rise:
    // a capture on the boundary is a race with the release, and the frame that came back was the
    // launching craft against a cloud deck with the mushroom nowhere in it.
    private static readonly double[] CloudCaptureFractions = [0.10, 0.30, 0.60, 0.95];

    private int _captured;

    // Cues a screenshot at each of those ages, for a burst that grew a cloud. The harness scores
    // where a store landed and cannot say whether the cloud over it looks like one, which is the
    // only question left about the cloud.
    //
    // _lingered is wall clock and the cloud is on simulated time, which agree only at 1x. The
    // linger runs at 1x, so these land where they say; a scenario warping through it would
    // photograph the wrong ages, and is not one anybody flies.
    private void CaptureCloud()
    {
        if (_round is not { } round) return;
        if (round.Munition.ChargeKg < MushroomCloud.ThresholdKg) return;

        while (_captured < CloudCaptureFractions.Length)
        {
            double fraction = CloudCaptureFractions[_captured];
            if (_lingered < fraction * MushroomCloud.RiseSeconds) return;

            _captured++;

            // The game's own framebuffer rather than the desktop: tools/screenshot.sh needs the
            // window in front and an unattended run on a machine somebody is using never has it.
            // This also comes back without the panel over the cloud.
            bool shot = KsaWorld.TryRequestScreenshot();

            if (CloudPassCost.Report() is { Length: > 0 } cost) _report(cost);

            _report($"{(shot ? "SHOT" : "CAPTURE")} cloud at {fraction:F2} of the rise "
                    + $"({fraction * MushroomCloud.RiseSeconds:F1} s), "
                    + $"top {MushroomCloud.DrawnCloudTop(MushroomCloud.KilotonsFor(round.Munition.ChargeKg)) / 1000.0:F2} km");
        }
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

        // Watching the cloud is a different run from scoring a drop, and it wants the opposite of
        // everything the scoring one does. The craft stays ON THE PAD: a rocket that climbs away is
        // a rocket the view follows away, and with the chase off the camera is the craft's. It
        // never takes off, so the burst happens where the camera already is and the cloud grows in
        // front of it.
        found.Policy.ChaseRounds = !_watchTheCloud;
        found.Policy.DrawBombSight = !_watchTheCloud;

        if (_watchTheCloud)
        {
            _report($"{_craftName} stays on the pad: {_battery.Ammo} x "
                    + $"{_battery.Munition.DisplayName}, chase off, watching from where it stands");

            _stagedAt = _sim;

            return Drop(_battery, _craft, KsaWorld.ParentBody(_craft)!, 0.0,
                        KsaWorld.LocalUp(_craft), double3.Zero, KsaWorld.LocalUp(_craft));
        }

        _report($"flying {_craftName}"
                + (ReferenceEquals(_craft, KsaWorld.ControlledVehicle)
                       ? ""
                       : " -- not the controlled craft, so the chase will not ride its store")
                + $": {_battery.Ammo} x {_battery.Munition.DisplayName}, chase on");

        AttitudeHook.Stage(_craft);
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
                    && KsaWorld.TryAnchorToGround(ringEcl, out _ringBody, out _ringAnchor);

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

        // A second after the release, so the store is clear of the rack and the sight has settled.
        if (!_warpSet && since >= 1.0 && (_request.AutoWarp || double.IsFinite(_request.WarpFactor)))
        {
            _warpSet = true;
            ApplyWarp();
        }

        if (!_againDone && double.IsFinite(_request.AgainAfterSeconds)
            && since >= _request.AgainAfterSeconds)
        {
            _againDone = true;
            SendItSomewhereElse(since);
        }

        if (round.State == RoundState.Flying)
        {
            // The store taken out of the world, which is not the same as the store failing to
            // arrive and must not be reported as one. Its State is never written when this happens:
            // AbandonFlight simply drops it from the roster and nothing steps it again, so the
            // budget below would eventually call it "still falling" 180 s later. Asked of the
            // battery rather than of the round, because only the roster knows.
            if (_battery is { } owner && !Holds(owner, round))
            {
                return $"FAIL the store was taken out of the world {since:F1} s after the release, "
                       + $"at {KsaWorld.SimulationSpeed:F0}x -- it was still flying";
            }

            if (since > FallBudgetSeconds) return $"FAIL the store was still falling {since:F0} s after the release";

            if (_sim - _saidAt >= ProgressEverySeconds && _body is { } under)
            {
                _saidAt = _sim;
                double3 overGround = round.VelocityEcl - KsaWorld.GroundVelocityAt(under, round.PositionEcl);
                _warpObserved = Math.Max(_warpObserved, KsaWorld.SimulationSpeed);
                _report($"falling: {since:F0} s, {Vec.Len(overGround):F0} m/s over the ground"
                        + (_battery!.Platform is null ? ", loose" : "")
                        + $", {KsaWorld.SimulationSpeed:F0}x"
                        + (KsaWorld.IsAutoWarpActive ? " (auto)" : ""));
            }

            return null;
        }

        if (round.State != RoundState.Detonated) return $"FAIL the store ended {round.State} rather than landing";

        return Landed(round, since);
    }

    // Whether the roster still has this round. A landed one leaves on the frame it detonates, so
    // this is only meaningful while it is flying.
    private static bool Holds(WeaponSystem battery, IProjectile round)
    {
        foreach (IProjectile held in battery.Rounds)
        {
            if (ReferenceEquals(held, round)) return true;
        }

        return false;
    }

    // The half of post-release aiming a suite cannot reach: a designation arriving while the store
    // is already falling, and the region it is judged against.
    private void SendItSomewhereElse(double since)
    {
        if (_battery is not { } battery) return;

        if (!double.IsFinite(_request.AgainOffsetMetres))
        {
            battery.ClearDesignation();
            _report($"CAPTURE again -- designation cleared {since:F1} s after the release; "
                    + "the store should keep the aim it already has");
            return;
        }

        if (_body is not { } body || !_haveRing
            || !KsaWorld.TryGroundAnchorEcl(_ringBody, _ringAnchor, out double3 ringEcl, out _))
        {
            _report("again: the ring could not be put back on the ground");
            return;
        }

        // North of the ring, in the local frame there. Any fixed direction would do; north is the
        // one the ascent already uses, so a run reads the same way throughout.
        double3 up = Vec.Unit(ringEcl - body.GetPositionEcl());
        double3 axis = Vec.Unit(body.GetBodyFixed2Ecl() * new double3(0, 0, 1));
        double3 north = Vec.Cross(up, Vec.Unit(Vec.Cross(axis, up)));

        double3 wantedEcl = ringEcl + (north * _request.AgainOffsetMetres);

        if (!KsaWorld.TryAnchorToGround(wantedEcl, out object? handle, out double3 anchor)
            || handle is null
            || !KsaWorld.TryGroundAnchorEcl(handle, anchor, out double3 aimEcl, out double3 aimVel))
        {
            _report("again: the new place could not be put on the ground");
            return;
        }

        _againAnchor = anchor;
        _haveAgain = true;

        _sentWasInReach = true;
        _haveSentImpact = false;

        if (StoreReach.FallingStore(battery) is { } measured)
        {
            TailKitReach was = StoreReach.SolveNow(battery, measured);
            _sentWasInReach = !was.Known || was.Covers(aimEcl);
            _sentReachMetres = was.RadiusMetres;
            _haveSentImpact = was.Known
                              && KsaWorld.TryAnchorToGround(was.ImpactEcl, out _, out _sentImpactAnchor);
        }

        string reach = StoreReach.FallingStore(battery) is { } speaking
                           ? StoreReach.SolveNow(battery, speaking).Describe(aimEcl)
                           : "no store in the air";

        battery.Designate(Aimpoint.OnGround(handle, anchor, aimEcl, aimVel), "somewhere else");
        _report($"CAPTURE again -- sent {_request.AgainOffsetMetres:F0} m north {since:F1} s after "
                + $"the release: {reach}");
    }

    private void ApplyWarp()
    {
        if (_request.AutoWarp)
        {
            // Half the remaining budget, which is long enough that the warp is still running while
            // the store falls -- the state the engine refuses a speed change in, and the one this
            // mode exists to sit in. The margin is KSA's own stopping distance.
            bool started = KsaWorld.TryAutoWarpTo(FallBudgetSeconds * 0.5, marginSeconds: 10.0);
            _report(started
                        ? "CAPTURE warp -- started KSA's own warp-to-a-time"
                        : "warp: KSA refused to start a warp-to-a-time");
            return;
        }

        bool set = KsaWorld.SetSimulationSpeed(_request.WarpFactor);
        _report(set
                    ? $"CAPTURE warp -- asked for {_request.WarpFactor:F0}x, world reads {KsaWorld.SimulationSpeed:F0}x"
                    : $"warp: {_request.WarpFactor:F0}x was refused");
    }

    private string? Landed(IProjectile round, double since)
    {
        // Back to real time before the linger, or the hand-back is watched at warp.
        if (_warpSet)
        {
            KsaWorld.StopAutoWarp();
            KsaWorld.SetSimulationSpeed(1.0);
            _report($"warp: peaked at {_warpObserved:F0}x while the store fell");
        }

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
                + " from the flight off the release state"
                + (_haveAgain ? $", {Offset(_againAnchor, landed)} from where it was sent" : ""));

        // A send the kit could never reach is a different question, and asking the arriving one of
        // it makes a run that can only ever fail -- which in a checklist is worse than no run at
        // all, because a red that is supposed to be red teaches everyone to skip the file.
        if (_haveAgain && !_sentWasInReach)
        {
            _verdict = WalkedFarEnough(landed);
            _phase = Phase.Lingering;
            return null;
        }

        // A store sent somewhere else is judged against there. A store whose designation was merely
        // cleared is still judged against the ring: clearing is not a recall, and the point of that
        // run is that the aim it already had survives.
        bool sent = _haveAgain;
        double miss = sent ? Vec.Len(landed - _againAnchor)
                           : _haveRing ? Vec.Len(landed - _ringAnchor) : double.PositiveInfinity;

        string what = sent ? "where it was sent" : "the ring";

        _verdict = miss <= BarMetres
                       ? $"PASS {miss:F0} m from {what}, inside {BarMetres:F0} m"
                       : $"FAIL {(double.IsFinite(miss) ? $"{miss:F0} m" : "no aim")} from {what}, "
                         + $"past {BarMetres:F0} m";

        _phase = Phase.Lingering;
        return null;
    }

    // Whether a store sent somewhere it cannot reach still walked as far as the ring promised.
    //
    // That is the only claim the ring makes out here, and it is the one worth flying:
    // TailKitReach.SettlingMargin is measured headlessly against a sphere with no terrain, and this
    // is the single piece of evidence that it stays a floor in the real game. Under-delivering is
    // the failure -- a ring that promises a walk the kit cannot fly is the one way this instrument
    // is worse than having none. Over-delivering is the design.
    private string WalkedFarEnough(double3 landed)
    {
        if (!_haveSentImpact) return "FAIL the unsteered landing at the send was not recorded";

        double walked = Vec.Len(landed - _sentImpactAnchor);
        double claimed = _sentReachMetres;

        if (!(claimed > 0.0)) return "FAIL the ring claimed no reach to be judged against";

        string how = $"walked {walked:F0} m of the {claimed:F0} m the ring claimed "
                     + $"({walked / claimed:F2}x), {Vec.Len(landed - _againAnchor):F0} m short of "
                     + "where it was sent";

        return walked >= claimed
                   ? $"PASS the reach held as a floor: {how}"
                   : $"FAIL the ring over-promised: {how}";
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
