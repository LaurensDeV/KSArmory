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
    /// this mod has made arrived between 12.9 and 17.5 degrees. A shallow arrival is still the worst
    /// there is for
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
    /// How much of the steepest arrival the tanks can pay for to actually ask for — precision
    /// against range, as one number.
    ///
    /// <para><b>The arrival angle is the dominant precision lever and it is not a taste.</b> Flown
    /// with the correction floor out of the way, the miss tracks <c>cot γ</c> exactly: 0.56x at
    /// 25.9 degrees, 0.44x at 33.0 and 0.43x at 41.1, against theory's 0.65 / 0.49 / 0.36. So the
    /// steeper the better, and what bounds it is what the stack can afford —
    /// <see cref="ArrivalBudget"/> already works that out every cycle and nothing used it.</para>
    ///
    /// <para>What a player owns is the trade, not the angle: steeper is more precise and costs reach
    /// and propellant. One at the steepest affordable, zero to leave
    /// <see cref="MinArrivalAngleDeg"/> alone — <b>and a half is what ships</b>, having won twice.
    /// At 2,000 km it is <b>0.48x</b>, 29.5 m against 13.5, rank p=0.021; at 6,269 km <b>0.69x</b>,
    /// 40 m against 20, 11 wins of 12, sign p=0.006 and rank p=0.009 with the interval entirely
    /// below one. Above it the margin runs out: 0.65 is unresolved and bimodal, and 0.8 is a settled
    /// loss at 5.55x with a worst shot 309 times the baseline.</para>
    ///
    /// <para><b>It cannot price a shot out of reach</b>, which is the usual objection to a non-zero
    /// default and is not the blocker: it is a fraction of what <see cref="ArrivalBudget"/> has
    /// already established the stack can pay for, so the floor moves with the propellant rather than
    /// against it.</para>
    ///
    /// <para>Shipped at 0.5. The fixtures that encode measured constants state their own geometry
    /// through <c>FixtureGeometry.ArrivalPreference</c> rather than inheriting this, so what ships
    /// can move without re-filing their numbers under the same names.</para>
    ///
    /// <para>Latched once per flight rather than followed. The affordable angle moves as the stack
    /// lightens, and a bound that walks re-opens the search against a different limit every
    /// cycle — <c>docs/ARRIVAL-ANGLE.md</c>'s reason for refusing <see cref="Loft"/> as an arrival
    /// control.</para>
    /// </summary>
    public double ArrivalPreference = 0.5;

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
    /// <para><b>On.</b> Taking the world's clock away because a target happened to be designated is
    /// not a weapon's decision, and that principle lives in where it stops rather than in the
    /// default. Nothing about this config is persisted, so "off" would not be a setting an operator
    /// could make once: it would be a tick box to find again on every launch, and forgetting it costs
    /// a ballistic coast in real time. Measured on two shots the same evening: 3.5 minutes of wall
    /// clock with it, seventeen without.</para>
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
    /// <para>On by default, because <see cref="Armed"/> is already the interlock and a computer
    /// that flies the whole shot and then waits to be told the obvious is not delivering anything.
    /// It releases only from <see cref="IcbmPhase.Coast"/> above
    /// <see cref="DeployAltitudeMetres"/>, and never from a burn that ended short — a trajectory
    /// known to fall short would scatter them over whatever is under the short fall.</para>
    /// </summary>
    public bool AutoRelease = true;

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
    /// Draw the ground the coasting bus can still divert to, and refuse a click outside it.
    ///
    /// <para>It costs seven flights of <see cref="ImpactPredictor"/> every
    /// <c>IcbmComputer.ReachIntervalSeconds</c>, so this is the switch that stops paying for it —
    /// and the flights only happen at all while the list can still be added to, which is a coast
    /// with either <see cref="DesignateByClicking"/> on or a second target already placed.</para>
    /// </summary>
    public bool ShowDivertReach = true;

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
    /// in the 70–90 range. A separate literal binds first and silently: at 25, flown at Mahia, the trim
    /// spent all of it and stopped <b>0.45 m/s</b> short of finishing a pass it had already committed
    /// to, on a bus with tens to spare. Tying the two together means the operator's lever moves the
    /// budget <em>down</em> from the real reserve, which is the only direction it is useful in.</para>
    /// </summary>
    public double TrimBudgetMetresPerSecond = PostBoostAim.MaxTrimMetresPerSecond;

    /// <summary>
    /// Whether a bus walking between targets starts each stop's aim correction from the bias the
    /// last stop walked to, rather than from none. <see cref="AimCorrection.Retarget"/> has the
    /// reasoning; a set of one never hops, so it is untouched either way.
    ///
    /// <para><b>On.</b> Flown paired at four targets with the next switch it took the landing to
    /// 0.35x [0.18, 0.73] and a walk's trim spend to 0.36–0.64x, 8 of 8 shots; on its own, spend
    /// 8 of 8 and the landing 0.61x [0.34, 1.03]. <c>docs/MIRV-TARGETS.md</c>.</para>
    /// </summary>
    public bool CarryAimBiasAcrossHops = true;

    /// <summary>
    /// Whether the bus's trim takes the frame its last hold still owes off what is left to gain
    /// before choosing again. A command reaches the engine a frame after it is written, and once the
    /// step is long enough that half a frame of thrust is the stop band, choosing without it
    /// overshoots out of the band on the opposite axis every time — a limit cycle at long steps,
    /// none at 1x. <see cref="BusTrim"/>.
    ///
    /// <para><b>On.</b> Flown at four targets, an aim pass at 84–117 ms steps cost 0.70 m/s over 42
    /// passes against 8.13 without it, and a walk's spend fell 8 of 8 shots; on a single target the
    /// landing is non-inferior at 0.88x [0.64, 1.13]. <c>docs/MIRV-TARGETS.md</c>.</para>
    /// </summary>
    public bool TrimCountsTheCommandInFlight = true;

    /// <summary>
    /// Whether a bus walking between several targets starts its walk as soon as the coast begins,
    /// rather than ending it on <see cref="ReleaseBeforeArrivalSeconds"/>.
    ///
    /// <para>A hop bought early moves the landing further per m/s — 1,076 m against 688 at the first
    /// slot at 6,179 km — and one trim pass's 10 m/s ceiling reaches further with it, so the widest a
    /// six-target chain can be spaced goes from about 4.5 km to about 8.5, past the Mk 21's 6 km blast
    /// radius. What it costs is the gate's own reason: the ejection kick has longer to grow, which puts
    /// each warhead's floor nearer 55 m than 14. A set of one never walks and is untouched.</para>
    ///
    /// <para><b>Off, and unflown.</b> <c>docs/MIRV-TARGETS.md</c>.</para>
    /// </summary>
    public bool WalkStartsAtCutoff;

    /// <summary>
    /// Whether a post-boost pass is decided on the reading that follows a flown correction, rather
    /// than on one fifteen seconds later.
    ///
    /// <para>The frame the trim settles on carries no reading yet — the prediction runs before the
    /// trim is driven, at most every half second — and off, that frame spends "a correction has
    /// been flown". The reading that follows then finds nothing flown and waits out
    /// <see cref="PostBoostAim.FlownWithinSeconds"/>: 95% of 757 later readings waited 14-15.6 s on
    /// <c>2026-09-11-band</c>, the warheads left on a reading 14.7 s old, and that dwell is the
    /// one-signed short bias before release, 71-74 of 80 flights short.
    /// <c>docs/ACCURACY-PLAN.md</c> 3cr.</para>
    ///
    /// <para><b>On.</b> A frame with no reading asks for one and spends nothing, so the pass is
    /// decided on the frame its reading arrives. Flown over 20 paired shots: the signed release
    /// downrange moved +8.3 m [+6.2, +10.5] on every one, −7.45 m to +0.47, and the miss went
    /// 0.58x [0.44, 1.08], the median rocket landing 12.5 m to 6.5. <c>docs/ACCURACY-PLAN.md</c>
    /// 3ct.</para>
    /// </summary>
    public bool DecideOnTheReading = true;

    /// <summary>
    /// Whether the post-boost loop releases on a reading inside what the trim can resolve, rather
    /// than trimming on it again.
    ///
    /// <para><b>The floor is the trim's settle band, carried to the ground.</b> The trim stops once
    /// each axis owes less than <see cref="BusTrim.SettledMetresPerSecond"/>, and at ~380 m of miss per
    /// m/s that is 7-8 m. A pass on a reading inside it is a fresh draw — worse 52-86% of the time
    /// over three nights, against a reading good to 0.2 m — and the flat 250 m improvement band then
    /// ends the loop three passes later whatever they read, sometimes on a rocket still converging.
    /// On, the floor is <see cref="BusTrim.StopBand"/> over the arc's sensitivity, priced once a pass,
    /// and the band tracks the miss. <c>docs/ACCURACY-PLAN.md</c> 3cu.</para>
    ///
    /// <para><b>On.</b> Flown over 20 paired shots: the release probe 0.72x [0.58, 0.88] and the
    /// landing 0.61x [0.53, 0.74], each won on 17 of 20 shots, with the reading a rocket releases on
    /// falling from 6.80 m to 4.95 and those releasing on one over 10 m from 16 of 80 to 1 — in four
    /// passes rather than five. <c>docs/ACCURACY-PLAN.md</c> 3cu.</para>
    /// </summary>
    public bool ReleaseInsideTheTrimFloor = true;

    /// <summary>
    /// Whether the trim finishes its null with pulses rather than held frames — the engine's own
    /// <c>FlightComputerManualThrustMode.Pulse</c>.
    ///
    /// <para><b>A held key fires for whole frames, and that is what sets the floor.</b> The trim
    /// stops inside <see cref="BusTrim.SettledMetresPerSecond"/> because a frame of jets at the step
    /// the world runs is 0.009 m/s and firing one would overshoot; carried to the ground that band is
    /// 7-8 m of miss. A pulse is the thruster's own <see cref="PulseSeconds"/> instead, about sixteen
    /// times finer, so the phase stops inside <see cref="BusTrim.PulseFloorPulses"/> pulses — about
    /// 1 m. <c>docs/ACCURACY-PLAN.md</c> 3cu item 39.</para>
    ///
    /// <para><b>On.</b> Flown over 20 paired shots after four faults were repaired: the landing
    /// 0.47x [0.41, 0.57] and the release probe 0.44x [0.26, 0.56], each won on all 20, with the
    /// median rocket landing 6.0 → 2.5 m and the reading it releases on 4.75 → 0.90. Nothing ended on
    /// the clock and no trim gave up, against 10 of 80 on the build that was refuted.
    /// <c>docs/ACCURACY-PLAN.md</c> 3cu.</para>
    /// </summary>
    public bool PulseTrim = true;

    /// <summary>
    /// How long one of this bus's thruster pulses lasts, in seconds.
    ///
    /// <para>The engine floors a thruster's <c>MinimumPulseTime</c> at a millisecond and the shipped
    /// bus declares exactly that, so this is the floor rather than a preference. It is typed rather
    /// than read off the craft, which is the one thing here that is not measured: reading it would
    /// mean walking a vehicle's thruster modules, and a bus with coarser jets wants its own number.
    /// Too small only stalls the phase out; too large stops it early.</para>
    /// </summary>
    public double PulseSeconds = 0.001;

    /// <summary>
    /// Let a released warhead re-read the ground under each sub-step as it meets it, rather than
    /// holding the frame's first sample.
    ///
    /// <para><b>The walk from the release probe is the round stopping on ground it has already
    /// left.</b> <see cref="Slug"/> samples the ground once a frame and holds it as a sphere, while a
    /// reentry vehicle covers 40-90 m of ground track in that frame, so on a slope it stops on the
    /// wrong height. Over ~870 traced warheads on seven nights the walk is that stop error times
    /// <c>cot(gamma)</c> at R² 0.86-0.92, and flights whose error happened to be under a metre walked
    /// a median 2 m against 7 overall. <c>docs/ACCURACY-PLAN.md</c> 3cr.</para>
    ///
    /// <para><b>On.</b> Flown over 20 paired shots: the walk 0.30x [0.26, 0.40] and the miss 0.60x
    /// [0.54, 0.79], with all 80 flights stopping within 0.1 m of their own surface and seat 3's
    /// walk falling from 52 m to 4. <c>docs/ACCURACY-PLAN.md</c> 3cs. It costs a terrain lookup per
    /// sub-step within <see cref="Slug.GroundResampleBandMetres"/> of the ground — a few dozen a
    /// warhead.</para>
    /// </summary>
    public bool ResampleGroundAtImpact = true;

    /// <summary>
    /// Let a released warhead integrate its fall to second order — <see cref="Slug.SecondOrder"/>.
    ///
    /// <para><b>The walk left after the ground re-read is the round's own integrator.</b> Reading
    /// gravity where a sub-step begins and moving on the velocity it ends with is leapfrog with an
    /// extra half-kick of <c>a·h/2</c>, 3.7 mm/s at the reentry vehicle's 1 ms sub-step, and a 380 s
    /// fall at 32° carries it to about 1.8 m short. Flown, the walk is −2.1 m median, short on 149 of
    /// 160 flights and growing smoothly with time from release; flying 12 logged release states
    /// through both integrators reproduces −1.76 m. <c>docs/ACCURACY-PLAN.md</c> 3cu.</para>
    ///
    /// <para><b>On.</b> Flown over 20 paired shots: the signed walk moved +1.73 m [+1.54, +1.88] on
    /// every one of them, its magnitude 0.37x [0.30, 0.44], and the cross +0.19 → +0.03 m. The
    /// landing could not see it — 1.05x, unresolved — because a one-signed 2 m inside a ±5 m release
    /// scatter is worth tenths of a metre of distance. <c>docs/ACCURACY-PLAN.md</c> 3cu.</para>
    /// </summary>
    public bool SecondOrderWarheads = true;

    /// <summary>
    /// Take a released warhead's drag at the sub-step's midpoint velocity rather than at the velocity
    /// it starts with — <see cref="Slug.DragAtMidpointVelocity"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>The round's one mis-paired argument.</b> A second-order round already reads the air
    /// and the pull half a sub-step on; the speed its drag is taken at was not. Drag is quadratic in
    /// that speed and a re-entering warhead sheds about 450 m/s², so the start-of-step speed is always
    /// the larger and the drag always too big — one-signed, every sub-step, for the whole fall, which
    /// puts the round short of its own prediction.</para>
    ///
    /// <para><b>Headless it closes the gap outright</b>, at the flown 877 km / 32° release: the round's
    /// disagreement with <see cref="ImpactPredictor"/> goes from <b>4.669 mm to 0.001 mm</b> at the
    /// shipped 1 ms sub-step, and stays under 0.03 mm from 5 ms down to 0.25. So what 3dn priced as the
    /// integrator's order is not truncation paid for the order chosen — it is removable exactly.</para>
    ///
    /// <para><b>On.</b> Unresolvable at 5 ms against a 107 mm walk (3dy); flown again at the shipped 1 ms
    /// once the walk's two epoch faults were gone and its spread was 3 mm, paired within each world over two
    /// blocks: the landing's walk moved <b>+4.70 mm</b> downrange against 4.67 predicted, +3.85 and +5.40 by
    /// block, all of it in the air, cross −0.5 mm. It costs one extra <see cref="Medium.Drag"/> per sub-step.
    /// <c>docs/ACCURACY-PLAN.md</c> 3dx, 3em.</para>
    /// </remarks>
    public bool DragAtMidpointVelocity = true;

    /// <summary>
    /// Solve a released warhead's ground crossing against the terrain under it rather than against
    /// the chord joining the sub-step's two height samples — <see cref="Slug.StopOnTheTerrain"/>.
    /// </summary>
    /// <remarks>
    /// <para>The two samples are a whole sub-step apart, <b>5.5 m of ground</b> at a 5,500 m/s
    /// arrival, so the round stops where the chord meets its path while the prediction point-samples
    /// the height field. They stop on different surfaces, separated by the ground's curvature over
    /// that span — which is why their disagreement scales with relief, and why it carries
    /// <b>57% of the walk's variance</b> (3dt, 3dv).</para>
    ///
    /// <para><b>Headless over ground rolling a metre every forty</b>, the round stops
    /// <b>31.4 mm</b> off the true surface solving against the chord and <b>0.4 mm</b> against the
    /// terrain, and the two land 69 mm apart. Flat ground is unchanged to a micron, as it must be:
    /// there the chord <em>is</em> the surface.</para>
    ///
    /// <para><b>On</b>, flown over 12 paired blocks: the walk's sd **0.619x [0.465, 0.824]**, 38.09 to
    /// 23.57 mm, against 0.656 declared — and <c>|apart|</c> from 17.667 mm to identically zero. The
    /// walk's *mean* did not move, which is what says this removed scatter and left the bias, as a
    /// curvature term must. Two height queries on the frame a round lands and none on any other.
    /// <c>docs/ACCURACY-PLAN.md</c> 3ea.</para>
    /// </remarks>
    public bool StopWarheadsOnTheTerrain = true;

    /// <summary>
    /// Read the air's motion per sub-step rather than holding the frame's first sample —
    /// <see cref="Slug.AirVelocityAtOwnSubStep"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>The fourth field of its kind, and the one that was held.</b> <see cref="RoundFields"/>
    /// re-reads gravity, density and the ground inside the sub-step loop because a round moves within
    /// the frame and those change over that distance. The air's motion is the ground's, and a
    /// re-entering round crosses about 150 m of it in a frame — so a held sample measures the drag
    /// against air the round has left. The error is square to the airspeed rather than along it, so
    /// it tilts the deceleration rather than resizing it, and <see cref="ImpactPredictor"/> recomputes
    /// the term at every RK stage.</para>
    ///
    /// <para><b>On.</b> Headless it moves the landing 3.59 mm. Flown once the walk was 3 mm wide, paired over two
    /// blocks at the Chaco: the landing walk went from (+3.35 down, +2.55 cross) to (+0.15, −0.40) mm and the
    /// group centre from 4.17 and 3.48 mm to 1.40 and 1.33, all of it in the air. <c>docs/ACCURACY-PLAN.md</c>
    /// 3dx, 3en.</para>
    /// </remarks>
    public bool WarheadAirVelocityPerSubStep = true;

    /// <summary>
    /// Ask the ground where it was at the sub-step's own instant — <see cref="Slug.GroundQueryAtOwnEpoch"/>.
    ///
    /// <para>A terrain query is a direction, and the engine resolves it in the frame the surface
    /// turns in at the <em>frame's end</em> rotation. The round back-dates only the body's
    /// translation, so the spin in between is left in: ~416 m/s at this latitude times the crossing's
    /// own distance into the frame, which the mod already prints and whose median is 11 ms.</para>
    ///
    /// <para>What it costs is that displacement times the seat's own height gradient, so it is signed
    /// per seat rather than global, and a pooled signed endpoint cancels it.</para>
    ///
    /// <para><b>On.</b> Flown over 20 paired shots: the landing 0.66x [0.55, 0.90], won on 18 of 20,
    /// and the walk 0.54x [0.47, 0.60] on all 20. The worst rocket 11.7 → 5.3 m, and walks of 2 m or
    /// more 25 of 80 → none. Every seat's walk collapses onto one residual near −0.25 m, and its slope
    /// against the frame's own duration goes to nothing — on seat 4 from −0.083 m/ms, which three
    /// earlier nights had measured at −0.082. <c>docs/ACCURACY-PLAN.md</c> 3cw, 3da.</para>
    /// </summary>
    public bool GroundQueryAtOwnEpoch = true;

    /// <summary>
    /// Put every prediction's ground crossing on the surface rather than on the first step under it —
    /// <see cref="ImpactPredictor"/>'s <c>stopOnTheSurface</c>, for the aim, the release probe, the
    /// holding cost and the trace.
    ///
    /// <para><b>The crossing search only ever stops below the ground.</b> It accepts a step up to
    /// <see cref="ImpactPredictor.CrossingToleranceMetres"/> deep, spread over most of that, so every
    /// prediction reads long by the depth times <c>cot(gamma)</c> while a warhead stops on the
    /// surface — and the aim loop, landing its predictions on the target, lands the rounds that much
    /// short. Over 80 traced warheads the step from the late re-flies to the landing is −0.201 m
    /// [−0.212, −0.189], short on all 80, against −0.199 from the tolerance at 32°: 0.20 of the
    /// −0.25 m walk. <c>docs/ACCURACY-PLAN.md</c> 40b.</para>
    ///
    /// <para><b>On.</b> Linear between the last step above the ground and the first below it, which is
    /// the rule the warhead's own stop obeys, on the same steps and lookups. Flown over 20 paired shots
    /// beside <see cref="FocusTubesOnTheAim"/>: the signed walk +0.200 m [+0.176, +0.239] on 20 of 20, the
    /// step from the late re-flies to the landing −0.212 m → −0.007, and their scatter 0.085 m → 0.008.
    /// The group's centre read 1.12x [0.91, 1.38], unresolved. <c>docs/ACCURACY-PLAN.md</c> 3dg.</para>
    /// </summary>
    public bool PredictionStopsOnTheSurface = true;

    /// <summary>
    /// Put that crossing on the ground under <em>it</em>, rather than on the chord between the ground
    /// under the two samples bracketing it — <see cref="ImpactPredictor"/>'s <c>stopOnTheTerrain</c>.
    ///
    /// <para><b>The other half of 49b.</b> <see cref="PredictionStopsOnTheSurface"/> put the answer on a
    /// chord; <c>Slug.StopOnTheTerrain</c> then taught the <em>round</em> to re-query at its own crossing
    /// and step onto the surface. The prediction was never taught the same, and on level ground it does not
    /// matter, because there the chord <em>is</em> the surface.</para>
    ///
    /// <para><b>On a slope it is a whole seat's tail.</b> The bracket is 0.4–1.1 m of track over the
    /// engine's float-packed 0.31 m tread, so the chord sits up to half a riser off the real ground — and
    /// because <see cref="ReleaseFocus"/> cancels the chord from the probe's <em>reported</em> impact to the
    /// aim, that height is handed to the round as <c>cot(gamma)</c> times as much ground. Over eight Chaco
    /// nights the one seat aimed at a slope (0.122) flies a walk sd of 10–16 mm against the flat seats'
    /// 1.9–2.2, downrange only, scaling monotonically with the seat's slope and untouched by everything
    /// 3el–3en fixed.</para>
    ///
    /// <para><b>On, and flown at both sites.</b> At the Chaco the prediction's own stop height over the
    /// ground reads <b>+17.45 mm at the sloped seat and 0.000 with this on</b>, every flat seat under 0.3 mm
    /// either way; that seat's within-rocket walk sd goes <b>15.36 → 2.20 mm</b> and its worst warhead
    /// 23.95 → 6.35 (3ev). At 26.485S 68.148W, where every seat is sloped, 96 rockets take the median from
    /// <b>10.45 → 3.45 mm</b> and every slope band improves 2.2x to 3.5x (3ew).</para>
    ///
    /// <para><b>It does not finish the job past slope 0.40</b>, where it leaves 14.10 mm against flat
    /// ground's 2.9 — which is where 3es's rig says the round's own sub-step becomes co-dominant, and is the
    /// condition for re-asking <see cref="WarheadSubStepMs"/>. Costs one height lookup per prediction that
    /// lands, three at most. Level ground cannot move by more than a long bracket's own sagitta, about
    /// 3 µm. <c>docs/ACCURACY-PLAN.md</c> 3es, 3ev, 3ew.</para>
    /// </summary>
    public bool PredictionStopsOnTheTerrain = true;

    /// <summary>
    /// A pulse phase that stops closing gives way to holding rather than ending the null —
    /// <see cref="BusTrim"/>.
    ///
    /// <para><b>The two clocks are in the wrong order.</b> <c>BusTrim.PulseSecondsPerNull</c> is 20 s
    /// and exists precisely to drop a pulse phase back to holding when it is chasing a reference that
    /// runs away from it; <c>BusTrim.StallSeconds</c> is 10 s and ends the whole null. The shorter one
    /// is always reached first, so that guard has never once run.</para>
    ///
    /// <para><b>And a hold is about 150 times the authority.</b> A pulse phase closes at
    /// <c>accel x pulse / PulseEverySeconds</c>, 3.7 mm/s per second on the shipped bus, where a hold
    /// closes at the acceleration itself. Flown at 12,902 km, no null owing under 1.5 m/s at the split
    /// has ever stalled and 31 of 53 above it have — the shape of a phase that cannot keep up rather
    /// than of an actuator that does not work, since the pulses arrive at the engine's full allowance
    /// and deliver 1.01x of what they ask. <c>docs/ACCURACY-PLAN.md</c> 3fd.</para>
    ///
    /// <para><b>One-shot, and the progress clock restarts with it.</b> A second stall belongs to the
    /// hold and ends the null, so this cannot become the wait that never ends, which costs 110 s and
    /// a worse residual. 3fb.</para>
    ///
    /// <para><b>On.</b> Flown over 24 paired blocks at 12,902 km against a declared primary endpoint:
    /// <b>0 of 24 flights lost against 16 of 24</b>, Fisher p = 0.0000, base worse in 6 of 6 accepted
    /// blocks and 23 of 24 over every shot — and not once worse in any block. Over all 24, no
    /// <c>hold</c> rocket stalled against 62 of 96, and one landed over a kilometre out against 60.
    /// It also met the prediction declared before the night that separates a fix from a coincidence:
    /// the corrections end on <c>payback</c> 19 and <c>trim</c> 0, keeping the fine band and
    /// continuing to correct, where <c>PulseTrim=false</c> removed the same stall by releasing early
    /// on <c>floor</c>. The cost is three flights out of clock at a median 0.34 km against sixteen at
    /// 1.76, and nothing ended on <see cref="BusTrim.MaxSeconds"/>. <c>docs/ACCURACY-PLAN.md</c>
    /// 3fh.</para>
    /// </summary>
    public bool StallFallsBackToHolding = true;

    /// <summary>
    /// How long the burn's thrust line may be frozen before cutoff, in seconds of burning. Zero
    /// counts it in frames instead — <see cref="IcbmProgram.HoldDirectionFrames"/>, which ships.
    ///
    /// <para><b>A count of frames is a duration that grows with the step.</b> The freeze begins at
    /// <c>Frames x accel x step x throttle</c> of velocity still to gain and burns that off at
    /// <c>accel x throttle</c>, so it lasts <c>Frames x step</c> seconds however long a frame is —
    /// measured headlessly at <b>0.223 s at a 21 ms step against 0.291 s at 28</b>. Everything the
    /// required velocity does in that time is left square to a line nothing can still thrust
    /// along.</para>
    ///
    /// <para><b>In the orbit plane it costs nothing, which is why no fixture saw it.</b> A shot
    /// aimed along the track has no out-of-plane work left to freeze: the residual there is 0-1%
    /// square to the thrust line and grows exactly linearly with the step, 4.08x over 4x. Aimed
    /// <b>26 deg off the plane</b> it is <b>59-93% square</b> and grows <b>5.9x over the same 4x</b>,
    /// reaching 2.9 times one frame's delta-v. Flown at 12,902 km the cross-track share is 81% at a
    /// 23 ms step and 94% at 28 — the same shape. <c>docs/ACCURACY-PLAN.md</c> 3fi.</para>
    ///
    /// <para><b>Never shorter than one frame</b>, which is the least a freeze can usefully be: below
    /// that the direction is steered to a difference of two nearly equal vectors, which is what
    /// <see cref="IcbmProgram.HoldDirectionBelow"/> exists to stop.</para>
    ///
    /// <para><b>Off, and unflown.</b> 0.35 s is 20 frames at the rate the plateau was measured on.</para>
    /// </summary>
    public double HoldDirectionSeconds;

    /// <summary>
    /// A rig's reserve in place of <see cref="IcbmProgram.AscentReserveSeconds"/>, applied with
    /// <see cref="FlyAnyRange"/> off as well. Zero leaves it to that switch. No control reaches it.
    /// </summary>
    internal double AscentReserveOverrideSeconds { get; init; }

    /// <summary>
    /// Fly a shot of any range on any stack. A solid stage's remaining delta-v is velocity it will add
    /// whatever it is told, so the arc is lofted until it needs at least that much — past the
    /// cheapest arc the need climbs with the flight time to escape, so one always exists. And once
    /// the shot is matching such a stage, or a stoppable one is throttled as low as it goes against
    /// <see cref="IcbmProgram.AscentReserveSeconds"/>, the closed loop takes over from the
    /// pitch programme, because it is the only phase that can cut off.
    ///
    /// <para><b>On.</b> Flown 2026-10-04/05 from 25 to 2,000 km with the other short-shot settings, every shot landed; <c>docs/SHORT-RANGE.md</c>.</para>
    /// </summary>
    public bool FlyAnyRange = true;

    /// <summary>
    /// On a short shot whose stage can stop, turn the thrust line at no more than
    /// <see cref="IcbmProgram.SlowLineDegPerSec"/> once what is left to gain is within this many seconds
    /// of the thrust being made, and burn on the part along it. Zero is off.
    ///
    /// <para>Every pass steers along what is left to gain, and near cutoff the stack's own thrust turns
    /// that line faster than the stack can follow: it moves at <c>a sin(err) / v</c>, which grows without
    /// bound as <c>v</c> falls. A 25 g core cannot throttle down as fast as it uses up what is left, and
    /// at its floor it still makes 3 g, so the line flips and the stack chases it for the rest of the
    /// core. Flown, every core that ran dry separated spinning at 70-109 deg/s, and twice in seven the
    /// spent core knocked the upper's engine off. Holding the line still instead was tried: drag and
    /// gravity turn what is left too, and a held line left 70-165 m/s ungained. In the rig at 0.5 s,
    /// 4 of 40 flights spin against 31 of 40 off (<c>FloorHoldStudy</c>), but most of that is a relight
    /// cutting off on the line slowed through the pause, which flown left 30 m/s across it and landed
    /// 4.6 km out at 200 km. The chase after a relight is not stopped by this alone: it is meant with
    /// <see cref="ShortShotFinishesInTheAir"/>, which removes the relight, and
    /// <see cref="ShortShotSolvesWithDrag"/>, and like them acts only on an arc that stays under
    /// <see cref="DeployAltitudeMetres"/>; <c>docs/SHORT-RANGE.md</c>, "Why 150 km failed".</para>
    ///
    /// <para><b>On.</b> Flown 2026-10-04/05 from 25 to 2,000 km with the other short-shot settings, every shot landed; <c>docs/SHORT-RANGE.md</c>.</para>
    /// </summary>
    public double ShortShotSlowsLineSeconds = 0.5;

    /// <summary>
    /// A short shot whose burn finishes in thick air, on an arc that stays under
    /// <see cref="DeployAltitudeMetres"/>, cuts off there and releases, rather than pausing to coast out of
    /// the air and lighting again. A higher arc still pauses: its release above the air is what gives it
    /// millimetres.
    ///
    /// <para>The relight is where the floor chase comes back: the stack coasts out still turning, which
    /// RCS cannot stop, and lights pointing well away from what is left. Cut off in the air, the vacuum
    /// arc is wrong by the drag the prediction already names.</para>
    ///
    /// <para><b>On.</b> Flown 2026-10-04/05 from 25 to 2,000 km with the other short-shot settings, every shot landed; <c>docs/SHORT-RANGE.md</c>.</para>
    /// </summary>
    public bool ShortShotFinishesInTheAir = true;

    /// <summary>
    /// In the last <see cref="IcbmProgram.DragSolveWithinSeconds"/> of a short shot's burn, fly each
    /// solved arc with the warhead's drag and move the aim until it lands on the target.
    ///
    /// <para>Cut off in the air, a vacuum arc falls short by the drag the prediction already names --
    /// 1.63 km predicted and 1.59 flown at 200 km. Correcting the aim afterwards from inside the air was
    /// tried and is not in: it is a loop reading at 2 Hz with a ratchet, and in the air each reading
    /// is mostly the cutoff state moving. This solves the same miss inside each pass instead.</para>
    ///
    /// <para><b>On.</b> Flown 2026-10-04/05 from 25 to 2,000 km with the other short-shot settings, every shot landed; <c>docs/SHORT-RANGE.md</c>.</para>
    /// </summary>
    public bool ShortShotSolvesWithDrag = true;

    /// <summary>
    /// Give each warhead the separation velocity that lands it where the tubes' mean would —
    /// <see cref="ReleaseFocus"/>.
    ///
    /// <para>Every release prediction is of the mean of the six mouths, and every round leaves its
    /// own, on a 0.86 m ring square to the release line. So the aim loop lands the mean on the target
    /// and the rounds on the ring's ground image around it, turned by a roll nothing holds: at a 32°
    /// arrival a metre of offset lands 1.85 m downrange in the plane and 0.93 m across out of it. Over
    /// 960 warheads the once-round harmonic in tube angle holds 0.958 of the variance within a group.
    /// <c>docs/ACCURACY-PLAN.md</c> item 41.</para>
    ///
    /// <para><b>On.</b> Flown over 20 paired shots beside <see cref="PredictionStopsOnTheSurface"/>, on an
    /// endpoint that switch cannot move: a group's rms about its own centre 1.29 m → 0.035 m, the report's
    /// floor of 0.08x on 20 of 20, and each warhead's slope on its logged ring shift +0.997 → +0.006. The
    /// kick is 2.2-2.5 mm/s, and over a ring the kicks sum to nothing, so the group's centre and the aim
    /// loop reading it stay where they were. Five Kepler coasts a warhead. <c>docs/ACCURACY-PLAN.md</c>
    /// 3dg.</para>
    /// </summary>
    public bool FocusTubesOnTheAim = true;

    /// <summary>
    /// Give each warhead back the velocity the bus's rotation threw it with, so it leaves on the state
    /// the release prediction flew — <see cref="ReleaseFocus.Kick"/>.
    ///
    /// <para>A turning bus throws each round with the spin at its own mouth, which every release
    /// prediction leaves out. The part the six share moves the group's centre; the part that goes
    /// round the ring turns whatever ring is left. Headless at 340 s and 32°, 4 mm/s of it moves the
    /// centre 1.26 m, and 4.8 m on a 1,500 s flight. <c>docs/ACCURACY-PLAN.md</c> item 42.</para>
    ///
    /// <para><b>Not the transient a loop was fed.</b> The mean release state leaves spin out because
    /// guidance given it chased it — a cutoff residual of 0.15 m/s became 4.31. This is one velocity
    /// on a round that has already left, after the aim has stopped, and nothing reads it back.</para>
    ///
    /// <para>Exactly the spin, not the least kick that cancels its ground image: that is 0.44-0.85 of
    /// the size at 340 s and lands 0.34 cm out where this lands 0.20, which is the ring kick's own, for
    /// three coasts where this costs none. Independent of <see cref="FocusTubesOnTheAim"/>, so a night
    /// can fly either alone.</para>
    ///
    /// <para><b>On.</b> Flown over 24 paired shots: the group's centre 0.55x [0.49, 0.60] on 22 of 24 and
    /// the landing 0.70x [0.65, 0.80] on 23 of 24, with the group's width unmoved at 1.00x. Off, each
    /// group's centre follows its warheads' logged thrown spin at slope 1.15; on, at −0.03. Headless, with
    /// both on, every warhead lands within 0.2 cm of the tubes' mean impact at 340 s.
    /// <c>docs/ACCURACY-PLAN.md</c> 3de.</para>
    /// </summary>
    public bool CancelSpinAtSeparation = true;

    /// <summary>
    /// Give each warhead the least velocity that moves the release probe's own impact onto the target
    /// along the ground — <see cref="ReleaseFocus.TryMissKick"/>.
    ///
    /// <para>The aim loop stops on its payback rule while the predicted impact drifts short at the
    /// holding cost, so the state the warheads leave on is already predicted to miss. Over 192 flights
    /// each group's centre lands a mean 1.05 m short of the aim, short on 76 of 96 on the shipped arm:
    /// 0.83 m of it is in the release probe and 0.22 m is the fall, which
    /// <see cref="PredictionStopsOnTheSurface"/> is for. Each probe's miss taken off its own group's
    /// landings puts the median centre at 0.22 m, from 1.26.</para>
    ///
    /// <para><b>Not the hold fed forward</b>, which the loop chases: one velocity on a round that has
    /// already left, after the aim has committed, read back by nothing. Refused past a cap --
    /// <see cref="LongShotMissKickMetresPerSecond"/> or <see cref="ShortShotMissKickMetresPerSecond"/> -- so a
    /// miss the loop failed to close cannot be flown out by a separation. Independent of the two above, and
    /// summed with them.</para>
    ///
    /// <para><b>Along the ground only</b>, so a crossing the probe found under the surface keeps its
    /// depth — 28 cm of ground on the traced arc — for <see cref="PredictionStopsOnTheSurface"/>.</para>
    ///
    /// <para><b>On</b>, flown over 16 paired blocks: the group's centre 0.10x [0.09, 0.15] on 16 of 16, the
    /// median rocket 1.29 → 0.14 m and under half a metre on 54 of 64, with each centroid following its
    /// release probe at slope +0.011 where it followed at +1.003. What it leaves runs along the track, 0.33 m
    /// of scatter against 0.03 across — <see cref="ProbeMissFollowsTheGround"/>'s term. Four Kepler coasts a
    /// warhead. <c>docs/ACCURACY-PLAN.md</c> 3dh.</para>
    /// </summary>
    public bool CancelProbeMissAtSeparation = true;

    /// <summary>
    /// The cap on that kick for a salvo released at cutoff in the air, in m/s; zero keeps
    /// <see cref="ReleaseFocus.MaxMissKickMetresPerSecond"/>.
    ///
    /// <para>Released at cutoff there is no trim, and flown the probe predicted each warhead's landing to
    /// centimetres while its kick, 96-168 mm/s at 300 km, was refused: each round left the bus later and
    /// further along one line, the group walking 28-104 m. <c>docs/SHORT-RANGE.md</c>.</para>
    ///
    /// <para><b>1 m/s.</b> Flown 2026-10-05 with <see cref="ShortShotReleasesTogether"/>, three stacks at
    /// 150 and 300 km: 1.5-3.8 mm and 0.20-0.66 m, against 46-134 m with neither. At 0.3 m/s alone the
    /// later warheads of each salvo still wanted 306-638 mm/s.</para>
    /// </summary>
    public double ShortShotMissKickMetresPerSecond = 1.0;

    /// <summary>
    /// Give that cap to every short shot's salvo, including one released above the air after the trim.
    ///
    /// <para>There the probe predicted the landing to centimetres, 2-10 m out on ten flights at 500 and 700 km,
    /// and each warhead's 19-50 mm/s kick was refused at <see cref="ReleaseFocus.MaxMissKickMetresPerSecond"/>.
    /// <b>On.</b> Flown 2026-10-06 on three stacks at both ranges: 1.4-3.0 mm, every kick taken.
    /// <c>docs/SHORT-RANGE.md</c>.</para>
    /// </summary>
    public bool ShortShotKickCapAfterTheTrim = true;

    /// <summary>
    /// The cap on that kick for a long shot, in m/s; zero keeps <see cref="ReleaseFocus.MaxMissKickMetresPerSecond"/>.
    ///
    /// <para>At 12,900 km the probe predicted each rocket's landing to centimetres, 5-98 m out, and kicks of
    /// 11-20 mm/s and more were refused at the 10 mm/s sized on the Chaco geometry. <b>1 m/s.</b> Paired over
    /// eight worlds on 2026-10-06: 2.0 mm median and 17.6 mm worst against 7.1 m and 67.4 m.
    /// <c>docs/ACCURACY-PLAN.md</c>.</para>
    /// </summary>
    public double LongShotMissKickMetresPerSecond = 1.0;

    /// <summary>
    /// Hold a long shot's aim correction while solids that cannot stop are burning and KSA reports what they have
    /// left -- which it does for the controlled craft alone, so the other seats correct exactly as before.
    ///
    /// <para>Only the controlled craft can read its stage's delta-v off KSA's staging display, so only its solve is
    /// held by what its solids must still deliver -- and its aim loop opened two minutes early, read a response of
    /// 0.07-0.85 and released with 0.8-8.5 km of bias where the other seats released 0.5-0.6 km: 27-475 m on four
    /// flights of ten at 12,900 km. <b>On.</b> Flown on three worlds at 12,900 km and one at the Chaco: seat 1
    /// opened 30-35 s before handover like the rest, read 1.01, and every rocket landed within 16.2 mm.
    /// <c>docs/ACCURACY-PLAN.md</c>.</para>
    /// </summary>
    public bool AimWaitsForTheSolids = true;

    /// <summary>
    /// Arm a short shot's backstop at <see cref="BusTrim.MaxMetresPerSecond"/> rather than
    /// <see cref="IcbmProgram.BackstopBelow"/> when its arc releases after the trim, so a residual that bottoms out
    /// and turns back up is cut off -- or, inside the air, coasted out -- and handed to the trim.
    ///
    /// <para>Real SRB4's 7 g core, at its 0.12 floor, closed to 2.4 m/s at 500 km with its thrust lagging the line,
    /// missed the 2 m/s backstop, overshot to 53 m/s and chased for minutes, finishing inside the air: 108 m at
    /// 500 km and 156 m at 700. <b>On.</b> Flown: it cut off at 87 km with 10.9 m/s left and landed 3.9 and 2.8 mm,
    /// and GeoSat FAT at 500 and 700 km never tripped it, 0.9 and 2.7 mm. <c>docs/SHORT-RANGE.md</c>.</para>
    /// </summary>
    public bool ShortShotBackstopsAtTheTrim = true;

    /// <summary>
    /// Keep working the throttle on the craft being flown while KSA discards its held keys -- whenever a modal or a text
    /// field has the keyboard, which KSA's own update notice does at every launch once a newer build is out. The step
    /// the key would make is written to the throttle itself instead. Flown on Real Liquid2 at 150 km with the keyboard
    /// held throughout: off, stuck at full through cutoff and 21-25 mm; on, 0.2-2.4 mm. <c>docs/ICBM-OUTSTANDING.md</c> 1.2.
    /// </summary>
    public bool ThrottleThroughTheKeyboardClear = true;

    /// <summary>
    /// Before the bus trims back toward the stack it just dropped, let the separation shove carry it far enough that
    /// the pair cannot close to the keep-out before the release -- <see cref="SeparationClearance.ForTheTrimMetres"/>.
    /// <b>Off</b> until flown. <c>docs/ICBM-OUTSTANDING.md</c> 1.8.
    /// </summary>
    public bool TrimWaitsOutTheStack;

    /// <summary>
    /// Let every warhead of a single-target salvo released at cutoff go in the frame the first does,
    /// rather than one a frame. The stack slows in the air between releases, and flown each later
    /// warhead landed further along one line. <b>On.</b> Flown 2026-10-05 with
    /// <see cref="ShortShotMissKickMetresPerSecond"/>: six warheads within 4 mm of each other.
    /// </summary>
    public bool ShortShotReleasesTogether = true;

    /// <summary>
    /// What solids that cannot be stopped are asked to leave for the stage after them, in m/s, with the
    /// solids steered along what is left while they burn; zero matches the arc to them exactly and flies
    /// them on the schedule.
    ///
    /// <para><b>30 m/s.</b> Matched exactly, the solids overshot a 25 km shot by 13 m/s 67 deg off the
    /// nose and the core, lit high, chased it at 115 deg/s; flown 2026-10-05 at 30, 6.4 deg/s at 25 km and
    /// 9.7 at 60, warheads within 37 mm. <c>docs/SHORT-RANGE.md</c>.</para>
    /// </summary>
    public double SolidsLeaveMetresPerSecond = 30.0;

    /// <summary>
    /// On a short shot, drop solids once they push less than the stack weighs, if a stage is left after
    /// them, and light that stage at its floor.
    ///
    /// <para><b>On.</b> Carried through their tail-off, they left the stack turning at 19 deg/s as they
    /// separated and 27-63 deg/s after; flown 2026-10-05 at 150-300 km on two stacks, 0.1-0.2 deg/s at
    /// separation and 8-16 after. <c>docs/SHORT-RANGE.md</c>.</para>
    /// </summary>
    public bool DropSolidsUnderWeight = true;

    /// <summary>
    /// Measure the miss <see cref="CancelProbeMissAtSeparation"/> cancels as the chord between the probe's
    /// impact and the target on the ground as it lies, rather than square to local up —
    /// <see cref="ReleaseFocus.TryMissOnTheGround"/>.
    ///
    /// <para>The kick moves the arc square to its arrival, and a crossing slides back along the arrival onto
    /// the ground it is measured on. Over ground falling away downrange at <c>g</c> a miss taken square to up
    /// is cancelled wrong by <c>1 − tan γ / (tan γ − g)</c> of itself: headless at 32°, 0.14 and 0.19 of it at
    /// slopes of ∓0.10 and 0.19 and 0.31 at ∓0.15, where level ground leaves 0.001, and a side slope of 0.20
    /// turns a 1 m cross miss into 0.31 m of range. On the item 43 smoke a rocket released 2.3 m short kept 14%
    /// of it in its own kicked prediction. Measured along the chord, one kick lands within 2 mm on every one of
    /// those grounds. <c>docs/ACCURACY-PLAN.md</c> 43b.</para>
    ///
    /// <para><b>On</b>, flown over 12 paired blocks: the group's centre 0.20x [0.14, 0.36] on 12 of 12, the
    /// median rocket 0.157 → 0.039 m, with each centroid's downrange following <c>dh · cot γ</c> at +0.850
    /// square to up and −0.067 along the chord. Two lookups of the height field a warhead, and nothing without
    /// <see cref="CancelProbeMissAtSeparation"/>. <c>docs/ACCURACY-PLAN.md</c> 3di.</para>
    /// </summary>
    public bool ProbeMissFollowsTheGround = true;

    /// <summary>
    /// Fly this rocket's warheads with the Mk 21's drag from its mass, calibre and coefficient —
    /// <see cref="Arsenal.Mk21WithDragFromShape"/>, 3.6x the constant's drag — and predict them the same way,
    /// since the prediction reads the launcher's round.
    ///
    /// <para><b>On.</b> It lost twice and then won, and the two losses were the vacuum kick rather than the round:
    /// a warhead at this drag leaves 8.8 mm of the ring's image in its group where the constant leaves 2.1, so this
    /// switch and <see cref="KickThroughTheAir"/> are one change and neither ships alone. Flown 20 paired blocks at
    /// the Chaco against a declared ×1.20 non-inferiority bar and past it on every endpoint: worst warhead
    /// <b>0.80x</b> with a one-sided 97.5% upper bound of <b>0.985</b>, so an improvement rather than merely no
    /// worse; mean distance lower on 18 of 20 shots and the group's spread on 17 of 20; the centre unresolved;
    /// all eight seats better. <c>docs/ACCURACY-PLAN.md</c> 3eu, and 3dk and 3eo are the two losses.</para>
    ///
    /// <para>The registered profile keeps its hand-typed <c>DragK</c> so a paired night can still fly the old
    /// baseline as an arm — turning this off is what the comparator now is.</para>
    /// </summary>
    public bool WarheadDragFromItsShape = true;

    /// <summary>
    /// Take each warhead's mass off the bus as it leaves.
    ///
    /// <para><b>On.</b> True to the vehicle, and it changes the shot: shedding about half the bus over
    /// six releases loosens the pointing band, which scales as one over the inertia. The trim measures
    /// its acceleration rather than assuming it, so that half adapts. The mass comes off where the part
    /// declares it, on the thruster ring, never at a tube. Flown non-inferior against a ×1.20 bar on one
    /// target, 0.89x [0.73, 1.07], and on a four-target walk, 0.90x [0.83, 0.99];
    /// <c>docs/ACCURACY-PLAN.md</c> 3fm and 3fn.</para>
    /// </summary>
    public bool ShedWarheadMass = true;

    /// <summary>
    /// Solve each warhead's separation kick through the air rather than in vacuum — the ring's image, the arrival
    /// and the velocity sensitivity flown with the release probe's own drag, <see cref="ReleaseFocus.FlownSensitivity"/>.
    ///
    /// <para>Coasted in vacuum for the drag flight's time, the arc the kick is solved on ends kilometres under the
    /// ground, and what the solve leaves goes with that depth. Headless at the flown release: 0.19% of the ring's
    /// image on the Mk 21's constant and 0.80% on <see cref="WarheadDragFromItsShape"/> — 2.1 and 8.8 mm of each
    /// group's spread, where 3eo flew 3.1 and 8.5 — and 0.5 and 2.3 mm of its centre off a 0.54 m probe miss.
    /// Through the air, a micrometre of each. <c>docs/ACCURACY-PLAN.md</c> 3eq.</para>
    ///
    /// <para><b>On.</b> Seven predictions on the salvo's first release, carried along the coast to the rest:
    /// 1.7 ms headless, and flown over 80 rockets a median of <b>0.74 ms</b> and a worst of <b>1.51</b>, with no two
    /// rockets' columns ever landing in the same 10 ms of one frame. The spin is given back exactly either way, and
    /// this does nothing unless a kick above is on. Flown with <see cref="WarheadDragFromItsShape"/>, which requires
    /// it: the mechanism endpoint moved +0.151% to −0.017% of the ring's image, every interval inside its declared
    /// window. <c>docs/ACCURACY-PLAN.md</c> 3eu.</para>
    ///
    /// <para><b>On an airless body it cannot help</b>, because the flown columns are then the coasted ones, and it
    /// still pays for them. Never flown off Earth.</para>
    /// </summary>
    public bool KickThroughTheAir = true;

    /// <summary>
    /// Fly this rocket's warheads at a stated integration sub-step, in milliseconds, in place of the
    /// <see cref="MunitionProfile.SubStepSeconds"/> the round carries. Zero leaves the profile alone, and is
    /// the default.
    /// </summary>
    /// <remarks>
    /// <para><b>This is the whole of what the kick cannot cancel.</b> Each warhead is kicked off its own
    /// release probe's miss, so everything the prediction gets wrong about <em>where</em> leaves the measured
    /// miss; what is left is the round and <see cref="ImpactPredictor"/> disagreeing about <em>flying
    /// there</em>. Headless at the flown release — 877 km, 31.7° — that gap is first order in this step and in
    /// nothing else: 6.53 / 3.73 / <b>1.54</b> / 0.77 / 0.39 / 0.19 m at 5.00 / 2.50 / <b>1.00</b> / 0.50 /
    /// 0.25 / 0.125 ms, the Mk 21 flying at 1 ms.</para>
    ///
    /// <para><b>The predictor's own step buys nothing</b>, which is what made this the lever rather than the
    /// obvious alternative: forty times finer on both of its steps moves the arrival 0.002 mm, because it
    /// bisects onto the crossing near the ground. Nor does the frame rate reach a 1 ms sub-step —
    /// <c>Slug.Update</c> sub-steps to it whatever the frame is — so this is independent of every throughput
    /// lever. <c>docs/ACCURACY-PLAN.md</c> 3dm.</para>
    ///
    /// <para><b>What it costs is sub-steps.</b> Six warheads at 1 ms is about 300 a frame and at 0.125 ms
    /// about 2,400, which has not been measured in a frame — so a night flying this watches the frame time as
    /// well as the miss. The count scales with the step, so <see cref="MunitionProfile.MaxFaithfulStepSeconds"/>
    /// does not move and the world's timewarp is untouched.</para>
    ///
    /// <para><b>Off, pending its night</b>, which is declared on the walk rather than on the landing: the walk
    /// is 0.021 m of a 0.031 m centre, and a one-signed term inside a comparable scatter is worth less at the
    /// ground than its own size.</para>
    /// </remarks>
    public double WarheadSubStepMs;

    /// <summary>
    /// The radius of the ring each warhead of a salvo is aimed at, in metres. Zero puts all of them
    /// on the designation, which is what ships.
    /// </summary>
    /// <remarks>
    /// <para>Six 1.80 m reentry vehicles arrive a median <b>8.7 mm</b> apart today, so they
    /// interpenetrate — rounds do not collide with one another — and burst as one. Spreading them
    /// is worth more as <b>measurement</b> than as realism: a kick commanded to put every warhead in
    /// one place cannot be checked, because each is asked for the same thing and any error appears
    /// as dispersion mixed with everything else. Asked for a known ring, the kick becomes an
    /// actuator with a delivered-against-asked residual — and it is 44.5% of the within-rocket
    /// variance (<c>docs/ACCURACY-PLAN.md</c> 3ef).</para>
    ///
    /// <para><b>Bounded by the separation cap, not by preference.</b>
    /// <see cref="ReleaseFocus.MaxMissKickMetresPerSecond"/> is 10 mm/s, which over a 345 s flight is
    /// about <b>3.5 m</b> — <see cref="WarheadFootprint.WidestAt"/> is the number for a given
    /// flight. Past it the kick is refused and the warhead flies the group's aim, which from outside
    /// looks exactly like this setting doing nothing, so the log says which happened. The probe's own
    /// miss kick has its own caps, <see cref="LongShotMissKickMetresPerSecond"/> and
    /// <see cref="ShortShotMissKickMetresPerSecond"/>.</para>
    ///
    /// <para><b>It is not a MIRV footprint.</b> Against a 706 m fireball and a 2 km lethal radius,
    /// 3.5 m is nothing. Real per-target spread needs the bus to manoeuvre between releases, and the
    /// cap exists so that a separation cannot become a burn.</para>
    /// </remarks>
    public double WarheadFootprintMetres;

    /// <summary>
    /// What a second of holding the warheads is charged at, overriding
    /// <see cref="PostBoostAim.HoldingCostsMetresPerSecond"/>.
    ///
    /// <para><b>This is the floor under the miss, and it is linear in it.</b> The correction stops
    /// when the predicted miss falls under <c>cycle seconds x this</c>, so what the loop accepts is
    /// set here and nowhere else. Measured over 96 flights: a 156 m threshold, 109 m accepted, 125 m
    /// flown, and a predictor good to 4 m — the miss is the floor, not the guidance.</para>
    ///
    /// <para><b>Zero uses the constant</b>, which is 26.0 and was derived from one flight at one
    /// geometry: an ejection kick worth 8.421 km at cutoff and 5.672 km at +106 s. Nothing has
    /// checked it at another range or arrival angle, and holding is a real cost — set it too low and
    /// the loop spends leverage it cannot get back.</para>
    /// </summary>
    public double HoldingCostMetresPerSecond;

    /// <summary>
    /// Measure the holding cost off the trajectory instead of taking a number for it.
    ///
    /// <para><b>No single number is right.</b> The decay of the release impulse's leverage runs from
    /// 0.82 m/s on a 500 km shot to 21.79 on a 12,900 km one, so the shipped constant is an
    /// intercontinental value applied at every range — about 19x too high at 2,000 km, where it was
    /// the whole floor under the miss. <see cref="HoldingCost"/> measures it in two predictions and a
    /// coast.</para>
    ///
    /// <para>Wins over <see cref="HoldingCostMetresPerSecond"/> when both are set, because a measured
    /// number beats a typed one. A probe that cannot be flown falls back to whichever of those two
    /// applies, so a refusal costs the old behaviour rather than a free correction.</para>
    ///
    /// <para><b>On, and flown both ways.</b> At 2,000 km it is <b>0.28x</b> over the constant across
    /// 96 flights, 12 of 12 shots, p&lt;0.001 — 110 m to 30 m. At 12,902 km it is 0.86x [0.39, 1.15],
    /// unresolved over another 96, so the range where it cannot be shown to help is also the range
    /// where it cannot be shown to hurt. What settles it is that no constant is right at either:
    /// 26.0 is nine to twenty times the measured decay at both geometries flown.</para>
    /// </summary>
    public bool DeriveHoldingCost = true;

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
    /// <para><b>On, and resolved by flight.</b> Thirteen paired shots
    /// at 2,000 km, arms alternating within each world: <b>11 wins, median 0.49x, sign p=0.022 and
    /// signed-rank p=0.017</b>, with every one of 21 abandonments removed. The two runs were flown
    /// separately and pooled, and the interim look could not have stopped early — at five shots the
    /// smallest reachable p is 0.0625 — so the stopping rule's true false-positive rate is 2.3%
    /// against a nominal 5%, measured rather than argued. <c>docs/MIRV-NEXT.md</c> <b>8ag</b>.</para>
    ///
    /// <para>It has never been observed to hurt. Across five nights it removes every abandonment
    /// and converts them into corrections that run, and the mechanism was understood before the
    /// statistics arrived — which is the opposite way round from the three settings beside it that
    /// lost.</para>
    /// </summary>
    public bool KeepOutCoversTheClearance = true;
}
