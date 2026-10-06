using System.Runtime.CompilerServices;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Tanks leaking through the holes shells have left in them — the liquid under each hole's level
/// running out at Torricelli's rate (<see cref="TankLeak"/>), taken off the tank, and a stream where
/// it goes (<see cref="LeakStream"/>), drawn by <see cref="CloudPass"/>.
///
/// <para><b>Measured ten times a second, written in the engine's own window.</b> The mass owed is
/// handed to the tank from <see cref="AttitudeHook"/>'s prefix on <c>Vehicle.PrepareWorker</c>, the
/// point between the worker's write-back and its next snapshot, following the engine's own
/// <c>DepleteConsumables</c>: take it off, mark the fill changed, and recompute the craft's mass,
/// which the engine does not do for itself.</para>
///
/// <para><b>A stream from every hole that leaks, up to the biggest few</b> in the world. Every other
/// hole still drains; it just draws nothing. Not particles: KSA's particle renderers draw nothing a
/// motion can stretch, so a stream of them reads as a string of beads.</para>
/// </summary>
internal static class Leaks
{
    /// <summary>Whether holed tanks leak, from the session's setting.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>The most leaking holes drawn with a stream at once, the biggest first.</summary>
    public const int MostStreams = 16;

    /// <summary>One stream to draw: the first <paramref name="Count"/> of its points, in Ecl, how thick it leaves, and a seed.</summary>
    public readonly record struct Stream(LeakStream.Point[] Points, int Count, double ExitRadius, float Seed);

    // One hole's stream, kept from frame to frame because its liquid is flown: a leak that stops lets
    // its tail fall, and is only forgotten once the last of it is gone.
    private sealed class JetState
    {
        public required LeakJet Jet;
        public required Vehicle Craft;
        public required float Seed;
        public Outlet Outlet;
        public bool Flowing;

        // How far behind the skin as drawn its stream starts, and how long since that was worked out.
        public double InsetMetres = double.NaN;
        public double InsetAge;
        public readonly LeakStream.Point[] Points = new LeakStream.Point[LeakStream.Points];
    }

    // How often the level and the flow are worked out. The level of a tank moves slowly, and a
    // shorter interval only buys more mass recomputes on the craft.
    private const double MeasureSeconds = 0.1;

    // One hole a tank is running out of, and how fast.
    private readonly record struct Outlet(BulletHoles.Hole Hole, double KgPerSecond, double JetSpeed);

    private sealed class Leak
    {
        public required Vehicle Craft;
        public double KgPerSecond;
        public int Leaking;
        public readonly List<Outlet> Outlets = [];
        public double ExpectedKg = double.NaN;
        public bool Told;
        public bool Floating;
    }

    // A tank's liquid as points, and how to carry a point of its part into the frame they are in: its own
    // shape and frame where the template can be read, the cylinder filling its part's box where not.
    private sealed class Form
    {
        public required double3[] Points;
        public required Func<double3, bool> Holds;
        public required string Described;
        public double3 Origin;
        public double3 AxisX = new(1, 0, 0), AxisY = new(0, 1, 0), AxisZ = new(0, 0, 1);

        public double3 FromPart(double3 p)
        {
            double3 d = p - Origin;
            return new double3(Vec.Dot(d, AxisX), Vec.Dot(d, AxisY), Vec.Dot(d, AxisZ));
        }

        public double3 DirectionFromPart(double3 v) => new(Vec.Dot(v, AxisX), Vec.Dot(v, AxisY), Vec.Dot(v, AxisZ));
    }

    private static readonly ConditionalWeakTable<Tank, Form> _forms = new();

    // What each tank's liquid has settled to, in its craft's assembly frame: the felt acceleration
    // followed with TankLeak.SettleSeconds of lag, so a thruster's pulse does not slosh it end to end.
    private static readonly ConditionalWeakTable<Tank, StrongBox<double3>> _settled = new();
    private static readonly Dictionary<Tank, List<BulletHoles.Hole>> _byTank = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Tank, Leak> _leaking = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Vehicle, Dictionary<Mole, float>> _owed = new(ReferenceEqualityComparer.Instance);
    private static readonly List<Stream> _streams = [];
    private static readonly Dictionary<BulletHoles.Hole, JetState> _jets = [];
    private static readonly List<BulletHoles.Hole> _spent = [];
    private static readonly Dictionary<Vehicle, List<(double3 Centre, double3 Push, double Radius, double Depth)>> _dents =
        new(ReferenceEqualityComparer.Instance);

