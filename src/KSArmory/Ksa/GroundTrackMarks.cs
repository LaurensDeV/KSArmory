using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// What this mod draws on KSA's ground-track map: where the warheads are going, and how far the
/// bus can still move them.
///
/// <para>Placement is <see cref="GroundTrackWindow.ProjectOnImage"/>, KSA's own function, so a
/// mark cannot disagree with the locations drawn beside it. The crop it is given is the engine's
/// own, captured as it is worked out — see <see cref="GroundTrackHook"/>.</para>
///
/// <para><b>An aim is latitude and longitude already</b>, which is what makes this cheap:
/// <see cref="AimSite"/> stores a place rather than a position, for the reason a ballistic flight
/// lasts half an hour. The reach is the exception — an ellipse in the ecliptic — so it is walked
/// round and each point converted, which also handles it crossing the dateline, where an
/// equirectangular map jumps a full width.</para>
/// </summary>
internal static class GroundTrackMarks
{
    private static readonly ImColor8 Lead = new(255, 170, 60, 255);
    private static readonly ImColor8 Other = new(255, 170, 60, 170);
    private static readonly ImColor8 Count = new(255, 200, 130, 200);
    private static readonly ImColor8 Reach = new(90, 190, 255, 150);

    // Fireball, what it destroys, what it damages. Hottest in, faintest out -- the order they are
    // read in is outward, and the eye should land on the lethal one.
    private static readonly ImColor8 Fireball = new(255, 240, 180, 210);
    private static readonly ImColor8 Lethal = new(255, 90, 60, 200);
    private static readonly ImColor8 Damage = new(255, 170, 60, 110);
    private static readonly ImColor8 Calibration = new(80, 230, 255, 220);
    private static readonly ImColor8 Label = new(80, 230, 255, 160);

    private const float MarkPx = 5f;
    private const float CrossArmPx = 5f;

    // Enough that the longest reach reads as a curve rather than a polygon; it is one conversion
    // and one projection each, on a window nobody has open while flying.
    private const int ReachPoints = 96;

    // Enough that a blast ring reads as round at the zoom one is worth looking at.
    private const int RingPoints = 48;

    // Under this across, a ring is a smudge on the mark it surrounds rather than information.
    private const float RingLeastPx = 6f;

    private static readonly double[] Latitudes = [45.0, 0.0, -45.0];
    private static readonly double[] Longitudes = [-90.0, 0.0, 90.0];

    public static void Draw(IcbmComputer? computer, float2 at, float2 size,
                            float2 min, float2 max, bool calibration)
    {
        ImDrawListPtr draw = ImGui.GetWindowDrawList();

        // Everything here is placed by projecting a coordinate, and a coordinate outside the crop
        // projects outside the image -- off the top of a zoomed map and over the window's own
        // chrome. The marks cull themselves; the reach cannot, because the line between two points
        // that are both off screen can still cross it.
        draw.PushClipRect(at, at + size, true);

        try
        {
            if (calibration) DrawCalibration(draw, at, size, min, max);

            if (computer is null) return;

            // Under the targets: the region is context for them, and an outline crossing a mark
            // should not be the thing on top.
            DrawReach(draw, computer, at, size, min, max);
            DrawEffects(draw, computer, at, size, min, max);
            DrawTargets(draw, computer, at, size, min, max);

            // Inside the clip, so the cursor's own line cannot spill over the window's chrome.
            GroundTrackDesignator.Update(computer, at, size, min, max);
        }
        finally
        {
            draw.PopClipRect();
        }
    }

    /// <summary>
    /// Which target's mark a point is on, or -1. What separates weighting a target already chosen
    /// from placing a new one — and they are different gestures, because only placement is bounded
    /// by what the bus can divert to.
    /// </summary>
    public static int Nearest(IcbmComputer computer, float2 at, float2 size,
                              float2 min, float2 max, float2 point)
    {
        var entries = computer.Targets;
        int found = -1;
        float nearest = GrabPx * GrabPx;

        for (int i = 0; i < entries.Count; i++)
        {
            AimSite site = entries[i].Site;
            if (!site.IsSet) continue;
            if (!MapViewport.Shows(MapViewport.ToUv(site.LongitudeDeg, site.LatitudeDeg), min, max))
                continue;

            float2 mark = GroundTrackWindow.ProjectOnImage(at, size, site.LongitudeDeg,
                                                           site.LatitudeDeg, min, max);

            float dx = point.X - mark.X;
            float dy = point.Y - mark.Y;
            float away = (dx * dx) + (dy * dy);

            if (away > nearest) continue;

            nearest = away;
            found = i;
        }

        return found;
    }

