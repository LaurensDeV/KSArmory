using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// A burst above the ground rather than on it: the cloud that stands on the ground under it, the fuse
/// that sets one off, and the parachute that gives whoever dropped it time to leave.
/// </summary>
public class AirBurstTests(ITestOutputHelper output)
{
    private const double Kt = 1.0e6;
    private const double Tsar = 50_000.0;

    [Fact]
    public void ABurstOnTheGroundIsTheSurfaceBurstItAlwaysWas()
    {
        foreach (double kt in new[] { 0.3, 20.0, 340.0 })
        {
            for (double age = 0.5; age < MushroomCloud.LifeSeconds; age += 7.0)
            {
                MushroomCloud.Shape at = MushroomCloud.At(kt * Kt, age);
                Assert.Equal(at, MushroomCloud.At(kt * Kt, age, 0.0));
                Assert.Equal(1.0, at.Coupling);
                Assert.Equal(1.0, at.StemShare);
            }

            Assert.Equal(MushroomCloud.FireballRadius(kt) * MushroomCloud.SurfaceBurstGain,
                         MushroomCloud.PeakFireballRadius(kt), 9);
        }
    }

    /// <summary>
    /// Bravo's numbers hold up to its own 15 Mt, and ten minutes after Tsar Bomba its cloud is as the
    /// reports have it: 64 to 67 km high and about 95 km across. Bravo's numbers alone drew it 52 km
    /// high and three times too wide.
    /// </summary>
    [Fact]
    public void TsarBombaIsAsTallAndAsWideAsItWasTenMinutesIn()
    {
        Assert.Equal(MushroomCloud.StratospherePenetration, MushroomCloud.PenetrationFor(340.0));
        Assert.Equal(MushroomCloud.StratospherePenetration, MushroomCloud.PenetrationFor(15_000.0), 9);
        Assert.Equal(MushroomCloud.TsarPenetration, MushroomCloud.PenetrationFor(Tsar), 9);
        Assert.Equal(MushroomCloud.TsarPenetration, MushroomCloud.PenetrationFor(500_000.0), 9);
        Assert.Equal(MushroomCloud.DrawnCapWidening, MushroomCloud.CapWideningFor(15_000.0), 9);

        MushroomCloud.Shape tenMinutes = MushroomCloud.At(Tsar * Kt, 600.0, 4000.0);
        double crown = tenMinutes.CapCentre + (MushroomCloud.CrownInTubes * tenMinutes.CapTube);
        double across = 2.0 * (tenMinutes.CapRadius + tenMinutes.CapTube);
        output.WriteLine($"50 Mt at 10 min: {crown / 1000.0:F1} km high, {across / 1000.0:F0} km across, "
                         + $"fade {tenMinutes.Fade:F2}, gone at {MushroomCloud.LifeFor(Tsar):F0} s");

        Assert.InRange(crown, 64_000.0, 67_000.0);
        Assert.InRange(across, 88_000.0, 102_000.0);
        Assert.True(tenMinutes.Fade > 0.8, "still standing, not fading");
        Assert.InRange(MushroomCloud.TallestDrawn(Tsar, 4000.0), 64_000.0, 68_000.0);

        double last = 0.0;
        foreach (double kt in new[] { 1000.0, 5000.0, 15_000.0, 25_000.0, Tsar })
        {
            double now = MushroomCloud.TallestDrawn(kt);
            Assert.True(now > last, $"{kt} kt stands at {now:F0} m, under {last:F0} m");
            last = now;
        }
    }

    [Fact]
    public void GroundCouplingIsWholeOnTheGroundAndGoneByTheFalloutSafeHeight()
    {
        foreach (double kt in new[] { 0.3, 20.0, Tsar })
        {
            double safe = MushroomCloud.FalloutSafeHeight(kt);

            Assert.Equal(1.0, MushroomCloud.GroundCoupling(kt, 0.0));
            Assert.Equal(1.0, MushroomCloud.GroundCoupling(kt, 0.5 * safe), 9);
            Assert.Equal(0.0, MushroomCloud.GroundCoupling(kt, safe), 9);

            double last = 1.0;
            for (double h = 0.0; h <= 2.0 * safe; h += safe / 50.0)
            {
                double c = MushroomCloud.GroundCoupling(kt, h);
                Assert.True(c <= last + 1e-12, $"{kt} kt coupling rose at {h:F0} m");
                last = c;
            }

            // Where the ball stops touching the ground, as the fallout-safe height says it is.
            Assert.InRange(safe / MushroomCloud.FireballRadius(kt), 0.80, 0.84);
        }

        // Tsar Bomba left next to no local fallout.
        Assert.True(MushroomCloud.GroundCoupling(Tsar, 4000.0) < 0.05);
        Assert.True(MushroomCloud.PeakFireballRadius(Tsar, 4000.0) < MushroomCloud.PeakFireballRadius(Tsar));
    }

