using Brutal.Numerics;

namespace KSArmory;

/// <summary>What a seeker is steering on this frame.</summary>
public enum SeekerPick
{
    Target,
    Decoy,

    /// <summary>A decoy took it and burnt out, and this seeker does not look again.</summary>
    Lost,
}

/// <summary>
/// One seeker's choice between its target and the decoys around it, held across frames.
///
/// <para>Each decoy is judged <b>once</b>, on the first frame it outshines the target: a chance
/// rolled every frame would make a flare's success a function of the frame rate. What takes the
/// seeker is then held until it is spent, which is how a real seeker's track gate behaves — it
/// follows the brightest return it has, and does not keep second-guessing it.</para>
///
/// <para>A radar seeker holds its target in a range gate, so only chaff inside the target's resolution
/// cell can take it; a heat seeker has no range to gate on and sees every flare in its field.</para>
/// </summary>
internal sealed class SeekerLock
{
    /// <summary>How many times brighter than the target a decoy must be to be in the running.</summary>
    public const double Margin = 1.0;

    /// <summary>The chance a decoy outshining the target takes a seeker with no resistance at all.</summary>
    public const double SeductionChance = 0.8;

    private readonly Random _random;
    private readonly HashSet<int> _judged = [];
    private bool _lost;

    public SeekerLock(Random random) => _random = random;

    /// <summary>The decoy it is on, or null while it is on its target.</summary>
    public Decoy? OnDecoy { get; private set; }

    /// <summary>
    /// Chooses what to steer on this frame.
    ///
    /// <para>The target and the decoys are end-of-step samples and the seeker is at the step's start,
    /// so both are put back by <paramref name="frameSeconds"/> of their own velocity before any line of
    /// sight is drawn — the same back-dating the round applies to whatever it is handed.</para>
    /// </summary>
    public SeekerPick Choose(MunitionProfile munition, double3 seekerPosEcl, double3 seekerVelEcl,
                             double3 seekerLocalVel, TargetState target, double targetSignature,
                             IReadOnlyList<Decoy> decoys, double frameSeconds)
    {
        if (!munition.Seducible) return SeekerPick.Target;

        if (OnDecoy is { } held)
        {
            if (!held.Spent && held.Signature > 0.0) return SeekerPick.Decoy;

            OnDecoy = null;
            if (!munition.ReacquiresAfterDecoy) _lost = true;
        }

        if (_lost) return SeekerPick.Lost;

        double3 targetAt = target.PositionEcl - (target.VelocityEcl * frameSeconds);
        double targetStrength = StrengthOf(munition, seekerPosEcl, seekerLocalVel, targetAt, targetSignature);
        double targetClosing = Signature.ClosingSpeed(seekerPosEcl, seekerVelEcl, targetAt, target.VelocityEcl);

        Decoy? taken = null;
        double best = targetStrength * Margin;

        for (int i = 0; i < decoys.Count; i++)
        {
            Decoy d = decoys[i];
            if (d.Profile.Fools != munition.Band || _judged.Contains(d.Id)) continue;

            double3 at = d.PositionEcl - (d.VelocityEcl * frameSeconds);
            if (munition.Band == SeekerBand.Radar && Vec.Len(at - targetAt) > ChaffNotch.CellMetres) continue;

            double strength = StrengthOf(munition, seekerPosEcl, seekerLocalVel, at, d.Signature);
            if (strength <= best) continue;

            if (munition.DopplerGateMps > 0f)
            {
                double closing = Signature.ClosingSpeed(seekerPosEcl, seekerVelEcl, at, d.VelocityEcl);
                if (Math.Abs(closing - targetClosing) > munition.DopplerGateMps) continue;
            }

            _judged.Add(d.Id);
            if (_random.NextDouble() < SeductionChance * (1.0 - munition.CountermeasureResistance))
            {
                taken = d;
                best = strength;
            }
        }

        if (taken is null) return SeekerPick.Target;

        OnDecoy = taken;
        return SeekerPick.Decoy;
    }

    /// <summary>What the round is handed to steer and fuse on, for the pick <see cref="Choose"/> made.</summary>
    public TargetState? Steer(SeekerPick pick, TargetState target) => pick switch
    {
        SeekerPick.Target => target,
        SeekerPick.Decoy when OnDecoy is { } d => new TargetState(d.PositionEcl, d.VelocityEcl, 0.0, d),
        _ => null,
    };

    private static double StrengthOf(MunitionProfile munition, double3 seekerPosEcl, double3 seekerLocalVel,
                                     double3 positionEcl, double signature)
    {
        double3 line = positionEcl - seekerPosEcl;
        return Signature.LockStrength(munition.Band, signature, Vec.Len(line),
                                      Vec.AngleBetween(line, seekerLocalVel), munition.SeekerFovRad);
    }
}
