using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Which instant the air a round flies through is sampled at.
///
/// <para>The body the round's altitude is measured against is sampled once a frame, and both it and
/// the round carry the planet's ~30 km/s of ecliptic travel — so the lookup has to say which point
/// in the frame it means. Getting it wrong by one frame is 0.9 km of apparent altitude at normal
/// speed and 3.9 km at eight times, on air that falls off over 8 km, which lands on drag.</para>
///
/// <para><b>The tell is that it scales with the step</b>, so it hides at one speed and is worth
/// kilometres at another. Flown at 8x: 4.76-5.27 km of miss taken to 0.65-0.76.</para>
/// </summary>
public class AirSampleEpochTests
{
    /// <summary>
    /// Back-dated, like every other sample a round is measured against. The whole frame is behind
    /// the sample, so the offsets run from minus a frame up to zero and never above it.
    /// </summary>
    [Theory]
    [InlineData(0.0167)]
    [InlineData(0.32)]
    public void TheAirIsSampledBehindTheFrameNeverAheadOfIt(double dt)
    {
        List<double> asked = [];

        var slug = new Slug(new double3(6_500_000, 0, 0), new double3(0, 2_000, 0),
                            null, 1, Vec.Zero, Vec.Zero)
        {
            Munition = Catalogue.MunitionNamed("MK21"),
            AirDensityAt = (_, seconds) =>
            {
                asked.Add(seconds);
                return 0.5;
            },
        };

        slug.Update(dt, null, new double3(-9.0, 0, 0), Vec.Zero, Vec.Zero, slug.Munition, 0.5);

        Assert.NotEmpty(asked);

        // Never ahead of the sample: that is the failure, and it is a whole frame wide.
        Assert.True(asked.TrueForAll(s => s <= 0.0),
                    $"the air was sampled up to {asked.Max():F4} s *after* the body it is "
                    + "measured against, which is a frame of the planet's own travel read as altitude");

        // And the whole frame is covered, so the far end is a frame back rather than nothing.
        Assert.True(asked.Min() <= -dt * 0.5,
                    $"the earliest sample was only {asked.Min():F4} s back on a {dt:F4} s frame");
    }

    /// <summary>
    /// A missile reads the same back-dated air. Handed the frame's single sample instead, a round
    /// low over a site near sea level reads a frame of the planet's travel as altitude, lands below
    /// the waterline, and water drag stops it the moment its motor does.
    /// </summary>
    [Fact]
    public void AMissileLowOverTheSeaFliesThroughAirNotWater()
    {
        const double radius = 6_371_000.0;
        const double dt = 1.0 / 60.0;

        // The planet's travel laid along the local vertical, which is where it reads as altitude.
        double3 up = new(1, 0, 0);
        double3 planetVelocity = up * 29_800.0;

        // Air above the mean surface and water below it: the cliff an altitude error falls off.
        static double Medium(double3 position, double3 centre)
            => Vec.Len(position - centre) - radius < 0.0 ? 840.0 : 1.0;

        var round = new Interceptor(up * (radius + 100.0), planetVelocity + new double3(0, 300, 0),
                                    null, 1, Vec.Zero, planetVelocity)
        {
            Munition = BuiltIns.Missile57E6,
        };

        double3 centre = Vec.Zero;

        // Four seconds, so the motor has stopped and only the medium is left holding the speed.
        for (int i = 0; i < 240 && round.State == RoundState.Flying; i++)
        {
            // The world advances first: the body's sample belongs to the end of the step the round
            // is about to be flown across.
            centre += planetVelocity * dt;
            double3 sampled = centre;

            RoundDriver.Fly(round, dt, null, Vec.Zero, planetVelocity, Vec.Zero, round.Munition,
                            Medium(round.PositionEcl, sampled),
                            new RoundFields(null,
                                            (at, seconds) => Medium(at - (planetVelocity * seconds), sampled),
                                            null));
        }

        double speed = Vec.Len(round.VelocityEcl - planetVelocity);
        Assert.True(speed > 250.0,
                    $"the missile slowed to {speed:F0} m/s: it was flown through water, a frame of "
                    + "the planet's travel below where it is");
    }
}
