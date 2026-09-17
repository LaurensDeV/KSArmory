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
    /// <para>On by default, because <see cref="Armed"/> is already the interlock and a computer
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
    /// <para><b>Off, and off is what ships.</b> Twelve paired shots at 2,000 km put it at
    /// <b>0.85x</b> over the shipped default, 9 wins of 12, sign p=0.146 — the interval is
    /// [0.53, 1.14], so it rules out anything worse than 1.14x and does not rule out nothing at
    /// all. It is the only setting here that has never lost.</para>
    /// </summary>
    public bool AimWithinTrimBudget;

    /// <summary>
    /// Let go of the attitude once the bus is pointing where it will release, and take it back only
    /// if it drifts.
    ///
    /// <para>KSA puts a vehicle off rails for as long as an actuator is <em>commanded</em>
    /// (<c>PhysicsBubble</c>'s per-vehicle rails choice), and off rails it is integrated rather than
    /// propagated as a conic. A bus sharing a physics bubble is harmless while it is on rails —
    /// measured at 224 to 241 coast probes of a divergent world costing nothing — and the free ride
    /// ends the moment this mod's own hold commands a thruster. What follows is ~4 m/s per probe of
    /// non-gravitational push, 90% of it across the plane, which walks the predicted impact and
    /// takes the shot to 88 km. <c>docs/ACCURACY-PLAN.md</c> 3ax.</para>
    ///
    /// <para>Holding is still what keeps the release line, so this is a band rather than a release:
    /// quiet inside <see cref="QuietCoastDeg"/>, and pointing again past
    /// <see cref="ReacquireCoastDeg"/>. Only during the coast, and never while burning, trimming or
    /// deploying — those need the attitude and are not where the coast's damage is done.</para>
    ///
    /// <para><b>It is bounded at both ends, and flying it without either end cost 89x.</b> Quiet
    /// begins only once the post-boost correction has finished and ends a margin before the release
    /// approach — see <see cref="QuietCoastEndsBeforeReleaseSeconds"/>. Neither bound costs much of
    /// what it is for: the coast to release runs ~980 s, the correction is over inside 120 of them,
    /// and the hold in between is ~88% of the exposure.</para>
    ///
    /// <para><b>Off, and off is what ships</b>, until it has been flown against a forced control.</para>
    /// </summary>
    public bool QuietCoast;

    /// <summary>
    /// Assert rails as well as going quiet, so the coast is propagated rather than integrated.
    ///
    /// <para><b>This is the half <see cref="QuietCoast"/> was missing.</b> Releasing the actuator is
    /// necessary and not sufficient: <c>PhysicsStates.TryToPutOnRails</c> puts a coasting vehicle
    /// back on rails only when the physics bubble's origin frame is <c>Cci</c>, and a bubble whose
    /// heaviest member sits below the near-surface radius — a rocket on its pad, a spent stage under
    /// 167 km — is <c>Ccf</c>, where there is no path back at all. Flown 2026-09-05: a quieted arm
    /// spent 276 of 376 coast probes off rails against a pointed arm's 282.</para>
    ///
    /// <para>Off rails and above its own <c>InPhysicsRadius</c>, the engine drops the centrifugal
    /// and Coriolis terms — a deficit of <c>2w x v + w x (w x r)</c>, 0.42 m/s^2 at 2.9 km/s, which
    /// is the non-gravitational push that walks the impact 50 to 160 km.
    /// <c>docs/ACCURACY-PLAN.md</c> 3bv has the derivation and the corroboration from logs already
    /// taken.</para>
    ///
    /// <para><b>Off, and off is what ships</b>, until it has been flown. It does nothing unless
    /// <see cref="QuietCoast"/> is on as well, because any commanded actuator takes the vehicle off
    /// rails again on the same sub-step.</para>
    /// </summary>
    public bool RailsDuringCoast;

    /// <summary>
    /// Let the aim correction's improvement threshold follow the miss instead of being 250 m flat.
    ///
    /// <para><b>The loop currently cannot see its own shot.</b>
    /// <see cref="AimCorrection.ImprovedByMetres"/> is 250 m absolute and
    /// <see cref="PostBoostAim.PassesWithoutImprovement"/> is three, so a cycle counts as an
    /// improvement only by bringing the impact 250 m closer — and traced 2026-09-07 the aim is
    /// <b>8 and 18 m</b> off at release on two flights landing at 12 and 19, with 1 to 4 m made
    /// during the whole fall. No cycle can improve by 250 m at that scale, so the loop always stops
    /// on three passes whatever it might have achieved. <c>docs/ACCURACY-PLAN.md</c> 3bw.</para>
    ///
    /// <para>On, the band is <c>max(1 m, 0.25 x best)</c> — a quarter being what 250 m was at the
    /// kilometre-scale miss the constant was chosen for, so it reproduces the old behaviour where it
    /// was calibrated and tightens as the shot improves. The floor is what the instrument can
    /// resolve rather than what is wanted.</para>
    ///
    /// <para><b>Off, and off is what ships.</b> Flown twice and unresolved both times: 0.88x
    /// [0.84, 1.10] on the miss over 14 shots, then 0.78x [0.51, 1.11] on the release probe over 20,
    /// with the landing at 0.98x. It takes <c>noimprov</c> from 42 of 80 flights to 6 and leaves the
    /// short bias at release where it was. <c>docs/ACCURACY-PLAN.md</c> 3cq.</para>
    ///
    /// <para><b>Its only channel is that stopping rule.</b> <see cref="AimCorrection.Freeze"/>
    /// reverts to the best-scoring aim at release, but in the frame the warheads leave, so the
    /// revert moves none of them: the flat band's 110 m median revert predicts the release probe at
    /// +0.10. What the band changes is how many passes the trim flies before release.
    /// <c>docs/ACCURACY-PLAN.md</c> 3cp.</para>
    /// </summary>
    public bool AimThresholdTracksTheMiss;

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
    /// already left, after the aim has committed, read back by nothing. Refused past
    /// <see cref="ReleaseFocus.MaxMissKickMetresPerSecond"/>, so a miss the loop failed to close cannot
    /// be flown out by a separation. Independent of the two above, and summed with them.</para>
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
    /// <para><b>Off</b>, pending its night: every flown baseline rests on the constant, and more drag steepens
    /// the shallowest arrival the round can make and lengthens what the air does to the fall.</para>
    /// </summary>
    public bool WarheadDragFromItsShape;

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
    /// How much of a warhead's miss kick to replace with what its siblings already asked for, from
    /// 0 — its own, which is every flight so far — to 1, the running mean alone.
    /// </summary>
    /// <remarks>
    /// <para>The kick cancels where <em>this</em> warhead's release probe says it will miss. The
    /// common part of that is worth <b>459 mm of centre</b> and must be kept. The part that differs
    /// between siblings behaves as noise injected one for one: the within-rocket landing deviation
    /// regresses on the probe's at <b>-1.090 +/- 0.052</b>, an interval containing -1 and excluding
    /// 0 (<c>docs/ACCURACY-PLAN.md</c> 3ef).</para>
    ///
    /// <para>The mean is over the warheads <b>already released</b>, never the whole six: a salvo
    /// leaves one tube at a time, 28 ms apart, so the six-warhead mean does not exist when the first
    /// one goes. Counterfactually over 132 rockets at 0.5 this is <b>0.886x</b> on the median worst
    /// warhead and 0.900x on its rms, and <b>0.93x</b> on ground past terrain gain one — where the
    /// ground amplifies without bound and injected noise costs most (3ej).</para>
    ///
    /// <para><b>Off, and never flown.</b> Every number above is arithmetic on logged quantities
    /// rather than a flight. Confirming it needs a within-rocket split — three warheads each way,
    /// scored on the worst of each half — which reaches 42% power in one night and 69% in two, where
    /// an ordinary paired night reaches 10%.</para>
    /// </remarks>
    public double ShrinkMissKickToTheGroup;

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
    /// miss kick shares the cap: flown at 2 m on a 6,179 km shot the largest kick was 7.07 mm/s, so
    /// refusals begin under 3 m (<c>docs/ACCURACY-PLAN.md</c> 3ek).</para>
    ///
    /// <para><b>It is not a MIRV footprint.</b> Against a 706 m fireball and a 2 km lethal radius,
    /// 3.5 m is nothing. Real per-target spread needs the bus to manoeuvre between releases, and the
    /// cap exists so that a separation cannot become a burn.</para>
    /// </remarks>
    public double WarheadFootprintMetres;

    /// <summary>Pointing error under which the coast hold lets go, in degrees.</summary>
    public double QuietCoastDeg = 0.5;

    /// <summary>Pointing error at which it takes the attitude back, in degrees.</summary>
    public double ReacquireCoastDeg = 2.0;

    /// <summary>
    /// Whether the quiet window also waits for the post-boost correction to finish.
    ///
    /// <para><b>Off, because measured in flight it makes the whole feature a no-op.</b> The
    /// reasoning for it was sound and the timing is not: the trim resolves onto the vehicle's own
    /// control axes, so a bus drifting between passes was thought to thrust along stale ones. But
    /// the correction does not occupy the start of the coast — it occupies <em>all</em> of it. Flown
    /// 2026-09-06, one paired shot: <c>holding (correcting)</c> on <b>417 of 429</b> coast probes,
    /// with the loop finishing only inside the release approach. Waiting for it leaves no window at
    /// all, and the flight measured exactly 0% of the coast quiet.</para>
    ///
    /// <para>Kept as a switch rather than deleted because the concern is real and untested — the
    /// trim is already excluded by <c>TrimIsFiring</c> while it fires, and what is unproven is
    /// whether it needs the line held <em>between</em> passes as well. That is one arm of a night,
    /// not a guess to bake in.</para>
    /// </summary>
    public bool QuietCoastAfterCorrection;

    /// <summary>
    /// How long before the release approach the coast hold takes the attitude back, in seconds.
    ///
    /// <para><b>Steady is not pointed.</b> <c>ReleaseSequence</c> waits for the bus to be steady
    /// before it latches its reference, and a bus nobody is holding is perfectly steady while
    /// aimed somewhere wrong — so warheads leave along whatever line the drift left. Flown
    /// 2026-09-05 with no such bound: the lost mode went from 12 of 56 to 1 of 56, and the healthy
    /// median from 0.017 km to 3.607.</para>
    ///
    /// <para>Re-pointing is cheap because going quiet does not move the bus. No actuator commanded
    /// is <em>on rails</em>, which is exact conic propagation — the drift is attitude and nothing
    /// else, so taking the line back restores it with no trajectory to undo.</para>
    ///
    /// <para>Measured against the margin it needs rather than chosen: a slew at the <c>Strict</c>
    /// profile's 30 deg/s is seconds even from the far side, and settling is
    /// <see cref="PostBoostAim.SettlesWithinSeconds"/>. Sixty is several times both, and costs ~6%
    /// of the quiet window.</para>
    /// </summary>
    public double QuietCoastEndsBeforeReleaseSeconds = 60.0;

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
    /// <para><b>On, and the only setting in this file resolved by flight.</b> Thirteen paired shots
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
