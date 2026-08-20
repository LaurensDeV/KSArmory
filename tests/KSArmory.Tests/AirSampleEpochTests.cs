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
}
