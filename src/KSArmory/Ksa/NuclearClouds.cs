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
    private const int RimStrands = 18;
    private const int MidStrands = 12;
    private const int CoreStrands = 6;

    private const double MidShell = 0.62;
    private const double CoreShell = 0.18;

    // Where the fireball rides, in the same terms. Inside the bundle rather than on the outermost
    // ring, because it is the cloud's core.
    private const double EmberShell = 0.55;

    // The ground skirt has no counterpart here: its pen count and tube are MushroomCloud's, because
    // the tube is derived from the count and splitting the two across this boundary is what drifts.
    //
    // Its ring is small enough that the tube sits at its floor rather than at the pitch, which
    // inverts the trade above -- a pen there is more smoke rather than a thinner tube -- so the
    // count is the smallest that still closes at the widest the skirt gets.

    // And a stem that is one column rather than a bundle of poles, which needs the spread to stay
    // inside the tube: overlapping capsules render as the deeper of the two, so nine parallel pens
    // closer together than their own radius are one column and no wider than one of them.
    private const int StemStrands = 9;
    private const double StemSpread = 0.24;

    // Radii, in metres, as fractions of the cap's own tube. The expansion ratio matters as much as
    // the size: a booster's plume swells a hundredfold from its nozzle, which is what makes it
    // billow, and 1.4x reads as a pipe.
    private const double CapInitial = 0.31;
    private const double CapExpanded = 0.65;
    private const double StemInitial = 0.16;
    private const double StemExpanded = 0.45;

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

        public readonly PlumeSmoke.Strand[] Stem =
            Enumerable.Range(0, StemStrands).Select(_ => new PlumeSmoke.Strand()).ToArray();
        public readonly PlumeSmoke.Strand[] Rim =
            Enumerable.Range(0, RimStrands).Select(_ => new PlumeSmoke.Strand()).ToArray();
        public readonly PlumeSmoke.Strand[] Mid =
            Enumerable.Range(0, MidStrands).Select(_ => new PlumeSmoke.Strand()).ToArray();
        public readonly PlumeSmoke.Strand[] Core =
            Enumerable.Range(0, CoreStrands).Select(_ => new PlumeSmoke.Strand()).ToArray();
        public readonly PlumeSmoke.Strand[] Surge =
            Enumerable.Range(0, MushroomCloud.SurgeStrands)
                      .Select(_ => new PlumeSmoke.Strand()).ToArray();
    }

    private static readonly List<Cloud> _clouds = [];
    private static bool _tinted;

    /// <summary>How many clouds are standing. Diagnostic.</summary>
    public static int Count => _clouds.Count;

    /// <summary>
    /// Starts a cloud over a burst, if the charge is big enough to have made one.
    ///
    /// <para>Silent for a conventional warhead: a 500 lb bomb does not grow a mushroom, and
    /// deciding that here rather than at the call site keeps every caller from having to know.</para>
    /// </summary>
    public static void Begin(double3 burstEcl, Vehicle? near, double chargeKg)
    {
        if (chargeKg < MushroomCloud.ThresholdKg) return;
        if (Detonation.BodyFor(near) is not { } body) return;
        if (!Vec.IsFinite(burstEcl)) return;
        if (!PlumeSmoke.Available) return;

        try
        {
            double3 burstCcf = (burstEcl - body.GetPositionEcl()).Transform(body.GetCce2Ccf());
            if (!Vec.IsFinite(burstCcf) || Vec.Len2(burstCcf) < 1.0) return;

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
            });

            double kt = MushroomCloud.KilotonsFor(chargeKg);
            // At its largest, not at age zero: the ramp is at 60% there, and a diagnostic that
            // reports the smallest the thing ever is sends the next reader looking in the wrong place.
            MushroomCloud.Flash peak = MushroomCloud.FlashAt(chargeKg, MushroomCloud.FlashSeconds(kt) * 0.1);

            // What is drawn, with the law beside it: they differ by MushroomCloud.DrawnScale on
            // purpose, and a diagnostic reporting only the law sends the next reader to the wrong
            // place when the thing on screen is not the size it says.
            Log.Info($"nuclear cloud: {kt:F2} kt rising to "
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
    public static void Update(double dtSim, bool dirty)
    {
        // Put the world's smoke back the moment the last cloud goes, so a booster is only tinted
        // while there is actually something standing to justify it.
        if (_clouds.Count == 0)
        {
            if (_tinted) { PlumeSmoke.Tint(false); _tinted = false; }
            return;
        }

        if (_tinted != dirty) { PlumeSmoke.Tint(dirty); _tinted = dirty; }

        // The light is re-submitted per frame, so a frame with no flash in it has to say so.
        bool lit = false;

        if (!double.IsFinite(dtSim)) return;

        // A paused world still submits, at the age it already had.
        //
        // Skipping the frame instead looks harmless and is not: a pen that misses one submission is
        // deactivated by the renderer, which closes the segment it was holding and breaks the chain
        // the merge and the level of detail both walk. The cloud comes back from a pause seamed.
        double step = Math.Max(0.0, dtSim);

        for (int i = _clouds.Count - 1; i >= 0; i--)
        {
            Cloud cloud = _clouds[i];
            cloud.Age += step;

            // One clock, from the burst. The smoke is withheld while the fireball is luminous, but
            // the cloud goes on ageing underneath it, so the pens are already up at the ball when
            // they start laying rather than back down on the ground.
            double progress = MushroomCloud.Progress(cloud.Age);

            MushroomCloud.Shape shape = MushroomCloud.At(cloud.ChargeKg, cloud.Age);
            if (cloud.Age > 0.0 && shape.Spent) { _clouds.RemoveAt(i); continue; }

            MushroomCloud.Flash flash = MushroomCloud.FlashAt(cloud.ChargeKg, cloud.Age);
            if (!flash.Spent)
            {
                lit = true;

                // Riding the same climb the pens do, so the ball lifts off the ground while it is
                // still burning and the smoke takes over from it in the air.
                //
                // The *same* climb means the same circle, and that is the whole of it: the pens walk
                // the path radius rather than the silhouette, so handing the ball an unwalked shape
                // puts it half its own radius above the topmost pen -- proud of its own smoke, which
                // is exactly where a ball reads as a ball. The shell is inside the bundle rather
                // than at its top for the same reason: the ball is the cloud's core, not its crown.
                MushroomCloud.Shape cored = shape with
                {
                    CapRadius = MushroomCloud.PathRadius(shape, shape.CapTube * CapExpanded),
                };

                // ...and never above the cap's own centre, so that once there is a cap the ball is
                // inside it rather than perched on top of it.
                double riseM = Math.Min(MushroomCloud.AxisHeight(cored, progress, EmberShell),
                                        shape.CapCentre);

                double3 riseCcf = cloud.Up * riseM;

                Fireball.Draw(cloud.Body.GetPositionEcl()
                              + (cloud.BurstCcf + riseCcf).Transform(cloud.Body.GetCce2Ccf().Inverse()),
                              flash.Radius,
                              new float3((float)flash.Colour.X, (float)flash.Colour.Y,
                                         (float)flash.Colour.Z),
                              (float)flash.Glow);
            }

            // Nothing while the fireball is still burning: the smoke would be drawn over it.
            if (MushroomCloud.SmokeStarted(cloud.ChargeKg, cloud.Age)) Draw(cloud, shape, progress);
        }

        if (!lit) Fireball.Clear();
    }

    /// <summary>Forgets every cloud, for a scene that no longer contains them.</summary>
    public static void Clear()
    {
        _clouds.Clear();
        if (_tinted) { PlumeSmoke.Tint(false); _tinted = false; }
    }

    private static void Draw(Cloud cloud, in MushroomCloud.Shape shape, double progress)
    {
        double capTube = shape.CapTube;
        double path = MushroomCloud.PathRadius(shape, capTube * CapExpanded);

        // Every pen is laid at the fireball's own width and swells to its full one as the stroke
        // climbs, which is what hands the burst over to the cloud rather than swapping one for the
        // other. MushroomCloud.TubeAtHandover has the arithmetic, and what skipping it costs.
        double handover = MushroomCloud.TubeAtHandover(MushroomCloud.KilotonsFor(cloud.ChargeKg));
        double growth = MushroomCloud.TubeGrowth(progress);

        // Both ends grown from the handover width, not one end scaled off the other: at the
        // handover the pen is laid at the fireball's radius AND swells to it, so there is no
        // expansion lag at the one instant the smoke has to already be the size of the ball it is
        // taking over from. The usual expansion ratio comes back as the tube reaches full width.
        double capLaid = Grown(handover, capTube * CapInitial, growth);
        double capNow = Grown(handover, capTube * CapExpanded, growth);
        double stemLaid = Grown(handover, capTube * StemInitial, growth);
        double stemNow = Grown(handover, capTube * StemExpanded, growth);

        // The stem is a bundle rather than a wire: one pen up the axis and three around it, so the
        // column has width and its edge is not a single tube's silhouette.
        //
        // Its spread grows with the tube, and has to: the bundle reads as one column only while the
        // engine merges it, and the merge radius scales with the tube. Full spread over a
        // handover-width tube is nine poles.
        // How far up the column the pens currently are, which is what gives the stem a profile
        // rather than a constant width: each pen widens and narrows as it climbs, and the trail it
        // leaves behind is the hourglass.
        double underside = shape.CapCentre - shape.CapRadius;
        double climbed = underside > 0.0 ? Math.Clamp(shape.StemTop / underside, 0.0, 1.0) : 1.0;
        double flare = MushroomCloud.StemFlare(climbed);
        double cloudTop = shape.CapCentre + shape.CapRadius;

        for (int i = 0; i < cloud.Stem.Length; i++)
        {
            double3 offset = MushroomCloud.StemPoint(shape, cloud.Up);

            if (i > 0)
            {
                double turn = 2.0 * Math.PI * (i - 1) / Math.Max(1, cloud.Stem.Length - 1);
                double off = shape.StemRadius * StemSpread * growth * flare;
                offset += (cloud.East * (Math.Cos(turn) * off))
                          + (cloud.North * (Math.Sin(turn) * off));
            }

            PlumeSmoke.Lay(cloud.Stem[i], cloud.Body, cloud.BurstCcf + Sheared(cloud, offset, cloudTop),
                           (float)stemLaid, (float)stemNow);
        }

        Surge(cloud, shape, progress, handover);

        Ring(cloud, cloud.Rim, shape, progress, 1.0, path, capLaid, capNow);
        Ring(cloud, cloud.Mid, shape, progress, MidShell, path, capLaid, capNow);
        Ring(cloud, cloud.Core, shape, progress, CoreShell, path, capLaid, capNow);
    }

    // The expansion ratio is carried across the ramp rather than the two ends being ramped apart.
    // How far a capsule swells from where it was laid is what makes it billow instead of reading as
    // a pipe, and that is a ratio rather than a width.
    private static double Grown(double from, double to, double growth) => from + ((to - from) * growth);

    private static void Surge(Cloud cloud, in MushroomCloud.Shape shape, double progress,
                              double handover)
    {
        double radius = shape.SurgeRadius;
        double height = shape.SurgeHeight;
        double tube = MushroomCloud.SurgeTube(shape, progress, handover);

        // Out of round in both radius and height, or the skirt is a machined disc with a flat top
        // sitting under the cloud -- which is a plinth, and reads as one. The two are given
        // different bearings so the collar does not simply bulge and rise together.
        for (int i = 0; i < cloud.Surge.Length; i++)
        {
            double turn = 2.0 * Math.PI * i / cloud.Surge.Length;
            double lobed = radius * MushroomCloud.Lobe(turn, MushroomCloud.SkirtLobeDepth);
            double stood = height * MushroomCloud.Lobe(turn + 2.0, MushroomCloud.SkirtLobeDepth);

            double3 at = cloud.BurstCcf
                         + Sheared(cloud,
                                   (cloud.Up * stood)
                                   + (cloud.East * (Math.Cos(turn) * lobed))
                                   + (cloud.North * (Math.Sin(turn) * lobed)),
                                   shape.CapCentre + shape.CapRadius);

            PlumeSmoke.Lay(cloud.Surge[i], cloud.Body, at, (float)(tube * 0.5), (float)tube);
        }
    }

    // Every point the cloud is drawn at, sheared downwind by how high it is. Applied here rather
    // than inside the stroke functions so the shape stays a shape and the wind stays a wind: the
    // cap leans further than the stem for free, because it is higher.
    private static double3 Sheared(Cloud cloud, double3 offset, double cloudTop)
        => offset + (cloud.Downwind * MushroomCloud.LeanAt(Vec.Dot(offset, cloud.Up), cloudTop));

    private static void Ring(Cloud cloud, PlumeSmoke.Strand[] pens, in MushroomCloud.Shape shape,
                             double progress, double shell, double path, double laid, double expanded)
    {
        // The stroke is described against the cap's own radius, so hand it the path circle rather
        // than the silhouette: a pen walking the rim puts a whole tube of smoke outside it.
        MushroomCloud.Shape walked = shape with { CapRadius = path };

        for (int i = 0; i < pens.Length; i++)
        {
            double3 at = cloud.BurstCcf
                         + Sheared(cloud,
                                   MushroomCloud.CapPoint(walked, i, pens.Length, progress, shell,
                                                          cloud.Up, cloud.East, cloud.North),
                                   shape.CapCentre + shape.CapRadius);

            // No fade here. A radius written after a segment is laid never reaches it -- the pen
            // only rewrites the one it currently holds open -- so fading this would look like it
            // worked and do nothing. The cloud goes out through the renderer's own segment
            // lifetime.
            PlumeSmoke.Lay(pens[i], cloud.Body, at, (float)laid, (float)expanded);
        }
    }
}
