using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The contact rule, and what a shell does with it. A gun fired by eye has no designated target,
/// so what it hits is decided entirely by what it runs into.
/// </summary>
public class ContactSweepTests
{
    private static readonly MunitionProfile Shell = Arsenal.Cannon20Mm;

    /// <summary>
    /// A round crosses tens of metres between samples, so a distance measured at both ends of a
    /// step says nothing about what happened in the middle. This is what lets a contact fuse have
    /// a radius of a couple of metres and still mean contact.
    /// </summary>
    [Fact]
    public void AStepIsNotSteppedOver()
    {
        // Ends 500 m short and 500 m past, and dead centre in between.
        double3 separation = new(500, 0, 0);
        double3 closing = new(-1000, 0, 0);

        Assert.True(Vec.Len(separation) > 5.0);
        Assert.True(ContactSweep.TryContact(separation, closing, 1.0, 5.0,
                                            out double when, out double miss));
        Assert.Equal(0.5, when, 6);
        Assert.Equal(0.0, miss, 6);
    }

    /// <summary>Passing wide is not a contact, however briefly it was the nearest thing.</summary>
    [Fact]
    public void PassingWideIsAMiss()
    {
        Assert.False(ContactSweep.TryContact(new double3(500, 40, 0), new double3(-1000, 0, 0),
                                             1.0, 5.0, out _, out double miss));
        Assert.Equal(40.0, miss, 6);
    }

    /// <summary>
    /// A shell fired at nothing still hits what is in the way. A slug fused only against a target
    /// fire control designated has nothing to fuse on in a hand-aimed burst, so every round flies
    /// its full 2.2 km and expires through the craft it passed straight through.
    /// </summary>
    [Fact]
    public void AShellFiredAtNothingStillHitsWhatIsInTheWay()
    {
        Slug shell = new(Vec.Zero, new double3(1100, 0, 0), null, -1, Vec.Zero, Vec.Zero)
        {
            Munition = Shell,
            Contacts = [new TargetState(new double3(300, 0, 0), Vec.Zero, 2.0)],
        };

        Fly(shell);

        Assert.Equal(RoundState.Detonated, shell.State);
        Assert.True(shell.MissDistance <= Shell.FuseRadius + 2.0,
                    $"expected contact, missed by {shell.MissDistance:F1} m");
    }

    /// <summary>
    /// The same round with nothing in front of it flies on. Without this the test above passes
    /// against a shell that detonates unconditionally.
    /// </summary>
    [Fact]
    public void AnEmptySkyIsStillAMiss()
    {
        Slug shell = new(Vec.Zero, new double3(1100, 0, 0), null, -1, Vec.Zero, Vec.Zero)
        {
            Munition = Shell,
            Contacts = [new TargetState(new double3(300, 60, 0), Vec.Zero, 2.0)],
        };

        Fly(shell);

        Assert.Equal(RoundState.Expired, shell.State);
    }

    /// <summary>
    /// A bystander in front of the target is hit instead of it. Fire control decides what to shoot
    /// at; it does not decide what a shell in the air passes through.
    /// </summary>
    [Fact]
    public void ItHitsWhateverItReachesFirst()
    {
        TargetState designated = new(new double3(600, 0, 0), Vec.Zero, 2.0);
        TargetState inTheWay = new(new double3(300, 0, 0), Vec.Zero, 2.0);

        Slug shell = new(Vec.Zero, new double3(1100, 0, 0), new object(), -1, Vec.Zero, Vec.Zero)
        {
            Munition = Shell,
            Contacts = [inTheWay],
        };

        Fly(shell, designated);

        Assert.Equal(RoundState.Detonated, shell.State);

        // Where it went off, not merely that it did: both bodies are on the line of flight, so a
        // shell that ignored the near one detonates just as surely, 300 m further on.
        Assert.True(Vec.Len(shell.PositionEcl - inTheWay.PositionEcl) < 10.0,
                    $"burst {Vec.Len(shell.PositionEcl):F0} m out, expected ~300 m");
    }

    /// <summary>
    /// The frame contract, tested for invariance the way docs/FRAMES-AND-EPOCHS.md asks. Put the
    /// whole scene on an ecliptic orbit — the shell and the body carrying the same 29.8 km/s, the
    /// body sampled a frame ahead as the engine reports it — and the hit must still be a hit.
    /// Without the back-dating the shared motion arrives as ~500 m of separation per frame, which
    /// is two hundred times the fuse radius it is compared against.
    /// </summary>
    [Fact]
    public void SharedEclipticMotionDoesNotDecideWhatIsHit()
    {
        double3 common = new(0, 29800, 0);
        const double dt = 1.0 / 60.0;

        Slug shell = new(Vec.Zero, new double3(1100, 0, 0) + common, null, -1, Vec.Zero, common)
        {
            Munition = Shell,
        };

        // The round is at the frame's start; a world sample has already moved across the step.
        for (double t = 0; t < 1.0 && shell.State == RoundState.Flying; t += dt)
        {
            double3 bodyEcl = new double3(300, 0, 0) + common * (t + dt);
            shell.Contacts = [new TargetState(bodyEcl, common, 2.0)];

            shell.Update(dt, null, Vec.Zero, common, Vec.Zero, Shell);
        }

        Assert.Equal(RoundState.Detonated, shell.State);
        Assert.True(shell.MissDistance <= Shell.FuseRadius + 2.0,
                    $"expected contact, missed by {shell.MissDistance:F1} m");
    }

    /// <summary>
    /// The fallback for a body with nothing to cast against, answered in fractions of the step.
    /// A round passing 3 m from something a metre across has missed it — which is the whole of the
    /// kitten complaint, and the size fed in is the only thing that decides it.
    /// </summary>
    [Fact]
    public void ReachingASphereIsDecidedByItsOwnSize()
    {
        double3 separation = new(10, 3, 0);
        double3 travel = new(20, 0, 0);

        Assert.False(ContactSweep.TryReachSphere(separation, travel, 1.0, out double fraction));
        Assert.True(ContactSweep.TryReachSphere(separation, travel, 5.0, out _));

        // Half way along the step, where the round draws abeam of it.
        Assert.Equal(0.5, fraction, 6);
    }

    /// <summary>
    /// A step that ends short of the body has not reached it. Without the clamp the closest
    /// approach is found beyond the step and a round strikes something it has not got to yet.
    /// </summary>
    [Fact]
    public void AStepThatEndsShortHasNotArrived()
    {
        Assert.False(ContactSweep.TryReachSphere(new double3(100, 0, 0), new double3(20, 0, 0),
                                                 5.0, out double fraction));
        Assert.Equal(1.0, fraction, 6);
    }

    private static void Fly(Slug shell, TargetState? target = null)
    {
        const double dt = 1.0 / 60.0;

        for (int i = 0; i < 200 && shell.State == RoundState.Flying; i++)
        {
            shell.Update(dt, target, Vec.Zero, Vec.Zero, Vec.Zero, shell.Munition);
        }
    }
}
