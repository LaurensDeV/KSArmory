using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// One countermeasures dispenser part: what it is loaded with and how it throws it.
///
/// <para>One kind per part, as a real magazine section is loaded, so the loadout is chosen in the
/// editor by which parts are fitted rather than by a setting that would have to be locked in flight.</para>
/// </summary>
public sealed class DispenserProfile
{
    public required string PartId { get; init; }
    public required string DisplayName { get; init; }
    public required DecoyKind Kind { get; init; }

    /// <summary>The <see cref="DecoyProfile.Name"/> it is loaded with.</summary>
    public required string Decoy { get; init; }

    public int Count = 30;

    /// <summary>Cartridges one press throws.</summary>
    public int SalvoSize = 2;

    /// <summary>Seconds between cartridges within a salvo.</summary>
    public float SalvoIntervalSeconds = 0.25f;

    /// <summary>Which way a cartridge leaves, in the part's frame: +X is out of the mounting face.</summary>
    public double3 EjectDirection = new(1, 0, 0);

    /// <summary>How fast a cartridge leaves the part, in m/s.</summary>
    public float EjectSpeed = 30f;
}

/// <summary>
/// One dispenser's magazine and the salvo it is working through.
///
/// <para>A press queues a salvo rather than emptying at once, and the queue runs on simulated time,
/// so a salvo is spaced the same at any frame rate and waits through a pause.</para>
/// </summary>
public sealed class Dispenser
{
    private int _queued;
    private double _untilNext;

    public Dispenser(DispenserProfile profile)
    {
        Profile = profile;
        Remaining = profile.Count;
    }

    public DispenserProfile Profile { get; }
    public DecoyKind Kind => Profile.Kind;
    public int Remaining { get; private set; }

    /// <summary>Cartridges queued and not yet thrown.</summary>
    public int Queued => _queued;

    public bool Busy => _queued > 0;

    /// <summary>Queues up to <paramref name="count"/> more, as many as are left; returns how many it took.</summary>
    public int Queue(int count)
    {
        int took = Math.Clamp(Remaining - _queued, 0, Math.Max(0, count));
        _queued += took;
        return took;
    }

    /// <summary>Runs the salvo on by <paramref name="dt"/>; returns how many cartridges leave this frame.</summary>
    public int Update(double dt)
    {
        if (!Busy)
        {
            _untilNext = 0.0;
            return 0;
        }

        if (!double.IsFinite(dt) || dt < 0.0) return 0;

        int thrown = 0;
        _untilNext -= dt;
        while (_untilNext <= 0.0 && Busy)
        {
            _queued--;
            Remaining--;
            thrown++;
            _untilNext += Math.Max(0.01, Profile.SalvoIntervalSeconds);
        }

        return thrown;
    }
}

/// <summary>
/// A craft's dispensers answering as one: a press of <b>Flares</b> is a salvo from <em>every</em> flare
/// dispenser aboard with any left, together.
///
/// <para>Together rather than from the fullest, because that is how a symmetric fit is used: a pair
/// either side of the craft puts its decoys either side of it, where one side at a time pulls a
/// seeker off to that side only. It also means fitting more dispensers makes each press heavier, which
/// is what a real installation's programme does with the dispensers it has.</para>
/// </summary>
public static class DispenseOrder
{
    /// <summary>Queues one salvo of <paramref name="kind"/> on every dispenser of that kind aboard; returns how many were queued.</summary>
    public static int Request(IReadOnlyList<Dispenser> onCraft, DecoyKind kind)
    {
        int queued = 0;
        for (int i = 0; i < onCraft.Count; i++)
        {
            Dispenser d = onCraft[i];
            if (d.Kind == kind) queued += d.Queue(Math.Max(1, d.Profile.SalvoSize));
        }

        return queued;
    }

    /// <summary>What a craft still carries of one kind, across every dispenser aboard.</summary>
    public static (int Remaining, int Capacity) Load(IReadOnlyList<Dispenser> onCraft, DecoyKind kind)
    {
        int left = 0, of = 0;
        for (int i = 0; i < onCraft.Count; i++)
        {
            if (onCraft[i].Kind != kind) continue;
            left += onCraft[i].Remaining;
            of += onCraft[i].Profile.Count;
        }

        return (left, of);
    }
}

/// <summary>A missile coming for a craft, as its warning receiver sees it.</summary>
public readonly record struct IncomingMissile(double3 PositionEcl, double3 VelocityEcl, SeekerBand? Band);

/// <summary>
/// When a dispenser left to itself throws, and what.
///
/// <para>A heat seeker gets flares and a radar one chaff; anything the receiver cannot place gets both.
/// Rate-limited, because a warning that holds for ten seconds is one threat and not six hundred.</para>
/// </summary>
public sealed class AutoDispense
{
    /// <summary>How far out a closing missile is answered, in metres.</summary>
    public const double WarningRangeMetres = 6000.0;

    /// <summary>Seconds between salvos while the warning holds.</summary>
    public const double RetriggerSeconds = 1.0;

    private double _sinceLast = double.PositiveInfinity;

    /// <summary>
    /// Whether a round is one a warning receiver answers: a guided missile in flight. A shell is not,
    /// whatever its profile's guidance field says — a gun's rounds are flown as <see cref="Slug"/>s and
    /// no decoy can move one.
    /// </summary>
    internal static bool Warns(IProjectile round)
        => round is Interceptor && round.State == RoundState.Flying && round.Munition.Steers;

    /// <summary>Decides this frame's salvo. Both false is none.</summary>
    public (bool Flares, bool Chaff) Decide(double dt, double3 craftPosEcl, double3 craftVelEcl,
                                            IReadOnlyList<IncomingMissile> incoming)
    {
        if (double.IsFinite(dt) && dt > 0.0) _sinceLast += dt;
        if (_sinceLast < RetriggerSeconds) return (false, false);

        bool flares = false, chaff = false;
        for (int i = 0; i < incoming.Count; i++)
        {
            IncomingMissile m = incoming[i];
            if (Vec.Len(m.PositionEcl - craftPosEcl) > WarningRangeMetres) continue;
            if (Signature.ClosingSpeed(craftPosEcl, craftVelEcl, m.PositionEcl, m.VelocityEcl) <= 0.0) continue;

            switch (m.Band)
            {
                case SeekerBand.Infrared: flares = true; break;
                case SeekerBand.Radar: chaff = true; break;
                default: flares = chaff = true; break;
            }
        }

        if (flares || chaff) _sinceLast = 0.0;
        return (flares, chaff);
    }
}
