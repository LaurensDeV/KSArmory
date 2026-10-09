namespace KSArmory;

/// <summary>
/// One installation's own settings for its ICBM computer: what can differ between two missiles in the
/// same world. The target is not here; it is a designation, held by the weapons system that acts on it.
/// Each setting says what it does, its verdict, and where the account is; the accounts are in
/// <c>docs/ACCURACY-PLAN.md</c>, <c>docs/SHORT-RANGE.md</c>, <c>docs/MIRV-TARGETS.md</c> and
/// <c>docs/ARRIVAL-ANGLE.md</c>.
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
    /// (<see cref="IcbmProgram.SteadyBeforeReleaseSeconds"/>). On, because this config is not saved, so off
    /// would be a tick box to find on every launch. <c>docs/ICBM-GUIDANCE.md</c>, "Automatic only if
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
    /// that stops paying for its flights. <c>docs/MIRV-TARGETS.md</c>.
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
    /// Whether a walk between several targets starts at cutoff rather than ending on
    /// <see cref="ReleaseBeforeArrivalSeconds"/>: about twice the spacing, a worse floor per warhead.
    /// <b>Off, and unflown.</b> <c>docs/MIRV-TARGETS.md</c>.
    /// </summary>
    public bool WalkStartsAtCutoff;

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
    /// add, and hand a stage that cannot stop or is at its floor to the closed loop. On; every shot from
    /// 25 to 2,000 km landed. <c>docs/SHORT-RANGE.md</c>.
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
