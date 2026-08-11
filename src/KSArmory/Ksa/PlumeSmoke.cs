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
/// mod-declared plume is the <c>isActive</c> argument, computed where the engine calls this for a
/// nozzle. A caller passing its own never meets it: no nozzle, no propellant, no thrust.</para>
///
/// <para>Two things it cannot do. Colour is a single global uniform, so tinting this would tint
/// every booster in the world and it is left alone; and it draws only for the camera's nearby
/// atmospheric body, with clouds and atmosphere both enabled.</para>
/// </summary>
internal static class PlumeSmoke
{
    private static VolumetricTrailRenderer? _renderer;
    private static bool _looked;
    private static bool _warned;

    /// <summary>Whether the renderer was found. False means nothing of this will draw.</summary>
    public static bool Available => Resolve() is not null;

    /// <summary>
    /// A cursor laying smoke. One per strand of the shape: move it and it draws a capsule from
    /// where it was to where it is.
    /// </summary>
    public sealed class Strand
    {
        internal readonly PlumeTrailEmitterState State = new();
    }

    /// <summary>
    /// Lays this strand's next segment, at a body-fixed position.
    ///
    /// <para><paramref name="initialRadius"/> is the capsule where it is laid and
    /// <paramref name="expandedRadius"/> what it swells to, which is how one moving point becomes a
    /// billowing column rather than a wire.</para>
    /// </summary>
    public static void Lay(Strand strand, Celestial body, double3 positionCcf,
                           float initialRadius, float expandedRadius)
    {
        if (Resolve() is not { } renderer) return;
        if (!Vec.IsFinite(positionCcf)) return;

        try
        {
            renderer.SubmitEmitter(strand.State, body, positionCcf,
                                   initialRadius, expandedRadius, isActive: true);
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
        // cloud looking frayed. Less depth and a softer edge give billows instead of tatters.
        renderer.ErosionMaxDepth = 0.45f;
        renderer.ErosionEdgeSharpness = 0.90f;

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
