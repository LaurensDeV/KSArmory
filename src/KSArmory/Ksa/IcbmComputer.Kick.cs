using Brutal.Numerics;
using KSA;

namespace KSArmory;

internal sealed partial class IcbmComputer
{
    // What the prediction says about the state the warhead is actually leaving on, beside where the
    // round then lands. The phase line cannot answer this: it is printed before the frame's Predict
    // and while the engines are still lit, so it carries a prediction of the *solved* cutoff arc.
    // Only these two numbers isolate what the prediction and the round still disagree about, which
    // is the difference every remaining metre of the miss lives in.
    //
    // At INFO, and it earns it: this fires once per warhead released rather than per frame, and a
    // diagnostic nobody has switched on is one that is never there in the salvo that needed it.
    // The arrival frame's three components, in the order that says what to look at next: up is a
    // height-reference error, downrange is energy or timing, cross is the plane or the clock.
    // docs/FRAMES-AND-EPOCHS.md, "Measure vectors, not magnitudes".
    private static string Said(double3 parts)
        => $" ({parts.X:+0.000;-0.000;0.000} up, {parts.Y:+0.000;-0.000;0.000} downrange,"
           + $" {parts.Z:+0.000;-0.000;0.000} cross)";

    // What the bus's rotation threw this warhead with, and where that and its tube's ring put it on the
    // ground beside the probe's own impact. Printed whether or not anything is kicked, so a night
    // flying no kick can regress each landing on it: slope one if the term is real. Every vector is
    // resolved in the probe's arrival frame, the one its landing line is. ACCURACY-PLAN.md 3db.
    private void SayTheSpin(IManualFire weapon, Slug released, ReleaseProbe? probe)
    {
        string who = KsaWorld.DisplayName(Craft);
        string what = RoundLabel.For(released.Tube);

        try
        {
            if (probe is not { } from || Parent is not { } parent)
            {
                Log.Info($"spin at separation on {who}: {what} not measured -- no release probe");
                return;
            }

            int tube = released.Tube - 1;

            if (!weapon.TryTubeOffsetFromMeanEcl(tube, out double3 offsetEcl)
                || !weapon.TryTubeSpinEcl(tube, out double3 spinEcl, out double3 armEcl, out double3 angularEcl)
                || !ArrivalFrame.TryAt(from.Impact.PointCci, from.Impact.VelocityCci, out ArrivalFrame frame))
            {
                Log.Info($"spin at separation on {who}: {what} not measured -- its tube would not resolve");
                return;
            }

            doubleQuat cce2Cci = parent.GetCce2Cci();
            double3 offset = offsetEcl.Transform(cce2Cci);
            double3 aboutCentre = spinEcl.Transform(cce2Cci);
            double3 thrown = released.SpinVelocityEcl.Transform(cce2Cci);
            double3 ring = Vec.Cross(angularEcl, offsetEcl).Transform(cce2Cci);

            if (!ReleaseFocus.TryLandingShift(Body, from.PositionCci, from.VelocityCci, from.Impact.Seconds,
                                              Vec.Zero, thrown, out double3 thrownLands)
                || !ReleaseFocus.TryLandingShift(Body, from.PositionCci, from.VelocityCci, from.Impact.Seconds,
                                                 Vec.Zero, aboutCentre, out double3 centreLands)
                || !ReleaseFocus.TryLandingShift(Body, from.PositionCci, from.VelocityCci, from.Impact.Seconds,
                                                 offset, Vec.Zero, out double3 ringLands))
            {
                Log.Info($"spin at separation on {who}: {what} not measured -- no landing solves on this arc");
                return;
            }

            // Common is the spin less the ring's share, which is the same whichever arm it was
            // thrown on: the ring is the mouth from the tubes' mean, and that pairs with nothing.
            Log.Info($"spin at separation on {who}: {what} from tube {released.Tube}, "
                     + $"{Vec.Len(armEcl):F3} m from the centre of mass, turning at "
                     + $"{Vec.Len(angularEcl) * 1000.0:F3} mrad/s -- common "
                     + $"{PerSecond(frame.Resolve(thrown - ring))} mm/s thrown, "
                     + $"{PerSecond(frame.Resolve(aboutCentre - ring))} about the centre of mass; ring "
                     + $"{PerSecond(frame.Resolve(ring))} mm/s; lands "
                     + $"{OnGround(frame.Resolve(thrownLands))} m from the spin thrown, "
                     + $"{OnGround(frame.Resolve(centreLands))} m from the spin about the centre of mass, "
                     + $"{OnGround(frame.Resolve(ringLands))} m from the ring");
        }
        catch (Exception e)
        {
            Log.Warn($"spin at separation on {who}: {what} not measured -- {e.Message}");
        }
    }