    // Dents change only when something hits the craft, and reading them walks every dent on it, so a
    // stream's start is worked out when it appears and then about once a second -- for the streams
    // drawn, never for every hole leaking.
    private const double InsetSeconds = 1.0;
    private static int _jetsMade;
    private static readonly HashSet<Tank> _seen = new(ReferenceEqualityComparer.Instance);
    private static readonly List<Tank> _gone = [];
    private static readonly List<(Vehicle Craft, Outlet Outlet)> _biggest = [];
    private static readonly List<Outlet> _outlets = [];
    private static readonly double[] _scratch = new double[TankLeak.Samples];
    private static double _sinceMeasured;
    private static bool _warned;

    /// <summary>How many tanks are leaking now.</summary>
    public static int Count => _leaking.Count;

    /// <summary>
    /// Simulated seconds the leaks have run, which a stream's pattern moves on: it holds through a pause
    /// and slows in slow motion, as the liquid does. Wrapped, so a shader's float keeps its precision.
    /// </summary>
    public static double Clock { get; private set; }

    /// <summary>The streams to draw this frame, placed as the craft are drawn.</summary>
    public static IReadOnlyList<Stream> Streams => _streams;

    /// <summary>Works the leaks out every <see cref="MeasureSeconds"/> and places the streams every frame.</summary>
    public static void Update(double dt)
    {
        if (!Enabled || BulletHoles.Count == 0)
        {
            if (_leaking.Count > 0 || _streams.Count > 0) Clear();
            return;
        }

        if (double.IsFinite(dt) && dt > 0.0)
        {
            _sinceMeasured += dt;
            Clock = (Clock + dt) % 600.0;
        }
        if (_sinceMeasured >= MeasureSeconds)
        {
            Measure(_sinceMeasured);
            _sinceMeasured = 0.0;
        }

        PlaceStreams(double.IsFinite(dt) && dt > 0.0 ? dt : 0.0);
    }

    /// <summary>Stops every leak and stream, and forgets what was owed, for a world that no longer has them.</summary>
    public static void Clear()
    {
        _streams.Clear();
        _jets.Clear();
        _leaking.Clear();
        _owed.Clear();
        _sinceMeasured = 0.0;
    }

    /// <summary>
    /// Takes what a craft's tanks owe off them. Called from <see cref="AttitudeHook"/>'s prefix, the
    /// one window where tank state can be written without the worker copying over it.
    /// </summary>
    public static void Apply(Vehicle craft)
    {
        if (_owed.Count == 0 || !_owed.Remove(craft, out Dictionary<Mole, float>? owed)) return;

        PartTree parts = craft.Parts;
        var moles = parts.Moles;

        foreach ((Mole mole, float kg) in owed)
        {
            var held = moles.GetModuleAndAllMutableStatesForInitialization(mole);
            held.Module.ConsumeStored(ref held.State, kg);
        }

        parts.RecomputeTotalFillFraction();
        moles.GetMutableGlobalStateForInitialization().ValuesUpdated = true;
        parts.PerformanceSequences.SetDirty();
        Unsafe.AsRef(in craft.Props).RecomputeMassProperties(parts.SubstanceStores, parts.Moles.States);
        craft.FlightComputer.ReadUpdatedVehicleConfiguration(craft);
    }