    [Fact]
    public void AnAirBurstRaisesAThinnerColumnAndNoneFromHighEnough()
    {
        double safe = MushroomCloud.FalloutSafeHeight(20.0);

        Assert.Equal(1.0, MushroomCloud.StemShare(20.0, 0.0));
        Assert.Equal(MushroomCloud.AirStemShare, MushroomCloud.StemShare(20.0, 1.5 * safe), 9);
        Assert.Equal(0.0, MushroomCloud.StemShare(20.0, 7.0 * safe), 9);

        // The higher air drops, at about five of those heights, still raised one.
        Assert.True(MushroomCloud.StemShare(20.0, 5.0 * safe) > 0.1);

        // Tsar Bomba stood on a column.
        Assert.True(MushroomCloud.StemShare(Tsar, 4000.0) > 0.4);
    }

    /// <summary>
    /// The cap starts where the bomb went off and ends where the laws put it, measured from the ground
    /// under it -- which is what keeps the stem, the skirt and the dust ring on the ground.
    /// </summary>
    [Fact]
    public void AnAirBurstsCapStartsAtItsHeightAndSettlesWhereASurfaceBurstsDoes()
    {
        const double height = 4000.0;

        MushroomCloud.Shape young = MushroomCloud.At(Tsar * Kt, 0.05, height);
        Assert.InRange(young.CapCentre, height, height + 200.0);

        // Once the blast front has let both out: at 50 Mt a real-time front at the speed of sound is
        // minutes crossing a cloud a hundred kilometres wide, and holds each in until it has.
        double settled = MushroomCloud.RiseSeconds + MushroomCloud.HeldByFront(Tsar) + 30.0;
        MushroomCloud.Shape risen = MushroomCloud.At(Tsar * Kt, settled, height);
        MushroomCloud.Shape surface = MushroomCloud.At(Tsar * Kt, settled);
        Assert.True(Math.Abs(risen.CapCentre - surface.CapCentre) < 0.005 * surface.CapCentre,
                    $"air burst's cap at {risen.CapCentre:F0} m, surface burst's at {surface.CapCentre:F0} m");

        // Never below where it went off.
        for (double age = 0.05; age < MushroomCloud.RiseSeconds; age += 0.5)
        {
            Assert.True(MushroomCloud.At(Tsar * Kt, age, height).CapCentre >= height);
        }

        // A burst above where its cap would settle still climbs, and never comes down.
        double over = MushroomCloud.DrawnStandingTop(0.3) * 2.0;
        MushroomCloud.Shape high = MushroomCloud.At(0.3 * Kt, MushroomCloud.RiseSeconds, over);
        Assert.True(high.CapCentre > over);
    }

    /// <summary>The ring of dust is where the front meets the ground, and nothing before it gets there.</summary>
    [Fact]
    public void TheDustFrontIsWhereTheBlastMeetsTheGround()
    {
        const double height = 4000.0;

        double reaches = MushroomCloud.ShockArrivalSeconds(Tsar, height);
        Assert.Equal(0.0, MushroomCloud.At(Tsar * Kt, reaches * 0.9, height).Shock);

        double age = reaches * 3.0;
        double front = MushroomCloud.ShockRadius(Tsar, age);
        Assert.Equal(Math.Sqrt((front * front) - (height * height)),
                     MushroomCloud.At(Tsar * Kt, age, height).Shock, 6);
    }

