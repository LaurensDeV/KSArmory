using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The sea-level burst laws, pinned to the bit. Every law docs/NUCLEAR-ALTITUDE.md moves by altitude has
/// to return exactly this at air 1, so each is folded over a grid of yields, heights, ages and ranges
/// into one hash of its outputs' bits: a change in the last bit of any value fails. A guard, not a
/// regression test -- it passes on the code it was taken from by construction.
///
/// <para>When a law is meant to move at sea level, the hash is re-recorded in that commit and the
/// message says why; the printed anchors say which values moved.</para>
/// </summary>
public class SeaLevelPinTests(ITestOutputHelper output)
{
    private static readonly double[] Yields = [0.3, 20.0, 1000.0, 50000.0];
    private static readonly double[] Heights = [0.0, 100.0, 500.0];
    private static readonly double[] Ages = [0.05, 0.5, 2.0, 10.0, 60.0, 200.0, 600.0];
    private static readonly double[] Ranges = [50.0, 500.0, 2000.0, 10000.0, 40000.0];

    private sealed class Fold
    {
        private ulong _hash = 14695981039346656037UL;

        public void Add(double value)
        {
            ulong bits = (ulong)BitConverter.DoubleToInt64Bits(value);
            for (int i = 0; i < 8; i++)
            {
                _hash ^= (bits >> (8 * i)) & 0xFF;
                _hash *= 1099511628211UL;
            }
        }

        public void Add(bool value) => Add(value ? 1.0 : 0.0);

        public ulong Hash => _hash;
    }

    private ulong Pin(string law, Action<Fold> fill)
    {
        Fold fold = new();
        fill(fold);
        output.WriteLine($"{law}: 0x{fold.Hash:X16}UL");
        return fold.Hash;
    }

    [Fact]
    public void TheShockFrontIsPinned()
    {
        Assert.Equal(0xE4AF80D7C6131371UL, Pin("shock", f =>
        {
            foreach (double kt in Yields)
            {
                foreach (double age in Ages) f.Add(MushroomCloud.ShockRadius(kt, age));
                foreach (double r in Ranges) f.Add(MushroomCloud.ShockArrivalSeconds(kt, r));
            }
        }));
        output.WriteLine($"anchor: 1 Mt front at 10 s {MushroomCloud.ShockRadius(1000, 10):R} m");
    }

    [Fact]
    public void TheBlastWaveIsPinned()
    {
        Assert.Equal(0x248CD99093BF51A8UL, Pin("blast wave", f =>
        {
            foreach (double kg in new[] { 20.0, 0.3e6, 20e6, 1e9, 50e9 })
            {
                foreach (double r in Ranges)
                {
                    double p = BlastWave.PeakOverpressurePascals(kg, r);
                    f.Add(p);
                    f.Add(BlastWave.PeakOverpressurePascals(kg, r, reflection: 1.0));
                    f.Add(BlastWave.PositivePhaseSeconds(kg, r));
                    f.Add(BlastWave.WindSpeed(p));
                    f.Add(BlastWave.PeakWindPascals(p));
                    f.Add(BlastWave.ReflectedPascals(p));
                    f.Add(BlastWave.WindImpulse(kg, r));
                }
            }
        }));
        output.WriteLine($"anchor: 20 kt at 2 km {BlastWave.PeakOverpressurePascals(20e6, 2000):R} Pa");
    }

    [Fact]
    public void TheGroundsReflectionIsPinned()
    {
        double3 up = new(0, 0, 1);
        Assert.Equal(0xCFD5AD3922B7036CUL, Pin("reflection", f =>
        {
            foreach (double kt in Yields)
            {
                foreach (double h in Heights)
                {
                    GroundReflection g = new(new double3(0, 0, h), up, h, MushroomCloud.GroundCoupling(kt, h));
                    foreach (double r in Ranges)
                    {
                        f.Add(g.GainAt(new double3(r, 0, 1), kt * 1e6));
                        f.Add(g.GainAt(new double3(r, 0, 300), kt * 1e6));
                    }
                }
            }
        }));
    }

