namespace KSArmory;

/// <summary>
/// Which of a craft's weapons is selected, as arithmetic.
///
/// <para>The roster it serves is KSA-facing and untestable, so the stepping and wrapping live here.</para>
/// </summary>
public static class WeaponSelection
{
    /// <summary>
    /// The index <paramref name="by"/> steps from <paramref name="at"/>, wrapping both ways.
    ///
    /// <para>Zero for an empty list.</para>
    /// </summary>
    public static int Step(int count, int at, int by)
    {
        if (count <= 0) return 0;

        // C#'s % keeps the sign of the left operand, so count is added before the second modulus.
        return ((at + by) % count + count) % count;
    }

    /// <summary>
    /// Where <paramref name="ordinal"/> sits among <paramref name="ordinals"/>, or 0 when it is
    /// not among them — which is what happens when the selected launcher has been shot off.
    /// </summary>
    public static int IndexOf(ReadOnlySpan<int> ordinals, int ordinal)
    {
        for (int i = 0; i < ordinals.Length; i++)
        {
            if (ordinals[i] == ordinal) return i;
        }

        return 0;
    }

    /// <summary>
    /// Which station of a group fires next: the first one after <paramref name="lastFired"/> with
    /// rounds left, wrapping. −1 when the whole group is empty.
    ///
    /// <para>Stepping on rather than taking the fullest makes a symmetric pair alternate, so a wing
    /// does not empty one side first. Magazines stay separate: each is filled per launcher, so a
    /// pooled one would refill on a switch.</para>
    /// </summary>
    public static int NextStation(ReadOnlySpan<int> ammo, int lastFired)
    {
        if (ammo.Length == 0) return -1;

        for (int step = 1; step <= ammo.Length; step++)
        {
            int at = Step(ammo.Length, lastFired, step);
            if (ammo[at] > 0) return at;
        }

        return -1;
    }
}