    /// <summary>
    /// The cloud as the shader draws it -- sheared downwind, leaned, billows proud of the tube -- never
    /// outruns its blast front, which a megatonne-class cloud is still inside minutes in. Bounded on the
    /// upright shape alone, Tsar's downwind edge ran ahead of it.
    /// </summary>
    [Fact]
    public void ADrawnMegatonCloudStaysInsideItsFront()
    {
        const double height = 4000.0;

        for (double age = 1.0; age < MushroomCloud.LifeFor(Tsar); age += 5.0)
        {
            MushroomCloud.Shape s = MushroomCloud.At(Tsar * Kt, age, height);
            if (s.Spent) continue;

            double across = ((s.CapRadius + ((1.0 + MushroomCloud.BillowReach) * s.CapTube))
                             * (1.0 + (MushroomCloud.AgedShear * MushroomCloud.Aged(age))))
                            + (MushroomCloud.MostLean * s.CapCentre);
            double up = s.CapCentre - height + ((MushroomCloud.CrownInTubes + MushroomCloud.BillowReach) * s.CapTube);
            double reach = Math.Sqrt((across * across) + (up * up));

            Assert.True(reach <= MushroomCloud.ShockRadius(Tsar, age) * 1.000001,
                        $"at {age:F0} s the cloud reaches {reach / 1000.0:F1} km, the front "
                        + $"{MushroomCloud.ShockRadius(Tsar, age) / 1000.0:F1} km");
        }
    }

    /// <summary>
    /// A store stops a metre over the ground and the cursor sits a hair off it: a surface burst a few
    /// metres up is still one, and its column joins its cap from the first instant rather than climbing
    /// to it for half a minute.
    /// </summary>
    [Fact]
    public void ASurfaceBurstAFewMetresUpIsStillJoinedToItsCap()
    {
        foreach (double height in new[] { 0.5, 3.0, 15.0 })
        {
            for (double age = 1.0; age < 60.0; age += 3.0)
            {
                MushroomCloud.Shape s = MushroomCloud.At(0.3 * Kt, age, height);
                Assert.Equal(1.0, s.ColumnTop, 9);
                Assert.Equal(1.0, s.StemShare, 9);
            }
        }
    }

    [Fact]
    public void AnAirBurstRaisesNoColumnUntilItsFrontReachesTheGround()
    {
        const double height = 4000.0;
        double reaches = MushroomCloud.ShockArrivalSeconds(Tsar, height);

        Assert.True(MushroomCloud.At(Tsar * Kt, reaches * 0.95, height).StemShare < 0.05,
                    "all but the 2% of a surface burst it still is");
        Assert.Equal(MushroomCloud.StemShare(Tsar, height),
                     MushroomCloud.At(Tsar * Kt, reaches + MushroomCloud.ColumnFormsSeconds + 1.0, height).StemShare, 9);

        // A surface burst's column is there from the first instant.
        Assert.Equal(1.0, MushroomCloud.At(20.0 * Kt, 0.01).StemShare);
    }

    /// <summary>
    /// A surface burst's column always joins its cap. An air burst's rises from the ground once the
    /// front is there, and a high one tops out under a cloud it never reaches -- the gap the
    /// photographs of the higher air drops show.
    /// </summary>
    [Fact]
    public void AHighAirBurstsColumnStopsShortOfItsCap()
    {
        for (double age = 0.5; age < MushroomCloud.LifeSeconds; age += 13.0)
        {
            Assert.Equal(1.0, MushroomCloud.At(20.0 * Kt, age).ColumnTop);
        }

        double safe = MushroomCloud.FalloutSafeHeight(20.0);

        // Low enough to be drawing the ground in: it joins, once it has climbed.
        double low = 1.2 * safe;
        double settled = MushroomCloud.ShockArrivalSeconds(20.0, low) + MushroomCloud.ColumnClimbSeconds + 1.0;
        Assert.Equal(1.0, MushroomCloud.At(20.0 * Kt, settled, low).ColumnTop);

        // High: it climbs, and stops well under the cap.
        double high = 5.0 * safe;
        double reaches = MushroomCloud.ShockArrivalSeconds(20.0, high);
        Assert.Equal(0.0, MushroomCloud.At(20.0 * Kt, reaches * 0.9, high).ColumnTop);

        double top = MushroomCloud.At(20.0 * Kt, reaches + MushroomCloud.ColumnClimbSeconds + 1.0, high).ColumnTop;
        output.WriteLine($"20 kt at {high:F0} m: the column tops out at {top:P0} of the cap's height");
        Assert.InRange(top, 0.1, 0.6);
    }

    [Fact]
    public void TheStemCarriesHowFarTheColumnReaches()
    {
        foreach (double radius in new[] { 0.0, 37.4, 1640.0, 12_000.0 })
        {
            foreach (double top in new[] { 0.0, 0.41, 1.0 })
            {
                (double r, double t) = MushroomCloud.UnpackStem(MushroomCloud.PackStem(radius, top));
                Assert.Equal(Math.Floor(radius), r);
                Assert.Equal(top, t, 2);
            }
        }
    }

