using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The shot, drawn in the world: where the warheads are going, where they were told to go, and
/// when they arrive.
///
/// <para>The line is the trajectory <see cref="ImpactPredictor"/> flew from the vehicle's actual
/// state; the ring is the place it was aimed at. While they disagree the burn is not finished.</para>
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

    private static readonly ImColor8[] Chain =
    [
        new(255, 175, 115, 225), new(120, 200, 255, 225), new(170, 235, 120, 225), new(240, 130, 220, 225),
        new(255, 225, 110, 225), new(120, 235, 215, 225), new(200, 160, 255, 225), new(255, 130, 130, 225),
    ];

    // Coarse enough that a half-hour arc is a few dozen lines rather than a few thousand.
    private const int MaxSegments = 96;

    // What to circle when there is no warhead to ask. Small enough to read as a mark rather than
    // a claim about anything.
    private const double UnarmedRingMetres = 250.0;

    private const float Half = 9f;
    private const float Tick = 5f;

    /// <param name="focused">
    /// The computer the panel is showing, the only one whose reach is drawn, since only one is
    /// being clicked at. Every computer's targets are drawn.
    /// </param>
    public static void Draw(IcbmComputers computers, IcbmComputer? focused, List<double3> scratch)
    {
        DrawInWorld(computers, focused, scratch);
        DrawMarks(computers, focused);
    }

    private static void DrawInWorld(IcbmComputers computers, IcbmComputer? focused, List<double3> scratch)
    {
        foreach (IcbmComputer computer in computers.All)
        {
            // The ring outlives the craft: a bus breaks up on reentry five to twenty seconds before
            // its warheads arrive. Target and Parent are latched, so it needs nothing from the craft.
            bool alive = KsaWorld.IsAlive(computer.Craft);
            if (!alive && !computer.SalvoStillArriving) continue;

            if (computer.Config.MarkTarget && !computer.SalvoHasLanded
                && computer.TargetEcl() is { } target)
            {
                DrawAimRing(computer, target);
                DrawOtherTargets(computer);
                if (ReferenceEquals(computer, focused)) DrawReach(computer);
            }

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

    // A ring rather than a solid, which large enough to see from orbit would hide the target.
    // Painted by the ring pass, so it is exact at any size with no terrain to sample.
    private static void DrawAimRing(IcbmComputer computer, double3 target)
    {
        double radius = RingRadius(computer);
        GroundRings.Add(target, radius, radius * 0.15, PaintedAim);
    }

    // The lethal radius, so two rings touching is the floor on how close two targets may be.
    private static double RingRadius(IcbmComputer computer)
        => computer.Munition is { } warhead ? Warhead.LethalRadius(warhead.ChargeKg) : UnarmedRingMetres;

    // Paler than the aim ring: the flight is aimed at the lead.
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

    // An ellipse: pinned, the two axes agree to within 3%, and drawn from the axes it cannot go
    // wrong where they do not.
    private static void DrawReach(IcbmComputer computer)
    {
        if (!computer.Reach.HasRegion) return;
        if (computer.ReachCentreEcl() is not { } centre) return;
        if (!computer.TryReachAxesEcl(out double3 major, out double3 minor)) return;

        GroundRings.AddEllipse(centre, major, Vec.Len(minor), PaintedReach);
    }

    // Clamped to the screen edge rather than dropped, so a target behind the camera still says
    // which way it lies.
    private static void DrawMarks(IcbmComputers computers, IcbmComputer? focused)
    {
        bool anything = false;
        foreach (IcbmComputer computer in computers.All)
        {
            if (computer.Config.MarkTarget && computer.Target.IsSet) { anything = true; break; }
        }

        if (!anything) return;

        if (!ScreenOverlay.Begin("##KSArmoryIcbmTargets"))
        {
            ImGui.End();
            return;
        }

        ImDrawListPtr draw = ImGui.GetWindowDrawList();

        int rockets = 0;
        foreach (IcbmComputer _ in computers.All) rockets++;

        int seat = -1;
        foreach (IcbmComputer computer in computers.All)
        {
            seat++;
            if (!computer.Config.MarkTarget) continue;
            if (!KsaWorld.IsAlive(computer.Craft)) continue;
            if (computer.TargetEcl() is not { } targetEcl) continue;
            if (!KsaWorld.TryProjectOrClamp(targetEcl, out float2 at, out bool inView)) continue;

            // One colour and chain per rocket, and full labels only on the focused one: eight identical
            // label sets read as columns of stops rather than eight walks.
            bool full = rockets <= 1 || ReferenceEquals(computer, focused);
            ImColor8 own = rockets <= 1 ? MarkOther : Chain[seat % Chain.Length];
            string craft = KsaWorld.DisplayName(computer.Craft);

            IcbmCommand command = computer.Command;

            ImColor8 colour = command.Reach switch
            {
                IcbmReach.ShortOfPropellant => MarkBad,
                IcbmReach.NoTrajectory => MarkBad,
                _ when command.Phase == IcbmPhase.Holding => MarkHeld,
                _ => Mark,
            };

            int count = computer.Targets.Count;
            var marks = new (float2 At, bool Visible)?[count];
            if (computer.LeadTarget >= 0 && computer.LeadTarget < count) marks[computer.LeadTarget] = (at, inView);

            DrawCross(draw, at, colour, inView);
            draw.AddText(new float2(at.X + Half + 6f, at.Y - Half), full ? Text : own,
                         full ? Numbered(computer, computer.LeadTarget) + (rockets > 1 ? $"{craft}  " : "") + Caption(computer)
                              : $"{Numbered(computer, computer.LeadTarget)}{craft}");

            // Numbered as the panel lists them, each with the lead's countdown: a walk is solved onto
            // one pinned arrival.
            string impact = ImpactIn(computer);

            for (int i = 0; i < count; i++)
            {
                if (i == computer.LeadTarget) continue;
                if (computer.SiteEcl(computer.Targets[i].Site) is not { } siteEcl) continue;
                if (!KsaWorld.TryProjectOrClamp(siteEcl, out float2 where, out bool visible)) continue;

                marks[i] = (where, visible);
                DrawCross(draw, where, MarkOther, visible);
                draw.AddText(new float2(where.X + Half + 6f, where.Y - Half), full ? Text : own,
                             full ? Numbered(computer, i) + computer.Targets[i].Site.Describe()
                                    + $"  {computer.Targets[i].Warheads} warhead(s){impact}"
                                  : $"{i + 1}");
            }

            if (count < 2) continue;

            int[] order = computer.Walk.Walk.VisitOrder(count, computer.LeadTarget);
            for (int k = 1; k < order.Length; k++)
            {
                if (order[k - 1] >= count || order[k] >= count) continue;
                if (marks[order[k - 1]] is not { Visible: true } from || marks[order[k]] is not { Visible: true } to) continue;

                draw.AddLine(from.At, to.At, own, 1.5f);
            }
        }

        ImGui.End();
    }

    private static string Numbered(IcbmComputer computer, int index)
        => computer.Targets.Count > 1 ? $"{index + 1}  " : "";

    private static string Caption(IcbmComputer computer)
    {
        string name = computer.Target.Describe();
        IcbmCommand command = computer.Command;

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

        // Clamped to the edge, a ring would read as a place rather than a direction.
        if (inView) draw.AddCircle(at, Half, colour, 16);
    }
}
