using System.Reflection;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Every nuclear burst's radiation still in the world, read by KSA's own dose gauge through
/// <see cref="DoseRateHook"/>. A burst above the air also fills a belt in KSA's own magnetosphere
/// model, so the gauge's orbit average and the map's Magnetospheres overlay show it as well.
/// </summary>
internal static class Radioactivity
{
    private sealed class Source
    {
        public required Celestial Body;
        public required BodyAir Air;
        public required double3 BurstCce;
        public required double3 GroundCcf;
        public required double3 Up;
        public required double3 Downwind;
        public required double Kt;
        public required BurstRadiation.Plume Plume;
        public required bool Cloud;
        public double Age;
    }

    private sealed class BeltShare
    {
        public required double L;
        public required double Width;
        public required double PeakMilliSvPerHour;
        public required double LifeSeconds;
        public double Age;

        public double Now => PeakMilliSvPerHour * Math.Exp(-Age / LifeSeconds);
    }

    private sealed class Belt
    {
        public required MagnetosphereReference Field;
        public readonly List<BeltShare> Shares = [];
        public RadiationBeltReference? Written;
    }

    private const int MaxSources = 32;
    private const double NegligibleSvPerSecond = 1.0e-10;
    private const double NegligibleBeltMilliSvPerHour = 0.01;

    // KSA re-averages an orbit's dose only when the orbit moves, so a belt written under a coasting craft
    // is re-written at most this often, each time telling KSA to average again.
    private const double BeltRewriteSeconds = 60.0;

    private static readonly List<Source> _sources = [];
    private static readonly List<Belt> _belts = [];
    private static double _sinceBeltWrite;
    private static double _sinceLog;
    private static bool _cacheUnreachable;

    public static void Begin(Celestial body, double3 burstEcl, double3 burstCcf, double3 groundCcf,
                             double3 up, double3 downwind, double chargeKg, double coupling, bool hasAir,
                             bool thin)
    {
        double kt = MushroomCloud.KilotonsFor(chargeKg);
        if (!(kt > 0.0)) return;

        BodyAir air = KsaWorld.BodyAirOf(body);
        BurstRadiation.Plume plume = BurstRadiation.Plume.For(kt, coupling, hasAir);

        if (_sources.Count >= MaxSources) _sources.RemoveAt(0);
        _sources.Add(new Source
        {
            Body = body,
            Air = air,
            BurstCce = burstEcl - body.GetPositionEcl(),
            GroundCcf = groundCcf,
            Up = up,
            Downwind = downwind,
            Kt = kt,
            Plume = plume,
            Cloud = hasAir && !thin,
        });

        double density = air.DensityAt(Vec.Len(burstCcf) - air.RadiusMetres);
        Log.Info($"radiation: {kt:G3} kt, {plume.FissionKt:G3} kt of it fission; prompt dose "
                 + string.Join(", ", new[] { 1_000.0, 3_000.0, 10_000.0, 100_000.0 }.Select(r =>
                       $"{BurstRadiation.PromptDoseSv(kt, r, density * r):G2} Sv at {r / 1000.0:F0} km"))
                 + (plume.Lays
                        ? $"; fallout {plume.Local:P0} local, {plume.UnitRate(0.0, 0.0) * BurstRadiation.SecondsPerHour:G3} Sv/h "
                          + $"at ground zero at H+1, the plume's mean fall {plume.MeanFallSeconds / 60.0:F0} min"
                        : "; no local fallout"));

        if (thin || !hasAir) FillBelt(body, burstCcf, plume.FissionKt);
    }

    /// <summary>The rate this mod's bursts add at a point (Sv/s), unshielded.</summary>
    public static double DoseRateAt(double3 positionEcl)
    {
        double rate = 0.0;

        foreach (Source source in _sources)
        {
            try
            {
                rate += RateFrom(source, positionEcl);
            }
            catch
            {
                // A body gone from under a burst irradiates nothing.
            }
        }

        return double.IsFinite(rate) ? rate : 0.0;
    }

