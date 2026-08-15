namespace KSArmory;

/// <summary>How a round is told where to go.</summary>
public enum GuidanceMode
{
    /// <summary>
    /// The round finds the target itself, within a gimbal limit about its own flight path.
    /// Losing the target inside that cone stops it steering.
    /// </summary>
    Seeker,

    /// <summary>
    /// The round homes on a radar emission rather than on the airframe carrying it. Inside its
    /// gimbal limit it steers exactly like a <see cref="Seeker"/> — what differs is what it can
    /// see at all: a contact that is not radiating is not a target, however large or close.
    ///
    /// <para>So the counter is to stop transmitting, and the round answers that the way the real
    /// weapon does: it carries on to where the emission last came from. Shutting a set down
    /// therefore saves it only if it also <em>moves</em>, which is the whole tactical shape of
    /// the thing and the reason this is a guidance mode rather than a filter on target
    /// selection.</para>
    /// </summary>
    AntiRadiation,

    /// <summary>
    /// The launcher tracks the target and uplinks steering commands — the round carries no
    /// seeker. It therefore cannot be blinded by a hard-manoeuvring target, and its gimbal
    /// limit is irrelevant; what breaks the engagement is the *launcher* losing the track.
    /// This is how the 57E6 and most short-range point-defence rounds actually work.
    /// </summary>
    CommandLink,

    /// <summary>
    /// The round does not steer at all — a bomb, or an unguided rocket. It leaves the tube and
    /// follows its ballistics from there, so there is nothing to lose lock on and no gimbal limit
    /// to release it outside of.
    /// </summary>
    None,
}

/// <summary>
/// Everything that makes one round behave differently from another: how it burns, how it
/// steers, how far it can see, and what it does when it gets there.
///
/// <para>A second missile type is a second instance of this — no new class, no branch in
/// <see cref="Interceptor"/>. <see cref="Interceptor"/> is the flight model; this is the round
/// flying it.</para>
///
/// <para>Fields rather than properties, and mutable, because the panel edits them live by
/// reference while an engagement is in progress. That is how the tuning sliders work.</para>
/// </summary>
public sealed class MunitionProfile
{
    /// <summary>Registry key. Referenced by <see cref="LauncherProfile.Munition"/>.</summary>
    public required string Name { get; init; }

    /// <summary>Shown in the panel.</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Subpart marker for this round's body mesh, matched against the launcher's subpart Ids.
    /// Null means the round has no model and draws as a tracer only.
    /// </summary>
    public string? BodyMarker { get; init; }

    /// <summary>
    /// Subpart marker for this round's fin set, matched the same way as <see cref="BodyMarker"/>.
    /// Null means the round has no separate fins, and nothing is animated.
    /// </summary>
    public string? FinMarker { get; init; }

    // ---- Boost ----------------------------------------------------------
    /// <summary>
    /// Length of the round's body mesh (m). The mesh is modelled about its centre — see
    /// build_missile in tools/model/pantsir.py — so a round placed at a tube mouth sits half
    /// out of it. This is what lets the mod seat it properly instead.
    /// </summary>
    public float BodyLength = 3.10f;

    /// <summary>
    /// Seconds the fins take to snap from stowed to full span after launch.
    ///
    /// <para>A flick, not a hinge easing open.</para>
    /// </summary>
    public float FinDeploySeconds = 0.18f;

    /// <summary>Fin span while stowed, as a fraction of full. Small enough to clear the bore.</summary>
    public float FinStowedScale = 0.06f;

    /// <summary>Speed the round leaves the rail at, relative to the platform (m/s).</summary>
    public float LaunchSpeed = 45f;

    /// <summary>Seconds of powered flight after launch, in the first stage.</summary>
    public float BoostSeconds = 2.4f;

    /// <summary>Axial acceleration during that stage (m/s^2).</summary>
    public float BoostAccel = 520f;