    private static string PerSecond(double3 parts)
        => $"({parts.X * 1000.0:+0.000;-0.000;0.000} up, {parts.Y * 1000.0:+0.000;-0.000;0.000} downrange, "
           + $"{parts.Z * 1000.0:+0.000;-0.000;0.000} cross)";

    private static string OnGround(double3 parts)
        => $"({parts.Y:+0.000;-0.000;0.000} downrange, {parts.Z:+0.000;-0.000;0.000} cross)";

    // Said on its own line and nowhere else, so every line after it keeps the form a night's scripts read.
    private ReleaseProbe? ChooseProbe(ReleaseProbe? own, Slug? released)
    {
        int tube = released?.Tube ?? 0;
        SalvoProbe.Choice chosen = _salvoProbe.Choose(own, tube);
        string what = RoundLabel.For(tube);

        if (chosen.Source == SalvoProbe.Source.Borrowed)
        {
            Log.Info($"release probe: {what} borrows {RoundLabel.For(chosen.FromTube)}'s, "
                     + $"{chosen.AgeSeconds * 1000.0:F0} ms old, for its separation -- its own found no impact");
        }
        else if (chosen.Source == SalvoProbe.Source.TooOld)
        {
            Log.Info($"release probe: {what} has none to borrow -- {RoundLabel.For(chosen.FromTube)}'s is "
                     + $"{chosen.AgeSeconds * 1000.0:F0} ms old, past the "
                     + $"{SalvoProbe.MaxAgeSeconds * 1000.0:F0} ms a separation may lean on");
        }

        return chosen.Probe;
    }

