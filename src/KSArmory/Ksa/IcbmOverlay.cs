using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The shot, drawn in the world: where the warheads are going, where they were told to go, and
/// when they arrive.
///
/// <para>Two marks rather than one, and the gap between them is the whole point. The line is the
/// trajectory <see cref="ImpactPredictor"/> flew from the vehicle's actual state; the ring on the
/// ground is the place it was aimed at, sized to what the warhead reaches. While they disagree the burn is not finished — which is a thing to look
/// at rather than a number to read, and the reason this is drawn at all.</para>
///
/// <para>The aim point keeps its mark whatever the vehicle is doing, and carries the time to
/// impact beside it. A designation that is only visible on the tab that set it is a designation
/// nobody can check while flying, and "when does this land" is the one question a countdown answers
/// better than any amount of prose.</para>
/// </summary>
internal static class IcbmOverlay
{
    private static readonly float4 Arc = new(0.55f, 0.8f, 1.0f, 0.9f);
    private static readonly float4 Aim = new(1.0f, 0.45f, 0.35f, 1.0f);

    // The same designation colour, thinned: the reach is where a target may go rather than where
    // one is, and drawn at the aim ring's weight it reads as another aim point.
    private static readonly float4 ReachEdge = new(1.0f, 0.55f, 0.4f, 0.45f);
    private static readonly float4 OtherAim = new(1.0f, 0.68f, 0.45f, 0.85f);

    private static readonly ImColor8 Mark = new(255, 115, 90, 235);
    private static readonly ImColor8 MarkHeld = new(245, 215, 115, 235);
    private static readonly ImColor8 MarkBad = new(250, 90, 80, 245);
    private static readonly ImColor8 MarkOther = new(255, 175, 115, 225);
    private static readonly ImColor8 Text = new(238, 240, 245, 245);

    // Coarse enough that a half-hour arc is a few dozen lines rather than a few thousand.
    private const int MaxSegments = 96;

    // What to circle when there is no warhead to ask. Small enough to read as a mark rather than
    // a claim about anything.
    private const double UnarmedRingMetres = 250.0;

    private const float Half = 9f;
    private const float Tick = 5f;

    /// <param name="focused">
    /// The computer the panel is showing, which is the only one whose reach and whose further
    /// targets are drawn. The same scoping <c>SiteDesignator</c> already has and for the same
    /// reason: a region exists to place a target in, only one computer is being clicked at, and six
    /// ellipses over six rockets is not six times as useful.
    /// </param>
    public static void Draw(IcbmComputers computers, IcbmComputer? focused, List<double3> scratch)
    {
        foreach (RingSet set in Rings.Values) set.Redrapes = 0;

        DrawInWorld(computers, focused, scratch);
        DrawMarks(computers, focused);
    }

    private static void DrawInWorld(IcbmComputers computers, IcbmComputer? focused, List<double3> scratch)
    {
        foreach (IcbmComputer computer in computers.All)
        {
            // The ring marks where the WARHEADS are going, so it outlives the craft that sent them:
            // a bus has no heat shield and they do, and it breaks up on reentry five to twenty
            // seconds before they arrive. Gated on the craft alone, the mark went out with the
            // warheads still falling toward it. The aim point needs nothing from the vehicle --
            // Target and Parent are both latched -- so this costs only the check.
            bool alive = KsaWorld.IsAlive(computer.Craft);
            if (!alive && !computer.SalvoStillArriving) continue;

            // Marked while the shot is still going to happen, and while it is on its way -- but not
            // once it has landed, when the ring has nothing left to mark. That last condition was
            // missing from the start and only became visible when the ring stopped disappearing
            // early for the unrelated reason above.
            if (computer.Config.MarkTarget && !computer.SalvoHasLanded
                && computer.TargetEcl() is { } target)
            {
                DrawAimRing(computer, target);

                // Only the one being clicked at, and only once it holds more than the lead: for a
                // single-target shot the ring above already is the whole set.
                if (ReferenceEquals(computer, focused))
                {
                    DrawOtherTargets(computer);
                    DrawReach(computer);
                }
            }

            // The trajectory is the vehicle's own and goes with it.
            if (!alive || !computer.Config.DrawTrajectory) continue;

            computer.PathEcl(scratch);
            if (scratch.Count < 2) continue;

            int stride = Math.Max(1, scratch.Count / MaxSegments);

            for (int i = stride; i < scratch.Count; i += stride)
            {
                KsaWorld.DrawLineEcl(scratch[i - stride], scratch[i], Arc);
            }

            KsaWorld.DrawLineEcl(scratch[^Math.Min(scratch.Count, stride + 1)], scratch[^1], Arc);
        }
    }

