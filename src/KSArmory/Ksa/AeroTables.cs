using System.Diagnostics;
using System.Numerics;
using BepuUtilities.Memory;
using Brutal.Numerics;
using KSA;
using KSA.Aero;

namespace KSArmory;

/// <summary>
/// Each tracked craft's <see cref="AeroTable"/>, sampled from the engine's own aerodynamics a few
/// directions a frame on a surface flow of the mod's own -- never the engine's, which the vehicle
/// worker is updating during this pass.
///
/// <para>A mesh is only read while it is the one published: KSA rebuilds into the same buffers and
/// unpublishes it first, from the main thread, before this runs.</para>
/// </summary>
internal static class AeroTables
{
    // 642 directions, about 8° apart: a coarser sphere misses the drop in drag along the nose.
    private const int Levels = 3;

    private const double ForgetAfterSeconds = 5.0;
    private const double RebuildAfterSeconds = 10.0;
    private const double BudgetMs = 1.0;

    // A rate over airspeed (rad/m) of the order a tumbling drone turns at: 0.7 rad/s at 200 m/s is 0.0035.
    private const float RatePerSpeed = 0.003f;

    private sealed class Entry : IDisposable
    {
        public required Vehicle Craft;
        public AeroMeshSource? Source;
        public BufferPool? Pool;
        public AeroSurfaceFlow? Flow;
        public AeroTable? Building;
        public AeroTable? Ready;
        public long AskedAt;
        public long ReadyAt;
        public LocalInclinationLaw Law;
        public SkinFrictionLaw Friction;
        public float Mach;
        public long StartedAt;
        public int Frames;
        public double SampleMs;

        public void Dispose()
        {
            Flow = null;
            Source = null;
            Pool?.Clear();
            Pool = null;
            Building = null;
        }
    }

    private static readonly Dictionary<Vehicle, Entry> Entries = new(ReferenceEqualityComparer.Instance);

    public static double LastStepMs { get; private set; }

    /// <summary>Works out the sphere's grid weights off the frame thread, once, before any table needs them.</summary>
    public static void Warm() => Task.Run(() => _ = new AeroTable(Levels));

    /// <summary>The craft's table if one is complete, and a request to keep one built for it.</summary>
    public static AeroTable? For(Vehicle craft)
    {
        if (!Entries.TryGetValue(craft, out Entry? entry))
        {
            entry = new Entry { Craft = craft };
            Entries[craft] = entry;
        }

        entry.AskedAt = Stopwatch.GetTimestamp();
        return entry.Ready;
    }

    /// <summary>Spends up to a millisecond filling tables, and lets go of craft nobody has asked about.</summary>
    public static void Step()
    {
        long start = Stopwatch.GetTimestamp();
        List<Vehicle>? dead = null;

        foreach (Entry entry in Entries.Values)
        {
            if (!KsaWorld.IsAlive(entry.Craft) || Stopwatch.GetElapsedTime(entry.AskedAt).TotalSeconds > ForgetAfterSeconds)
            {
                (dead ??= []).Add(entry.Craft);
                continue;
            }

            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds < BudgetMs) Fill(entry, start);
        }

        if (dead is not null)
        {
            foreach (Vehicle craft in dead)
            {
                Entries[craft].Dispose();
                Entries.Remove(craft);
            }
        }

        LastStepMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private static void Fill(Entry entry, long start)
    {
        try
        {
            PartTree tree = entry.Craft.Parts;
            if (tree.IsRebuildingAerodynamics || tree.Aerodynamics is not { } aero)
            {
                entry.Dispose();
                return;
            }

            if (!ReferenceEquals(aero.Source, entry.Source))
            {
                entry.Dispose();
                entry.Ready = null;
                entry.Source = aero.Source;
                entry.Pool = new BufferPool();
                entry.Flow = new AeroSurfaceFlow(aero.Mesh.Surface, entry.Pool);
            }

            if (entry.Building is null)
            {
                if (entry.Ready is not null && Stopwatch.GetElapsedTime(entry.ReadyAt).TotalSeconds < RebuildAfterSeconds) return;
                if (!TryConditions(entry)) return;
                entry.Building = new AeroTable(Levels);
                entry.StartedAt = Stopwatch.GetTimestamp();
                entry.Frames = 0;
                entry.SampleMs = 0.0;
            }

            entry.Frames++;
            long sampling = Stopwatch.GetTimestamp();

            float3 com = entry.Craft.Props.CenterOfMassAsmb;
            var centreOfMass = new Vector3(com.X, com.Y, com.Z);
            AeroTable table = entry.Building;
            Span<double3> channels = stackalloc double3[AeroTable.Channels];
            for (int i = table.Filled; i < table.Count && Stopwatch.GetElapsedTime(start).TotalMilliseconds < BudgetMs; i++)
            {
                Vector3 flow = table.Direction(i).ToBepu();
                entry.Flow!.Update(flow);
                SurfaceLoads still = entry.Law.ComputeLoads(entry.Flow, in entry.Friction, flow, Vector3.Zero, centreOfMass);
                channels[0] = Double3Ex.FromBepu(still.ForcePerDynamicPressure);
                channels[1] = Double3Ex.FromBepu(still.MomentPerDynamicPressure);

                // Damping is a finite difference over a rate about each axis, linear about none.
                for (int axis = 0; axis < 3; axis++)
                {
                    Vector3 rate = axis == 0 ? new Vector3(RatePerSpeed, 0, 0)
                                 : axis == 1 ? new Vector3(0, RatePerSpeed, 0) : new Vector3(0, 0, RatePerSpeed);
                    SurfaceLoads turning = entry.Law.ComputeLoads(entry.Flow, in entry.Friction, flow, rate, centreOfMass);
                    channels[2 + axis] = (Double3Ex.FromBepu(turning.ForcePerDynamicPressure) - channels[0]) * (1.0 / RatePerSpeed);
                    channels[5 + axis] = (Double3Ex.FromBepu(turning.MomentPerDynamicPressure) - channels[1]) * (1.0 / RatePerSpeed);
                }

                table.Set(i, channels);
            }

            entry.SampleMs += Stopwatch.GetElapsedTime(sampling).TotalMilliseconds;

            if (table.Filled == table.Count)
            {
                table.Seal();
                Log.Info($"aero table for {KsaWorld.DisplayName(entry.Craft)}: {table.Count} directions at Mach {entry.Mach:F2} "
                         + $"in {entry.Frames} frame(s), {entry.SampleMs:F1} ms sampling, "
                         + $"{Stopwatch.GetElapsedTime(entry.StartedAt).TotalSeconds:F1} s");
                entry.Ready = table;
                entry.ReadyAt = Stopwatch.GetTimestamp();
                entry.Building = null;
            }
        }
        catch (Exception e)
        {
            Log.Debug(() => $"aero table for {KsaWorld.DisplayName(entry.Craft)}: {e.Message}");
            entry.Dispose();
            entry.Ready = null;
        }
    }

    // The laws at the craft's own speed and air, held for the table: below Mach 1.3 the pressure law
    // is the same at any speed, and friction moves little over the seconds a table lives.
    private static bool TryConditions(Entry entry)
    {
        Vehicle craft = entry.Craft;
        if (KsaWorld.ParentBody(craft) is not Celestial body || body.GetPhysicalAtmosphere() is not { } air) return false;

        double3 position = KsaWorld.PositionEcl(craft);
        double altitude = Vec.Len(position - body.GetPositionEcl()) - body.MeanRadius;
        if (!(altitude < air.Height)) return false;

        double airspeed = Vec.Len(KsaWorld.VelocityEcl(craft) - KsaWorld.GroundVelocityAt(craft, position));
        double temperature = air.ComputeTemperatureAtAltitude(Math.Max(altitude, 0.0));
        double density = air.ComputeDensityAtAltitude(Math.Max(altitude, 0.0));
        double sound = air.Mixture.ComputeSpeedOfSound(temperature);
        if (!(airspeed > 1.0) || !(sound > 0.0)) return false;

        entry.Mach = (float)(airspeed / sound);
        entry.Law = new LocalInclinationLaw(entry.Mach, (float)air.Mixture.ComputeGamma(temperature));
        entry.Friction = new SkinFrictionLaw(air.Mixture, temperature, density, airspeed, null);
        return true;
    }
}
