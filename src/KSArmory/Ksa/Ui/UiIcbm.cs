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

    // Double, and entered rather than dragged. AimSite carries doubles and the round flies to
    // whatever is typed here, so this widget's own resolution is a hard floor under the whole shot
    // -- docs/KINETIC-FLOOR.md section 7. A float holds latitude to 0.21 m and longitude to 1.70 m
    // near the date line, and a slider spanning half a turn moves about 100 km per pixel.
    private double _siteLat;
    private double _siteLon;
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

        // Describe() rounds to three decimals, which is 111 m and right for an overlay label and
        // wrong for the one place an operator might copy a coordinate down. Printed in full here
        // rather than made more precise there.
        if (computer.Target.IsSet)
        {
            ImGui.TextDisabled($"  {computer.Target.LatitudeDeg:F7}, {computer.Target.LongitudeDeg:F7}");
        }

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

        ImGui.InputDouble("Latitude", ref _siteLat, 0.0, 0.0, "%.7f", ImGuiInputTextFlags.None);
        ImGui.InputDouble("Longitude", ref _siteLon, 0.0, 0.0, "%.7f", ImGuiInputTextFlags.None);

        _siteLat = Math.Clamp(_siteLat, -89.9, 89.9);
        _siteLon = Math.Clamp(_siteLon, -180.0, 180.0);

        if (parent is not null)
        {
            double lastDigit = 1e-7 * 2.0 * Math.PI * parent.MeanRadius / 360.0;

            ImGui.TextDisabled($"  the last digit is {lastDigit:F2} m of latitude and "
                               + $"{lastDigit * Math.Cos(_siteLat * Math.PI / 180.0):F2} m of "
                               + $"longitude on {parent.Id}");
        }

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

        // Beside the impact clock rather than under a fold: a ballistic coast is half an hour of
        // nothing happening, and a control that answers "must I sit through this" is no use behind
        // a disclosure triangle.
        if (computer.Program.Phase == IcbmPhase.Coast)
        {
            // The coast's own clock. The impact time above is a different question and minutes
            // later: what a coast is counting down to is the warheads leaving, and without this
            // the only number on the panel that moves is one nothing is waiting for.
            double toRelease = computer.SecondsToRelease;

            if (!double.IsFinite(toRelease))
            {
                ImGui.TextDisabled("RELEASE IN  --:--   (the warheads are being held)");
            }
            else if (toRelease > 0.0)
            {
                ImGui.TextColored(Working, $"RELEASE IN  {IcbmProgram.Clock(toRelease)}");
            }
            else
            {
                ImGui.TextColored(Good, "RELEASE  due now");
            }

            if (computer.CanWarpTheCoast && ImGui.Button("Warp the coast"))
            {
                computer.TryWarpTheCoast();
            }

            bool auto = config.WarpTheCoast;
            if (ImGui.Checkbox("Warp the coast without asking", ref auto)) config.WarpTheCoast = auto;

            // Says when the world comes back rather than only that it will. The hand-back is what
            // ends the fast part of the coast, and it is a settling margin ahead of the release.
            double toNormal = computer.SecondsToReleaseApproach;

            ImGui.TextDisabled("  " + (config.WarpTheCoast
                ? double.IsFinite(toNormal) && toNormal > 0.0
                      ? $"back to normal speed in {IcbmProgram.Clock(toNormal)}, "
                        + "a settling margin before the release"
                      : "presses that for you every shot, and hands the world back before the release"
                : "off - the coast runs at whatever speed you set"));
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

        // Named for what it actually is, and that changes twice during a flight. It is a free-fall
        // prediction of the craft this computer is flying: a what-if while the engines are running,
        // the actual answer once they have stopped, and about a vehicle nobody is aiming any more
        // the moment a warhead leaves. Only the first of those is a question about the engines.
        string what = computer.WarheadsAway > 0 ? "The bus alone would land"
                    : computer.Program.IsBurning ? "If the engines stopped now"
                    : "Predicted impact";

        if (computer.WarheadsAway > 0)
        {
            // The shot has left, so the bus's own arc answers nothing about it. Said rather than
            // hidden, because the line was on screen a moment ago and a readout that silently
            // vanishes reads as broken.
            ImGui.TextDisabled($"{computer.WarheadsAway} warhead(s) away - they are on their own "
                               + "arcs now, and this no longer describes the shot");
        }

        if (double.IsFinite(computer.PredictedMissMetres))
        {
            double miss = computer.PredictedMissMetres;

            if (computer.WarheadsAway > 0)
            {
                ImGui.TextDisabled(miss < 1000.0
                                       ? $"  {what}: {miss:F0} m from the target"
                                       : $"  {what}: {miss / 1000.0:F1} km from the target");
            }
            else
            {
                ImGui.TextColored(miss < 2000.0 ? Good : Working,
                                  miss < 1000.0
                                      ? $"{what}: {miss:F0} m from the target"
                                      : $"{what}: {miss / 1000.0:F1} km from the target");
            }
        }
        else if (!computer.Target.IsSet)
        {
            ImGui.TextDisabled($"{what}: nothing to measure against");
        }
        else if (computer.AltitudeMetres < 1000.0)
        {
            ImGui.TextDisabled($"{what}: it is still on the ground");
        }
        else
        {
            ImGui.TextColored(Bad, $"{what}: it never comes down");
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

        float hold = (float)config.ReleaseBeforeArrivalSeconds;
        if (ImGui.SliderFloat("Release at", ref hold, 0f, 900f, "%.0f s before arrival"))
        {
            config.ReleaseBeforeArrivalSeconds = hold;
        }

        ImGui.TextDisabled("  " + (config.ReleaseBeforeArrivalSeconds < 1.0
            ? "as soon as the altitude allows, which is early on the way up"
            : $"held until {config.ReleaseBeforeArrivalSeconds / 60.0:F0} min from arrival, so the "
              + "ejection kick has less flight to grow in"));

        float budget = (float)config.TrimBudgetMetresPerSecond;
        if (ImGui.SliderFloat("Trim budget", ref budget, 0f, 60f, "%.0f m/s for the flight"))
        {
            config.TrimBudgetMetresPerSecond = budget;
        }

        ImGui.TextDisabled("  " + (config.TrimBudgetMetresPerSecond <= 0.0
            ? "no trimming at all; the warheads go on the aim as the burn left it"
            : $"{config.TrimBudgetMetresPerSecond:F0} m/s across every correction, then it stops"
              + (config.TrimBudgetMetresPerSecond < PostBoostAim.MaxTrimMetresPerSecond
                     ? $" - under the {PostBoostAim.MaxTrimMetresPerSecond:F0} the bus reserves anyway"
                     : "")));

        // A structural limit rather than a preference, so it sits with the other things that
        // constrain the flight rather than with the ones that shape it.
        float gee = config.MaxAccelerationGee;
        if (ImGui.SliderFloat("Acceleration limit", ref gee, 0f, 15f, "%.1f g"))
        {
            config.MaxAccelerationGee = gee;
        }

        double now = computer.Program.LastBooster.AccelerationNow / 9.80665;
        string pulling = double.IsFinite(now) && now > 0.0 ? $"; pulling {now:F1} g now" : "";

        // Reports the airframe's own limit rather than being a second switch for it. There is
        // nothing to set: the engine destroys the vehicle at that number whatever anybody types,
        // so the guidance holds under it and this says what it settled on.
        double airframe = computer.AirframeLimitGee;

        ImGui.TextDisabled("  " + (config.MaxAccelerationGee < 0.05
            ? airframe > 0.0
                  ? $"the airframe's own {airframe:F1} g limit only{pulling}"
                  : "off - full throttle throughout, whatever the stack ends up pulling" + pulling
            : $"throttled to hold {config.MaxAccelerationGee:F1} g{pulling}"));

        if (airframe > 0.0)
        {
            ImGui.TextDisabled($"  KSA destroys this stack at {airframe:F1} g, off its own size; "
                               + $"the guidance holds it to {airframe * IcbmProgram.StructuralMarginFraction:F1}");
        }

        // Above Loft, because it overrides it: the two both move the flight time, and a control
        // that wins an argument reads better before the one it wins it with than after.
        // Bounded by what the stack can pay for, not by a round number. Arrival angle is bought
        // with propellant, and the ceiling is a property of this rocket against this target -- so a
        // fixed 45 lets an operator ask for an angle no arc can be flown at and find out only when
        // the shot falls short. The mod does not refuse such a shot, which makes the ceiling worth
        // showing rather than discovering.
        double afford = computer.Program.SteepestAffordableArrivalDeg;
        bool bounded = double.IsFinite(afford) && afford >= ArrivalBudget.ResolutionDeg;

        // Never below where the slider already is. The ceiling falls as the tanks empty, and a
        // maximum that walks down past a live setting silently rewrites it mid-flight.
        float top = bounded ? (float)Math.Max(afford, config.MinArrivalAngleDeg) : 45f;

        float floor = (float)config.MinArrivalAngleDeg;
        if (ImGui.SliderFloat("Steepest arrival", ref floor, 0f, top, "%.0f deg minimum"))
        {
            config.MinArrivalAngleDeg = Math.Min(floor, top);
        }

        if (bounded)
        {
            bool atTheLimit = config.MinArrivalAngleDeg >= afford - ArrivalBudget.ResolutionDeg;

            ImGui.TextColored(atTheLimit ? Working : Good,
                              $"  the stack can afford {afford:F0} deg from here");
        }
        else if (double.IsFinite(afford))
        {
            ImGui.TextColored(Bad, "  the stack cannot afford any arc to that target");
        }
        else
        {
            ImGui.TextDisabled("  nothing costed yet, so the limit is unknown");
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

        // Beside the floor rather than with the other switches, because the floor is what turns it
        // from the thing that closes the miss into the thing that causes it.
        bool correct = config.CorrectAim;
        if (ImGui.Checkbox("Correct the aim from the prediction", ref correct)) config.CorrectAim = correct;
        ImGui.TextDisabled(config.CorrectAim
            ? "  the aim carries what the flown arc loses to drag and to real ground"
            : "  the aim is the target; the solver's own answer is flown unmodified");

        if (config.CorrectAim && config.MinArrivalAngleDeg >= 0.5)
        {
            ImGui.TextDisabled("  under a floor the search is still moving when this opens, and it");
            ImGui.TextDisabled("  reads that as drag: 8.52 km against 0.018 km off, headless at 15");
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
            ? "  lights the first engine, then fires each stage as the running one runs dry"
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