    // A ring draped on the terrain rather than a solid at the aim point, for two reasons that both
    // matter: anything large enough to see from orbit is large enough to sit over the target and
    // hide it, and a shape with no radius says nothing about how far the warhead reaches - which is
    // the question a mark on a target is being asked. Same shape as the bomb sight's pipper.
    // Every draped shape is kept as OFFSETS from its own centre rather than as ecliptic positions --
    // the ecliptic carries the planet's ~29.8 km/s, so cached absolute points are left behind within
    // a frame. Same rule the round bodies and the draw anchor obey.
    //
    // Draping is what costs: a point on the ground is a terrain lookup, and a 64-segment ring is 65
    // of them. Measured at sixteen rockets, IcbmOverlay.Draw was 5.45 ms of a 13.67 ms draw pass.
    // The ground under a fixed lat/lon does not move, so the shape is rebuilt on a wall clock rather
    // than per frame.
    //
    // A circle is rotationally symmetric, so the planet turning under a cached ring changes nothing
    // about how it looks; what ages is which ground each point was draped onto, and a second of
    // Earth's rotation is four thousandths of a degree.
    private const double RingRebuildSeconds = 1.0;

    // How many shapes one computer may re-drape in a frame. A six-target set plus its reach is
    // seven, and letting them all fall due together is a seven-fold spike on one frame -- which is
    // the number FrameBudget reports, since it reports the worst frame beside the mean. At one
    // apiece the per-frame drape cost is what a single-target shot has always paid, and seven
    // deadlines a second are comfortably met at any frame rate anyone plays at.
    private const int RedrapesPerFrame = 1;

    private sealed class Ring
    {
        public double3 Anchor;
        public double Size;
        public readonly List<double3> Offsets = [];
        public readonly System.Diagnostics.Stopwatch Since = System.Diagnostics.Stopwatch.StartNew();

        /// <param name="anchorFromBody">
        /// Where the shape is centred, measured <b>from the body's centre</b>. Never the ecliptic
        /// position: that carries the planet's ~29.8 km/s, so two frames' worth differ by ~500 m and
        /// the ten-metre test is true on every frame after the first — which re-drapes every ring
        /// every frame and makes <see cref="RingRebuildSeconds"/> do nothing at all.
        /// </param>
        public bool Stale(double3 anchorFromBody, double size)
            => Offsets.Count == 0 || Since.Elapsed.TotalSeconds >= RingRebuildSeconds
               || !Size.Equals(size) || Vec.Len(anchorFromBody - Anchor) > 10.0;
    }

    // One per computer, holding every shape that computer draws on the ground: the aim ring, the
    // reach, and one per target past the lead.
    private sealed class RingSet
    {
        public readonly Ring Aim = new();
        public readonly Ring Reach = new();
        public readonly List<Ring> Targets = [];
        public int Redrapes;

        public Ring TargetRing(int index)
        {
            while (Targets.Count <= index) Targets.Add(new Ring());
            return Targets[index];
        }
    }

    private static readonly Dictionary<IcbmComputer, RingSet> Rings = [];

    private static RingSet SetFor(IcbmComputer computer)
    {
        if (!Rings.TryGetValue(computer, out RingSet? set)) Rings[computer] = set = new RingSet();
        return set;
    }

    // Rebuilds the ring if it is stale and this computer has a re-drape left this frame, then draws
    // whatever it holds. A ring whose turn has not come is drawn as it stands, which is a second of
    // a planet's rotation out of date -- four thousandths of a degree.
    private static void DrawDrapedCircle(IcbmComputer computer, RingSet set, Ring ring, double3 centre,
                                         double3 up, double radius, float4 colour, int segments)
    {
        if (ring.Stale(centre - BodyEcl(computer), radius) && set.Redrapes < RedrapesPerFrame)
        {
            set.Redrapes++;
            ring.Since.Restart();
            ring.Anchor = centre - BodyEcl(computer);
            ring.Size = radius;

            KsaWorld.CollectDrapedCircleEcl(centre, up, radius, ring.Offsets, segments);
        }

        for (int i = 1; i < ring.Offsets.Count; i++)
        {
            KsaWorld.DrawLineEcl(centre + ring.Offsets[i - 1], centre + ring.Offsets[i], colour);
        }
    }

