using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The ballistic computer's pane: where the warheads are going, and everything that decides it.
///
/// <para>Laid out in the order the questions get asked, which is not the order the settings are
/// declared in. What is it aimed at, will it get there, what is it doing about it now — and the
/// numbers that shape the shot below that, because they are chosen once and then watched rather
/// than adjusted.</para>
///
/// <para>The line that earns its place is <b>Holding</b>. Every gate in the program returns
/// quietly, so a computer that is unarmed, one with no target, one whose stack is short of the
/// delta-v and one waiting for an engine all look identical from outside: a rocket sitting on the
/// pad doing nothing.</para>
/// </summary>
internal sealed partial class Ui
{
    private readonly IcbmComputers _icbms = icbms;

    private string _siteLabel = string.Empty;
    private float _siteLat;
    private float _siteLon;
    private static readonly float4 Good = new(0.55f, 0.95f, 0.55f, 1f);
    private static readonly float4 Working = new(0.95f, 0.85f, 0.45f, 1f);
    private static readonly float4 Bad = new(0.98f, 0.5f, 0.45f, 1f);

    private void DrawIcbm(IcbmComputer computer)
    {
        DrawIcbmTarget(computer);
        ImGui.Separator();
        DrawIcbmStatus(computer);
        ImGui.Separator();
        DrawIcbmTrajectory(computer);
    }

