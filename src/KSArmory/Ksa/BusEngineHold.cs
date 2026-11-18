using KSA;

namespace KSArmory;

/// <summary>
/// A bus's own engine switched off whenever staging has lit it, until a hop wants it. KSA puts an engine no
/// stage names into the first, so a design that never staged the bus's engine lights it at liftoff.
///
/// <para>Written in <see cref="AttitudeHook"/>'s window, as <see cref="DamagedEngines"/> shuts a dead engine,
/// and asked again there, because a hop may light the engine after the hold was asked for.</para>
/// </summary>
internal static class BusEngineHold
{
    private static readonly Dictionary<Vehicle, (Part Bus, Func<bool> StillWanted)> _owed =
        new(ReferenceEqualityComparer.Instance);

    private static readonly HashSet<Vehicle> _held = new(ReferenceEqualityComparer.Instance);

    /// <summary>Once a frame, while the engine on <paramref name="bus"/> should stay dark.</summary>
    public static void Hold(Vehicle craft, Part bus, Func<bool> stillWanted) => _owed[craft] = (bus, stillWanted);

    /// <summary>Whether the hold has had to switch this craft's bus engine off at least once.</summary>
    public static bool HasHeld(Vehicle craft) => _held.Contains(craft);

    /// <summary>Only from <see cref="AttitudeHook"/>'s prefix.</summary>
    public static void Apply(Vehicle craft)
    {
        if (_owed.Count == 0 || !_owed.Remove(craft, out (Part Bus, Func<bool> StillWanted) owed)) return;
        if (!owed.StillWanted()) return;

        try
        {
            foreach (EngineController engine in craft.Parts.Modules.Get<EngineController>())
            {
                if (!engine.IsActive || !ReferenceEquals(engine.Parent?.FullPart, owed.Bus.FullPart)) continue;

                engine.SetIsActive(craft, false);

                if (_held.Add(craft))
                {
                    Log.Info($"ICBM computer on {KsaWorld.DisplayName(craft)}: the bus's own engine was lit by "
                             + $"staging (sequence {engine.Sequence}); held off until a hop lights it");
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn($"bus engine on {KsaWorld.DisplayName(craft)} could not be held off: {e.Message}");
        }
    }

    /// <summary>Forgets every hold, for a world being taken away.</summary>
    public static void Clear()
    {
        _owed.Clear();
        _held.Clear();
    }
}
