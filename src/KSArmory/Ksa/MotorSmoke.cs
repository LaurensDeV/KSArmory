using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The smoke trail a burning round leaves behind it, through the same volumetric renderer the
/// mushroom clouds use.
///
/// <para>A missile is what that renderer actually wants. Its own note says a <em>stationary</em>
/// emitter lays one dragged segment rather than a chain, which is why a cloud has to fake movement
/// with forty-five cursors tracing circles; a round in flight simply is the moving point, and one
/// cursor draws its whole trail.</para>
///
/// <para><b>Nothing is pooled and nothing is released.</b> A <see cref="PlumeSmoke.Strand"/> is a
/// state object rather than a borrowed emitter, and skipping a frame is how a chain is ended: the
/// engine's tracker sees a gap in the frame numbers and closes the open segment itself. So a round
/// that stops burning, is reaped, or is shot down needs no unwinding — this simply stops submitting
/// for it. The segments already laid are the world's, not this class's.</para>
///
/// <para>Three limits, all the engine's and all read out of it rather than guessed:
/// a segment lives <b>1200 s</b> and expands over <b>5 s</b>, both global settings shared with
/// every booster in the world; segments are capped at <b>16,384 per celestial body</b> and evicted
/// oldest-first, which is a budget shared with <see cref="NuclearClouds"/>; and nothing is drawn on
/// an airless world or above the atmosphere, because the renderer only runs for the camera's nearby
/// atmospheric body.</para>
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
    public void Update(IEffectSource battery)
    {
        ArgumentNullException.ThrowIfNull(battery);

        if (!_config.MotorSmoke || !PlumeSmoke.Available || battery.EffectBody is not { } body)
        {
            // Not a release: dropping the entries is all there is to do, and the trail already in
            // the air goes on ageing out of its own accord.
            ForgetOwnedBy(battery);
            return;
        }

        double3 centre = KsaWorld.PositionEcl(body);
        if (!Vec.IsFinite(centre)) return;

        foreach (IProjectile round in battery.Rounds)
        {
            if (Burning(round)) Lay(round, battery, body, centre);
            else _finished.Add(round);
        }

        // A round reaped mid-burn never reaches the branch above and would keep its entry.
        foreach (KeyValuePair<IProjectile, Live> kv in _laying)
        {
            if (!ReferenceEquals(kv.Value.Owner, battery)) continue;
            if (!battery.Rounds.Contains(kv.Key)) _finished.Add(kv.Key);
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

    // While the motor burns, which is the whole of it: a solid rocket smokes because it is
    // burning, and the trail behind a coasting round is what it laid earlier rather than anything
    // it is still doing.
    private static bool Burning(IProjectile round)
        => round.State == RoundState.Flying
           && round.Munition.TotalBoostSeconds > 0f
           && round.Age <= round.Munition.TotalBoostSeconds;

    private void Lay(IProjectile round, IEffectSource battery, Celestial body, double3 centre)
    {
        if (!battery.TryRoundEffectEcl(round, out double3 ecl)) return;

        // Behind the nozzle, not at the round's centre: smoke laid at the middle of the body reads
        // as the missile dragging a column out of its own flank. The same half-length the plume
        // steps back by, and for the same reason.
        double3 along = Vec.Unit(round.VelocityLocal);
        if (Vec.Len2(along) > 0.5) ecl -= along * (round.Munition.BodyLength * 0.5);

        double3 positionCcf = (ecl - centre).Transform(body.GetCce2Ccf());
        if (!Vec.IsFinite(positionCcf)) return;

        if (!_laying.TryGetValue(round, out Live? live))
        {
            live = new Live { Strand = new PlumeSmoke.Strand(), Owner = battery };
            _laying[round] = live;
        }
        else
        {
            live.Owner = battery;
        }

        // Off the round's own size, so a 30 mm shell does not lay the column a HARM does. The
        // expanded radius is what makes a moving point read as a billowing trail rather than a
        // wire -- see PlumeSmoke.Lay.
        float laid = (float)(round.Munition.BodyLength * 0.25);
        float expanded = (float)(round.Munition.BodyLength * 3.0);

        PlumeSmoke.Lay(live.Strand, body, positionCcf, laid, expanded);
    }

    private void ForgetOwnedBy(IEffectSource battery)
    {
        foreach (KeyValuePair<IProjectile, Live> kv in _laying)
        {
            if (ReferenceEquals(kv.Value.Owner, battery)) _finished.Add(kv.Key);
        }

        foreach (IProjectile round in _finished) _laying.Remove(round);
        _finished.Clear();
    }
}
