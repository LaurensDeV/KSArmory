using Brutal.Numerics;
using KSA;

namespace KSArmory;

internal sealed partial class IcbmComputer
{
    // One warhead per designation, and it is the first away. A salvo leaves inside a tenth of a
    // second and lands in a group tens of metres wide, so any of the six answers the question and
    // six traces answer it six times.
    //
    // The round is reached by casting past IManualFire on purpose: a launcher owes a ballistic
    // computer the ability to shoot and nothing else, and widening that role for a diagnostic would
    // put rounds in front of everything else that takes it.
    private void BeginTrace(IManualFire weapon)
    {
        if (!_traceWanted) return;
        if (TraceSetup() is not { } setup) return;
        if (weapon is not IRoundsInFlight inFlight) return;

        // The round just fired is the one just appended - nothing runs between FireAt and here.
        if (inFlight.Rounds is not { Count: > 0 } rounds) return;

        _tracedWarhead = setup.Warhead;

        WarheadTrace trace = new();
        trace.Begin(rounds[^1], setup);
        _traces.Add(trace);
    }

    // Sample() is what normally re-derives the aim, and it cannot run without the craft. Only the
    // aim has to be re-derived at all: a place on a turning planet moves through Cci every frame,
    // where Parent, Body and the warhead profile are latched and do not.
    private void StepTraceLoose(double simStep)
    {
        if (!_traceWanted || !_traces.Any(t => t.Watching)) return;
        if (Parent is not { } parent) return;

        if (Target.IsSet && Target.BodyName == parent.Id)
        {
            _trueAimCci = (SurfacePointEcl(parent, Target.LatitudeDeg, Target.LongitudeDeg)
                           - parent.GetPositionEcl()).Transform(parent.GetCce2Cci());
        }

        StepTrace(simStep);
    }

    private void StepTrace(double simStep)
    {
        if (!_traceWanted)
        {
            foreach (WarheadTrace trace in _traces) trace.Forget();
            _traces.Clear();
            return;
        }

        if (!_traces.Any(t => t.Watching)) return;

        if (TraceSetup() is not { } setup)
        {
            // Said once rather than returning quietly. A stranded trace is indistinguishable in the
            // log from a flight that was never traced, and reads as a sampling choice rather than
            // a fault.
            if (!_saidTraceStranded)
            {
                _saidTraceStranded = true;
                Log.Warn($"warhead trace on {KsaWorld.DisplayName(Craft)} stranded: "
                         + (Parent is null ? "no parent body" : "no warhead profile")
                         + " -- the round is still flying and nothing is watching it");
            }

            return;
        }

        // Backwards, because a trace that has finished is dropped here and each holds a round the
        // roster will let go of; keeping them costs the flight's worth of samples again on reload.
        for (int i = _traces.Count - 1; i >= 0; i--)
        {
            _traces[i].Update(simStep, setup);
            if (!_traces[i].Watching) _traces.RemoveAt(i);
        }
    }

    private WarheadTrace.Setup? TraceSetup()
    {
        if (Parent is not { } parent) return null;

        // Latched, because a trace follows one round that has already left and the profile of a
        // round in the air cannot change. `_warhead` is re-read from the launcher every frame, so
        // it goes null the moment the launcher does -- and TraceSetup returning null there does not
        // end the trace, it *strands* it: Finish is only reachable from Update, so the round lands
        // with nothing watching and the flight is simply absent from the log. Measured on
        // 2026-09-08: all eight traces began, four finished.
        if ((_warhead ?? _tracedWarhead) is not { } warhead) return null;

        return new WarheadTrace.Setup(parent, Body, warhead, _trueAimCci, PredictStepSeconds,
                                      _terrainRadius ??= TerrainRadiusAt,
                                      _densityRatio ??= DensityRatioAt,
                                      KsaWorld.DisplayName(Craft),
                                      Config.PredictionStopsOnTheSurface,
                                      Config.PredictionStopsOnTheTerrain);
    }
}
