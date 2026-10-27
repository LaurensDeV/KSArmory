namespace KSArmory;

/// <summary>
/// What one craft's weapons, between them, have in the air against each thing they are shooting at:
/// the craft over-commits, not the weapon, since two rails each under <c>RoundsPerTarget</c> fire two salvoes.
///
/// <para>Keyed on the target handle by reference, since two sensors hold separate tracks for one
/// aircraft. Rebuilt each frame, because a round leaves the count four ways that share no single place.</para>
/// </summary>
internal sealed class TargetAllocation
{
    private readonly Dictionary<object, int> _committed = new(ReferenceEqualityComparer.Instance);

    public int TargetCount => _committed.Count;

    public void Clear() => _committed.Clear();

    /// <summary>Counts one round in the air; one with no handle, such as a bomb on a coordinate, engages nothing.</summary>
    public void Commit(object? targetRef)
    {
        if (targetRef is null) return;

        _committed[targetRef] = CommittedTo(targetRef) + 1;
    }

    public int CommittedTo(object? targetRef)
        => targetRef is not null && _committed.TryGetValue(targetRef, out int n) ? n : 0;
}
