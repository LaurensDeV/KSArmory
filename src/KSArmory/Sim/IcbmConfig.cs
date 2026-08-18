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
