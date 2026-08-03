# KSA API surface

Every external type and member `AirDefence.dll` binds to, read out of its
metadata tables by `tools/api-surface.sh`. **Generated - do not edit.**

This is the checklist for a KSA update: anything here that changed shape in the new
build is a breaking change for this mod, and anything not here cannot be. See the
`upgrade-ksa` skill, which diffs the decompiled sources against exactly this list.

44 types and 119 members across 4 assemblies.

## Brutal.Core.Numerics

### Brutal.Numerics.byte4

- `void .ctor(byte, byte, byte, byte)`

### Brutal.Numerics.double3

- `Brutal.Numerics.double3 Cross(Brutal.Numerics.double3, Brutal.Numerics.double3)`
- `Brutal.Numerics.double3 op_Addition(Brutal.Numerics.double3, Brutal.Numerics.double3)`
- `Brutal.Numerics.double3 op_Division(Brutal.Numerics.double3, double)`
- `Brutal.Numerics.double3 op_Multiply(Brutal.Numerics.double3, double)`
- `Brutal.Numerics.double3 op_Subtraction(Brutal.Numerics.double3, Brutal.Numerics.double3)`
- `Brutal.Numerics.double3 op_UnaryNegation(Brutal.Numerics.double3)`
- `bool Equals(Brutal.Numerics.double3)`
- `double Dot(Brutal.Numerics.double3, Brutal.Numerics.double3)`
- `double Length()`
- `double LengthSquared()`
- `double X`
- `double Y`
- `double Z`
- `void .ctor(double, double, double)`

### Brutal.Numerics.double4x4

*referenced as a type only*

### Brutal.Numerics.doubleQuat

- `Brutal.Numerics.double3 op_Multiply(Brutal.Numerics.doubleQuat, Brutal.Numerics.double3)`
- `Brutal.Numerics.doubleQuat Conjugate(Brutal.Numerics.doubleQuat)`
- `Brutal.Numerics.doubleQuat CreateFromAxisAngle(Brutal.Numerics.double3, double)`
- `Brutal.Numerics.doubleQuat get_Identity()`
- `Brutal.Numerics.doubleQuat op_Multiply(Brutal.Numerics.doubleQuat, Brutal.Numerics.doubleQuat)`

### Brutal.Numerics.float2

*referenced as a type only*

### Brutal.Numerics.float4

- `void .ctor(float, float, float, float)`

## Brutal.ImGui

### Brutal.ImGuiApi.ImGui

- `bool Begin(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiWindowFlags)`
- `bool Begin(Brutal.ImGuiApi.ImString, ref bool, Brutal.ImGuiApi.ImGuiWindowFlags)`
- `bool Button(Brutal.ImGuiApi.ImString, ref System.Nullable`1<Brutal.Numerics.float2>)`
- `bool Checkbox(Brutal.ImGuiApi.ImString, ref bool)`
- `bool SliderFloat(Brutal.ImGuiApi.ImString, ref float, float, float, Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiSliderFlags)`
- `bool SliderInt(Brutal.ImGuiApi.ImString, ref int, int, int, Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiSliderFlags)`
- `bool TreeNode(Brutal.ImGuiApi.ImString)`
- `void End()`
- `void ProgressBar(float, ref System.Nullable`1<Brutal.Numerics.float2>, Brutal.ImGuiApi.ImString)`
- `void SameLine(float, float)`
- `void Separator()`
- `void Text(Brutal.ImGuiApi.ImString)`
- `void TextColored(ref Brutal.Numerics.float4, Brutal.ImGuiApi.ImString)`
- `void TextDisabled(Brutal.ImGuiApi.ImString)`
- `void TreePop()`

### Brutal.ImGuiApi.ImGuiSliderFlags

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiWindowFlags

*referenced as a type only*

### Brutal.ImGuiApi.ImString

- `Brutal.ImGuiApi.ImString op_Implicit(string)`
- `void .ctor(int, int)`
- `void AppendFormatted(string, int, string)`
- `void AppendFormatted<1>(!!0, int, string)`
- `void AppendLiteral(System.ReadOnlySpan`1<char>)`

## KSA

### KSA.Astronomical

- `Brutal.Numerics.double3 GetPositionEcl()`
- `Brutal.Numerics.double3 GetVelocityEcl()`
- `double get_MeanRadius()`
- `string get_Id()`
- `void UpdatePerFrameData()`

### KSA.Camera

- `Brutal.Numerics.double3 EclToEgo(Brutal.Numerics.double3)`
- `Brutal.Numerics.double3 EgoToEcl(Brutal.Numerics.double3)`
- `Brutal.Numerics.double3 GetPositionEgo(KSA.IPosition)`
- `KSA.IFollowable get_Following()`

### KSA.CelestialSystem

- `KSA.LookupCollection`1<KSA.Astronomical> get_All()`

### KSA.DefaultVehicleSaves

- `KSA.VehicleSave FindSave(string)`

### KSA.Double3Ex

- `Brutal.Numerics.double3 Transform(Brutal.Numerics.double3, Brutal.Numerics.double4x4)`

### KSA.GizmosRenderer

- `void DrawLine(Brutal.Numerics.double3, Brutal.Numerics.double3, Brutal.Numerics.float4)`
- `void DrawSphere(Brutal.Numerics.double3, float, Brutal.Numerics.float4)`

