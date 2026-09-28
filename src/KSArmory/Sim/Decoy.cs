using Brutal.Numerics;

namespace KSArmory;

public enum DecoyKind
{
    Flare,
    Chaff,
}

/// <summary>
/// One kind of expendable: what it fools, how bright it gets and for how long, and how hard the air
/// stops it.
/// </summary>
public sealed class DecoyProfile
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required DecoyKind Kind { get; init; }

    /// <summary>Peak signature — kW/sr for a flare, <see cref="RadarSignature"/>'s m² for chaff.</summary>
    public float PeakSignature;

    /// <summary>Seconds from ejection to peak: a flare ignites at once, a chaff cloud has to bloom.</summary>
    public float RiseSeconds;

    /// <summary>Seconds until the signature has decayed to nothing, which is when the decoy is gone.</summary>
    public float LifeSeconds = 4f;

    /// <summary>
    /// Quadratic drag at sea-level air, per metre: deceleration is this times speed squared.
    /// <c>g / k</c> is the square of the speed it settles to falling.
    /// </summary>
    public float DragPerMetre;

    public SeekerBand Fools => Kind == DecoyKind.Flare ? SeekerBand.Infrared : SeekerBand.Radar;

    /// <summary>Signature at this age: a linear rise to the peak, then a linear decay to nothing at the end of its life.</summary>
    public double SignatureAt(double age)
    {
        if (!(age >= 0.0) || age >= LifeSeconds) return 0.0;
        if (age < RiseSeconds) return PeakSignature * (age / RiseSeconds);

        double fade = LifeSeconds - RiseSeconds;
        return fade > 0.0 ? PeakSignature * (1.0 - ((age - RiseSeconds) / fade)) : 0.0;
    }
}

/// <summary>
/// A flare or a chaff cloud in the air. Not a round: nothing aims it, it fuses on nothing and no
/// radar tracks it, so it is a point with a signature that falls through the air until it is spent.
/// </summary>
public sealed class Decoy
{
    /// <summary>Longest sub-step, in seconds. Chaff stops from aircraft speed in tens of milliseconds.</summary>
    public const double SubStep = 0.02;

    private static int _nextId;

    public Decoy(DecoyProfile profile, double3 positionEcl, double3 velocityEcl, object? droppedBy)
    {
        Profile = profile;
        PositionEcl = positionEcl;
        VelocityEcl = velocityEcl;
        DroppedBy = droppedBy;
        Id = Interlocked.Increment(ref _nextId);
    }

    public DecoyProfile Profile { get; }
    public int Id { get; }

    /// <summary>The craft that dispensed it.</summary>
    public object? DroppedBy { get; }

    /// <summary>The body it falls through — the game's, held opaquely because nothing here reads it.</summary>
    public object? Body { get; init; }

    public double3 PositionEcl { get; private set; }
    public double3 VelocityEcl { get; private set; }
    public double Age { get; private set; }

    /// <summary>Whether it has come down on the ground, where it lies and burns out.</summary>
    public bool Landed { get; private set; }

    /// <summary>Puts it on the ground at <paramref name="groundEcl"/>, moving with the ground there.</summary>
    public void Land(double3 groundEcl, double3 groundVelocityEcl)
    {
        PositionEcl = groundEcl;
        VelocityEcl = groundVelocityEcl;
        Landed = true;
    }

    public double Signature => Profile.SignatureAt(Age);
    public bool Spent => Age >= Profile.LifeSeconds;

    /// <summary>
    /// Advances it one frame through air moving at <paramref name="airVelocityEcl"/>.
    ///
    /// <para>Semi-implicit in the drag, because chaff's is stiff: an explicit step at 20 ms throws it
    /// backwards through the air it is stopping in. Drag acts on the velocity relative to the air, so
    /// adding the same velocity to both changes nothing.</para>
    /// </summary>
    public void Step(double dt, double3 gravity, double3 airVelocityEcl, double densityRatio)
    {
        if (!double.IsFinite(dt) || dt <= 0.0) return;

        if (Landed)
        {
            PositionEcl += airVelocityEcl * dt;
            VelocityEcl = airVelocityEcl;
            Age += dt;
            return;
        }

        double k = Profile.DragPerMetre * (double.IsFinite(densityRatio) ? Math.Max(0.0, densityRatio) : 0.0);
        int steps = Math.Max(1, (int)Math.Ceiling(dt / SubStep));
        double h = dt / steps;

        double3 relative = VelocityEcl - airVelocityEcl;
        double3 position = PositionEcl;

        for (int i = 0; i < steps; i++)
        {
            double3 before = relative;
            relative = (relative + (gravity * h)) / (1.0 + (k * Vec.Len(relative) * h));
            position += (airVelocityEcl + ((before + relative) * 0.5)) * h;
        }

        PositionEcl = position;
        VelocityEcl = airVelocityEcl + relative;
        Age += dt;
    }
}
