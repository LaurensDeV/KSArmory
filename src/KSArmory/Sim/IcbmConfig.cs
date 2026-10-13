namespace KSArmory;

/// <summary>
/// One installation's own settings for its ICBM computer: what can differ between two missiles in the
/// same world. The target is not here; it is a designation, held by the weapons system that acts on it.
/// Each setting says what it does, its verdict, and where the account is; how they fit together is
/// <c>docs/ICBM-GUIDANCE.md</c>, and the flights are in <c>docs/ACCURACY-PLAN.md</c>, <c>docs/SHORT-RANGE.md</c>,
/// <c>docs/MIRV-TARGETS.md</c> and <c>docs/ARRIVAL-ANGLE.md</c>.
/// </summary>
internal sealed class IcbmConfig
{
    /// <summary>Nothing lights an engine until this is set. The whole safety interlock.</summary>
    public bool Armed;

    /// <summary>
    /// The most acceleration the stack may be flown at, in standard gravities; zero is no limit. Against
    /// standard gravity rather than the local field, because it is the number written on the airframe.
    /// </summary>
    public float MaxAccelerationGee;

    /// <summary>
    /// Multiplies the flight time of the cheapest shot: above one lofts, below one depresses. Not an
    /// arrival-angle control, and from orbit it can invert one — 33.9° at 1.0 became 6.2° at 1.8 on a
    /// 556 km shot. <c>docs/ARRIVAL-ANGLE.md</c>.
    /// </summary>
    public double Loft = 1.0;

    /// <summary>
    /// The shallowest the warheads may come in, in degrees below the horizontal; zero is off, and the
    /// default. A bound on the arc search rather than a nudge, so a shot with no arc steep enough is
    /// reported rather than flown flat. <c>docs/ARRIVAL-ANGLE.md</c> is the account.
    /// </summary>
    public double MinArrivalAngleDeg;

    /// <summary>
    /// How much of the steepest arrival the tanks can pay for (<see cref="ArrivalBudget"/>) to ask for,
    /// latched once per flight. <b>0.5 ships</b>, having won twice: 0.48x at 2,000 km and 0.69x at
    /// 6,269 km; 0.65 is bimodal and 0.8 a settled loss at 5.55x. <c>docs/ACCURACY-PLAN.md</c> 3aa, 3am.
    /// Fixtures state their own through <c>FixtureGeometry.ArrivalPreference</c>.
    /// </summary>
    public double ArrivalPreference = 0.5;

    /// <summary>
    /// Whether the aim carries the bias the flown prediction asks for. On: headless at 7°, 19.56 km
    /// uncorrected against 0.38. Under an arrival floor it inverts, headless 8.52 km against 0.018;
    /// not flown. <c>docs/METRE-LEVEL.md</c> B1.
    /// </summary>
    public bool CorrectAim = true;

    /// <summary>Altitude at which the pitch programme starts turning away from vertical.</summary>
    public double TurnStartMetres = 800.0;

    /// <summary>Altitude by which the pitch programme has reached the horizon.</summary>
    public double TurnEndMetres = 55_000.0;

    /// <summary>
    /// How far the commanded thrust line may sit off the airflow while there is air worth worrying about.
    /// </summary>
    public double MaxAngleOfAttackDeg = 8.0;

    /// <summary>
    /// The dynamic pressure below which closed-loop guidance may steer freely, in pascals, so it means
    /// the same on a body with a thick atmosphere and on one with none.
    /// </summary>
    public double HandoverPressurePa = 1200.0;

    /// <summary>
    /// Clicking the world names the aim point. A mode rather than a button, because pressing a button
    /// puts the cursor over the panel; off by default, like the burst tool and the mouse trigger.
    /// </summary>
    public bool DesignateByClicking = false;

    /// <summary>
    /// Fire the next sequence when there is nothing to burn with: the ignition on the pad, and each
    /// spent stage after it.
    /// </summary>
    public bool AutoStage = true;

    /// <summary>
    /// Warp the ballistic coast without being asked, stopping a settling margin short of the release
    /// (<see cref="IcbmProgram.SteadyBeforeReleaseSeconds"/>). On, so a coast is warped without a tick box to find
    /// first. <c>docs/ICBM-GUIDANCE.md</c>, "Automatic only if
    /// somebody said so".
    /// </summary>
    public bool WarpTheCoast = true;

    /// <summary>
    /// Let the warheads go by themselves, from <see cref="IcbmPhase.Coast"/> above
    /// <see cref="DeployAltitudeMetres"/> and never from a burn that ended short. On, because
    /// <see cref="Armed"/> is already the interlock.
    /// </summary>
    public bool AutoRelease = true;

    /// <summary>
    /// Put the bus back on its solution with its own thrusters before letting anything go, which only
    /// it can do once the burn is over. On, and free where there is nothing to trim.
    /// <c>docs/ICBM-GUIDANCE.md</c>.
    /// </summary>
    public bool TrimBeforeRelease = true;

