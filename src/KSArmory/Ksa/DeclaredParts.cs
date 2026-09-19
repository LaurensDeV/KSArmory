using KSA;

namespace KSArmory;

/// <summary>
/// The part library, as <see cref="IPartCatalogue"/> — the only KSA contact the audit needs.
///
/// <para>Answers off <c>PartTemplate</c> rather than off anything on a craft, because the
/// question is what was <em>declared</em>: a part nobody has placed yet still has to exist before
/// a profile naming it means anything.</para>
///
/// <para><b>Asks through <c>ModLibrary.Get</c>, which throws, rather than through <c>Has</c> or
/// <c>TryGet</c>, which do not.</b> Both of those dispatch on the type argument through a chain
/// of branches that has no <c>PartTemplate</c> case and falls through to <c>false</c> — so
/// <c>Has&lt;PartTemplate&gt;</c> answers "no such part" for every Id in the game, including
/// Core's. Only <c>Get</c> reaches <c>AllParts</c>, and it reports a miss by throwing.</para>
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
            // A miss, which is the answer the audit wants -- and anything else the library does
            // with a bad Id, since this is a report and must not cost a frame.
            return null;
        }
    }
}
