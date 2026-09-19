namespace KSArmory.Tests;

/// <summary>
/// The arrival geometry the measured constants in this suite were taken at.
///
/// <para>A fixture that encodes a flown number — <c>ArrivalDebtTests</c>'s 2.48 m/s per kilometre,
/// the residual's split along the thrust line, the observer's epoch term — is a statement about
/// one trajectory. <see cref="IcbmConfig.ArrivalPreference"/> changes the angle every headless
/// flight arrives at, so a fixture that inherits the shipped default silently re-files those
/// numbers under the same names whenever the default moves.</para>
///
/// <para>So they state it. What ships is free to change without a fixture's constants having to be
/// re-recorded, and a test that means "whatever ships" keeps inheriting instead — which is the
/// distinction this constant exists to make visible.</para>
/// </summary>
internal static class FixtureGeometry
{
    /// <summary>Measured with the arrival floor off, which is what these constants were flown at.</summary>
    public const double ArrivalPreference = 0.0;
}