    /// <summary>Keep a mark on the designated target, with the time to impact beside it.</summary>
    public bool MarkTarget = true;

    /// <summary>Draw the arc this vehicle is on; per installation, since one arc is worth more than four.</summary>
    public bool DrawTrajectory = true;

    /// <summary>
    /// Draw the ground the coasting bus can still divert to, and refuse a click outside it. The switch
    /// that stops paying for its flights. <c>docs/ICBM-GUIDANCE.md</c>, "Several targets".
    /// </summary>
    public bool ShowDivertReach = true;

    /// <summary>
    /// Altitude above which the post-boost vehicle is willing to let its warheads go. Deployment itself
    /// belongs to fire control.
    /// </summary>
    public double DeployAltitudeMetres = 100_000.0;

    /// <summary>
    /// How close to arrival the warheads are let go, in seconds; zero releases once the altitude allows.
    /// An altitude alone is met on the way up, giving the separation kick the whole coast to grow into a
    /// miss; and the bus is no reentry vehicle, so a release inside the air breaks it up among its warheads.
    /// </summary>
    public double ReleaseBeforeArrivalSeconds = 420.0;

    /// <summary>
    /// The release gate for a computer with one target, in seconds before arrival; negative is
    /// <see cref="ReleaseBeforeArrivalSeconds"/>. A walk keeps that one, since a later gate shortens every stop's fall
    /// and prices its hops past one pass: 13.0 m/s at 350 s against 10.8 at 420, flown. 350 ships with
    /// <see cref="KickAtTheRelease"/> and <see cref="FreezeKeepsABetterRecentAim"/>: <c>docs/ICBM-OUTSTANDING.md</c> 1.14.
    /// </summary>
    public double SingleTargetReleaseBeforeArrivalSeconds = 350.0;

    /// <summary>
    /// How much the bus may spend on trimming across the whole flight, in m/s; zero is none, negative
    /// unlimited. Defaults to <see cref="PostBoostAim.MaxTrimMetresPerSecond"/>, the reserve sized against
    /// what a bus carries: a separate 25 bound first and stopped a committed pass 0.45 m/s short.
    /// <c>docs/MIRV-NEXT.md</c>.
    /// </summary>
    public double TrimBudgetMetresPerSecond = PostBoostAim.MaxTrimMetresPerSecond;

    /// <summary>
    /// Whether each stop of a walk starts its aim correction from the bias the last stop reached
    /// (<see cref="AimCorrection.Retarget"/>). On: at four targets the landing 0.61x on its own and 0.35x
    /// with <see cref="TrimCountsTheCommandInFlight"/>. <c>docs/MIRV-TARGETS.md</c>.
    /// </summary>
    public bool CarryAimBiasAcrossHops = true;

    /// <summary>
    /// Whether the trim takes off the frame its last hold still owes before choosing again, since a
    /// command reaches the engine a frame late. On: an aim pass at 84–117 ms steps 0.70 m/s against 8.13,
    /// and one target non-inferior at 0.88x. <c>docs/MIRV-TARGETS.md</c>.
    /// </summary>
    public bool TrimCountsTheCommandInFlight = true;

    /// <summary>
    /// Whether the walk planner prices each hop at the reach of the slot it is flown in, scaled down
    /// from the reach measured now, rather than every hop at that one reach. On, with
    /// <see cref="PriceHopsAtWhatTheTrimPays"/>: flown at 4 km, 7 of 8 walks flew every stop planned, against
    /// 0 of 16 priced at the reach measured now. <c>docs/MIRV-TARGETS.md</c>, "six stops to the ground".
    /// </summary>
    public bool PriceEachHopAtItsSlot = true;

    /// <summary>
    /// Whether the trim reserve kept beyond the budget for nulling the split may be spent on hops once a
    /// walk's first stop has let its warheads go. Flown paired on a 4 km six-target walk: 12 of 12 buses walked five stops
    /// against four for all 12 without it, every warhead within 5 mm. <c>docs/MIRV-TARGETS.md</c>.
    /// </summary>
    public bool SpendTheSplitReserveAfterTheFirstStop = true;

    /// <summary>
    /// Whether a walk prices each hop at what <see cref="BusTrim"/> pays for it -- the divert's parts on the bus's
    /// three axes summed, since it fires one at a time -- rather than at its length. Off, hops priced 6.4-6.9
    /// m/s flew at 11-14 and every walk at 4 km ran dry at stop 5 of 6; on with
    /// <see cref="PriceEachHopAtItsSlot"/>, 7 of 8 flew every stop planned. <c>docs/MIRV-TARGETS.md</c>,
    /// "six stops to the ground".
    /// </summary>
    public bool PriceHopsAtWhatTheTrimPays = true;