    private void DrawIcbmTarget(IcbmComputer computer)
    {
        IcbmConfig config = computer.Config;
        Celestial? parent = computer.Parent;

        ImGui.TextDisabled(parent is null
            ? "no parent body - nothing to fly a ballistic arc around"
            : $"flying about {parent.Id}");

        ImGui.Text($"Target: {computer.Target.Describe()}");

        if (computer.Target.IsSet && parent is not null && computer.Target.BodyName != parent.Id)
        {
            // A ballistic arc is a two-body problem about one planet. Another world is an
            // interplanetary transfer, which is a different manoeuvre, not a longer one.
            ImGui.TextColored(Bad, $"designated on {computer.Target.BodyName}, which is not the body");
            ImGui.TextColored(Bad, "this vehicle is flying around. Only ballistic shots are flown.");
        }

        // A mode, not a button: pressing a button puts the cursor over the panel, so what it reads
        // is whatever lies behind the control rather than the place being pointed at.
        bool picking = config.DesignateByClicking;
        if (ImGui.Checkbox("Designate by clicking the world", ref picking)) config.DesignateByClicking = picking;

        ImGui.TextDisabled(config.DesignateByClicking
            ? "  a ring follows the cursor; click the ground to aim there"
            : "  or enter coordinates below");

        if (config.DesignateByClicking)
        {
            ImGui.TextDisabled("  shift-click is still the lock gesture, and clicks on a window do nothing");
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear target")) computer.Designate(AimSite.None);

        ImGui.Separator();

        ImGui.SliderFloat("Latitude", ref _siteLat, -89.9f, 89.9f, "%.4f");
        ImGui.SliderFloat("Longitude", ref _siteLon, -180f, 180f, "%.4f");
        TextField("Label", ref _siteLabel);

        if (ImGui.Button("Designate those coordinates") && parent is not null)
        {
            computer.Designate(new AimSite(parent.Id, _siteLat, _siteLon,
                                           string.IsNullOrWhiteSpace(_siteLabel) ? "" : _siteLabel.Trim()));
        }
    }

    private void DrawIcbmStatus(IcbmComputer computer)
    {
        IcbmConfig config = computer.Config;
        IcbmCommand command = computer.Command;

        bool armed = config.Armed;
        if (ImGui.Checkbox("Ballistic computer armed", ref armed)) config.Armed = armed;

        ImGui.SameLine();
        if (ImGui.Button("Release one warhead"))
        {
            if (!computer.Release(_batteries.For(computer.Craft)?.Battery))
            {
                Log.Warn("nothing to release: no weapon aboard, none left, or it is still reloading");
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Abort"))
        {
            config.Armed = false;
            computer.Abort("aborted from the panel");
        }

        // First of all, because without it none of the rest can happen. A rocket that plans a
        // perfect shot and will not turn is the least explicable state this mod has.
        if (!AttitudeHook.Installed)
        {
            ImGui.TextColored(Bad, "NO ATTITUDE CONTROL - this vehicle cannot be pointed");
            ImGui.TextDisabled($"  {AttitudeHook.Trouble}");
            ImGui.Separator();
        }

        // Then, and unmissable. Everything below is detail about a shot that is not going to
        // happen if this line is showing.
        if (command.Reach == IcbmReach.NoTrajectory)
        {
            ImGui.TextColored(Bad, "TARGET UNREACHABLE - no trajectory arrives there");
        }
        else if (command.Reach == IcbmReach.ShortOfPropellant)
        {
            ImGui.TextColored(Bad, $"TARGET UNREACHABLE - short by "
                                   + $"{command.ShortfallMetresPerSecond:F0} m/s of delta-v");
            ImGui.TextDisabled("  measured against one stage's exhaust velocity over the whole");
            ImGui.TextDisabled("  vehicle's propellant, so a deeply staged rocket may still make it");
        }

        // Its own line because it is the one unreachable a setting on this panel can fix, and
        // reading it as "no trajectory" sends the operator after a different target instead.
        if (command.Reach == IcbmReach.TooShallow)
        {
            ImGui.TextColored(Bad, $"NO ARC ARRIVES AT {config.MinArrivalAngleDeg:F0} DEG OR STEEPER");
            ImGui.TextDisabled("  lower the steepest-arrival minimum below, or pick a nearer target");
        }

        // The explanation for an otherwise inexplicable number. A deorbit onto the ground track
        // costs a hundred metres a second; the same shot at a place well off the plane costs
        // thousands, and nothing else on this panel says which of those is being quoted — or that
        // the answer is a different orbit rather than a bigger tank.
        if (computer.OffPlaneDegrees > OrbitPlane.NotableDegrees && computer.Target.IsSet)
        {
            double closest = computer.Program.ClosestOffPlaneDegrees;

            ImGui.TextColored(Working, $"Target is {computer.OffPlaneDegrees:F0} deg off this orbit's plane");

            // The instantaneous angle says the target is off the plane. Whether that is a wait or a
            // dead end is the *closest* it ever comes, which is what the search already measured
            // across a day of revolutions.
            if (double.IsFinite(closest) && closest > OrbitPlane.NotableDegrees)
            {
                ImGui.TextColored(Bad, $"  and never closer than {closest:F0} deg - this orbit does not");
                ImGui.TextColored(Bad, "  reach that latitude. Waiting cannot fix an inclination.");
            }
            else if (double.IsFinite(closest))
            {
                ImGui.TextDisabled($"  it comes within {closest:F0} deg later on, which is what the wait is for.");
            }

            ImGui.TextDisabled($"  turning the plane from here is about {computer.PlaneChangeCost:F0} m/s,");
            ImGui.TextDisabled("  cheapest a quarter orbit before the target and burnt normal to the plane.");
        }

        ImGui.TextColored(PhaseColour(command.Phase), $"Phase: {command.Phase}");
        ImGui.TextColored(command.Phase == IcbmPhase.NoSolution ? Bad : Working, $"Holding: {command.Hold}");

        ImGui.Separator();

        // The one number a player actually wants off this panel.
        double arrival = computer.SecondsToArrival;
        if (double.IsFinite(arrival))
        {
            // Said differently once the engines are off, because it means something different: a
            // warhead released *this instant* would land then, and release waits for the bus to
            // clear the stack and for the trim to finish. Reading it as a countdown to the impact
            // is reading it early by however long that takes.
            ImGui.TextColored(Good, computer.ArrivalIsIfReleasedNow
                                        ? $"IMPACT IF RELEASED NOW  {IcbmProgram.Clock(arrival)}"
                                        : $"IMPACT IN  {IcbmProgram.Clock(arrival)}");
        }
        else
        {
            ImGui.TextDisabled("IMPACT IN  --:--   (no shot under way)");
        }

        if (double.IsFinite(command.SecondsToBurn) && command.SecondsToBurn > 0.0)
        {
            ImGui.TextColored(Working, $"Burn starts in {IcbmProgram.Clock(command.SecondsToBurn)}"
                                       + $"   ({command.VelocityToGain:F0} m/s to spend)");
            ImGui.TextDisabled("  coasting on purpose - leaving now costs far more than waiting does");

            // A button rather than something that happens. Taking the world's clock away because a
            // target was designated is not a weapon's decision to make.
            if (computer.CanWarpToWindow && ImGui.Button("Warp to the burn window"))
            {
                computer.TryWarpToWindow();
            }
        }

        if (computer.Program.Arc is { } arc)
        {
            ImGui.Text($"Planned arc: {(arc.ApogeeRadius - computer.Body.SurfaceRadius) / 1000.0:F0} km up,"
                       + $" then falls for {arc.FlightSeconds / 60.0:F1} min");
        }
        else
        {
            ImGui.TextDisabled("Planned arc: none solved yet");
        }

        if (command.VelocityToGain > 0.0)
        {
            string cutoff = double.IsFinite(command.SecondsToCutoff)
                ? $"cutoff in {command.SecondsToCutoff:F0} s"
                : "it cannot finish this burn";
            ImGui.Text($"Still to gain: {command.VelocityToGain:F0} m/s   -   {cutoff}");
        }

        // What the bus is doing between the burn ending and the first warhead leaving, which is
        // otherwise a stretch of coast with nothing happening on screen and the release held.
        if (computer.TrimSaid.Length > 0)
        {
            double left = computer.TrimToGainMetresPerSecond;

            // Wrapped, because this line is a sentence rather than a readout and the panel is
            // narrow by default: unwrapped it runs off the edge and is only legible with the
            // window pulled across the whole screen.
            ImGui.PushStyleColor(ImGuiCol.Text,
                                 double.IsFinite(left) && left <= BusTrim.SettledMetresPerSecond
                                     ? Good : Working);
            ImGui.TextWrapped($"Bus trim: {computer.TrimSaid}");
            ImGui.PopStyleColor();
        }

        // Named for what it actually is. It is a free-fall prediction, so on the pad the honest
        // answer is "on the pad" - true, useless, and worth saying rather than dressing up.
        if (double.IsFinite(computer.PredictedMissMetres))
        {
            double miss = computer.PredictedMissMetres;
            ImGui.TextColored(miss < 2000.0 ? Good : Working,
                              miss < 1000.0
                                  ? $"If the engines stopped now: {miss:F0} m from the target"
                                  : $"If the engines stopped now: {miss / 1000.0:F1} km from the target");
        }
        else if (!computer.Target.IsSet)
        {
            ImGui.TextDisabled("If the engines stopped now: nothing to measure against");
        }
        else if (computer.AltitudeMetres < 1000.0)
        {
            ImGui.TextDisabled("If the engines stopped now: it is still on the ground");
        }
        else
        {
            ImGui.TextColored(Bad, "If the engines stopped now: it never comes down");
        }

        ImGui.Separator();

        BoosterPerformance booster = new(computer.Craft.FlightComputer.ActiveEnginePerformanceMax.Thrust,
                                         computer.Craft.FlightComputer.ActiveEnginePerformanceMax.MassFlowRate,
                                         computer.Craft.TotalMass, computer.Craft.PropellantMass);

        ImGui.Text($"This stage: {booster.DeltaVRemaining / 1000.0:F2} km/s of delta-v, "
                   + $"{booster.BurnSecondsRemaining:F0} s of burn, "
                   + $"{booster.AccelerationNow / 9.81:F1} g");

        // The caveat that makes the number above readable. KSA reports the engines that are
        // running, so a three-stage rocket on the pad shows the first stage's figure and looks
        // hopelessly short of a shot it can comfortably make.
        ImGui.TextDisabled("  the running stage only - a stack with more below this reads low");
    }

    private void DrawIcbmTrajectory(IcbmComputer computer)
    {
        IcbmConfig config = computer.Config;

        bool mark = config.MarkTarget;
        if (ImGui.Checkbox("Mark the target and count down to impact", ref mark)) config.MarkTarget = mark;
        ImGui.TextDisabled(config.MarkTarget
            ? "  stays on screen wherever the target is, and points at it from the edge"
            : "  the target is only visible on this tab");

        bool draw = config.DrawTrajectory;
        if (ImGui.Checkbox("Draw the predicted trajectory", ref draw)) config.DrawTrajectory = draw;
        ImGui.TextDisabled(config.DrawTrajectory
            ? "  the arc it is on now, and a ring on the aim point"
            : "  nothing is drawn in the world for this vehicle");

        // Above Loft, because it overrides it: the two both move the flight time, and a control
        // that wins an argument reads better before the one it wins it with than after.
        float floor = (float)config.MinArrivalAngleDeg;
        if (ImGui.SliderFloat("Steepest arrival", ref floor, 0f, 45f, "%.0f deg minimum"))
        {
            config.MinArrivalAngleDeg = floor;
        }

        // Asked beside achieved, because those two differing is the whole reason this control
        // exists: before it, the arrival was whatever the cheapest arc happened to give.
        double planned = computer.Program.Arc?.ArrivalAngleDeg ?? double.NaN;
        string arriving = double.IsFinite(planned) ? $"; the arc it has arrives at {planned:F0} deg"
                                                   : "; no arc solved yet";

        ImGui.TextDisabled("  " + (config.MinArrivalAngleDeg < 0.5
            ? "off - the cheapest arc wins, which from orbit is a graze at about 7 deg" + arriving
            : $"no shallower than {config.MinArrivalAngleDeg:F0} deg{arriving}"));

        if (config.MinArrivalAngleDeg >= 0.5)
        {
            ImGui.TextDisabled("  steeper is more accurate and costs reach: 15-20 deg is where");
            ImGui.TextDisabled("  the trade turns, and it overrides Loft where they disagree");
        }

        // A multiplier on the cheapest flight time, shown as one. Printed bare it reads as an
        // absolute setting, and then 1.00 needs a sentence to explain that it is not.
        float loft = (float)config.Loft;
        if (ImGui.SliderFloat("Loft", ref loft, 0.6f, 1.8f, "%.2f x cheapest")) config.Loft = loft;
        ImGui.TextDisabled("  " + (config.Loft > 1.005
            ? "a longer flight than the cheapest: higher, slower, arrives steeper, costs more"
            : config.Loft < 0.995
                ? "a shorter flight than the cheapest: flatter and faster, and costs more"
                : "minimum energy - the cheapest shot there is"));

        bool autoRelease = config.AutoRelease;
        if (ImGui.Checkbox("Release warheads automatically", ref autoRelease)) config.AutoRelease = autoRelease;
        ImGui.TextDisabled(config.AutoRelease
            ? "  one at a time from the coast, once past the release altitude"
            : "  nothing leaves the bus until the button above is pressed");

        bool trim = config.TrimBeforeRelease;
        if (ImGui.Checkbox("Trim the bus before releasing", ref trim)) config.TrimBeforeRelease = trim;
        ImGui.TextDisabled(config.TrimBeforeRelease
            ? "  thrusters put it back on the solution after the split, which the burn cannot"
            : "  the warheads leave on whatever the cutoff and the decoupler left the bus doing");

        bool repoint = config.RepointBetweenReleases;
        if (ImGui.Checkbox("Aim each tube before it fires", ref repoint))
        {
            config.RepointBetweenReleases = repoint;
        }
        ImGui.TextDisabled(config.RepointBetweenReleases
            ? "  turns between releases so every round leaves on the same line"
            : "  all rounds leave on the attitude the burn ended on, and spread by the tube cant");

        bool autoStage = config.AutoStage;
        if (ImGui.Checkbox("Stage automatically", ref autoStage)) config.AutoStage = autoStage;
        ImGui.TextDisabled(config.AutoStage
            ? "  fires the next stage when the running one has nothing left"
            : "  staging is yours, including the one that lights the first engine");

        if (ImGui.TreeNode("Ascent"))
        {
            float turnStart = (float)config.TurnStartMetres;
            if (ImGui.SliderFloat("Pitch-over starts (m)", ref turnStart, 100f, 5000f, "%.0f"))
            {
                config.TurnStartMetres = turnStart;
            }

            float turnEnd = (float)config.TurnEndMetres;
            if (ImGui.SliderFloat("Pitch programme ends (m)", ref turnEnd, 10_000f, 120_000f, "%.0f"))
            {
                config.TurnEndMetres = turnEnd;
            }

            float aoa = (float)config.MaxAngleOfAttackDeg;
            if (ImGui.SliderFloat("Angle of attack limit (deg)", ref aoa, 1f, 30f, "%.1f"))
            {
                config.MaxAngleOfAttackDeg = aoa;
            }
            ImGui.TextDisabled($"  the stack is held within {config.MaxAngleOfAttackDeg:F0} deg of the airflow while loaded");

            float handover = (float)config.HandoverPressurePa;
            if (ImGui.SliderFloat("Guidance takes over below (Pa)", ref handover, 50f, 20_000f, "%.0f"))
            {
                config.HandoverPressurePa = handover;
            }
            ImGui.TextDisabled($"  dynamic pressure, so {config.HandoverPressurePa:F0} Pa means the same "
                               + "thing on a body with no air");

            float deploy = (float)config.DeployAltitudeMetres;
            if (ImGui.SliderFloat("Release warheads above (m)", ref deploy, 1_000f, 400_000f, "%.0f"))
            {
                config.DeployAltitudeMetres = deploy;
            }

            ImGui.TreePop();
        }
    }

    private static float4 PhaseColour(IcbmPhase phase) => phase switch
    {
        IcbmPhase.NoSolution => Bad,
        IcbmPhase.Idle => new float4(0.7f, 0.7f, 0.7f, 1f),
        IcbmPhase.Coast => Good,
        _ => Working,
    };
}
