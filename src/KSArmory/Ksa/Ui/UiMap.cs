using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The terrain under a director, and what it can see on it.
///
/// <para>Drawn from the height field rather than from anything the engine renders: KSA offers no
/// map view a mod can borrow, so this samples the ground itself. That is why the square is shaded
/// relief and not a picture — <c>GetTerrainHeightFromDirCce</c> answers with heights and nothing
/// else, so there is no ground texture, no coastline and no colour to read off.</para>
///
/// <para>One scan for the whole panel rather than one per head. Only the selected head's map is
/// drawn, and a second scan would double a cost that is the entire reason the grid is cached.</para>
/// </summary>
internal partial class Ui
{
    private readonly TerrainMapScan _map = new();

    private static readonly uint MapUnknown = ImGui.ColorConvertFloat4ToU32(new float4(0.10f, 0.11f, 0.13f, 1f));
    private static readonly uint MapGrid = ImGui.ColorConvertFloat4ToU32(new float4(0.45f, 0.55f, 0.65f, 0.22f));
    private static readonly uint MapEdge = ImGui.ColorConvertFloat4ToU32(new float4(0.55f, 0.65f, 0.75f, 0.55f));
    private static readonly uint MapSelf = ImGui.ColorConvertFloat4ToU32(new float4(0.45f, 0.95f, 0.55f, 1f));
    private static readonly uint MapTrack = ImGui.ColorConvertFloat4ToU32(new float4(0.95f, 0.80f, 0.30f, 1f));
    private static readonly uint MapLocked = ImGui.ColorConvertFloat4ToU32(new float4(0.98f, 0.35f, 0.30f, 1f));
    private static readonly uint MapAim = ImGui.ColorConvertFloat4ToU32(new float4(0.40f, 0.80f, 1.00f, 1f));

    // Whichever head on the shown craft has its map open, or null.
    //
    // One window, so one head at a time -- the same exclusion the main view uses, and for the same
    // reason: opening a second would silently redraw the first. _headScratch is filled while the
    // panes are drawn, which happens before this.
    private OpticalHeads.Entry? SelectedHead()
    {
        foreach (OpticalHeads.Entry entry in _headScratch)
        {
            if (entry.Policy.MapOpen) return entry;
        }

        return null;
    }

    // Opens this head's map, and shuts any other so the one window is unambiguous.
    private void TakeMap(OpticConfig policy)
    {
        foreach (OpticalHeads.Entry other in _headScratch)
        {
            if (!ReferenceEquals(other.Policy, policy)) other.Policy.MapOpen = false;
        }

        policy.MapOpen = !policy.MapOpen;
    }

    private void DrawMapWindow()
    {
        if (SelectedHead() is not { } entry || !entry.Policy.MapOpen) return;

        ImGui.SetNextWindowSize(new float2(460f, 560f), ImGuiCond.FirstUseEver);

        bool open = entry.Policy.MapOpen;
        if (ImGui.Begin($"{entry.Head.Profile.DisplayName} map###KSArmoryMap", ref open))
        {
            DrawMapContents(entry);
        }

        ImGui.End();
        entry.Policy.MapOpen = open;
    }

    private void DrawMapContents(OpticalHeads.Entry entry)
    {
        OpticalHead head = entry.Head;
        OpticConfig policy = entry.Policy;

        if (head.Platform is not { IsDisposed: false } platform)
        {
            ImGui.TextColored(Amber, "no craft");
            return;
        }

        // Sampled around the ground under the craft rather than around the craft: a map centred on
        // something at 10 km reads as a map of nothing in particular, and the pod is looking down.
        double3 anchor = head.PlatformEcl;
        Celestial? body = KsaWorld.ParentBody(platform);

        _map.Update(body, anchor, policy.MapSpanMetres, TerrainMap.Cells);

        DrawMapControls(policy);

        if (_map.Frame is not { } frame)
        {
            ImGui.TextColored(Amber, body is null
                ? "  no body here — a map wants ground under it"
                : "  no bearing here — the map cannot be drawn at a pole");
            return;
        }

        float side = Math.Max(160f, Math.Min(ImGui.GetContentRegionAvail().X,
                                             ImGui.GetContentRegionAvail().Y - 46f));
        float2 origin = ImGui.GetCursorScreenPos();

        ImDrawListPtr draw = ImGui.GetWindowDrawList();

        // Over the ground, not through space: a craft's ecliptic velocity is 29.8 km/s of the
        // planet's own motion and would point every craft in the system the same way. The same
        // distinction a round's airspeed obeys -- see KsaWorld.GroundVelocityAt.
        double3 overGround = frame.ToLocalDirection(
            KsaWorld.VelocityEcl(platform) - KsaWorld.GroundVelocityAt(platform, anchor));

        DrawRelief(draw, origin, side);
        DrawGraticule(draw, origin, side, policy.MapSpanMetres);
        DrawContacts(draw, origin, side, head, frame, policy.MapSpanMetres);
        DrawHeading(draw, origin, side, overGround);

        ImGui.Dummy(new float2(side, side));

        DrawMapLegend(overGround);
    }