### KSA.IFollowable

*referenced as a type only*

### KSA.IObjectId

- `string get_Id()`

### KSA.IOrbiter

*referenced as a type only*

### KSA.IParentBody

- `Brutal.Numerics.doubleQuat GetCce2Cci()`
- `System.Collections.Generic.List`1<KSA.IOrbiter> get_Children()`
- `double get_Mu()`

### KSA.IPosition

- `Brutal.Numerics.double3 GetPositionEcl()`

### KSA.IVelocity

- `Brutal.Numerics.double3 GetVelocityEcl()`

### KSA.LookupCollection`1

*referenced as a type only*

### KSA.Mod

- `string get_Id()`

### KSA.Orbit

- `KSA.Orbit CreateFromStateCci(KSA.IParentBody, KSA.SimTime, Brutal.Numerics.double3, Brutal.Numerics.double3, Brutal.Numerics.byte4)`
- `double get_Apoapsis()`
- `double get_Eccentricity()`
- `double get_Periapsis()`

### KSA.Part

- `Brutal.Numerics.double3 PositionEgo(ref Brutal.Numerics.double4x4)`
- `Brutal.Numerics.double3 PositionVehicleAsmbOffset(Brutal.Numerics.double3)`
- `Brutal.Numerics.double3 get_PositionParentAsmb()`
- `Brutal.Numerics.double3 get_PositionVehicleAsmb()`
- `Brutal.Numerics.double3 get_Scale()`
- `Brutal.Numerics.doubleQuat get_Asmb2ParentAsmb()`
- `Brutal.Numerics.doubleQuat get_Asmb2VehicleAsmb()`
- `System.ReadOnlySpan`1<KSA.Part> get_SubParts()`
- `string get_Id()`
- `void ResetCachedPosMatrixValues()`
- `void set_Asmb2ParentAsmb(Brutal.Numerics.doubleQuat)`
- `void set_Asmb2ParentAsmbSafe(Brutal.Numerics.doubleQuat)`
- `void set_PositionParentAsmb(Brutal.Numerics.double3)`
- `void set_PositionParentAsmbSafe(Brutal.Numerics.double3)`
- `void set_Scale(Brutal.Numerics.double3)`

### KSA.PartTree

- `KSA.Part get_Root()`
- `KSA.PartTree DeepCopy()`
- `System.ReadOnlySpan`1<KSA.Part> get_Parts()`

### KSA.Program

- `KSA.Camera GetMainCamera()`
- `KSA.Camera GetRenderCamera()`
- `KSA.GizmosRenderer GizmosRenderer`
- `KSA.Vehicle ControlledVehicle`
- `KSA.Viewport get_MainViewport()`
- `System.ReadOnlySpan`1<KSA.Vehicle> get_VehiclesInFrame()`

### KSA.SimStep

- `double get_DeltaTime()`

### KSA.SimTime

*referenced as a type only*

### KSA.Transform3D

- `Brutal.Numerics.double3 get_PositionEcl()`

### KSA.Universe

- `KSA.CelestialSystem get_CurrentSystem()`
- `KSA.SimStep GetLastSimStep()`
- `KSA.SimTime GetElapsedSimTime()`
- `bool IsPaused()`
- `double get_SimulationSpeed()`
- `void DestroyVehicleFromEvent(KSA.Vehicle, KSA.VehicleDestructionEvent)`

### KSA.Vehicle

- `Brutal.Numerics.double4x4 GetMatrixAsmb2Ego(Brutal.Numerics.double3)`
- `Brutal.Numerics.doubleQuat get_Asmb2Ego()`
- `Brutal.Numerics.doubleQuat get_Body2Cce()`
- `KSA.IParentBody get_Parent()`
- `KSA.PartTree get_Parts()`
- `KSA.Vehicle CreateVehicle(KSA.CelestialSystem, Brutal.Numerics.doubleQuat, Brutal.Numerics.double3, KSA.IParentBody, string, KSA.Part, KSA.Orbit)`
- `KSA.Vehicle get_BubbleLeader()`
- `KSA.VehicleUpdateTask UpdateTask`
- `bool get_IsDisposed()`
- `void AddToTask(KSA.VehicleUpdateTask)`

### KSA.VehicleDestructionCause

*referenced as a type only*

### KSA.VehicleDestructionEvent

- `KSA.VehicleDestructionCause Cause`
- `float PeakDynamicPressure`
- `float PeakGLoad`
- `void .ctor()`

### KSA.VehicleSave

- `KSA.PartTree Load(KSA.Viewport)`

### KSA.VehicleUpdateTask

*referenced as a type only*

### KSA.Viewport

*referenced as a type only*

## StarMap.API

### StarMap.API.StarMapAfterGuiAttribute

- `void .ctor()`

### StarMap.API.StarMapAfterOnFrameAttribute

- `void .ctor()`

### StarMap.API.StarMapAllModsLoadedAttribute

- `void .ctor()`

### StarMap.API.StarMapImmediateLoadAttribute

- `void .ctor()`

### StarMap.API.StarMapModAttribute

- `void .ctor()`

### StarMap.API.StarMapUnloadAttribute

- `void .ctor()`