    /// <summary>
    /// Whether a post-boost pass is decided on the reading that follows a flown correction rather than
    /// one fifteen seconds later. On: the release +8.3 m [+6.2, +10.5] on 20 of 20, the miss 0.58x.
    /// <c>docs/ACCURACY-PLAN.md</c> 3cr, 3ct.
    /// </summary>
    public bool DecideOnTheReading = true;

    /// <summary>
    /// Whether the post-boost loop releases on a reading inside what the trim can resolve rather than
    /// trimming on it again. On: the release probe 0.72x and the landing 0.61x over 20 paired shots.
    /// <c>docs/ACCURACY-PLAN.md</c> 3cu.
    /// </summary>
    public bool ReleaseInsideTheTrimFloor = true;

    /// <summary>
    /// Whether the trim finishes its null with the engine's pulses
    /// (<c>FlightComputerManualThrustMode.Pulse</c>) rather than held frames. On: the landing 0.47x and
    /// the release probe 0.44x, each on 20 of 20. <c>docs/ACCURACY-PLAN.md</c> 3cu.
    /// </summary>
    public bool PulseTrim = true;

    /// <summary>
    /// How long one of this bus's thruster pulses lasts, in seconds. The engine floors
    /// <c>MinimumPulseTime</c> at a millisecond and the shipped bus declares that; typed rather than read
    /// off the craft, so a bus with coarser jets wants its own number.
    /// </summary>
    public double PulseSeconds = 0.001;

    /// <summary>
    /// Let a released warhead re-read the ground under each sub-step rather than hold the frame's first
    /// sample. On: the walk 0.30x and the miss 0.60x over 20 paired shots, for a few dozen lookups a
    /// warhead. <c>docs/ACCURACY-PLAN.md</c> 3cr, 3cs.
    /// </summary>
    public bool ResampleGroundAtImpact = true;

    /// <summary>
    /// Integrate a released warhead's fall to second order (<see cref="Slug.SecondOrder"/>). On: the
    /// signed walk +1.73 m on 20 of 20, the landing unresolved at 1.05x. <c>docs/ACCURACY-PLAN.md</c> 3cu.
    /// </summary>
    public bool SecondOrderWarheads = true;

    /// <summary>
    /// Take a released warhead's drag at the sub-step's midpoint velocity
    /// (<see cref="Slug.DragAtMidpointVelocity"/>). On: flown +4.70 mm downrange against 4.67 predicted,
    /// for one more <see cref="Medium.Drag"/> a sub-step. <c>docs/ACCURACY-PLAN.md</c> 3dx, 3em.
    /// </summary>
    public bool DragAtMidpointVelocity = true;

    /// <summary>
    /// Solve a released warhead's ground crossing against the terrain under it rather than the chord
    /// between two height samples (<see cref="Slug.StopOnTheTerrain"/>). On: the walk's sd 0.619x over
    /// 12 paired blocks. <c>docs/ACCURACY-PLAN.md</c> 3ea.
    /// </summary>
    public bool StopWarheadsOnTheTerrain = true;

    /// <summary>
    /// Read the air's motion per sub-step rather than hold the frame's first sample
    /// (<see cref="Slug.AirVelocityAtOwnSubStep"/>). On: the group centre 4.17 → 1.40 mm and
    /// 3.48 → 1.33 mm over two blocks. <c>docs/ACCURACY-PLAN.md</c> 3dx, 3en.
    /// </summary>
    public bool WarheadAirVelocityPerSubStep = true;

    /// <summary>
    /// Ask the ground where it was at the sub-step's own instant (<see cref="Slug.GroundQueryAtOwnEpoch"/>).
    /// On: the landing 0.66x and the walk 0.54x over 20 paired shots. <c>docs/ACCURACY-PLAN.md</c> 3cw, 3da.
    /// </summary>
    public bool GroundQueryAtOwnEpoch = true;

    /// <summary>
    /// Put every prediction's ground crossing on the surface rather than the first step under it
    /// (<see cref="ImpactPredictor"/>'s <c>stopOnTheSurface</c>). On: the step from the late re-flies to
    /// the landing −0.212 → −0.007 m. <c>docs/ACCURACY-PLAN.md</c> 3dg.
    /// </summary>
    public bool PredictionStopsOnTheSurface = true;

    /// <summary>
    /// Put that crossing on the ground under it rather than the chord between the two samples bracketing it
    /// (<see cref="ImpactPredictor"/>'s <c>stopOnTheTerrain</c>). On: the sloped Chaco seat's walk sd
    /// 15.36 → 2.20 mm and the rough site's median 10.45 → 3.45 mm; past slope 0.40 it leaves 14.10 mm,
    /// the condition for re-asking <see cref="WarheadSubStepMs"/>. <c>docs/ACCURACY-PLAN.md</c> 3es, 3ev, 3ew.
    /// </summary>
    public bool PredictionStopsOnTheTerrain = true;

