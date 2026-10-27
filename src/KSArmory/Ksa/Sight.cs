using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Paints the gunner's sight over the camera window the optical head is driving.
///
/// <para><b>Two reticules, because one ring can only be laid on one solution.</b> The pipper is
/// read back from fire control rather than solved again: a second solve takes the target from a
/// later instant and paints a pipper the turret was never sent to.</para>
/// </summary>
internal static class Sight
{
    private static readonly ImColor8 Reticle = new(90, 255, 120, 235);
    private static readonly ImColor8 Pending = new(255, 200, 60, 200);
    private static readonly ImColor8 Gun = new(255, 120, 90, 235);
    private static readonly ImColor8 Shadow = new(0, 0, 0, 140);

    private static readonly ReticleStroke[] _strokes = new ReticleStroke[KSArmory.Reticle.MaxStrokes];

    // Points along the horizontal reference. Enough that the arc follows the level circle rather
    // than cutting the chord across it, which at a wide field dips kilometres below level.
    private const int ArcPoints = 9;

    // How far out the reference points are placed. Far enough to read as a direction, near enough
    // that a camera on the ground is not looking at points beyond the horizon.
    private const double ReferenceDistanceMetres = 30000.0;

    /// <param name="weapon">Null for a director on a craft with no armament.</param>
    public static void Draw(IOpticalHead system, OpticConfig policy, ISightPicture? weapon)
    {
        if (policy.Viewport < 0 || system.OpticPart is null) return;

        // The window this head is driving, which need not be the main view: everything below is
        // measured and drawn against it.
        if (!SightSurface.TryFor(policy.Viewport, out SightSurface surface)) return;

        Draw(system, policy, weapon, surface);
    }

    /// <summary>Paints the sight on a surface already resolved, and releases it.</summary>
    public static void Draw(IOpticalHead system, OpticConfig policy, ISightPicture? weapon,
                            SightSurface surface)
    {
        if (system.OpticPart is null)
        {
            surface.Finish();
            return;
        }

        try
        {
            ImDrawListPtr draw = surface.Draw;
            float2 centre = surface.Centre;

            if (policy.Symbology)
            {
                DrawReferenceLine(surface, system);
                DrawBoresight(draw, centre);
            }

            // Outside the symbology switch: it is a control's own state.
            if (policy.MouseAim && policy.Viewport == KsaWorld.MainViewportIndex)
            {
                DrawDragIndicator(draw, policy, centre);
            }

            DrawTarget(surface, system, policy, weapon);

            // Outside the symbology switch: it says the picture is of the craft itself.
            if (system.Masked) DrawWarning(surface, "MASK", 28f);
            if (system.AtGimbalLimit) DrawWarning(surface, "GIMBAL LIMIT", 46f);
            DrawLaser(surface, system, policy);

            if (policy.Symbology && weapon is not null) DrawStatus(surface, weapon, policy);
        }
        finally
        {
            surface.Finish();
        }
    }

    // Where the cursor is against the rest area, drawn from the same numbers the head acts on.
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

        // From the edge of the ring, so it never covers the boresight.
        float2 from = new(centre.X + towards.X * radius, centre.Y + towards.Y * radius);
        float2 to = centre + fromCentre;

        if (commands) Line(draw, from, to, colour);

