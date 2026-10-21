using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using KSA.Rendering.Water.Data;

namespace KSArmory;

internal static partial class KsaWorld
{
    /// <summary>Whether something is running and every running engine is a solid; false if unreadable.</summary>
    public static bool OnlySolidsRunning(Vehicle craft)
    {
        try
        {
            bool any = false;
            Span<EngineController> engines = craft.Parts.Modules.Get<EngineController>();
            for (int i = 0; i < engines.Length; i++)
            {
                if (!engines[i].IsActive) continue;
                foreach (RocketCore core in engines[i].Cores)
                {
                    if (core is not SolidMotor) return false;
                    any = true;
                }
            }
            return any;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the engines running now stop when told to. False while any active engine burns a
    /// solid grain: KSA's <c>SolidMotor</c> keeps burning once lit whatever it is commanded, and only
    /// a spent grain stops it. True when it cannot be read, which is every engine that throttles.
    /// </summary>
    public static bool RunningEnginesCanStop(Vehicle craft)
    {
        try
        {
            Span<EngineController> engines = craft.Parts.Modules.Get<EngineController>();
            for (int i = 0; i < engines.Length; i++)
            {
                if (!engines[i].IsActive) continue;
                foreach (RocketCore core in engines[i].Cores)
                {
                    if (core is SolidMotor) return false;
                }
            }
            return true;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// How fast the craft's own jets can turn it, as its flight computer reckons it: the weaker of pitch and yaw's angular
    /// acceleration, and the profile's rate limit, both in radians.
    /// </summary>
    public static bool TryRcsAuthority(Vehicle craft, out double accelRadPerS2, out double rateLimitRadPerS)
    {
        accelRadPerS2 = double.NaN;
        rateLimitRadPerS = double.NaN;

        try
        {
            FlightComputer computer = craft.FlightComputer;
            accelRadPerS2 = Math.Min(computer.RcsTorqueAuthority.Y, computer.RcsTorqueAuthority.Z);
            rateLimitRadPerS = computer.RateLimit;
            return accelRadPerS2 > 0.0 && double.IsFinite(accelRadPerS2);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The craft's control frame, as three axes in the parent body's inertial frame: the nose, the
    /// starboard beam and the belly.
    ///
    /// <para>The control frame rather than the assembly frame, because it is what KSA maps its
    /// thrusters against — <c>ThrusterController</c> flags a nozzle
    /// <c>TranslateForward</c>/<c>Right</c>/<c>Down</c> by which of these three its thrust points
    /// along. So a translation command resolved in any other frame fires the wrong jets, and on a
    /// craft whose command pod is mounted square to the stack it does it without looking wrong.
    /// </para>
    /// </summary>
    public static bool TryControlFrameCci(Vehicle craft, Celestial parent,
                                          out double3 noseCci, out double3 rightCci, out double3 downCci)
    {
        noseCci = default;
        rightCci = default;
        downCci = default;

        try
        {
            doubleQuat helm2Body = craft.Helm2Body;
            doubleQuat body2Cce = craft.Body2Cce;
            doubleQuat cce2Cci = parent.GetCce2Cci();

            noseCci = Axis(double3.UnitX);
            rightCci = Axis(double3.UnitY);
            downCci = Axis(double3.UnitZ);

            return Vec.IsFinite(noseCci) && Vec.IsFinite(rightCci) && Vec.IsFinite(downCci)
                   && !noseCci.Equals(Vec.Zero) && !rightCci.Equals(Vec.Zero) && !downCci.Equals(Vec.Zero);

            double3 Axis(double3 ctrl)
                => Vec.Unit(ctrl.Transform(helm2Body).Transform(body2Cce).Transform(cce2Cci));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The vacuum exhaust velocity KSA's rocket model gives the craft's engines, or its attitude
    /// thrusters, at full throttle: summed thrust over summed mass flow. NaN if unreadable.
    /// </summary>
    public static double DeclaredVacuumExhaust(Vehicle craft, bool thrusters)
    {
        try
        {
            double thrust = 0.0, flow = 0.0;
            if (thrusters)
            {
                Span<ThrusterController> jets = craft.Parts.Modules.Get<ThrusterController>();
                for (int i = 0; i < jets.Length; i++)
                {
                    RocketControllerData d = RocketControllerData.ComputeFromCores(jets[i].Cores.AsSpan(), 0f, 1f);
                    thrust += Vec.Len(new double3(d.ThrustMax.X, d.ThrustMax.Y, d.ThrustMax.Z));
                    flow += d.MassFlowRateMax;
                }
            }
            else
            {
                Span<EngineController> engines = craft.Parts.Modules.Get<EngineController>();
                for (int i = 0; i < engines.Length; i++)
                {
                    RocketControllerData d = RocketControllerData.ComputeFromCores(engines[i].Cores.AsSpan(), 0f, 1f);
                    thrust += Vec.Len(new double3(d.ThrustMax.X, d.ThrustMax.Y, d.ThrustMax.Z));
                    flow += d.MassFlowRateMax;
                }
            }
            return flow > 0.0 ? thrust / flow : double.NaN;
        }
        catch
        {
            return double.NaN;
        }
    }

    private static FieldInfo? _manualInputs;

    private static FieldInfo? _engineThrottle;

    /// <summary>
    /// Move a craft's throttle by <paramref name="delta"/> outright, clamped as the engine clamps it: what holding the
    /// throttle key would do, for the frames KSA clears held keys. False where the private field cannot be reached.
    /// </summary>
    public static bool TryNudgeThrottle(Vehicle craft, double delta)
    {
        try
        {
            _manualInputs ??= typeof(Vehicle).GetField("_manualControlInputs", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_manualInputs?.GetValue(craft) is not { } inputs) return false;
            _engineThrottle ??= inputs.GetType().GetField("EngineThrottle", BindingFlags.Public | BindingFlags.Instance);
            if (_engineThrottle is null) return false;

            float have = (float)_engineThrottle.GetValue(inputs)!;
            _engineThrottle.SetValue(inputs, Math.Clamp(have + (float)delta, craft.GetMinThrottle(), 1f));

            // ManualControlInputs is a struct, so what was read is a boxed copy and has to go back.
            _manualInputs.SetValue(craft, inputs);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static FieldInfo? _workerState;

    private static FieldInfo? _workerInputs;

    private static FieldInfo? _thrusterFlags;

    /// <summary>
    /// Put translation jets back on a craft after KSA has cleared its held keys: on the vehicle's own inputs and on
    /// the copy its worker reads, both structs held by value. False where a private field cannot be reached.
    /// </summary>
    public static bool TryRestoreJets(Vehicle craft, ThrusterMapFlags jets)
    {
        try
        {
            _manualInputs ??= typeof(Vehicle).GetField("_manualControlInputs", BindingFlags.NonPublic | BindingFlags.Instance);
            _workerState ??= typeof(Vehicle).GetField("_threadWorkerUpdateState", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_manualInputs is null || _workerState?.GetValue(craft) is not { } state) return false;

            _workerInputs ??= state.GetType().GetField("ManualControlInputs", BindingFlags.Public | BindingFlags.Instance);
            _thrusterFlags ??= typeof(ManualControlInputs).GetField("ThrusterCommandFlags", BindingFlags.Public | BindingFlags.Instance);
            if (_workerInputs is null || _thrusterFlags is null) return false;

            object own = _manualInputs.GetValue(craft)!;
            _thrusterFlags.SetValue(own, (ThrusterMapFlags)_thrusterFlags.GetValue(own)! | jets);
            _manualInputs.SetValue(craft, own);

            object worker = _workerInputs.GetValue(state)!;
            _thrusterFlags.SetValue(worker, (ThrusterMapFlags)_thrusterFlags.GetValue(worker)! | jets);
            _workerInputs.SetValue(state, worker);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Claim the keyboard for the UI on the next frame, as a modal or a focused text field does.</summary>
    public static void HoldTheKeyboard() => Brutal.ImGuiApi.ImGui.SetNextFrameWantCaptureKeyboard(true);

    /// <summary>
    /// Whether KSA is throwing away the held controls on this craft this frame, and which of its reasons
    /// applies.
    ///
    /// <para><b>The throttle and the trim this mod drives are held controls.</b>
    /// <c>Vehicle.PrepareWorker</c> clears them on the controlled vehicle, before reading them, whenever the
    /// UI holds the keyboard, that vehicle is marked inactive, or the world runs past 30x — so the craft
    /// being flown keeps whatever throttle it had while any modal or text field has focus. KSA opens such a
    /// modal by itself at every launch once a build newer than the install is published.</para>
    /// </summary>
    public static bool DiscardsHeldControls(Vehicle craft, out string why)
    {
        why = "";
        if (!ReferenceEquals(Program.ControlledVehicle, craft)) return false;

        try
        {
            if (Brutal.ImGuiApi.ImGui.GetIO().WantCaptureKeyboard)
            {
                why = KSA.Popup.AnyOpen ? "a KSA popup holds the keyboard" : "the UI holds the keyboard";
            }
            else if (!Program.IsControlledVehicleActive)
            {
                why = "the controlled vehicle is marked inactive";
            }
            else if (SimulationSpeed > 30.0)
            {
                why = "the world runs past 30x";
            }
        }
        catch
        {
            return false;
        }

        return why.Length > 0;
    }

    /// <summary>
    /// Closes every popup KSA has open, for a scenario nobody is there to click through.
    ///
    /// <para><b>A modal takes the keyboard, and with it the controlled vehicle's throttle</b> —
    /// <see cref="DiscardsHeldControls"/>. A rocket under one keeps full throttle through a staging that
    /// needs it lowered, past its airframe's limit.</para>
    ///
    /// <para>Closed through the popup's own public <c>Active</c> flag, the one its buttons clear, on the
    /// engine's private list. If that list moves this closes nothing and warns once.</para>
    /// </summary>
    /// <returns>The type names of what was closed, empty when nothing was open.</returns>
    public static IReadOnlyList<string> CloseEnginePopups()
    {
        if (!KSA.Popup.AnyOpen) return [];
        if (EnginePopups() is not { } popups) return [];

        List<string> closed = [];
        foreach (KSA.Popup popup in popups)
        {
            if (!popup.Active) continue;
            popup.Active = false;
            closed.Add(popup.GetType().Name);
        }

        return closed;
    }

    private static FieldInfo? _popupListField;

    private static bool _lookedForPopupList;

    private static List<KSA.Popup>? EnginePopups()
    {
        if (!_lookedForPopupList)
        {
            _lookedForPopupList = true;
            _popupListField = typeof(KSA.Popup).GetField("Popups", BindingFlags.NonPublic | BindingFlags.Static);

            if (_popupListField?.FieldType != typeof(List<KSA.Popup>))
            {
                Log.Warn("Popup.Popups has moved - a scenario cannot close KSA's popups, and one left open "
                         + "holds the throttle of the craft being flown");
                _popupListField = null;
            }
        }

        try
        {
            return _popupListField?.GetValue(null) as List<KSA.Popup>;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Give a craft a control part if it has none and one of ours can serve.
    ///
    /// <para><b>A craft with no control part steers against the wrong axes.</b> KSA builds the
    /// thruster control map in the control part's frame — <c>Helm2Body</c> is
    /// <c>ControlConnector ?? ControlPart ?? Identity</c> — so with none it resolves every attitude
    /// command against the raw assembly axes. A correction then comes out about a different axis
    /// from the one that was wrong, which grows the error rather than cancelling it: measured on
    /// the pad as an attitude error running 0.01 deg to 85 deg with the tracker firing throughout.</para>
    ///
    /// <para>Nothing elects one on its own. <c>CelestialSystem</c> restores whatever the save
    /// recorded and there is no fallback, so a stack assembled without a command pod flies with
    /// <c>Identity</c> for ever. This fills that hole and never takes a choice: it runs only when
    /// <c>ControlPart</c> is already null, and KSA refuses any part not declaring
    /// <c>&lt;Control /&gt;</c>, so a rail or a sight cannot become one through here.</para>
    /// </summary>
    public static void EnsureControlPart(Vehicle craft, Part candidate)
    {
        try
        {
            if (craft.ControlPart is not null) return;

            craft.SetControlPart(candidate);

            if (craft.ControlPart is not null)
            {
                Log.Info($"{DisplayName(craft)} had no control part; "
                         + $"controlling from {candidate.Id}");
            }
        }
        catch (Exception e)
        {
            Log.Error("could not elect a control part", e);
        }
    }
}