    // The separation velocity this tube's round leaves with, solved from the probe's own state and
    // flight time so nothing is flown twice: its ring focused on the probe's impact, the spin it was
    // thrown with given back, the probe's own miss cancelled, or any of them. docs/ACCURACY-PLAN.md
    // items 41 and 42.
    private void KickAtSeparation(IManualFire weapon, Slug released, ReleaseProbe? probe)
    {
        string who = KsaWorld.DisplayName(Craft);
        string what = RoundLabel.For(released.Tube);

        try
        {
            if (probe is not { } from || Parent is not { } parent)
            {
                Log.Info($"focus on {who}: {what} not kicked -- no release probe to solve against");
                return;
            }

            doubleQuat cce2Cci = parent.GetCce2Cci();
            double3 offsetCci = Vec.Zero;
            bool focusRing = Config.FocusTubesOnTheAim;
            bool beyondTheRing = Config.CancelSpinAtSeparation || Config.CancelProbeMissAtSeparation;

            // An offset that will not resolve costs the ring, never the other two: all three are independent.
            if (focusRing)
            {
                if (weapon.TryTubeOffsetFromMeanEcl(released.Tube - 1, out double3 offsetEcl))
                {
                    offsetCci = offsetEcl.Transform(cce2Cci);
                }
                else
                {
                    focusRing = false;
                    Log.Info(beyondTheRing
                                 ? $"focus on {who}: {what}'s ring not focused -- its tube's offset would not resolve"
                                 : $"focus on {who}: {what} not kicked -- its tube's offset would not resolve");

                    if (!beyondTheRing) return;
                }
            }

            // Each warhead to its own point on a ring, or all six to the designation at a footprint
            // of zero. The spin axis is exactly +Z in a body's own Cci.
            double3 aimedAt = WarheadFootprint.AimFor(from.TargetCci, new double3(0, 0, 1),
                                                      Config.WarheadFootprintMetres,
                                                      released.Tube - 1, weapon.TubeCount);

            if (Config.WarheadFootprintMetres > 0.0
                && !WarheadFootprint.WithinTheCap(Config.WarheadFootprintMetres, from.Impact.Seconds))
            {
                Log.Info($"focus on {who}: {what}'s {Config.WarheadFootprintMetres:F1} m footprint is "
                         + $"past what the cap can throw on a {from.Impact.Seconds:F0} s flight -- "
                         + $"{WarheadFootprint.WidestAt(from.Impact.Seconds):F2} m is the widest");
            }

            ReleaseFocus.ProbeMiss? miss = Config.CancelProbeMissAtSeparation
                ? new ReleaseFocus.ProbeMiss(from.Impact.GroundFixedPointCci, aimedAt,
                                             Config.ProbeMissFollowsTheGround ? TerrainRadiusAt : null)
                : null;

            ReleaseFocus.FlownSensitivity? throughTheAir =
                Config.KickThroughTheAir && (focusRing || miss is not null) ? KickColumnsThroughTheAir(from, who, what) : null;

            double missCap = MissKickCap;
            ReleaseFocus.Separation kick = ReleaseFocus.Kick(Body, from.PositionCci, from.VelocityCci,
                                                             from.Impact.Seconds, offsetCci,
                                                             released.SpinVelocityEcl.Transform(cce2Cci),
                                                             focusRing,
                                                             Config.CancelSpinAtSeparation,
                                                             miss, throughTheAir, missCap);

            bool missGiven = kick.Miss == ReleaseFocus.MissOutcome.Cancelled;
            bool anything = kick.RingFocused || kick.SpinCancelled || missGiven;

            if (focusRing && !kick.RingFocused)
            {
                Log.Info(anything
                             ? $"focus on {who}: {what}'s ring not focused -- no kick solves on this arc"
                             : $"focus on {who}: {what} not kicked -- no kick solves on this arc");
            }

            string missSaid = miss is null ? "" : ProbeMissSaid(from);

            if (kick.Miss == ReleaseFocus.MissOutcome.Unsolved)
            {
                Log.Info($"focus on {who}: {what}'s release probe miss{missSaid} not cancelled -- "
                         + "no kick solves on this arc");
            }
            else if (kick.Miss == ReleaseFocus.MissOutcome.OverTheCap)
            {
                Log.Info($"focus on {who}: {what}'s release probe miss{missSaid} not cancelled -- its "
                         + $"{Vec.Len(kick.MissKickCci) * 1000.0:F3} mm/s kick is over the "
                         + $"{missCap * 1000.0:F1} mm/s cap");
            }

            if (!anything) return;

            if (!released.TryAddSeparationVelocity(kick.KickCci.Transform(parent.GetCci2Cce())))
            {
                Log.Info($"focus on {who}: {what} not kicked -- it has already flown a step");
                return;
            }

            if (kick.RingFocused)
            {
                // The angle is what says it is a solve: -offset/T would read 180.
                double angle = Vec.AngleBetween(kick.RingKickCci, offsetCci) * 180.0 / Math.PI;

                Log.Info($"focus on {who}: tube {released.Tube} sits {Vec.Len(offsetCci):F3} m off the "
                         + $"tubes' mean, so {what} is kicked {Vec.Len(kick.RingKickCci) * 1000.0:F3} mm/s, "
                         + $"{angle:F1} deg from that offset, for a {from.Impact.Seconds:F0} s flight");
            }

            if (kick.SpinCancelled)
            {
                Log.Info($"focus on {who}: {what} is given back the "
                         + $"{Vec.Len(released.SpinVelocityEcl) * 1000.0:F3} mm/s of spin it was thrown with");
            }

            if (missGiven)
            {
                Log.Info($"focus on {who}: {what} is kicked {Vec.Len(kick.MissKickCci) * 1000.0:F3} mm/s to "
                         + $"cancel the release probe's miss{missSaid}");
            }
        }
        catch (Exception e)
        {
            Log.Warn($"focus on {who}: {what} not kicked -- {e.Message}");
        }
    }

    // The salvo's columns flown once and carried to each release, or re-flown when this release is too far along the
    // coast or over other ground for the ones held. Said on every warhead, so a flight can confirm the arm engaged
    // and read what it cost in the frame; null, said, solves that warhead in vacuum as the switch off would.
    // The bus slows in the air between one release and the next, so each later warhead leaves on a
    // slower bus: flown at 150-300 km, about 50 mm/s a frame and a group walking 28-104 m.
    private bool ReleasesTogether =>
        Config.ShortShotReleasesTogether && Program.ReleasesAtCutoff && !_walker.Walking;