    private static double RateFrom(Source s, double3 positionEcl)
    {
        double3 cce = positionEcl - s.Body.GetPositionEcl();
        double rate = 0.0;

        if (s.Age < 10.0 * BurstRadiation.PromptSeconds(s.Kt))
        {
            double dose = BurstRadiation.PromptDoseSv(s.Kt, Vec.Len(cce - s.BurstCce),
                                                      BurstRadiation.Column(s.Air, s.BurstCce, cce));
            rate += BurstRadiation.PromptRate(dose, s.Kt, s.Age);
        }

        double3 ccf = cce.Transform(s.Body.GetCce2Ccf());
        double ground = Vec.Len(s.GroundCcf);
        double3 dir = Vec.Unit(ccf);

        if (s.Plume.Lays)
        {
            double along = Vec.Dot(dir, s.Downwind);
            double3 side = dir - (s.Up * Vec.Dot(dir, s.Up)) - (s.Downwind * along);
            double height = Math.Max(Vec.Len(ccf) - ground, 0.0);
            double groundAlt = ground - s.Air.RadiusMetres;
            double column = s.Air.HasAir
                                ? s.Air.ColumnAbove(groundAlt) - s.Air.ColumnAbove(groundAlt + height)
                                : 0.0;

            rate += s.Plume.GroundRate(along * ground, Vec.Len(side) * ground, s.Age)
                    * BurstRadiation.GammaThrough(column);
        }

        if (s.Cloud)
        {
            (double drift, double up, double radius) = s.Plume.Cloud(s.Kt, s.Age);
            double3 centre = Vec.Unit(s.GroundCcf + (s.Downwind * drift)) * (ground + up);
            double distance = Vec.Len(ccf - centre);

            rate += BurstRadiation.CloudRate(s.Plume.AloftKt(s.Age), s.Age, distance, radius,
                                             BurstRadiation.Column(s.Air, centre, ccf, 8));
        }

        return rate;
    }

    public static void Update(double dtSim)
    {
        double step = double.IsFinite(dtSim) ? Math.Max(dtSim, 0.0) : 0.0;

        for (int i = _sources.Count - 1; i >= 0; i--)
        {
            Source s = _sources[i];
            s.Age += step;
            if (Spent(s)) _sources.RemoveAt(i);
        }

        _sinceBeltWrite += step;
        if (_belts.Count > 0 && _sinceBeltWrite >= BeltRewriteSeconds)
        {
            foreach (Belt belt in _belts)
            {
                foreach (BeltShare share in belt.Shares) share.Age += _sinceBeltWrite;
            }

            _sinceBeltWrite = 0.0;
            WriteBelts();
        }

        _sinceLog += step;
        if (_sources.Count > 0 && _sinceLog >= 10.0)
        {
            _sinceLog = 0.0;
            if (KsaWorld.ControlledVehicle is { IsDisposed: false } craft)
            {
                double here = DoseRateAt(craft.GetPositionEcl());
                Log.Debug(() => $"radiation: {here * BurstRadiation.SecondsPerHour * 1000.0:G3} mSv/h from "
                                + $"{_sources.Count} burst(s) at {craft.Id}");
            }
        }
    }

    private static bool Spent(Source s)
    {
        if (s.Age < 10.0 * BurstRadiation.PromptSeconds(s.Kt) || s.Age < BurstRadiation.PromptPhaseSeconds) return false;

        double ground = s.Plume.UnitRate(0.0, 0.0) * BurstRadiation.Decay(s.Age);
        double aloft = 0.0;
        if (s.Cloud)
        {
            double radius = s.Plume.Cloud(s.Kt, s.Age).Radius;
            aloft = BurstRadiation.CloudRate(s.Plume.AloftKt(s.Age), s.Age, 0.0, radius, 0.0);
        }

        return ground < NegligibleSvPerSecond && aloft < NegligibleSvPerSecond;
    }