    /// <summary>
    /// A pulse phase that stops closing gives way to holding, once, rather than ending the null
    /// (<see cref="BusTrim"/>). On: at 12,902 km, 0 of 24 flights lost against 16 of 24.
    /// <c>docs/ACCURACY-PLAN.md</c> 3fd, 3fh.
    /// </summary>
    public bool StallFallsBackToHolding = true;

    /// <summary>
    /// A rig's reserve in place of <see cref="IcbmProgram.AscentReserveSeconds"/>, applied with
    /// <see cref="FlyAnyRange"/> off as well. Zero leaves it to that switch. No control reaches it.
    /// </summary>
    internal double AscentReserveOverrideSeconds { get; init; }

    /// <summary>
    /// Fly a shot of any range on any stack: loft the arc until it needs at least what a solid stage will
    /// add, and hand a stage that cannot stop or is at its floor to the closed loop. On; 25 to 2,000 km have
    /// landed, though RealSRB4 cannot finish 1,000 km (<c>docs/ICBM-OUTSTANDING.md</c> 1.17). <c>docs/ICBM-GUIDANCE.md</c>,
    /// "Short shots".
    /// </summary>
    public bool FlyAnyRange = true;

    /// <summary>
    /// On a short shot under <see cref="DeployAltitudeMetres"/> whose stage can stop, turn the thrust line
    /// no faster than <see cref="IcbmProgram.SlowLineDegPerSec"/> once what is left is within this many
    /// seconds of thrust; zero is off. Meant with <see cref="ShortShotFinishesInTheAir"/> and
    /// <see cref="ShortShotSolvesWithDrag"/>. On at 0.5 s. <c>docs/SHORT-RANGE.md</c>, "Why 150 km failed".
    /// </summary>
    public double ShortShotSlowsLineSeconds = 0.5;

    /// <summary>
    /// A short shot whose burn finishes in thick air, on an arc under <see cref="DeployAltitudeMetres"/>,
    /// cuts off and releases there rather than coasting out and relighting. On.
    /// <c>docs/SHORT-RANGE.md</c>.
    /// </summary>
    public bool ShortShotFinishesInTheAir = true;

    /// <summary>
    /// In the last <see cref="IcbmProgram.DragSolveWithinSeconds"/> of a short shot's burn, fly each solved
    /// arc with the warhead's drag and move the aim until it lands. On; 1.63 km predicted and 1.59 flown
    /// short at 200 km without it. <c>docs/SHORT-RANGE.md</c>.
    /// </summary>
    public bool ShortShotSolvesWithDrag = true;

    /// <summary>
    /// Give each warhead the separation velocity that lands it where the tubes' mean would
    /// (<see cref="ReleaseFocus"/>). On: a group's rms about its centre 1.29 → 0.035 m on 20 of 20.
    /// <c>docs/ACCURACY-PLAN.md</c> 3db, 3dg.
    /// </summary>
    public bool FocusTubesOnTheAim = true;

    /// <summary>
    /// Give each warhead back the velocity the bus's rotation threw it with
    /// (<see cref="ReleaseFocus.Kick"/>). On: the group's centre 0.55x on 22 of 24 and the landing 0.70x.
    /// <c>docs/ACCURACY-PLAN.md</c> 3de.
    /// </summary>
    public bool CancelSpinAtSeparation = true;

    /// <summary>
    /// Give each warhead the least velocity that moves the release probe's impact onto the target along the
    /// ground (<see cref="ReleaseFocus.TryMissKick"/>), refused past
    /// <see cref="LongShotMissKickMetresPerSecond"/> or <see cref="ShortShotMissKickMetresPerSecond"/>.
    /// On: the group's centre 0.10x on 16 of 16. <c>docs/ACCURACY-PLAN.md</c> 3dh.
    /// </summary>
    public bool CancelProbeMissAtSeparation = true;

    /// <summary>
    /// The cap on that kick for a salvo released at cutoff in the air, in m/s; zero keeps
    /// <see cref="ReleaseFocus.MaxMissKickMetresPerSecond"/>. 1 m/s: with
    /// <see cref="ShortShotReleasesTogether"/>, 1.5 mm to 0.66 m against 46–134 m with neither.
    /// <c>docs/SHORT-RANGE.md</c>.
    /// </summary>
    public double ShortShotMissKickMetresPerSecond = 1.0;

    /// <summary>
    /// Give that cap to every short shot's salvo, including one released above the air after the trim.
    /// On: 1.4–3.0 mm at 500 and 700 km, every kick taken. <c>docs/SHORT-RANGE.md</c>.
    /// </summary>
    public bool ShortShotKickCapAfterTheTrim = true;