    private void DrawMapControls(OpticConfig policy)
    {
        ImGui.Text("Span:");
        ImGui.SameLine();

        if (ImGui.SmallButton("-")) policy.MapSpanMetres = TerrainMap.Zoom(policy.MapSpanMetres, -1);
        ImGui.SameLine();
        if (ImGui.SmallButton("+")) policy.MapSpanMetres = TerrainMap.Zoom(policy.MapSpanMetres, +1);

        ImGui.SameLine();
        ImGui.Text(policy.MapSpanMetres >= 1000f
            ? $"{policy.MapSpanMetres / 1000f:F1} km across"
            : $"{policy.MapSpanMetres:F0} m across");

        ImGui.SameLine();
        if (ImGui.SmallButton("Rescan")) _map.Invalidate();
    }

    // The terrain itself, one filled cell per sample. A quad per cell at 64x64 is 4096 rectangles,
    // which ImGui draws without complaint; the expensive half is sampling them, not drawing them.
    private void DrawRelief(ImDrawListPtr draw, float2 origin, float side)
    {
        int cells = _map.Cells;
        if (cells < 2) return;

        float step = side / cells;
        double lift = Math.Max(1.0, _map.Highest - _map.Lowest);

        for (int j = 0; j < cells; j++)
        {
            // Row 0 is the southern edge and the screen's bottom, so rows are drawn up the square.
            float top = origin.Y + side - (j + 1) * step;

            for (int i = 0; i < cells; i++)
            {
                float left = origin.X + i * step;
                float2 a = new(left, top);
                float2 b = new(left + step + 1f, top + step + 1f);

                if (_map.ReliefAt(i, j) is not { } relief || _map.At(i, j) is not { } height)
                {
                    draw.AddRectFilled(a, b, MapUnknown);
                    continue;
                }

                // Relief carries the shape; a little height on top separates a lit high ridge from
                // a lit low one, which pure shading cannot.
                double band = (height - _map.Lowest) / lift;
                float tone = (float)Math.Clamp(relief * 0.78 + band * 0.22, 0.0, 1.0);

                draw.AddRectFilled(a, b, ImGui.ColorConvertFloat4ToU32(
                    new float4(tone * 0.52f + 0.05f, tone * 0.60f + 0.06f, tone * 0.55f + 0.08f, 1f)));
            }
        }
    }

    private static void DrawGraticule(ImDrawListPtr draw, float2 origin, float side, float span)
    {
        // A ring per 500 m out to the edge, which is what turns a picture into a range scale.
        float2 centre = new(origin.X + side * 0.5f, origin.Y + side * 0.5f);

        for (float ring = 500f; ring * 2f <= span * 1.45f; ring += 500f)
        {
            draw.AddCircle(centre, side * ring / span, MapGrid, 0, 1f);
        }

        draw.AddLine(new float2(centre.X, origin.Y), new float2(centre.X, origin.Y + side), MapGrid, 1f);
        draw.AddLine(new float2(origin.X, centre.Y), new float2(origin.X + side, centre.Y), MapGrid, 1f);

        draw.AddRect(origin, new float2(origin.X + side, origin.Y + side), MapEdge, 0f, 0, 1.5f);

        // North is up: the frame's north axis is the body's rotation axis, not the ecliptic pole.
        draw.AddText(new float2(centre.X + 4f, origin.Y + 3f), MapEdge, "N");
    }

