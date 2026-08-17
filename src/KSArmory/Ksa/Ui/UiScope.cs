using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The radar scope: a plan-position indicator for one installation's set.
///
/// <para>A different instrument from the director's map, not a second copy of it. The map is
/// north-up terrain relief showing what a <em>head</em> can see over the skyline; this is the
/// <em>set's</em> own picture — craft-centred, polar, no ground — which is the view that answers
/// "what is out there and on what bearing".</para>
///
/// <para>It sits at the head of the Radar tab, above the track list that already reports the
/// same contacts as numbers — one instrument in two readings rather than two instruments.</para>
///
/// <para>Geometry lives in <see cref="ScopeGeometry"/> so the conventions that look right while
/// being wrong — a mirrored bearing, an inverted Y, slant range — are settled by tests rather than
/// by looking at it.</para>
/// </summary>
internal partial class Ui
{
    private static readonly uint ScopeFace = ImGui.ColorConvertFloat4ToU32(new float4(0.04f, 0.15f, 0.06f, 1f));
    private static readonly uint ScopeGrid = ImGui.ColorConvertFloat4ToU32(new float4(0.35f, 0.72f, 0.42f, 0.55f));
    private static readonly uint ScopeRim = ImGui.ColorConvertFloat4ToU32(new float4(0.45f, 0.90f, 0.52f, 0.85f));
    private static readonly uint ScopeSweep = ImGui.ColorConvertFloat4ToU32(new float4(0.55f, 1.00f, 0.62f, 0.95f));
    private static readonly uint ScopeSelf = ImGui.ColorConvertFloat4ToU32(new float4(0.85f, 1.00f, 0.88f, 1f));
    private static readonly uint ScopeBlip = ImGui.ColorConvertFloat4ToU32(new float4(0.55f, 1.00f, 0.62f, 1f));
    private static readonly uint ScopeThreat = ImGui.ColorConvertFloat4ToU32(new float4(1.00f, 0.78f, 0.20f, 1f));
    private static readonly uint ScopeLocked = ImGui.ColorConvertFloat4ToU32(new float4(1.00f, 0.45f, 0.30f, 1f));


    // Opens this system's scope window and shuts anyone else's, so there is one rather than one
    // per installation stacked on top of each other. Same rule the director's map follows.
    private void TakeScope(SystemConfig policy)
    {
        foreach (WeaponSystems.Entry other in _batteries.All)
        {
            if (!ReferenceEquals(other.Policy, policy)) other.Policy.ScopeOpen = false;
        }

        policy.ScopeOpen = !policy.ScopeOpen;
    }

    private void DrawScopeWindow()
    {
        WeaponSystems.Entry? scoped = null;
        foreach (WeaponSystems.Entry entry in _batteries.All)
        {
            if (entry.Policy.ScopeOpen) { scoped = entry; break; }
        }

        if (scoped is not { } open) return;

        bool visible = open.Policy.ScopeOpen;
        ImGui.SetNextWindowSize(new float2(420f, 520f), ImGuiCond.FirstUseEver);

        if (ImGui.Begin($"Radar scope — {open.Battery.Profile.DisplayName}###ksarmory_scope", ref visible))
        {
            // The same drawing the tab gets. A second copy would be a second thing to keep in step.
            DrawScopeFor(open.Battery, open.Policy);
        }

        ImGui.End();
        open.Policy.ScopeOpen = visible;
    }

    // The scope, at the head of the Radar tab and above the track list.
    private void DrawScope() => DrawScopeFor(_battery, _policy);

    private void DrawScopeFor(WeaponSystem battery, SystemConfig policy)
    {
        if (battery.Platform is not { IsDisposed: false } platform)
        {
            ImGui.TextColored(Amber, "no craft");
            return;
        }

        // North comes from the body's own axis, the same way the map gets it -- so a bearing on the
        // scope is a bearing on the ground rather than one measured off the ecliptic, which is 23
        // degrees out on Earth.
        Celestial? body = KsaWorld.ParentBody(platform);
        MapFrame? frame = body is null
            ? null
            : ScopeFrame(body, battery.MountEcl);

        DrawScopeControls(policy);

        if (frame is not { } local)
        {
            ImGui.TextColored(Amber, body is null
                ? "  no body here — a bearing wants ground under it"
                : "  no bearing here — the scope cannot be drawn at a pole");
            return;
        }

        float side = Math.Max(180f, Math.Min(ImGui.GetContentRegionAvail().X,
                                             ImGui.GetContentRegionAvail().Y - 120f));
        float2 origin = ImGui.GetCursorScreenPos();
        float radius = side * 0.5f;
        float2 centre = new(origin.X + radius, origin.Y + radius);

        ImDrawListPtr draw = ImGui.GetWindowDrawList();

        DrawScopeFace(draw, centre, radius, policy.ScopeRangeMetres);
        DrawScopeSweep(draw, centre, radius, battery, local);
        DrawScopeContacts(draw, centre, radius, battery, policy, local, policy.ScopeRangeMetres);

        ImGui.Dummy(new float2(side, side));

        DrawScopeKey();
    }

