using System.Runtime.InteropServices;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The gun tracers to draw this frame, for <see cref="CloudPass"/> to add into the scene as
/// <c>Shaders/KSArmoryTracer.comp</c>.
///
/// <para>Holds the systems and resolves their rounds only when the pass records, through
/// <see cref="IEffectSource.TryRoundEffectEcl"/> against that instant's camera — the pairing the
/// plume and the chase camera hang on, so the ecliptic motion cancels and a tracer sits where the
/// round's body would.</para>
///
/// <para>Every gun round is drawn: a tracer as a glowing streak, and the rest as a faint grey one of
/// the round's own width (<see cref="TracerLook.Ball"/>), which the shader tells apart by the
/// brightness's sign.</para>
///
/// <para>Collected after the step rather than in the UI pass, so tracers stay up with the UI hidden:
/// they are the rounds, not an annotation of them.</para>
/// </summary>
internal static class ShellTracers
{
    /// <summary>One tracer as the shader reads it: both ends in pixels with their depths.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct Segment
    {
        public float4 Head;    // x, y, device depth, brightness; negative is a round with no tracer
        public float4 Tail;    // x, y, device depth, the core's half-width in pixels
    }

    /// <summary>
    /// The most drawn in one view: 128 KB of the buffer. The pass costs what is on screen rather than
    /// this, and tracers are taken first, so past it the rounds left out are the faint ones.
    /// </summary>
    public const int MostPerView = 4096;

    // The core's brightness before exposure. Tuned by eye in game against a daylit sky: KSA's
    // particle tracer was 18 over a sphere a fifth of a metre across.
    private const double Radiance = 24.0;

    /// <summary>
    /// A round with no tracer, at the same scale as a tracer's 24: <c>Config.BallRoundBrightness</c>,
    /// handed over each frame. Zero draws none.
    /// </summary>
    public static double BallBrightness { get; set; } = 1.5;

    // Nearer than this to the eye a streak is clipped, not projected.
    private const double NearestW = 0.05;

    private const double StaleSeconds = 0.5;

    private static readonly List<WeaponSystem> _systems = [];
    private static readonly System.Diagnostics.Stopwatch _sinceCollected = new();
    private static readonly System.Diagnostics.Stopwatch _sincePainted = new();
    private static readonly System.Diagnostics.Stopwatch _sinceReported = new();

    /// <summary>Whether the pass drew tracers recently enough that nothing else need draw the shells.</summary>
    public static bool Painting => _sincePainted.IsRunning && _sincePainted.Elapsed.TotalSeconds < StaleSeconds;

    /// <summary>How many the last view drew, for the log and the bridge.</summary>
    public static int LastDrawn { get; private set; }

    public static void BeginFrame()
    {
        _systems.Clear();
        _sinceCollected.Restart();
    }

    public static void Collect(WeaponSystem system)
    {
        if (system.Rounds.Count > 0) _systems.Add(system);
    }

    /// <summary>Called by the pass each time it reaches the tracers, drawn or not.</summary>
    public static void MarkPainted() => _sincePainted.Restart();