    /// <summary>
    /// The cap on that kick for a long shot, in m/s; zero keeps
    /// <see cref="ReleaseFocus.MaxMissKickMetresPerSecond"/>. 1 m/s: at 12,900 km, 2.0 mm median against
    /// 7.1 m. <c>docs/ACCURACY-PLAN.md</c>, "12,900 km on 2026-10-06".
    /// </summary>
    public double LongShotMissKickMetresPerSecond = 1.0;

    /// <summary>
    /// Give a miss kick over its cap at the cap, along the line that cancels it, rather than refuse it whole. At
    /// 12,900 km seven of 48 groups landed 442-550 m out on a kick of 1.01-1.17 m/s against the 1 m/s cap, which at
    /// the cap would have taken 86-99% of it. <b>Off, and unflown.</b> <c>docs/ACCURACY-PLAN.md</c> 3fr.
    /// </summary>
    public bool MissKickStopsAtTheCap;

    /// <summary>
    /// Hold a long shot's aim correction while solids that cannot stop are burning and KSA reports what
    /// they have left, which it does for the controlled craft alone. On: every rocket within 16.2 mm.
    /// <c>docs/ACCURACY-PLAN.md</c>, "12,900 km on 2026-10-06".
    /// </summary>
    public bool AimWaitsForTheSolids = true;

    /// <summary>
    /// Arm a short shot's backstop at <see cref="BusTrim.MaxMetresPerSecond"/> rather than
    /// <see cref="IcbmProgram.BackstopBelow"/> when its arc releases after the trim, so a residual that
    /// turns back up is cut off and handed to the trim. On: Real SRB4 108 → 3.9 mm at 500 km.
    /// <c>docs/SHORT-RANGE.md</c>.
    /// </summary>
    public bool ShortShotBackstopsAtTheTrim = true;

    /// <summary>
    /// Once a short shot's throttle-down has run <see cref="IcbmProgram.PushesThroughAfterSeconds"/> in the air, add the
    /// measured push against it to the throttle, lead the line across it and freeze the line only under
    /// <see cref="IcbmProgram.PushesThroughHoldBelow"/>. Flown, Real Liquid2 at 25 km: off, a 12.5 min hover and
    /// 0.1-0.4 mm; on, 18 s and 0.4-1.8 mm. <c>docs/SHORT-RANGE.md</c>.
    /// </summary>
    public bool ShortShotPushesThroughAStall = true;

    /// <summary>
    /// How long the throttle-down runs in the air before <see cref="ShortShotPushesThroughAStall"/> acts, in seconds.
    /// LowTWR at 200 km stalls at its floor with the slowed line falling behind the drag, and cuts off 2.8 m/s short
    /// 8 s into its ramp, before a push-through at 10 s can act: 7 of 8 such flights landed 247-344 m out. At 4 s, 6 of 6
    /// within 24 mm, and 700 km in 2.3 mm against 2.3 km. Not the default: in the rig the game's stack held at its floor
    /// spins on 5 of 8 low arcs at 4 s (<c>FloorHoldTests</c>). <c>docs/ICBM-OUTSTANDING.md</c> 1.12.
    /// </summary>
    public double ShortShotPushesThroughAfterSeconds = IcbmProgram.PushesThroughAfterSeconds;

    /// <summary>
    /// Hand the ascent to the closed loop once burning on the schedule has raised the velocity to gain past
    /// <see cref="IcbmProgram.OutrunShareOfTheStack"/> of what the stack holds, on a stage that can stop. Flown 2026-10-12, RealSRB4
    /// at 1,000 km: off, 3 of 3 burned out 12.8-13.7 km/s short; on, 3 of 3 within 2.5 mm, and it never trips on GeoSat FAT at
    /// 1,983 km. <c>docs/ICBM-OUTSTANDING.md</c> 1.17.
    /// </summary>
    public bool HandsOverWhenBurningAddsToTheShot = true;

    /// <summary>
    /// Where solids burn out inside the closed loop, count the arrival's latch window and the steady aim from their
    /// burnout rather than from the loop's start. Off, the latch fires on the jettison's own frame, against a
    /// prediction the solid stage's projection made: flown from <c>ORBIT 300</c> to 40 N, 10 E, the miss jumped 1.1 m
    /// to 1.57 km in that cycle and the loop froze on it. On, ORBIT 300 and ORBIT 800 to there cut off 0.03-0.06 km off
    /// their own prediction against 1.0-1.6, and it never fires on a pad shot. <c>docs/ICBM-OUTSTANDING.md</c> 1.14.
    /// </summary>
    public bool LatchWaitsOutTheSolids = true;

    /// <summary>
    /// Before dropping a dry stage inside the air during the closed loop, turn the nose into the airflow. Off, the
    /// stage goes at whatever angle of attack the guidance holds: RealLiquid2 at 1,000 km drops its core at 24 deg and
    /// 7.7 kPa, and the core's sideways drag carries it into the upper's bell, striking it every flight and breaking
    /// the engine on four of six. On: dropped at 2.9-7.8 deg, no strike on three flights, and it never engaged on nine
    /// other cells. <c>docs/ICBM-OUTSTANDING.md</c> 1.18.
    /// </summary>
    public bool StagesIntoTheAirflow = true;

