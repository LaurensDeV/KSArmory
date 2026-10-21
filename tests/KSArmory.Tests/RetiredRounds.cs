namespace KSArmory.Tests;

/// <summary>
/// Rounds the game no longer flies, kept for tests that pin an algorithm on them or reproduce a flight
/// logged with them.
/// </summary>
internal static class RetiredRounds
{
    /// <summary>
    /// The Mk 21 with the hand-typed DragK of 1.5e-5 it was registered with until its drag came from its
    /// shape: 3.6x less drag than it flies. The flights of 2026-09-17 and before were flown with it.
    /// </summary>
    public static MunitionProfile Mk21Constant
    {
        get
        {
            MunitionProfile round = Arsenal.ReentryVehicleMk21.Copy();
            round.MassKg = 0f;
            round.CalibreMm = 0f;
            round.DragCoefficient = 0f;
            round.DragK = 1.5e-5f;
            return round;
        }
    }
}
