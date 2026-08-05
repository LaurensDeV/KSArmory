# KSA API surface

Every external type and member `KSArmory.dll` binds to, read out of its
metadata tables by `tools/api-surface.sh`. **Generated - do not edit.**

This is the checklist for a KSA update: anything here that changed shape in the new
build is a breaking change for this mod, and anything not here cannot be. See the
`upgrade-ksa` skill, which diffs the decompiled sources against exactly this list.

94 types and 252 members across 6 assemblies.

## Brutal.Concurrency

### Brutal.Concurrency.Jobs.JobScheduler

- `void Wait()`

## Brutal.Core.Common

### Brutal.Pointers.Ptr

*referenced as a type only*

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

- `Brutal.Numerics.float2 op_Addition(Brutal.Numerics.float2, Brutal.Numerics.float2)`
- `float X`
- `float Y`
- `void .ctor(float, float)`

### Brutal.Numerics.float3

- `float X`
- `float Y`
- `float Z`

### Brutal.Numerics.float4

- `void .ctor(float, float, float, float)`

### Brutal.Numerics.int2

- `int X`
- `int Y`

## Brutal.ImGui

### Brutal.ImGuiApi.ImColor8

- `void .ctor(byte, byte, byte, byte)`

### Brutal.ImGuiApi.ImDrawListExtensions

- `void AddLine(Brutal.ImGuiApi.ImDrawListPtr, ref Brutal.Numerics.float2, ref Brutal.Numerics.float2, Brutal.ImGuiApi.ImColor8, float)`
- `void AddText(Brutal.ImGuiApi.ImDrawListPtr, ref Brutal.Numerics.float2, Brutal.ImGuiApi.ImColor8, Brutal.ImGuiApi.ImString)`
- `void AddTriangleFilled(Brutal.ImGuiApi.ImDrawListPtr, ref Brutal.Numerics.float2, ref Brutal.Numerics.float2, ref Brutal.Numerics.float2, Brutal.ImGuiApi.ImColor8)`

### Brutal.ImGuiApi.ImDrawListPtr

*referenced as a type only*

### Brutal.ImGuiApi.ImGui