    /// <summary>
    /// On a coast longer than <see cref="LongCoastSeconds"/>, re-solve the arc from the bus's own state when the release
    /// gate opens, at the cheapest arrival near the committed one, and commit to that. Off, the trim nulls against the
    /// cutoff's conic at the cutoff's arrival, and a cutoff residual grows over the coast into a debt that is timing
    /// rather than landing: flown from ORBIT 800 to 40 N 10 E, 0.22-0.32 m/s short at cutoff owed 8-17 m/s at the split
    /// and landed 2.2 km out; on, 0.19-0.40 short landed within 2.6 mm, the arrival moved 2-4 s.
    /// <c>docs/ICBM-OUTSTANDING.md</c> 1.20.
    /// </summary>
    public bool LongCoastFreesTheArrival = true;

    /// <summary>The coast after cutoff, in seconds, past which <see cref="LongCoastFreesTheArrival"/> acts.</summary>
    public double LongCoastSeconds = 4_000.0;

    /// <summary>
    /// When the trim refuses a correction over its ceiling having spent nothing, commit the cheapest arrival from the
    /// bus's state rather than <c>IcbmProgram.ReleaseArrival</c>'s NaN. A NaN arrival opens the release gate and turns
    /// every later post-boost pass into nothing, and the trim restarts against the arc it just refused: the shot goes
    /// untrimmed. Flown from ORBIT 800: a refusal of 17.1 m/s committed an arrival 2.66 s earlier and landed within
    /// 1.3 mm; one 260 s out with 3.5 km of miss still asked over the ceiling and shipped what the old path would.
    /// <c>docs/ICBM-OUTSTANDING.md</c> 1.20, <c>docs/ACCURACY-PLAN.md</c> 3bq.
    /// </summary>
    public bool RefusedTrimTakesTheCheapestArrival = true;

    /// <summary>
    /// Resume the aim correction at cutoff rather than on the first frame the engines are off, which a hold for a burn
    /// window spends. In <c>PastCutoffRig</c>, an orbital bus aimed 400 km behind: off, 0 post-boost passes and released
    /// 404.6 m out; on, 4 passes and 1.3 m. Paired on the pad at 6,181 km, 1.07x [0.73, 1.59] over 32 rockets, both near
    /// 3 mm; from orbit it flew on every hold that landed. <c>docs/ICBM-OUTSTANDING.md</c> 1.9.
    /// </summary>
    public bool AimResumesAtCutoff = true;

    /// <summary>
    /// Add the separation kick to the predicted arc where the warheads will leave it -- the departure coasted in vacuum
    /// to <see cref="IcbmProgram.ReleaseGate"/> before arrival (<see cref="ReleaseCoast"/>) -- rather than at cutoff or
    /// wherever the bus is now. Off, the kick is given the whole coast to grow in: flown to 40 N, 10 E from
    /// <c>ORBIT 300</c>, a four-hour coast, the burn's aim absorbed 1.6 km of it, the release found 2 km the trim could
    /// not pay so close in, and the warheads landed 1.93 km out. On, with <see cref="FreezeKeepsABetterRecentAim"/> and a
    /// 350 s single-target gate: trim 0.20-0.27x on three nights, that coast in millimetres, the landing neutral (1.21x
    /// without the later gate). <c>docs/ICBM-OUTSTANDING.md</c> 1.14.
    /// </summary>
    public bool KickAtTheRelease = true;

    /// <summary>
    /// When the burn commits its arrival, freeze the aim on the best of the last ten readings if it missed by less than the banked best,
    /// rather than always reverting to the best. The flat 250 m band banks nothing under 250 m, so on the 2026-10-11
    /// 6,181 km night every rocket converged to 1-6 m and was reverted to a best two minutes stale, cut off 4.2 km off
    /// its own prediction, and spent 16-18 m/s of trim where the split asked 2.5-3. On, with <see cref="KickAtTheRelease"/>:
    /// trim 0.54x alone. <c>docs/ICBM-OUTSTANDING.md</c> 1.16.
    /// </summary>
    public bool FreezeKeepsABetterRecentAim = true;

    /// <summary>
    /// Keep a burn window once the hold has chosen to wait for it. Off, every re-solve asks again whether waiting saves
    /// 1,000 m/s, and nearing the window leaving now always passes: flown under warp, it lit 7.2 deg off the plane at
    /// 15,800x on a 50 s step. From <c>ORBIT 300</c>, without it 2 of 2 holds left mid-warp, 7-47 deg off the plane;
    /// with it 7 of 7 lit on their window. <c>docs/ICBM-OUTSTANDING.md</c> 1.9.
    /// </summary>
    public bool HoldKeepsItsWindow = true;

