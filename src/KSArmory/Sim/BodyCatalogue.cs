namespace KSArmory;

/// <summary>
/// What every mod's <c>Bodies.xml</c> said about each body, and what a body nobody described gets. A
/// later mod's entry for a body replaces an earlier one's, and says so, so a system mod can restate a
/// body it reshapes.
/// </summary>
internal static class BodyCatalogue
{
    private static readonly Dictionary<string, (BodyTraits Traits, string Source)> _bodies = new(StringComparer.Ordinal);

    /// <summary>Takes a bodies file from a mod; every fault and replacement comes back to be logged.</summary>
    public static List<PackFault> Register(string xml, string source)
    {
        (List<(string Id, BodyTraits Traits)> bodies, List<PackFault> faults) = BodyReader.Read(xml, source);
        foreach ((string id, BodyTraits traits) in bodies)
        {
            if (_bodies.TryGetValue(id, out (BodyTraits _, string Source) earlier))
                faults.Add(new PackFault(source, "Body", id, $"replaces what {earlier.Source} said about it"));

            _bodies[id] = (traits, source);
        }

        return faults;
    }

    /// <summary>What is known about a body, or the neutral default.</summary>
    public static BodyTraits For(string? bodyId)
        => bodyId is not null && _bodies.TryGetValue(bodyId, out (BodyTraits Traits, string _) entry)
               ? entry.Traits
               : BodyTraits.Default;

    /// <summary>
    /// Every entry naming a body the world does not have: a body this system leaves out, a typo, or a body
    /// a KSA update renamed, which would otherwise quietly give the real body none of its entry.
    /// </summary>
    public static List<PackFault> Audit(Func<string, bool> exists)
    {
        List<PackFault> faults = [];
        foreach ((string id, (BodyTraits _, string source)) in _bodies)
        {
            if (!exists(id)) faults.Add(new PackFault(source, "Body", id, "names no body in this solar system"));
        }

        return faults;
    }

    // For tests: the catalogue is process-wide and a test registers into it.
    internal static void Clear() => _bodies.Clear();
}
