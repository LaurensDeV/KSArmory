using KSA;

namespace KSArmory;

/// <summary>
/// The part library, as <see cref="IPartCatalogue"/> — the only KSA contact the audit needs.
///
/// <para>Answers off <c>PartTemplate</c> rather than off anything on a craft, because the
/// question is what was <em>declared</em>: a part nobody has placed yet still has to exist before
/// a profile naming it means anything.</para>
/// </summary>
public sealed class DeclaredParts : IPartCatalogue
{
    public bool Declares(string partId)
    {
        try
        {
            return !string.IsNullOrEmpty(partId) && ModLibrary.Has<PartTemplate>(partId);
        }
        catch
        {
            // The audit is a report. A library that will not answer must not cost a frame.
            return false;
        }
    }

    public IReadOnlyList<string> SubPartIdsOf(string partId)
    {
        try
        {
            if (!ModLibrary.TryGet(partId, out PartTemplate? part) || part is null) return [];

            List<string> ids = [];
            foreach (PartInstance sub in part.SubPartInstances)
            {
                if (sub.Id is { Length: > 0 } id) ids.Add(id);
            }

            return ids;
        }
        catch
        {
            return [];
        }
    }
}
