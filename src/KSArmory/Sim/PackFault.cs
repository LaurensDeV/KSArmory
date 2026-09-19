namespace KSArmory;

/// <summary>
/// One definition a pack offered and the catalogue would not take, with the reason.
///
/// <para>A definition is refused on its own: one bad round does not cost a pack its other five,
/// and a bad attribute does not cost a round its file. The alternative — refusing the file —
/// makes a typo look like a mod that never loaded, which is the failure this whole path exists
/// to stop being silent.</para>
/// </summary>
/// <param name="Source">The pack that offered it, as the pack named itself.</param>
/// <param name="Element">Which kind of definition, or <c>WeaponPack</c> when the file itself
/// is the problem.</param>
/// <param name="Name">What the definition called itself, or empty when it did not get that far.</param>
/// <param name="Reason">What was wrong, in terms an author can act on.</param>
public readonly record struct PackFault(string Source, string Element, string Name, string Reason)
{
    public override string ToString()
        => Name.Length > 0
               ? $"{Source}: {Element} '{Name}' - {Reason}"
               : $"{Source}: {Element} - {Reason}";
}