    private static void Measure(double step)
    {
        foreach (List<BulletHoles.Hole> list in _byTank.Values) list.Clear();

        foreach (BulletHoles.Hole hole in BulletHoles.All)
        {
            if (!KsaWorld.TryTankOf(hole.SubPart, out Tank? tank)) continue;

            if (!_byTank.TryGetValue(tank, out List<BulletHoles.Hole>? list)) _byTank[tank] = list = [];
            list.Add(hole);
        }

        _seen.Clear();
        foreach ((Tank tank, List<BulletHoles.Hole> holes) in _byTank)
        {
            if (holes.Count == 0) continue;

            try
            {
                if (MeasureTank(tank, holes, step)) _seen.Add(tank);
            }
            catch (Exception e)
            {
                if (!_warned)
                {
                    _warned = true;
                    Log.Warn($"a leak could not be worked out ({e.GetType().Name}: {e.Message}); tanks will not leak");
                }
            }
        }

        _gone.Clear();
        foreach (Tank tank in _leaking.Keys)
        {
            if (!_seen.Contains(tank)) _gone.Add(tank);
        }

        foreach (Tank tank in _gone) Stop(tank);

        // Tanks no hole reaches any more drop out of the grouping too, so it does not grow for ever.
        _gone.Clear();
        foreach ((Tank tank, List<BulletHoles.Hole> holes) in _byTank)
        {
            if (holes.Count == 0) _gone.Add(tank);
        }

        foreach (Tank tank in _gone) _byTank.Remove(tank);
    }

    // One tank's level, flow and debt. False when nothing runs out of it.
    private static bool MeasureTank(Tank tank, List<BulletHoles.Hole> holes, double step)
    {
        if (!KsaWorld.TryCraftOf(tank.Parent, out Vehicle? craft)) return false;

        ReadOnlySpan<MoleState> states = craft.Parts.Moles.States;
        double mass = 0.0, volume = 0.0;
        foreach (Mole mole in tank.Moles)
        {
            if (mole.Liquid is not { } liquid || !(liquid.StorageDensity > 0f)) continue;

            double held = states[mole.StatesIdx].Mass;
            mass += held;
            volume += held / liquid.StorageDensity;
        }

        _leaking.TryGetValue(tank, out Leak? was);
        if (was is not null && double.IsFinite(was.ExpectedKg) && !was.Told)
        {
            // The first debt paid is the test of whether a write from the prefix holds at all.
            was.Told = true;
            Log.Info($"leak on {KsaWorld.DisplayName(craft)}: tank holds {mass:F1} kg against {was.ExpectedKg:F1} "
                     + $"expected after the first draw -- the write {(Math.Abs(mass - was.ExpectedKg) < 1.0 ? "held" : "did NOT hold")}");
        }

        if (!(mass > 0.0) || !(volume > 0.0) || !(tank.StorageVolume > 0f)) return false;

        Part frame = tank.Parent;
        if (!KsaWorld.TryPartToVehicleAsmb(frame, out double4x4 tankToVehicle)) return false;
        double4x4.Invert(tankToVehicle, out double4x4 vehicleToTank);

        Form form = _forms.GetValue(tank, static t => FormOf(t));
        double3[] points = form.Points;

        double3 raw = KsaWorld.FeltAccelerationAsmb(craft);
        StrongBox<double3> settling = _settled.GetValue(tank, _ => new StrongBox<double3>(raw));
        settling.Value = TankLeak.Settle(settling.Value, raw, step);
        double3 felt = settling.Value;
        double pull = Vec.Len(felt);
        double fill = Math.Min(volume / tank.StorageVolume, 1.0);
        bool floating = TankLeak.Weightless(pull);

        // Floating, the liquid has no surface and no hole is above or below it.
        double3 up = floating ? Vec.Zero
                              : Vec.Unit(form.DirectionFromPart(felt.Transform(vehicleToTank) - Vec.Zero.Transform(vehicleToTank)));
        double surface = floating ? 0.0 : TankLeak.SurfaceHeight(points, up, fill, _scratch);
        double density = mass / volume;

        if (was is null)
        {
            Log.Debug(() => $"leak geometry: fill {volume / tank.StorageVolume:P1} of {tank.StorageVolume:F1} m3, "
                            + $"density {mass / volume:F0} kg/m3, felt {pull:F2} m/s2 up {up} in the tank's frame "
                            + $"(felt {felt} in the craft's), taken as {form.Described}, surface at {surface:F2}");
        }

        double flow = 0.0;
        int leaking = 0;
        _outlets.Clear();
        foreach (BulletHoles.Hole hole in holes)
        {
            if (!KsaWorld.TryPartToVehicleAsmb(hole.SubPart, out double4x4 subToVehicle)) continue;

            double3 at = form.FromPart(hole.Local.Transform(subToVehicle).Transform(vehicleToTank));
            if (!form.Holds(at)) continue;

            double depth = surface - Vec.Dot(at, up);
            if (was is null) Log.Debug(() => $"leak geometry: hole at {at} in the tank's frame, {Vec.Dot(at, up):F2} up, {depth:F2} under the surface");
            double kgPerSecond = TankLeak.FlowKgPerSecond(depth, pull, density, hole.Core, fill);
            if (!(kgPerSecond > 0.0)) continue;

            flow += kgPerSecond;
            leaking++;
            _outlets.Add(new Outlet(hole, kgPerSecond, TankLeak.JetSpeed(depth, pull, density)));
        }

        if (!(flow > 0.0)) return false;

        double taken = Math.Min(flow * step, mass);
        Owe(craft, tank, states, mass, taken);

        if (was is null)
        {
            Log.Info($"tank on {KsaWorld.DisplayName(craft)} leaking {flow:F1} kg/s through {leaking} of its "
                     + $"{holes.Count} hole(s), {mass:F0} kg left");
            was = new Leak { Craft = craft };
            _leaking[tank] = was;
        }

        // A change worth reading -- a settled leak going to floating, a burn doubling the pull -- is said
        // once, not every pass.
        if (was.KgPerSecond > 0.0 && Math.Abs(flow - was.KgPerSecond) > 0.25 * was.KgPerSecond)
        {
            double before = was.KgPerSecond;
            Log.Debug(() => $"leak on {KsaWorld.DisplayName(craft)}: {before:F1} -> {flow:F1} kg/s through {leaking} "
                            + $"hole(s), felt {pull:F2} m/s2, liquid {(floating ? "floating" : "settled")}, "
                            + $"{fill:P0} full");
        }

        if (floating != was.Floating)
        {
            Log.Info($"liquid in a holed tank on {KsaWorld.DisplayName(craft)} {(floating ? "floating" : "settled")} "
                     + $"(felt {pull:F2} m/s2): leaking {flow:F1} kg/s through {leaking} hole(s), {fill:P0} full");
            was.Floating = floating;
        }

        was.Craft = craft;
        was.KgPerSecond = flow;
        was.Leaking = leaking;
        was.Outlets.Clear();
        was.Outlets.AddRange(_outlets);
        if (!was.Told) was.ExpectedKg = mass - taken;
        return true;
    }