    // Only into a field that already holds belts: KSA declares belts where a field is strong enough to
    // trap, and a field it gives none to is one this burst could not fill either.
    private static void FillBelt(Celestial body, double3 burstCcf, double fissionKt)
    {
        try
        {
            if (body.BodyTemplate?.MagnetosphereReference is not { Belts.Count: > 0 } field) return;

            double l = Radiation.ComputeL(field.ToMag(burstCcf), body.MeanRadius, out _);
            double peak = BurstRadiation.BeltPeakMilliSvPerHour(fissionKt, l);
            if (!double.IsFinite(l) || !(peak > NegligibleBeltMilliSvPerHour)) return;

            Belt? belt = _belts.Find(b => ReferenceEquals(b.Field, field));
            if (belt is null) _belts.Add(belt = new Belt { Field = field });

            belt.Shares.Add(new BeltShare
            {
                L = l,
                Width = BurstRadiation.BeltWidth(l),
                PeakMilliSvPerHour = peak,
                LifeSeconds = BurstRadiation.BeltLifeSeconds(l),
            });

            WriteBelts();
            Log.Info($"radiation: the burst fills a belt round {body.Id} at L {l:F2}, "
                     + $"{peak:G3} mSv/h at its peak, holding for {BurstRadiation.BeltLifeSeconds(l) / 86_400.0:F0} days");
        }
        catch (Exception e)
        {
            Log.Warn($"radiation: could not fill a belt ({e.Message})");
        }
    }

    // Fission debris fills a belt with beta electrons, as Core declares Earth's outer belt.
    private const double FissionBeltProtonFraction = 0.0;

    // One belt per field, the shares merged by strength, because KSA draws at most four.
    private static void WriteBelts()
    {
        for (int i = _belts.Count - 1; i >= 0; i--)
        {
            Belt belt = _belts[i];
            belt.Shares.RemoveAll(s => s.Now < NegligibleBeltMilliSvPerHour);

            if (belt.Written is { } old) belt.Field.Belts.Remove(old);
            belt.Written = null;

            if (belt.Shares.Count == 0)
            {
                _belts.RemoveAt(i);
                continue;
            }

            double total = belt.Shares.Sum(s => s.Now);
            double l = belt.Shares.Sum(s => s.Now * s.L) / total;
            double width = Math.Sqrt(belt.Shares.Sum(s => s.Now * ((s.Width * s.Width) + ((s.L - l) * (s.L - l)))) / total);

            belt.Written = new RadiationBeltReference(l, width, total, protonFraction: FissionBeltProtonFraction);
            belt.Field.Belts.Add(belt.Written);
        }

        ForgetOrbitAverage();
    }

    // Every orbit average KSA keeps, the gauge's and each crewed craft's: a cache holding a field's old
    // belts goes on dosing crew at warp until the sun has moved a degree, about a day.
    private static void ForgetOrbitAverage()
    {
        if (_cacheUnreachable) return;

        try
        {
            const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
            const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

            object? gauge = typeof(Program).GetField("_orbitDoseAverageCache", Static)?.GetValue(null);
            FieldInfo? field = gauge?.GetType().GetField("_magnetosphere", Instance);
            FieldInfo? crew = typeof(Vehicle).GetField("_orbitDoseAverageCache", Instance);
            if (gauge is null || field is null || crew is null)
                throw new MissingFieldException("an OrbitDoseAverageCache._magnetosphere");

            field.SetValue(gauge, null);
            if (typeof(Program).GetField("_burnOrbitDoseAverageCache", Static)?.GetValue(null) is { } burn)
                field.SetValue(burn, null);

            foreach (Vehicle vehicle in KsaWorld.Vehicles)
            {
                if (crew.GetValue(vehicle) is { } cache) field.SetValue(cache, null);
            }
        }
        catch (Exception e)
        {
            _cacheUnreachable = true;
            Log.Warn($"radiation: KSA's orbit dose averages cannot be refreshed, so the gauge and crew at warp "
                     + $"see a filled or emptied belt only once the orbit changes ({e.Message})");
        }
    }

    public static void Clear()
    {
        _sources.Clear();

        foreach (Belt belt in _belts)
        {
            if (belt.Written is { } written) belt.Field.Belts.Remove(written);
        }

        if (_belts.Count > 0) ForgetOrbitAverage();
        _belts.Clear();
        _sinceBeltWrite = 0.0;
    }
}
