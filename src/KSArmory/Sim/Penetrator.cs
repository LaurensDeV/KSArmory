namespace KSArmory;

/// <summary>One part a round's path crosses, as the walk judges it.</summary>
/// <param name="PartJoules">The energy that breaks the part (<see cref="PartToughness.Joules"/>).</param>
/// <param name="CosObliquity">The cosine of the angle between the path and the part's surface normal where it enters.</param>
/// <param name="ThicknessLeftMetres">What craters have left of the wall or plate where the path enters.</param>
/// <param name="Judged">The verdict already reached for this crossing where the strike's own cast met it.</param>
internal readonly record struct LayerCrossing(int Index, double PartJoules, TankWall Wall, ArmourLayer Layer,
                                              double CosObliquity, double ThicknessLeftMetres, bool? Judged = null);

internal enum CrossingOutcome
{
    Through,
    Stopped,
    Glanced,
}

/// <summary>What one crossing did: the share of the part's health it cost, and a dent where the round stopped.</summary>
internal readonly record struct CrossingResult(int Ordinal, int Index, double Share, bool Struck, CrossingOutcome Outcome,
                                               double DentMetres);

/// <summary>
/// A round that does not burst walked through the parts on its path, nearest first: an armour plate judged by
/// Lambert's law, a tank by its wall and then its liquid, anything else keeping its share of what is left.
/// The order is the path's, so spaced plates, a plate in front of a tank, and a tank behind another all follow.
/// </summary>
internal static class Penetrator
{
    /// <summary>
    /// Walks <paramref name="crossings"/> and writes what each did into <paramref name="into"/>; returns the speed
    /// the round has left. <paramref name="ram"/> answers, for the crossing at an ordinal entered at a speed, the
    /// share of the round's energy the tank's liquid took; null where nothing can ram.
    /// </summary>
    public static double Walk(ReadOnlySpan<LayerCrossing> crossings, double massKg, double diameterMetres,
                              double lengthOverDiameter, double speed, double damageScale, double leftoverJoules,
                              Func<int, double, double>? ram, List<CrossingResult> into)
    {
        into.Clear();
        if (!(massKg > 0.0) || !(speed > 0.0)) return 0.0;

        double density = ArmourLayer.RoundDensity(massKg, diameterMetres, lengthOverDiameter);
        double v = speed;
        for (int i = 0; i < crossings.Length; i++)
        {
            LayerCrossing c = crossings[i];
            double energy = 0.5 * massKg * v * v;
            double joules = c.PartJoules > 0.0 ? c.PartJoules : double.PositiveInfinity;

            if (c.Layer.Known)
            {
                bool through = c.Judged ?? v > c.Layer.BallisticLimit(massKg, diameterMetres, lengthOverDiameter,
                                                                       c.CosObliquity, c.ThicknessLeftMetres, damageScale);
                if (!through)
                {
                    CrossingOutcome outcome = c.Layer.Glances(v, c.CosObliquity, density) ? CrossingOutcome.Glanced
                                                                                           : CrossingOutcome.Stopped;
                    double dent = c.Layer.DentMetres(v, massKg, diameterMetres, lengthOverDiameter, c.CosObliquity, damageScale);
                    into.Add(new CrossingResult(i, c.Index, PartToughness.KineticCoupling * energy / joules, false, outcome, dent));
                    return 0.0;
                }

                double left = c.Layer.ResidualSpeed(v, massKg, diameterMetres, lengthOverDiameter, c.CosObliquity,
                                                    c.ThicknessLeftMetres, damageScale);
                double lost = energy - (0.5 * massKg * left * left);
                into.Add(new CrossingResult(i, c.Index, PartToughness.KineticCoupling * lost / joules, true,
                                            CrossingOutcome.Through, 0.0));
                v = left;
            }
            else if (c.Wall.Known)
            {
                bool through = c.Judged ?? c.Wall.Perforates(energy, diameterMetres, c.ThicknessLeftMetres);
                if (!through)
                {
                    into.Add(new CrossingResult(i, c.Index, PartToughness.KineticCoupling * energy / joules, false,
                                                CrossingOutcome.Stopped, c.Wall.DentMetres(energy, diameterMetres)));
                    return 0.0;
                }

                // Through a tank, the liquid takes the energy and does the damage: a dry tank is only holed.
                double taken = ram?.Invoke(i, v) ?? 0.0;
                into.Add(new CrossingResult(i, c.Index, 0.0, true, CrossingOutcome.Through, 0.0));
                v *= Math.Sqrt(Math.Max(1.0 - taken, 0.0));
            }
            else
            {
                double kept = PartToughness.KineticCoupling * energy;
                into.Add(new CrossingResult(i, c.Index, kept / joules, true, CrossingOutcome.Through, 0.0));
                v *= Math.Sqrt(1.0 - PartToughness.KineticCoupling);
            }

            if (0.5 * massKg * v * v < leftoverJoules) return v;
        }

        return v;
    }
}
