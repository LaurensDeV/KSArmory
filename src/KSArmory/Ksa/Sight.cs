using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Paints the gunner's sight over the camera window the optical head is driving.
///
/// <para>An ImGui overlay rather than gizmos: gizmos are drawn in the world and would sit *in*
/// the scene at the target's distance, scaling and occluding with it. A sight is on the glass.</para>
///
/// <para><b>Two reticules, because one ring can only be laid on one solution.</b> The launcher
/// points at the cannon's ballistic lead whenever the guns have the engagement, and at the target
/// itself otherwise — so the pipper and the target bracket are in different places precisely when
/// the lead matters, and the line between them is the lead being taken. Where they are drawn is
/// read back from fire control rather than solved again here: a second solve would take the
/// target's position from a later instant and paint a pipper the turret was never sent to.</para>
/// </summary>
internal static class Sight
{
    private static readonly ImColor8 Reticle = new(90, 255, 120, 235);
    private static readonly ImColor8 Pending = new(255, 200, 60, 200);
    private static readonly ImColor8 Gun = new(255, 120, 90, 235);
    private static readonly ImColor8 Armed = new(255, 90, 90, 240);
    private static readonly ImColor8 Shadow = new(0, 0, 0, 140);

    private static readonly ReticleStroke[] _strokes = new ReticleStroke[KSArmory.Reticle.MaxStrokes];

    // Points along the horizontal reference. Enough that the arc follows the level circle rather
    // than cutting the chord across it, which at a wide field dips kilometres below level.
    private const int ArcPoints = 9;

    // How far out the reference points are placed. Far enough to read as a direction, near enough
    // that a camera on the ground is not looking at points beyond the horizon.
    private const double ReferenceDistanceMetres = 30000.0;

    /// <param name="weapon">
    /// The weapons system the head is watching for, if any. Null for a director on a craft that
    /// carries no armament — the bracket, the reference and the zoom all still mean something,
    /// and the arm state, the ammo and the gun's pipper do not exist to be drawn.
    /// </param>
    public static void Draw(IOpticalHead battery, OpticConfig policy, ISightPicture? weapon)
    {
        if (policy.Viewport < 0 || battery.OpticPart is null) return;

        // The background list, not a window of the mod's own. A full-screen window is submitted
        // after the panel and therefore draws over it, so the reference line and the status block
        // cut across whatever the operator is reading. This list renders beneath every window,
        // which is what a sight on the glass wants and is what the game itself uses for its own
        // main-viewport overlays.
        ImDrawListPtr draw = ImGui.GetBackgroundDrawList();

        ImGuiViewportPtr main = ImGui.GetMainViewport();
        float2 centre = new(main.Pos.X + main.Size.X * 0.5f, main.Pos.Y + main.Size.Y * 0.5f);

        if (policy.Symbology)
        {
            DrawReferenceLine(draw, battery);
            DrawBoresight(draw, centre);
        }

        // Outside the symbology switch: this is a control's own state rather than an annotation
        // of one, and a drag with nothing showing where its rest area ends is a control you have
        // to learn by feel.
        if (policy.MouseAim && policy.Viewport == KsaWorld.MainViewportIndex)
        {
            DrawDragIndicator(draw, policy, centre);
        }

        DrawTarget(draw, battery, main, centre, weapon);

        if (policy.Symbology && weapon is not null) DrawStatus(draw, weapon, policy, main);
    }

    // Where the cursor is against the rest area, drawn from the same numbers the head acts on --
    // a ring that lies about when it will move is worse than no ring.
    private static void DrawDragIndicator(ImDrawListPtr draw, OpticConfig policy, float2 centre)
    {
        if (!KsaWorld.TryCursorFromViewCentre(policy.MouseDeadZonePx, out float2 fromCentre,
                                              out bool commands, out _))
        {
            return;
        }

        float radius = Math.Max(4f, policy.MouseDeadZonePx);
        ImColor8 colour = commands ? Gun : Pending;

        draw.AddCircle(centre + new float2(1f, 1f), radius, Shadow, 0, 2.5f);
        draw.AddCircle(centre, radius, colour, 0, 1.4f);

        if (!SightPicture.TryPointing(centre, centre + fromCentre, out float2 towards)) return;

        // From the edge of the ring rather than from the middle, so the line reads as the command
        // it is -- how far past resting the cursor has gone -- and never covers the boresight.
        float2 from = new(centre.X + towards.X * radius, centre.Y + towards.Y * radius);
        float2 to = centre + fromCentre;

        if (commands) Line(draw, from, to, colour);

        draw.AddCircleFilled(to, 3f, colour);
    }