    // How far behind the skin as drawn a stream starts, so it appears from the hole rather than over
    // it; the hull hides it until it is out.
    private const double StreamInsetMetres = 0.05;

    // How far behind the skin as drawn a hole's stream starts, off the craft's dents read once a pass.
    private static double Inset(Vehicle craft, BulletHoles.Hole hole)
    {
        if (!KsaWorld.TryPartToVehicleAsmb(hole.SubPart, out double4x4 subToVehicle)) return StreamInsetMetres;

        if (!_dents.TryGetValue(craft, out var dents)) _dents[craft] = dents = KsaWorld.DentsOn(craft);
        return DentedIn(dents, hole, subToVehicle) + StreamInsetMetres;
    }

    // How far KSA's dents have pushed the skin in at a hole, along the shell's path: each dent is the
    // part shader's (1 - t^2)^2 bump of its depth over its radius, summed. The hole is anchored to the
    // skin before it was dented, which the same burst's front then pushes in.
    private static double DentedIn(List<(double3 Centre, double3 Push, double Radius, double Depth)> dents,
                                   BulletHoles.Hole hole, double4x4 subToVehicle)
    {
        double3 atVehicle = hole.Local.Transform(subToVehicle);
        double3 inward = Vec.Unit(atVehicle - hole.CameFrom.Transform(subToVehicle));
        double3 pushed = Vec.Zero;
        foreach ((double3 centre, double3 push, double radius, double depth) in dents)
        {
            if (!(radius > 0.0) || !(depth > 0.0)) continue;

            double t2 = Vec.Len2(atVehicle - centre) / (radius * radius);
            if (t2 >= 1.0) continue;

            double f = (1.0 - t2) * (1.0 - t2);
            pushed += Vec.Unit(push) * (depth * f);
        }

        return Math.Max(Vec.Dot(pushed, inward), 0.0);
    }