    /// <summary>
    /// Let the bus drift through a hold, turning onto its burn only as long before ignition as the turn takes
    /// (<see cref="HoldPointing"/>), and drifting again if the window moves away.
    /// Off, the attitude is held for the whole wait: flown from <c>ORBIT 300</c> with a day's hold warped through, the jets
    /// measured 0.000 m/s2 at the trim, 2.44 m/s went unpaid and the warheads landed 1.70 km out. With it, 4 of 5 holds
    /// trimmed and landed within 5 mm; the fifth pointed for five minutes at 1x before lighting and emptied them anyway.
    /// </summary>
    public bool HoldDriftsUntilIgnition = true;

    /// <summary>
    /// Point with KSA's Balanced profile while holding for a window, 1 deg of deadband against Strict's 0.2, and Strict once
    /// the burn lights. Off, Strict throughout: flown from <c>ORBIT 300</c>, a minute of pointing at 1x left the jets 0.55
    /// m/s2 and 4.5-5 minutes left them empty. Unflown.
    /// </summary>
    public bool HoldPointsLoosely;

    /// <summary>
    /// When the next stage would drop the launcher, move that stage to the end of the staging list -- as a player's drag
    /// does -- and stage on, where it holds the joint alone. Flown on <c>TEST Any BusEarly</c>: off, the burn ended 435
    /// and 4,461 m/s short and the bus crashed with all six warheads aboard; on, it moved stage 2 to last and landed all
    /// six within 5 mm at 700 and 6,179 km. <c>docs/ICBM-OUTSTANDING.md</c> 1.5.
    /// </summary>
    public bool MovesAnEarlyRelease = true;

    /// <summary>
    /// Under an arrival floor, asked or preferred, take no aim reading during the burn and latch the arrival without
    /// waiting for one; the passes after cutoff still correct. The floored search hops between arcs as the correction
    /// opens, and it banks the hop as drag. Flown neutral on a solid deorbit stage; unflown on a liquid one.
    /// <c>docs/ARRIVAL-ANGLE.md</c>, "Built 2026-10-10".
    /// </summary>
    public bool AimWaitsForCutoffUnderAFloor;

    /// <summary>
    /// Keep working the throttle on the craft being flown while KSA discards its held keys, by writing the
    /// key's step to the throttle. On: 21–25 mm off, 0.2–2.4 mm on. <c>docs/ICBM-OUTSTANDING.md</c> 1.2.
    /// </summary>
    public bool ThrottleThroughTheKeyboardClear = true;

    /// <summary>
    /// Keep the trim's jets firing on the craft being flown while KSA discards its held keys, by putting the held
    /// translation flags back after the clear. On: 1.29 km off, 2.4 mm on, one pair. <c>docs/ICBM-OUTSTANDING.md</c> 1.2.
    /// </summary>
    public bool JetsThroughTheKeyboardClear = true;

    /// <summary>
    /// Before the bus trims back toward the stack it just dropped, let the separation shove carry it far enough that
    /// the pair cannot close to the keep-out before the release -- <see cref="SeparationClearance.ForTheTrimMetres"/>.
    /// From orbit, on: 8 of 8 clear; off: 6 of 13 inside the keep-out. No cost on the pad. <c>docs/ICBM-OUTSTANDING.md</c> 1.8.
    /// </summary>
    public bool TrimWaitsOutTheStack = true;

    /// <summary>
    /// Make a throttle below a liquid engine's floor by switching the engine on and off in that share of frames, and hold
    /// it off while KSA's load is within a tenth of breaking the airframe. Flown without it, a stack whose four A2s floor at
    /// 11.6% was loaded past KSA's 21.2 g by that floor and destroyed at 700 and 6,179 km; with it, whole and every warhead
    /// within 5 mm at both. Paired on GeoSat FAT it changes nothing: 1.06x at 6,181 km and 0.99x at 12,900, 80 rockets.
    /// Six rig fixtures built on the floor-held cutoff are pinned off, each saying why. <c>docs/ICBM-OUTSTANDING.md</c> 1.11.
    /// </summary>
    public bool PulsesBelowTheFloor = true;

    /// <summary>
    /// Hold the trim for <see cref="TrimWaitsOutTheStack"/> only while it nulls the split, never for a post-boost
    /// correction or a walk's hop, leaving the stack to the keep-out after that. On: at 12,900 km 2.3 mm median and 12 mm worst
    /// against 26.2 mm and 527 m with every correction held, and from orbit every bus still at its keep-out.
    /// <c>docs/ACCURACY-PLAN.md</c> 3fr.
    /// </summary>
    public bool StackWaitsOnlyForTheSplit = true;