    // How near a mark counts as being on it. Wider than the diamond, because the gesture is worth
    // more than the pixel: a click that lands a few pixels off should weight the target it
    // obviously meant rather than place a new one on top of it.
    private const float GrabPx = 11f;

    // What a warhead does to the ground round each target: the fireball, what it destroys and
    // what it damages, as Warhead derives them from the charge. Walked on the sphere rather than
    // drawn as screen circles -- GroundCircle says why that is not the same shape.
    //
    // Per warhead, not per target. A target taking three is hit three times in one place, which
    // makes the hole deeper and not wider; a bigger circle would say otherwise.
    private static void DrawEffects(ImDrawListPtr draw, IcbmComputer computer,
                                    float2 at, float2 size, float2 min, float2 max)
    {
        if (computer.Munition is not { } warhead) return;
        if (computer.Parent is not { } body) return;

        double charge = warhead.ChargeKg;
        if (!(charge > 0.0)) return;

        double across = Math.Max(1e-6, max.Y - min.Y);
        double perDegree = size.Y / (across * 180.0);

        var entries = computer.Targets;

        for (int i = 0; i < entries.Count; i++)
        {
            AimSite site = entries[i].Site;
            if (!site.IsSet || entries[i].Warheads <= 0) continue;
            if (!MapViewport.Shows(MapViewport.ToUv(site.LongitudeDeg, site.LatitudeDeg), min, max))
                continue;

            Ring(draw, body, site, Warhead.BlastRadius(charge), perDegree, Damage, at, size, min, max);
            Ring(draw, body, site, Warhead.LethalRadius(charge), perDegree, Lethal, at, size, min, max);
            Ring(draw, body, site, Warhead.FireballRadius(charge), perDegree, Fireball, at, size, min, max);
        }
    }

    private static void Ring(ImDrawListPtr draw, Celestial body, AimSite site, double metres,
                             double pixelsPerDegree, ImColor8 colour,
                             float2 at, float2 size, float2 min, float2 max)
    {
        if (!(metres > 0.0)) return;

        // Cheap first: a ring under a few pixels across is noise round the mark, and walking it
        // would be dozens of projections for something nobody can see.
        if (GroundCircle.HeightDeg(metres, body.MeanRadius) * pixelsPerDegree < RingLeastPx) return;

        float2 wasAt = default;
        float wasU = 0f;
        bool have = false;

        for (int i = 0; i <= RingPoints; i++)
        {
            GroundCircle.At(site.LatitudeDeg, site.LongitudeDeg, metres, body.MeanRadius,
                            2.0 * Math.PI * i / RingPoints, out double lat, out double lon);

            float u = MapViewport.ToUv(lon, lat).X;
            float2 point = GroundTrackWindow.ProjectOnImage(at, size, lon, lat, min, max);

            if (have && MapViewport.Joins(wasU, u)) draw.AddLine(wasAt, point, colour, 1.2f);

            wasAt = point;
            wasU = u;
            have = true;
        }
    }

    private static void DrawTargets(ImDrawListPtr draw, IcbmComputer computer,
                                    float2 at, float2 size, float2 min, float2 max)
    {
        var entries = computer.Targets;
        int drawn = 0;

        for (int i = 0; i < entries.Count; i++)
        {
            AimSite site = entries[i].Site;
            if (!site.IsSet) continue;

            float2 uv = MapViewport.ToUv(site.LongitudeDeg, site.LatitudeDeg);
            if (!MapViewport.Shows(uv, min, max)) continue;

            bool lead = i == computer.LeadTarget;
            float2 point = GroundTrackWindow.ProjectOnImage(at, size, site.LongitudeDeg,
                                                            site.LatitudeDeg, min, max);

            DrawDiamond(draw, point, lead ? Lead : Other, lead);

            // The warhead count, not the index: how many arrive is what an operator is deciding.
            draw.AddText(new float2(point.X + MarkPx + 3f, point.Y - 6f), lead ? Lead : Count,
                         entries[i].Warheads.ToString());

            if (lead) _leadAt = point;
            drawn++;
        }

        // Said when the count changes, which answers "why is my target not on the map": held and
        // off the crop are different answers, and the screen position is what a projection fault
        // would show up in.
        if (drawn == _drawn) return;

        _drawn = drawn;
        Log.Debug($"ground-track map: {drawn} of {entries.Count} target(s) on the crop"
                  + (drawn > 0 ? $", lead at {_leadAt.X:F0},{_leadAt.Y:F0} px" : ""));
    }