    private double MissKickCap =>
        Config.ShortShotMissKickMetresPerSecond > 0.0
        && (Program.ReleasesAtCutoff || (Config.ShortShotKickCapAfterTheTrim && Program.IsShortShot))
            ? Config.ShortShotMissKickMetresPerSecond
            : Config.LongShotMissKickMetresPerSecond > 0.0 && !Program.IsShortShot
                ? Config.LongShotMissKickMetresPerSecond
                : ReleaseFocus.MaxMissKickMetresPerSecond;

    private ReleaseFocus.FlownSensitivity? KickColumnsThroughTheAir(in ReleaseProbe from, string who, string what)
    {
        if (_warhead is not { } warhead) return null;

        double groundRadius = Vec.Len(from.Impact.PointCci);
        double dragK = warhead.AppliedDragK;

        if (_flownKick is { } held && held.Covers(from.Impact.Seconds, groundRadius) && dragK == _flownKickDragK)
        {
            Log.Info($"focus on {who}: {what}'s kick solved through the air, on columns carried "
                     + $"{held.FlightSeconds - from.Impact.Seconds:F3} s along the coast");
            return held;
        }

        long started = System.Diagnostics.Stopwatch.GetTimestamp();

        _flownKick = ReleaseFocus.FlownSensitivity.TryFly(
            Body, from.PositionCci, from.VelocityCci,
            new ReleaseFocus.Air(new ImpactPredictor.Drag(DensityRatioAt, warhead), PredictStepSeconds, groundRadius,
                                 Config.PredictionStopsOnTheSurface,
                                 Config.PredictionStopsOnTheTerrain));
        _flownKickDragK = dragK;

        double ms = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        Log.Info(_flownKick is null
                     ? $"focus on {who}: {what}'s kick NOT solved through the air -- a column did not come down "
                       + $"({ms:F2} ms), so it is solved in vacuum"
                     : $"focus on {who}: {what}'s kick solved through the air, on columns flown for it in {ms:F2} ms "
                       + $"at k {dragK:E3}");

        return _flownKick;
    }

    // The miss the kick cancels, along the ground and carried to the arrival the frame's axes stand at.
    // The probe line resolves it uncarried, which turns it by the planet's spin over the flight: 1.4 deg
    // at 340 s, a couple of centimetres across for every metre downrange.
    private string ProbeMissSaid(in ReleaseProbe from)
    {
        if (!ArrivalFrame.TryAt(from.Impact.PointCci, from.Impact.VelocityCci, out ArrivalFrame frame)) return "";

        double3 atArrival = Body.CarryCci(from.Impact.GroundFixedPointCci - from.TargetCci, from.Impact.Seconds);
        return $" {OnGround(frame.Resolve(atArrival))} m{GroundRiseSaid(from)}";
    }

    // How steeply the ground climbs from the aim to the impact, read whichever way the miss is measured:
    // a miss taken square to up is cancelled wrong by that slope, so it is the per-flight check on the
    // cancellation. ReleaseFocus.TryMissOnTheGround has the geometry.
    private string GroundRiseSaid(in ReleaseProbe from)
    {
        if (!ReleaseFocus.TryMissOnTheGround(from.Impact.GroundFixedPointCci, from.TargetCci, TerrainRadiusAt,
                                             out double3 chord))
        {
            return " over ground whose rise could not be read";
        }

        double3 up = Vec.Unit(from.Impact.GroundFixedPointCci);
        double rise = Vec.Dot(chord, up);
        double run = Vec.Len(chord - up * rise);
        return run > 0.0 ? $" over ground rising {rise / run:+0.000;-0.000;0.000} from the aim to the impact" : "";
    }

