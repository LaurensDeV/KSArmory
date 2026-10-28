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
    /// <summary>
    /// Blocks until KSA's vehicle solver jobs have finished the step they are working on.
    ///
    /// <para>Required before destroying a vehicle from a mod hook: the dispose mutates the
    /// <c>_vehicleStates</c> that <c>VehicleUpdateTask.Run</c> enumerates off the main thread. The
    /// scheduler is the whole barrier, since the worker pool runs inside a disposed
    /// <c>ParallelBatch</c>. No mod hook sits in <c>PrepareFrame</c>'s safe window
    /// (<c>docs/KSA-FRAME-ORDER.md</c>).</para>
    /// </summary>
    public static void WaitForVehicleSolvers()
    {
        try
        {
            JobSystems.VehicleSolver?.Wait();
        }
        catch (Exception e)
        {
            // The race stays; taking the frame down is worse.
            Log.Warn($"could not join the vehicle solvers before a kill ({e.GetType().Name})");
        }
    }

    /// <summary>
    /// Takes a vehicle out of the world without breaking it up. Same threading rule as
    /// <see cref="Destroy"/>, and it takes that barrier itself: removing a vehicle also mutates the
    /// shapes registry, which the vehicle worker holds for its whole run, and a removal refused
    /// half-way leaves the physics bubble indexing a vehicle list one shorter than it thinks.
    ///
    /// <para>Not <c>DestroyVehicleFromEvent</c>, which sheds up to twelve debris vehicles.
    /// <c>EndMission</c> rather than <c>Kill</c>, the crew disposition being the only difference.</para>
    /// </summary>
    public static void Remove(Vehicle v)
    {
        if (!IsAlive(v)) return;
        try
        {
            WaitForVehicleSolvers();
            using ShapesUnlock shapes = ConstraintSim.UnlockShapesBlocking();

            Universe.DestroyVehicle(v, CrewDisposition.EndMission);
            InvalidateCensus();
        }
        catch (Exception e)
        {
            Log.Warn($"could not remove {DisplayName(v)} from the world: {e.Message}");
        }
    }

    /// <summary>
    /// Destroys a vehicle, attributing it to collision damage. Must be called from the main
    /// thread, and only after <see cref="WaitForVehicleSolvers"/> — see there for why.
    ///
    /// <para>Leaves debris; <see cref="Remove"/> does not.</para>
    /// </summary>
    public static void Destroy(Vehicle v, float blastSeverity)
    {
        if (!IsAlive(v)) return;
        try
        {
            var evt = new VehicleDestructionEvent
            {
                Cause = VehicleDestructionCause.Collision,
                PeakGLoad = blastSeverity,
                PeakDynamicPressure = blastSeverity,
            };
            Universe.DestroyVehicleFromEvent(v, evt);

            InvalidateCensus();
        }
        catch (Exception e)
        {
            Log.Warn($"failed to destroy {DisplayName(v)}: {e.Message}");
        }
    }

    /// <summary>A part's box in its craft's assembly frame: the centre, and half its size along each axis.</summary>
    public static bool TryPartBox(Part part, out double3 centreAsmb, out double3 halfExtents)
    {
        centreAsmb = default;
        halfExtents = default;

        try
        {
            (double3 min, double3 max) = part.BoundingBoxVehicleAsmb;
            centreAsmb = (min + max) * 0.5;
            halfExtents = (max - min) * 0.5;
            return Vec.IsFinite(centreAsmb) && Vec.IsFinite(halfExtents);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where a part's box centre is drawn, in Ego: off the craft's drawn position rather than its
    /// analytic one, which on a landed craft is metres away.
    /// </summary>
    public static bool TryPartEgo(Vehicle v, Part part, out double3 centreEgo)
    {
        centreEgo = default;
        if (!TryPartBox(part, out double3 centreAsmb, out _) || !TryVehicleEgo(v, out double3 craftEgo)) return false;

        try
        {
            centreEgo = craftEgo + (v.Asmb2Ego * (centreAsmb - v.CenterOfMassAsmb));
            return Vec.IsFinite(centreEgo);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where a round's path meets a craft's mesh, as the sub-part it struck, the point in that
    /// sub-part's own frame — the engine's <c>RayCastEgo</c> answers in exactly that frame — and
    /// where the cast started in the same frame, which is the path the round came in along.
    /// <paramref name="separation"/> is the craft's centre less the round, both at the instant it
    /// struck, and the cast starts <paramref name="castFromMetres"/> back along
    /// <paramref name="travel"/>, the round's velocity relative to the craft.
    /// </summary>
    public static bool TryHullHit(Vehicle v, double3 separation, double3 travel, double castFromMetres,
                                  out Part? subPart, out double3 local, out double3 cameFrom)
    {
        subPart = null;
        local = cameFrom = default;

        double length = Vec.Len(travel);
        if (!IsAlive(v) || !(length > 0.0) || !Vec.IsFinite(separation)) return false;

        try
        {
            double3 along = travel / length;
            double4x4 asmb2Round = v.GetMatrixAsmb2Ego(separation);
            Ray ray = new() { Origin = along * -castFromMetres, Direction = along };

            double nearest = double.MaxValue;
            foreach (Part part in v.Parts.Parts)
            {
                if (!part.RayCastEgo(in asmb2Round, ray, out double near, out _, out double3 at, out _,
                                     out _, out _, out Part? struck, out _)
                    || struck is null || near < 0.0 || near >= nearest)
                {
                    continue;
                }

                nearest = near;
                subPart = struck;
                local = at;
            }

            if (subPart is null) return false;

            double4x4.Invert(subPart.MatrixAsmb2Ego(in asmb2Round), out double4x4 toLocal);
            cameFrom = ray.Origin.Transform(toLocal);
            return Vec.IsFinite(local) && Vec.IsFinite(cameFrom);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The acceleration a craft feels, in its own assembly frame — what an accelerometer aboard reads:
    /// thrust, drag and the ground's push, with gravity left out. Liquid settles against it.
    /// </summary>
    public static double3 FeltAccelerationAsmb(Vehicle v)
    {
        try
        {
            double3 felt = v.Asmb2Ego.Inverse() * v.AccelerationBody.Transform(v.Body2Cce);
            return Vec.IsFinite(felt) ? felt : Vec.Zero;
        }
        catch
        {
            return Vec.Zero;
        }
    }

    /// <summary>
    /// A tank's own shape — outside and, inside its wall, what the liquid fills — with where it sits in
    /// its part: the origin and the part-frame directions of its own x, y and z.
    ///
    /// <para>Read off the tank's template, a private field found by reflection and verified; a tank of a
    /// shape this does not know, or a build that has moved the field, answers false and the caller takes
    /// the cylinder filling the part's box instead.</para>
    /// </summary>
    public static bool TryTankShape(Tank tank, out TankShape outside, out TankShape inside, out double3 origin,
                                    out double3 axisX, out double3 axisY, out double3 axisZ)
    {
        outside = inside = default;
        origin = axisX = axisY = axisZ = default;

        try
        {
            if (TankTemplateField?.GetValue(tank) is not AsmbTankTemplate template) return false;

            double wall;
            switch (template)
            {
                case SphericalTankTemplate sphere:
                    outside = TankShape.Sphere(sphere.OuterRadius);
                    wall = sphere.WallThickness;
                    break;
                case CylindricalTankTemplate cylinder:
                    outside = TankShape.Conical(cylinder.Length, cylinder.OuterRadius, cylinder.OuterRadius,
                                                cylinder.DomeHeightFraction);
                    wall = cylinder.WallThickness;
                    break;
                case ConicalTankTemplate cone:
                    outside = TankShape.Conical(cone.Length, cone.RadiusBase, cone.RadiusTop, cone.DomeHeightFraction);
                    wall = cone.WallThickness;
                    break;
                default:
                    return false;
            }

            inside = outside.Inside(wall);
            origin = template.LocationAsmb;
            floatQuat paf2Asmb = template.GetPaf2Asmb();
            axisX = double3.Unpack(new float3(1f, 0f, 0f).Transform(paf2Asmb));
            axisY = double3.Unpack(new float3(0f, 1f, 0f).Transform(paf2Asmb));
            axisZ = double3.Unpack(new float3(0f, 0f, 1f).Transform(paf2Asmb));

            return outside.Extent.Radius > 0.0 && Vec.IsFinite(origin) && Vec.IsFinite(axisX);
        }
        catch
        {
            return false;
        }
    }

    private static FieldInfo? TankTemplateField
    {
        get
        {
            if (_tankTemplateProbed) return _tankTemplateField;

            _tankTemplateProbed = true;
            _tankTemplateField = typeof(Tank).GetField("_template", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_tankTemplateField is null || !typeof(AsmbTankTemplate).IsAssignableFrom(_tankTemplateField.FieldType))
            {
                Log.Warn("Tank._template has moved - leaking tanks are taken as the cylinder filling their box");
                _tankTemplateField = null;
            }

            return _tankTemplateField;
        }
    }

    private static FieldInfo? _tankTemplateField;

    private static bool _tankTemplateProbed;

    /// <summary>What a tank holds, in kg, off the craft's own resource states.</summary>
    public static bool TryTankMassKg(Tank tank, out double kg)
    {
        kg = 0.0;
        try
        {
            if (!TryCraftOf(tank.Parent, out Vehicle? craft)) return false;
            ReadOnlySpan<MoleState> states = craft.Parts.Moles.States;
            foreach (Mole mole in tank.Moles) kg += states[mole.StatesIdx].Mass;
            return double.IsFinite(kg);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The tank a part carries, on the part or any of its sub-parts.</summary>
    public static bool TryTankOf(Part part, [NotNullWhen(true)] out Tank? tank)
    {
        tank = null;

        try
        {
            Part full = part.FullPart;
            Span<Tank> own = full.Modules.Get<Tank>();
            if (!own.IsEmpty)
            {
                tank = own[0];
                return true;
            }

            foreach (Part sub in full.SubParts)
            {
                Span<Tank> theirs = sub.Modules.Get<Tank>();
                if (theirs.IsEmpty) continue;

                tank = theirs[0];
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where a point in a sub-part's own frame is drawn, in Ecl: through the matrix the engine draws the
    /// part with for the main camera, and back, since a craft's drawn place and its analytic one are
    /// metres apart on the ground. For a particle emitter, which takes Ecl.
    /// </summary>
    public static bool TryDrawnPartPointEcl(Part subPart, double3 local, out double3 ecl)
    {
        ecl = default;

        try
        {
            if (Program.GetMainCamera() is not { } camera) return false;
            if (!TryCraftOf(subPart, out Vehicle? craft)) return false;

            double4x4 vehicle = craft.GetMatrixAsmb2Ego(camera);
            ecl = camera.EgoToEcl(local.Transform(subPart.MatrixAsmb2Ego(in vehicle)));
            return Vec.IsFinite(ecl);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where a craft's centre of mass is drawn, in Ecl, for the main camera or one viewport's,
    /// which draw other craft metres apart.
    /// </summary>
    public static bool TryDrawnCentreEcl(Vehicle craft, out double3 ecl, int viewIndex = -1)
    {
        ecl = default;

        try
        {
            if (CameraFor(viewIndex) is not { } camera || !IsAlive(craft)) return false;

            ecl = camera.EgoToEcl(craft.CenterOfMassAsmb.Transform(craft.GetMatrixAsmb2Ego(camera)));
            return Vec.IsFinite(ecl);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>A part's own frame to its craft's assembly frame, sub-parts' parents included.</summary>
    public static bool TryPartToVehicleAsmb(Part part, out double4x4 toVehicle)
    {
        toVehicle = default;

        try
        {
            double4x4 identity = double4x4.Identity;
            toVehicle = part.MatrixAsmb2Ego(in identity);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The live craft a part is on.</summary>
    public static bool TryCraftOf(Part part, [NotNullWhen(true)] out Vehicle? craft)
    {
        craft = null;

        try
        {
            craft = part.FullPart.Tree?.OwningVehicle;
            return craft is not null && IsAlive(craft);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The matrix the engine draws a craft with for <paramref name="camera"/>, with where its centre
    /// of mass is then and how far its parts reach from it.
    /// </summary>
    public static bool TryVehicleMatrixEgo(Vehicle v, Camera camera, out double4x4 asmb2Ego, out double3 centreEgo,
                                           out double radiusMetres)
    {
        asmb2Ego = default;
        centreEgo = default;
        radiusMetres = 0.0;

        try
        {
            asmb2Ego = v.GetMatrixAsmb2Ego(camera);
            centreEgo = v.CenterOfMassAsmb.Transform(asmb2Ego);
            radiusMetres = MeanRadius(v);
            return Vec.IsFinite(centreEgo);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>A sub-part's own frame to the camera, off its craft's <see cref="TryVehicleMatrixEgo"/>.</summary>
    public static bool TryPartMatrixEgo(Part subPart, in double4x4 vehicleAsmb2Ego, out double4x4 toEgo)
    {
        toEgo = default;

        try
        {
            toEgo = subPart.MatrixAsmb2Ego(in vehicleAsmb2Ego);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Whether a part is on a craft that is still in the world.</summary>
    public static bool IsOnLiveCraft(Part part)
    {
        try
        {
            return part.FullPart.Tree?.OwningVehicle is { } v && IsAlive(v);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// How hot a craft looks to a heat seeker, in kW/sr: its engines' thrust at the throttle it is
    /// set to, while there is propellant to burn. The throttle is the manual control's, which is every
    /// craft's in this build; an engine shut down at full throttle still reads hot.
    /// </summary>
    public static double HeatOf(Vehicle v)
    {
        try
        {
            double thrust = v.IsAnyEnginePropellantAvailable()
                                ? v.FlightComputer.ActiveEnginePerformanceMax.Thrust
                                : 0.0;
            return Signature.HeatOfCraft(thrust, v.GetManualThrottle());
        }
        catch
        {
            return Signature.AirframeKwPerSr;
        }
    }

    /// <summary>A direction in a craft's assembly frame, turned into the ecliptic.</summary>
    public static double3 VehicleAsmbDirectionToEcl(Vehicle v, double3 directionAsmb) => v.Asmb2Ego * directionAsmb;

    /// <summary>Where a craft's mass is centred, in its own assembly frame.</summary>
    public static bool TryCentreOfMassAsmb(Vehicle v, out double3 centreAsmb)
    {
        centreAsmb = default;
        if (!IsAlive(v)) return false;

        try
        {
            centreAsmb = v.CenterOfMassAsmb;
            return Vec.IsFinite(centreAsmb);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where a burst strikes a part, and which way it pushes, in the craft's assembly frame: the
    /// point where the ray from the part's box centre toward the burst leaves the box, as the
    /// engine's own test dent places one, and the push along the blast. The box's centre and the
    /// face it shows the blast are what the wind behind the front pushes on.
    /// </summary>
    public static bool TryBlastFace(Part part, double3 burstAsmb, out double3 faceAsmb, out double3 pushAsmb,
                                    out double acrossMetres, out double3 centreAsmb, out double facingM2)
    {
        faceAsmb = default;
        pushAsmb = default;
        acrossMetres = 0.0;
        centreAsmb = default;
        facingM2 = 0.0;

        (double3 min, double3 max) = part.BoundingBoxVehicleAsmb;
        double3 centre = (min + max) * 0.5;
        double3 half = (max - min) * 0.5;

        double3 toward = burstAsmb - centre;
        double length = Vec.Len(toward);
        if (!(length > 1.0e-6)) return false;
        toward /= length;

        double exit = double.MaxValue;
        if (Math.Abs(toward.X) > 1.0e-9) exit = Math.Min(exit, half.X / Math.Abs(toward.X));
        if (Math.Abs(toward.Y) > 1.0e-9) exit = Math.Min(exit, half.Y / Math.Abs(toward.Y));
        if (Math.Abs(toward.Z) > 1.0e-9) exit = Math.Min(exit, half.Z / Math.Abs(toward.Z));
        if (!double.IsFinite(exit)) return false;

        faceAsmb = centre + (toward * exit);
        pushAsmb = -toward;
        acrossMetres = Vec.Len(half);
        centreAsmb = centre;
        facingM2 = BlastShove.ProjectedArea(half, toward);
        return Vec.IsFinite(faceAsmb);
    }

    /// <summary>
    /// Dents one part through the engine's own impact path, as a collision would, and answers
    /// whether the engine took it: its threshold, its depth law and the player's Impact Dents
    /// setting decide, not this. The footprint is the part's whole cross-section, which the engine
    /// clamps to its largest, because a shock loads a whole face where a collision loads a patch.
    /// </summary>
    public static bool ReportBlastDent(Part part, double3 faceAsmb, double3 pushAsmb, double acrossMetres,
                                       double pressureRatio)
    {
        try
        {
            double tolerance = part.FullPart.CrashTolerancePascals;
            if (!(tolerance > 0.0) || !(pressureRatio > 0.0)) return false;

            int before = FxDeformation.Shared.TotalReported;

            // The engine reads an impulse per area and divides by its contact step, 0.01, to get the
            // pressure it compares with the part's tolerance.
            FxDeformation.ReportContact(part, float3.Zero, float3.Pack(in faceAsmb), float3.Pack(in pushAsmb),
                                        pressureRatio * tolerance * 0.01, Math.PI * acrossMetres * acrossMetres);

            return FxDeformation.Shared.TotalReported > before;
        }
        catch (Exception e)
        {
            if (!_warnedAboutDents)
            {
                _warnedAboutDents = true;
                Log.Warn($"a blast dent could not be reported: {e.Message}");
            }

            return false;
        }
    }

    private static bool _warnedAboutDents;

    /// <summary>
    /// A point in the craft's assembly frame, by the pairing <see cref="TryCollectDamageableParts"/>
    /// places the parts with, inverted -- the frame the engine keeps its dents in.
    /// </summary>
    public static double3 EclToVehicleAsmb(Vehicle v, double3 pointEcl)
        => v.CenterOfMassAsmb + (v.Asmb2Ego.Inverse() * (pointEcl - PositionEcl(v)));

    /// <summary>And back: a point in the craft's assembly frame, in the ecliptic.</summary>
    public static double3 VehicleAsmbToEcl(Vehicle v, double3 pointAsmb)
        => PositionEcl(v) + (v.Asmb2Ego * (pointAsmb - v.CenterOfMassAsmb));

    /// <summary>
    /// The dents the engine holds on a craft, in its assembly frame: where each sits, which way it
    /// pushes, and how deep and wide it is.
    /// </summary>
    public static List<(double3 Centre, double3 Push, double Radius, double Depth)> DentsOn(Vehicle v)
    {
        List<(double3, double3, double, double)> dents = [];

        try
        {
            KSA.PartTree tree = v.Parts;
            tree.Dents.EnsureBuilt(tree, Program.GetRenderer().FrameCount);

            foreach (KSA.Deformation.Dent d in tree.Dents.Dents)
            {
                dents.Add((double3.Unpack(in d.Center), double3.Unpack(in d.Direction), d.Radius, d.Depth));
            }
        }
        catch (Exception e)
        {
            Log.Warn($"could not read the dents on {DisplayName(v)}: {e.Message}");
        }

        return dents;
    }

    /// <summary>Takes every dent off a craft, through the engine's own clear.</summary>
    public static int ClearDents(Vehicle v)
    {
        int cleared = 0;

        foreach (Part part in v.Parts.Parts)
        {
            foreach (FxDeformation module in part.Modules.Get<FxDeformation>())
            {
                module.ClearDents();
                cleared++;
            }
        }

        return cleared;
    }

    /// <summary>
    /// Every part of a craft a blast could break, with the three things
    /// <see cref="BlastDamage"/> needs about each.
    ///
    /// <para>Ecl, against <paramref name="vehicleEcl"/> so the craft is read at one instant, with the
    /// centre-of-mass correction the tube geometry uses. Internally attached parts are skipped, as
    /// <c>PartFailure.Detect</c> skips them: the engine abandons one mid-split.</para>
    ///
    /// <para>False for an unreadable tree, which the caller destroys whole rather than treat as
    /// bulletproof.</para>
    /// </summary>
    public static bool TryCollectDamageableParts(Vehicle v, double3 vehicleEcl,
                                                 List<DamageablePart> into, List<Part> handles)
    {
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(handles);

        into.Clear();
        handles.Clear();

        try
        {
            if (v.IsDisposed) return false;

            double3 centreOfMass = v.CenterOfMassAsmb;
            doubleQuat asmb2Ego = v.Asmb2Ego;

            ReadOnlySpan<Part> parts = v.Parts.Parts;
            if (parts.Length == 0) return false;

            for (int i = 0; i < parts.Length; i++)
            {
                Part part = parts[i];
                if (part.IsAttachedInternal) continue;

                double3 ecl = vehicleEcl + asmb2Ego * (part.PositionVehicleAsmb - centreOfMass);
                if (!Vec.IsFinite(ecl)) continue;

                (double3 min, double3 max) = part.BoundingBoxVehicleAsmb;
                double radius = Vec.Len(max - min) * 0.5;
                if (!double.IsFinite(radius) || radius < 0.0) radius = 0.0;

                double3 size = max - min;
                double area = 0.5 * ((size.X * size.Y) + (size.Y * size.Z) + (size.Z * size.X));
                if (!double.IsFinite(area) || area < 0.0) area = 0.0;

                into.Add(new DamageablePart(handles.Count, ecl, radius, part.CrashTolerancePascals,
                                            MassKg: part.ComputeSubtreeInertMass(), PresentedAreaM2: area));
                handles.Add(part);
            }

            return handles.Count > 0;
        }
        catch (Exception e)
        {
            Log.Warn($"could not read the parts of {DisplayName(v)}: {e.Message}");
            into.Clear();
            handles.Clear();
            return false;
        }
    }

    /// <summary>How many parts a craft has, or zero for one that cannot be read.</summary>
    public static int PartCount(Vehicle v)
    {
        if (!IsAlive(v)) return 0;

        try
        {
            return v.Parts.Count;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Whether losing this many of a craft's parts at once destroys it outright.
    ///
    /// <para>KSA's own rule, asked rather than copied, so it cannot drift from the engine.</para>
    /// </summary>
    public static bool LosingThatManyPartsIsFatal(int failedCount, int partCount)
    {
        try
        {
            return PartFailure.TrippedTheFragmentGuard(failedCount, partCount);
        }
        catch (Exception e)
        {
            Log.Warn($"fragment guard unavailable ({e.GetType().Name}); treating {failedCount} lost parts as fatal");
            return true;
        }
    }

    /// <summary>
    /// Whether part failures can be handed to the engine at all.
    ///
    /// <para>Probed once. If the field ever moves the mod falls back to destroying whole craft,
    /// as <c>Config.DamageIndividualParts</c> off does — a worse weapon, not a broken one.</para>
    /// </summary>
    public static bool CanQueuePartFailures
    {
        get
        {
            if (_partFailureQueueProbed) return _updateStateField is not null;

            _partFailureQueueProbed = true;
            _updateStateField = typeof(Vehicle).GetField(
                "_threadWorkerUpdateState", BindingFlags.Instance | BindingFlags.NonPublic);

            if (_updateStateField is null || _updateStateField.FieldType != typeof(VehicleUpdateState))
            {
                Log.Warn("Vehicle._threadWorkerUpdateState has moved - part damage is off for this "
                         + "session and warheads will destroy whole craft");
                _updateStateField = null;
            }

            return _updateStateField is not null;
        }
    }

    private static FieldInfo? _updateStateField;

    private static bool _partFailureQueueProbed;

    /// <summary>
    /// Hands the engine a set of parts to break off a craft, to be applied at the engine's own
    /// point in the frame.
    ///
    /// <para>Queued, never applied here: a split from a mod hook hands results staged against the
    /// whole craft to its fragments. It goes where <c>PartFailure.Detect</c> puts its own, applied
    /// by <c>ApplyRenderEventsToVehicles</c> (<c>docs/KSA-FRAME-ORDER.md</c>). A pending event is
    /// merged into, not replaced.</para>
    /// </summary>
    public static bool TryQueuePartFailure(Vehicle v, List<Part> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        if (!IsAlive(v) || parts.Count == 0 || !CanQueuePartFailures) return false;

        try
        {
            if (_updateStateField!.GetValue(v) is not VehicleUpdateState state)
            {
                // No update state means no physics bubble, so nothing is simulating it to break.
                Log.Debug(() => $"no update state on {DisplayName(v)}; its parts were not broken");
                return false;
            }

            // Before the engine splits it, so the pieces it names after this craft are known for
            // wreckage from the first frame they exist.
            Wreckage.Broke(v.Id);

            if (state.PartFailureEvent is { } pending)
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    if (!pending.FailedParts.Contains(parts[i])) pending.FailedParts.Add(parts[i]);
                }

                return true;
            }

            InvalidateCensus();

            state.PartFailureEvent = new PartFailureEvent
            {
                FailedParts = parts,

                // Never set. The fragment guard was asked when the parts were queued, and a craft
                // it condemned was destroyed outright instead of reaching here.
                DestroyWholeVehicle = false,
            };

            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"failed to queue {parts.Count} part failure(s) on {DisplayName(v)}: {e.Message}");
            return false;
        }
    }
}
