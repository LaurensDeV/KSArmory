using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// What the ground under a burst adds to its blast at a point, as a multiple of the free-air
/// overpressure there.
///
/// <para><b>On the ground it is the hemisphere</b>: the half of the energy that would have gone down
/// is reflected up, so everything is loaded as though by twice the charge. <b>In the air it is the
/// reflection</b>: near ground zero the front strikes the ground and is reflected, and past an
/// incidence of about 40° the two merge into the Mach stem, one front standing on the ground and
/// carrying the reflected overpressure -- twice the incident for a weak front, up to eight for a
/// strong one (Glasstone and Dolan, 3.23-3.33). A point above the stem feels the free-air front.
/// That is why weapons are air-burst: at the right height the stem loads the ground harder than a
/// surface burst of the same yield.</para>
///
/// <para>The damage law goes as the cube of the distance, so a pressure multiple is exactly a charge
/// multiple, and a caller applies this by scaling the charge.</para>
/// </summary>
internal readonly record struct GroundReflection(double3 BurstEcl, double3 Up, double Height, double Coupling)
{
    /// <summary>A burst with no ground near enough to matter.</summary>
    public static GroundReflection FreeAir => new(Vec.Zero, Vec.Zero, double.PositiveInfinity, 0.0);

    /// <summary>The hemisphere's doubling, on the ground.</summary>
    public const double SurfaceGain = 2.0;

    /// <summary>
    /// Where the Mach stem forms, as ground range over burst height: the tangent of the roughly 40°
    /// incidence past which a reflected front merges with the one it reflects.
    /// </summary>
    public const double MachOnset = 0.84;

    /// <summary>
    /// How fast the stem's top climbs past where it forms, per metre of ground range. The triple
    /// point rises from nothing to hundreds of metres at kiloton scale; the slope is drawn, not
    /// measured.
    /// </summary>
    public const double TriplePointSlope = 0.1;

    /// <summary>
    /// The layer over the ground that meets the reflected front inside the Mach onset, as a share of
    /// the burst height: the incident and reflected fronts are one there, and part apart above it.
    /// </summary>
    public const double ReflectedLayer = 0.03;

    /// <summary>The multiple of the free-air overpressure at a point, for a charge in kg.</summary>
    public double GainAt(double3 pointEcl, double chargeKg)
    {
        if (!double.IsFinite(Height) || !(chargeKg > 0.0) || !Vec.IsFinite(pointEcl)) return 1.0;

        double3 offset = pointEcl - BurstEcl;
        double up = Vec.Dot(offset, Up);
        double range = Vec.Len(offset - (Up * up));
        double over = up + Height;

        double reflected = SurfaceGain;
        double incident = BlastWave.PeakOverpressurePascals(chargeKg, Vec.Len(offset), reflection: 1.0);
        if (incident > 0.0) reflected = Math.Max(BlastWave.ReflectedPascals(incident) / incident, SurfaceGain);

        double air = 1.0 + ((reflected - 1.0) * InReflection(Height, range, over));
        double coupling = Math.Clamp(Coupling, 0.0, 1.0);
        return (coupling * SurfaceGain) + ((1.0 - coupling) * air);
    }

    /// <summary>
    /// Where a front pushes a point from: the burst, or for a point in an air burst's Mach stem the
    /// place over ground zero level with it -- the stem is a wall standing on the ground and pushes
    /// along it, where near ground zero the front still comes down from above. The burst is taken
    /// where it is now, which a caller following a front across frames passes in.
    /// </summary>
    public double3 PushFrom(double3 burstEcl, double3 pointEcl)
    {
        if (!double.IsFinite(Height) || !(Height > 0.0) || Coupling >= 0.5) return burstEcl;

        double3 offset = pointEcl - burstEcl;
        double up = Vec.Dot(offset, Up);
        double across = Vec.Len(offset - (Up * up));
        if (across < MachOnset * Height) return burstEcl;

        return InReflection(Height, across, up + Height) > 0.5 ? burstEcl + (Up * up) : burstEcl;
    }

    /// <summary>
    /// How far a point <paramref name="over"/> the ground at <paramref name="range"/> from ground zero
    /// is in the reflected front, in [0, 1]: under the stem's top, or inside the thin layer the
    /// reflection meets the incident front in before the stem forms.
    /// </summary>
    public static double InReflection(double burstHeight, double range, double over)
    {
        if (!(burstHeight > 0.0)) return 1.0;

        double top = Math.Max(Math.Max(range - (MachOnset * burstHeight), 0.0) * TriplePointSlope,
                              ReflectedLayer * burstHeight);
        if (over <= top) return 1.0;

        double edge = 0.25 * top;
        return over >= top + edge ? 0.0 : 1.0 - ((over - top) / edge);
    }
}