- `Brutal.ImGuiApi.ImDrawListPtr GetWindowDrawList()`
- `Brutal.ImGuiApi.ImGuiIOPtr GetIO()`
- `Brutal.ImGuiApi.ImGuiViewportPtr GetMainViewport()`
- `Brutal.Numerics.float2 CalcTextSize(Brutal.ImGuiApi.ImString, bool, float)`
- `Brutal.Numerics.float2 GetMousePos()`
- `bool Begin(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiWindowFlags)`
- `bool Begin(Brutal.ImGuiApi.ImString, ref bool, Brutal.ImGuiApi.ImGuiWindowFlags)`
- `bool BeginTabBar(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiTabBarFlags)`
- `bool BeginTabItem(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiTabItemFlags)`
- `bool BeginTable(Brutal.ImGuiApi.ImString, int, Brutal.ImGuiApi.ImGuiTableFlags, ref System.Nullable`1<Brutal.Numerics.float2>, float)`
- `bool Button(Brutal.ImGuiApi.ImString, ref System.Nullable`1<Brutal.Numerics.float2>)`
- `bool Checkbox(Brutal.ImGuiApi.ImString, ref bool)`
- `bool InputText(Brutal.ImGuiApi.ImString, System.ReadOnlySpan`1<byte>, Brutal.ImGuiApi.ImGuiInputTextFlags, Brutal.ImGuiApi.ImGuiInputTextCallback, Brutal.Pointers.Ptr)`
- `bool IsItemHovered(Brutal.ImGuiApi.ImGuiHoveredFlags)`
- `bool IsMouseClicked(Brutal.ImGuiApi.ImGuiMouseButton, bool)`
- `bool IsMouseDown(Brutal.ImGuiApi.ImGuiMouseButton)`
- `bool IsWindowHovered(Brutal.ImGuiApi.ImGuiHoveredFlags)`
- `bool RadioButton(Brutal.ImGuiApi.ImString, bool)`
- `bool SliderFloat(Brutal.ImGuiApi.ImString, ref float, float, float, Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiSliderFlags)`
- `bool SliderInt(Brutal.ImGuiApi.ImString, ref int, int, int, Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiSliderFlags)`
- `bool SmallButton(Brutal.ImGuiApi.ImString)`
- `bool TableNextColumn()`
- `bool TreeNode(Brutal.ImGuiApi.ImString)`
- `void End()`
- `void EndTabBar()`
- `void EndTabItem()`
- `void EndTable()`
- `void PopID()`
- `void PopStyleColor(int)`
- `void ProgressBar(float, ref System.Nullable`1<Brutal.Numerics.float2>, Brutal.ImGuiApi.ImString)`
- `void PushID(int)`
- `void PushStyleColor(Brutal.ImGuiApi.ImGuiCol, ref Brutal.Numerics.float4)`
- `void SameLine(float, float)`
- `void Separator()`
- `void SetNextWindowBgAlpha(float)`
- `void SetNextWindowPos(ref Brutal.Numerics.float2, Brutal.ImGuiApi.ImGuiCond, ref System.Nullable`1<Brutal.Numerics.float2>)`
- `void SetNextWindowSize(ref Brutal.Numerics.float2, Brutal.ImGuiApi.ImGuiCond)`
- `void SetTooltip(Brutal.ImGuiApi.ImString)`
- `void TableNextRow(Brutal.ImGuiApi.ImGuiTableRowFlags, float)`
- `void TableSetupColumn(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiTableColumnFlags, float, Brutal.ImGuiApi.ImGuiID)`
- `void Text(Brutal.ImGuiApi.ImString)`
- `void TextColored(ref Brutal.Numerics.float4, Brutal.ImGuiApi.ImString)`
- `void TextDisabled(Brutal.ImGuiApi.ImString)`
- `void TreePop()`

### Brutal.ImGuiApi.ImGuiCol

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiCond

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiHoveredFlags

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiID

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiIOPtr

- `ref bool get_WantCaptureMouse()`

### Brutal.ImGuiApi.ImGuiInputTextCallback

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiInputTextFlags

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiMouseButton

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiSliderFlags

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiTabBarFlags

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiTabItemFlags

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiTableColumnFlags

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiTableFlags

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiTableRowFlags

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiViewportPtr

- `ref Brutal.Numerics.float2 get_Pos()`
- `ref Brutal.Numerics.float2 get_Size()`

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
- `KSA.AtmosphereReference GetAtmosphereReference()`
- `KSA.OrbitView OrbitView`
- `KSA.Rendering.Water.Data.OceanReference GetOceanReference()`
- `double get_MeanRadius()`
- `string get_Id()`
- `void UpdatePerFrameData()`

### KSA.AtmosphereReference

- `KSA.PhysicalAtmosphereReference Physical`

### KSA.Camera

- `Brutal.Numerics.double3 EclToEgo(Brutal.Numerics.double3)`
- `Brutal.Numerics.double3 EgoToEcl(Brutal.Numerics.double3)`
- `Brutal.Numerics.double3 GetForwardEcl()`
- `Brutal.Numerics.double3 GetPositionEgo(KSA.IPosition)`
- `Brutal.Numerics.double3 GetRightEcl()`
- `Brutal.Numerics.double3 GetUpEcl()`
- `Brutal.Numerics.float2 EclToScreen(Brutal.Numerics.double3, bool)`
- `Brutal.Numerics.int2 FramebufferSize`
- `KSA.Celestial get_NearbyCelestial()`
- `KSA.IFollowable get_Following()`
- `KSA.Ray ScreenToEgoRay(Brutal.Numerics.float2)`
- `double CurrentAltitudeKm`
- `double DistanceToNearbyCelestialKm`
- `double DistanceToNearbyCelestialSurfaceMeanKm`
- `double NearbyCelestialTerrainHeight`
- `float GetFieldOfView()`
- `void LookAt(Brutal.Numerics.double3, Brutal.Numerics.double3, Brutal.Numerics.double3)`
- `void SetFollow(KSA.IFollowable, bool, bool, bool)`
- `void Unfollow(bool)`
- `void set_NearbyCelestial(KSA.Celestial)`

### KSA.CameraMode

*referenced as a type only*

### KSA.Celestial

- `Brutal.Numerics.doubleQuat GetCce2Ccf()`
- `KSA.CelestialTemplate get_BodyTemplate()`
- `double GetLatitudeFromCce(Brutal.Numerics.double3)`
- `double GetLongitudeFromCce(Brutal.Numerics.double3)`
- `double GetTerrainHeightFromDirCce(Brutal.Numerics.double3, bool)`

### KSA.CelestialSystem

- `KSA.Astronomical GetIndex(int)`
- `KSA.LookupCollection`1<KSA.Astronomical> get_All()`
- `int get_Count()`

