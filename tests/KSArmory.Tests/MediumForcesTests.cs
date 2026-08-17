using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The two forces themselves, called directly.
///
/// <para><see cref="MediumTests"/> flies whole rounds through air and water and is the better test
/// of the physics; this one covers the guards, which a flown test cannot reach without a profile
/// contrived to produce a NaN. A round in a vacuum, at rest, or declaring no drag has to come away
/// with exactly nothing.</para>
/// </summary>
public class MediumForcesTests
{
    private static readonly double3 Gravity = new(0, -9.81, 0);

    private static MunitionProfile Round(float dragK = 0.0002f, float neutral = 0f) => new()
    {
        Name = "test",
        DisplayName = "test",
        DragK = dragK,
        NeutralDensityRatio = neutral,
    };

    // ---- Buoyancy ---------------------------------------------------------

    /// <summary>
    /// Zero switches the whole term off, which is what lets every round that only ever flies in air
    /// behave exactly as it would if buoyancy had never been added.
    /// </summary>
    [Fact]
    public void ARoundWithNoNeutralDensityJustFalls()
    {
        Assert.Equal(Gravity, Medium.Buoyancy(Gravity, Round(), 1.0));
        Assert.Equal(Gravity, Medium.Buoyancy(Gravity, Round(), 840.0));
    }

    /// <summary>A round at its neutral density neither sinks nor rises.</summary>
    [Fact]
    public void AtNeutralDensityGravityCancels()
    {
        double3 a = Medium.Buoyancy(Gravity, Round(neutral: 840f), 840.0);

        Assert.True(Vec.Len(a) < 1e-9, $"a neutral round accelerates at {Vec.Len(a):F6} m/s²");
    }

    /// <summary>Denser than the medium it still sinks; lighter and it rises.</summary>
    [Fact]
    public void DenserSinksAndLighterRises()
    {
        Assert.True(Medium.Buoyancy(Gravity, Round(neutral: 840f), 400.0).Y < 0.0);
        Assert.True(Medium.Buoyancy(Gravity, Round(neutral: 840f), 1200.0).Y > 0.0);
    }

    // ---- Drag -------------------------------------------------------------

    /// <summary>
    /// Nothing in a vacuum, nothing at rest, nothing for a round that declares no drag. Each of
    /// these divides out a zero somewhere if it is not guarded, and a NaN in an acceleration is a
    /// round that leaves the world on the next step.
    /// </summary>
    [Fact]
    public void DragIsExactlyNothingWhereItCannotApply()
    {
        double3 moving = new(300, 0, 0);

        Assert.Equal(Vec.Zero, Medium.Drag(moving, Round(), 0.0));
        Assert.Equal(Vec.Zero, Medium.Drag(Vec.Zero, Round(), 1.0));
        Assert.Equal(Vec.Zero, Medium.Drag(moving, Round(dragK: 0f), 1.0));
    }

    /// <summary>Opposes the airspeed, which is what makes it a deceleration to subtract.</summary>
    [Fact]
    public void DragActsAlongTheAirspeed()
    {
        double3 drag = Medium.Drag(new double3(300, 0, 0), Round(), 1.0);

        Assert.True(drag.X > 0.0, "the returned vector is subtracted, so it points along the motion");
        Assert.Equal(0.0, drag.Y, 12);
        Assert.Equal(0.0, drag.Z, 12);
    }

    /// <summary>
    /// Quadratic in airspeed: twice the speed is four times the drag. This is what makes a coasting
    /// round bleed speed instead of holding it, and what makes a bomb's fall into thicker air a
    /// problem no closed-form sight can solve.
    /// </summary>
    [Fact]
    public void DragGoesAsTheSquareOfAirspeed()
    {
        double slow = Vec.Len(Medium.Drag(new double3(100, 0, 0), Round(), 1.0));
        double fast = Vec.Len(Medium.Drag(new double3(200, 0, 0), Round(), 1.0));

        Assert.Equal(4.0, fast / slow, 9);
    }

    /// <summary>And linear in the medium's density, so one profile is right at every altitude.</summary>
    [Fact]
    public void DragScalesWithTheMediumsDensity()
    {
        double3 v = new(300, 0, 0);

        double seaLevel = Vec.Len(Medium.Drag(v, Round(), 1.0));
        double thin = Vec.Len(Medium.Drag(v, Round(), 0.25));

        Assert.Equal(0.25, thin / seaLevel, 9);
    }
}
