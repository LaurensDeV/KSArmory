namespace KSArmory;

/// <summary>
/// Which of a craft's weapons is selected, as arithmetic.
///
/// <para>The roster this serves is KSA-facing and cannot be reached from the test project, so what
/// lives here is the part that can be: stepping round a list and wrapping. That is also the part
/// with somewhere to hide a mistake — an off-by-one in a selector reads as a weapon that cannot be
/// reached, and a bad modulus on a negative step throws rather than wrapping.</para>
/// </summary>
public static class WeaponSelection
{
    /// <summary>
    /// The index <paramref name="by"/> steps from <paramref name="at"/>, wrapping both ways.
    ///
    /// <para>Wrapped rather than saturating: a selector that stops at the end has to be reasoned
    /// about before each press, which is the opposite of switching weapons quickly. Zero for an
    /// empty list, so a caller with nothing to select gets a valid index rather than an
    /// exception.</para>
    /// </summary>
    public static int Step(int count, int at, int by)
    {
        if (count <= 0) return 0;

        // C#'s % keeps the sign of the left operand, so a negative step lands on a negative index
        // and the caller reads off the front of the list. Adding count before the second modulus
        // is what makes stepping backwards wrap instead of throwing.
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
    /// <para>Stations carrying the same store are one weapon to the operator, so the selector
    /// groups them and this decides which of them the trigger actually reaches. Stepping on rather
    /// than always taking the fullest is what makes a symmetric pair alternate, which is the
    /// behaviour worth having: a wing that empties one side first flies increasingly out of trim,
    /// and the asymmetry is worst exactly when the last store is the heaviest thing left.</para>
    ///
    /// <para>Magazines stay separate — this picks between them rather than pooling them. Pooling
    /// is what would let a store come back: the magazine is sized and filled per launcher, so a
    /// shared one refills on a switch.</para>
    /// </summary>
    public static int NextStation(ReadOnlySpan<int> ammo, int lastFired)
    {
        if (ammo.Length == 0) return -1;

        // Start after the last one used, so two full stations alternate rather than one draining.
        for (int step = 1; step <= ammo.Length; step++)
        {
            int at = Step(ammo.Length, lastFired, step);
            if (ammo[at] > 0) return at;
        }

        return -1;
    }
}