    private static void DrawScopeControls(SystemConfig policy)
    {
        ImGui.Text("Range:");
        ImGui.SameLine();

        ReadOnlySpan<float> spans = ScopeRanges;
        for (int i = 0; i < spans.Length; i++)
        {
            if (i > 0) ImGui.SameLine();

            bool on = Math.Abs(policy.ScopeRangeMetres - spans[i]) < 1f;
            if (on) ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.20f, 0.42f, 0.30f, 1f));

            if (ImGui.Button(spans[i] >= 1000f ? $"{spans[i] / 1000f:F0} km" : $"{spans[i]:F0} m"))
            {
                policy.ScopeRangeMetres = spans[i];
            }

            if (on) ImGui.PopStyleColor();
        }

        ImGui.TextDisabled($"rings every {ScopeGeometry.RingRange(policy.ScopeRangeMetres, 0) / 1000.0:F1} km");
    }

    // Range settings the scope steps between, as the map steps its span.
    private static ReadOnlySpan<float> ScopeRanges => [5_000f, 20_000f, 50_000f, 200_000f];

    private static void DrawScopeFace(ImDrawListPtr draw, float2 centre, float radius, float range)
    {
        draw.AddCircleFilled(centre, radius, ScopeFace, 64);

        foreach (float ring in ScopeGeometry.Rings)
        {
            draw.AddCircle(centre, radius * ring, ScopeGrid, 64, 1.0f);
        }

        draw.AddCircle(centre, radius, ScopeRim, 64, 1.6f);

        // Cardinal spokes and their letters, so a bearing can be read off without counting.
        ReadOnlySpan<string> marks = ["N", "E", "S", "W"];
        for (int i = 0; i < 4; i++)
        {
            double bearing = i * Math.PI / 2.0;
            float2 to = Face(centre, radius, ScopeGeometry.Plot(bearing, 1.0, 1.0));

            draw.AddLine(centre, to, ScopeGrid, 1.0f);

            float2 label = Face(centre, radius * 0.90f, ScopeGeometry.Plot(bearing, 1.0, 1.0));
            draw.AddText(new float2(label.X - 4f, label.Y - 7f), ScopeRim, marks[i]);
        }

        // The site itself, dead centre by construction.
        draw.AddCircleFilled(centre, 3f, ScopeSelf);
    }

    // Off the array's own angle rather than a clock, so a halted array draws a halted sweep --
    // which is the honest reading of "this set is not scanning". One trace per radiating face:
    // the Pantsir's wedge is double-sided, so it paints two half a turn apart.
    private static void DrawScopeSweep(ImDrawListPtr draw, float2 centre, float radius,
                                       WeaponSystem battery, MapFrame frame)
    {
        int faces = battery.Profile.SearchRadarFaces;
        if (faces <= 0) return;

        // Where the array's zero mark points, on the ground. The array turns about the craft's own
        // up, so its bearing is the craft's heading plus however far it has spun.
        if (!TryCraftHeading(battery, frame, out double heading)) return;

        Span<double> bearings = stackalloc double[ScopeGeometry.MaxSweepFaces];
        // Traverse plus spin: the array rides the turret, so its angle is both. RadarPose composes
        // them by adding, and reading the spin alone leaves the sweep behind whenever the turret
        // has slewed off the craft's centreline.
        double array = battery.Turret.BearingRad + battery.RadarSpinRad;
        int count = ScopeGeometry.SweepBearings(heading, array, faces, bearings);

        for (int i = 0; i < count; i++)
        {
            float2 tip = Face(centre, radius, ScopeGeometry.Plot(bearings[i], 1.0, 1.0));
            draw.AddLine(centre, tip, ScopeSweep, 1.6f);
        }
    }

    // The craft's own forward, as a compass bearing. Its +Y is the direction it drives, and the
    // array's angle is measured from there.
    private static bool TryCraftHeading(WeaponSystem battery, MapFrame frame, out double bearingRad)
    {
        bearingRad = 0.0;
        if (battery.Platform is not { IsDisposed: false } platform) return false;

        try
        {
            double3 forward = frame.ToLocalDirection(platform.Asmb2Ego * new double3(0, 1, 0));
            if (!Vec.IsFinite(forward)) return false;

            bearingRad = ScopeGeometry.BearingRad(forward.X, forward.Y);
            return true;
        }
        catch
        {
            return false;      // a craft mid-rebuild has no heading; draw no sweep rather than a wrong one
        }
    }

    private void DrawScopeContacts(ImDrawListPtr draw, float2 centre, float radius,
                                   WeaponSystem battery, SystemConfig policy, MapFrame frame,
                                   float range)
    {
        Track? locked = battery.LockedTrack;

        for (int i = 0; i < battery.Radar.Tracks.Count; i++)
        {
            Track track = battery.Radar.Tracks[i];

            double3 offset = frame.ToLocal(track.PositionEcl);
            double bearing = ScopeGeometry.BearingRad(offset.X, offset.Y);
            double ground = ScopeGeometry.GroundRange(offset.X, offset.Y);

            bool beyond = ScopeGeometry.Beyond(ground, range);
            float2 at = Face(centre, radius, ScopeGeometry.Plot(bearing, ground, range));

            bool isLocked = locked is not null && ReferenceEquals(track, locked);
            uint colour = isLocked ? ScopeLocked : track.IsThreat ? ScopeThreat : ScopeBlip;

            switch (SymbolOf(track, policy))
            {
                case ScopeGeometry.Blip.Missile:
                    draw.AddText(new float2(at.X - 5f, at.Y - 7f), colour, "M");
                    break;

                // Geometry rather than a Greek delta: the font here is not guaranteed to carry one,
                // and a symbol that renders as a box on somebody else's machine is worse than none.
                case ScopeGeometry.Blip.Unknown:
                    draw.AddNgon(at, 6f, colour, 3, isLocked ? 2.2f : 1.5f);
                    break;

                default:
                    draw.AddLine(new float2(at.X - 4f, at.Y - 4f), new float2(at.X + 4f, at.Y + 4f),
                                 colour, isLocked ? 2.2f : 1.5f);
                    draw.AddLine(new float2(at.X - 4f, at.Y + 4f), new float2(at.X + 4f, at.Y - 4f),
                                 colour, isLocked ? 2.2f : 1.5f);
                    break;
            }

            // Transmitting: a mark beside the symbol rather than one of its own, because a hostile
            // that switches its set on must not stop being drawn as a hostile.
            if (IsEmitting(track)) draw.AddText(new float2(at.X + 6f, at.Y - 9f), colour, ScopeGeometry.Emitting);

            // Held on the rim: a tick outward, so "out there, that way" is not read as "there".
            if (beyond)
            {
                float2 out2 = Face(centre, radius * 1.06f, ScopeGeometry.Plot(bearing, 1.0, 1.0));
                draw.AddLine(at, out2, colour, 1.4f);
            }

            if (isLocked) draw.AddCircle(at, 10f, colour, 16, 1.4f);
        }
    }

    // Symbols mean nothing without a key, and this one has to be on the same screen as the face --
    // symbology a reader has to remember is symbology they read wrong.
    private static void DrawScopeKey()
    {
        // Spelled out rather than drawn: the triangle on the face is geometry precisely because the
        // font may not carry one, so putting a Greek delta in the key would reintroduce the risk it
        // was avoided for.
        ImGui.TextDisabled("X  known side      triangle  unknown side");
        ImGui.TextDisabled("M  round in the air     R  transmitting");
    }

    // Whose it is, as the scope's own IFF sees it -- so the symbol agrees with what fire control
    // would decide about the same contact rather than being a second opinion.
    private static ScopeGeometry.Blip SymbolOf(Track track, SystemConfig policy)
        => ScopeGeometry.SymbolFor(track.Contact is RoundContact,
                                   policy.Iff.Classify(track.Contact.TeamKey) != Allegiance.Unknown);

    // Read off the same roster the anti-radiation path asks, rather than a second source that
    // could disagree about who is transmitting.
    private bool IsEmitting(Track track)
        => track.Contact.Handle is Vehicle craft && _batteries.IsEmitting(craft);

    // The body's own frame, so a bearing on the scope is a bearing on the ground rather than one
    // measured off the ecliptic -- 23 degrees out on Earth. Same query the map's scan makes.
    private static MapFrame? ScopeFrame(Celestial body, double3 anchorEcl)
    {
        try
        {
            return MapFrame.TryAt(body.GetPositionEcl(), anchorEcl, body.GetRotationAxisCce());
        }
        catch (Exception e)
        {
            Log.Warn($"scope: cannot read the body's frame -- {e.Message}");
            return null;
        }
    }

    // Unit face coordinates to screen pixels.
    private static float2 Face(float2 centre, float radius, float2 unit)
        => new(centre.X + (unit.X * radius), centre.Y + (unit.Y * radius));
}
