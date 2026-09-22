using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The mushroom clouds standing in the world, and what draws them.
///
/// <para>This holds where each one is and how old it is; <see cref="MushroomCloud"/> says what
/// shape that age makes, and <see cref="CloudPass"/> raymarches it. Nothing here draws the column
/// itself — what this class still draws directly is the fireball over it.</para>
///
/// <para><b>Everything is body-fixed.</b> The burst is converted to the body's rotating frame once,
/// and every position after that is an offset in it — so the cloud stands over the ground it was
/// made on, through the planet's spin and its 29.8 km/s around the star. Held in the ecliptic it
/// would be left behind within a frame, which is the same trap the bomb sight fell into.</para>
///
/// <para>Advanced on <b>simulated</b> time. A cloud is a thing in the world rather than a viewing
/// duration, so it freezes with a pause and slows with the panel's slow-motion.</para>
/// </summary>
internal static class NuclearClouds
{
    private sealed class Cloud
    {
        public required Celestial Body;
        public required double3 BurstCcf;
        public required double3 Up;
        public double ChargeKg;

        // Which way this one leans. Fixed per cloud rather than per frame, or the column would
        // wander; and per cloud rather than global, so two bursts in sight of each other do not
        // lean identically.
        public required double3 Downwind;

        public double Age;
    }

    private static readonly List<Cloud> _clouds = [];

    // The newest burst worth pointing a camera at, which is NOT the newest cloud: an airless burst
    // grows no column and is still the thing to watch. Body-fixed like the clouds, and flat rather
    // than a list because nothing about it advances -- how big a burst draws is settled when it
    // happens.
    private static (Celestial Body, double3 BurstCcf, double3 Up, AirlessBurst.Extent Drawn)? _watch;

    /// <summary>How many clouds are standing. Diagnostic.</summary>
    public static int Count => _clouds.Count;

    /// <summary>
    /// The newest cloud standing, as the shader pass needs it: where it is in the ecliptic, which
    /// way is up there, how far it reaches and how old it is.
    ///
    /// <para>The newest rather than the nearest, so whatever is asked about one cloud is asked
    /// about the one still changing shape. <see cref="TryAt"/> is how the pass reaches the
    /// rest.</para>
    /// </summary>
    public static bool TryNewest(out double3 burstEcl, out double3 up, out double radiusMetres,
                                 out double ageSeconds, out MushroomCloud.Shape shape,
                                 out double3 downwind)
        => TryAt(_clouds.Count - 1, out burstEcl, out up, out radiusMetres, out ageSeconds,
                 out shape, out downwind);