    private ReleaseProbe? ProbeRelease()
    {
        if (Parent is not { } parent) return null;
        if (_warhead is not { } warhead) return null;

        try
        {
            doubleQuat cce2Cci = parent.GetCce2Cci();
            double3 positionCci = (KsaWorld.PositionEcl(Craft) - parent.GetPositionEcl()).Transform(cce2Cci)
                                  + ReleaseOffsetCci();
            double3 velocityCci = (KsaWorld.VelocityEcl(Craft) - parent.GetVelocityEcl()).Transform(cce2Cci)
                                  + ReleaseImpulseCci();

            if (!ImpactPredictor.TryPredict(Body, positionCci, velocityCci, PredictStepSeconds,
                                            ImpactPredictor.DefaultMaxSeconds,
                                            out ImpactPredictor.Impact hit, out ImpactPredictor.Ending ending,
                                            TerrainRadiusAt, null,
                                            new ImpactPredictor.Drag(DensityRatioAt, warhead),
                                            stopOnTheSurface: Config.PredictionStopsOnTheSurface,
                                       stopOnTheTerrain: Config.PredictionStopsOnTheTerrain))
            {
                // Every exit reads the same from outside, and one of them is a whole horizon flown for
                // nothing: ACCURACY-PLAN.md 3ep.
                Log.Info("release probe: no impact predicted from the release state -- "
                         + $"{ending.Said(Body.SurfaceRadius)}; released {Body.AltitudeOf(positionCci) / 1000.0:F3} km "
                         + $"up at {Vec.Len(velocityCci):F1} m/s, thrown {Vec.Len(ReleaseImpulseCci()):F3} m/s "
                         + $"from {Vec.Len(ReleaseOffsetCci()):F2} m off the orbit position");
                return null;
            }

            double3 cce = hit.GroundFixedPointCci.Transform(parent.GetCci2Cce());
            double miss = Body.SurfaceRadius * Vec.AngleBetween(hit.GroundFixedPointCci, _trueAimCci);

            // The angle this reports has to match the one the release line reports for the tube.
            // The prediction throws the warhead along the direction the vehicle was commanded to
            // hold; the round actually leaves along its tube. If those disagree, two metres a
            // second is being applied in the wrong direction, and radially that is 3.4 km per m/s.
            double3 impulse = ReleaseImpulseCci();
            string thrown = impulse.Equals(Vec.Zero)
                ? ""
                : $", {(_releaseMeasured ? "measured" : "assumed")} thrown "
                  + $"{Vec.AngleBetween(impulse, velocityCci) * 180.0 / Math.PI:F0} deg from the "
                  + $"platform's track, {Vec.Len(ReleaseOffsetCci()):F1} m off the orbit position";

            // The warheads' own ETA, latched here and never rewritten. This probe is flown from the
            // state a warhead actually left in, so it is the one honest arrival time there is --
            // and after this instant Predict is flying the *bus*, which coasts on to its own impact
            // about half a minute later. Letting that overwrite the readout makes a correct
            // countdown reach zero as the warheads land and then jump back to twenty seconds.
            _arrivalLeft = hit.Seconds;
            _salvoAway = true;

            // Resolved, not just measured. A magnitude cannot say whether the residual is one-signed,
            // and the whole question about the pre-release term is its SIGN: 301 of 375 flights at a
            // 32 deg arrival land short, which is a bias and removable, where scatter is neither.
            // Without this it has to be reconstructed by fitting each seat's aim point from its own
            // landings; this makes it a direct read. ACCURACY-PLAN.md 3co.
            // Carried like ProbeMissSaid's, and for the reason its comment gives: a ground-fixed
            // separation resolved against axes taken at the arrival is turned by the planet's spin
            // over the flight. ACCURACY-PLAN.md 3dw.
            string resolved = ArrivalFrame.TryAt(hit.PointCci, hit.VelocityCci, out ArrivalFrame frame)
                ? Said(frame.Resolve(Body.CarryCci(hit.GroundFixedPointCci - _trueAimCci, hit.Seconds)))
                : "";

            Log.Info($"release probe: predicted from the release state -> "
                      + $"{parent.GetLatitudeFromCce(cce):F3},{parent.GetLongitudeFromCce(cce):F3}, "
                      + $"{Distance.Measure(miss)} from the target{resolved}, "
                      + $"{hit.Seconds:F0} s of flight{thrown}");

            return new ReleaseProbe(positionCci, velocityCci, hit, _trueAimCci);
        }
        catch (Exception e)
        {
            // A probe that throws inside the frame hook is worse than one that says nothing.
            Log.Warn($"release probe: not flown -- {e.Message}");
            return null;
        }
    }
}
