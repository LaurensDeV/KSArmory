using KSA;

namespace KSArmory;

/// <summary>
/// A bus's own engine kept as the computer wants it, lit for a hop and dark otherwise. KSA puts an engine no stage
/// names into the first, so a design that never staged the bus's engine lights it at liftoff.
///
/// <para>Both ways in <see cref="AttitudeHook"/>'s window, as <see cref="DamagedEngines"/> shuts a dead engine, so the
/// hold and the hop's ignition cannot write it on opposite sides of one worker run.</para>
/// </summary>
internal static class BusEngineHold
{
    private static readonly Dictionary<Vehicle, (Part Bus, Func<bool> Lit)> _owed =
        new(ReferenceEqualityComparer.Instance);

    private static readonly HashSet<Vehicle> _held = new(ReferenceEqualityComparer.Instance);

    /// <summary>Once a frame, with whether the engine on <paramref name="bus"/> should be lit, asked again at the write.</summary>
    public static void Hold(Vehicle craft, Part bus, Func<bool> lit) => _owed[craft] = (bus, lit);

    /// <summary>Whether the hold has had to switch this craft's bus engine off at least once.</summary>
    public static bool HasHeld(Vehicle craft) => _held.Contains(craft);

    /// <summary>Only from <see cref="AttitudeHook"/>'s prefix.</summary>
    public static void Apply(Vehicle craft)
    {
        if (_owed.Count == 0 || !_owed.Remove(craft, out (Part Bus, Func<bool> Lit) owed)) return;
        bool lit = owed.Lit();

        try
        {
            foreach (EngineController engine in craft.Parts.Modules.Get<EngineController>())
            {
                if (engine.IsActive == lit || !ReferenceEquals(engine.Parent?.FullPart, owed.Bus.FullPart)) continue;

                engine.SetIsActive(craft, lit);

                if (!lit && _held.Add(craft))
                {
                    Log.Info($"ICBM computer on {KsaWorld.DisplayName(craft)}: the bus's own engine was lit by "
                             + $"staging (sequence {engine.Sequence}); held off until a hop lights it");
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn($"bus engine on {KsaWorld.DisplayName(craft)} could not be set: {e.Message}");
        }
    }

    /// <summary>Forgets every hold, for a world being taken away.</summary>
    public static void Clear()
    {
        _owed.Clear();
        _held.Clear();
    }
}
