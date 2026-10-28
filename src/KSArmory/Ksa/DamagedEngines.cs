using System.Runtime.CompilerServices;
using KSA;

namespace KSArmory;

/// <summary>
/// Hurt engines written through KSA's own levers (<see cref="EngineDamage"/>): the chamber pressure a
/// combustor or thermal core may reach, the gimbal's travel, and whether the engine is lit.
///
/// <para>Written in <see cref="AttitudeHook"/>'s window, where the worker does not copy over it, and
/// the engine's cached performance re-derived after, as a structural change would. A shut-down engine
/// is shut again whenever it is found lit, because the player can relight it from the part menu.
/// Nothing is saved, which matches the health it follows: a reload puts every engine back.</para>
/// </summary>
internal static class DamagedEngines
{
    private sealed class Applied
    {
        public double Share = 1.0;
        public bool GimbalLost;
    }

    private static readonly ConditionalWeakTable<Part, Applied> _applied = new();
    private static readonly ConditionalWeakTable<RocketCore, StrongBox<float>> _fullPressure = new();
    private static readonly ConditionalWeakTable<Gimbal, StrongBox<(float Y, float Z)>> _fullTravel = new();
    private static readonly Dictionary<Vehicle, List<(Part Part, double Health)>> _owed =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Finds what each hurt engine is owed. Once a frame.</summary>
    public static void Update()
    {
        foreach ((object handle, double health) in PartHealth.World.Damaged())
        {
            if (handle is not Part part || !KsaWorld.TryCraftOf(part, out Vehicle? craft)) continue;

            // Spent: already queued to break off, or riding a piece of debris.
            if (health <= 0.0 || !HasEngine(part)) continue;

            double share = EngineDamage.ThrustShare(health);
            bool gimbalLost = EngineDamage.GimbalLost(health);
            bool relit = EngineDamage.Dead(health) && AnyLit(part);

            Applied applied = _applied.GetValue(part, static _ => new Applied());
            if (applied.Share == share && applied.GimbalLost == gimbalLost && !relit) continue;

            if (!_owed.TryGetValue(craft, out List<(Part, double)>? parts)) _owed[craft] = parts = [];
            if (!parts.Exists(p => ReferenceEquals(p.Item1, part))) parts.Add((part, health));
        }
    }

    /// <summary>Writes what is owed on one craft. Only from <see cref="AttitudeHook"/>'s prefix.</summary>
    public static void Apply(Vehicle craft)
    {
        if (_owed.Count == 0 || !_owed.Remove(craft, out List<(Part Part, double Health)>? parts)) return;

        try
        {
            foreach ((Part part, double health) in parts)
            {
                if (!ReferenceEquals(part.FullPart.Tree, craft.Parts)) continue;

                Applied applied = _applied.GetValue(part, static _ => new Applied());
                double share = EngineDamage.ThrustShare(health);
                bool gimbalLost = EngineDamage.GimbalLost(health);

                foreach (Part piece in Pieces(part))
                {
                    foreach (RocketCore core in piece.Modules.Get<RocketCore>()) Throttle(core, share);
                    foreach (Gimbal gimbal in piece.Modules.Get<Gimbal>()) Hold(gimbal, gimbalLost);
                }

                if (EngineDamage.Dead(health))
                {
                    foreach (EngineController engine in craft.Parts.Modules.Get<EngineController>())
                    {
                        if (engine.IsActive && OnPart(engine, part)) engine.SetIsActive(craft, false);
                    }
                }

                // Said at each quarter of thrust lost, not every frame a gun takes a little more.
                if (Math.Floor(applied.Share * 4.0) != Math.Floor(share * 4.0) || applied.GimbalLost != gimbalLost)
                {
                    Log.Info($"engine damage: {part.Id} on {KsaWorld.DisplayName(craft)} at {health:P0} health, "
                             + $"{share:P0} of its thrust{(gimbalLost ? ", gimbal lost" : string.Empty)}"
                             + $"{(EngineDamage.Dead(health) ? ", shut down" : string.Empty)}");
                }

                applied.Share = share;
                applied.GimbalLost = gimbalLost;
            }

            Refresh(craft);
        }
        catch (Exception e)
        {
            Log.Warn($"engine damage on {KsaWorld.DisplayName(craft)} could not be written: {e.Message}");
        }
    }

    /// <summary>Forgets every queued write, for a world being taken away.</summary>
    public static void Clear() => _owed.Clear();

    // The pressure a throttle of one reaches, so thrust is capped as a lower throttle would cap it.
    private static void Throttle(RocketCore core, double share)
    {
        switch (core)
        {
            case Combustor combustor:
            {
                float full = _fullPressure.GetValue(core, _ => new StrongBox<float>(combustor.Config.CombustionPressureMax)).Value;
                combustor.Config.CombustionPressureMax = (float)(full * share);
                break;
            }
            case ThermalCore thermal:
            {
                float full = _fullPressure.GetValue(core, _ => new StrongBox<float>(thermal.Config.ChamberPressureMax)).Value;
                thermal.Config.ChamberPressureMax = (float)(full * share);
                break;
            }
            default:
                return;
        }

        core.RecomputeDesignConditions();
    }

    private static void Hold(Gimbal gimbal, bool lost)
    {
        (float y, float z) = _fullTravel.GetValue(gimbal, g => new StrongBox<(float, float)>((g.AxisY.MaxAngle, g.AxisZ.MaxAngle))).Value;
        gimbal.AxisY.MaxAngle = lost ? 0f : y;
        gimbal.AxisZ.MaxAngle = lost ? 0f : z;
    }

    // What PartTree re-derives on a structural change, which a capped core or a held gimbal is not.
    private static void Refresh(Vehicle craft)
    {
        PartTree tree = craft.Parts;
        foreach (EngineController engine in tree.Modules.Get<EngineController>())
        {
            RocketControllerData.ComputeFromCores(engine.Cores.AsSpan(), 0f, 101325f, 1f,
                                                  out RocketControllerData vacuum, out RocketControllerData seaLevel);
            engine.VacuumData = vacuum;
            engine.SeaLevelData = seaLevel;
        }

        foreach (GimbalController gimbals in tree.Modules.Get<GimbalController>()) gimbals.RecomputeStaticData();

        tree.PerformanceSequences.SetDirty();
        craft.FlightComputer.ReadUpdatedVehicleConfiguration(craft);
    }

    private static bool HasEngine(Part part)
    {
        try
        {
            foreach (Part piece in Pieces(part))
            {
                if (!piece.Modules.Get<RocketCore>().IsEmpty || !piece.Modules.Get<Gimbal>().IsEmpty) return true;
            }
        }
        catch
        {
            // A part tree mid-rebuild; asked again next frame.
        }

        return false;
    }

    private static bool AnyLit(Part part)
    {
        try
        {
            if (!KsaWorld.TryCraftOf(part, out Vehicle? craft)) return false;
            foreach (EngineController engine in craft.Parts.Modules.Get<EngineController>())
            {
                if (engine.IsActive && OnPart(engine, part)) return true;
            }
        }
        catch
        {
            // As above.
        }

        return false;
    }

    private static bool OnPart(EngineController engine, Part part)
        => ReferenceEquals(engine.Parent?.FullPart, part.FullPart);

    private static IEnumerable<Part> Pieces(Part part)
    {
        Part full = part.FullPart;
        yield return full;
        foreach (Part sub in full.SubParts.ToArray()) yield return sub;
    }
}
