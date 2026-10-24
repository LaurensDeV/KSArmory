using Brutal.Numerics;
using KSA;

namespace KSArmory;

internal sealed partial class IcbmComputer
{
    private double3 _coastProbePosCci = double3.Zero;
    private double3 _coastProbeVelCci = double3.Zero;
    private bool _coastProbeHasState;

    // What is close enough to hold a bubble open. A bubble merges on proximity and releases only on
    // a parent or frame change, so the thing to name is whatever stays near -- and a lone rocket
    // shares one with its own spent stage, so it is not simply another rocket.
    private string NearestSaid()
    {
        Vehicle? near = KsaWorld.NearestVehicle(Craft, out double metres);

        return near is null || !double.IsFinite(metres)
                   ? ", nearest none"
                   : $", nearest {KsaWorld.DisplayName(near)} at {metres / 1000.0:F2} km";
    }

    // A flight-plan margin as a reader wants it: a number, or "inf" for a horizon nothing reaches.
    private static string Fmt(double seconds) =>
        double.IsPositiveInfinity(seconds) ? "inf"
        : double.IsNaN(seconds) ? "?"
        : seconds.ToString("F1");

    // How often the coast is written down, in simulated seconds.
    private const double CoastProbeSeconds = 10.0;

    private double _sinceCoastProbe;
    private double _coastProbeMiss = double.NaN;

    // The rails decision counted every frame between probes: PhysicsBubble remakes it every
    // sub-step, so a briefly commanded actuator is invisible to one reading in ten seconds
    // (docs/ACCURACY-PLAN.md 3bh).
    private int _railFrames;
    private int _offRailsFrames;
    private int _commandedFrames;
    private int _activeFrames;

    private void SampleRails()
    {
        if (KsaWorld.OnRails(Craft) is not { } rails) return;

        _railFrames++;
        if (rails) return;

        _offRailsFrames++;

        switch (KsaWorld.OffRailsActuator(Craft))
        {
            case "commanded": _commandedFrames++; break;
            case "active": _activeFrames++; break;
            case "commanded+active": _commandedFrames++; _activeFrames++; break;
        }
    }

    private string RailsSaid()
    {
        if (_railFrames == 0) return "";

        string said = $", frames off rails {100.0 * _offRailsFrames / _railFrames:F0}%"
                      + $" (commanded {100.0 * _commandedFrames / _railFrames:F0}%"
                      + $", active {100.0 * _activeFrames / _railFrames:F0}%"
                      + $", of {_railFrames})";

        _railFrames = _offRailsFrames = _commandedFrames = _activeFrames = 0;
        return said;
    }

