using Brutal.Numerics;

namespace KSArmory.Tests;

/// <summary>
/// A post-boost vehicle in flight, with a thruster set and nothing else. Its control frame is
/// fixed, because the trim never turns it: the whole reason it resolves onto the vehicle's own axes
/// rather than pointing at the answer is that the release line is already decided by then.
///
/// <para><b>The two accelerations are separate on purpose</b>, and zero lateral is <em>not</em> the
/// shipped bus. That layout sums to 4.000 units fore and aft and 4.243 in every lateral direction
/// with the roll torques cancelling — see <see cref="BusAuthorityTests"/>. Zero is kept because it
/// is the case that exercises striking a direction off, not because anything flies it.</para>
/// </summary>
internal sealed class TrimBus
{
    public double3 PositionCci;
    public double3 VelocityCci;

    public double3 NoseCci;
    public double3 RightCci;
    public double3 DownCci;

    /// <summary>What the axial pair can do. Every thruster set has one.</summary>
    public double AxialAcceleration = 3.0;

    /// <summary>What the lateral jets can do. Zero is the layout the shipped bus has.</summary>
    public double LateralAcceleration;

    public void Step(BallisticBody body, TrimAxes fire, double seconds)
    {
        double3 thrust = Push(fire, TrimAxes.Forward, NoseCci, AxialAcceleration)
                       + Push(fire, TrimAxes.Backward, -NoseCci, AxialAcceleration)
                       + Push(fire, TrimAxes.Right, RightCci, LateralAcceleration)
                       + Push(fire, TrimAxes.Left, -RightCci, LateralAcceleration)
                       + Push(fire, TrimAxes.Down, DownCci, LateralAcceleration)
                       + Push(fire, TrimAxes.Up, -DownCci, LateralAcceleration);

        double3 gravity = body.GravityCci(PositionCci);

        VelocityCci += (gravity + thrust) * seconds;
        PositionCci += VelocityCci * seconds;
    }

    private static double3 Push(TrimAxes fire, TrimAxes direction, double3 along, double magnitude)
        => (fire & direction) != TrimAxes.None ? Vec.Unit(along) * magnitude : Vec.Zero;
}