    private static void DrawContacts(ImDrawListPtr draw, float2 origin, float side,
                                     OpticalHead head, MapFrame frame, float span)
    {
        float2 At(float2 unit) => new(origin.X + unit.X * side, origin.Y + unit.Y * side);

        // The craft itself, dead centre by construction.
        float2 self = At(new float2(0.5f, 0.5f));
        draw.AddCircleFilled(self, 4f, MapSelf);
        draw.AddCircle(self, 7f, MapSelf, 0, 1.4f);

        // Where the sight is looking, as a line from the craft to where it meets the ground.
        if (head.TryOpticViewEcl(out double3 eye, out double3 forward))
        {
            double3 local = frame.ToLocal(eye + forward * span);
            float2 unit = TerrainMap.ToUnitSquare(local, span);
            float2 to = At(TerrainMap.EdgeToward(unit) ?? unit);

            draw.AddLine(self, to, MapAim, 1.4f);
        }

        Track? watched = head.LockedTrack;

        for (int i = 0; i < head.Radar.Tracks.Count; i++)
        {
            Track track = head.Radar.Tracks[i];

            float2 unit = TerrainMap.ToUnitSquare(frame.ToLocal(track.PositionEcl), span);
            bool off = !TerrainMap.OnMap(unit);
            if (off && TerrainMap.EdgeToward(unit) is { } edge) unit = edge;

            float2 at = At(unit);
            bool locked = watched is not null && ReferenceEquals(track, watched);
            uint colour = locked ? MapLocked : MapTrack;

            // A square for a contact on the map, a triangle pointing out for one past its edge, so
            // "that way, further" is never mistaken for "there".
            if (off)
            {
                draw.AddNgonFilled(at, 5f, colour, 3);
            }
            else
            {
                draw.AddRect(new float2(at.X - 4f, at.Y - 4f), new float2(at.X + 4f, at.Y + 4f),
                             colour, 0f, 0, locked ? 2.2f : 1.5f);
            }

            if (locked) draw.AddCircle(at, 9f, colour, 0, 1.2f);
        }
    }

    // Which way the craft is going over the ground, as an arrow off its own mark.
    //
    // A fixed length rather than a leader out to where it will be: a leader is the tactical
    // convention and is unreadable here, because at 200 m/s a useful lead time is several times
    // the width of a 2 km square. The arrow says the direction and the legend says the speed.
    private static void DrawHeading(ImDrawListPtr draw, float2 origin, float side, double3 overGround)
    {
        if (TerrainMap.HeadingDeg(overGround) is not { } heading) return;

        float2 centre = new(origin.X + side * 0.5f, origin.Y + side * 0.5f);

        // Screen axes: north is up, so a heading of zero is -Y and it turns clockwise into +X.
        double radians = double.DegreesToRadians(heading);
        float2 along = new((float)Math.Sin(radians), (float)-Math.Cos(radians));

        float reach = side * 0.17f;
        float2 tip = new(centre.X + along.X * reach, centre.Y + along.Y * reach);

        draw.AddLine(centre, tip, MapSelf, 2.0f);

        // A head on it, so the line reads as pointing rather than as a radius of the range rings.
        float2 back = new(-along.X, -along.Y);
        float2 side1 = new(-along.Y, along.X);

        draw.AddTriangleFilled(
            tip,
            new float2(tip.X + (back.X + side1.X * 0.55f) * 9f, tip.Y + (back.Y + side1.Y * 0.55f) * 9f),
            new float2(tip.X + (back.X - side1.X * 0.55f) * 9f, tip.Y + (back.Y - side1.Y * 0.55f) * 9f),
            MapSelf);
    }

    private void DrawMapLegend(double3 overGround)
    {
        double speed = TerrainMap.GroundSpeed(overGround);

        if (TerrainMap.HeadingDeg(overGround) is { } heading)
        {
            ImGui.Text($"  heading {heading:F0}°   {speed:F0} m/s over the ground"
                       + $"   {(overGround.Z >= 0.0 ? "climbing" : "descending")} {Math.Abs(overGround.Z):F0} m/s");
        }
        else
        {
            ImGui.TextDisabled("  stationary over the ground — no heading to show");
        }

        ImGui.TextDisabled($"  relief {_map.Lowest:F0}-{_map.Highest:F0} m"
                           + $"   {_map.Cells}x{_map.Cells} cells at {_map.MetresPerCell:F0} m"
                           + $"   scan {_map.LastScanMs:F0} ms");

        if (_map.Unknown > 0)
        {
            ImGui.TextColored(Amber, $"  {_map.Unknown} cell(s) the height field would not answer for");
        }
    }
}