    /// <summary>
    /// One standing cloud by index, in the order they were made. <see cref="Count"/> bounds it.
    ///
    /// <para>The pass draws them one dispatch each, so it needs them all rather than the newest —
    /// a six-warhead bus makes six of these.</para>
    /// </summary>
    public static bool TryAt(int index, out double3 burstEcl, out double3 up, out double radiusMetres,
                             out double ageSeconds, out MushroomCloud.Shape shape,
                             out double3 downwind)
    {
        downwind = default;

        burstEcl = default;
        up = default;
        radiusMetres = 0.0;
        ageSeconds = 0.0;
        shape = default;

        if (index < 0 || index >= _clouds.Count) return false;

        Cloud cloud = _clouds[index];

        try
        {
            burstEcl = cloud.Body.GetPositionEcl()
                       + cloud.BurstCcf.Transform(cloud.Body.GetCce2Ccf().Inverse());
            up = cloud.Up.Transform(cloud.Body.GetCce2Ccf().Inverse());
            ageSeconds = cloud.Age;
            shape = MushroomCloud.At(cloud.ChargeKg, cloud.Age);

            // Chosen at the burst rather than per frame, so the column leans one way for its whole
            // life instead of wandering.
            downwind = cloud.Downwind.Transform(cloud.Body.GetCce2Ccf().Inverse());

            // The whole thing, cap and lean included, so the bounding sphere cannot clip the shape
            // it is there to reject against.
            double kt = MushroomCloud.KilotonsFor(cloud.ChargeKg);
            radiusMetres = MushroomCloud.DrawnCloudTop(kt);

            return Vec.IsFinite(burstEcl) && Vec.IsFinite(up) && Vec.IsFinite(downwind)
                   && radiusMetres > 0.0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The newest burst, as a camera has to frame it: where it is, which way is up there, how far
    /// across the whole thing reaches and how high its top stands.
    ///
    /// <para>Separate from <see cref="TryNewest"/> because the two answer different questions. That
    /// one is the shader's, and there is nothing for it to march on an airless body; this one is
    /// the camera's, and a dust dome is every bit as much a thing to look at as a column. The two
    /// numbers are both needed because a dome is four times wider than it is tall, so a view framed
    /// on height alone stands far too close.</para>
    /// </summary>
    public static bool TryWatch(out double3 burstEcl, out double3 up,
                                out double radiusMetres, out double topMetres)
    {
        burstEcl = default;
        up = default;
        radiusMetres = 0.0;
        topMetres = 0.0;

        if (_watch is not { } watch || watch.Drawn.Empty) return false;

        try
        {
            burstEcl = watch.Body.GetPositionEcl()
                       + watch.BurstCcf.Transform(watch.Body.GetCce2Ccf().Inverse());
            up = watch.Up.Transform(watch.Body.GetCce2Ccf().Inverse());
            radiusMetres = watch.Drawn.RadiusMetres;
            topMetres = watch.Drawn.TopMetres;

            return Vec.IsFinite(burstEcl) && Vec.IsFinite(up) && radiusMetres > 0.0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Starts whatever a burst leaves standing, if the charge is big enough to have left anything.
    ///
    /// <para>Silent for a conventional warhead: a 500 lb bomb does not grow a mushroom, and
    /// deciding that here rather than at the call site keeps every caller from having to know.</para>
    ///
    /// <para><b>A cloud is only one of the two answers.</b> On a body with no air this hands over
    /// to <see cref="BurstEjecta"/>, which throws dust instead — so a burst still has one entry
    /// point and no caller has to know which kind of body it happened over.</para>
    /// </summary>
    public static void Begin(double3 burstEcl, Vehicle? near, double chargeKg, Celestial? known = null)
    {
        if (chargeKg < MushroomCloud.ThresholdKg) return;
        if (Detonation.BodyFor(near, known) is not { } body) return;
        if (!Vec.IsFinite(burstEcl)) return;

        try
        {
            double3 burstCcf = (burstEcl - body.GetPositionEcl()).Transform(body.GetCce2Ccf());
            if (!Vec.IsFinite(burstCcf) || Vec.Len2(burstCcf) < 1.0) return;

            // No air refuses a cloud twice over: a mushroom is buoyant and has nothing to rise
            // through, and KSA raymarches the trail volume only for an AtmosphericBody, so smoke
            // laid here would draw nowhere at any altitude. Before this the cloud was built anyway
            // and its dimensions logged, over a body where nobody could ever see one.
            if (!KsaWorld.HasAtmosphere(body))
            {
                // Above the GROUND, not above the mean sphere: what decides a surface burst is
                // whether the fireball touches the terrain that is there, and on the Moon the two
                // differ by kilometres.
                AirlessBurst.Extent thrown = BurstEjecta.Begin(
                    body, burstCcf, chargeKg, KsaWorld.HeightAboveTerrain(body, burstEcl));

                _watch = (body, burstCcf, Vec.Unit(burstCcf), thrown);
                return;
            }

            // A burst inside a standing cloud's own fireball is the SAME EVENT, and is added to it
            // rather than starting another. Six warheads of a bus land about 9 mm apart: drawn as
            // six clouds that is six times the smoke in one place and six full-screen dispatches
            // marching the same pixels, where the truth is one burst of the combined yield.
            for (int i = 0; i < _clouds.Count; i++)
            {
                Cloud standing = _clouds[i];
                if (!ReferenceEquals(standing.Body, body)) continue;

                double reach = MushroomCloud.PeakFireballRadius(
                    MushroomCloud.KilotonsFor(standing.ChargeKg + chargeKg));

                if (Vec.Len2(burstCcf - standing.BurstCcf) > reach * reach) continue;

                standing.ChargeKg += chargeKg;

                Log.Info($"nuclear cloud: burst {Vec.Len(burstCcf - standing.BurstCcf):F1} m from a "
                         + $"standing one and inside its {reach:F0} m fireball, so it is that one -- "
                         + $"now {MushroomCloud.KilotonsFor(standing.ChargeKg):F2} kt");
                return;
            }

            // Local vertical in the body's own frame, which is just the way out from its centre.
            // The other two only have to be perpendicular: the shape has an axis of symmetry, so
            // which way "east" points is not a question anybody has to answer.
            double3 up = Vec.Unit(burstCcf);
            double3 east = Vec.Unit(Vec.AnyPerpendicular(up));
            double3 north = Vec.Unit(Vec.Cross(up, east));

            // A bearing for the wind aloft, taken off where the burst is rather than from a clock,
            // so the same crater leans the same way every time. Two bursts within sight of each
            // other land on nearly the same bearing, which is right: they stand in one wind.
            double bearing = Math.Tau * ((Math.Abs(burstCcf.X) + Math.Abs(burstCcf.Z)) * 0.001 % 1.0);

            _clouds.Add(new Cloud
            {
                Body = body,
                BurstCcf = burstCcf,
                Up = up,
                Downwind = Vec.Unit((east * Math.Cos(bearing)) + (north * Math.Sin(bearing))),
                ChargeKg = chargeKg,
            });

            double kt = MushroomCloud.KilotonsFor(chargeKg);

            // The dust along the ground, which the raymarch does not draw and never could: its
            // push constant carries the column's four numbers and has no room for the skirt's.
            BurstEjecta.BeginSurge(body, burstCcf, chargeKg);

            // A column is about as tall as it is wide, so one number frames it both ways.
            _watch = (body, burstCcf, up,
                      new AirlessBurst.Extent(MushroomCloud.DrawnCloudTop(kt),
                                              MushroomCloud.DrawnCloudTop(kt)));

            // At its largest, not at age zero: the ramp is at 60% there, and a diagnostic that
            // reports the smallest the thing ever is sends the next reader looking in the wrong place.
            MushroomCloud.Flash peak = MushroomCloud.FlashAt(chargeKg, MushroomCloud.FlashSeconds(kt) * 0.1);

            // What is drawn, with the law beside it: they differ by MushroomCloud.DrawnScale on
            // purpose, and a diagnostic reporting only the law sends the next reader to the wrong
            // place when the thing on screen is not the size it says.
            // Where the burst was, because a cloud with no column under it is the correct drawing
            // of an airburst and the wrong drawing of a surface one. Against the mean sphere, which
            // is what the shape's own heights are measured from.
            double burstAlt = Vec.Len(burstCcf) - body.MeanRadius;

            Log.Info($"nuclear cloud: {kt:F2} kt at {burstAlt:F0} m altitude, rising to "
                     + $"{MushroomCloud.DrawnCloudTop(kt) / 1000.0:F2} km, "
                     + $"cap {MushroomCloud.DrawnCapRadius(kt) * 2.0 / 1000.0:F2} km across "
                     + $"(drawn at {MushroomCloud.DrawnScale:P0} of the law's "
                     + $"{MushroomCloud.CloudTop(kt) / 1000.0:F2} km)");
            Log.Info($"  fireball {peak.Radius:F0} m for {MushroomCloud.FlashSeconds(kt):F1} s, "
                     + $"glow {peak.Glow:F0} ({Fireball.BloomingEmissive(new float3(
                           (float)peak.Colour.X, (float)peak.Colour.Y, (float)peak.Colour.Z)):F1} "
                     + $"blooms), light {(Fireball.LightAccepted ? "on" : "STOOD DOWN")}, "
                     + $"smoke waits {MushroomCloud.FlashSeconds(kt):F1} s");
        }
        catch (Exception e)
        {
            Log.Warn($"nuclear cloud: could not start one ({e.Message})");
        }
    }

    /// <summary>Advances every cloud and lays this frame's smoke.</summary>
    public static void Update(double dtSim)
    {
        if (_clouds.Count == 0)
        {
            Fireball.Clear();
            return;
        }

        if (!double.IsFinite(dtSim)) return;

        double step = Math.Max(0.0, dtSim);

        // The light is re-submitted per frame, so a frame with no flash in it has to say so.
        bool lit = false;

        for (int i = _clouds.Count - 1; i >= 0; i--)
        {
            Cloud cloud = _clouds[i];
            cloud.Age += step;

            MushroomCloud.Shape shape = MushroomCloud.At(cloud.ChargeKg, cloud.Age);
            if (cloud.Age > 0.0 && shape.Spent) { _clouds.RemoveAt(i); continue; }

            MushroomCloud.Flash flash = MushroomCloud.FlashAt(cloud.ChargeKg, cloud.Age);
            if (flash.Spent) continue;

            lit = true;

            double3 riseCcf = cloud.Up * MushroomCloud.EmberHeight(cloud.ChargeKg, cloud.Age);

            Fireball.Draw(cloud.Body.GetPositionEcl()
                          + (cloud.BurstCcf + riseCcf).Transform(cloud.Body.GetCce2Ccf().Inverse()),
                          flash.Radius,
                          new float3((float)flash.Colour.X, (float)flash.Colour.Y,
                                     (float)flash.Colour.Z),
                          (float)flash.Glow);
        }

        if (!lit) Fireball.Clear();
    }

    /// <summary>Forgets every cloud, for a scene that no longer contains them.</summary>
    public static void Clear()
    {
        _clouds.Clear();
        _watch = null;
        Fireball.Clear();
    }
}
