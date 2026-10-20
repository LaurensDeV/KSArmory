using Brutal.Numerics;
using KSA;

namespace KSArmory;

// A hop flown on the bus's own engine under HopsBurnTheEngine: Sim/HopBurn.cs decides, this actuates.
internal sealed partial class IcbmComputer
{
    private readonly HopBurn _hopBurn = new();
    private bool _engineLit;
    private double _hopMassBefore = double.NaN;
    private double _hopAskedBefore = double.NaN;

    private bool HopsBurnTheEngine
        => Config.HopsBurnTheEngine && Config.HopsSizeTheirOwnSlots
           && _release is { } release && release.Profile.HopEngineThrustNewtons > 0.0;

    // What the engine pushes the bus at now, or zero where it has none.
    private double EngineAcceleration
        => HopsBurnTheEngine && _release is { } release && KsaWorld.IsAlive(Craft) && KsaWorld.MassKg(Craft) > 0.0
               ? release.Profile.HopEngineThrustNewtons / KsaWorld.MassKg(Craft)
               : 0.0;

    // At a hand-over, once the arc to the next stop has been asked for.
    private void BeginTheHop()
    {
        if (!HopsBurnTheEngine) return;

        _hopBurn.Begin(!Program.CoastLineCci.Equals(Vec.Zero) ? Program.CoastLineCci : _entryLineCci);
        _hopMassBefore = double.NaN;
        _hopAskedBefore = double.NaN;
    }

    private void ResetTheHop()
    {
        _hopBurn.Reset();
        if (_engineLit) LightTheEngine(false);
    }

    private void DriveHop(double simStep, in IcbmState state)
    {
        if (_hopBurn.Now is HopBurn.Stage.Idle or HopBurn.Stage.Done) return;

        double3 toGain = Vec.Zero;
        if (Program.Arc is { } arc
            && Kepler.TryCoast(state.Body.Mu, Program.ReferencePositionCci, arc.RequiredVelocityCci,
                               Program.SecondsSinceReference, out _, out double3 required))
        {
            toGain = required - state.VelocityCci;
        }

        double3 nose = Vec.Zero;
        if (Parent is { } parent) KsaWorld.TryControlFrameCci(Craft, parent, out nose, out _, out _);

        HopBurn.Stage was = _hopBurn.Now;
        bool stackInTheWay = was == HopBurn.Stage.Pending && TryTheStackPath(toGain)
                             && Config.EngineHopsClearTheStack && _stackClosestMetres < _stackKeepOutMetres;
        HopBurn.Decision d = _hopBurn.Update(simStep, toGain, nose, EngineAcceleration, ReleasedMetres, stackInTheWay);

        if (was == HopBurn.Stage.Pending && _hopBurn.Now != HopBurn.Stage.Pending && _hopBurn.Now != HopBurn.Stage.Done)
        {
            Log.Info($"hop on {KsaWorld.DisplayName(Craft)}: spent stack {_stackApartMetres:F0} m away, the burn passes it "
                     + $"at {_stackClosestMetres:F0} m against a keep-out of {_stackKeepOutMetres:F0} m"
                     + (_hopBurn.HeldForTheStackSeconds > 0.0 ? $", held {_hopBurn.HeldForTheStackSeconds:F0} s for it" : ""));
        }

        if (was == HopBurn.Stage.Pending) _hopAskedBefore = _hopBurn.AskedMetresPerSecond;
        if (d.EngineOn && !_engineLit) _hopMassBefore = Mass();
        if (!d.LineCci.Equals(Vec.Zero)) Program.CoastLineCci = d.LineCci;
        if (d.EngineOn != _engineLit) LightTheEngine(d.EngineOn);
        if (d.ResolveNow) ResolveTheHop();

        if (_hopBurn.Now != HopBurn.Stage.Done) return;

        if (_hopBurn.Burnt) ResolveTheHop();
        _trim.Resume();
        _spentAtHopRound = _trim.SpentMetresPerSecond;

        Log.Info($"hop on {KsaWorld.DisplayName(Craft)}: "
                 + (_hopBurn.Burnt
                        ? $"{_hopAskedBefore:F1} m/s asked, the engine burnt {_hopBurn.BurnSeconds:F1} s and "
                          + $"{_hopMassBefore - Mass():F1} kg, {_hopBurn.Said}, turned onto it and back in "
                          + $"{_hopBurn.TurnedSeconds:F1} s"
                        : _hopBurn.Said));
    }

    private int _watchedRelease;
    private double _watchedSeconds, _leftAtMetres, _closestMetres, _closestAtSeconds;
    private double3 _velocityAtRelease;
    private string _closestSaid = "";