    private static Form FormOf(Tank tank)
    {
        (double3 min, double3 max) = tank.Parent.BoundingBoxPartAsmb;

        // The template's shape, fitted into the part's box along the template's own axis: a template is
        // sized to its volume model, which on the stock 3 m tank is twice the mesh's radius, and the
        // holes are on the mesh.
        if (KsaWorld.TryTankShape(tank, out TankShape outside, out TankShape inside, out _,
                                  out double3 x, out double3 y, out double3 z))
        {
            double3 centre = (min + max) * 0.5;
            double lowX = double.MaxValue, highX = double.MinValue, lowY = double.MaxValue, highY = double.MinValue;
            double lowZ = double.MaxValue, highZ = double.MinValue;
            for (int corner = 0; corner < 8; corner++)
            {
                double3 c = new((corner & 1) == 0 ? min.X : max.X, (corner & 2) == 0 ? min.Y : max.Y,
                                (corner & 4) == 0 ? min.Z : max.Z);
                double3 d = c - centre;
                (lowX, highX) = (Math.Min(lowX, Vec.Dot(d, x)), Math.Max(highX, Vec.Dot(d, x)));
                (lowY, highY) = (Math.Min(lowY, Vec.Dot(d, y)), Math.Max(highY, Vec.Dot(d, y)));
                (lowZ, highZ) = (Math.Min(lowZ, Vec.Dot(d, z)), Math.Max(highZ, Vec.Dot(d, z)));
            }

            double radius = 0.5 * Math.Min(highY - lowY, highZ - lowZ);
            TankShape fitted = outside.FitTo(highX - lowX, radius);
            double wallShare = outside.Extent.Radius > 0.0 ? 1.0 - (inside.Extent.Radius / outside.Extent.Radius) : 0.0;
            TankShape holds = fitted.Inside(fitted.Extent.Radius * wallShare);
            (double low, double high, _) = fitted.Extent;

            return new Form
            {
                Points = TankLeak.FillPoints(holds),
                Holds = p => fitted.Contains(p, TankLeak.SkinShare),
                Described = $"its own shape fitted to its box, {fitted}",
                Origin = centre + (x * (0.5 * (lowX + highX) - (0.5 * (low + high)))),
                AxisX = x,
                AxisY = y,
                AxisZ = z,
            };
        }

        return new Form
        {
            Points = TankLeak.FillPoints(min, max),
            Holds = p => TankLeak.IsInTank(min, max, p),
            Described = $"the cylinder filling its box, {min} to {max}",
        };
    }

    // Shares a draw between the liquids in a tank by mass, onto what the craft already owes.
    private static void Owe(Vehicle craft, Tank tank, ReadOnlySpan<MoleState> states, double mass, double taken)
    {
        if (!_owed.TryGetValue(craft, out Dictionary<Mole, float>? owed))
        {
            _owed[craft] = owed = new Dictionary<Mole, float>(ReferenceEqualityComparer.Instance);
        }

        foreach (Mole mole in tank.Moles)
        {
            if (mole.Liquid is null) continue;

            double held = states[mole.StatesIdx].Mass;
            float more = (float)(taken * (held / mass));

            // Never more than the tank holds, however many passes a craft that is not being stepped
            // has missed.
            owed[mole] = (float)Math.Min(owed.GetValueOrDefault(mole) + more, held);
        }
    }

    private static void Stop(Tank tank)
    {
        if (_leaking.Remove(tank, out Leak? leak))
        {
            Log.Info($"tank on {KsaWorld.DisplayName(leak.Craft)} stopped leaking");
        }
    }

