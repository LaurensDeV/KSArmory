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
    /// <summary>True once the vehicle has been destroyed or unloaded.</summary>
    public static bool IsAlive(Vehicle? v) => v is { IsDisposed: false };

    /// <summary>
    /// Whether a craft is wreckage, as KSA marks the pieces <c>PartFailure.ShedDebris</c> breaks off.
    /// The craft they came off is not marked, so it stays a target until it is destroyed.
    /// </summary>
    public static bool IsDebris(Vehicle v)
    {
        try
        {
            return IsAlive(v) && v.IsDebris;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// A craft's drag as <c>PhysicsStates.ComputeDrag</c> computes it: the body-fixed
    /// <c>AerodynamicCdABody</c> plus a tenth of the surface area, over its mass, in the air of the body
    /// it flies over — with its attitude and turn. Null when any of it cannot be read, which includes a
    /// body with no air.
    /// </summary>
    public static DragShape? DragShapeOf(Vehicle v)
    {
        try
        {
            if (!IsAlive(v) || ParentBody(v)?.GetAtmosphereReference()?.Physical is null) return null;

            ref readonly VehicleProperties props = ref v.Props;
            float3 positive = props.AerodynamicCdABody.Positive;
            float3 negative = props.AerodynamicCdABody.Negative;
            var shape = new DragShape(new double3(positive.X, positive.Y, positive.Z),
                                      new double3(negative.X, negative.Y, negative.Z),
                                      0.1 * props.TotalSurfaceArea, v.Body2Cce, v.BodyRates,
                                      props.TotalMass, Medium.ReferenceDensityKgPerM3,
                                      v.FlightComputer.ActiveEnginePerformanceMax.ExhaustVelocity,
                                      props.TotalPropellantMass);
            return shape.IsUsable ? shape : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the engine is propagating this vehicle analytically rather than integrating it.
    ///
    /// <para>The distinction is the whole accuracy question for a coast. On rails the state is an
    /// exact Kepler conic evaluated at the frame's time and the step size cannot matter; off rails
    /// it is velocity-Verlet, whose truncation grows with the step — and the step is scaled by an
    /// engine speed governor the nominal warp figure does not show.</para>
    ///
    /// <para>Unknown rather than false when it cannot be read: a vehicle the engine will not answer
    /// for says nothing about which way it is being flown, and reading that as "integrated" would
    /// invent the very transition this is here to detect.</para>
    /// </summary>
    public static bool? OnRails(Vehicle? v)
    {
        if (!IsAlive(v)) return null;

        try { return v!.Situation.IsOnRails(); }
        catch { return null; }
    }

    /// <summary>
    /// Which of the engine's actuator tests is holding this vehicle off rails, as a short label —
    /// or null when neither is, which narrows the cause to the four this cannot see.
    ///
    /// <para><b>Naming the term is far cheaper than eliminating the terms one arm at a time.</b>
    /// A bus that verifiably stopped commanding its attitude hold still coasted 72% off rails with
    /// 88% of the coast quiet, against a control at 72% — `docs/ACCURACY-PLAN.md` 3bf.</para>
    ///
    /// <para><c>PhysicsBubble</c> takes a vehicle off rails on
    /// <c>anyActuatorCommanded || AnyActuatorActive() || ocean || animating || KittenWantsWake</c>,
    /// then on a freefall needing full physics, then on the origin's precision. The two actuator
    /// terms are the ones a mod can read, and they are the ones under suspicion; the others are
    /// ruled out by inspection for a coasting bus — it is not in an ocean, not a kitten, and the
    /// precision test needs a bubble origin past 2.2e12 m.</para>
    ///
    /// <para><b>The reading is one frame stale and says so.</b> The state carries the worker run
    /// staged for this frame, so a flag set during the run this frame dispatches is not visible
    /// until the next — which is a phase error of one frame on a term that persists for minutes,
    /// and irrelevant at that scale.</para>
    /// </summary>
    public static string? OffRailsActuator(Vehicle? v)
    {
        if (!IsAlive(v) || !CanQueuePartFailures) return null;

        try
        {
            if (_updateStateField!.GetValue(v!) is not VehicleUpdateState state) return null;

            bool commanded = state.FlightComputerOutput.AnyActuatorCommanded;
            bool active = state.AnyActuatorActive();

            return (commanded, active) switch
            {
                (true, true) => "commanded+active",
                (true, false) => "commanded",
                (false, true) => "active",
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// How long the engine's own flight plan for this vehicle is still verified for.
    ///
    /// <para>The plan's expiry losing its race with sim time is one of the seven things that puts a
    /// vehicle off rails, and the only one this mod can read without reflection — the actuator
    /// flags hang off <c>Vehicle._threadWorkerUpdateState</c>, which is private. A margin that
    /// collapses to nothing at the same probe the rails flag flips is the discriminator.</para>
    /// </summary>
    // Past a day the plan carries EndOfTime rather than a horizon, and that is not a margin.
    private const double FlightPlanHorizonSeconds = 86_400.0;

    public static double FlightPlanMarginSeconds(Vehicle? v)
    {
        if (!IsAlive(v)) return double.NaN;

        try
        {
            double margin = (v!.FlightPlan.ExpiryGameTime - Universe.GetElapsedTime()).Seconds();

            // RecalculateFlightPlan resets the horizon to EndOfTime, which comes back as ~1.7e29
            // seconds and is not a margin. Reported as infinity so a reader sees "verified for as
            // long as anyone cares" rather than a number that looks like data.
            return margin > FlightPlanHorizonSeconds ? double.PositiveInfinity : margin;
        }
        catch { return double.NaN; }
    }

    /// <summary>
    /// How many vehicles share this one's physics bubble.
    ///
    /// <para>World load is what separated the two divergent worlds from the eight healthy ones —
    /// see <c>docs/ACCURACY-PLAN.md</c> 3ar. Read per craft rather than counted here because the
    /// bubble is what the engine actually budgets against.</para>
    /// </summary>
    public static int BubbleVehicleCount(Vehicle? v)
    {
        if (!IsAlive(v)) return -1;

        try { return v!.BubbleVehicleCount; }
        catch { return -1; }
    }

    /// <summary>
    /// The bubble's reference frame, and how high its origin sits — <c>Cci</c>, <c>Ccf</c>, or null
    /// when there is no bubble to ask.
    ///
    /// <para><b>This is the discriminator for staying off rails, and it is one boolean.</b>
    /// `PhysicsStates.TryToPutOnRails` returns a coasting vehicle to rails only when the bubble
    /// origin is <c>Cci</c>; in a <c>Ccf</c> bubble there is no path back at all, and a member is
    /// integrated, with the rotating-frame terms, for as long as the bubble holds it.</para>
    ///
    /// <para>The frame is the <em>bubble's</em>, taken from its heaviest member, so a spent stage
    /// left below the near-surface radius holds the whole bubble in <c>Ccf</c> while the bus coasts
    /// far above it. That is why releasing the attitude bought nothing: the actuator is one
    /// condition of going off rails, and the frame is what refuses to undo it.</para>
    /// </summary>
    public static (string Frame, double OriginAltitudeMetres)? BubbleFrameOf(Vehicle? v)
    {
        if (!IsAlive(v)) return null;

        try
        {
            if (!v!.HasPhysicsBubble) return null;

            ref readonly BubbleOrigin origin = ref v.BubbleOrigin;
            double altitude = origin.Parent is Celestial body
                                  ? Vec.Len(origin.PositionBub) - body.MeanRadius
                                  : double.NaN;

            return (origin.BubFrame.IsCcf() ? "Ccf" : "Cci", altitude);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// What the bubble's heaviest member is called, which is what chooses the frame above.
    ///
    /// <para>The altitude alone says a bubble has taken the rotating frame from something on the
    /// ground and cannot say from <em>what</em>, which leaves a merge like 3cn's unattributable.</para>
    /// </summary>
    public static string BubbleLeaderName(Vehicle? v)
    {
        if (!IsAlive(v)) return "?";

        try
        {
            return v!.BubbleLeader is { } leader && IsAlive(leader) ? DisplayName(leader) : "?";
        }
        catch
        {
            return "?";
        }
    }

    /// <summary>How high the bubble's heaviest member is, which is what chooses the frame above.</summary>
    public static double BubbleLeaderAltitudeMetres(Vehicle? v)
    {
        if (!IsAlive(v)) return double.NaN;

        try
        {
            if (v!.BubbleLeader is not { } leader || !IsAlive(leader)) return double.NaN;
            if (leader.Orbit?.Parent is not Celestial body) return double.NaN;

            return Vec.Len(PositionEcl(leader) - body.GetPositionEcl()) - body.MeanRadius;
        }
        catch
        {
            return double.NaN;
        }
    }

    /// <summary>
    /// How far the flight computer says this craft is from the attitude it was asked for, in
    /// degrees, or NaN when it cannot be read.
    ///
    /// <para>The engine's own error rather than one derived here: it is what the tracker acts on,
    /// so it is the number that decides whether an actuator gets commanded — and a command is what
    /// takes a vehicle off rails.</para>
    /// </summary>
    public static double PointingErrorDeg(Vehicle? v)
    {
        if (!IsAlive(v)) return double.NaN;

        try
        {
            float3 e = v!.FlightComputer.ErrorAngles;
            double3 rad = new double3(e.X, e.Y, e.Z);
            return Vec.IsFinite(rad) ? Vec.Len(rad) * 180.0 / Math.PI : double.NaN;
        }
        catch { return double.NaN; }
    }

    /// <summary>
    /// The nearest other vehicle to <paramref name="v"/>, and how far away it is.
    ///
    /// <para>A bubble merges on proximity and only ever releases a vehicle whose parent body or
    /// frame changes, so what keeps one alive is whatever stays close — see
    /// <c>docs/ACCURACY-PLAN.md</c> 3au. The count alone cannot say what that is, and a lone rocket
    /// shares a bubble with its own spent stage, so it is not simply other rockets.</para>
    ///
    /// <para>Walks the shared census rather than taking its own, and is called once per coast probe
    /// rather than per frame.</para>
    /// </summary>
    public static Vehicle? NearestVehicle(Vehicle? v, out double metres)
    {
        metres = double.NaN;
        if (!IsAlive(v)) return null;

        Vehicle? nearest = null;
        double best = double.MaxValue;
        double3 here = PositionEcl(v!);

        foreach (Vehicle other in Vehicles)
        {
            if (ReferenceEquals(other, v) || !IsAlive(other)) continue;

            double3 between = PositionEcl(other) - here;
            if (!Vec.IsFinite(between)) continue;

            double apart = Vec.Len(between);
            if (apart >= best) continue;

            best = apart;
            nearest = other;
        }

        if (nearest is not null) metres = best;
        return nearest;
    }

    /// <summary>
    /// Whether the engine has been told to integrate <em>every</em> vehicle in the world.
    ///
    /// <para>A public static the game writes from one debug checkbox and reads every sub-step. It
    /// is the only switch in the engine that changes how all vehicles are propagated at one
    /// instant, so it is worth knowing rather than assuming.</para>
    /// </summary>
    public static bool ForcedOffRails
    {
        get
        {
            try { return PhysicsBubble._forceOffRails; }
            catch { return false; }
        }
    }

    /// <summary>
    /// The instant the engine's own state for this vehicle belongs to, in seconds.
    ///
    /// <para>Compared against <see cref="SimClockSeconds"/> it says whether the state being read is
    /// the frame's or an older one. A world-level disturbance that moved every prediction at once
    /// would show here as the two clocks parting, and nothing else this mod reads would notice.</para>
    /// </summary>
    public static double StateEpochSeconds(Vehicle? v)
    {
        if (!IsAlive(v)) return double.NaN;

        try { return v!.Orbit.StateVectors.StateTime.Seconds(); }
        catch { return double.NaN; }
    }

    /// <summary>
    /// Every live vehicle in the world, built at most once a frame and shared by everything that
    /// walks it.
    ///
    /// <para><b>This is the mod's most repeated piece of work.</b> Each weapons system's radar
    /// scan walks the whole system, and so do its contact candidates, so walked per system a world
    /// with four armed craft walks it eight times a frame to reach the same answer. Nothing about
    /// that answer is per system: it is what exists.</para>
    ///
    /// <para><b>Freshness is a generation, not a frame count.</b> The list may not outlive a
    /// change to the world, because a destroyed vehicle stays in it as a disposed reference where
    /// a fresh walk would simply omit it. So anything that removes a vehicle invalidates it, and
    /// each hook that can start a pass invalidates it once on the way in. Holding it across a
    /// frame is what <see cref="CollectVehicles"/>'s own warning is about, and this does not.</para>
    /// </summary>
    public static IReadOnlyList<Vehicle> Vehicles
    {
        get
        {
            if (_censusFresh) return _census;

            CollectVehicles(_census);
            _censusFresh = true;
            return _census;
        }
    }

    /// <summary>
    /// Throws the shared census away. Called at the top of each pass, and by anything that takes a
    /// vehicle out of the world.
    /// </summary>
    public static void InvalidateCensus() => _censusFresh = false;

    /// <summary>What this mod's warheads have broken up this session, so no set takes the pieces for targets.</summary>
    public static Wreckage Wreckage { get; } = new();

    /// <summary>
    /// Which craft the panel's flag has put on which side, rebuilt once a frame by
    /// <c>KSArmoryMod.CollectTeams</c>. Named for its type rather than <c>Teams</c>,
    /// which is the helper that resolves a craft <em>name</em> to a side and is the fallback
    /// underneath this.
    ///
    /// <para>Here rather than threaded through <c>Scan</c> for the reason <see cref="Wreckage"/>
    /// is: every sensor in the world needs the same answer and none of them owns it. A
    /// <see cref="VehicleContact"/> is built from a bare craft inside the scan, so the alternative
    /// is a parameter on four update signatures to reach the one line that reads it.</para>
    /// </summary>
    public static TeamRoster TeamRoster { get; } = new();

    private static readonly List<Vehicle> _census = [];

    private static bool _censusFresh;

    /// <summary>
    /// Appends every vehicle the game currently has loaded into <paramref name="into"/>.
    ///
    /// Reads <c>Universe.CurrentSystem.All</c> rather than <c>Program.VehiclesInFrame</c>.
    /// The latter is a per-frame scratch buffer refilled by <c>RefreshVehiclesInFrame()</c>
    /// at a point in the tick that does not line up with a Harmony postfix on OnFrame - it
    /// reads back empty from there, which silently blinds the radar. The system's collection
    /// is the authoritative list and is valid from any hook.
    ///
    /// Copies immediately: the result must not be held across frames, and engine state must not
    /// be under iteration while vehicles are being destroyed.
    /// </summary>
    public static void CollectVehicles(List<Vehicle> into)
    {
        into.Clear();

        try
        {
            if (Universe.CurrentSystem is { } system)
            {
                ReadOnlySpan<Astronomical> all = system.All.AsSpan();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] is Vehicle { IsDisposed: false } v) into.Add(v);
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn($"vehicle enumeration failed: {e.Message}");
        }

        // Belt and braces: if the system collection ever comes back empty, fall back to the
        // per-frame buffer rather than going blind.
        if (into.Count != 0) return;

        try
        {
            ReadOnlySpan<Vehicle> inFrame = Program.VehiclesInFrame;
            for (int i = 0; i < inFrame.Length; i++)
            {
                if (inFrame[i] is { IsDisposed: false } v) into.Add(v);
            }
        }
        catch
        {
            // Nothing more to try.
        }
    }

    public static double3 PositionEcl(Vehicle v) => v.GetPositionEcl();

    /// <summary>
    /// Where a body is, guarded — the anchor a round falls back on once the craft that fired it is
    /// gone. Non-finite rather than throwing, so a caller can decline to move anything this frame.
    /// </summary>
    public static double3 PositionEcl(Celestial body)
    {
        try
        {
            return body.GetPositionEcl();
        }
        catch
        {
            return new double3(double.NaN, double.NaN, double.NaN);
        }
    }

    public static double3 VelocityEcl(Vehicle v) => v.GetVelocityEcl();

    /// <summary>A craft's whole mass as KSA flies it (kg), or NaN.</summary>
    public static double MassKg(Vehicle v)
    {
        try { return v.Props.TotalMass; }
        catch { return double.NaN; }
    }

    /// <summary>
    /// How fast a craft is turning, in Ecl, rad/s.
    ///
    /// <para><c>Body2Ego</c>, <c>Asmb2Cce</c> and <c>Asmb2Ego</c> all return the same quaternion,
    /// so KSA's body frame and its assembly frame are one and the same and <c>BodyRates</c> needs
    /// no conversion beyond the craft's attitude.</para>
    /// </summary>
    public static double3 AngularVelocityEcl(Vehicle v)
    {
        try
        {
            double3 rates = v.Asmb2Ego * v.BodyRates;
            return Vec.IsFinite(rates) ? rates : Vec.Zero;
        }
        catch
        {
            return Vec.Zero;      // a craft that will not report its spin simply is not spinning
        }
    }

    /// <summary>
    /// The point a craft turns about, in Ecl — its centre of mass, not its assembly origin.
    ///
    /// <para>The two differ by metres on a real stack, and a lever arm measured from the origin
    /// gives a released store the wrong tangential velocity by exactly that offset times the spin
    /// rate.</para>
    /// </summary>
    public static double3 CentreOfMassEcl(Vehicle v)
    {
        try
        {
            double3 com = v.GetPositionEcl() + v.Asmb2Ego * v.CenterOfMassAsmb;
            return Vec.IsFinite(com) ? com : v.GetPositionEcl();
        }
        catch
        {
            return v.GetPositionEcl();
        }
    }

    /// <summary>Rough size of a vehicle, used to scale hit and blast checks.</summary>
    public static double MeanRadius(Vehicle v)
    {
        double r = v.MeanRadius;
        return double.IsFinite(r) && r > 0.0 ? r : 5.0;
    }

    public static string DisplayName(Vehicle v)
    {
        try { return string.IsNullOrEmpty(v.Id) ? "unnamed" : v.Id; }
        catch { return "unnamed"; }
    }

    /// <summary>
    /// Local "up" at the platform: the radial-out direction from whatever body it is bound to.
    /// This is the natural boresight for a defence site. Falls back to the platform's velocity
    /// direction, and finally to +Z, so the caller always gets a usable unit vector.
    /// </summary>
    public static double3 LocalUp(Vehicle platform)
    {
        try
        {
            if (platform.Parent is IPosition parent)
            {
                double3 radial = platform.GetPositionEcl() - parent.GetPositionEcl();
                double3 up = Vec.Unit(radial);
                if (!up.Equals(Vec.Zero)) return up;
            }
        }
        catch
        {
            // Parent can be null or mid-transition during scene changes; fall through.
        }

        double3 alongTrack = Vec.Unit(platform.GetVelocityEcl());
        return alongTrack.Equals(Vec.Zero) ? new double3(0, 0, 1) : alongTrack;
    }

    /// <summary>
    /// The body a craft is at, or null in deep space. What the terrain map is drawn against: its
    /// centre, its rotation axis and its height field all come from here.
    /// </summary>
    public static Celestial? ParentBody(Vehicle platform)
    {
        try
        {
            return platform.Parent as Celestial;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a craft is resting on something — ground, water or another craft — and so moves with the
    /// surface under it rather than on its own.
    /// </summary>
    public static bool RestsOnSurface(Vehicle vehicle)
    {
        try
        {
            return vehicle.Situation.HasAnyContact();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a craft has anything for a store to ride on.
    ///
    /// <para>The cheap form of <see cref="WeaponInventory.HasPlatform"/>, for the callers that walk
    /// every craft every frame and would otherwise survey each one to ask a question the first part
    /// usually answers. Same rule and same reason — see <see cref="PartRides"/>.</para>
    /// </summary>
    public static bool HasPlatform(Vehicle? vehicle)
    {
        if (!IsAlive(vehicle)) return false;

        try
        {
            ReadOnlySpan<Part> parts = vehicle!.Parts.Parts;

            // An unreadable or empty tree is a platform: a craft the mod stops recognising is a
            // worse answer than one it recognises for a frame while the tree is rebuilt.
            if (parts.Length == 0) return true;

            for (int i = 0; i < parts.Length; i++)
            {
                if (!PartRides(parts[i])) return true;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Whether a part attaches to the side of something rather than stacking on it, and so cannot
    /// be a craft on its own.
    ///
    /// <para>Asked of the game rather than recorded here. <c>VehicleEditor.IsAllowedAsRootPart</c>
    /// rejects a part if any of its connectors carries <c>ToSurface</c> or <c>FromSurface</c>, and
    /// that is the same question: a rail, a rack and a targeting pod are stores, and a craft rooted
    /// on one is a craft the editor would never have let anybody build. It happens anyway, because
    /// isolating a failed part splits the vehicle wherever it can.</para>
    ///
    /// <para>A part with no connectors at all rides nothing and is treated as a platform, which is
    /// the reading that cannot make a working craft disappear.</para>
    /// </summary>
    public static bool PartRides(Part part)
    {
        try
        {
            List<Part.Connector.TemplateBase> connectors = part.Template.Connectors;

            for (int i = 0; i < connectors.Count; i++)
            {
                Part.Connector.Flag flags = connectors[i].Flags;

                if ((flags & (Part.Connector.Flag.ToSurface | Part.Connector.Flag.FromSurface)) != 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            // Unreadable is a platform, for the same reason as no connectors: the cost of guessing
            // wrong the other way is a craft the mod stops recognising.
            return false;
        }
    }

    /// <summary>
    /// Flattens a craft's part tree into what <see cref="WeaponSurvey"/> takes.
    ///
    /// <para>Position and orientation are read in the vehicle's assembly frame, which is the
    /// frame the parts were placed in, so what comes back is where the player put them. Only the
    /// top-level parts: a subpart is a piece of one part's own articulation, not a component
    /// somebody bolted on.</para>
    /// </summary>
    public static void SurveyParts(Vehicle? vehicle, List<SurveyedPart> into)
    {
        into.Clear();
        if (!IsAlive(vehicle)) return;

        try
        {
            ReadOnlySpan<Part> parts = vehicle!.Parts.Parts;
            for (int i = 0; i < parts.Length; i++)
            {
                Part part = parts[i];
                into.Add(new SurveyedPart(part.Id, part.PositionVehicleAsmb,
                                          part.Asmb2VehicleAsmb, PartRides(part)));
            }
        }
        catch
        {
            // A tree being rebuilt underneath the read, during staging or docking. Next frame sees
            // the finished one; reporting a half-built craft would be worse than reporting none.
            into.Clear();
        }
    }
}