    /// <summary>
    /// Projects every burning tracer into <paramref name="into"/> for a view of
    /// <paramref name="width"/> by <paramref name="height"/>, and the pixels they can reach.
    /// </summary>
    public static int Build(Camera camera, int width, int height, Span<Segment> into,
                            out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = minY = int.MaxValue;
        maxX = maxY = int.MinValue;
        int n = 0;
        int tracers = 0, balls = 0, burnt = 0, behind = 0;

        if (!_sinceCollected.IsRunning || _sinceCollected.Elapsed.TotalSeconds > StaleSeconds) return 0;

        double worldRate = KsaWorld.IsPaused ? 0.0 : KsaWorld.SimulationSpeed;
        double3 cameraEcl = camera.PositionEcl;

        // The tracers on the first pass and the rest on the second.
        for (int pass = 0; pass < 2; pass++)
        foreach (WeaponSystem system in _systems)
        {
            foreach (IProjectile round in system.Rounds)
            {
                if (n >= into.Length) break;
                if (round is not Slug slug || !RoundLabel.IsGunRound(round.Tube)
                    || round.State != RoundState.Flying || system.ShellDrawnAsBody(round))
                {
                    continue;
                }

                // A tracer that has burned through flies on as an ordinary round.
                bool glowing = slug.Tracer && round.Age < round.Munition.TracerBurnSeconds;
                if (glowing != (pass == 0)) continue;

                if (glowing) tracers++;
                else balls++;
                if (slug.Tracer && !glowing) burnt++;

                // A round with no tracer has nothing to burn out, only the muzzle to leave.
                double burn = TracerLook.Burn(round.Age, round.DistanceFlown,
                                              glowing ? round.Munition.TracerBurnSeconds : double.PositiveInfinity);
                if (!(burn > 0.0)) continue;

                double3 along = Vec.Unit(round.VelocityLocal);
                if (!Vec.IsFinite(along) || !system.TryRoundEffectEcl(round, out double3 headEcl)) continue;

                double length = TracerLook.LengthMetres(Vec.Len(round.VelocityLocal), worldRate,
                                                        Vec.Len(round.TravelSinceLaunch));

                // Differenced against the camera in double and only then projected.
                double3 head = headEcl - cameraEcl;
                double3 tail = head - (along * length);
                if (!Vec.IsFinite(head)) continue;

                double4 h = camera.EgoToClipDouble(head);
                double4 t = camera.EgoToClipDouble(tail);
                if (!TracerLook.TryClipToFront(ref h, ref t, NearestW))
                {
                    behind++;
                    continue;
                }

                // Pixels a metre spans across the line of sight at the head, off the camera's own
                // projection rather than a field of view read from somewhere else.
                double4 aside = camera.EgoToClipDouble(head + Vec.Unit(Vec.AnyPerpendicular(head)));
                if (!(aside.W > NearestW)) continue;

                (double hx, double hy) = Pixel(h, width, height);
                (double tx, double ty) = Pixel(t, width, height);
                (double ax, double ay) = Pixel(aside, width, height);
                double perMetre = Math.Sqrt(((ax - hx) * (ax - hx)) + ((ay - hy) * (ay - hy)));

                double core, level;
                if (glowing)
                {
                    (core, double share) = TracerLook.Core(perMetre);
                    level = Radiance * burn * share;
                }
                else
                {
                    if (!(BallBrightness > 0.0)) continue;

                    (core, double share) = TracerLook.Ball(perMetre, round.Munition.CalibreMm / 1000.0);
                    level = -BallBrightness * share * burn;
                }

                if (!double.IsFinite(hx + hy + tx + ty + core + level)) continue;

                into[n++] = new Segment
                {
                    Head = new float4((float)hx, (float)hy, (float)(h.Z / h.W), (float)level),
                    Tail = new float4((float)tx, (float)ty, (float)(t.Z / t.W), (float)(core * 0.5)),
                };

                int reach = (int)Math.Ceiling(core * 3.0) + 2;
                minX = Math.Min(minX, (int)Math.Floor(Math.Min(hx, tx)) - reach);
                minY = Math.Min(minY, (int)Math.Floor(Math.Min(hy, ty)) - reach);
                maxX = Math.Max(maxX, (int)Math.Ceiling(Math.Max(hx, tx)) + reach);
                maxY = Math.Max(maxY, (int)Math.Ceiling(Math.Max(hy, ty)) + reach);
            }
        }

        LastDrawn = n;

        // What the stream came to, once a second while there is one: a tracer missing from the
        // picture is otherwise indistinguishable from one that was never fired.
        if (tracers + balls > 0 && (!_sinceReported.IsRunning || _sinceReported.Elapsed.TotalSeconds >= 1.0))
        {
            _sinceReported.Restart();
            Log.Debug(() => $"tracers: {n} drawn of {tracers} tracer(s) and {balls} other round(s), {burnt} burnt out, "
                            + $"{behind} behind the eye");
        }

        return n;
    }

    private static (double X, double Y) Pixel(double4 clip, int width, int height)
        => (((clip.X / clip.W * 0.5) + 0.5) * width, ((clip.Y / clip.W * 0.5) + 0.5) * height);
}
