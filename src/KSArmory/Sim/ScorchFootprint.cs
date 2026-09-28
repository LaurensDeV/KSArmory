namespace KSArmory;

/// <summary>
/// How far a scorch mark reaches downwind, across and upwind, in patch radii -- the box the cloud
/// pass dispatches a mark over.
///
/// <para>All three restate the shader's own constants: the plume's run with its wandering tip, its
/// widest half-width with its wandering edge and soft cut, and the patch's ragged rim. If the shader
/// grows past them the mark is cropped at a straight edge partway along itself, which reads as
/// terrain rather than as a fault. <c>ScorchFootprintTests</c> compares the two sides.</para>
/// </summary>
public static class ScorchFootprint
{
    public const double Reach = 3.3;
    public const double Across = 1.8;
    public const double Behind = 1.2;
}
