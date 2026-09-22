using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The mushroom clouds standing in the world, and what draws them.
///
/// <para><see cref="MushroomCloud"/> says what shape the cloud is at an age; this walks that shape
/// with a handful of <see cref="PlumeSmoke.Strand"/> cursors, one climbing for the stem and a ring
/// of them tracing the cap. Neither of KSA's volumetric renderers has drag or a vortex field, so
/// the roll-up is drawn rather than simulated.</para>
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
    // Pens on the rim, and inside it. One engine rule decides these, and it is arithmetic.
    //
    // Coverage: smoke is a capsule whose core reaches 0.55 of its radius, so pens further apart
    // than 1.1 radii leave clear air between them and the cloud reads as ropes. Three concentric
    // shells, each one's outer edge reaching the next one's inner, so the cap is filled rather than
    // a shell with a hollow axis.
    //
    // The engine's emitter GROUPING does not apply to any of this, and reasoning as though it did
    // is what sized the handover tube three times too thin. Mod pens are tracked and then cleared
    // before the merge runs -- docs/NUCLEAR-EFFECT.md has the frame ordering. What overlapping
    // capsules do here is the raymarcher's own rule: the deeper of the two, never the sum.
    private const int MidStrands = 12;

    private const double CoreShell = 0.18;

    // Where the fireball rides, in the same terms. Inside the bundle rather than on the outermost
    // ring, because it is the cloud's core.

    // The ground skirt has no counterpart here: its pen count and tube are MushroomCloud's, because
    // the tube is derived from the count and splitting the two across this boundary is what drifts.
    //
    // Its ring is small enough that the tube sits at its floor rather than at the pitch, which
    // inverts the trade above -- a pen there is more smoke rather than a thinner tube -- so the
    // count is the smallest that still closes at the widest the skirt gets.

    // And a stem that is one column rather than a bundle of poles, which needs the spread to stay
    // inside the tube: overlapping capsules render as the deeper of the two, so nine parallel pens
    // closer together than their own radius are one column and no wider than one of them.
    private const double StemSpread = 0.24;

    // Radii, in metres, as fractions of the cap's own tube. The expansion ratio matters as much as
    // the size: a booster's plume swells a hundredfold from its nozzle, which is what makes it
    // billow, and 1.4x reads as a pipe.
    private const double CapInitial = 0.31;
    private const double StemInitial = 0.16;

    private sealed class Cloud
    {
        public required Celestial Body;
        public required double3 BurstCcf;
        public required double3 Up;
        public required double3 East;
        public required double3 North;
        public required double ChargeKg;

        // Which way this one leans. Fixed per cloud rather than per frame, or the column would
        // wander; and per cloud rather than global, so two bursts in sight of each other do not
        // lean identically.
        public required double3 Downwind;

        public double Age;
        public double LastReport = -99.0;

        // Where this stem ends up, so a pen can be told how far up its own finished column it is.
        // Fixed per cloud: the shape is a pure function of charge and age, so this is knowable at
        // the burst and never changes.
        public required double FullStem;
    }

    private static readonly List<Cloud> _clouds = [];

    /// <summary>How many clouds are standing. Diagnostic.</summary>
    public static int Count => _clouds.Count;

    /// <summary>
    /// The newest cloud standing, as the shader pass needs it: where it is in the ecliptic, which
    /// way is up there, how far it reaches and how old it is.
    ///
    /// <para>One, because a push constant holds one and because two mushroom clouds in sight of
    /// each other is not a case anybody has. The newest rather than the nearest: it is the one
    /// still changing shape, and a settled cloud is the one that can afford to be drawn by pens.
    /// </para>
    /// </summary>
    public static bool TryNewest(out double3 burstEcl, out double3 up, out double radiusMetres,
                                 out double ageSeconds, out MushroomCloud.Shape shape,
                                 out double3 downwind)
    {
        downwind = default;

        burstEcl = default;
        up = default;
        radiusMetres = 0.0;
        ageSeconds = 0.0;
        shape = default;

        if (_clouds.Count == 0) return false;

        Cloud cloud = _clouds[^1];

        try
        {
            burstEcl = cloud.Body.GetPositionEcl()
                       + cloud.BurstCcf.Transform(cloud.Body.GetCce2Ccf().Inverse());
            up = cloud.Up.Transform(cloud.Body.GetCce2Ccf().Inverse());
            ageSeconds = cloud.Age;
            shape = MushroomCloud.At(cloud.ChargeKg, cloud.Age);

            // The same wind the pens lean into, so the two drawings of one burst agree about which
            // way it is blowing rather than each choosing.
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
                BurstEjecta.Begin(body, burstCcf, chargeKg,
                                  KsaWorld.HeightAboveTerrain(body, burstEcl));
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
                East = east,
                North = north,
                Downwind = Vec.Unit((east * Math.Cos(bearing)) + (north * Math.Sin(bearing))),
                ChargeKg = chargeKg,
                FullStem = MushroomCloud.At(chargeKg, MushroomCloud.RiseSeconds).StemTop,
            });

            double kt = MushroomCloud.KilotonsFor(chargeKg);
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
        Fireball.Clear();
    }
}