        draw.AddCircleFilled(to, 3f, colour);
    }

    // The head's own axis, the middle of the view. Always present, so a featureless magnified
    // picture still shows the sight is running.
    private static void DrawBoresight(ImDrawListPtr draw, float2 centre)
    {
        const float gap = 5f;
        const float arm = 9f;

        Line(draw, new float2(centre.X - gap - arm, centre.Y), new float2(centre.X - gap, centre.Y), Reticle);
        Line(draw, new float2(centre.X + gap, centre.Y), new float2(centre.X + gap + arm, centre.Y), Reticle);
        Line(draw, new float2(centre.X, centre.Y - gap - arm), new float2(centre.X, centre.Y - gap), Reticle);
        Line(draw, new float2(centre.X, centre.Y + gap), new float2(centre.X, centre.Y + gap + arm), Reticle);
    }

    // The horizontal through the site, drawn from points that sit on it rather than flat across
    // the screen, which is only right while the camera is level.
    private static void DrawReferenceLine(SightSurface surface, IOpticalHead system)
    {
        ImDrawListPtr draw = surface.Draw;

        if (!system.TryOpticViewEcl(out double3 eye, out double3 forward)) return;

        // Sized to the field the camera is showing: a fixed span puts both ends behind the camera
        // at high magnification.
        double fovRad = KsaWorld.ViewportFovRad(surface.Index);
        double half = Math.Clamp(fovRad, 0.02, 1.2);

        Span<double3> arc = stackalloc double3[ArcPoints];
        int n = SightPicture.ReferenceArc(eye, forward, system.Boresight, half,
                                          ReferenceDistanceMetres, arc);
        if (n < 2) return;

        Span<float2> at = stackalloc float2[ArcPoints];
        Span<bool> ok = stackalloc bool[ArcPoints];

        // The draw list clips off-picture points; only a point behind the camera is dropped, with
        // the segments touching it, so the reference survives the head swinging past the vertical.
        for (int i = 0; i < n; i++)
        {
            ok[i] = KsaWorld.TryProjectUnbounded(surface.Index, arc[i], out at[i]);
        }

        int middle = n / 2;

        for (int i = 0; i + 1 < n; i++)
        {
            if (!ok[i] || !ok[i + 1]) continue;

            // Broken either side of the middle so the reference never crosses what is being watched.
            if (i == middle || i + 1 == middle) continue;

            Line(draw, at[i], at[i + 1], Pending);
        }
    }

    private static void DrawTarget(SightSurface surface, IOpticalHead system, OpticConfig policy,
                                   ISightPicture? weapon)
    {
        ImDrawListPtr draw = surface.Draw;
        float2 centre = surface.Centre;

        if (system.LockedTrack is not { } track) return;

        // Where the craft is drawn, not simulated: the gap is metres on the ground, tens of pixels at
        // 3°. And as this surface's camera draws it, which for a camera window is not the main view's.
        double3 targetEgo;
        if (surface.Index != KsaWorld.MainViewportIndex && track.Contact is VehicleContact craftContact)
        {
            if (!KsaWorld.TryDrawnMiddleEcl(craftContact.Vehicle, out double3 targetEcl, surface.Index)) return;
            if (!KsaWorld.TryMainCameraEgo(targetEcl, out targetEgo)) return;
        }
        else if (!track.Contact.TryDrawEgo(out targetEgo))
        {
            return;
        }

        if (!KsaWorld.TryProjectEgoOrClamp(surface.Index, targetEgo, out float2 at, out bool inView)) return;

        bool settled = system.OpticOnTarget;
        ImColor8 colour = settled && !system.Masked ? Reticle : Pending;

        if (!inView)
        {
            DrawEdgeCue(draw, centre, at, colour, track);
            return;
        }

        // The markers' small fixed bracket: an apparent-size box would grow to cover the view.
        const float half = KSArmory.Reticle.IconHalfSize;

        int count = KSArmory.Reticle.Build(at, half, settled, _strokes);
        for (int i = 0; i < count; i++) Line(draw, _strokes[i].A, _strokes[i].B, colour);

        if (weapon is not null) DrawPipper(surface, weapon, at, track, targetEgo);

        string label = KSArmory.Reticle.RangeAndClosing(track.Range, track.ClosingSpeed);
        Text(draw, new float2(at.X - half, at.Y + half + 6f), label, colour);

        // Why the head is not on it: a bracket says a contact is held, never that the head is
        // doing anything about it.
        OpticHold hold = OpticFollow.Why(OnAxis(system, track), policy.MouseAim, policy.Manual,
                                         system.Designation.Kind != AimpointKind.None,
                                         policy.Tracking || system.FollowsCue, settled);

        if (hold.Holds)
        {
            Text(draw, new float2(at.X - half, at.Y - half - 18f), hold.Reason, Pending);
        }
    }

    // Whether the head is looking at the contact, off the view the camera is driven from rather
    // than off the bracket, which may be clamped to the edge.
    private static bool OnAxis(IOpticalHead system, Track track)
    {
        if (!system.TryOpticViewEcl(out double3 eye, out double3 forward)) return true;

        double3 toTarget = track.PositionEcl - eye;
        if (Vec.Len2(toTarget) < 1.0) return true;

        return double.RadiansToDegrees(Vec.AngleBetween(forward, toTarget)) <= OpticFollow.OnAxisDeg;
    }

    // Where the shells will be, sized to what the round covers at that range.
    private static void DrawPipper(SightSurface surface, ISightPicture system,
                                   float2 targetAt, Track track, double3 targetEgo)
    {
        ImDrawListPtr draw = surface.Draw;

        if (!system.TryRingAimEcl(out double3 aimEcl, out bool isGunLead) || !isGunLead) return;

        // The lead as a separation from the target, carried onto its drawn position: the solve is
        // from the analytic one, and projected directly the gap would show a lead that is not there.
        double3 leadEgo = targetEgo + (aimEcl - track.PositionEcl);

        if (!KsaWorld.TryProjectEgoOrClamp(surface.Index, leadEgo, out float2 at,
                                           out bool leadInView))
        {
            return;
        }

        if (!leadInView) return;

        MunitionProfile shell = Catalogue.MunitionNamed(system.Profile.GunMunition ?? system.Munition.Name);

        float radius = SightZoom.ApparentPixels(Warhead.LethalRadius(shell.ChargeKg), track.Range,
                                                double.RadiansToDegrees(KsaWorld.ViewportFovRad(surface.Index)),
                                                surface.Size.Y);
        radius = Math.Clamp(radius, 6f, 90f);

        // From the bracket to the pipper: that separation is the lead being taken.
        Line(draw, targetAt, at, Gun);

        draw.AddCircle(at + new float2(1f, 1f), radius, Shadow, 0, 2.5f);
        draw.AddCircle(at, radius, Gun, 0, 1.6f);
        draw.AddCircleFilled(at, 2.0f, Gun);

        if (system.GunFlightSeconds > 0.0)
        {
            Text(draw, new float2(at.X + radius + 6f, at.Y - 7f),
                 $"TOF {system.GunFlightSeconds:F1} s", Gun);
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

    private static void DrawStatus(SightSurface surface, ISightPicture system, OpticConfig policy)
    {
        ImDrawListPtr draw = surface.Draw;
        float2 at = new(surface.Pos.X + 24f, surface.Pos.Y + 24f);
        const float line = 17f;

        Text(draw, at, system.Profile.DisplayName, Reticle);
        at.Y += line;

        if (system.Profile.TubeCount > 0)
        {
            bool ready = system.Ammo > 0 && system.IsLaid;
            Text(draw, at, $"MSL {system.Ammo}", ready ? Reticle : Pending);
            at.Y += line;
        }

        if (system.Profile.HasCannon)
        {
            bool ready = system.GunAmmo > 0 && system.GunsAreLaid;
            Text(draw, at, $"GUN {system.GunAmmo}", ready ? Gun : Pending);
            at.Y += line;
        }

        if (system.Profile.FiresBeam)
        {
            Text(draw, at, system.BeamLasing ? $"LSR {system.BeamSecondsLeft} s FIRING" : $"LSR {system.BeamSecondsLeft} s",
                 system.BeamSecondsLeft > 0 ? Reticle : Pending);
            at.Y += line;
        }

        // Which weapon owns the bearing: the ring is laid on the gun's lead or on the target.
        if (system.TryRingAimEcl(out _, out bool isGunLead))
        {
            Text(draw, at, isGunLead ? "GUN HAS THE RING" : "MSL HAS THE RING",
                 isGunLead ? Gun : Reticle);
        }

        double fovDeg = double.RadiansToDegrees(KsaWorld.ViewportFovRad(surface.Index));
        string zoom = $"x{SightZoom.Clamp(policy.Magnification):0.#}   {fovDeg:F1} deg";

        Text(draw, new float2(surface.Pos.X + surface.Size.X - 150f, surface.Pos.Y + 24f),
             zoom, Reticle);
    }

    // Drawn twice: a dark stroke under a bright one, so the sight stays readable against both sky
    // and terrain without a panel behind it.
    private static void Line(ImDrawListPtr draw, float2 a, float2 b, ImColor8 colour)
    {
        draw.AddLine(a + new float2(1f, 1f), b + new float2(1f, 1f), Shadow, 2.5f);
        draw.AddLine(a, b, colour, 1.6f);
    }

    private static readonly ImColor8 Laser = new(255, 80, 80, 235);

    // The laser's state low in the picture, and a diamond where its spot lands. The range is the
    // laser's own: the slant range to whatever the beam met, not to the tracked contact.
    private static void DrawLaser(SightSurface surface, IOpticalHead system, OpticConfig policy)
    {
        if (!system.HasLaser || !policy.Lasing) return;

        string line = system.LaserInhibited ? "LASER BLOCKED"
                    : system.Spot is { } spot ? $"LASER   {Distance.Say(spot.RangeMetres)}"
                    : "LASER   NO RETURN";

        float2 size = ImGui.CalcTextSize(line);
        float2 at = new(surface.Centre.X - size.X * 0.5f, surface.Pos.Y + surface.Size.Y - 64f);
        Text(surface.Draw, at, line, system.LaserInhibited ? Pending : Laser);

        if (system.Spot is not { } landed) return;
        if (!KsaWorld.TryMainCameraEgo(landed.PositionEcl, out double3 spotEgo)) return;
        if (!KsaWorld.TryProjectEgoOrClamp(surface.Index, spotEgo, out float2 p, out bool inView) || !inView) return;

        const float r = 5f;
        float2 n = new(p.X, p.Y - r), e = new(p.X + r, p.Y), s = new(p.X, p.Y + r), w = new(p.X - r, p.Y);
        Line(surface.Draw, n, e, Laser);
        Line(surface.Draw, e, s, Laser);
        Line(surface.Draw, s, w, Laser);
        Line(surface.Draw, w, n, Laser);
    }

    private static void DrawWarning(SightSurface surface, string label, float below)
    {
        float2 size = ImGui.CalcTextSize(label);
        Text(surface.Draw, new float2(surface.Centre.X - size.X * 0.5f, surface.Centre.Y + below), label, Pending);
    }

    // On whole pixels: a glyph drawn at a fraction is resampled, so a steady number shimmers.
    private static void Text(ImDrawListPtr draw, float2 at, string what, ImColor8 colour)
    {
        at = new float2(MathF.Round(at.X), MathF.Round(at.Y));
        draw.AddText(at + new float2(1f, 1f), Shadow, what);
        draw.AddText(at, colour, what);
    }

    private static float2 Towards(float2 from, float2 to, float fraction)
        => new(from.X + (to.X - from.X) * fraction, from.Y + (to.Y - from.Y) * fraction);
}