    // What every cached anchor is measured from. Zero without a parent, which reads as stale every
    // frame -- and nothing gets this far without one, since the aim point is resolved on it.
    private static double3 BodyEcl(IcbmComputer computer)
        => computer.Parent is { } parent ? parent.GetPositionEcl() : Vec.Zero;

    private static void DrawAimRing(IcbmComputer computer, double3 target)
    {
        // Off gravity, because that is the one direction the mod already resolves everywhere.
        double3 up = Vec.Unit(KsaWorld.GravityAt(computer.Craft, target) * -1.0);
        if (Vec.Len2(up) < 0.5) return;

        double radius = RingRadius(computer);
        RingSet set = SetFor(computer);

        DrawDrapedCircle(computer, set, set.Aim, target, up, radius, Aim, segments: 64);
        // The inner pip is metres across, so draping it costs 17 accurate terrain samples to move
        // it by less than its own width. The outer ring is what shows the ground.
        KsaWorld.DrawCircleEcl(target, up, radius * 0.15, Aim, segments: 16, drape: false);
    }

    // The warhead's own lethal radius, so what a ring circles is what arriving there does -- and so
    // two rings touching is the floor on how close two targets may be. A vehicle carrying nothing
    // that lets go still gets a mark, because the aim point is a designation rather than a property
    // of the payload.
    private static double RingRadius(IcbmComputer computer)
        => computer.Munition is { } warhead ? Warhead.LethalRadius(warhead.ChargeKg) : UnarmedRingMetres;

    // The places past the lead, each in its own ring. Dimmer and coarser than the aim ring: the
    // flight is aimed at the lead and these are where the bus is meant to go afterwards.
    private static void DrawOtherTargets(IcbmComputer computer)
    {
        if (computer.Targets.Count < 2) return;

        RingSet set = SetFor(computer);
        double radius = RingRadius(computer);

        for (int i = 0; i < computer.Targets.Count; i++)
        {
            if (i == computer.LeadTarget) continue;
            if (computer.SiteEcl(computer.Targets[i].Site) is not { } at) continue;

            double3 up = Vec.Unit(KsaWorld.GravityAt(computer.Craft, at) * -1.0);
            if (Vec.Len2(up) < 0.5) continue;

            DrawDrapedCircle(computer, set, set.TargetRing(i), at, up, radius, OtherAim, segments: 24);
        }
    }

    // The ground the bus can still divert to, at the trim budget the set has not spent. An ellipse
    // rather than a circle because the reach is one: pinned the two axes agree to within 3%, and a
    // shape drawn from the axes cannot go wrong on the geometry where they do not.
    private static void DrawReach(IcbmComputer computer)
    {
        if (!computer.Reach.HasRegion) return;
        if (computer.ReachCentreEcl() is not { } centre) return;
        if (!computer.TryReachAxesEcl(out double3 major, out double3 minor)) return;

        RingSet set = SetFor(computer);
        Ring ring = set.Reach;

        if (ring.Stale(centre - BodyEcl(computer), computer.Reach.SemiMajorMetres)
            && set.Redrapes < RedrapesPerFrame)
        {
            set.Redrapes++;
            ring.Since.Restart();
            ring.Anchor = centre - BodyEcl(computer);
            ring.Size = computer.Reach.SemiMajorMetres;

            KsaWorld.CollectDrapedRingEcl(centre, major, minor, ring.Offsets);
        }

        for (int i = 1; i < ring.Offsets.Count; i++)
        {
            KsaWorld.DrawLineEcl(centre + ring.Offsets[i - 1], centre + ring.Offsets[i], ReachEdge);
        }
    }

