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

    /// <summary>
    /// Whether the aim carries the bias the flown prediction asks for.
    ///
    /// <para>On, because a shallow arrival is tens of kilometres short of a solution that is
    /// otherwise perfect and only the correction closes it — headless at 7 degrees, 19.56 km
    /// uncorrected against 0.38.</para>
    ///
    /// <para><b>Under a floor it inverts, and the size of that is the reason this is reachable at
    /// all.</b> A constrained search is still walking the cutoff instant by minutes when the
    /// correction opens its first readings, and the loop reads that as a drag shortfall and spends
    /// kilometres removing it. Headless at a 15 degree floor over real relief: 0.018 km with this
    /// off against 8.52 km with it on. Not flown — see <c>docs/METRE-LEVEL.md</c> B1.</para>
    /// </summary>
    public bool CorrectAim = true;

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
    /// <para><b>On.</b> It was off on the principle that taking the world's clock away because a
    /// target happened to be designated is not a weapon's decision — and that principle survives in
    /// where it stops, not in the default. Nothing about this config is persisted, so "off" was not
    /// a setting an operator could make once: it was a tick box to find again on every launch, and
    /// forgetting it costs a ballistic coast in real time. Measured on two shots the same evening:
    /// 3.5 minutes of wall clock with it, seventeen without.</para>
    ///
    /// <para>The button beside it remains the way to take the action deliberately, and this still
    /// hands the world back a settling margin short of the release rather than running to it.</para>
    ///
    /// <para>It stops where the button stops, a settling margin short of the release — see
    /// <see cref="IcbmProgram.SteadyBeforeReleaseSeconds"/>. It never overrides a warp the player
    /// started, and it asks for nothing while anything aboard is being integrated.</para>
    /// </summary>
    public bool WarpTheCoast = true;

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
    ///
    /// <para><b>Defaulted to the reserve above it rather than to a number of its own.</b> There are
    /// two budgets and only one of them is derived: <see cref="PostBoostAim.MaxTrimMetresPerSecond"/>
    /// is sized against what a bus actually carries — 60 leaves one separation null on the smallest
    /// in the 70–90 range — while this one had a literal 25 and no account of where it came from. The undocumented
    /// one won, silently: flown at Mahia the trim spent all 25 and stopped <b>0.45 m/s</b> short of
    /// finishing a pass it had already committed to, on a bus with tens to spare. Tying the two
    /// together means the operator's lever moves the budget <em>down</em> from the real reserve, which
    /// is the only direction it was ever useful in.</para>
    /// </summary>
    public double TrimBudgetMetresPerSecond = PostBoostAim.MaxTrimMetresPerSecond;

    /// <summary>
    /// Size the trim's per-pass ceiling from what is left of the budget on the <em>first</em> pass
    /// too, rather than only once the aim has moved.
    ///
    /// <para>The ceiling asks how much one pass may spend, and it is
    /// <see cref="BusTrim.MaxMetresPerSecond"/> — ten — until <c>PostBoostAim.Cycles</c> is above
    /// zero. That guard asks whether the <em>aim</em> has moved when the question is whether the
    /// <em>bus</em> has separated: flown, 11 of 14 trims were already over ten with no wait at all,
    /// so the pass that matters most is refused before the loop that would have raised the ceiling
    /// has run once.</para>
    ///
    /// <para><b>It carries its own guard.</b> Widening the ceiling to the budget without one is a
    /// licence to spend the tank on a wind-up, so with this on the loop also refuses a demand that
    /// has grown half again since the previous pass — <see cref="PostCutoffSequence.IsRunaway"/>.
    /// Size was always the wrong question: a steep arrival asks 7–11 m/s where a shallow one asks
    /// 2.45, and asks once, while a runaway grows by an order of magnitude a pass.</para>
    ///
    /// <para><b>Off, and off is what ships.</b> It licenses a 10–20 m/s correction whose size
    /// tracks a disagreement about the arrival rather than a decoupler's shove, and whether that is
    /// the trim earning its propellant or chasing a stale arrival has not been flown.
    /// <c>docs/EIGHT-ROCKETS.md</c> item 1, and <c>docs/METRE-LEVEL.md</c> B1, where it is one of
    /// three things blocking the arrival angle that gets the miss under fifty metres.</para>
    /// </summary>
    public bool TrimCeilingFromBudget;

    /// <summary>
    /// Hold the aim correction to an aim the trim can actually fly it to.
    ///
    /// <para><see cref="AimCorrection.MaxMetres"/> is 300 km flat, and what the budget buys is
    /// 24 km on a 3,459 km shot and 113 km on a 12,902 km one — so the loop is licensed to walk
    /// somewhere the actuator can never follow. The flown symptom is a demand that exceeds whatever
    /// is left of the ceiling on every pass until the budget is gone, read until now as the solve
    /// diverging: it is not, it is an aim move being priced honestly.
    /// <see cref="AimAuthority"/> has the exchange rate.</para>
    ///
    /// <para>Nearer and flyable beats further and not, because the endings are not on one scale: a
    /// correction that ran to completion landed at 140 m and every other ending at 5 to 45 km.
    /// What it cannot do is make the shot want a nearer aim — if the correction genuinely needs
    /// 200 km, this clamps it and the shot still misses, with the propellant unspent rather than
    /// wasted.</para>
    ///
    /// <para><b>Off, and off is what ships.</b> Unflown.</para>
    /// </summary>
    public bool AimWithinTrimBudget;

    /// <summary>
    /// Let the keep-out interlock answer the safety question the clearance timeout answers by
    /// giving up.
    ///
    /// <para><b>An abandoned clearance costs the whole aim correction</b>, because it returns
    /// before any of it is applied and the shot lands where the raw burn put it. Flown, it abandons
    /// <b>87 of 144</b> flights — and on the night that first attributed the ending to each rocket,
    /// <b>8 of 8</b>, on every arm. Against that, a correction that runs to completion lands at
    /// <b>140 m</b> and every other ending at 5 to 45 km.</para>
    ///
    /// <para>The timeout exists for a real reason: a bus that cannot open the gap must not hold its
    /// salvo for ever, and a ninety-second hold put a release probe 6.8 km out. But giving up is a
    /// crude answer to it. <see cref="BusTrim"/>'s keep-out interlock is the precise one — computed
    /// in the same pass, so it already knows which way the stack lies, and it withholds the
    /// directions that point at the stage while spending the frame on the ones that do not. With
    /// every direction withheld the trim waits, which is what the timeout wanted, and
    /// <see cref="BusTrim.MaxSeconds"/> and the budget bound the wait.</para>
    ///
    /// <para><b>Off, and off is what ships.</b> It has been flown as a branch and removed every
    /// abandonment; what it lost to then was the correction loop diverging and releasing on the
    /// worst aim it found, and <c>AimCorrection.Freeze</c> keeping the best bias has landed since.
    /// It has never been flown against a baseline in the same world.</para>
    /// </summary>
    public bool KeepOutCoversTheClearance;
}
