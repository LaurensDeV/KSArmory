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

    /// <summary>
    /// The engine's pulse contract: a commanded direction fires for one thruster
    /// <c>MinimumPulseTime</c> and no more often than this, however long the frame is.
    ///
    /// <para><b>The allowance is per vehicle, not per direction</b> — one clock, whichever way the
    /// command points. So a phase that re-picks the largest axis every frame gets exactly as many
    /// pulses as one that finishes an axis before starting the next, and the greedy order is free.
    /// </para>
    /// </summary>
    public const double PulseEverySeconds = 0.15;

    /// <summary>One pulse, the millisecond the engine floors a thruster's own minimum at.</summary>
    public double PulseSeconds = 0.001;

    private double _sincePulse = PulseEverySeconds;

    public void Step(BallisticBody body, TrimAxes fire, double seconds, bool pulse = false)
    {
        double3 thrust = Push(fire, TrimAxes.Forward, NoseCci, AxialAcceleration)
                       + Push(fire, TrimAxes.Backward, -NoseCci, AxialAcceleration)
                       + Push(fire, TrimAxes.Right, RightCci, LateralAcceleration)
                       + Push(fire, TrimAxes.Left, -RightCci, LateralAcceleration)
                       + Push(fire, TrimAxes.Down, DownCci, LateralAcceleration)
                       + Push(fire, TrimAxes.Up, -DownCci, LateralAcceleration);

        // A held frame thrusts for the whole of it; a pulse for its own length, and only when the
        // engine's 0.15 s has come round again.
        double burning = seconds;

        if (pulse)
        {
            _sincePulse += seconds;
            burning = _sincePulse >= PulseEverySeconds ? PulseSeconds : 0.0;
            if (burning > 0.0) _sincePulse = 0.0;
        }
        else
        {
            _sincePulse = PulseEverySeconds;
        }

        double3 gravity = body.GravityCci(PositionCci);

        VelocityCci += gravity * seconds + thrust * burning;
        PositionCci += VelocityCci * seconds;
    }

    private static double3 Push(TrimAxes fire, TrimAxes direction, double3 along, double magnitude)
        => (fire & direction) != TrimAxes.None ? Vec.Unit(along) * magnitude : Vec.Zero;
}
