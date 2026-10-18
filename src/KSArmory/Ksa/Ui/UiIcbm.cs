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

        if (computer.Target.IsSet)
        {
            ImGui.SameLine();
            if (ImGui.Button("Clear target")) computer.Designate(AimSite.None);

            // Describe() rounds to three decimals, which is 111 m and right for an overlay label and
            // wrong for the one place an operator might copy a coordinate down. Printed in full here
            // rather than made more precise there.
            ImGui.TextDisabled($"  {computer.Target.LatitudeDeg:F7}, {computer.Target.LongitudeDeg:F7}");
        }

        if (computer.Target.IsSet && parent is not null && computer.Target.BodyName != parent.Id)
        {
            // A ballistic arc is a two-body problem about one planet. Another world is an
            // interplanetary transfer, which is a different manoeuvre, not a longer one.
            ImGui.TextColored(Bad, $"designated on {computer.Target.BodyName}, which is not the body");
            ImGui.TextColored(Bad, "this vehicle is flying around. Only ballistic shots are flown.");
        }

        // Only once there is more than one. With a single target the list repeats the line above,
        // and nothing can add to it before cutoff -- so this appears exactly when it says something.
        if (computer.Targets.Count > 1) DrawIcbmTargetList(computer);

        // A mode, not a button: pressing a button puts the cursor over the panel, so what it reads
        // is whatever lies behind the control rather than the place being pointed at.
        bool picking = config.DesignateByClicking;
        if (ImGui.Checkbox("Designate by clicking the world", ref picking)) config.DesignateByClicking = picking;
        Tip("On: a ring follows the cursor; click the ground to aim there. Shift-click is still the "
            + "lock gesture, and clicks on a window do nothing. Off: enter coordinates below. Once the "
            + $"burn is over a click adds another target instead, up to {TargetSet.MaxTargets} -- "
            + "designating there would start the shot over on a bus that is already coasting. This "
            + "also arms clicking on the ground-track map.");

        ImGui.Checkbox("Show on the ground-track map", ref _config.DrawGroundTrackMarks);
        Tip("Draws the targets and the ground this bus can still divert to on KSA's own "
            + "ground-track map, which F3 opens. The lead target is filled and the number beside "
            + "each is its warhead count. With designating on, the map can be clicked like the "
            + "ground -- though a pixel there is tens of kilometres unzoomed, so the coordinates "
            + "below stay the way to be exact.");

        if (!GroundTrackHook.Installed && GroundTrackHook.Trouble.Length > 0)
            ImGui.TextDisabled($"  not drawing: {GroundTrackHook.Trouble}");

        DrawIcbmReach(computer);

        string resolution = "Typed rather than dragged: a slider spanning half a turn moves about 100 km "
                            + "per pixel.";

        if (parent is not null)
        {
            double lastDigit = 1e-7 * 2.0 * Math.PI * parent.MeanRadius / 360.0;

            resolution = $"The last digit is {lastDigit:F2} m of latitude and "
                         + $"{lastDigit * Math.Cos(_siteLat * Math.PI / 180.0):F2} m of longitude on "
                         + $"{parent.Id}. {resolution}";
        }

        float field = ImGui.GetFontSize() * 7f;

        ImGui.SetNextItemWidth(field);
        ImGui.InputDouble("Lat", ref _siteLat, 0.0, 0.0, "%.7f", ImGuiInputTextFlags.None);
        Tip(resolution);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(field);
        ImGui.InputDouble("Lon", ref _siteLon, 0.0, 0.0, "%.7f", ImGuiInputTextFlags.None);
        Tip(resolution);

        _siteLat = Math.Clamp(_siteLat, -89.9, 89.9);
        _siteLon = Math.Clamp(_siteLon, -180.0, 180.0);

        ImGui.SetNextItemWidth(field * 2f);
        TextField("##sitelabel", ref _siteLabel, "label, optional");

        ImGui.SameLine();
        if (ImGui.Button("Designate") && parent is not null)
        {
            computer.Designate(new AimSite(parent.Id, _siteLat, _siteLon,
                                           string.IsNullOrWhiteSpace(_siteLabel) ? "" : _siteLabel.Trim()));
        }
        Tip("Aims at the latitude and longitude above.");
    }

    // What the bus can still reach, beside the tool that places targets in it rather than under a
    // fold: it is the answer to "why did my click do nothing", which is the one kind of line
    // CLAUDE.md says never to hide.
    private static void DrawIcbmReach(IcbmComputer computer)
    {
        IcbmConfig config = computer.Config;

        bool show = config.ShowDivertReach;
        if (ImGui.Checkbox("Show what the bus can still divert to", ref show)) config.ShowDivertReach = show;
        Tip("On: the ground the bus can put a warhead on is outlined, and a click outside it is "
            + "refused. Before the burn is over that region is the release epoch's alone and costs "
            + "nothing; once the bus is coasting it is flown, seven flights of the impact predictor "
            + "every few seconds, and only while the list can still be edited -- designate mode on, "
            + "or a second target already placed. Off: nothing is flown, nothing is drawn, and a "
            + "click designates rather than adding.");

        if (!show) return;

        ReachDisplay reach = computer.Reach;

        // Silent where nothing has been aimed at yet: until a place is named there is no landing for
        // a reach to be around, and the line would sit on every computer that has never been used.
        if (!reach.HasRegion && computer.Targets.Count == 0) return;

        ImGui.TextColored(reach.HasRegion ? Good : Working, "  " + reach.Say());

        if (reach.HasRegion) ImGui.TextDisabled("  " + reach.SayBudget());
    }

    private static void DrawIcbmTargetList(IcbmComputer computer)
    {
        int aboard = computer.WarheadsAboard;
        int lead = computer.LeadTarget;

        ImGui.Text(computer.DescribeTargets());

        // Inline rather than in a tooltip: a player whose warheads all land on one of several
        // targets has no other way of finding out why, and every refusal here is silent.
        if (computer.Targets.Count > 0)
        {
            ReleaseWalker walker = computer.Walk;

            ImGui.TextColored(walker.Walk.Walks ? Good : Working, "  " + walker.Walk.Say());

            if (walker.Walking)
            {
                ReleaseStep step = walker.Step;

                ImGui.TextColored(Good, $"  on stop {walker.Stop + 1} of {walker.Walk.Stops}: "
                                        + $"target {step.Target + 1}, {step.Away} of "
                                        + $"{step.Warheads} warhead(s) away");
            }
            else if (walker.Curtailed)
            {
                // Inline, because it is the answer to "why did the rest of my warheads land on the
                // wrong target" and a tooltip hides that as surely as a fold does.
                ImGui.TextColored(Working, "  the next hop is more than one trim pass will fly once "
                                           + "the bus's own residual is counted, so the walk ended "
                                           + $"at target {walker.Step.Target + 1} and the warheads "
                                           + "left go there");
            }
        }

        int removed = -1;

        for (int i = 0; i < computer.Targets.Count; i++)
        {
            TargetSet.Entry entry = computer.Targets[i];

            ImGui.PushID(i);

            ImGui.Text($"  {i + 1}  {entry.Site.Describe()}{(i == lead ? "   <- flown to" : "")}");

            if (i == lead)
            {
                Tip("The one the whole flight is aimed at: the arc, the correction and the trim are "
                    + "solved against it, so it has no Remove -- Clear target above starts the shot "
                    + "over, and then a click places a new one. It is whichever target is farthest "
                    + "downrange, not the first clicked, because the bus walks inward from where the "
                    + "booster puts it -- and it stops moving once the arrival is committed.");
            }

            ImGui.SameLine(ImGui.GetFontSize() * 18f, 0f);
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 8f);

            int warheads = entry.Warheads;
            if (ImGui.SliderInt("##warheads", ref warheads, 0, aboard))
            {
                computer.SetTargetWarheads(i, warheads);
            }
            Tip("How many of the bus's warheads are meant for this place. What no target takes rides "
                + "the bus down.");

            if (computer.MayRemoveTarget(i))
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Remove")) removed = i;
            }

            ImGui.PopID();
        }

        if (removed >= 0) computer.RemoveTarget(removed);

        if (ImGui.SmallButton("Split evenly")) computer.BalanceTargets();
        Tip("Spreads every warhead over the targets chosen, the remainder going to the earliest of "
            + "them. A target added by clicking the world starts with none, so that nothing is taken "
            + "off a place already aimed at without being asked.");
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
            if (!computer.Release(_roster.For(computer.Craft)?.Weapon))
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
            Tip("Measured against the engine's own figure for what the whole stack has left, across the "
                + "stages it has not yet flown. Where that cannot be read it falls back to one stage's "
                + "exhaust velocity over the whole vehicle's propellant, which reads a deeply staged "
                + "rocket low, so such a rocket may still make it. Once a burn has run dry, it is the "
                + "velocity that burn left ungained.");
        }

        if (computer.StagesAfterTheRelease is > 0 and int after && !computer.Program.IsBurning
            && computer.Program.Phase != IcbmPhase.Coast)
        {
            ImGui.TextColored(Bad, $"The bus's release is staged before {after} more stage{(after == 1 ? "" : "s")}"
                                       + (computer.Config.MovesAnEarlyRelease
                                              ? ": it will be moved to the end when the burn reaches it"
                                              : ": the burn will stop there and the shot fall short"));
            Tip("The staging list fires the decoupler holding the bus before the stages after it, which would let the "
                + "warheads go with the burn unfinished. Drag it to the end of the staging list to fix it before launch.");
        }

        IcbmProgram program = computer.Program;
        if (program.Phase is IcbmPhase.Idle or IcbmPhase.NoSolution or IcbmPhase.Holding
            && double.IsFinite(program.NeedMetresPerSecond) && double.IsFinite(program.HaveMetresPerSecond))
        {
            ImGui.TextDisabled($"Delta-v: needs {program.NeedMetresPerSecond:F0} m/s, has {program.HaveMetresPerSecond:F0} m/s"
                               + (program.HaveIsTheWholeStack ? "" : " (one stage's reckoning)"));
            Tip(program.HaveIsTheWholeStack
                    ? "Needs: what the solved shot asks of the stack from here. Has: the engine's own figure for the "
                      + "whole stack, across the stages it has not yet flown."
                    : "Needs: what the solved shot asks of the stack from here. Has: the running stage's exhaust velocity "
                      + "over all the propellant, because the engine reports the whole stack only for the craft being "
                      + "flown. That reads a staged rocket low, so a shortfall here may not be one.");
        }

        // Its own line because it is the one unreachable a setting on this panel can fix, and
        // reading it as "no trajectory" sends the operator after a different target instead.
        if (command.Reach == IcbmReach.TooShallow)
        {
            ImGui.TextColored(Bad, $"NO ARC ARRIVES AT {config.MinArrivalAngleDeg:F0} DEG OR STEEPER");
            ImGui.TextDisabled("  lower the steepest-arrival minimum below, or pick a nearer target");
        }

        // Amber rather than red, and up here rather than beside the slider: the shot is still going
        // to arrive, so it is not an unreachable -- but the rocket is not flying what it was told
        // to and nothing else on this panel says so out loud. The two angles are printed beside the
        // control below, and an operator who has to spot that they differ has not been told.
        else if (computer.Program.ArrivalFloorUnaffordable)
        {
            double got = computer.Program.Arc?.ArrivalAngleDeg ?? double.NaN;

            ImGui.TextColored(Working, $"FLYING A SHALLOWER ARRIVAL THAN THE "
                                       + $"{config.MinArrivalAngleDeg:F0} DEG ASKED FOR");
            ImGui.TextDisabled(double.IsFinite(got)
                ? $"  the stack cannot afford it from here, so it is on {got:F0} deg -- it will"
                  + " arrive, less precisely"
                : "  the stack cannot afford it from here -- it will arrive, less precisely");
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
            Tip("On: presses Warp the coast for you every shot, and hands the world back a settling "
                + "margin before the release. Off: the coast runs at whatever speed you set.");

            // Says when the world comes back rather than only that it will. The hand-back is what
            // ends the fast part of the coast, and it is a settling margin ahead of the release.
            double toNormal = computer.SecondsToReleaseApproach;

            if (config.WarpTheCoast && double.IsFinite(toNormal) && toNormal > 0.0)
            {
                ImGui.TextDisabled($"  back to normal speed in {IcbmProgram.Clock(toNormal)}, "
                                   + "a settling margin before the release");
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

        // Named for what it actually is. While guidance holds an arc the prediction departs from the
        // planned cutoff, so mid-ascent it is where the burn lands if it finishes as planned; cut off
        // now, the rocket would come down near the pad.
        string what = computer.WarheadsAway > 0 ? "The bus alone would land"
                    : computer.Program.IsBurning && computer.PredictsFromCutoff ? "When the burn ends as planned"
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
        else if (computer.Program.Phase == IcbmPhase.Holding)
        {
            // Released now a holding bus stays in orbit, which is true and says nothing about the plan.
            ImGui.TextDisabled($"{what}: after the burn in {IcbmProgram.Clock(computer.Command.SecondsToBurn)}");
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

        // The caveat that makes this number readable. KSA reports the engines that are running, so
        // a three-stage rocket on the pad shows the first stage's figure and looks hopelessly short
        // of a shot it can comfortably make.
        Tip("The running stage's engines over the whole vehicle's propellant, so a stack with more "
            + "stages still to fly reads low.");
    }

    private void DrawIcbmTrajectory(IcbmComputer computer)
    {
        IcbmConfig config = computer.Config;

        bool mark = config.MarkTarget;
        if (ImGui.Checkbox("Mark the target and count down to impact", ref mark)) config.MarkTarget = mark;
        Tip("On: a ring on the aim point, and a mark with the countdown that stays on screen wherever "
            + "the target is and points at it from the edge. Off: the target is only visible on this tab.");

        bool draw = config.DrawTrajectory;
        if (ImGui.Checkbox("Draw the predicted trajectory", ref draw)) config.DrawTrajectory = draw;
        Tip("On: draws the arc the vehicle is on. Off: no arc is drawn. The ring on the aim point "
            + "belongs to marking the target, not to this.");

        bool autoRelease = config.AutoRelease;
        if (ImGui.Checkbox("Release warheads automatically", ref autoRelease)) config.AutoRelease = autoRelease;
        Tip("On: one at a time from the coast, once past the release altitude. Off: nothing leaves the "
            + "bus until Release one warhead is pressed.");
        if (!config.AutoRelease) ImGui.TextDisabled("  nothing leaves the bus until the button above is pressed");

        if (computer.Munition is { } warhead)
        {
            ImGui.TextDisabled(warhead.BurstHeightMetres > 0f
                                   ? $"Warheads burst {warhead.BurstHeightMetres:F0} m over the ground"
                                   : "Warheads burst on the ground");
            Tip("Set by the burst height on the Tuning tab, which every warhead of this kind shares.");
        }

        bool autoStage = config.AutoStage;
        if (ImGui.Checkbox("Stage automatically", ref autoStage)) config.AutoStage = autoStage;
        Tip("On: lights the first engine, then fires each stage as the running one runs dry. Off: "
            + "staging is yours, including the one that lights the first engine.");
        if (!config.AutoStage) ImGui.TextDisabled("  staging is yours, including the one that lights the first engine");

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
        Tip("The shallowest the warheads may come in. Steeper is more accurate and costs reach: 15-20 "
            + "deg is where the trade turns, and it overrides Loft where they disagree. At 0 it is off, "
            + "and unless Precision against range asks for an angle the cheapest arc wins, which from "
            + "orbit is a graze at about 7 deg.");

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
        // exists: without it, the arrival is whatever the cheapest arc happens to give.
        double planned = computer.Program.Arc?.ArrivalAngleDeg ?? double.NaN;
        string arriving = double.IsFinite(planned) ? $"; the arc it has arrives at {planned:F0} deg"
                                                   : "; no arc solved yet";

        ImGui.TextDisabled("  " + (config.MinArrivalAngleDeg < 0.5
            ? "off" + arriving
            : $"no shallower than {config.MinArrivalAngleDeg:F0} deg{arriving}"));

        // Beside the floor rather than beside Correct the aim, because the floor is what turns the
        // correction from the thing that closes the miss into the thing that causes it.
        if (config.CorrectAim && config.MinArrivalAngleDeg >= 0.5)
        {
            ImGui.TextDisabled("  under a floor the search is still moving when the aim correction");
            ImGui.TextDisabled("  opens, and it reads that as drag: 8.52 km against 0.018 km off,");
            ImGui.TextDisabled("  headless at 15 -- Correct the aim is under Engineering");
        }

        float preference = (float)config.ArrivalPreference;
        if (ImGui.SliderFloat("Precision against range", ref preference, 0.0f, 1.0f, "%.2f"))
        {
            config.ArrivalPreference = preference;
        }
        Tip("Asks for that fraction of the steepest arrival the tanks can pay for, and never less than "
            + "the Steepest arrival minimum. Latched once, the first time any arc is affordable. At 0 "
            + "it is off: the arrival is whatever the cheapest arc gives, or the minimum above.");

        // Closed, the only fold on the tab, and a developer's alone. What is above it is what a
        // player decides; what is under it has a right answer the shipped defaults already hold. The
        // research switches are not here at all: a shot spec sets them by name.
        if (!Build.Developer) return;

        bool engineering = ImGui.CollapsingHeader("Engineering");
        Tip("The shot, the release and the ascent. The defaults are what ships; changing one here changes "
            + "the shot, and nothing else on this tab will say so. The research switches paired shot nights fly "
            + "as arms are set from the shot spec, not here.");
        if (!engineering) return;

        ImGui.SeparatorText("Shot");

        // A multiplier on the cheapest flight time, shown as one. Printed bare it reads as an
        // absolute setting, and then 1.00 needs a sentence to explain that it is not.
        float loft = (float)config.Loft;
        if (ImGui.SliderFloat("Loft", ref loft, 0.6f, 1.8f, "%.2f x cheapest")) config.Loft = loft;
        Tip("At 1: minimum energy, the cheapest shot there is. Above 1: a longer flight than the "
            + "cheapest -- higher, slower, arrives steeper, costs more. Below 1: a shorter flight than "
            + "the cheapest -- flatter and faster, and costs more. It is not an arrival-angle control: "
            + "from orbit, raising it makes leaving now dearer too, so the burn window can move to a "
            + "cheap flat departure and arrive shallower instead. Steepest arrival asks for an angle, "
            + "and wins where the two disagree.");

        bool correct = config.CorrectAim;
        if (ImGui.Checkbox("Correct the aim from the prediction", ref correct)) config.CorrectAim = correct;
        Tip("On: the aim carries what the flown arc loses to drag and to real ground. Off: the aim is "
            + "the target; the solver's own answer is flown unmodified.");

        bool derive = config.DeriveHoldingCost;
        if (ImGui.Checkbox("Measure the holding cost", ref derive))
        {
            config.DeriveHoldingCost = derive;
        }
        Tip("On: measured off the trajectory each pass, so the floor suits the shot. Off: taken from "
            + "the number below, which is right at one range only.");

        float holding = (float)config.HoldingCostMetresPerSecond;
        if (ImGui.SliderFloat("Holding cost, m/s", ref holding, 0.0f, 40.0f, "%.1f"))
        {
            config.HoldingCostMetresPerSecond = holding;
        }
        Tip("What a second of holding the warheads is charged at. The correction stops once the "
            + $"predicted miss is under {PostBoostAim.FirstCycleSeconds:F0} s of that on its first cycle, "
            + "and that is the floor under the miss"
            + (config.HoldingCostMetresPerSecond > 0.0
                   ? $": {config.HoldingCostMetresPerSecond * PostBoostAim.FirstCycleSeconds:F0} m here. "
                   : ". ")
            + $"At 0 it takes {PostBoostAim.HoldingCostsMetresPerSecond:F0} m/s, measured on one flight: "
            + $"a {PostBoostAim.HoldingCostsMetresPerSecond * PostBoostAim.FirstCycleSeconds:F0} m floor. "
            + "Only used while Measure the holding cost is off, or before it has measured anything.");

        ImGui.SeparatorText("Release");

        float hold = (float)config.ReleaseBeforeArrivalSeconds;
        if (ImGui.SliderFloat("Release at", ref hold, 0f, 900f, "%.0f s before arrival"))
        {
            config.ReleaseBeforeArrivalSeconds = hold;
        }
        Tip("The warheads are held until this long before arrival, so the ejection kick has less "
            + "flight to grow in. At 0 they go as soon as the altitude allows, which is early on the "
            + "way up. A walk between several targets ends its last release here.");

        bool ownGate = config.SingleTargetReleaseBeforeArrivalSeconds >= 0.0;
        if (ImGui.Checkbox("One target releases on its own gate", ref ownGate))
        {
            config.SingleTargetReleaseBeforeArrivalSeconds = ownGate ? 350.0 : -1.0;
        }
        if (ownGate)
        {
            float single = (float)config.SingleTargetReleaseBeforeArrivalSeconds;
            if (ImGui.SliderFloat("One target at", ref single, 0f, 900f, "%.0f s before arrival"))
            {
                config.SingleTargetReleaseBeforeArrivalSeconds = single;
            }
        }
        Tip("On: a shot at one target lets its warheads go at this gate instead. A walk keeps the gate above, "
            + "since a later one shortens every stop's fall and prices its hops past what one pass flies.");

        bool trim = config.TrimBeforeRelease;
        if (ImGui.Checkbox("Trim the bus before releasing", ref trim)) config.TrimBeforeRelease = trim;
        Tip("On: thrusters put it back on the solution after the split, which the burn cannot. Off: the "
            + "warheads leave on whatever the cutoff and the decoupler left the bus doing.");

        float budget = (float)config.TrimBudgetMetresPerSecond;
        if (ImGui.SliderFloat("Trim budget", ref budget, 0f,
                              (float)PostBoostAim.MaxTrimMetresPerSecond,
                              "%.0f m/s for the flight"))
        {
            config.TrimBudgetMetresPerSecond = budget;
        }
        Tip("Spent across every correction, then it stops. At 0 there is no trimming at all, and the "
            + "warheads go on the aim as the burn left it. The bus reserves "
            + $"{PostBoostAim.MaxTrimMetresPerSecond:F0} m/s anyway, so a budget under that is the one "
            + "that binds.");

        bool keepOut = config.KeepOutCoversTheClearance;
        if (ImGui.Checkbox("Keep trimming past the clearance", ref keepOut))
        {
            config.KeepOutCoversTheClearance = keepOut;
        }
        Tip("On: a clearance that runs out of time stops waiting rather than giving up, and the "
            + "keep-out withholds the directions that point at the stack. Off: a clearance that runs "
            + "out of time abandons the trim, and the warheads go on the aim as the burn left it.");

        ImGui.SeparatorText("Ascent");

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
        Tip($"The stack is held within {config.MaxAngleOfAttackDeg:F0} deg of the airflow while loaded.");

        float handover = (float)config.HandoverPressurePa;
        if (ImGui.SliderFloat("Guidance takes over below (Pa)", ref handover, 50f, 20_000f, "%.0f"))
        {
            config.HandoverPressurePa = handover;
        }
        Tip($"Dynamic pressure, so {config.HandoverPressurePa:F0} Pa means the same thing on a body "
            + "with no air.");

        float deploy = (float)config.DeployAltitudeMetres;
        if (ImGui.SliderFloat("Release warheads above (m)", ref deploy, 1_000f, 400_000f, "%.0f"))
        {
            config.DeployAltitudeMetres = deploy;
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
