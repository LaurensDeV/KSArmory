using System.Diagnostics;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Sweeps each gun mount's travel against the craft it is on, and hands the turret the map
/// (<see cref="TravelMap"/>). Off the frame's critical path by construction: a fixed number of
/// rays a frame across every mount, the craft on the panel first, and a craft is swept only once it
/// has kept its shape for <see cref="SettleFrames"/> frames, so a break-up is swept once rather than
/// on every frame it loses a part. Until then a mount keeps its profile's limit, or its last map.
///
/// <para>Other mounts are left out of the geometry, base and all: their barrels move, and wherever
/// they happened to point during the sweep would stay blocked for good.</para>
/// </summary>
internal sealed class TravelSweeps
{
    public const int RaysPerFrame = 50;

    /// <summary>
    /// And no more time than this a frame, since a ray against a big craft costs more than one
    /// against a small one (ms).
    /// </summary>
    public const double MsPerFrame = 1.0;
    public const int SettleFrames = 60;

    private sealed class State
    {
        public Vehicle? Craft;
        public Part? Launcher;
        public int PartCount;
        public int StableFrames;
        public TravelSweep? Sweep;
        public bool Mapped;
        public int Rays;
        public double Ms;
        public double WorstMs;
        public int Frames;
    }

    private readonly Dictionary<WeaponSystem, State> _states = [];
    private readonly List<WeaponSystems.Entry> _order = [];
    private readonly HashSet<WeaponSystem> _present = [];
    private readonly List<WeaponSystem> _gone = [];
    private readonly Stopwatch _clock = new();
    private readonly Stopwatch _frame = new();

    public void Step(IEnumerable<WeaponSystems.Entry> all, Vehicle? focused)
    {
        _order.Clear();
        foreach (WeaponSystems.Entry e in all) if (Sweeps(e.Weapon.Profile)) _order.Add(e);
        _order.Sort((a, b) => (b.Craft == focused).CompareTo(a.Craft == focused));

        _present.Clear();
        foreach (WeaponSystems.Entry e in _order) _present.Add(e.Weapon);
        _gone.Clear();
        foreach (WeaponSystem w in _states.Keys) if (!_present.Contains(w)) _gone.Add(w);
        foreach (WeaponSystem w in _gone) _states.Remove(w);

        _frame.Restart();
        int budget = RaysPerFrame;
        foreach (WeaponSystems.Entry e in _order)
        {
            if (budget <= 0 || _frame.Elapsed.TotalMilliseconds >= MsPerFrame) break;
            budget -= Advance(e.Weapon, budget);
        }
    }

    private static bool Sweeps(LauncherProfile profile) => profile.Trains && profile.HasCannon;

    // Rays spent on this mount this frame.
    private int Advance(WeaponSystem system, int budget)
    {
        if (system.Platform is not { } craft || system.Launcher is not { } launcher) return 0;
        if (!_states.TryGetValue(system, out State? state)) _states[system] = state = new State();

        int parts = craft.Parts?.Parts.Length ?? 0;
        if (state.Craft != craft || state.Launcher != launcher || state.PartCount != parts)
        {
            // Changed shape: keep the old map until the new one is swept.
            (state.Craft, state.Launcher, state.PartCount) = (craft, launcher, parts);
            (state.StableFrames, state.Sweep, state.Mapped, state.Rays, state.Ms) = (0, null, false, 0, 0.0);
            (state.WorstMs, state.Frames) = (0.0, 0);
            return 0;
        }

        if (state.Mapped) return 0;
        if (state.StableFrames < SettleFrames)
        {
            state.StableFrames++;
            return 0;
        }

        LauncherProfile profile = system.Profile;
        state.Sweep ??= TravelSweep.For(profile);

        _clock.Restart();
        int spent = 0;
        double minE = double.DegreesToRadians(profile.MinElevationDeg);
        double maxE = double.DegreesToRadians(profile.MaxElevationDeg);
        var rows = new TravelMap(new bool[state.Sweep.Bearings, state.Sweep.Elevations], minE, maxE, 0.0);

        while (spent < budget && _frame.Elapsed.TotalMilliseconds < MsPerFrame
               && state.Sweep.TryNext(out int i, out int j))
        {
            bool clear = true;
            foreach ((double3 start, double3 end) in TravelSweep.Probes(profile, i * rows.BearingStepRad, rows.ElevationOfRow(j)))
            {
                spent++;
                if (Blocked(craft, launcher, start, end)) { clear = false; break; }
            }
            state.Sweep.Record(i, j, clear);
        }
        double ms = _clock.Elapsed.TotalMilliseconds;
        state.Ms += ms;
        state.WorstMs = Math.Max(state.WorstMs, ms);
        state.Frames++;
        state.Rays += spent;

        if (state.Sweep.Done)
        {
            state.Mapped = true;
            TravelMap map = state.Sweep.Result(minE, maxE, profile.RestElevationRad);
            system.Turret.Map = map;
            string arc = map.Arc is (double lo, double hi)
                ? $"{double.RadiansToDegrees(lo):F0} to {double.RadiansToDegrees(hi):+0;-0} deg"
                : "all the way round";
            Log.Info($"travel map: {profile.DisplayName} on {craft.Id} traverses {arc}; "
                     + $"{state.Sweep.Tested} of {state.Sweep.Bearings * state.Sweep.Elevations} poses tested, "
                     + $"{state.Rays} rays in {state.Ms:F2} ms over {state.Frames} frames, worst {state.WorstMs:F2}");
        }

        return spent;
    }

    // Whether a segment in the launcher's frame meets any part of the craft but the launcher and the
    // other mounts. Everything in one frame the engine built, so no epoch enters it.
    private static bool Blocked(Vehicle craft, Part launcher, double3 startPart, double3 endPart)
    {
        try
        {
            if (craft.Parts is not { } tree) return false;

            double4x4 asmb2Ego = craft.GetMatrixAsmb2Ego(double3.Zero);
            doubleQuat rotate = craft.Asmb2Ego;
            double3 start = rotate * (launcher.PositionVehicleAsmb + launcher.Asmb2VehicleAsmb * startPart - craft.CenterOfMassAsmb);
            double3 end = rotate * (launcher.PositionVehicleAsmb + launcher.Asmb2VehicleAsmb * endPart - craft.CenterOfMassAsmb);

            double length = Vec.Len(end - start);
            if (!(length > 0.0)) return false;
            Ray ray = new() { Origin = start, Direction = (end - start) / length };

            foreach (Part part in tree.Parts)
            {
                if (part == launcher || IsMount(part)) continue;
                if (!part.RayCastEgo(in asmb2Ego, ray, out double near, out double far,
                                     out _, out _, out _, out _, out _, out _))
                {
                    continue;
                }
                if ((near >= 0.0 && near <= length) || (near < 0.0 && far >= 0.0)) return true;
            }
            return false;
        }
        catch
        {
            // A craft the engine will not answer for blocks nothing, which is the profile's limit.
            return false;
        }
    }

    private static bool IsMount(Part part)
        => Catalogue.LauncherForPart(part.Id) is { Trains: true } || Catalogue.OpticForPart(part.Id) is not null;
}
