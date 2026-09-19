using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where a shell burst relative to what it was fired at, in the directions that say why.
///
/// <para>A lead that assumes the target holds its velocity is exact against a target that does, and
/// misses anything slowing, turning or falling by half its acceleration times the flight time
/// squared. Splitting both the miss and that term along the target's track, up, and to the right of
/// the track is what tells an unled acceleration apart from an aim that was simply wrong.</para>
/// </summary>
internal static class LeadError
{
    /// <summary>A displacement along a track, up, and to the right of the track (along × up).</summary>
    internal readonly record struct Split(double Along, double Up, double Right)
    {
        public override string ToString()
            => $"{Along:+0;-0;0} m along its track, {Up:+0;-0;0} m up, {Right:+0;-0;0} m right";
    }

    /// <summary>
    /// Resolves <paramref name="displacement"/> onto the horizontal of <paramref name="track"/>, the
    /// local <paramref name="up"/>, and the right of that track. A track with nothing horizontal in
    /// it takes any horizontal, so a vertical dive still splits into three.
    /// </summary>
    public static Split Resolve(double3 displacement, double3 track, double3 up)
    {
        double3 u = Vec.Unit(up);
        double3 horizontal = track - u * Vec.Dot(track, u);
        double3 along = Vec.Len2(horizontal) > 1e-9 ? Vec.Unit(horizontal) : Vec.Unit(Vec.AnyPerpendicular(u));
        double3 right = Vec.Cross(along, u);

        return new Split(Vec.Dot(displacement, along), Vec.Dot(displacement, u), Vec.Dot(displacement, right));
    }

    /// <summary>
    /// Where a burst lands relative to a target that changed velocity steadily over the flight, when
    /// the lead assumed it kept the velocity it had at the trigger. Velocities only, so neither side
    /// carries a position sampled at a different instant from the other.
    /// </summary>
    public static double3 SteadyTargetMiss(double3 velocityAtFire, double3 velocityAtBurst, double seconds)
        => (velocityAtBurst - velocityAtFire) * (-0.5 * seconds);
}
