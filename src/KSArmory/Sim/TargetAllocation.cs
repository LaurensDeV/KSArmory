namespace KSArmory;

/// <summary>
/// What one craft's weapons, between them, already have in the air against each thing they are
/// shooting at.
///
/// <para><b>The unit that over-commits is the craft, not the weapon.</b> Each weapons system
/// counts only the rounds it fired itself, so two rails on one aircraft each look at their own
/// tally, each find capacity under <c>RoundsPerTarget</c>, and each fire a full salvo at the same
/// target. The limit is obeyed twice over and the aircraft is engaged twice as hard as anybody
/// asked for. Nothing about the count is per weapon: what matters is how many rounds are on their
/// way, whoever threw them.</para>
///
/// <para><b>Handles are opaque and compared by reference.</b> A target can be a craft or another
/// round, and the only thing every weapon on a craft can agree on is the object itself — two
/// sensors hold their own separate track for the same aircraft, so counting by track would count
/// nothing in common. This is the same reason a weapon attributes its own rounds by
/// <c>TargetRef</c> rather than by track.</para>
///
/// <para>Rebuilt from scratch each frame rather than incremented and decremented. A round that
/// detonated, was shot down, went loose or lost its lock has to stop being counted, and there is
/// no single place all four of those pass through.</para>
/// </summary>
internal sealed class TargetAllocation
{
    private readonly Dictionary<object, int> _committed = new(ReferenceEqualityComparer.Instance);

    /// <summary>How many things the craft is currently shooting at.</summary>
    public int TargetCount => _committed.Count;

    public void Clear() => _committed.Clear();

    /// <summary>
    /// Counts one round in the air. A round with nothing to steer at is not committed to anything
    /// and is deliberately not counted: a bomb falling on a coordinate cannot be said to be
    /// engaging a target, and counting it would hold fire against a target nobody is shooting at.
    /// </summary>
    public void Commit(object? targetRef)
    {
        if (targetRef is null) return;

        _committed[targetRef] = CommittedTo(targetRef) + 1;
    }

    /// <summary>How many rounds the whole craft has in the air against this one thing.</summary>
    public int CommittedTo(object? targetRef)
        => targetRef is not null && _committed.TryGetValue(targetRef, out int n) ? n : 0;
}
