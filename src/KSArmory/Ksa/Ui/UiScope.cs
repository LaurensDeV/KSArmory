using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The radar scope: a plan-position indicator for one installation's set, and for the infrared
/// search parts aboard its craft. Craft-centred and polar, with no ground.
/// <para>Geometry lives in <see cref="ScopeGeometry"/>, so a mirrored bearing, an inverted Y or
/// slant range is caught by tests rather than by eye.</para>
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
    private static readonly float4 ScopeHostileInk = new(1.00f, 0.22f, 0.22f, 1f);
    private static readonly uint ScopeHostile = ImGui.ColorConvertFloat4ToU32(ScopeHostileInk);


    // Shuts anyone else's: one scope window, as with the director's map.
    private void TakeScope(SystemConfig policy)
    {
        foreach (WeaponSystems.Entry other in _roster.All)
        {
            if (!ReferenceEquals(other.Policy, policy)) other.Policy.ScopeOpen = false;
        }

        policy.ScopeOpen = !policy.ScopeOpen;
    }

    private void DrawScopeWindow()
    {
        WeaponSystems.Entry? scoped = null;
        string? tab = null;
        foreach (WeaponSystems.Entry entry in _roster.All)
        {
            // A saved setting can outlive the set it was saved against.
            if (entry.Policy.ScopeOpen && ScopeTab(entry.Weapon.Sensor) is { } name)
            {
                scoped = entry;
                tab = name;
                break;
            }
        }

        if (scoped is not { } open || tab is null) return;

        bool visible = open.Policy.ScopeOpen;
        ImGui.SetNextWindowSize(new float2(420f, 520f), ImGuiCond.FirstUseEver);

        if (ImGui.Begin($"{tab} scope — {open.Weapon.Profile.DisplayName}###ksarmory_scope",
                        ref visible))
        {
            DrawScopeFor(open.Weapon, open.Policy);
        }

        ImGui.End();
        open.Policy.ScopeOpen = visible;
    }

    private void DrawScope() => DrawScopeFor(_system, _policy);

    private void DrawScopeFor(WeaponSystem system, SystemConfig policy)
    {
        if (system.Platform is not { IsDisposed: false } platform)
        {
            ImGui.TextColored(Amber, "no craft");
            return;
        }

        Celestial? body = KsaWorld.ParentBody(platform);
        MapFrame? frame = body is null
            ? null
            : ScopeFrame(body, system.MountEcl);

        Span<float> steps = stackalloc float[ScopeGeometry.RangeStepCount];
        steps = steps[..ScopeGeometry.RangeSteps(system.Sensor.Range, steps)];
        float range = ScopeGeometry.SnapRange(policy.ScopeRangeMetres, steps);

        DrawScopeControls(policy, steps, range);

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

        DrawScopeFace(draw, centre, radius);
        // A silent set is not scanning, so it paints no sweep.
        if (!policy.RadarSilent) DrawScopeSweep(draw, centre, radius, system, local);
        DrawScopeContacts(draw, centre, radius, system, policy, local, range);

        ImGui.Dummy(new float2(side, side));

        DrawScopeKey();
    }

    private static void DrawScopeControls(SystemConfig policy, ReadOnlySpan<float> steps, float range)
    {
        ImGui.Text("Range:");
        ImGui.SameLine();

        for (int i = 0; i < steps.Length; i++)
        {
            if (i > 0) ImGui.SameLine();

            bool on = Math.Abs(range - steps[i]) < 1f;
            if (on) ImGui.PushStyleColor(ImGuiCol.Button, new float4(0.20f, 0.42f, 0.30f, 1f));

            if (ImGui.Button(ScopeDistance(steps[i]))) policy.ScopeRangeMetres = steps[i];

            if (on) ImGui.PopStyleColor();
        }

        ImGui.TextDisabled($"rings every {ScopeDistance((float)ScopeGeometry.RingRange(range, 0))}"
                           + "   widest is the set's reach");
    }

    private static string ScopeDistance(float metres)
        => metres >= 1000f ? $"{metres / 1000f:0.#} km" : $"{metres:F0} m";

    private static void DrawScopeFace(ImDrawListPtr draw, float2 centre, float radius)
    {
        draw.AddCircleFilled(centre, radius, ScopeFace, 64);

        foreach (float ring in ScopeGeometry.Rings)
        {
            draw.AddCircle(centre, radius * ring, ScopeGrid, 64, 1.0f);
        }

        draw.AddCircle(centre, radius, ScopeRim, 64, 1.6f);

        ReadOnlySpan<string> marks = ["N", "E", "S", "W"];
        for (int i = 0; i < 4; i++)
        {
            double bearing = i * Math.PI / 2.0;
            float2 to = Face(centre, radius, ScopeGeometry.Plot(bearing, 1.0, 1.0));

            draw.AddLine(centre, to, ScopeGrid, 1.0f);

            float2 label = Face(centre, radius * 0.90f, ScopeGeometry.Plot(bearing, 1.0, 1.0));
            draw.AddText(new float2(label.X - 4f, label.Y - 7f), ScopeRim, marks[i]);
        }

        draw.AddCircleFilled(centre, 3f, ScopeSelf);
    }

    // Off the array's own angle, not a clock, so a halted array draws a halted sweep. One trace per
    // radiating face.
    private static void DrawScopeSweep(ImDrawListPtr draw, float2 centre, float radius,
                                       WeaponSystem system, MapFrame frame)
    {
        int faces = system.Profile.SearchRadarFaces;
        if (faces <= 0) return;

        if (!TryCraftHeading(system, frame, out double heading)) return;

        Span<double> bearings = stackalloc double[ScopeGeometry.MaxSweepFaces];
        // Traverse plus spin, as RadarPose composes them: the array rides the turret.
        double array = system.Turret.BearingRad + system.RadarSpinRad;
        int count = ScopeGeometry.SweepBearings(heading, array, faces, bearings);

        for (int i = 0; i < count; i++)
        {
            float2 tip = Face(centre, radius, ScopeGeometry.Plot(bearings[i], 1.0, 1.0));
            draw.AddLine(centre, tip, ScopeSweep, 1.6f);
        }
    }

    // The craft's +Y, which the array's angle is measured from.
    private static bool TryCraftHeading(WeaponSystem system, MapFrame frame, out double bearingRad)
    {
        bearingRad = 0.0;
        if (system.Platform is not { IsDisposed: false } platform) return false;

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
                                   WeaponSystem system, SystemConfig policy, MapFrame frame,
                                   float range)
    {
        Track? locked = system.LockedTrack;

        _searchSets.On(system.Platform, _searchScratch);

        for (int i = 0; i < system.Radar.Tracks.Count; i++)
        {
            Track track = system.Radar.Tracks[i];
            DrawScopeBlip(draw, centre, radius, system, policy, frame, range, track,
                          isLocked: locked is not null && ReferenceEquals(track, locked),
                          heat: HeldBySearchSet(track.Contact.Handle));
        }

        for (int s = 0; s < _searchScratch.Count; s++)
        {
            List<Track> held = _searchScratch[s].Radar.Tracks;
            for (int i = 0; i < held.Count; i++)
            {
                Track track = held[i];
                if (system.Radar.TrackFor(track.Contact.Handle) is not null || HeldEarlier(s, track.Contact.Handle)) continue;

                DrawScopeBlip(draw, centre, radius, system, policy, frame, range, track, isLocked: false, heat: true);
            }
        }
    }

    private bool HeldBySearchSet(object handle)
    {
        for (int s = 0; s < _searchScratch.Count; s++)
        {
            if (_searchScratch[s].Radar.TrackFor(handle) is not null) return true;
        }

        return false;
    }

    // Each contact once, however many search parts hold it.
    private bool HeldEarlier(int set, object handle)
    {
        for (int s = 0; s < set; s++)
        {
            if (_searchScratch[s].Radar.TrackFor(handle) is not null) return true;
        }

        return false;
    }

    private void DrawScopeBlip(ImDrawListPtr draw, float2 centre, float radius, WeaponSystem system,
                               SystemConfig policy, MapFrame frame, float range, Track track,
                               bool isLocked, bool heat)
    {
        double3 offset = frame.ToLocal(track.PositionEcl);
        double bearing = ScopeGeometry.BearingRad(offset.X, offset.Y);
        double ground = ScopeGeometry.GroundRange(offset.X, offset.Y);

        bool beyond = ScopeGeometry.Beyond(ground, range);
        float2 at = Face(centre, radius, ScopeGeometry.Plot(bearing, ground, range));

        ScopeGeometry.Blip symbol = SymbolOf(track, policy);
        uint colour = isLocked ? ScopeLocked
                    : symbol == ScopeGeometry.Blip.Hostile ? ScopeHostile
                    : track.IsThreat ? ScopeThreat
                    : ScopeBlip;

        switch (symbol)
        {
            case ScopeGeometry.Blip.Missile:
                draw.AddText(new float2(at.X - 5f, at.Y - 7f), colour, "M");
                break;

            // Geometry, not a Greek delta: the font is not guaranteed to carry one.
            case ScopeGeometry.Blip.Unknown:
                draw.AddNgon(at, 6f, colour, 3, isLocked ? 2.2f : 1.5f);
                break;

            case ScopeGeometry.Blip.Hostile:
                DrawHostile(draw, at, system, frame, track, colour, isLocked, beyond);
                break;

            default:
                draw.AddLine(new float2(at.X - 4f, at.Y - 4f), new float2(at.X + 4f, at.Y + 4f),
                             colour, isLocked ? 2.2f : 1.5f);
                draw.AddLine(new float2(at.X - 4f, at.Y + 4f), new float2(at.X + 4f, at.Y - 4f),
                             colour, isLocked ? 2.2f : 1.5f);
                break;
        }

        // Beside the symbol, so a hostile that transmits is still drawn as a hostile.
        if (IsEmitting(track)) draw.AddText(new float2(at.X + 6f, at.Y - 9f), colour, ScopeGeometry.Emitting);
        if (heat) draw.AddText(new float2(at.X + 6f, at.Y + 1f), colour, ScopeGeometry.Heat);

        // Held on the rim: a tick outward, so it is not read as being there.
        if (beyond)
        {
            float2 out2 = Face(centre, radius * 1.06f, ScopeGeometry.Plot(bearing, 1.0, 1.0));
            draw.AddLine(at, out2, colour, 1.4f);
        }

        if (isLocked)
        {
            draw.AddCircle(at, 10f, colour, 16, 1.4f);
            DrawTrackBeam(draw, centre, at, colour);
        }
    }

    // Its heading over the ground, never the ecliptic, which carries the planet's 29.8 km/s.
    private static void DrawHostile(ImDrawListPtr draw, float2 at, WeaponSystem system, MapFrame frame,
                                    Track track, uint colour, bool isLocked, bool beyond)
    {
        float thickness = isLocked ? 2.2f : 1.5f;

        // Clamped to the rim, the symbol is not where the craft is.
        if (beyond || !TryGroundHeading(system, frame, track, out double heading))
        {
            draw.AddNgon(at, 6f, colour, 3, thickness);
            return;
        }

        float2 along = ScopeGeometry.Plot(heading, 1.0, 1.0);
        float2 across = new(-along.Y, along.X);

        float2 nose = new(at.X + (along.X * 8f), at.Y + (along.Y * 8f));
        float2 left = new(at.X - (along.X * 5f) + (across.X * 6f), at.Y - (along.Y * 5f) + (across.Y * 6f));
        float2 right = new(at.X - (along.X * 5f) - (across.X * 6f), at.Y - (along.Y * 5f) - (across.Y * 6f));
        draw.AddTriangle(nose, left, right, colour, thickness);

        draw.AddLine(nose, new float2(at.X + (along.X * 34f), at.Y + (along.Y * 34f)), colour, 1.0f);
    }

    private static bool TryGroundHeading(WeaponSystem system, MapFrame frame, Track track, out double bearingRad)
    {
        bearingRad = 0.0;
        if (system.Platform is not { IsDisposed: false } platform) return false;

        double3 overGround = frame.ToLocalDirection(
            track.VelocityEcl - KsaWorld.GroundVelocityAt(platform, track.PositionEcl));
        if (TerrainMap.HeadingDeg(overGround) is not { } heading) return false;

        bearingRad = double.DegreesToRadians(heading);
        return true;
    }

    // Stops at the ring, so the symbol inside it stays readable.
    private static void DrawTrackBeam(ImDrawListPtr draw, float2 centre, float2 at, uint colour)
    {
        float dx = at.X - centre.X;
        float dy = at.Y - centre.Y;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= 12f) return;

        float reach = (length - 10f) / length;
        draw.AddLine(centre, new float2(centre.X + (dx * reach), centre.Y + (dy * reach)), colour, 1.2f);
    }

    private static void DrawScopeKey()
    {
        // Spelled out: the font may not carry a delta.
        ImGui.TextColored(ScopeHostileInk, "triangle pointing, with a line  hostile, and its heading");
        ImGui.TextDisabled("X  friendly or neutral      triangle  unknown side");
        ImGui.TextDisabled($"M  round in the air     {ScopeGeometry.Emitting}  transmitting     {ScopeGeometry.Heat}  held by an infrared search set");
        ImGui.TextDisabled("ringed, with a line  the lock, held by the track radar");
    }

    // The IFF fire control uses, on Track.Team. Never Contact.TeamKey: that is a craft name, and
    // Classify handed a name answers Unknown for nearly everything.
    private static ScopeGeometry.Blip SymbolOf(Track track, SystemConfig policy)
        => ScopeGeometry.SymbolFor(track.Contact is RoundContact, policy.Iff.Classify(track.Team));

    // The roster the anti-radiation path asks, so the two cannot disagree.
    private bool IsEmitting(Track track)
        => track.Contact.Handle is Vehicle craft && _roster.IsEmitting(craft);

    // North on the body's axis, not the ecliptic pole: 23 degrees apart on Earth.
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

    private static float2 Face(float2 centre, float radius, float2 unit)
        => new(centre.X + (unit.X * radius), centre.Y + (unit.Y * radius));
}