    private static int _drawn = -1;
    private static float2 _leadAt;

    private static void DrawReach(ImDrawListPtr draw, IcbmComputer computer,
                                  float2 at, float2 size, float2 min, float2 max)
    {
        if (!computer.Reach.HasRegion) { SayReach("no region"); return; }
        if (computer.ReachCentreEcl() is not { } centre) { SayReach("no centre"); return; }
        if (!computer.TryReachAxesEcl(out double3 major, out double3 minor))
        {
            SayReach("no axes");
            return;
        }

        float2 wasAt = default;
        float wasU = 0f;
        bool have = false;
        int segments = 0;

        for (int i = 0; i <= ReachPoints; i++)
        {
            double angle = 2.0 * Math.PI * i / ReachPoints;
            double3 on = centre + (major * Math.Cos(angle)) + (minor * Math.Sin(angle));

            if (!KsaWorld.TryLatitudeLongitude(on, out _, out double lat, out double lon))
            {
                have = false;
                continue;
            }

            float u = MapViewport.ToUv(lon, lat).X;
            float2 point = GroundTrackWindow.ProjectOnImage(at, size, lon, lat, min, max);

            if (have && MapViewport.Joins(wasU, u))
                draw.AddLine(wasAt, point, Reach, 1.5f);

            wasAt = point;
            wasU = u;
            have = true;
            segments++;
        }

        SayReach($"{segments} point(s), semi-axes {Vec.Len(major) / 1000.0:F1} x "
                 + $"{Vec.Len(minor) / 1000.0:F1} km");
    }

    // Said when the answer changes. Which of the three refusals it is matters: a bus on the pad
    // has no divert footprint at all, and that reads the same from outside as a projection that
    // silently dropped every point.
    private static void SayReach(string what)
    {
        if (what == _said) return;

        _said = what;
        Log.Debug($"ground-track map: reach {what}");
    }

    private static string _said = "";

    private static void DrawDiamond(ImDrawListPtr draw, float2 at, ImColor8 colour, bool filled)
    {
        float2 top = new(at.X, at.Y - MarkPx);
        float2 right = new(at.X + MarkPx, at.Y);
        float2 bottom = new(at.X, at.Y + MarkPx);
        float2 left = new(at.X - MarkPx, at.Y);

        if (filled)
        {
            draw.AddTriangleFilled(top, right, bottom, colour);
            draw.AddTriangleFilled(top, left, bottom, colour);
            return;
        }

        draw.AddLine(top, right, colour, 1.5f);
        draw.AddLine(right, bottom, colour, 1.5f);
        draw.AddLine(bottom, left, colour, 1.5f);
        draw.AddLine(left, top, colour, 1.5f);
    }

    // Nine crosses at round coordinates, which sit on the ground they name at any zoom -- 0,-90 on
    // the Galapagos. NOT on KSA's drawn grid lines except unzoomed: that grid is 25 meridians and
    // 13 parallels spread across whatever is visible, so zoomed in it falls on arbitrary values.
    private static void DrawCalibration(ImDrawListPtr draw, float2 at, float2 size,
                                        float2 min, float2 max)
    {
        for (int i = 0; i < Latitudes.Length; i++)
        {
            for (int j = 0; j < Longitudes.Length; j++)
            {
                double lat = Latitudes[i];
                double lon = Longitudes[j];

                if (!MapViewport.Shows(MapViewport.ToUv(lon, lat), min, max)) continue;

                float2 point = GroundTrackWindow.ProjectOnImage(at, size, lon, lat, min, max);

                draw.AddLine(new float2(point.X - CrossArmPx, point.Y),
                             new float2(point.X + CrossArmPx, point.Y), Calibration, 1.5f);
                draw.AddLine(new float2(point.X, point.Y - CrossArmPx),
                             new float2(point.X, point.Y + CrossArmPx), Calibration, 1.5f);
                draw.AddText(new float2(point.X + 7f, point.Y - 6f), Label, $"{lat:F0},{lon:F0}");
            }
        }
    }
}
