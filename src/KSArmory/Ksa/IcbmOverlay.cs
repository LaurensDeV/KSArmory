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

    private static readonly ImColor8 Mark = new(255, 115, 90, 235);
    private static readonly ImColor8 MarkHeld = new(245, 215, 115, 235);
    private static readonly ImColor8 MarkBad = new(250, 90, 80, 245);
    private static readonly ImColor8 Text = new(238, 240, 245, 245);

    // Coarse enough that a half-hour arc is a few dozen lines rather than a few thousand.
    private const int MaxSegments = 96;

    // What to circle when there is no warhead to ask. Small enough to read as a mark rather than
    // a claim about anything.
    private const double UnarmedRingMetres = 250.0;

    private const float Half = 9f;
    private const float Tick = 5f;

    public static void Draw(IcbmComputers computers, List<double3> scratch)
    {
        DrawInWorld(computers, scratch);
        DrawMarks(computers);
    }

    private static void DrawInWorld(IcbmComputers computers, List<double3> scratch)
    {
        foreach (IcbmComputer computer in computers.All)
        {
            if (!KsaWorld.IsAlive(computer.Craft)) continue;

            if (computer.Config.MarkTarget && computer.TargetEcl() is { } target)
            {
                DrawAimRing(computer, target);
            }

            if (!computer.Config.DrawTrajectory) continue;

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
    private static void DrawAimRing(IcbmComputer computer, double3 target)
    {
        // Off gravity, because that is the one direction the mod already resolves everywhere.
        double3 up = Vec.Unit(KsaWorld.GravityAt(computer.Craft, target) * -1.0);
        if (Vec.Len2(up) < 0.5) return;

        // The warhead's own lethal radius, so what the ring circles is what arriving there does.
        // A vehicle carrying nothing that lets go still gets a mark, because the aim point is a
        // designation rather than a property of the payload.
        double radius = computer.Munition is { } warhead
                            ? Warhead.LethalRadius(warhead.ChargeKg)
                            : UnarmedRingMetres;

        KsaWorld.DrawCircleEcl(target, up, radius, Aim);
        KsaWorld.DrawCircleEcl(target, up, radius * 0.15, Aim, segments: 16);
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
            draw.AddText(new float2(at.X + Half + 6f, at.Y - Half), Text, Caption(computer));
        }

        ImGui.End();
    }

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