    // The screen-space half: a mark on the aim point wherever it is, with the countdown beside it.
    // Clamped to the edge when it is off screen rather than dropped, because a target behind the
    // camera is exactly when knowing which way it lies is worth something.
    private static void DrawMarks(IcbmComputers computers, IcbmComputer? focused)
    {
        bool anything = false;
        foreach (IcbmComputer computer in computers.All)
        {
            if (computer.Config.MarkTarget && computer.Target.IsSet) { anything = true; break; }
        }

        if (!anything) return;

        ImGuiViewportPtr main = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(main.Pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(main.Size, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0f);

        // NoInputs: the overlay covers the screen, so anything else would swallow every click in
        // the game.
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
                                       | ImGuiWindowFlags.NoInputs
                                       | ImGuiWindowFlags.NoNav
                                       | ImGuiWindowFlags.NoFocusOnAppearing
                                       | ImGuiWindowFlags.NoBringToFrontOnFocus
                                       | ImGuiWindowFlags.NoSavedSettings
                                       | ImGuiWindowFlags.NoBackground;

        if (!ImGui.Begin("##KSArmoryIcbmTargets", flags))
        {
            ImGui.End();
            return;
        }

        ImDrawListPtr draw = ImGui.GetWindowDrawList();

        foreach (IcbmComputer computer in computers.All)
        {
            if (!computer.Config.MarkTarget) continue;
            if (!KsaWorld.IsAlive(computer.Craft)) continue;
            if (computer.TargetEcl() is not { } targetEcl) continue;
            if (!KsaWorld.TryProjectOrClamp(targetEcl, out float2 at, out bool inView)) continue;

            IcbmCommand command = computer.Command;

            ImColor8 colour = command.Reach switch
            {
                IcbmReach.ShortOfPropellant => MarkBad,
                IcbmReach.NoTrajectory => MarkBad,
                _ when command.Phase == IcbmPhase.Holding => MarkHeld,
                _ => Mark,
            };

            DrawCross(draw, at, colour, inView);
            draw.AddText(new float2(at.X + Half + 6f, at.Y - Half), Text,
                         Numbered(computer, computer.LeadTarget) + Caption(computer));

            // The rest of the set, on the one computer being clicked at. Numbered because the panel
            // lists them by number and a ring on the ground with no number is a place nobody can
            // pair with a row.
            if (!ReferenceEquals(computer, focused)) continue;

            for (int i = 0; i < computer.Targets.Count; i++)
            {
                if (i == computer.LeadTarget) continue;
                if (computer.SiteEcl(computer.Targets[i].Site) is not { } siteEcl) continue;
                if (!KsaWorld.TryProjectOrClamp(siteEcl, out float2 where, out bool visible)) continue;

                DrawCross(draw, where, MarkOther, visible);
                draw.AddText(new float2(where.X + Half + 6f, where.Y - Half), Text,
                             Numbered(computer, i) + computer.Targets[i].Site.Describe()
                             + $"  {computer.Targets[i].Warheads} warhead(s)");
            }
        }

        ImGui.End();
    }

    // The number the panel's list uses, and only once there is a list: on a single-target shot a
    // "1" in front of the aim point is noise about a distinction that does not exist yet.
    private static string Numbered(IcbmComputer computer, int index)
        => computer.Targets.Count > 1 ? $"{index + 1}  " : "";

    // What the mark says: what it is, and when it lands.
    private static string Caption(IcbmComputer computer)
    {
        string name = computer.Target.Describe();
        IcbmCommand command = computer.Command;

        // The failures come first, because a countdown on a shot that cannot be made is worse than
        // no countdown at all.
        if (command.Reach == IcbmReach.NoTrajectory) return $"{name}  UNREACHABLE";

        if (command.Reach == IcbmReach.ShortOfPropellant)
        {
            return $"{name}  UNREACHABLE, short {command.ShortfallMetresPerSecond:F0} m/s";
        }

        double arrival = computer.SecondsToArrival;
        if (!double.IsFinite(arrival)) return name;

        return command.Phase == IcbmPhase.Holding
            ? $"{name}  impact T+{IcbmProgram.Clock(arrival)} (holding {IcbmProgram.Clock(command.SecondsToBurn)})"
            : $"{name}  impact T+{IcbmProgram.Clock(arrival)}";
    }

    private static void DrawCross(ImDrawListPtr draw, float2 at, ImColor8 colour, bool inView)
    {
        draw.AddLine(new float2(at.X - Half, at.Y), new float2(at.X - Half + Tick, at.Y), colour);
        draw.AddLine(new float2(at.X + Half, at.Y), new float2(at.X + Half - Tick, at.Y), colour);
        draw.AddLine(new float2(at.X, at.Y - Half), new float2(at.X, at.Y - Half + Tick), colour);
        draw.AddLine(new float2(at.X, at.Y + Half), new float2(at.X, at.Y + Half - Tick), colour);

        // A ring only while it is genuinely on screen. Clamped to the edge it would read as a place
        // out there rather than as a direction to look in.
        if (inView) draw.AddCircle(at, Half, colour, 16);
    }
}