    // The coast from cutoff to the first release, which nothing else instruments. The rate tells
    // the ramps apart: a constant drift is the bus's state moving, a growing one is the prediction
    // diverging from a state that is not.
    private void ProbeTheCoast(double simStep)
    {
        // After the salvo, only the bubble the spent bus is carried in: a bubble merge can seed
        // there (docs/ACCURACY-PLAN.md 3cn), and there is nothing left to predict.
        if (_salvoAway)
        {
            _coastProbeMiss = double.NaN;
            _coastProbeHasState = false;
            WatchTheBubble(simStep);
            return;
        }

        if (Program.Phase != IcbmPhase.Coast)
        {
            _sinceCoastProbe = 0.0;
            _coastProbeMiss = double.NaN;
            _coastProbeHasState = false;
            return;
        }

        // Every frame of the coast, so the probe can report the interval rather than the instant.
        SampleRails();

        _sinceCoastProbe += simStep;
        if (_sinceCoastProbe < CoastProbeSeconds) return;

        double interval = _sinceCoastProbe;
        _sinceCoastProbe = 0.0;

        if (PredictedImpact is not { } hit) return;

        double miss = PredictedMissMetres;
        double rate = double.IsFinite(_coastProbeMiss) && interval > 0.0
                          ? (miss - _coastProbeMiss) / interval
                          : double.NaN;
        _coastProbeMiss = miss;

        if (Parent is not { } parent) return;

        doubleQuat cce2Cci = parent.GetCce2Cci();
        double3 positionCci = (KsaWorld.PositionEcl(Craft) - parent.GetPositionEcl()).Transform(cce2Cci);
        double3 velocityCci = (KsaWorld.VelocityEcl(Craft) - parent.GetVelocityEcl()).Transform(cce2Cci);

        // Zero above ~209 km over 6,181 samples, so a guard rather than a reading: a failed air
        // read here would land the prediction short and nothing else would say so (ACCURACY-PLAN 3an).
        double density = DensityRatioAt(positionCci);

        // Where the impact walks, not only how far: along-track and cross-track are different faults.
        string lands = "";

        try
        {
            double3 cce = hit.GroundFixedPointCci.Transform(parent.GetCci2Cce());
            lands = $", lands {parent.GetLatitudeFromCce(cce):F3},{parent.GetLongitudeFromCce(cce):F3}";

            // The aim read back through the same frames: it never changes, so a drifting readback
            // is the conversion rather than the trajectory (docs/ACCURACY-PLAN.md, item 17).
            double3 aimCce = _trueAimCci.Transform(parent.GetCci2Cce());
            double aimLat = parent.GetLatitudeFromCce(aimCce);
            double aimLon = parent.GetLongitudeFromCce(aimCce);

            lands += $", aim reads {aimLat:F3},{aimLon:F3}"
                     + $" (set {Target.LatitudeDeg:F3},{Target.LongitudeDeg:F3})";
        }
        catch
        {
        }

        // The non-gravitational push: the last probe's state coasted under gravity alone and
        // differenced, as a radial/along/cross vector, since the push is cross-track (ACCURACY-PLAN 3as).
        double pushMps = double.NaN;
        double pushRadial = 0.0, pushAlong = 0.0, pushCross = 0.0;

        if (_coastProbeHasState
            && Kepler.TryCoast(KsaWorld.BodyMu(parent), _coastProbePosCci, _coastProbeVelCci, interval,
                               out _, out double3 coastedVelCci))
        {
            double3 slip = velocityCci - coastedVelCci;
            if (Vec.IsFinite(slip))
            {
                pushMps = Vec.Len(slip);

                // A cross-track impulse does no work and leaves |h| alone, so it moves the impact
                // without showing in the energy.
                double3 radial = Vec.Unit(positionCci);
                double3 cross = Vec.Unit(Vec.Cross(positionCci, velocityCci));
                double3 along = Vec.Cross(cross, radial);

                pushRadial = Vec.Dot(slip, radial);
                pushAlong = Vec.Dot(slip, along);
                pushCross = Vec.Dot(slip, cross);
            }
        }

        _coastProbePosCci = positionCci;
        _coastProbeVelCci = velocityCci;
        _coastProbeHasState = true;

        // Whether the state being read belongs to this frame; no other reading would see a clock
        // that parts from the engine's.
        double stateEpoch = KsaWorld.StateEpochSeconds(Craft);
        double clockGap = double.IsFinite(stateEpoch) ? KsaWorld.SimClockSeconds - stateEpoch : double.NaN;

        // On rails the engine propagates an exact conic; off rails it integrates, with a truncation
        // the warp figure does not show (docs/ACCURACY-PLAN.md, item 17).
        bool? rails = KsaWorld.OnRails(Craft);

        // Absent off rails means something neither actuator flag can see is holding it.
        string? actuator = rails is false ? KsaWorld.OffRailsActuator(Craft) : null;

        // Only a Cci origin lets TryToPutOnRails put a coasting vehicle back, and the heaviest
        // member picks the frame, so a spent stage left low holds the whole bubble in Ccf.
        string frame = KsaWorld.BubbleFrameOf(Craft) is var (name, originAlt)
            ? $", {name} origin at {originAlt / 1000.0:F0} km"
              + $", leader {KsaWorld.BubbleLeaderName(Craft)}"
              + $" at {KsaWorld.BubbleLeaderAltitudeMetres(Craft) / 1000.0:F0} km"
            : "";

        string loop = $", {(rails is null ? "rails unknown" : rails.Value ? "on rails" : "off rails")}"
                      + (KsaWorld.ForcedOffRails ? " (forced)" : "")
                      + (actuator is null ? rails is false ? " (neither actuator flag)" : "" : $" ({actuator})")
                      + frame
                      + RailsSaid()
                      + $", trim {(TrimIsFiring ? "firing" : _trim.Done ? "done" : "idle")}"
                      + $", {_sinceObserve:F0} s since the aim last read"
                      + (_measureDue ? ", reading due" : "");

        Log.Info($"coast probe on {KsaWorld.DisplayName(Craft)}: "
                 + $"{AltitudeMetres / 1000.0:F1} km, {Vec.Len(velocityCci):F1} m/s, "
                 + $"r_dot {Vec.Dot(velocityCci, Vec.Unit(positionCci)):+0.0;-0.0;0.0} m/s, "
                 + $"density {density:E2}, "
                 + $"predicted miss {miss / 1000.0:F2} km"
                 + (double.IsFinite(rate) ? $" moving {rate:+0.0;-0.0;0.0} m/s" : "")
                 + $", arrives in {hit.Seconds:F0} s, "
                 + $"committed {Program.CommittedArrivalFromNow:F0} s"
                 + lands
                 + $", state {clockGap:+0.000;-0.000;0.000} s behind"
                 + (double.IsFinite(pushMps)
                        ? $", off-gravity {pushMps:F4} m/s"
                          // Three sections: with two, negative zero prints "-+0.0000".
                          + $" (r {pushRadial:+0.0000;-0.0000;0.0000}"
                          + $", a {pushAlong:+0.0000;-0.0000;0.0000}"
                          + $", c {pushCross:+0.0000;-0.0000;0.0000})"
                        : "")
                 + $", plan {Fmt(KsaWorld.FlightPlanMarginSeconds(Craft))} s"
                 + $", bubble {KsaWorld.BubbleVehicleCount(Craft)}"
                 + NearestSaid()
                 + loop
                 + $", release in {IcbmProgram.Clock(SecondsToReleaseApproach)}");
    }

    // A spent bus's bubble, logged only when its membership or frame changes.
    private int _watchedBubble = -1;
    private string _watchedFrame = "";

    private void WatchTheBubble(double simStep)
    {
        _sinceCoastProbe += simStep;
        if (_sinceCoastProbe < CoastProbeSeconds) return;
        _sinceCoastProbe = 0.0;

        if (!KsaWorld.IsAlive(Craft)) return;

        int members = KsaWorld.BubbleVehicleCount(Craft);
        string frame = KsaWorld.BubbleFrameOf(Craft) is { } b ? b.Frame : "?";

        if (members == _watchedBubble && frame == _watchedFrame) return;

        _watchedBubble = members;
        _watchedFrame = frame;

        Log.Info($"spent bus {KsaWorld.DisplayName(Craft)}: bubble {members}, {frame} origin"
                 + $", leader {KsaWorld.BubbleLeaderName(Craft)}"
                 + $" at {KsaWorld.BubbleLeaderAltitudeMetres(Craft) / 1000.0:F0} km"
                 + $", {(KsaWorld.OnRails(Craft) is false ? "off rails" : "on rails")}");
    }
}
