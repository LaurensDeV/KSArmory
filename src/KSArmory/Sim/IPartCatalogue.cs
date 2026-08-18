namespace KSArmory;

/// <summary>
/// <b>The seam the audit asks what parts actually exist through.</b>
///
/// <para>A profile names a part Id and a handful of subpart markers, and nothing before the game
/// is running can say whether any of them resolve — the parts arrive from another mod's XML, or
/// from ours, and either can fail to load without a word. This is the one question the audit needs
/// answered, kept narrow so the rest of it stays testable with no game.</para>
/// </summary>
public interface IPartCatalogue
{
    /// <summary>Whether anything declared a part with this Id.</summary>
    bool Declares(string partId);

    /// <summary>
    /// The subpart Ids that part carries, or empty when it declares none — and also when the part
    /// itself is unknown, which <see cref="Declares"/> is the way to tell apart.
    /// </summary>
    IReadOnlyList<string> SubPartIdsOf(string partId);
}