    /// <summary>
    /// Stages after the first, burned in order.
    /// </summary>
    ///
    /// <remarks>
    /// Empty for a single-stage round, which is every round the mod ships. Two <i>powered</i>
    /// stages are genuinely different accelerations for different durations, and averaging them
    /// into one gets the burnout speed roughly right and the trajectory wrong. The 57E6 is not
    /// that case: its second stage carries no motor, so a hard burn and then a coast is the round
    /// it actually is.
    ///
    /// Kept separate from <see cref="BoostSeconds"/> rather than folding the first stage in, so
    /// every profile that does not care reads exactly as it did.
    /// </remarks>
    public BoostStage[] Stages = [];

    /// <summary>Total seconds of powered flight, across every stage.</summary>
    public float TotalBoostSeconds
    {
        get
        {
            float total = BoostSeconds;
            for (int i = 0; i < Stages.Length; i++) total += Stages[i].Seconds;
            return total;
        }
    }

    /// <summary>
    /// Axial acceleration at <paramref name="age"/> seconds after launch, zero once burnt out.
    /// </summary>
    public float BoostAccelAt(double age)
    {
        if (age <= BoostSeconds) return age < 0.0 ? 0f : BoostAccel;

        double from = BoostSeconds;
        for (int i = 0; i < Stages.Length; i++)
        {
            from += Stages[i].Seconds;
            if (age <= from) return Stages[i].Accel;
        }

        return 0f;
    }

    /// <summary>Round self-destructs this long after launch.</summary>
    public float MaxFlightSeconds = 30f;

    /// <summary>
    /// Longest simulation step this round can be integrated across at full fidelity, in seconds.
    /// </summary>
    ///
    /// <remarks>
    /// Per round, because the limit is a property of how hard it manoeuvres rather than of how
    /// fast it goes. The fuse is an analytic closest-approach solve over each sub-step, so a round
    /// cannot tunnel through its own fuse radius at any speed; what a long step drops is the
    /// curvature, and that error is about half the lateral acceleration times the step squared.
    /// A 35 g endgame round at 0.32 s loses roughly its own fuse radius, which is why 0.32 is the
    /// default. A round coasting ballistically loses centimetres.
    ///
    /// It matters because the world is slowed to keep this step, so a weapon that flies for
    /// minutes holds the player's timewarp down for all of them and eventually trips the policy's
    /// own abandon guard. Raising it for a round that does not manoeuvre costs nothing and is what
    /// makes a long-range weapon playable.
    ///
    /// The step at which a real intercept starts to degrade is unmeasured. 0.32 s is the value the
    /// shipped rounds fly at, so treat it as a default to keep rather than a licence to raise it.
    /// </remarks>
    public float MaxFaithfulStepSeconds = (float)Interceptor.MaxFaithfulStep;

    /// <summary>
    /// How far this round can usefully be sent, in metres.
    ///
    /// <para>On the round rather than on the set that finds the target or the launcher that throws
    /// it: reach is a property of what is flying. On <see cref="SensorProfile"/> it would have a
    /// gun-only mount describing its cannon's reach as its radar's, and the CIWS carrying the same
    /// number in two files. Detection range is still the sensor's and is a different question: a
    /// set that sees 36 km feeding a round that flies 20 is the normal case.</para>
    /// </summary>
    public float MinRange;

    /// <inheritdoc cref="MinRange"/>
    public float MaxRange = 20000f;

    // ---- Guidance -------------------------------------------------------
    /// <summary>Proportional-navigation constant. 3-5 is the classic range.</summary>
    public float NavConstant = 4f;

    /// <summary>Lateral acceleration limit (g). Airframes cap out; ours does too.</summary>
    public float MaxLateralG = 35f;

    /// <summary>
    /// How the round is steered. <see cref="GuidanceMode.CommandLink"/> ignores
    /// <see cref="SeekerFovDeg"/> entirely.
    /// </summary>
    public GuidanceMode Guidance = GuidanceMode.CommandLink;

    /// <summary>Seeker gimbal limit, half-angle off the round's velocity vector (degrees).</summary>
    public float SeekerFovDeg = 55f;

