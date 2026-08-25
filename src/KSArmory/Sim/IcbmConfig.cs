namespace KSArmory;

/// <summary>
/// One installation's own settings for its ICBM computer.
///
/// <para>Everything here can sensibly differ between two missiles in the same world, which is the
/// test <c>CLAUDE.md</c> applies: whether a shot is armed, how high it is lofted and how hard its
/// stack may be flown into the airflow all belong to the vehicle carrying the computer, not to the
/// session. What is <em>not</em> here is the target — that is a designation, and it lives with the
/// weapons system that will act on it.</para>
/// </summary>
internal sealed class IcbmConfig
{
    /// <summary>Nothing lights an engine until this is set. The whole safety interlock.</summary>
    public bool Armed;

    /// <summary>
    /// The most acceleration the stack may be flown at, in standard gravities. Zero is no limit.
    ///
    /// <para>A light upper stage on a full-sized motor climbs to many times its own weight in
    /// thrust as it empties, and a vehicle with a structural limit is destroyed at it. Throttling
    /// to hold the cap costs a little gravity loss and keeps the stack together.</para>
    ///
    /// <para>Against standard gravity rather than the local field: it is a limit on the airframe,
    /// so it is the number written on the airframe.</para>
    /// </summary>
    public float MaxAccelerationGee;

    /// <summary>
    /// Multiplies the flight time of the cheapest shot. One is minimum energy; above one is a
    /// lofted trajectory that arrives steeper and later, below one is a depressed one that arrives
    /// sooner and costs far more. Both ends run out: a shot flat enough to pass through the planet
    /// is refused rather than flown.
    ///
    /// <para><b>Not an arrival-angle control, and from orbit it can invert one.</b> Raising it
    /// makes leaving <em>now</em> dearer as well as making the arc taller, so
    /// <see cref="BurnWindow"/> re-optimises the departure under the new cost and can defer to a
    /// cheap flat window instead — measured at a 556 km shot going from 33.9 degrees at loft 1.0 to
    /// 6.2 at loft 1.8. <see cref="MinArrivalAngleDeg"/> is the control that asks for an arrival
    /// angle, and where the two disagree the floor wins.</para>
    /// </summary>
    public double Loft = 1.0;

    /// <summary>
    /// The shallowest the warheads may come in, in degrees below the local horizontal. Zero is off.
    ///
    /// <para><b>A bound on the search rather than a nudge to it.</b> Every arc the flight-time
    /// search considers has to satisfy this, and the window search takes the earliest departure
    /// whose cheapest satisfying arc is affordable — so waiting can no longer produce a shallower
    /// arrival than leaving now would have, which is what <see cref="Loft"/> could not promise.
    /// A shot with no arc steep enough is reported as such rather than flown flat.</para>
    ///
    /// <para><b>Off by default, and the default is what has been flown.</b> Every ballistic shot
    /// this mod has made arrived at about seven degrees, which is the worst arrival there is for
    /// precision: from 7.5 to 20 degrees the rms velocity sensitivity falls from 5,614 to 686
    /// metres per metre a second, and the impact's sensitivity to a ten per cent error in the drag
    /// model falls from 1,795 m to 29 — a factor of 62, and the one term no correction loop can
    /// remove, because its only observer shares the model. 15 to 20 degrees is where the trade
    /// turns; steeper buys tens of metres for kilometres a second of reach.
    /// <c>docs/ARRIVAL-ANGLE.md</c> is the account.</para>
    ///
    /// <para>What it costs is propellant and downrange, and from orbit those are the same thing:
    /// a 400 km platform reaches 6,379 km at 7.5 degrees for a 473 m/s brake, and 2,015 km at 20
    /// degrees for 2,576 — 86% of the stack arriving against 44%.</para>
    /// </summary>
    public double MinArrivalAngleDeg;

    /// <summary>Altitude at which the pitch programme starts turning away from vertical.</summary>
    public double TurnStartMetres = 800.0;

    /// <summary>Altitude by which the pitch programme has reached the horizon.</summary>
    public double TurnEndMetres = 55_000.0;

    /// <summary>
    /// How far the commanded thrust line may sit off the airflow while there is still air worth
    /// worrying about. Wide enough for guidance to work, narrow enough that the stack is never
    /// flown across its own slipstream.
    /// </summary>
    public double MaxAngleOfAttackDeg = 8.0;

    /// <summary>
    /// The dynamic pressure below which closed-loop guidance is allowed to steer freely. In pascals,
    /// so it means the same thing on a body with a thick atmosphere and on one with none.
    /// </summary>
    public double HandoverPressurePa = 1200.0;

    /// <summary>
    /// Clicking the world names the aim point.
    ///
    /// <para>A mode rather than a button, because a button cannot be used to point at anything:
    /// pressing one puts the cursor over the panel, so what it reads is whatever lies behind the
    /// control. Armed, it takes left clicks that are not on a window and not shift-held — the same
    /// gesture the burst tool and the mouse trigger use, and off by default for the same reason.
    /// </para>
    /// </summary>
    public bool DesignateByClicking = false;

    /// <summary>
    /// Fire the next sequence when there is nothing to burn with — which is the ignition on the
    /// pad, and the stage below being thrown away every time after that.
    /// </summary>
    public bool AutoStage = true;

    /// <summary>
    /// Warp the ballistic coast without being asked each time, up to the release point.
    ///
    /// <para><b>Off, and the default is the rule rather than an opinion about the feature.</b>
    /// Taking the world's clock away because a target happened to be designated is not a weapon's
    /// decision — so warping is an action, and the button beside this one is how it is taken. What
    /// this does is let an operator who wants it delegate the press: ticking it <em>is</em> the
    /// permission, given once instead of every shot.</para>
    ///
    /// <para>It stops where the button stops, a settling margin short of the release — see
    /// <see cref="IcbmProgram.SteadyBeforeReleaseSeconds"/>. It never overrides a warp the player
    /// started, and it asks for nothing while anything aboard is being integrated.</para>
    /// </summary>
    public bool WarpTheCoast = false;

