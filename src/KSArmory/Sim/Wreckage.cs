namespace KSArmory;

/// <summary>
/// The craft this mod's warheads have broken, and the pieces that came off them — which are
/// wreckage, not targets.
///
/// <para>A burst that breaks parts off a craft leaves every piece as a vehicle of its own, flying
/// on at the target's speed and inside the threat radius, so a set that takes them for new threats
/// spends its belt on falling debris: flown, a CIWS fired about 480 shells at the pieces of one
/// drone.</para>
///
/// <para><b>Lineage is read off the engine's naming</b>, because the engine records none.
/// <c>Vehicle.GenerateSplitId</c> names a piece <c>{parent}_{n}</c> — unless the piece carries a
/// named control part, when it takes that name instead and is not wreckage by this rule, which is
/// right: somebody can fly it. If the engine stops naming pieces this way, nothing is recognised
/// and wreckage is engaged as it was before.</para>
///
/// <para>The broken craft keeps its own name and stays a target, because losing parts is not
/// being destroyed.</para>
/// </summary>
public sealed class Wreckage
{
    private readonly HashSet<string> _broken = new(StringComparer.Ordinal);
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _lookup;

    public Wreckage() => _lookup = _broken.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>This mod broke parts off the craft called <paramref name="id"/>.</summary>
    public void Broke(string? id)
    {
        if (!string.IsNullOrEmpty(id)) _broken.Add(id);
    }

    /// <summary>
    /// Whether <paramref name="id"/> is a piece of a craft this mod broke, however many splits
    /// down: a piece broken again names its own pieces after itself.
    /// </summary>
    public bool IsPiece(string? id)
    {
        if (string.IsNullOrEmpty(id) || _broken.Count == 0) return false;

        ReadOnlySpan<char> name = id;
        while (TryParent(name, out ReadOnlySpan<char> parent))
        {
            if (_lookup.Contains(parent)) return true;
            name = parent;
        }

        return false;
    }

    public void Clear() => _broken.Clear();

    // "{parent}_{digits}" to its parent, or false for a name the engine did not give a piece.
    internal static bool TryParent(ReadOnlySpan<char> id, out ReadOnlySpan<char> parent)
    {
        parent = default;

        int underscore = id.LastIndexOf('_');
        if (underscore <= 0 || underscore == id.Length - 1) return false;

        foreach (char c in id[(underscore + 1)..])
        {
            if (!char.IsAsciiDigit(c)) return false;
        }

        parent = id[..underscore];
        return true;
    }
}