### KSA.CelestialTemplate

- `System.Collections.Generic.List`1<KSA.LocationReference> Locations`

### KSA.CharacterAttachmentReference

*referenced as a type only*

### KSA.CharacterReference

*referenced as a type only*

### KSA.Control

*referenced as a type only*

### KSA.DefaultVehicleSaves

- `KSA.VehicleSave FindSave(string)`

### KSA.DensityReference

- `double op_Implicit(KSA.DensityReference)`

### KSA.DistanceReference

- `double op_Implicit(KSA.DistanceReference)`

### KSA.Double3Ex

- `Brutal.Numerics.double3 Transform(Brutal.Numerics.double3, Brutal.Numerics.double4x4)`
- `Brutal.Numerics.double3 Transform(Brutal.Numerics.double3, Brutal.Numerics.doubleQuat)`

### KSA.GizmosRenderer

- `void DrawLine(Brutal.Numerics.double3, Brutal.Numerics.double3, Brutal.Numerics.float4)`
- `void DrawSphere(Brutal.Numerics.double3, float, Brutal.Numerics.float4)`

### KSA.Gltf2Reference

*referenced as a type only*

### KSA.IFollowable

- `KSA.OrbitView get_OrbitView()`

### KSA.IKeyed

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

### KSA.JobSystems

- `Brutal.Concurrency.Jobs.JobScheduler VehicleSolvers`

### KSA.KittenEva

- `KSA.CharacterReference Character`
- `void .ctor(KSA.CelestialSystem, string, Brutal.Numerics.doubleQuat, Brutal.Numerics.double3, KSA.IParentBody, string, KSA.Part, KSA.Orbit)`

### KSA.KittenRosterData

- `System.Collections.Generic.List`1<KSA.KittenRosterEntryData> Kittens`

### KSA.KittenRosterEntryData

- `string Character`
- `string Name`

### KSA.LandmarkReference

- `bool IsLaunchPad`

### KSA.LocationReference

- `Brutal.Numerics.double3 get_ForwardCcf()`

### KSA.LookupCollection`1

*referenced as a type only*

### KSA.Mod

- `string get_Id()`

### KSA.ModLibrary

- `!!0 Get<1>(string)`

### KSA.ModuleList

- `bool HasAny<1>()`

### KSA.Module`1

*referenced as a type only*

### KSA.Module`1+List

*referenced as a type only*

### KSA.Orbit

- `KSA.Orbit CreateFromStateCci(KSA.IParentBody, KSA.SimTime, Brutal.Numerics.double3, Brutal.Numerics.double3, Brutal.Numerics.byte4)`
- `double get_Apoapsis()`
- `double get_Eccentricity()`
- `double get_Periapsis()`

### KSA.OrbitController

- `double Azimuth`
- `double DistancePower`
- `double Elevation`

### KSA.OrbitView

- `double Azimuth`
- `double DistancePower`
- `double Elevation`

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