    /// <summary>
    /// Seconds after launch during which the round does not steer at all.
    ///
    /// <para>Separation. A round that starts guiding on its first sub-step turns immediately, and
    /// for a rail bolted to the side of a craft that means turning into the craft. Coasting clear
    /// first is also what makes the turn onto the target read as an arc rather than a kink at the
    /// muzzle.</para>
    /// </summary>
    public float SeparationSeconds;

    /// <summary>Fraction of local gravity the autopilot compensates for.</summary>
    public float GravityCompensation = 1f;

    /// <summary>
    /// Medium density ratio at which this round is neutrally buoyant, in the same units as
    /// <see cref="DragK"/> is scaled by — multiples of sea-level air. Zero disables buoyancy.
    ///
    /// <para>A torpedo sits near 840, the density of water, so it neither sinks nor rises once
    /// submerged while still falling normally through air. Gravity is scaled by
    /// <c>1 - medium / this</c>, so a round denser than its medium still sinks and a lighter one
    /// rises.</para>
    /// </summary>
    public float NeutralDensityRatio;

    /// <summary>
    /// Quadratic drag coefficient, k in <c>a = -k*|v|*v</c>, <b>at sea level</b>.
    ///
    /// <para>Scaled at runtime by the density where the round is, so one profile is correct on the
    /// pad, climbing out and in orbit. Zero disables drag outright.</para>
    /// </summary>
    public float DragK = 3.0e-5f;

    // ---- Warhead --------------------------------------------------------
    /// <summary>Proximity fuse trigger radius (m).</summary>
    public float FuseRadius = 15f;

    /// <summary>
    /// Burst at a set time of flight rather than only on proximity — flak.
    ///
    /// <para>The time is not here: it belongs to the shot, not to the munition, because it is the
    /// flight time to where the target is going and that differs every trigger pull. The gun sets
    /// it from the same lead solution it aimed with.</para>
    ///
    /// <para>The proximity fuse still runs. A shell that meets something on the way should not
    /// sail through it waiting for a clock.</para>
    /// </summary>
    public bool TimedFuse;

    /// <summary>Fuse stays safe for this long after launch, so a round cannot kill its own
    /// platform.</summary>
    public float FuseArmSeconds = 0.6f;

    /// <summary>
    /// Explosive charge (kg). <b>This is the warhead</b> — the radii below are read off it.
    ///
    /// <para>One figure rather than three, because three independent radii can describe a warhead
    /// whose lethal radius exceeds its blast radius, and because a round's reach is not a free
    /// choice: it follows from what it carries. <see cref="Warhead"/> has the scaling.</para>
    /// </summary>
    public float ChargeKg = 20f;

    /// <summary>
    /// Whether the ground stops this round.
    ///
    /// <para>Off for everything that flies at aircraft, which is why a shell passes through a hill
    /// and a missile that misses carries on into space. That is cheap and, for a weapon aimed
    /// upwards, invisible. A bomb has nothing else to arrive at, so it is the one round for which
    /// the terrain is the whole point.</para>
    ///
    /// <para>It costs a terrain sample per round per frame, which is why it is opt-in rather than
    /// how every round behaves: a CIWS burst is 150 shells in the air and a rack holds one bomb.</para>
    /// </summary>
    public bool HitsTerrain;

    /// <summary>Radius inside which a detonation is unconditionally lethal (m).</summary>
    public float LethalRadius => (float)Warhead.LethalRadius(ChargeKg);

    /// <summary>Radius at which blast effect falls to zero (m).</summary>
    public float BlastRadius => (float)Warhead.BlastRadius(ChargeKg);

    /// <summary>Roughly how big the burst should look (m).</summary>
    public float FireballRadius => (float)Warhead.FireballRadius(ChargeKg);

    public float SeekerFovRad => float.DegreesToRadians(SeekerFovDeg);
    public double MaxLateralAccel => MaxLateralG * 9.80665;
}

/// <summary>
/// One burn in a multi-stage round: how long it lasts and how hard it pushes.
///
/// <para>A separate type rather than two parallel arrays, because a stage whose duration and
/// acceleration can be indexed apart is a stage that can be half-edited.</para>
/// </summary>
public readonly record struct BoostStage(float Seconds, float Accel);
