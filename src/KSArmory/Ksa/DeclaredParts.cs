using KSA;

namespace KSArmory;

/// <summary>
/// The part library as <see cref="IPartCatalogue"/>, off <c>PartTemplate</c> because the question is
/// what was declared, not what is on a craft.
/// <para>Asks through <c>ModLibrary.Get</c>, which throws on a miss: <c>Has</c> and <c>TryGet</c> have
/// no <c>PartTemplate</c> case and answer false for every Id, Core's included.</para>
/// </summary>
public sealed class DeclaredParts : IPartCatalogue
{
    public bool Declares(string partId) => Template(partId) is not null;

    public IReadOnlyList<string> SubPartIdsOf(string partId)
    {
        if (Template(partId) is not { } part) return [];

        List<string> ids = [];
        foreach (PartInstance sub in part.SubPartInstances)
        {
            if (sub.Id is { Length: > 0 } id) ids.Add(id);
        }

        return ids;
    }

    private static PartTemplate? Template(string partId)
    {
        if (string.IsNullOrEmpty(partId)) return null;

        try
        {
            return ModLibrary.Get<PartTemplate>(partId);
        }
        catch
        {
            // A miss, or anything else the library does with a bad Id: this is a report and must not cost a frame.
            return null;
        }
    }
}