    /// <summary>
    /// Let the warheads go by itself once the trajectory is good and the vehicle is high enough.
    ///
    /// <para>On by default, because the master arm above is already the interlock and a computer
    /// that flies the whole shot and then waits to be told the obvious is not delivering anything.
    /// It releases only from <see cref="IcbmPhase.Coast"/> above
    /// <see cref="DeployAltitudeMetres"/>, and never from a burn that ended short — a trajectory
    /// known to fall short would scatter them over whatever is under the short fall.</para>
    /// </summary>
    public bool AutoRelease = true;

    /// <summary>
    /// Turn the vehicle between releases so each tube in turn throws along the same line.
    ///
    /// <para>Tubes are canted — a MIRV bus's six sit six degrees off its own axis — so rounds
    /// released from one attitude leave on different vectors and scatter, and there is one aim for
    /// all of them. Measured in flight at about 1,200 m across six warheads.</para>
    ///
    /// <para><b>Off, and now for a flown reason rather than a suspected one.</b> Flown once it
    /// could actually latch its axes, a separated bus released its six tubes at 5.2, 2.1, 8.2,
    /// 12.8, 14.1 and 11.7 degrees off the line — against the six degrees of cant the turning
    /// exists to remove. It is not that the turn fails to help; it is that this vehicle cannot hold
    /// the attitude it is turned to, so commanding one leaves the tube further off the line than
    /// leaving it alone does.</para>
    ///
    /// <para>The machinery around it is worth keeping and is not the problem: the salvo no longer
    /// takes three minutes, the release is budgeted in one currency, and the give-up paths name
    /// which failure it is. What is missing is a bus that can hold an offset — more attitude
    /// authority, or a turn small enough to be held.</para>
    ///
    /// <para>Free for a launcher it does not describe: a single tube is the mean of its own axes, so
    /// nothing is asked to turn.</para>
    /// </summary>
    public bool RepointBetweenReleases;

    /// <summary>
    /// Put the bus back on its solution with its own thrusters before letting anything go.
    ///
    /// <para>The burn ends exact and two things then move the vehicle off it: whatever the cutoff
    /// left, and the decoupler that drops the spent stack — about a metre a second, arriving after
    /// the last thing that could have compensated for it. Measured in flight as 3.5 km between the
    /// one warhead that left before the split and the five that left after it.</para>
    ///
    /// <para>On by default, and free for a vehicle it does not describe: a launcher with no
    /// decoupler and a clean cutoff has nothing to trim, so <see cref="BusTrim"/> finds nothing to
    /// gain and stands aside. It costs release time on a bus whose thrusters are weak, which is
    /// what the trim's own budget is for.</para>
    /// </summary>
    public bool TrimBeforeRelease = true;

    /// <summary>
    /// Keep a mark on the designated target, with the time to impact beside it.
    ///
    /// <para>Separate from the trajectory, and on by default, because it answers a different
    /// question. The arc is diagnostic — it says whether the burn is finished. The mark says where
    /// the warheads are going and when they get there, which is the thing worth having on screen
    /// whatever else is being looked at.</para>
    /// </summary>
    public bool MarkTarget = true;

    /// <summary>
    /// Draw the arc this vehicle is currently on.
    ///
    /// <para>Per installation rather than session-wide, because it is the missile being flown whose
    /// trajectory is worth seeing, and four arcs across the sky is not four times as useful as
    /// one.</para>
    /// </summary>
    public bool DrawTrajectory = true;

    /// <summary>
    /// Altitude above which the post-boost vehicle is willing to let its warheads go. Deployment
    /// itself belongs to fire control; this only says when the trajectory is far enough along for it
    /// to be sensible.
    /// </summary>
    public double DeployAltitudeMetres = 100_000.0;

    /// <summary>
    /// How close to arrival the warheads are let go, in seconds. Zero releases as soon as the
    /// altitude allows.
    ///
    /// <para><b>An altitude alone is the wrong shape.</b> A hundred kilometres is satisfied on the
    /// way up as well as the way down, and the ascent crossing wins — so the warheads leave near
    /// the start of a half-hour coast, and every metre per second the separation kick gives them
    /// has the whole flight to grow into a miss. Holding them until the arrival is close shrinks
    /// that in proportion to the time saved, and leaves the trim and the aim correction converging
    /// for longer.</para>
    ///
    /// <para>Not arbitrarily late, though. Six have to clear each other and the bus, their fuses
    /// have to arm, and the sequence itself takes a few seconds a round — and the bus is not a
    /// reentry vehicle, so a release inside the air breaks it up among its own warheads. Minutes,
    /// not seconds.</para>
    /// </summary>
    public double ReleaseBeforeArrivalSeconds = 420.0;

    /// <summary>
    /// How much the bus may spend on trimming across the whole flight, in metres per second of
    /// attitude-control propellant. Zero is no budget at all; negative is unlimited.
    ///
    /// <para>One trim run is already bounded, but the bus is asked to trim again at every release
    /// and the coast between them can be half an hour. Without a total, a vehicle that keeps
    /// finding a small correction worth making spends the tanks on corrections worth metres and
    /// arrives with nothing left for the one worth kilometres.</para>
    ///
    /// <para>Spent on the shot rather than per correction, so an early runaway is paid for by the
    /// later ones going without — which is the right way round: the corrections that matter most
    /// are the ones nearest arrival.</para>
    /// </summary>
    public double TrimBudgetMetresPerSecond = 25.0;
}