    // How near the last warhead released came to its bus in the minute after it left, and what the bus was doing then:
    // under where it started, the bus flew back through it.
    private void WatchTheReleased(double simStep)
    {
        if (_sentTo.Count > _watchedRelease)
        {
            if (_watchedRelease > 0) SayTheClosest();
            _watchedRelease = _sentTo.Count;
            _watchedSeconds = 0.0;
            _leftAtMetres = ReleasedMetres;
            _closestMetres = double.PositiveInfinity;
            _closestAtSeconds = 0.0;
            _closestSaid = "";
            _velocityAtRelease = KsaWorld.IsAlive(Craft) ? KsaWorld.VelocityEcl(Craft) : Vec.Zero;
            return;
        }

        if (_watchedRelease == 0 || !double.IsFinite(_watchedSeconds)) return;

        _watchedSeconds += simStep;
        double released = ReleasedMetres;
        if (double.IsFinite(released) && released < _closestMetres)
        {
            _closestMetres = released;
            _closestAtSeconds = _watchedSeconds;
            _closestSaid = WhatTheBusIsDoing();
        }

        if (_watchedSeconds >= 60.0) SayTheClosest();
    }

    private string WhatTheBusIsDoing()
    {
        if (_sentTo.Count == 0 || !KsaWorld.IsAlive(Craft) || Parent is not { } parent) return "";

        double3 offset = _sentTo[^1].Round.OffsetFromPlatform;
        double3 gained = KsaWorld.VelocityEcl(Craft) - _velocityAtRelease;
        double toward = Vec.Len2(offset) > 0.0 ? Vec.Dot(gained, Vec.Unit(offset)) : double.NaN;

        string axes = "";
        if (KsaWorld.TryControlFrameCci(Craft, parent, out double3 nose, out double3 right, out double3 down))
        {
            double3 cci = offset.Transform(parent.GetCce2Cci());
            axes = $", at ({Vec.Dot(cci, nose):+0.0;-0.0} nose, {Vec.Dot(cci, right):+0.0;-0.0} right, "
                   + $"{Vec.Dot(cci, down):+0.0;-0.0} down) m";
        }

        return $"{axes}, the bus {toward:+0.00;-0.00} m/s toward it since the release, hop {_hopBurn.Now}, "
               + $"trim {_trim.Firing}{(Turning ? ", turning" : "")}, rails {KsaWorld.OnRails(Craft)?.ToString() ?? "?"}";
    }

    private void SayTheClosest()
    {
        if (!double.IsFinite(_watchedSeconds)) return;

        Log.Info($"released warhead {_watchedRelease} on {KsaWorld.DisplayName(Craft)}: left {_leftAtMetres:F1} m from "
                 + $"the bus, closest {_closestMetres:F1} m at {_closestAtSeconds:F1} s of the {_watchedSeconds:F0} after"
                 + _closestSaid);
        _watchedSeconds = double.NaN;
    }

    private double _stackApartMetres = double.NaN, _stackClosestMetres = double.NaN, _stackKeepOutMetres = double.NaN;

    // Where a burn started now would take the bus against its spent stack, in the stack's frame, both from one instant.
    private bool TryTheStackPath(double3 toGainCci)
    {
        _stackApartMetres = _stackClosestMetres = _stackKeepOutMetres = double.NaN;
        if (_separatedFrom is not { } stack || !KsaWorld.IsAlive(stack) || Parent is not { } parent) return false;

        doubleQuat cce2Cci = parent.GetCce2Cci();
        double3 fromStack = (KsaWorld.PositionEcl(Craft) - KsaWorld.PositionEcl(stack)).Transform(cce2Cci);
        double3 relative = (KsaWorld.VelocityEcl(Craft) - KsaWorld.VelocityEcl(stack)).Transform(cce2Cci);

        double radius;
        try { radius = stack.MeanRadius; }
        catch { radius = double.NaN; }

        _stackApartMetres = Vec.Len(fromStack);
        _stackKeepOutMetres = ProximityWatch.KeepOutFor(radius);
        _stackClosestMetres = HopBurn.ClosestToTheStackMetres(fromStack, relative, toGainCci, EngineAcceleration,
                                                               Vec.Len(toGainCci));
        return double.IsFinite(_stackClosestMetres);
    }

    // Against the bus in the same frame: the round's own ecliptic position is a step of ~30 km/s from the craft's.
    private double ReleasedMetres => _sentTo.Count > 0 ? Vec.Len(_sentTo[^1].Round.OffsetFromPlatform) : double.NaN;

    // A hop on the nozzles pushes the bus back along the line its warhead just left on, and passes through it.
    private bool HopWaitsForItsWarhead
        => Config.HopsWaitForTheirWarhead && _trim.ToGainMetresPerSecond > HopBurn.LeaveForTheNozzlesMetresPerSecond
           && ReleasedMetres < HopBurn.ClearMetres;

    private void ResolveTheHop()
    {
        if (Config.HopsFreeTheirArrival) Program.FreeTheHopArrival();
        else Program.CorrectCoastArc();
    }

    // Active as the part's own box makes it, so the engine burns whatever its stage; the bus is the craft's only engine
    // once it has let go of the stack, and a hop only ever follows a release.
    private void LightTheEngine(bool on)
    {
        _engineLit = on;
        if (on) VehicleCommand.SetEnginesActive(Craft, true);
        VehicleCommand.SetEngine(Craft, running: on);
        if (!on) VehicleCommand.SetEnginesActive(Craft, false);
    }
}