    // The biggest leaking holes' streams, flown on by a step and placed where the craft is drawn.
    private static void PlaceStreams(double dt)
    {
        _streams.Clear();

        _biggest.Clear();
        foreach (Leak leak in _leaking.Values)
        {
            foreach (Outlet outlet in leak.Outlets) _biggest.Add((leak.Craft, outlet));
        }

        _biggest.Sort((a, b) => b.Outlet.KgPerSecond.CompareTo(a.Outlet.KgPerSecond));

        foreach (JetState jet in _jets.Values) jet.Flowing = false;

        for (int i = 0; i < _biggest.Count && i < MostStreams; i++)
        {
            (Vehicle craft, Outlet outlet) = _biggest[i];
            if (!_jets.TryGetValue(outlet.Hole, out JetState? jet))
            {
                jet = new JetState { Jet = new LeakJet(), Craft = craft, Seed = (_jetsMade++ % 97) * 7.31f };
                _jets[outlet.Hole] = jet;
            }

            jet.Craft = craft;
            jet.Outlet = outlet;
            jet.Flowing = true;
        }

        _dents.Clear();
        _spent.Clear();
        foreach ((BulletHoles.Hole hole, JetState jet) in _jets)
        {
            try
            {
                if (!Fly(hole, jet, dt)) _spent.Add(hole);
            }
            catch
            {
                _spent.Add(hole);
            }
        }

        foreach (BulletHoles.Hole hole in _spent) _jets.Remove(hole);
    }

    // Moves one jet on and adds what it draws. False once it has nothing left to draw.
    private static bool Fly(BulletHoles.Hole hole, JetState jet, double dt)
    {
        if (!KsaWorld.TryDrawnCentreEcl(jet.Craft, out double3 centre)) return false;

        // Relative to the craft's centre, in axes that do not turn with it: there a free parcel's
        // acceleration is the pull the craft feels, reversed.
        double3 pull = -KsaWorld.VehicleAsmbDirectionToEcl(jet.Craft, KsaWorld.FeltAccelerationAsmb(jet.Craft));

        if (jet.Flowing)
        {
            jet.InsetAge += dt;
            if (!double.IsFinite(jet.InsetMetres) || jet.InsetAge >= InsetSeconds)
            {
                jet.InsetAge = 0.0;
                jet.InsetMetres = Inset(jet.Craft, hole);
            }
        }

        double3 exit = default, velocity = default;
        bool flowing = jet.Flowing
                       && KsaWorld.TryDrawnPartPointEcl(hole.SubPart, hole.Local, out double3 at)
                       && KsaWorld.TryDrawnPartPointEcl(hole.SubPart, hole.CameFrom, out double3 cameFrom)
                       && Outward(at, cameFrom, jet.Outlet.JetSpeed, jet.InsetMetres, centre, out exit, out velocity);

        jet.Jet.Step(dt, exit, velocity, Vec.IsFinite(pull) ? pull : Vec.Zero, flowing);
        if (!flowing && jet.Jet.Empty) return false;

        double exitRadius = LeakStream.ExitRadius(hole.Core);
        int count = jet.Jet.Draw(exit, exitRadius, flowing, jet.Points);
        for (int i = 0; i < count; i++) jet.Points[i] = jet.Points[i] with { Position = jet.Points[i].Position + centre };

        if (count >= 2) _streams.Add(new Stream(jet.Points, count, exitRadius, jet.Seed));
        return true;
    }

    // Where a stream leaves the hull, relative to the craft's centre, and how fast: out along the way
    // the shell came in, from the skin as the dents have left it.
    private static bool Outward(double3 at, double3 cameFrom, double jetSpeed, double inset, double3 centre,
                                out double3 exit, out double3 velocity)
    {
        double3 outward = Vec.Unit(cameFrom - at);
        exit = at - (outward * inset) - centre;
        velocity = outward * jetSpeed;
        return Vec.IsFinite(exit) && Vec.IsFinite(velocity);
    }
}
