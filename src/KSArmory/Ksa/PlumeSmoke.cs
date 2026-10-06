using System.Reflection;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Smoke drawn through the renderer KSA uses for solid-booster plumes.
///
/// <para>It is the best-looking volume in the engine: a raymarched chain of swept capsules eroded
/// by a Worley volume whose scale follows the capsule's own radius, self-shadowed, lit by the
/// atmosphere LUTs, and stored in the <b>body-fixed</b> frame so what is drawn stays over the
/// ground. Segments live twenty minutes and advect through an altitude-sheared wind field.</para>
///
/// <para><b>One private field is the whole obstacle.</b> <c>VolumetricTrailRenderer</c> is public,
/// <c>SubmitEmitter</c> is public, <c>PlumeTrailEmitterState</c> is public — but
/// <c>Program._volumetricTrailRenderer</c> has no accessor, where its sibling exhaust renderer got
/// one. So the field is reflected once and everything after it is an ordinary call, which keeps all
/// of it except the field name inside <c>docs/KSA-API-SURFACE.md</c>.</para>
///
/// <para><b>A duty cycle is not in the way.</b> The <c>DutyCycle &gt; 0</c> test that stops a
/// mod-declared plume is the <c>isFlowing</c> argument, computed where the engine calls this for a
/// nozzle. A caller passing its own never meets it: no nozzle, no propellant, no thrust.</para>
///
/// <para><b>A segment only swells if it is thrown.</b> KSA grows a segment as a jet entraining
/// air, <c>r = r₀·∛(1 + t/T)</c> with <c>T = r₀ / (3·k·v)</c> on its release speed <c>v</c>, and at
/// rest it keeps its laid radius and full heat — a glowing wire. So each segment is thrown back
/// along the trail at the speed that reaches its expanded radius in <see cref="SwellSeconds"/>,
/// which also moves it along its own line rather than off it.</para>
///
/// <para><b>Colour, density and lifetime are per-emitter</b>, passed at submit time — so what
/// this mod lays never reaches KSA's own boosters, which read their own plume template. The
/// one thing it still cannot do is draw anywhere but the camera's nearby body, air or not.</para>
/// </summary>
internal static class PlumeSmoke
{
    private static VolumetricTrailRenderer? _renderer;
    private static bool _looked;
    private static bool _warned;

    /// <summary>Whether the renderer was found. False means nothing of this will draw.</summary>
    public static bool Available => Resolve() is not null;

    // Every segment is laid white: motor and flare trails are all that go through this renderer.
    private static readonly float3 Colour = new(1f, 1f, 1f);

    /// <summary>
    /// A cursor laying smoke. One per strand of the shape: move it and it draws a capsule from
    /// where it was to where it is.
    /// </summary>
    public sealed class Strand
    {
        internal readonly PlumeTrailEmitterState State = new();
    }

    // Core's DefaultPlumeTrail, which is what a booster lays. Twenty minutes is the whole reason a cloud can stand.
    private const float StockDensity = 1f;
    private const float StockLifetimeSeconds = 1200f;

    // The engine's own expansion time before it modelled entrainment, which the trails were tuned on.
    private const float SwellSeconds = 5f;

    // PlumeTrailSettings.ExhaustEntrainmentCoefficient, a readonly field; and the tracker lays a
    // segment at 1.65 nozzle radii (PlumeTrailEmitterTracker.SubmitEmitter).
    private const float Entrainment = 0.2f;
    private const float LaidPerNozzleRadius = 1.65f;

