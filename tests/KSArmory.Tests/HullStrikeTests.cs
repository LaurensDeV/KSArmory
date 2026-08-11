using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A kinetic round has to touch what it kills.
///
/// <para>A craft's bounding sphere is the half-diagonal of its bounding box — ten metres clear of
/// a rocket's skin — so a contact fuse tested against it destroys things the shell visibly missed.
/// These pin the sphere back to what it is good for, which is rejecting.</para>
/// </summary>
public class HullStrikeTests
{
    private const double Dt = 1.0 / 60.0;
    private static readonly MunitionProfile Shell = Arsenal.Cannon20Mm;

    /// <summary>A hull test the test drives, which also records everything it was asked.</summary>
    private sealed class Hull : IHullTest
    {
        public HullVerdict Verdict = HullVerdict.Struck;
        public double Fraction = 0.5;
        public object? Only;

        public readonly List<(double3 Separation, double3 Travel)> Asked = [];

        public HullVerdict Judge(object? body, double3 separation, double3 travel,
                                 out double fraction)
        {
            Asked.Add((separation, travel));
            fraction = Fraction;

            if (Only is not null && !ReferenceEquals(Only, body)) return HullVerdict.Missed;

            return Verdict;
        }
    }

    /// <summary>
    /// A shell passing 8 m from a rocket's centre is well outside the hull and well inside the
    /// sphere that contains it. Judged on the sphere alone it detonates and destroys the craft at
    /// a miss distance of 8 m.
    /// </summary>
    [Fact]
    public void AShellThatPassesBesideAHullDoesNotDetonate()
    {
        Hull hull = new() { Verdict = HullVerdict.Missed };
        Slug shell = Fire(hull, new TargetState(new double3(300, 8, 0), Vec.Zero, 25.0, new object()));

        Fly(shell);

        Assert.NotEmpty(hull.Asked);
        Assert.Equal(RoundState.Expired, shell.State);
    }

    /// <summary>
    /// The same geometry with the hull answering yes. Without this the test above passes against a
    /// round that never detonates at all.
    /// </summary>
    [Fact]
    public void AShellThatMeetsTheHullDetonatesOnIt()
    {
        object craft = new();
        Hull hull = new() { Verdict = HullVerdict.Struck };
        Slug shell = Fire(hull, new TargetState(new double3(300, 8, 0), Vec.Zero, 25.0, craft));

        Fly(shell);

        Assert.Equal(RoundState.Detonated, shell.State);
        Assert.Same(craft, shell.StruckBody);

        // Zero, because a strike is on the surface. The number in the log is then what happened
        // rather than how far away the middle of the craft was.
        Assert.Equal(0.0, shell.MissDistance);
    }

    /// <summary>
    /// Fire control decides what to shoot at; it does not decide what a shell in the air passes
    /// through. A round that struck a bystander must say so, or the kill is scored against the
    /// designated target's lethal range and destroys something the shell never reached.
    /// </summary>
    [Fact]
    public void AStrikeNamesWhatItHit()
    {
        object designated = new();
        object bystander = new();

        Hull hull = new() { Only = bystander };

        Slug shell = new(Vec.Zero, new double3(1100, 0, 0), designated, -1, Vec.Zero, Vec.Zero)
        {
            Munition = Shell,
            Hull = hull,
            Contacts = [new TargetState(new double3(300, 0, 0), Vec.Zero, 25.0, bystander)],
        };

        Fly(shell, new TargetState(new double3(600, 0, 0), Vec.Zero, 25.0, designated));

        Assert.Equal(RoundState.Detonated, shell.State);
        Assert.Same(bystander, shell.StruckBody);
    }

    /// <summary>
    /// A timed burst reaches nothing. Without this, naming what was hit passes against an
    /// implementation that names something unconditionally.
    /// </summary>
    [Fact]
    public void ATimedBurstStruckNothing()
    {
        Slug shell = new(Vec.Zero, new double3(1100, 0, 0), null, -1, Vec.Zero, Vec.Zero)
        {
            Munition = Shell,
            Hull = new Hull(),
            FuseSeconds = 0.2,
        };

        Fly(shell);

        Assert.Equal(RoundState.Detonated, shell.State);
        Assert.Null(shell.StruckBody);
    }

    /// <summary>
    /// The sphere rejects and the hull decides, in that order. This guards cost rather than
    /// correctness: the narrow phase walks triangles, and a round asking about every craft in the
    /// world every sub-step is a burst of 150 shells doing it 600 times a frame.
    /// </summary>
    [Fact]
    public void TheSphereRejectsBeforeTheHullIsAsked()
    {
        Hull hull = new();
        Slug shell = Fire(hull, new TargetState(new double3(300, 100, 0), Vec.Zero, 25.0, new object()));

        Fly(shell);

        Assert.Empty(hull.Asked);
        Assert.Equal(RoundState.Expired, shell.State);
    }

    /// <summary>
    /// The seam carries differences, never positions. Put the whole scene on an ecliptic orbit and
    /// the hull test must be handed exactly the same numbers — otherwise the 29.8 km/s carrier
    /// reaches a geometry query measured in metres, and the subtraction meant to cancel it sits at
    /// a call site no test reaches. See docs/FRAMES-AND-EPOCHS.md.
    /// </summary>
    [Fact]
    public void TheHullTestIsNeverHandedAnAbsolutePosition()
    {
        double3 common = new(29800, 12000, -4000);

        Hull atRest = new() { Verdict = HullVerdict.Missed };
        Hull carried = new() { Verdict = HullVerdict.Missed };

        Slug still = Fire(atRest, new TargetState(new double3(300, 8, 0), Vec.Zero, 25.0, new object()));
        Slug moving = new(Vec.Zero, new double3(1100, 0, 0) + common, null, -1, Vec.Zero, common)
        {
            Munition = Shell,
            Hull = carried,
        };

        for (int i = 0; i < 200; i++)
        {
            still.Update(Dt, null, Vec.Zero, Vec.Zero, Vec.Zero, Shell);

            // The round sits at the frame's start; a world sample has already crossed the step.
            double3 bodyEcl = new double3(300, 8, 0) + common * (Dt * (i + 1));
            moving.Contacts = [new TargetState(bodyEcl, common, 25.0, new object())];
            moving.Update(Dt, null, Vec.Zero, common, Vec.Zero, Shell);
        }

        Assert.NotEmpty(atRest.Asked);
        Assert.Equal(atRest.Asked.Count, carried.Asked.Count);

        for (int i = 0; i < atRest.Asked.Count; i++)
        {
            Assert.True(Vec.Len(atRest.Asked[i].Separation - carried.Asked[i].Separation) < 1e-6,
                        $"separation {i} moved by "
                        + $"{Vec.Len(atRest.Asked[i].Separation - carried.Asked[i].Separation):F6} m");
            Assert.True(Vec.Len(atRest.Asked[i].Travel - carried.Asked[i].Travel) < 1e-9,
                        $"travel {i} moved");
        }
    }

    private static Slug Fire(IHullTest hull, TargetState body) =>
        new(Vec.Zero, new double3(1100, 0, 0), null, -1, Vec.Zero, Vec.Zero)
        {
            Munition = Shell,
            Hull = hull,
            Contacts = [body],
        };

    private static void Fly(Slug shell, TargetState? target = null)
    {
        for (int i = 0; i < 200 && shell.State == RoundState.Flying; i++)
        {
            shell.Update(Dt, target, Vec.Zero, Vec.Zero, Vec.Zero, shell.Munition);
        }
    }
}