    /// <summary>
    /// Too high for a column: no stem, nothing on the ground, a bigger fireball, and its debris
    /// climbing and swelling as one ball.
    /// </summary>
    [Fact]
    public void InThinAirTheDebrisIsABallAndTheGroundIsUntouched()
    {
        const double height = 40_000.0;
        const double thin = 0.003;

        Assert.False(MushroomCloud.IsThin(MushroomCloud.ThinAirRatio));
        Assert.True(MushroomCloud.IsThin(thin));

        MushroomCloud.Shape young = MushroomCloud.At(1000.0 * Kt, 2.0, height, thin);
        MushroomCloud.Shape old = MushroomCloud.At(1000.0 * Kt, 200.0, height, thin);

        foreach (MushroomCloud.Shape s in new[] { young, old })
        {
            Assert.Equal(0.0, s.StemShare);
            Assert.Equal(0.0, s.Coupling);
            Assert.Equal(0.0, s.Shock);
            Assert.True(s.CapCentre >= height);
        }

        Assert.True(old.CapCentre > young.CapCentre, "it climbs");
        Assert.True(old.CapTube > young.CapTube, "and swells");

        // Grown by the cube root of the thinning air, and sooner, since the pulse is quicker there.
        double dense = MushroomCloud.FlashAt(1000.0 * Kt, 1.0, height).Radius;
        double inThin = MushroomCloud.FlashAt(1000.0 * Kt, 1.0, height, thin).Radius;
        Assert.Equal(Math.Cbrt(1.0 / thin), MushroomCloud.ThinAirGrowth(thin), 12);
        Assert.True(inThin / dense >= Math.Cbrt(1.0 / thin), $"{inThin / dense:F2}");

        // The bound holds the whole ball through its life.
        double bound = MushroomCloud.RisenBound(1000.0, height, thin);
        for (double age = 1.0; age < MushroomCloud.LifeFor(1000.0); age += 20.0)
        {
            MushroomCloud.Shape s = MushroomCloud.At(1000.0 * Kt, age, height, thin);
            Assert.True(s.CapCentre + (2.0 * s.CapTube) <= bound, $"at {age} s the ball leaves its bound");
        }
    }

    [Fact]
    public void TheHeatCarriesTheCouplingAndTheStemInOneFloat()
    {
        foreach (double heat in new[] { 0.0, 0.02, 0.5, 0.98 })
        {
            foreach (double coupling in new[] { 0.0, 0.37, 1.0 })
            {
                foreach (double share in new[] { 0.0, 0.45, 1.0 })
                {
                    (double h, double c, double s) = MushroomCloud.UnpackHeat(MushroomCloud.PackHeat(heat, coupling, share));
                    Assert.Equal(heat, h, 3);
                    Assert.True(Math.Abs(coupling - c) <= 0.5 / 63.0 + 1e-9, $"coupling {coupling} came back {c}");
                    Assert.True(Math.Abs(share - s) <= 0.5 / 63.0 + 1e-9, $"share {share} came back {s}");
                }
            }
        }

        // A surface burst with its ball out is exactly nothing hot.
        Assert.Equal(0.0, MushroomCloud.UnpackHeat(MushroomCloud.PackHeat(0.0, 1.0, 1.0)).Heat);
    }

    [Fact]
    public void AHeightIsMeasuredOverTheSeaWhereThereIsSea()
    {
        Assert.Equal(4000.0, BurstSettings.HeightOver(4200.0, 200.0, 0.0, hasSea: true));
        Assert.Equal(4000.0, BurstSettings.HeightOver(4000.0, -3000.0, 0.0, hasSea: true));
        Assert.Equal(7000.0, BurstSettings.HeightOver(4000.0, -3000.0, 0.0, hasSea: false));
        Assert.Equal(0.0, BurstSettings.HeightOver(-10.0, 0.0, 0.0, hasSea: true));
        Assert.Equal(4000.0, BurstSettings.HeightOver(4000.0, double.NaN, 0.0, hasSea: false));
    }

    // ---- The fuse and the chute -------------------------------------------------------------

    private const double PlanetRadius = 6_371_000.0;