    [Fact]
    public void TheCloudsShapeIsPinned()
    {
        Assert.Equal(0x81E0DC69BCE2AD78UL, Pin("cloud", f =>
        {
            foreach (double kt in Yields)
            {
                foreach (double h in Heights)
                {
                    foreach (double age in Ages)
                    {
                        MushroomCloud.Shape s = MushroomCloud.At(kt * 1e6, age, h);
                        f.Add(s.CapCentre); f.Add(s.CapRadius); f.Add(s.CapTube);
                        f.Add(s.StemTop); f.Add(s.StemRadius); f.Add(s.SurgeRadius); f.Add(s.SurgeHeight);
                        f.Add(s.Roll); f.Add(s.Fade); f.Add(s.Shock); f.Add(s.Coupling); f.Add(s.StemShare);
                        f.Add(s.ColumnTop);
                    }
                }
            }
        }));
        output.WriteLine($"anchor: 20 kt cap centre at 60 s {MushroomCloud.At(20e6, 60).CapCentre:R} m");
    }

    [Fact]
    public void TheFireballIsPinned()
    {
        Assert.Equal(0x1F7F434225A546DFUL, Pin("fireball", f =>
        {
            foreach (double kt in Yields)
            {
                f.Add(MushroomCloud.FlashSeconds(kt));
                f.Add(MushroomCloud.WhiteHotSeconds(kt));
                f.Add(MushroomCloud.FireballRadius(kt));
                foreach (double h in Heights)
                {
                    f.Add(MushroomCloud.PeakFireballRadius(kt, h));
                    foreach (double age in new[] { 0.001, 0.01, 0.1, 0.5, 2.0, 7.0, 14.0, 20.0 })
                    {
                        MushroomCloud.Flash fl = MushroomCloud.FlashAt(kt * 1e6, age, h);
                        f.Add(fl.Radius); f.Add(fl.Glow);
                        f.Add(fl.Colour.X); f.Add(fl.Colour.Y); f.Add(fl.Colour.Z);
                    }
                }
            }
        }));
    }

    [Fact]
    public void TheGlareIsPinned()
    {
        Assert.Equal(0x879EDEB1AA6B29A2UL, Pin("glare", f =>
        {
            foreach (double kt in Yields)
            {
                foreach (double r in Ranges)
                {
                    foreach (double glow in new[] { 1.0, 100.0, 600.0 }) f.Add(FlashGlare.Suns(kt, r, glow));
                }
            }
        }));
    }

    [Fact]
    public void TheDamageLawIsPinned()
    {
        Assert.Equal(0xD7A41E7CF3C3E788UL, Pin("damage", f =>
        {
            foreach (double kg in new[] { 20.0, 0.3e6, 20e6, 1e9, 50e9 })
            {
                f.Add(Warhead.LethalRadius(kg)); f.Add(Warhead.BlastRadius(kg)); f.Add(Warhead.FireballRadius(kg));
                foreach (double tolerance in new[] { 1e5, 9e6, 1e8 })
                {
                    f.Add(BlastDamage.FailureRadius(kg, tolerance));
                    foreach (double r in Ranges)
                    {
                        f.Add(BlastDamage.PressureRatio(kg, tolerance, r));
                        f.Add(BlastDamage.DentRatio(kg, tolerance, r));
                    }
                }
            }
        }));
    }

    /// <summary>Which of the burst's effects each band of air switches on, as they stand.</summary>
    [Fact]
    public void TheRegimesArePinned()
    {
        Assert.Equal(0x7504242357459262UL, Pin("regimes", f =>
        {
            foreach (double air in new[] { 1.0, 0.13, 0.015, 0.0098, 1e-3, 2.5e-4, 1e-4, 1e-5, 2e-7, 0.0 })
            {
                f.Add(MushroomCloud.IsThin(air));
                f.Add(MushroomCloud.ThinAirGrowth(air));
                f.Add(XRayGlow.Lights(air));
                f.Add(Aurora.Lights(air));
                f.Add(DebrisShell.FieldHold(air));
            }
        }));
    }
}
