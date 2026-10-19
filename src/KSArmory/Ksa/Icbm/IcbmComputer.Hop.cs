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

        // Against the bus in the same frame: the round's own ecliptic position is a step of ~30 km/s from the craft's.
        double released = _sentTo.Count > 0 ? Vec.Len(_sentTo[^1].Round.OffsetFromPlatform) : double.NaN;

        HopBurn.Stage was = _hopBurn.Now;
        HopBurn.Decision d = _hopBurn.Update(simStep, toGain, nose, EngineAcceleration, released);

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
