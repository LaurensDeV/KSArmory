namespace KSArmory.Tests;

/// <summary>
/// Six tubes on a six-degree cone, as a launcher for the tests that are about cant.
///
/// <para>The shipped MIRV bus no longer has any: its tubes were straightened, because the spread a
/// cone produces depends on where the bus's nose happens to be drifting and nothing aboard can
/// correct it — KSA's attitude tracker measured a 22.11 degree dead zone against a 6 degree turn.
/// See <c>docs/MIRV-NEXT.md</c> item 5.</para>
///
/// <para><b>The machinery it exercises is still general and still shipped.</b>
/// <c>Sim/ReleasePointing.cs</c> and <c>Sim/ReleaseSequence.cs</c> name no launcher, and a weapon
/// pack may register a canted one on a vehicle that can hold the command. Pointing these tests at
/// the shipped profile would have quietly stopped testing any of it the moment the bus went
/// straight — which is exactly what <c>MirvSpreadTests</c> refused to let happen.</para>
/// </summary>
internal static class CantedRing
{
    /// <summary>The bus's own geometry up to the straightening, kept as the canted specimen.</summary>
    public static Tube[] Tubes =>
    [
        new(new(2.62959, 1.05860, 0.00000), new(0.99452, 0.10453, 0.00000)),
        new(new(2.62959, 0.52930, 0.91678), new(0.99452, 0.05226, 0.09052)),
        new(new(2.62959, -0.52930, 0.91678), new(0.99452, -0.05226, 0.09052)),
        new(new(2.62959, -1.05860, 0.00000), new(0.99452, -0.10453, 0.00000)),
        new(new(2.62959, -0.52930, -0.91678), new(0.99452, -0.05226, -0.09052)),
        new(new(2.62959, 0.52930, -0.91678), new(0.99452, 0.05226, -0.09052)),
    ];
}
