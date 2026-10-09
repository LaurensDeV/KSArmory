using System.Runtime.InteropServices;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

internal sealed partial class IcbmComputer
{
    private bool _separated;
    private readonly List<ShedCandidate> _shedCandidates = [];

    // Whether the next sequence would fire the joint the launcher hangs on -- that one, not any
    // later. A launcher that can separate at all is not a reason to refuse every stage: a
    // multi-stage stack carrying a bus has a decoupler under it from the moment it is built, and
    // treating that as "the next stage drops my rounds" strands it with a dead first stage.
    private static bool StagingWouldDropTheLauncher(IManualFire? weapon)
        => weapon is { NextStageSeparatesIt: true };

    // Once, and only where the part tree offers a joint to let go of. Nothing shipped declares one,
    // so this does nothing until a craft is built with a decoupler under its launcher.
    private void SeparateOnce(IManualFire? weapon)
    {
        if (_separated || weapon is null) return;

        if (!weapon.CanSeparate)
        {
            _separated = true;
            return;
        }

        // Latched before the call, not after: the split lands a frame later and the module does not
        // de-duplicate, so a second request queues a second decouple.
        _separated = true;

        if (weapon.Separate())
        {
            _awaitingSplit = true;
            _didSplit = true;
            _wasBeforeSplit.Clear();
            KsaWorld.CollectVehicles(_wasBeforeSplit);

            Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)} separating the launcher "
                     + "from the stack before deploying");
        }
    }

    private void StepStagingProbe(double simStep)
    {
        if (!KsaWorld.IsAlive(Craft)) return;

        bool inWindow = double.IsFinite(_sinceStaging);
        if (inWindow) _sinceStaging += simStep;

        if (_partWatch.TryLost(Craft, _lostParts))
        {
            string when = inWindow && _sinceStaging <= StagingWindowSeconds
                              ? $"{_sinceStaging:F2} s after a staging"
                              : "outside any staging";

            Log.Info($"{KsaWorld.DisplayName(Craft)} lost {_lostParts.Count} part(s) {when}, turning "
                     + $"{Diagnostics.SpinDegPerSec(Craft):F1} deg/s: {string.Join(", ", _lostParts)}");
        }

        if (!inWindow) return;

        if (_stagingProbe < StagingProbeAt.Length && _sinceStaging >= StagingProbeAt[_stagingProbe])
        {
            Log.Info($"staging probe on {KsaWorld.DisplayName(Craft)}: +{_sinceStaging:F2} s -- "
                     + $"{Diagnostics.DescribeEngines(Craft)}, turning "
                     + $"{Diagnostics.SpinDegPerSec(Craft):F1} deg/s");

            // Once, at a second: by then a stage that lit has thrust, and a dropped stage has gone.
            if (StagingProbeAt[_stagingProbe] == 1.0 && Diagnostics.EngineCount(Craft) == 0)
            {
                Log.Info($"staging left no engine on {KsaWorld.DisplayName(Craft)}");
            }

            _stagingProbe++;
        }

        if (_sinceStaging > StagingWindowSeconds) _sinceStaging = double.NaN;
    }

    // What came off at the last staging, by the same difference WhatWasDropped uses. Run one frame
    // late because the stage is deferred through the engine's input buffer -- a census taken on the
    // frame the command was issued sees the world before it.
    private void CollectShedStages()
    {
        if (!_awaitingStage) return;

        _awaitingStage = false;

        _afterStage.Clear();
        KsaWorld.CollectVehicles(_afterStage);

        for (int i = 0; i < _afterStage.Count; i++)
        {
            Vehicle other = _afterStage[i];

            if (ReferenceEquals(other, Craft)
                || _wasBeforeStage.Contains(other)
                || _shed.Contains(other))
            {
                continue;
            }

            _shed.Add(other);
        }

        _wasBeforeStage.Clear();
    }

    // Spent stages cost frame time for the whole coast while they fall, and frame time is the only
    // thing that buys simulation rate. Off unless asked for: it destroys things in the player's
    // world. Sim/StageDisposal.cs holds the rules, including that the half the clearance is still
    // reading is never taken.
    private void DisposeShedStages()
    {
        for (int i = _shed.Count - 1; i >= 0; i--)
        {
            Vehicle stage = _shed[i];

            if (!KsaWorld.IsAlive(stage))
            {
                _shed.RemoveAt(i);
                continue;
            }

            // Never the craft being flown, whatever the census decided: destroying it clears
            // ControlledVehicle and strands the player in a scene that carries on without them.
            if (ReferenceEquals(stage, Craft)
                || ReferenceEquals(stage, KsaWorld.ControlledVehicle))
            {
                continue;
            }

            double3 between = KsaWorld.PositionEcl(stage) - KsaWorld.PositionEcl(Craft);
            double apart = Vec.IsFinite(between) ? Vec.Len(between) : double.NaN;

            if (!StageDisposal.MayDispose(_session.DisposeSpentStages,
                                          ReferenceEquals(stage, _separatedFrom), apart))
            {
                continue;
            }

            Log.Info($"{KsaWorld.DisplayName(Craft)}: taking the spent stage "
                     + $"{KsaWorld.DisplayName(stage)} out of the world at {apart / 1000.0:F1} km, "
                     + "so it stops costing frame time while it falls");

            KsaWorld.WaitForVehicleSolvers();
            KsaWorld.Remove(stage);
            _shed.RemoveAt(i);
        }
    }

    // The half of the stack this vehicle let go of, found by difference: the decoupler makes a
    // vehicle that did not exist a frame ago, and everything else in the world did.
    //
    // Rehome captures it too, but only when the computer follows its weapon onto the *other* half.
    // The ordinary case is the bus keeping both the launcher and the computer, where Rehome never
    // runs -- so without this the distance is unreadable on every flight and SeparationClearance
    // falls back to a blind clock, which is the trim being authorised while the stack is still
    // metres away.
    private Vehicle? WhatWasDropped()
    {
        _afterSplit.Clear();
        KsaWorld.CollectVehicles(_afterSplit);

        _shedCandidates.Clear();

        for (int i = 0; i < _afterSplit.Count; i++)
        {
            Vehicle other = _afterSplit[i];
            if (ReferenceEquals(other, Craft) || _wasBeforeSplit.Contains(other)) continue;

            double3 between = KsaWorld.PositionEcl(other) - KsaWorld.PositionEcl(Craft);
            if (!Vec.IsFinite(between)) continue;

            _shedCandidates.Add(new ShedCandidate(i, Vec.Len(between)));
        }

        ShedChoice choice = ShedStage.Choose(CollectionsMarshal.AsSpan(_shedCandidates));

        if (choice.Verdict != ShedVerdict.Take)
        {
            Log.Info($"split on {KsaWorld.DisplayName(Craft)}: no stack adopted -- {choice.Why}");
        }

        _wasBeforeSplit.Clear();
        return choice.Verdict == ShedVerdict.Take ? _afterSplit[choice.Index] : null;
    }

    // The world half of SeparationClearance: how far apart the two actually are. Both positions
    // come from the same instant, so the ecliptic motion they each carry cancels in the difference
    // - see docs/FRAMES-AND-EPOCHS.md. NaN rather than zero when the stack cannot be read, because
    // the two mean opposite things there.
    private Clearance Clear(double simStep)
    {
        _sinceSplit += simStep;

        double apart = double.NaN;
        double radius = double.NaN;

        if (_separatedFrom is { } stack && KsaWorld.IsAlive(stack))
        {
            double3 between = KsaWorld.PositionEcl(stack) - KsaWorld.PositionEcl(Craft);
            if (Vec.IsFinite(between)) apart = Vec.Len(between);

            // The stage's own bounding sphere, which is what the coarse contact test a released
            // store would be scored against actually uses.
            try { radius = stack.MeanRadius; }
            catch { radius = double.NaN; }
        }

        if (!_saidClearOnce)
        {
            _saidClearOnce = true;
            Log.Info($"clearance on {KsaWorld.DisplayName(Craft)}: "
                     + $"stack {(_separatedFrom is null ? "null" : KsaWorld.DisplayName(_separatedFrom))}, "
                     + $"alive {KsaWorld.IsAlive(_separatedFrom)}, "
                     + $"apart {apart:F1} m, radius {radius:F1} m");
        }

        // Measured off the same pair of samples the gate is about to decide on, so the two cannot
        // report different distances about one frame.
        _proximity.Update(simStep, apart, radius);

        // What the trim's interlock is asked, recorded here because this is the one place the
        // separation is measured -- a second derivation could report a different distance about the
        // same frame. In Cci, because that is the frame the trim's own axes are in.
        _keepOutTowardCci = double3.Zero;

        if (double.IsFinite(apart) && apart < ProximityWatch.KeepOutFor(radius)
            && _separatedFrom is { } near && KsaWorld.IsAlive(near) && Parent is { } parent)
        {
            double3 towardEcl = KsaWorld.PositionEcl(near) - KsaWorld.PositionEcl(Craft);

            if (Vec.IsFinite(towardEcl) && !towardEcl.Equals(double3.Zero))
            {
                // A difference of two Ecl positions is already Cce, so this is the same one-rotation
                // conversion every other Cci quantity in this class takes.
                _keepOutTowardCci = Vec.Unit(towardEcl).Transform(parent.GetCce2Cci());
            }
        }

        // An unreadable stack falls back to the clock rather than to "clear": a part tree
        // mid-rebuild reads as no distance at all, and treating that as clearance is exactly the
        // case this exists to prevent -- and it is asked fresh every pass, never remembered.
        return SeparationClearance.Check(apart, radius, _sinceSplit, ForTheTrimMetres(radius));
    }

    // Zero once the release is within a pass of the trim: the wait must leave the trim time to fly.
    private double ForTheTrimMetres(double radius)
    {
        if (!Config.TrimWaitsOutTheStack || _separatedFrom is not { } stack || !KsaWorld.IsAlive(stack)
            || Parent is not { } parent || !Vec.IsFinite(_trim.ToGainCci))
        {
            return 0.0;
        }

        // The release goes when the post-boost passes finish, at the latest when their clock runs out, and never
        // before the release gate.
        double toRelease = Math.Max(_postBoost.SecondsLeft, double.IsFinite(SecondsToRelease) ? SecondsToRelease : 0.0);

        doubleQuat cce2Cci = parent.GetCce2Cci();
        double3 fromStack = (KsaWorld.PositionEcl(Craft) - KsaWorld.PositionEcl(stack)).Transform(cce2Cci);
        double3 relative = (KsaWorld.VelocityEcl(Craft) - KsaWorld.VelocityEcl(stack)).Transform(cce2Cci);

        return SeparationClearance.ForTheTrimMetres(ProximityWatch.KeepOutFor(radius), fromStack, relative,
                                                    _trim.ToGainCci, toRelease);
    }
}
