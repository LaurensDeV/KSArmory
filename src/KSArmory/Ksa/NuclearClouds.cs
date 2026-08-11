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
    // Pens on the rim, and inside it.
    //
    // The count is arithmetic rather than taste. Smoke is drawn as a capsule whose solid core
    // reaches 0.55 of its radius, so neighbouring pens further apart than 1.1 radii leave a gap
    // between them -- and eight pens on a 380 m cap sat 300 m apart with 80 m tubes, which is 140 m
    // of clear air between each pair. That is what a ring of ropes is.
    //
    // Twenty at the radii below gives a 120 m pitch against a 130 m tube: cores that overlap by
    // 23 m, so the surface closes. The inner ring fills the dome the rim leaves hollow, and costs
    // nothing, because overlapping smoke takes the deeper of two rather than adding them.
    private const int RimStrands = 20;
    private const int DomeStrands = 10;
    private const double DomeShell = 0.55;

    // And a stem that is a column rather than a bundle of poles.
    //
    // Four pens put three of them in a ring 69 m across with 94 m tubes -- a 144 m pitch against
    // the same 1.1-radii rule, so they read as separate pillars climbing. Eight around the axis
    // closes it.
    private const int StemStrands = 9;
    private const double StemSpread = 0.85;

    // Radii, in metres, as fractions of the cap's own tube. The expansion ratio matters as much as
    // the size: a booster's plume swells a hundredfold from its nozzle, which is what makes it
    // billow, and 1.4x reads as a pipe.
    private const double CapInitial = 0.31;
    private const double CapExpanded = 0.90;
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

        public double Age;

        public readonly PlumeSmoke.Strand[] Stem =
            Enumerable.Range(0, StemStrands).Select(_ => new PlumeSmoke.Strand()).ToArray();
        public readonly PlumeSmoke.Strand[] Rim =
            Enumerable.Range(0, RimStrands).Select(_ => new PlumeSmoke.Strand()).ToArray();
        public readonly PlumeSmoke.Strand[] Dome =
            Enumerable.Range(0, DomeStrands).Select(_ => new PlumeSmoke.Strand()).ToArray();
    }

    private static readonly List<Cloud> _clouds = [];

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

            _clouds.Add(new Cloud
            {
                Body = body,
                BurstCcf = burstCcf,
                Up = up,
                East = east,
                North = Vec.Unit(Vec.Cross(up, east)),
                ChargeKg = chargeKg,
            });

            Log.Info($"nuclear cloud: {MushroomCloud.KilotonsFor(chargeKg):F2} kt rising to "
                     + $"{MushroomCloud.CloudTop(MushroomCloud.KilotonsFor(chargeKg)) / 1000.0:F1} km");
        }
        catch (Exception e)
        {
            Log.Warn($"nuclear cloud: could not start one ({e.Message})");
        }
    }

    /// <summary>Advances every cloud and lays this frame's smoke.</summary>
    public static void Update(double dtSim)
    {
        if (_clouds.Count == 0) return;
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

            MushroomCloud.Shape shape = MushroomCloud.At(cloud.ChargeKg, cloud.Age);
            if (shape.Spent) { _clouds.RemoveAt(i); continue; }

            Draw(cloud, shape, MushroomCloud.Progress(cloud.Age));
        }
    }

    /// <summary>Forgets every cloud, for a scene that no longer contains them.</summary>
    public static void Clear() => _clouds.Clear();

    private static void Draw(Cloud cloud, in MushroomCloud.Shape shape, double progress)
    {
        double capTube = shape.CapTube;
        double path = MushroomCloud.PathRadius(shape, capTube * CapExpanded);

        // The stem is a bundle rather than a wire: one pen up the axis and three around it, so the
        // column has width and its edge is not a single tube's silhouette.
        for (int i = 0; i < cloud.Stem.Length; i++)
        {
            double3 at = cloud.BurstCcf + MushroomCloud.StemPoint(shape, cloud.Up);

            if (i > 0)
            {
                double turn = 2.0 * Math.PI * (i - 1) / Math.Max(1, cloud.Stem.Length - 1);
                double off = shape.StemRadius * StemSpread;
                at += (cloud.East * (Math.Cos(turn) * off)) + (cloud.North * (Math.Sin(turn) * off));
            }

            PlumeSmoke.Lay(cloud.Stem[i], cloud.Body, at,
                           (float)(capTube * StemInitial), (float)(capTube * StemExpanded));
        }

        Ring(cloud, cloud.Rim, shape, progress, 1.0, path, capTube);
        Ring(cloud, cloud.Dome, shape, progress, DomeShell, path, capTube);
    }

    private static void Ring(Cloud cloud, PlumeSmoke.Strand[] pens, in MushroomCloud.Shape shape,
                             double progress, double shell, double path, double capTube)
    {
        // The stroke is described against the cap's own radius, so hand it the path circle rather
        // than the silhouette: a pen walking the rim puts a whole tube of smoke outside it.
        MushroomCloud.Shape walked = shape with { CapRadius = path };

        for (int i = 0; i < pens.Length; i++)
        {
            double3 at = cloud.BurstCcf
                         + MushroomCloud.CapPoint(walked, i, pens.Length, progress, shell,
                                                  cloud.Up, cloud.East, cloud.North);

            // No fade here. A radius written after a segment is laid never reaches it -- the pen
            // only rewrites the one it currently holds open -- so fading this would look like it
            // worked and do nothing. The cloud goes out through the renderer's own segment
            // lifetime.
            PlumeSmoke.Lay(pens[i], cloud.Body, at,
                           (float)(capTube * CapInitial), (float)(capTube * CapExpanded));
        }
    }
}
