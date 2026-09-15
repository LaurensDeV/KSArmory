using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The drag a craft feels from air arriving from a given direction, as the engine computes it.
///
/// <para>KSA's drag is not a coefficient on airspeed squared. It is a drag area fixed to the craft's
/// body, times dynamic pressure, over the mass: the bounding box's faces weighted by how squarely the
/// air meets each — 0.3 on the nose, 1.0 on the tail, 1.2 on the sides — plus a skin term of a tenth
/// of its surface area (<c>BoundingBoxCdA.ComputeCdA</c>, <c>PhysicsStates.ComputeDrag</c>). So a craft
/// holding its attitude while its path bends presents a different area, and its apparent coefficient
/// drifts: flown, 65.8 m² falling to 52.9 while its slowing over that area held at ½ρ to four places.
/// A lead holding the coefficient put every first shell 25 m behind and above the drone.</para>
///
/// <para>Computed rather than read off the slowing, because a craft under power is not slowing and
/// still has all of its drag.</para>
///
/// <para>A copy of the engine's model as of 2026.9.7.5402, which RocketWerkz are reworking:
/// <c>docs/BLOCKED-ON-KSA.md</c> has the check that confirms it still matches.</para>
/// </summary>
/// <param name="Positive">Drag area of each body axis's positive face (m²).</param>
/// <param name="Negative">Drag area of each body axis's negative face (m²).</param>
/// <param name="Skin">The skin term, a tenth of the surface area, whichever way the air comes (m²).</param>
/// <param name="Body2Ecl">The craft's attitude.</param>
/// <param name="BodyRates">How fast it is turning, in its own axes (rad/s), held over a prediction.</param>
/// <param name="Mass">Its mass (kg).</param>
/// <param name="SeaLevelDensity">Sea-level density of the air it flies in (kg/m³).</param>
/// <param name="ExhaustVelocity">
/// Its engines' thrust over their mass flow (m/s), zero with none. Throttle-independent, so the mass
/// flow follows from the thrust it is measured under: flown, a burning drone's push rose from 21.0 to
/// 22.8 m/s² over 15 s on a steady force, and holding it put the bursts 19.5 m under it.
/// </param>
/// <param name="PropellantMass">What it has left to burn (kg), which is when the thrust stops.</param>
public readonly record struct DragShape(double3 Positive, double3 Negative, double Skin, doubleQuat Body2Ecl,
                                        double3 BodyRates, double Mass, double SeaLevelDensity,
                                        double ExhaustVelocity, double PropellantMass)
{
    /// <summary>The mass flow that gives it <paramref name="thrustNow"/> of acceleration (kg/s).</summary>
    public double MassFlowFor(double3 thrustNow)
        => ExhaustVelocity > 0.0 ? Vec.Len(thrustNow) * Mass / ExhaustVelocity : 0.0;

    /// <summary>How long that mass flow lasts on what it has left.</summary>
    public double BurnSeconds(double massFlow)
        => massFlow > 0.0 ? PropellantMass / massFlow : double.PositiveInfinity;

    /// <summary>Its mass <paramref name="seconds"/> from now, burning at <paramref name="massFlow"/> until the propellant is gone.</summary>
    public double MassAt(double massFlow, double seconds)
        => Mass - (massFlow * Math.Clamp(seconds, 0.0, BurnSeconds(massFlow)));

    /// <summary>The drag area air moving along <paramref name="airflowEcl"/> meets, in m², <paramref name="seconds"/> from now.</summary>
    public double AreaFacing(double3 airflowEcl, double seconds = 0.0)
    {
        double3 d = ToBody(Vec.Unit(airflowEcl), seconds);

        return (Math.Max(d.X, 0.0) * Positive.X) + (Math.Max(d.Y, 0.0) * Positive.Y) + (Math.Max(d.Z, 0.0) * Positive.Z)
               + (Math.Max(-d.X, 0.0) * Negative.X) + (Math.Max(-d.Y, 0.0) * Negative.Y) + (Math.Max(-d.Z, 0.0) * Negative.Z)
               + Skin;
    }

    /// <summary>
    /// The drag acceleration on it at <paramref name="airVelocity"/>, in a medium
    /// <paramref name="densityRatio"/> times sea-level air, <paramref name="seconds"/> from now.
    /// </summary>
    public double3 DragAcceleration(double3 airVelocity, double densityRatio, double seconds = 0.0)
    {
        double airspeed = Vec.Len(airVelocity);
        if (!(airspeed > 0.0) || !(densityRatio > 0.0)) return Vec.Zero;

        double perSpeed = 0.5 * SeaLevelDensity * densityRatio * AreaFacing(airVelocity, seconds) * airspeed / Mass;
        return airVelocity * -perSpeed;
    }

    /// <summary>
    /// A direction fixed to the body — an engine's thrust — carried <paramref name="seconds"/> on by
    /// the turn.
    /// </summary>
    public double3 Carry(double3 vectorEcl, double seconds)
        => IsTurning ? Body2Ecl * (Turn(seconds) * (doubleQuat.Conjugate(Body2Ecl) * vectorEcl)) : vectorEcl;

    public bool IsTurning => Vec.Len2(BodyRates) > 0.0;

    /// <summary>True when every term is a real number the drag can be computed from.</summary>
    public bool IsUsable
        => Vec.IsFinite(Positive) && Vec.IsFinite(Negative) && double.IsFinite(Skin) && Vec.IsFinite(BodyRates)
           && Positive.X >= 0.0 && Positive.Y >= 0.0 && Positive.Z >= 0.0
           && Negative.X >= 0.0 && Negative.Y >= 0.0 && Negative.Z >= 0.0 && Skin >= 0.0
           && (Positive.X + Positive.Y + Positive.Z + Negative.X + Negative.Y + Negative.Z + Skin) > 0.0
           && Mass > 0.0 && double.IsFinite(Mass) && SeaLevelDensity > 0.0 && double.IsFinite(SeaLevelDensity)
           && ExhaustVelocity >= 0.0 && double.IsFinite(ExhaustVelocity)
           && PropellantMass >= 0.0 && PropellantMass < Mass;

    // Body rates are in the body's own axes, so the turn composes on the body side of the attitude.
    private double3 ToBody(double3 ecl, double seconds)
    {
        double3 now = doubleQuat.Conjugate(Body2Ecl) * ecl;
        return IsTurning && seconds != 0.0 ? doubleQuat.Conjugate(Turn(seconds)) * now : now;
    }

    private doubleQuat Turn(double seconds)
    {
        double rate = Vec.Len(BodyRates);
        return doubleQuat.CreateFromAxisAngle(BodyRates * (1.0 / rate), rate * seconds);
    }
}