    /// <summary>
    /// Lays this strand's next segment, at a body-fixed position.
    ///
    /// <para><paramref name="initialRadius"/> is the capsule where it is laid and
    /// <paramref name="expandedRadius"/> what it swells to, which is how one moving point becomes a
    /// billowing column rather than a wire.</para>
    /// </summary>
    /// <param name="thrownCcf">
    /// Which way the smoke leaves the source, body-fixed; only the direction is read. Zero throws it
    /// straight up.
    /// </param>
    /// <param name="density">
    /// How thick this segment is, against <see cref="StockDensity"/>. A booster's plume is 1. Below
    /// 1 a segment transmits rather than scattering, which is the only way to make a bundle of
    /// overlapping segments read as dust instead of as a solid.
    /// </param>
    public static void Lay(Strand strand, Celestial body, double3 positionCcf, double3 thrownCcf,
                           float initialRadius, float expandedRadius, float density = StockDensity)
    {

        if (Resolve() is not { } renderer) return;
        if (!Vec.IsFinite(positionCcf)) return;

        float nozzle = initialRadius / LaidPerNozzleRadius;
        double swell = Math.Max(expandedRadius / initialRadius, 1.0);
        double speed = nozzle * ((swell * swell * swell) - 1.0) / (3.0 * Entrainment * SwellSeconds);

        double3 along = Vec.Unit(thrownCcf);
        if (Vec.Len2(along) < 0.5) along = Vec.Unit(positionCcf);

        var frame = new PlumeTrailEmitterFrame(positionCcf, double3.Zero, doubleQuat.Identity,
                                               double3.Zero, Universe.GetElapsedSeconds());
        try
        {
            renderer.SubmitEmitter(strand.State, body, in frame, double3.Zero, along * speed,
                                   nozzle, expandedRadius, Colour, density, StockLifetimeSeconds,
                                   isFlowing: true, isInsideAtmosphere: true);
        }
        catch (Exception e)
        {
            Warn($"submitting a segment threw: {e.Message}");
        }
    }

    // Resolved once. The field is private, so this is the one place the mod stands on a name rather
    // than on a signature -- and the one thing api-surface.sh cannot check for it.
    private static VolumetricTrailRenderer? Resolve()
    {
        if (_looked) return _renderer;
        _looked = true;

        try
        {
            FieldInfo? field = typeof(Program).GetField(
                "_volumetricTrailRenderer", BindingFlags.NonPublic | BindingFlags.Instance);

            if (field is null)
            {
                Warn("KSA.Program has no _volumetricTrailRenderer field - it has been renamed or "
                     + "given an accessor. If it now has one, use it and delete this.");
                return null;
            }

            _renderer = field.GetValue(Program.Instance) as VolumetricTrailRenderer;

            if (_renderer is null) { Warn("_volumetricTrailRenderer is not yet built"); return null; }

            Tune(_renderer);
            Log.Info("volumetric smoke: the trail renderer is reachable");
        }
        catch (Exception e)
        {
            Warn($"reaching the trail renderer threw: {e.Message}");
        }

        return _renderer;
    }

    // The renderer is shipped tuned for a booster's exhaust, which is a thin shredded trail. A
    // cloud wants the opposite: dense, lumpy, and lit well enough to read as a volume.
    //
    // These are the renderer's own fields and are therefore global -- a booster's contrail gets
    // them too, and comes out smoother than stock. There is no per-emitter override anywhere in the
    // system, so that is the trade rather than an oversight.
    private static void Tune(VolumetricTrailRenderer renderer)
    {
        // Noise eats up to 80% of the shape by default, which shreds an exhaust nicely and leaves a
        // cloud looking frayed. Rather less than that gives billows instead of tatters.
        //
        // But not much less, and the reason is mass rather than taste. A 0.3 kt surface burst lofts
        // about 90 t of soil, which at any concentration that reads as visible dust fills a few
        // hundred million cubic metres of air -- semi-transparently. Drawing the same volume opaque
        // is what makes a small device look like it made far more smoke than it could have. Erosion
        // is the only lever the renderer offers for that: there is no density or absorption field,
        // and trailColor's alpha is unused.
        renderer.ErosionMaxDepth = 0.68f;
        renderer.ErosionEdgeSharpness = 0.93f;

        // The self-shadow ray is only as long as the local radius, so more steps buys resolution
        // inside the billows rather than reach.
        renderer.SelfShadowStepCount = 8;

        // And lift the shadowed side, which is what stops a big volume reading as a silhouette.
        renderer.SkyAmbientBrightness = 4.0f;
    }

    // Once. A failure here is permanent for the session and repeating it fills the log.
    private static void Warn(string why)
    {
        if (_warned) return;

        _warned = true;
        Log.Warn($"volumetric smoke unavailable, falling back to particles: {why}");
    }
}
