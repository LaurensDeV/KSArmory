namespace KSArmory;

/// <summary>
/// What one pack got out of registering: how much stuck, and everything that did not.
///
/// <para>Handed back to the caller <em>and</em> kept, because a pack is free to ignore a return
/// value and a refusal nobody sees is the failure this whole path exists to prevent.</para>
/// </summary>
public readonly record struct PackResult(string Source, int Registered, IReadOnlyList<PackFault> Faults)
{
    /// <summary>True when everything the pack offered was taken.</summary>
    public bool Complete => Faults.Count == 0;
}
