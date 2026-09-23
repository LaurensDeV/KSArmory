using KSA;

namespace KSArmory;

/// <summary>
/// Holds off the engine's own crash damage on a craft a burst may only dent, while it settles from
/// the shove the blast gave it: the craft that fired and the one being flown are never broken by a
/// burst, and a rocket knocked over onto the pad is broken by that burst whichever code finds the
/// contact.
///
/// <para>The engine finds crash damage on its worker and applies it at the start of the next frame,
/// and no mod hook sits between. So each step a craft is held, the workers are joined and what they
/// found is taken back, which costs a stall on those steps only. Held until the craft is back on
/// rails, which is the engine saying it has come to rest, or for <see cref="MostSeconds"/>.</para>
/// </summary>
internal static class CrashGuard
{
    // How long a craft still tumbling is held at most, and how long before rails are believed: the
    // shove takes it off rails on the next worker run, and a reading before that is the old one.
    private const double MostSeconds = 30.0;
    private const double AtLeastSeconds = 1.0;

    // How long each craft has been held, which parts the engine would have broken and how often it
    // would have destroyed it: the engine finds the same contact every step the craft lies on it, so
    // they are told once, at the end.
    private sealed class Held
    {
        public double Seconds;
        public readonly HashSet<Part> Parts = new(ReferenceEqualityComparer.Instance);
        public int Wholes;
    }

    private static readonly Dictionary<Vehicle, Held> _held = new(ReferenceEqualityComparer.Instance);
    private static readonly List<Vehicle> _scratch = [];

    public static bool Holding => _held.Count > 0;

    public static void Hold(Vehicle craft)
    {
        if (!KsaWorld.IsAlive(craft)) return;

        if (!_held.ContainsKey(craft))
        {
            Log.Info($"holding off crash damage on {KsaWorld.DisplayName(craft)} while it settles from the blast");
        }

        if (_held.TryGetValue(craft, out Held? held)) held.Seconds = 0.0;
        else _held[craft] = new Held();
    }

    public static void Clear() => _held.Clear();

    public static void Update(double dt)
    {
        if (_held.Count == 0 || !double.IsFinite(dt) || dt < 0.0)
        {
            KsaWorld.ForgetQueuedFailures();
            return;
        }

        KsaWorld.WaitForVehicleSolvers();

        _scratch.Clear();
        _scratch.AddRange(_held.Keys);

        foreach (Vehicle craft in _scratch)
        {
            if (!KsaWorld.IsAlive(craft))
            {
                _held.Remove(craft);
                continue;
            }

            Held held = _held[craft];
            if (KsaWorld.TryHoldOffCrash(craft, held.Parts, out bool whole) && whole) held.Wholes++;

            held.Seconds += dt;

            if (held.Seconds > MostSeconds || (held.Seconds > AtLeastSeconds && KsaWorld.IsOnRails(craft)))
            {
                _held.Remove(craft);
                string kept = held.Parts.Count + held.Wholes > 0
                    ? $"; kept {held.Parts.Count} part(s) from breaking on contact"
                      + (held.Wholes > 0 ? " and the craft from being destroyed" : "")
                    : "";
                Log.Info($"{KsaWorld.DisplayName(craft)} has settled after {held.Seconds:F1} s{kept}; "
                         + "crash damage is its own again");
            }
        }

        KsaWorld.ForgetQueuedFailures();
    }
}
