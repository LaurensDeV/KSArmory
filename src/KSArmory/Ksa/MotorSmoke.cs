using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The smoke trail a burning round leaves behind it, through the same volumetric renderer the
/// mushroom clouds use.
///
/// <para>A moving emitter lays a chain, so one cursor draws a round's whole trail.</para>
///
/// <para><b>Nothing is pooled and nothing is released.</b> A <see cref="PlumeSmoke.Strand"/> is a
/// state object, and skipping a frame is how a chain is ended: the engine's tracker sees a gap in the
/// frame numbers and closes the open segment itself.</para>
///
/// <para>The engine's limits: a segment lives <b>1200 s</b>, a global setting shared with every
/// booster; segments are capped at <b>16,384 per celestial body</b>, evicted oldest-first and shared
/// with <see cref="NuclearClouds"/>; and only the camera's nearby body is drawn, with or without
/// air.</para>
/// </summary>
internal sealed class MotorSmoke
{
    private sealed class Live
    {
        public required PlumeSmoke.Strand Strand { get; init; }
        public required IEffectSource Owner { get; set; }
    }

    private readonly Dictionary<IProjectile, Live> _laying = [];
    private readonly List<IProjectile> _finished = [];

    private readonly Config _config;

    public MotorSmoke(Config config) => _config = config;

    /// <summary>Lays this frame's smoke for every round this system has burning.</summary>
    public void Update(IEffectSource system)
    {
        ArgumentNullException.ThrowIfNull(system);

        if (!_config.MotorSmoke || !PlumeSmoke.Available || system.EffectBody is not { } body)
        {
            ForgetOwnedBy(system);
            return;
        }

        double3 centre = KsaWorld.PositionEcl(body);
        if (!Vec.IsFinite(centre)) return;

        foreach (IProjectile round in system.Rounds)
        {
            if (Burning(round)) Lay(round, system, body, centre);
            else _finished.Add(round);
        }

        // A round reaped mid-burn never reaches the branch above and would keep its entry.
        foreach (KeyValuePair<IProjectile, Live> kv in _laying)
        {
            if (!ReferenceEquals(kv.Value.Owner, system)) continue;
            if (!system.Rounds.Contains(kv.Key)) _finished.Add(kv.Key);
        }

        foreach (IProjectile round in _finished) _laying.Remove(round);
        _finished.Clear();
    }

    /// <summary>Forgets any system the roster no longer knows, loose ones included.</summary>
    public void Sweep(WeaponSystems roster)
    {
        ArgumentNullException.ThrowIfNull(roster);

        foreach (KeyValuePair<IProjectile, Live> kv in _laying)
        {
            if (!roster.Knows(kv.Value.Owner)) _finished.Add(kv.Key);
        }

        foreach (IProjectile round in _finished) _laying.Remove(round);
        _finished.Clear();
    }

    /// <summary>Drops every cursor. The smoke already laid stays where it is.</summary>
    public void Clear() => _laying.Clear();

    private static bool Burning(IProjectile round)
        => round.State == RoundState.Flying
           && round.Munition.TotalBoostSeconds > 0f
           && round.MotorBurning;

    private void Lay(IProjectile round, IEffectSource system, Celestial body, double3 centre)
    {
        // Nothing until the nozzle has cleared the tube: the round is seated with its centre on the
        // launch anchor, so that is a whole body length of travel. Travel rather than age, so it
        // holds for a slow exit and for a store dropped before its motor lights.
        double clearance = round.Munition.BodyLength;
        if (Vec.Len2(round.TravelSinceLaunch) < clearance * clearance) return;

        if (!system.TryRoundEffectEcl(round, out double3 ecl)) return;

        // At the nozzle, half a body behind the centre, as the plume is.
        double3 along = Vec.Unit(round.VelocityLocal);
        if (Vec.Len2(along) > 0.5) ecl -= along * (round.Munition.BodyLength * 0.5);

        double3 positionCcf = (ecl - centre).Transform(body.GetCce2Ccf());
        if (!Vec.IsFinite(positionCcf)) return;

        if (!_laying.TryGetValue(round, out Live? live))
        {
            live = new Live { Strand = new PlumeSmoke.Strand(), Owner = system };
            _laying[round] = live;
        }
        else
        {
            live.Owner = system;
        }

        // Fractions of BodyLength, a length, because the trail wants the calibre. The expanded
        // radius is reached within PlumeSmoke's 5 s swell, so it is the trail's look for most of its life.
        double width = round.Munition.BodyLength * _config.MotorSmokeWidth;

        float laid = (float)(width * 0.08);
        float expanded = (float)(width * 0.7);

        PlumeSmoke.Lay(live.Strand, body, positionCcf, (-along).Transform(body.GetCce2Ccf()), laid, expanded);
    }

    private void ForgetOwnedBy(IEffectSource system)
    {
        foreach (KeyValuePair<IProjectile, Live> kv in _laying)
        {
            if (ReferenceEquals(kv.Value.Owner, system)) _finished.Add(kv.Key);
        }

        foreach (IProjectile round in _finished) _laying.Remove(round);
        _finished.Clear();
    }
}
