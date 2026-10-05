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

    // The ring pass's inks, whose alpha is the brightness kept over dark ground. The reach is the
    // designation colour thinned: it is where a target may go rather than where one is.
    private static readonly float4 PaintedAim = new(1.0f, 0.45f, 0.35f, 0.05f);
    private static readonly float4 PaintedOtherAim = new(1.0f, 0.68f, 0.45f, 0.05f);
    private static readonly float4 PaintedReach = new(1.0f, 0.55f, 0.4f, 0.04f);

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
    /// The computer the panel is showing, which is the only one whose reach is drawn: a region exists
    /// to place a target in, only one computer is being clicked at, and six ellipses over six rockets
    /// is not six times as useful. Every computer's targets are drawn, because where a salvo is going
    /// is worth seeing whichever rocket the panel is on.
    /// </param>
    public static void Draw(IcbmComputers computers, IcbmComputer? focused, List<double3> scratch)
    {
        DrawInWorld(computers, focused, scratch);
        DrawMarks(computers);
    }

    private static void DrawInWorld(IcbmComputers computers, IcbmComputer? focused, List<double3> scratch)
    {
        foreach (IcbmComputer computer in computers.All)
        {
            // The ring marks where the WARHEADS are going, so it outlives the craft that sent them:
            // a bus has no heat shield and they do, and it breaks up on reentry five to twenty
            // seconds before they arrive. Gated on the craft alone, the mark would go out with
            // the warheads still falling toward it. The aim point needs nothing from the vehicle --
            // Target and Parent are both latched -- so this costs only the check.
            bool alive = KsaWorld.IsAlive(computer.Craft);
            if (!alive && !computer.SalvoStillArriving) continue;

            // Marked while the shot is still going to happen, and while it is on its way -- but not
            // once it has landed, when the ring has nothing left to mark.
            if (computer.Config.MarkTarget && !computer.SalvoHasLanded
                && computer.TargetEcl() is { } target)
            {
                DrawAimRing(computer, target);
                DrawOtherTargets(computer);
                if (ReferenceEquals(computer, focused)) DrawReach(computer);
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

    // A ring on the terrain rather than a solid at the aim point: anything large enough to see from
    // orbit is large enough to sit over the target and hide it, and a shape with no radius says
    // nothing about how far the warhead reaches. Painted by the ring pass on whatever the depth buffer
    // holds, so it is an exact circle at any size with no terrain to sample.
    private static void DrawAimRing(IcbmComputer computer, double3 target)
    {
        double radius = RingRadius(computer);
        GroundRings.Add(target, radius, radius * 0.15, PaintedAim);
    }

    // The warhead's own lethal radius, so what a ring circles is what arriving there does -- and so
    // two rings touching is the floor on how close two targets may be. A vehicle carrying nothing
    // that lets go still gets a mark, because the aim point is a designation rather than a property
    // of the payload.
    private static double RingRadius(IcbmComputer computer)
        => computer.Munition is { } warhead ? Warhead.LethalRadius(warhead.ChargeKg) : UnarmedRingMetres;

    // The places past the lead, each in its own ring, a paler ink than the aim ring: the flight is
    // aimed at the lead and these are where the bus is meant to go afterwards.
    private static void DrawOtherTargets(IcbmComputer computer)
    {
        if (computer.Targets.Count < 2) return;

        double radius = RingRadius(computer);

        for (int i = 0; i < computer.Targets.Count; i++)
        {
            if (i == computer.LeadTarget) continue;
            if (computer.SiteEcl(computer.Targets[i].Site) is { } at) GroundRings.Add(at, radius, 0.0, PaintedOtherAim);
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

        GroundRings.AddEllipse(centre, major, Vec.Len(minor), PaintedReach);
    }

    // The screen-space half: a mark on the aim point wherever it is, with the countdown beside it.
    // Clamped to the edge when it is off screen rather than dropped, because a target behind the
    // camera is exactly when knowing which way it lies is worth something.
    private static void DrawMarks(IcbmComputers computers)
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

            // The rest of the set, numbered because the panel lists them by number and a ring on the
            // ground with no number is a place nobody can pair with a row. Each carries the same
            // countdown: every warhead of a walk is solved onto the one pinned arrival.
            string impact = ImpactIn(computer);

            for (int i = 0; i < computer.Targets.Count; i++)
            {
                if (i == computer.LeadTarget) continue;
                if (computer.SiteEcl(computer.Targets[i].Site) is not { } siteEcl) continue;
                if (!KsaWorld.TryProjectOrClamp(siteEcl, out float2 where, out bool visible)) continue;

                DrawCross(draw, where, MarkOther, visible);
                draw.AddText(new float2(where.X + Half + 6f, where.Y - Half), Text,
                             Numbered(computer, i) + computer.Targets[i].Site.Describe()
                             + $"  {computer.Targets[i].Warheads} warhead(s){impact}");
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

    // The countdown a further target's mark carries, or nothing when the lead's would say nothing.
    private static string ImpactIn(IcbmComputer computer)
    {
        if (computer.Command.Reach is IcbmReach.NoTrajectory or IcbmReach.ShortOfPropellant) return "";

        double arrival = computer.SecondsToArrival;
        return double.IsFinite(arrival) ? $"  impact T+{IcbmProgram.Clock(arrival)}" : "";
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
