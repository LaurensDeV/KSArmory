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
    /// <para><b>Off until it has flown.</b> Predicted from the logs: the release probe 0.80x, and no
    /// worse than 0.86x on flown readings alone.</para>
    /// </summary>
    public bool ReleaseInsideTheTrimFloor;

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
    /// <para><b>Off until it has flown.</b> Predicted: the walk from about −2.1 m to −0.3.</para>
    /// </summary>
    public bool SecondOrderWarheads;

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
