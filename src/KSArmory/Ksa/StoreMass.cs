using System.Runtime.CompilerServices;
using KSA;

namespace KSArmory;

/// <summary>
/// A launcher or dispenser part made lighter by what it has fired. The part's declared mass is with
/// its full load; each frame the mass it should have now is worked out from what is still aboard
/// (<see cref="StoreLoad"/>), and a part whose mass differs is rewritten in <see cref="AttitudeHook"/>'s
/// window, the one place the physics worker does not copy over it.
///
/// <para><b>The part's own mass, through KSA's own slot</b> (<c>Part.SetInertMassPropertiesAsmb</c>,
/// what a parachute does when it loses its canopy). It moves with the part through a split or a dock,
/// and is not saved, which matches a magazine: a reload refills both.</para>
///
/// <para><b>Scaled where it is declared, never moved to the tube.</b> The MIRV bus's mass sits on its
/// thruster ring on purpose; taken off at a tube it would put every attitude correction off-axis.</para>
/// </summary>
internal static class StoreMass
{
    // The part's mass with its full load, read the first time it is seen and again whenever the
    // engine puts the template's back, as a load or a rescale does.
    private sealed class Held
    {
        public OffsetMassProperties Full;
        public float WrittenKg = float.NaN;
        public double WantedFactor = 1.0;
    }

    private static readonly ConditionalWeakTable<Part, Held> _held = new();
    private static readonly Dictionary<Vehicle, List<Part>> _owed = new(ReferenceEqualityComparer.Instance);

    /// <summary>Wants a part lighter by <paramref name="shedKg"/>. Called each frame for every launcher and dispenser.</summary>
    public static void Want(Vehicle craft, Part part, double shedKg)
    {
        try
        {
            if (!TryInertMass(part, out InertMass module)) return;

            Held held = _held.GetValue(part, static _ => new Held());
            float now = module.MassPropertiesAsmb.Props.Mass;

            // Not what was last written: the engine has put the template's back, so that is full.
            if (float.IsNaN(held.WrittenKg) || Math.Abs(now - held.WrittenKg) > 1e-3f)
            {
                held.Full = module.MassPropertiesAsmb;
                held.WrittenKg = now;
            }

            held.WantedFactor = StoreLoad.Factor(held.Full.Props.Mass, shedKg);
            float wantedKg = (float)(held.Full.Props.Mass * held.WantedFactor);

            // A tenth of a percent of the part, so a gun firing is a write every few dozen rounds
            // rather than a whole recompute of the craft every frame; a store is always far more.
            float band = Math.Max(1e-3f, 0.001f * held.Full.Props.Mass);
            if (Math.Abs(wantedKg - held.WrittenKg) <= band) return;

            if (!_owed.TryGetValue(craft, out List<Part>? parts)) _owed[craft] = parts = [];
            if (!parts.Contains(part)) parts.Add(part);
        }
        catch
        {
            // A part tree mid-rebuild; asked again next frame.
        }
    }

    /// <summary>Writes what is owed on one craft. Only from <see cref="AttitudeHook"/>'s prefix.</summary>
    public static void Apply(Vehicle craft)
    {
        if (_owed.Count == 0 || !_owed.Remove(craft, out List<Part>? parts)) return;

        PartTree tree = craft.Parts;
        bool wrote = false;

        foreach (Part part in parts)
        {
            if (!_held.TryGetValue(part, out Held? held) || !ReferenceEquals(part.Tree, tree)) continue;

            OffsetMassProperties props = held.Full;
            props.Props = held.Full.Props.Scale((float)held.WantedFactor);

            part.SetInertMassPropertiesAsmb(in props);
            Log.Info($"store mass: {part.Id} on {KsaWorld.DisplayName(craft)} now {props.Props.Mass:F2} kg "
                     + $"of {held.Full.Props.Mass:F2} (was {held.WrittenKg:F2})");
            held.WrittenKg = props.Props.Mass;
            wrote = true;
        }

        if (!wrote) return;

        // The tree's sum, then the craft's: the engine copies one into the other only on a structural
        // change, so a part's new mass alone reaches nothing that flies.
        tree.RefreshStaticMass();
        ref VehicleProperties props2 = ref Unsafe.AsRef(in craft.Props);
        props2.InertMassPropsAsmb = tree.ComputeInertMassPropertiesAsmb();
        props2.RecomputeMassProperties(tree.SubstanceStores, tree.Moles.States);
        tree.PerformanceSequences.SetDirty();
        craft.FlightComputer.ReadUpdatedVehicleConfiguration(craft);
    }

    /// <summary>Forgets every queued write, for a world being taken away.</summary>
    public static void Clear() => _owed.Clear();

    private static bool TryInertMass(Part part, out InertMass module)
    {
        module = null!;
        if (!Module<InertMass>.TryGetFrom(part.Modules, out Module<InertMass>.List list)) return false;

        foreach (InertMass m in list.Modules)
        {
            if (!ReferenceEquals(m.Parent, part)) continue;

            module = m;
            return true;
        }

        return false;
    }
}