- `KSA.ModuleList Modules`
- `KSA.Part get_Root()`
- `KSA.PartTree DeepCopy()`
- `List<KSA.Control> Controls`
- `System.ReadOnlySpan`1<KSA.Part> get_Parts()`
- `int get_Count()`
- `void RecomputeAllDerivedData()`

### KSA.PhysicalAtmosphereReference

- `KSA.DensityReference SeaLevelDensity`
- `KSA.DistanceReference get_Height()`
- `bool IsValid()`
- `double GetAtmosphericDensityAtAltitude(double)`

### KSA.Program

- `KSA.Camera GetMainCamera()`
- `KSA.Camera GetRenderCamera()`
- `KSA.GizmosRenderer GizmosRenderer`
- `KSA.Program get_Instance()`
- `KSA.Vehicle get_ControlledVehicle()`
- `KSA.Viewport get_MainViewport()`
- `System.Collections.Generic.List`1<KSA.Viewport> Viewports`
- `System.ReadOnlySpan`1<KSA.Vehicle> get_VehiclesInFrame()`
- `void SetCameraUbo(KSA.Viewport)`
- `void UpdateShaderData(double, KSA.Viewport)`
- `void set_ControlledVehicle(KSA.Vehicle)`

### KSA.Ray

- `Brutal.Numerics.double3 Direction`

### KSA.Rendering.Water.Data.OceanReference

- `KSA.DensityReference Density`
- `KSA.DistanceReference Level`
- `bool IsValid()`

### KSA.SerializedId

- `string get_Id()`

### KSA.SimSpeed

- `void .ctor(double)`

### KSA.SimStep

- `KSA.SimTime get_NextTime()`
- `double get_DeltaTime()`

### KSA.SimTime

*referenced as a type only*

### KSA.Transform3D

- `Brutal.Numerics.double3 get_PositionEcl()`

### KSA.Universe

- `KSA.CelestialSystem get_CurrentSystem()`
- `KSA.KittenRosterData get_KittenRoster()`
- `KSA.SimStep GetLastSimStep()`
- `KSA.SimTime GetElapsedSimTime()`
- `bool IsPaused()`
- `double get_SimulationSpeed()`
- `void DestroyVehicleFromEvent(KSA.Vehicle, KSA.VehicleDestructionEvent)`
- `void SetSimulationSpeed(KSA.SimSpeed)`

### KSA.Vehicle

- `Brutal.Numerics.double3 get_CenterOfMassAsmb()`
- `Brutal.Numerics.double4x4 GetMatrixAsmb2Ego(Brutal.Numerics.double3)`
- `Brutal.Numerics.doubleQuat get_Asmb2Ego()`
- `Brutal.Numerics.doubleQuat get_Body2Cce()`
- `Brutal.Numerics.float3 get_BoundingBoxHalfExtentsAsmb()`
- `KSA.IParentBody get_Parent()`
- `KSA.PartTree get_Parts()`
- `KSA.Vehicle CreateVehicle(KSA.CelestialSystem, Brutal.Numerics.doubleQuat, Brutal.Numerics.double3, KSA.IParentBody, string, KSA.Part, KSA.Orbit)`
- `KSA.Vehicle get_BubbleLeader()`
- `KSA.VehicleUpdateTask UpdateTask`
- `bool get_IsControllable()`
- `bool get_IsDisposed()`
- `void AddToTask(KSA.VehicleUpdateTask)`
- `void TeleportToLocation(KSA.Celestial, double, double)`
- `void UpdateAfterPartTreeModification()`

### KSA.VehicleDestructionCause

*referenced as a type only*

### KSA.VehicleDestructionEvent

- `KSA.VehicleDestructionCause Cause`
- `float PeakDynamicPressure`
- `float PeakGLoad`
- `void .ctor()`

### KSA.VehicleSave

- `KSA.PartTree Load(KSA.Viewport)`
- `KSA.VehicleSaveData VehicleSaveData`

### KSA.VehicleSaveData

- `string Character`

### KSA.VehicleUpdateTask

*referenced as a type only*

### KSA.Viewport

- `Brutal.Numerics.float2 Position`
- `KSA.Camera BaseCamera`
- `KSA.Camera GetCamera()`
- `KSA.CameraMode Mode`
- `KSA.OrbitController OrbitController`
- `bool IsOffscreen`
- `bool Visible`
- `int get_Height()`
- `int get_Width()`
- `void SetCameraMode(KSA.CameraMode)`

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