    /// <summary>
    /// Let every warhead of a single-target salvo released at cutoff go in the frame the first does, since
    /// the stack slows in the air between releases. On: six warheads within 4 mm of each other.
    /// <c>docs/SHORT-RANGE.md</c>.
    /// </summary>
    public bool ShortShotReleasesTogether = true;

    /// <summary>
    /// What solids that cannot be stopped are asked to leave for the stage after them, in m/s, steered along
    /// what is left; zero matches them exactly. 30 m/s: the core's chase 115 → 6.4 deg/s at 25 km.
    /// <c>docs/SHORT-RANGE.md</c>.
    /// </summary>
    public double SolidsLeaveMetresPerSecond = 30.0;

    /// <summary>
    /// On a short shot, drop solids once they push less than the stack weighs, if a stage is left after
    /// them, and light that stage at its floor. On. <c>docs/SHORT-RANGE.md</c>.
    /// </summary>
    public bool DropSolidsUnderWeight = true;

    /// <summary>
    /// Measure the miss <see cref="CancelProbeMissAtSeparation"/> cancels along the chord on the ground
    /// rather than square to local up (<see cref="ReleaseFocus.TryMissOnTheGround"/>). On: the group's
    /// centre 0.20x on 12 of 12. <c>docs/ACCURACY-PLAN.md</c> 43b, 3di.
    /// </summary>
    public bool ProbeMissFollowsTheGround = true;

    /// <summary>
    /// Fly and predict this rocket's warheads with the Mk 21's drag from its shape
    /// (<see cref="Arsenal.Mk21WithDragFromShape"/>), 3.6x the constant's. On, with
    /// <see cref="KickThroughTheAir"/>, which it requires: worst warhead 0.80x, upper bound 0.985.
    /// Off is the old baseline as an arm. <c>docs/ACCURACY-PLAN.md</c> 3eu; 3dk and 3eo lost.
    /// </summary>
    public bool WarheadDragFromItsShape = true;

    /// <summary>
    /// Take each warhead's mass off the bus as it leaves, where the part declares it. On: non-inferior at
    /// 0.89x on one target and 0.90x on a four-target walk. <c>docs/ACCURACY-PLAN.md</c> 3fm, 3fn.
    /// </summary>
    public bool ShedWarheadMass = true;

    /// <summary>
    /// Solve each warhead's separation kick through the air rather than in vacuum
    /// (<see cref="ReleaseFocus.FlownSensitivity"/>). On, with <see cref="WarheadDragFromItsShape"/>;
    /// it does nothing unless a kick is on, and on an airless body it pays and cannot help.
    /// <c>docs/ACCURACY-PLAN.md</c> 3eq, 3eu.
    /// </summary>
    public bool KickThroughTheAir = true;

    /// <summary>
    /// Fly this rocket's warheads at a stated sub-step, in ms, in place of the round's
    /// <see cref="MunitionProfile.SubStepSeconds"/>; zero leaves it, and is the default. The round's
    /// sub-step, not the predictor's, is what the kick cannot cancel. <b>Off on flown evidence</b>: it
    /// did not move the landing (3ei); flown again only if sloped-ground walk is the largest term left.
    /// <c>docs/ACCURACY-PLAN.md</c> 3dm, 3dn, 3ei, 3ew; <c>docs/ICBM-OUTSTANDING.md</c> 3.3.
    /// </summary>
    public double WarheadSubStepMs;

    /// <summary>
    /// The radius of the ring each warhead of a salvo is aimed at, in metres; zero puts all on the
    /// designation, which ships. A measurement actuator rather than a footprint, bounded by
    /// <see cref="WarheadFootprint.WidestAt"/>; past it the kick is refused and logged.
    /// <c>docs/ACCURACY-PLAN.md</c> 3ef, 3ek.
    /// </summary>
    public double WarheadFootprintMetres;

    /// <summary>
    /// What a second of holding the warheads is charged at, overriding
    /// <see cref="PostBoostAim.HoldingCostsMetresPerSecond"/>; zero uses that constant. It is the floor
    /// under the miss. <c>docs/ACCURACY-PLAN.md</c> 3j.
    /// </summary>
    public double HoldingCostMetresPerSecond;

    /// <summary>
    /// Measure the holding cost off the trajectory (<see cref="HoldingCost"/>), winning over
    /// <see cref="HoldingCostMetresPerSecond"/>. On: 0.28x at 2,000 km, unresolved 0.86x at 12,902.
    /// <c>docs/ACCURACY-PLAN.md</c> 3p, 3w.
    /// </summary>
    public bool DeriveHoldingCost = true;

    /// <summary>
    /// Let <see cref="BusTrim"/>'s keep-out interlock answer what the clearance timeout answers by giving
    /// up, which costs the whole aim correction. On: 11 wins of 13 at 2,000 km, median 0.49x.
    /// <c>docs/MIRV-NEXT.md</c> 8ag.
    /// </summary>
    public bool KeepOutCoversTheClearance = true;
}
