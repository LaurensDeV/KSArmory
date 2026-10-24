using Brutal.Numerics;
using KSA;

namespace KSArmory;

internal sealed partial class IcbmComputer
{
    // Cast past IManualFire on purpose: widening that role for a diagnostic would put rounds in
    // front of everything else that takes it.
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
            // Said, because a stranded trace otherwise reads as a flight that was never traced.
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

        // Latched: `_warhead` goes null with the launcher, and null here strands the trace, since
        // Finish is reachable only from Update.
        if ((_warhead ?? _tracedWarhead) is not { } warhead) return null;

        return new WarheadTrace.Setup(parent, Body, warhead, _trueAimCci, PredictStepSeconds,
                                      _terrainRadius ??= TerrainRadiusAt,
                                      _densityRatio ??= DensityRatioAt,
                                      KsaWorld.DisplayName(Craft),
                                      Config.PredictionStopsOnTheSurface,
                                      Config.PredictionStopsOnTheTerrain);
    }
}
