namespace KSArmory.Tests;

/// <summary>
/// Six tubes on a six-degree cone, as a launcher for the tests that are about cant.
///
/// <para>The shipped MIRV bus's tubes are straight, because a cone's spread depends on where the
/// bus's nose drifts and nothing aboard can correct it (<c>docs/MIRV-NEXT.md</c> item 5). But
/// <c>Sim/ReleasePointing.cs</c> and <c>Sim/ReleaseSequence.cs</c> name no launcher and a weapon
/// pack may register a canted one, so these tests keep a canted specimen rather than reading the
/// shipped profile, which would test no cant at all.</para>
/// </summary>
internal static class CantedRing
{
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
