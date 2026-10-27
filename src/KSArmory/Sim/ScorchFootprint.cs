namespace KSArmory;

/// <summary>
/// How far a scorch mark reaches downwind, across and upwind, in patch radii -- the box the cloud
/// pass dispatches a mark over.
///
/// <para>All three restate the shader's own constants; if the shader grows past them the mark is
/// cropped at a straight edge, which reads as terrain. <c>ScorchFootprintTests</c> compares them.</para>
/// </summary>
public static class ScorchFootprint
{
    public const double Reach = 3.3;
    public const double Across = 1.8;
    public const double Behind = 1.2;
}