    // The head's own axis, which is the middle of the view because the camera is boresighted on it.
    // Small and always present: at high magnification with no target the picture is otherwise
    // featureless, and there is nothing to say the sight is even running.
    private static void DrawBoresight(ImDrawListPtr draw, float2 centre)
    {
        const float gap = 5f;
        const float arm = 9f;

        Line(draw, new float2(centre.X - gap - arm, centre.Y), new float2(centre.X - gap, centre.Y), Reticle);
        Line(draw, new float2(centre.X + gap, centre.Y), new float2(centre.X + gap + arm, centre.Y), Reticle);
        Line(draw, new float2(centre.X, centre.Y - gap - arm), new float2(centre.X, centre.Y - gap), Reticle);
        Line(draw, new float2(centre.X, centre.Y + gap), new float2(centre.X, centre.Y + gap + arm), Reticle);
    }

    // The horizontal through the site, drawn from places that genuinely sit on it. A line laid
    // flat across the screen would only be right where the camera happens to be level, and the
    // whole reason to draw one is that it is not.
    private static void DrawReferenceLine(ImDrawListPtr draw, IOpticalHead battery)
    {
        if (!battery.TryOpticViewEcl(out double3 eye, out double3 forward)) return;

        // Sized to the field the camera is actually showing. A fixed span puts both ends far
        // outside a magnified picture, and at 3° that is most of a right angle away -- behind the
        // camera at any elevation, which is a reference line that vanishes the moment it is
        // needed.
        double fovRad = KsaWorld.ViewportFovRad(KsaWorld.MainViewportIndex);
        double half = Math.Clamp(fovRad, 0.02, 1.2);

        Span<double3> arc = stackalloc double3[ArcPoints];
        int n = SightPicture.ReferenceArc(eye, forward, battery.Boresight, half,
                                          ReferenceDistanceMetres, arc);
        if (n < 2) return;

        Span<float2> at = stackalloc float2[ArcPoints];
        Span<bool> ok = stackalloc bool[ArcPoints];

        // Off the edge of the picture is expected and kept -- the draw list clips. Only a point
        // behind the camera is dropped, and then the segments touching it are skipped rather than
        // the whole line, so the reference survives the head swinging past the vertical.
        for (int i = 0; i < n; i++) ok[i] = KsaWorld.TryProjectUnbounded(arc[i], out at[i]);

        int middle = n / 2;

        for (int i = 0; i + 1 < n; i++)
        {
            if (!ok[i] || !ok[i + 1]) continue;

            // Broken either side of the middle so the reference never crosses whatever is being
            // watched, which at zero elevation is exactly where the target sits.
            if (i == middle || i + 1 == middle) continue;

            Line(draw, at[i], at[i + 1], Pending);
        }
    }

    // The target bracket, the gun pipper, and the lead between them.
    private static void DrawTarget(ImDrawListPtr draw, IOpticalHead battery, ImGuiViewportPtr main,
                                   float2 centre, ISightPicture? weapon)
    {
        if (battery.LockedTrack is not { } track) return;

        // Where the craft is *drawn*, which is not where it is simulated. A bracket is the one
        // thing that has to sit exactly on the target, and the analytic-versus-physics gap is
        // metres on the ground -- noise at 50° of field and tens of pixels at 3°.
        if (!track.Contact.TryDrawEgo(out double3 targetEgo)) return;
        if (!KsaWorld.TryProjectEgoOrClamp(targetEgo, out float2 at, out bool inView)) return;

        bool settled = battery.OpticOnTarget;
        ImColor8 colour = settled ? Reticle : Pending;

        if (!inView)
        {
            // Off the glass entirely, which magnification makes routine rather than exceptional.
            DrawEdgeCue(draw, centre, at, colour, track);
            return;
        }

        // The same small bracket the on-screen system markers use, and deliberately not sized to
        // how large the target looks. The sight is boresighted on what it is watching, so an
        // apparent-size box grows without bound as the target closes and ends up covering the
        // view it is drawn on.
        const float half = KSArmory.Reticle.IconHalfSize;

        int count = KSArmory.Reticle.Build(at, half, settled, _strokes);
        for (int i = 0; i < count; i++) Line(draw, _strokes[i].A, _strokes[i].B, colour);

        if (weapon is not null) DrawPipper(draw, weapon, main, at, track, targetEgo);

        string label = KSArmory.Reticle.RangeAndClosing(track.Range, track.ClosingSpeed);
        Text(draw, new float2(at.X - half, at.Y + half + 6f), label, colour);

        if (!settled) Text(draw, new float2(at.X - half, at.Y - half - 18f), "SLEWING", colour);
    }

