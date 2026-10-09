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

    // What the rails decision saw BETWEEN probes, rather than at the instant one is taken.
    //
    // A probe is one reading every 10 simulated seconds and PhysicsBubble remakes the decision
    // every sub-step, so an actuator commanded briefly is invisible to a sampled reading. That gap
    // is not hypothetical: 3bh has a divergent world where every off-rails probe of BOTH arms read
    // "neither actuator flag", and quieting still moved the off-rails share from 68% to 38% -- a
    // difference with no visible cause, which is exactly what under-sampling looks like.
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

    // The 168 seconds between cutoff and the first release, which is the one window in a flight that
    // nothing instruments. Shot 006 of 2026-09-02-1508 put all eight rockets 75-99 km out, and the
    // whole divergence happened in here: healthy through cutoff, healthy for 65 s of coast, then the
    // predicted impact walks off at 252-342 m/s of simulated time in a straight line. The phase line
    // fires on change and the warhead trace starts at release, so the ramp was only ever visible as
    // a DEBUG stream nobody reads.
    //
    // Rate as well as position, because the discriminator is what KIND of ramp it is: a constant
    // drift is the bus's own state moving, a growing one is the prediction diverging from a state
    // that is not.
    private void ProbeTheCoast(double simStep)
    {
        // After the salvo the bus is no longer being guided, but it is still in the world and still
        // in whatever bubble the world has made -- and 3cn's merge seed happens exactly there. A bus
        // that goes silent from release to re-entry leaves that window unobservable, which is why
        // the seed is bracketed to ten seconds and unattributed. This is the light version: no
        // prediction, because there is nothing left to predict, just what frame it is being carried
        // in and who is deciding that.
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

        // Zero above ~209 km, measured over 6,181 samples with no exception -- so this column is a
        // guard rather than a reading. Every failure path in KsaWorld.MediumDensityRatioAt returns
        // sea-level air, which here would bend the predicted arc down and land it short; nothing
        // else in the flight would say so. docs/ACCURACY-PLAN.md 3an.
        double density = DensityRatioAt(positionCci);

        // And where the impact is walking to, not just how far. A miss that grows is one number; a
        // miss that grows because the impact is marching along the track is a different fault from
        // one that grows because it is sliding across it.
        string lands = "";

        try
        {
            double3 cce = hit.GroundFixedPointCci.Transform(parent.GetCci2Cce());
            lands = $", lands {parent.GetLatitudeFromCce(cce):F3},{parent.GetLongitudeFromCce(cce):F3}";

            // The miss is an angle between two points and either of them can be what moves. The
            // aim is built from a latitude and longitude the operator set and never changes, so
            // reading it back through the same frames has one right answer -- and a readback that
            // drifts is the conversion rather than the trajectory. Every computer in the world
            // shares those frames, which is the only thing so far that would explain eight
            // predictions moving in one frame. docs/ACCURACY-PLAN.md item 17.
            double3 aimCce = _trueAimCci.Transform(parent.GetCci2Cce());
            double aimLat = parent.GetLatitudeFromCce(aimCce);
            double aimLon = parent.GetLongitudeFromCce(aimCce);

            lands += $", aim reads {aimLat:F3},{aimLon:F3}"
                     + $" (set {Target.LatitudeDeg:F3},{Target.LongitudeDeg:F3})";
        }
        catch
        {
            // A frame the engine will not convert says nothing about the flight; the rest of the
            // line is still worth having.
        }

        // What the coast is doing that gravity does not account for. A ballistic coast is an exact
        // function of one state, so propagating the previous probe's state forward under gravity
        // alone and differencing gives the non-gravitational part directly -- and that is the only
        // way to see it: the walk is about half a metre a second and is invisible in a printed
        // speed. Log it as a VECTOR in a radial/along/cross basis -- a magnitude alone read as
        // along-track once and the mechanism is cross-track. docs/ACCURACY-PLAN.md 3as.
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

                // The basis the walk is actually levered through. Cross-track is the one that
                // moves an impact without moving the conic -- a normal impulse does no work and
                // leaves |h| alone -- so a push that shows up here and not in the energy is the
                // signature to look for.
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

        // Whether the state being read belongs to this frame. A clock that parts from the engine's
        // is world-level by construction, and no other reading in this mod would see it.
        double stateEpoch = KsaWorld.StateEpochSeconds(Craft);
        double clockGap = double.IsFinite(stateEpoch) ? KsaWorld.SimClockSeconds - stateEpoch : double.NaN;

        // Whether anything is perturbing the bus while it waits, and how long the correction has
        // gone without a reading. A coast is an exact function of one state, so a predicted impact
        // that walks means that state is moving, and a perturbation far too small to see as a
        // speed is tens of kilometres of impact -- cross-track, where the lever is the orbit
        // radius over the angular momentum rather than this arc's 0.4-2.9 km per m/s along it. The rationing is the other half: one reading taken 975 s after the last is a
        // full-size correction nothing has verified. docs/ACCURACY-PLAN.md item 17.
        // On rails the engine propagates this vehicle as an exact conic and the step cannot matter;
        // off rails it integrates, and the truncation scales with a step the nominal warp figure
        // does not show. A prediction that starts walking on a coast is either that transition or
        // the governor behind it, and neither is visible anywhere else. docs/ACCURACY-PLAN.md 17.
        bool? rails = KsaWorld.OnRails(Craft);

        // Which actuator term is holding it, when one is. Absent on a vehicle that is on rails and
        // absent on one held off by something neither flag can see -- and that second absence is
        // the informative one, because it is what says to go looking past the actuators.
        string? actuator = rails is false ? KsaWorld.OffRailsActuator(Craft) : null;

        // The bubble's frame, which decides whether going off rails is recoverable at all: only a
        // Cci origin lets TryToPutOnRails put a coasting vehicle back. Printed with the origin's
        // altitude and the leader's, because the frame is chosen by the heaviest member and a spent
        // stage left low is what holds a whole bubble in Ccf.
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
                          // Three sections, not two: a component that lands on negative zero takes
                          // the sign from the value and the body from the POSITIVE section, which
                          // prints "-+0.0000" and breaks anything reading the column back.
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

    // What the world is doing to a bus nobody is flying any more. Bounded by the bus's own life, and
    // silent unless the membership or the frame actually changes -- a line every ten seconds for
    // every spent bus in the world would drown the log it is meant to make readable.
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
