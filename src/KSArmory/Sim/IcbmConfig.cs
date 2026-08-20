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
    /// Multiplies the flight time of the cheapest shot. One is minimum energy; above one is a
    /// lofted trajectory that arrives steeper and later, below one is a depressed one that arrives
    /// sooner and costs far more. Both ends run out: a shot flat enough to pass through the planet
    /// is refused rather than flown.
    /// </summary>
    public double Loft = 1.0;

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

    /// <summary>Fire the next stage when the running one has nothing left to burn.</summary>
    public bool AutoStage = true;

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
    /// <para><b>On, because the cant is the largest error left and what made turning cost more than
    /// it saved has been rebuilt.</b> It was off after a flight where a separated bus hunted rather
    /// than settled — every release a timeout, a three-minute salvo, and the miss going from
    /// 1.7-0.3 km to 6-10 km. Three things have changed since. One tube's failure now latches for
    /// the vehicle instead of being re-proved five times, which took that salvo from 282 s to under
    /// 30. The release is budgeted in one currency rather than gated on an angle and a sweep
    /// independently, so a bus that cannot hold still releases at its best rather than at a
    /// deadline — 0.8 deg off and 0.139 m/s at the tube on the flown numbers, against 5.1 deg and
    /// 0.291. And the give-up paths name which failure it is rather than only that there was one.
    /// </para>
    ///
    /// <para>All three are measured headlessly against flown inputs rather than observed, which is
    /// the standing doubt. If a salvo scatters, this is the switch — and the failure is a spread
    /// group rather than a lost one, because a launcher that cannot point releases anyway.</para>
    ///
    /// <para>Free for a launcher it does not describe: a single tube is the mean of its own axes, so
    /// nothing is asked to turn.</para>
    /// </summary>
    public bool RepointBetweenReleases = true;

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
}