    // Where the shells will actually be. Sized to what the round covers at that range rather than
    // to a fixed icon, so the ring closing on the bracket is the shot coming together.
    private static void DrawPipper(ImDrawListPtr draw, ISightPicture battery, ImGuiViewportPtr main,
                                   float2 targetAt, Track track, double3 targetEgo)
    {
        if (!battery.TryRingAimEcl(out double3 aimEcl, out bool isGunLead) || !isGunLead) return;

        // The lead as a separation from the target, carried onto the target's *drawn* position.
        // The solve is measured from the analytic one, so projecting it directly would put the
        // pipper and the bracket in two different frames and show their gap as a lead that is not
        // there.
        double3 leadEgo = targetEgo + (aimEcl - track.PositionEcl);

        if (!KsaWorld.TryProjectEgoOrClamp(leadEgo, out float2 at, out bool leadInView)) return;
        if (!leadInView) return;

        MunitionProfile shell = Arsenal.MunitionNamed(battery.Profile.GunMunition ?? battery.Munition.Name);

        float radius = SightZoom.ApparentPixels(Warhead.LethalRadius(shell.ChargeKg), track.Range,
                                                double.RadiansToDegrees(KsaWorld.ViewportFovRad(KsaWorld.MainViewportIndex)),
                                                main.Size.Y);
        radius = Math.Clamp(radius, 6f, 90f);

        // The lead itself. Drawn from the bracket to the pipper because that separation *is* the
        // lead being taken, and at 4 km against a crosser it is most of the screen.
        Line(draw, targetAt, at, Gun);

        draw.AddCircle(at + new float2(1f, 1f), radius, Shadow, 0, 2.5f);
        draw.AddCircle(at, radius, Gun, 0, 1.6f);
        draw.AddCircleFilled(at, 2.0f, Gun);

        if (battery.GunFlightSeconds > 0.0)
        {
            Text(draw, new float2(at.X + radius + 6f, at.Y - 7f),
                 $"TOF {battery.GunFlightSeconds:F1} s", Gun);
        }
    }

    // A chevron on the edge, pointing the way the contact went.
    private static void DrawEdgeCue(ImDrawListPtr draw, float2 centre, float2 at, ImColor8 colour,
                                    Track track)
    {
        if (!SightPicture.TryPointing(centre, at, out float2 towards)) return;

        float2 side = new(-towards.Y, towards.X);
        float2 tip = new(at.X + towards.X * 10f, at.Y + towards.Y * 10f);
        float2 left = new(at.X - towards.X * 6f + side.X * 8f, at.Y - towards.Y * 6f + side.Y * 8f);
        float2 right = new(at.X - towards.X * 6f - side.X * 8f, at.Y - towards.Y * 6f - side.Y * 8f);

        Line(draw, tip, left, colour);
        Line(draw, tip, right, colour);
        Line(draw, left, right, colour);

        Text(draw, new float2(at.X - 24f, at.Y + 14f), $"{track.Range / 1000.0:F1} km", colour);
    }

    // The block a gunner reads without looking away from the target: what is on, what is loaded,
    // and how far in the optics are wound.
    private static void DrawStatus(ImDrawListPtr draw, ISightPicture battery, OpticConfig policy,
                                   ImGuiViewportPtr main)
    {
        float2 at = new(main.Pos.X + 24f, main.Pos.Y + 24f);
        const float line = 17f;

        Text(draw, at, battery.Profile.DisplayName, Reticle);
        at.Y += line;

        if (battery.Profile.TubeCount > 0)
        {
            bool ready = battery.Ammo > 0 && battery.IsLaid;
            Text(draw, at, $"MSL {battery.Ammo}", ready ? Reticle : Pending);
            at.Y += line;
        }

        if (battery.Profile.HasCannon)
        {
            bool ready = battery.GunAmmo > 0 && battery.GunsAreLaid;
            Text(draw, at, $"GUN {battery.GunAmmo}", ready ? Gun : Pending);
            at.Y += line;
        }

        // Which weapon owns the bearing. Only one can: the ring is laid on the gun's lead or on the
        // target, and a missile released in the first state leaves along a tube pointing elsewhere.
        if (battery.TryRingAimEcl(out _, out bool isGunLead))
        {
            Text(draw, at, isGunLead ? "GUN HAS THE RING" : "MSL HAS THE RING",
                 isGunLead ? Gun : Reticle);
        }

        double fovDeg = double.RadiansToDegrees(KsaWorld.ViewportFovRad(KsaWorld.MainViewportIndex));
        string zoom = $"x{SightZoom.Clamp(policy.Magnification):0.#}   {fovDeg:F1} deg";
        Text(draw, new float2(main.Pos.X + main.Size.X - 150f, main.Pos.Y + 24f), zoom, Reticle);
    }

    // Drawn twice: a dark stroke under a bright one, so the sight stays readable against both sky
    // and terrain without a panel behind it.
    private static void Line(ImDrawListPtr draw, float2 a, float2 b, ImColor8 colour)
    {
        draw.AddLine(a + new float2(1f, 1f), b + new float2(1f, 1f), Shadow, 2.5f);
        draw.AddLine(a, b, colour, 1.6f);
    }

    private static void Text(ImDrawListPtr draw, float2 at, string what, ImColor8 colour)
    {
        draw.AddText(at + new float2(1f, 1f), Shadow, what);
        draw.AddText(at, colour, what);
    }

    private static float2 Towards(float2 from, float2 to, float fraction)
        => new(from.X + (to.X - from.X) * fraction, from.Y + (to.Y - from.Y) * fraction);
}
