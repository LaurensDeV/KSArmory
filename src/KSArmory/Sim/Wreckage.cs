namespace KSArmory;

/// <summary>
/// The craft this mod's warheads have broken, and the pieces that came off them — which are
/// wreckage, not targets.
///
/// <para>Each piece flies on inside the threat radius, and taken for a threat it draws the belt: a
/// CIWS fired about 480 shells at the pieces of one drone.</para>
///
/// <para>Lineage is read off the engine's naming, because it records none: <c>Vehicle.GenerateSplitId</c>
/// names a piece <c>{parent}_{n}</c>, unless it carries a named control part and takes that name. If the
/// naming changes, wreckage is engaged like any other contact. The broken craft itself stays a target.</para>
/// </summary>
public sealed class Wreckage
{
    private readonly HashSet<string> _broken = new(StringComparer.Ordinal);
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _lookup;

    public Wreckage() => _lookup = _broken.GetAlternateLookup<ReadOnlySpan<char>>();

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