    private sealed class Ball : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Vec.Zero;
            surfaceRadius = PlanetRadius;
            return true;
        }
    }

    private static MunitionProfile Store(float burstHeight = 0f, float chuteSink = 0f) => new()
    {
        Name = "AIRBURST",
        DisplayName = "air-burst store",
        Guidance = GuidanceMode.None,
        LaunchSpeed = 0f,
        BoostSeconds = 0f,
        BoostAccel = 0f,
        MaxFlightSeconds = 900f,
        DragK = 1.25e-4f,
        FuseRadius = 0f,
        FuseArmSeconds = 2f,
        ChargeKg = 1f,
        HitsTerrain = true,
        BurstHeightMetres = burstHeight,
        ChuteSinkMetresPerSecond = chuteSink,
        ChuteOpensSeconds = 1.5f,
    };

    private static Slug Released(MunitionProfile munition, double altitude, double3 velocity = default)
    {
        double3 start = new(PlanetRadius + altitude, 0, 0);
        return new Slug(start, velocity, null, 1, start, Vec.Zero) { Munition = munition, Ground = new Ball() };
    }

    private static int Fall(Slug store, double dt = 1.0 / 60.0, int frames = 200_000)
    {
        int i = 0;
        for (; i < frames && store.State == RoundState.Flying; i++)
        {
            double3 gravity = Vec.Unit(-store.PositionEcl) * Medium.StandardGravity;
            store.Update(dt, null, gravity, Vec.Zero, Vec.Zero, store.Munition);
        }

        return i;
    }

    private static double Altitude(Slug store) => Vec.Len(store.PositionEcl) - PlanetRadius;

    [Fact]
    public void TheFuseFiresAtItsHeightOnTheWayDown()
    {
        Slug store = Released(Store(burstHeight: 500f), 3000.0, new double3(0, 200, 0));
        Fall(store);

        Assert.Equal(RoundState.Detonated, store.State);
        Assert.True(store.BurstAtHeight);
        Assert.False(store.HitGround);
        Assert.Equal(500.0, Altitude(store), 0);
        Assert.True(double.IsPositiveInfinity(store.MissDistance), "an air burst is not a direct hit");
    }

    /// <summary>
    /// Released under its own burst height, it never crosses it on the way down, so the ground ends
    /// it -- and one on the way up does not fire either.
    /// </summary>
    [Fact]
    public void AStoreReleasedUnderItsFuseHeightBurstsOnTheGround()
    {
        Slug store = Released(Store(burstHeight: 500f), 300.0, new double3(0, 200, 30));
        Fall(store);

        Assert.True(store.HitGround);
        Assert.False(store.BurstAtHeight);
    }

    [Fact]
    public void AStoreIsNotFusedBeforeItArms()
    {
        // Released just over the fuse height, it crosses it inside the two seconds it is safe for.
        Slug store = Released(Store(burstHeight: 500f), 505.0);
        Fall(store);

        Assert.True(store.HitGround);
    }

    [Fact]
    public void UnderItsChuteAStoreFallsAtItsSinkRate()
    {
        const float sink = 18f;
        Slug store = Released(Store(chuteSink: sink), 10_000.0, new double3(0, 240, 0));

        double3 last = store.PositionEcl;
        for (int i = 0; i < 60 * 60; i++)
        {
            double3 gravity = Vec.Unit(-store.PositionEcl) * Medium.StandardGravity;
            store.Update(1.0 / 60.0, null, gravity, Vec.Zero, Vec.Zero, store.Munition);
        }

        // A minute on it is steady, and at sea-level density its own drag barely adds to the chute's.
        double down = Vec.Len(store.VelocityEcl);
        output.WriteLine($"under the chute: {down:F2} m/s, {Altitude(store):F0} m up, "
                         + $"{Vec.Len(store.PositionEcl - last):F0} m from release");
        Assert.InRange(down, sink * 0.95, sink * 1.01);

        // Without one the same store is falling far faster.
        Slug free = Released(Store(), 10_000.0, new double3(0, 240, 0));
        for (int i = 0; i < 60 * 20 && free.State == RoundState.Flying; i++)
        {
            double3 gravity = Vec.Unit(-free.PositionEcl) * Medium.StandardGravity;
            free.Update(1.0 / 60.0, null, gravity, Vec.Zero, Vec.Zero, free.Munition);
        }

        Assert.True(Vec.Len(free.VelocityEcl) > 5.0 * sink);
    }

    [Fact]
    public void TheChuteIsShutUntilItOpensAndFillsOverASecond()
    {
        MunitionProfile store = Store(chuteSink: 18f);
        double3 falling = new(0, 0, -100);

        Assert.Equal(Vec.Zero, Medium.ChuteDrag(falling, store, 1.0, 1.4));
        double half = Vec.Len(Medium.ChuteDrag(falling, store, 1.0, 2.0));
        double full = Vec.Len(Medium.ChuteDrag(falling, store, 1.0, 3.0));

        Assert.Equal(0.5 * full, half, 6);

        // At the sink rate it holds the store up against standard gravity.
        Assert.Equal(Medium.StandardGravity,
                     Vec.Len(Medium.ChuteDrag(new double3(0, 0, -18), store, 1.0, 10.0)), 6);
        Assert.Equal(Vec.Zero, Medium.ChuteDrag(falling, Store(), 1.0, 10.0));
    }

    /// <summary>
    /// A probe flown from a store already under its canopy starts at the store's age, so the canopy is
    /// open from its first step. Started at zero it flies the first seconds of a fresh release -- the
    /// canopy shut and then filling -- and the store arrives early. Falling straight down under a
    /// canopy it arrives in the same place either way, so it is the time that tells.
    /// </summary>
    [Fact]
    public void AProbeFromAStoreInFlightKeepsItsCanopyOpen()
    {
        MunitionProfile store = Store(burstHeight: 1500f, chuteSink: 18f);
        double3 release = new(PlanetRadius + 8_000.0, 0, 0);
        const double step = 0.05;

        Slug real = new(release, new double3(0, 220, 0), null, 1, release, Vec.Zero) { Munition = store, Ground = new Ball() };
        for (int i = 0; i < 400; i++)
        {
            double3 g = Vec.Unit(-real.PositionEcl) * Medium.StandardGravity;
            real.Update(step, null, g, Vec.Zero, Vec.Zero, store);
        }

        double3 from = real.PositionEcl;
        double3 moving = real.VelocityEcl;
        double age = real.Age;
        double remaining = Fall(real, step) * step;

        double Predict(double startAge)
        {
            List<double3> path = [];
            double at = BombSight.StepFor(store, step);
            Assert.True(BombSight.TryPredict(from, moving, Vec.Zero, Vec.Zero, Vec.Zero, _ => Vec.Zero, store,
                                             p => Vec.Unit(-p) * Medium.StandardGravity, _ => 1.0, new Ball(),
                                             at, path, out _, null, startAge));
            return (path.Count - 1) * at;
        }

        double kept = Predict(age);
        double restarted = Predict(0.0);
        output.WriteLine($"{remaining:F1} s left to fall: predicted {kept:F1} s at its own age, {restarted:F1} s started at zero");

        Assert.True(Math.Abs(kept - remaining) < 0.7, $"at its own age the probe says {kept:F1} s, the store took {remaining:F1}");
        Assert.True(Math.Abs(restarted - remaining) > 1.0, "a probe started at zero should arrive visibly early");
    }

    /// <summary>
    /// The sight flies the same fuse, so its ring sits on the ground under where the store will go
    /// off rather than giving up because it never touched the ground.
    /// </summary>
    [Fact]
    public void TheSightMarksTheGroundUnderAnAirBurst()
    {
        MunitionProfile store = Store(burstHeight: 1500f, chuteSink: 18f);
        double3 release = new(PlanetRadius + 10_000.0, 0, 0);
        double3 overGround = new(0, 200, 0);

        List<double3> path = [];
        Assert.True(BombSight.TryPredict(release, overGround, Vec.Zero, Vec.Zero, Vec.Zero, _ => Vec.Zero, store,
                                         p => Vec.Unit(-p) * Medium.StandardGravity,
                                         _ => 1.0, new Ball(),
                                         BombSight.StepFor(store, 0.05), path, out double3 impact));

        Assert.Equal(0.0, Vec.Len(impact) - PlanetRadius, 3);
        Assert.Equal(1500.0, Vec.Len(path[^1]) - PlanetRadius, 0);

        // Where the same store bursts, flown for real.
        Slug real = new(release, overGround, null, 1, release, Vec.Zero) { Munition = store, Ground = new Ball() };
        Fall(real, 0.05);
        double3 underReal = Vec.Unit(real.PositionEcl) * PlanetRadius;
        output.WriteLine($"ring {Vec.Len(impact - underReal):F1} m from under the real burst, "
                         + $"{path.Count} steps");
        Assert.True(Vec.Len(impact - underReal) < 5.0);
    }
}
