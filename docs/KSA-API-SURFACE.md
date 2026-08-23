# KSA API surface

Every external type and member `KSArmory.dll` binds to, read out of its
metadata tables by `tools/api-surface.sh`. **Generated - do not edit.**

This is the checklist for a KSA update: anything here that changed shape in the new
build is a breaking change for this mod, and anything not here cannot be. See the
`upgrade-ksa` skill, which diffs the decompiled sources against exactly this list.

155 types and 438 members across 7 assemblies.

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
- `Brutal.Numerics.double3 get_UnitX()`
- `Brutal.Numerics.double3 get_UnitY()`
- `Brutal.Numerics.double3 get_UnitZ()`
- `Brutal.Numerics.double3 get_Zero()`
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

- `Brutal.Numerics.double4x4 CreateScale(double)`
- `Brutal.Numerics.double4x4 CreateTranslation(Brutal.Numerics.double3)`
- `Brutal.Numerics.double4x4 op_Multiply(Brutal.Numerics.double4x4, Brutal.Numerics.double4x4)`
- `void .ctor(double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double)`

### Brutal.Numerics.doubleQuat

- `Brutal.Numerics.double3 op_Multiply(Brutal.Numerics.doubleQuat, Brutal.Numerics.double3)`
- `Brutal.Numerics.doubleQuat Concatenate(Brutal.Numerics.doubleQuat, Brutal.Numerics.doubleQuat)`
- `Brutal.Numerics.doubleQuat Conjugate(Brutal.Numerics.doubleQuat)`
- `Brutal.Numerics.doubleQuat CreateFromAxisAngle(Brutal.Numerics.double3, double)`
- `Brutal.Numerics.doubleQuat CreateFromRotationMatrix(Brutal.Numerics.double4x4)`
- `Brutal.Numerics.doubleQuat get_Identity()`
- `Brutal.Numerics.doubleQuat op_Multiply(Brutal.Numerics.doubleQuat, Brutal.Numerics.doubleQuat)`
- `double W`
- `double X`
- `double Y`
- `double Z`

### Brutal.Numerics.float2

- `Brutal.Numerics.float2 op_Addition(Brutal.Numerics.float2, Brutal.Numerics.float2)`
- `Brutal.Numerics.float2 op_Multiply(Brutal.Numerics.float2, float)`
- `float X`
- `float Y`
- `void .ctor(float, float)`

### Brutal.Numerics.float3

- `Brutal.Numerics.float3 op_Multiply(Brutal.Numerics.float3, float)`
- `float X`
- `float Y`
- `float Z`
- `void .ctor(float, float, float)`

### Brutal.Numerics.float4

- `void .ctor(float, float, float, float)`

### Brutal.Numerics.float4x4

- `Brutal.Numerics.float4x4 Pack(ref Brutal.Numerics.double4x4)`

### Brutal.Numerics.int2

- `int X`
- `int Y`

## Brutal.Glfw

### Brutal.GlfwApi.GlfwKeyAction

*referenced as a type only*

### Brutal.GlfwApi.GlfwModifier

*referenced as a type only*

## Brutal.ImGui

### Brutal.ImGuiApi.ImColor8

- `Brutal.ImGuiApi.ImColor8 op_Implicit(ref uint)`
- `void .ctor(byte, byte, byte, byte)`

### Brutal.ImGuiApi.ImDrawFlags

*referenced as a type only*

### Brutal.ImGuiApi.ImDrawListExtensions

- `void AddCircle(Brutal.ImGuiApi.ImDrawListPtr, ref Brutal.Numerics.float2, float, Brutal.ImGuiApi.ImColor8, int, float)`
- `void AddCircleFilled(Brutal.ImGuiApi.ImDrawListPtr, ref Brutal.Numerics.float2, float, Brutal.ImGuiApi.ImColor8, int)`
- `void AddLine(Brutal.ImGuiApi.ImDrawListPtr, ref Brutal.Numerics.float2, ref Brutal.Numerics.float2, Brutal.ImGuiApi.ImColor8, float)`
- `void AddNgon(Brutal.ImGuiApi.ImDrawListPtr, ref Brutal.Numerics.float2, float, Brutal.ImGuiApi.ImColor8, int, float)`
- `void AddNgonFilled(Brutal.ImGuiApi.ImDrawListPtr, ref Brutal.Numerics.float2, float, Brutal.ImGuiApi.ImColor8, int)`
- `void AddRect(Brutal.ImGuiApi.ImDrawListPtr, ref Brutal.Numerics.float2, ref Brutal.Numerics.float2, Brutal.ImGuiApi.ImColor8, float, Brutal.ImGuiApi.ImDrawFlags, float)`
- `void AddRectFilled(Brutal.ImGuiApi.ImDrawListPtr, ref Brutal.Numerics.float2, ref Brutal.Numerics.float2, Brutal.ImGuiApi.ImColor8, float, Brutal.ImGuiApi.ImDrawFlags)`
- `void AddText(Brutal.ImGuiApi.ImDrawListPtr, ref Brutal.Numerics.float2, Brutal.ImGuiApi.ImColor8, Brutal.ImGuiApi.ImString)`
- `void AddTriangleFilled(Brutal.ImGuiApi.ImDrawListPtr, ref Brutal.Numerics.float2, ref Brutal.Numerics.float2, ref Brutal.Numerics.float2, Brutal.ImGuiApi.ImColor8)`

### Brutal.ImGuiApi.ImDrawListPtr

*referenced as a type only*

### Brutal.ImGuiApi.ImGui

- `Brutal.ImGuiApi.ImDrawListPtr GetBackgroundDrawList(Brutal.ImGuiApi.ImGuiViewportPtr)`
- `Brutal.ImGuiApi.ImDrawListPtr GetWindowDrawList()`
- `Brutal.ImGuiApi.ImGuiIOPtr GetIO()`
- `Brutal.ImGuiApi.ImGuiViewportPtr GetMainViewport()`
- `Brutal.Numerics.float2 CalcTextSize(Brutal.ImGuiApi.ImString, bool, float)`
- `Brutal.Numerics.float2 GetContentRegionAvail()`
- `Brutal.Numerics.float2 GetCursorScreenPos()`
- `Brutal.Numerics.float2 GetMousePos()`
- `bool Begin(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiWindowFlags)`
- `bool Begin(Brutal.ImGuiApi.ImString, ref bool, Brutal.ImGuiApi.ImGuiWindowFlags)`
- `bool BeginMainMenuBar()`
- `bool BeginMenu(Brutal.ImGuiApi.ImString, bool)`
- `bool BeginTabBar(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiTabBarFlags)`
- `bool BeginTabItem(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiTabItemFlags)`
- `bool BeginTable(Brutal.ImGuiApi.ImString, int, Brutal.ImGuiApi.ImGuiTableFlags, ref System.Nullable`1<Brutal.Numerics.float2>, float)`
- `bool Button(Brutal.ImGuiApi.ImString, ref System.Nullable`1<Brutal.Numerics.float2>)`
- `bool Checkbox(Brutal.ImGuiApi.ImString, ref bool)`
- `bool CollapsingHeader(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiTreeNodeFlags)`
- `bool InputText(Brutal.ImGuiApi.ImString, System.ReadOnlySpan`1<byte>, Brutal.ImGuiApi.ImGuiInputTextFlags, Brutal.ImGuiApi.ImGuiInputTextCallback, Brutal.Pointers.Ptr)`
- `bool InputTextMultiline(Brutal.ImGuiApi.ImString, System.ReadOnlySpan`1<byte>, ref System.Nullable`1<Brutal.Numerics.float2>, Brutal.ImGuiApi.ImGuiInputTextFlags, Brutal.ImGuiApi.ImGuiInputTextCallback, Brutal.Pointers.Ptr)`
- `bool IsItemHovered(Brutal.ImGuiApi.ImGuiHoveredFlags)`
- `bool IsMouseClicked(Brutal.ImGuiApi.ImGuiMouseButton, bool)`
- `bool IsMouseDown(Brutal.ImGuiApi.ImGuiMouseButton)`
- `bool IsWindowHovered(Brutal.ImGuiApi.ImGuiHoveredFlags)`
- `bool MenuItem(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImString, ref bool, bool)`
- `bool RadioButton(Brutal.ImGuiApi.ImString, bool)`
- `bool Selectable(Brutal.ImGuiApi.ImString, bool, Brutal.ImGuiApi.ImGuiSelectableFlags, ref System.Nullable`1<Brutal.Numerics.float2>)`
- `bool SliderFloat(Brutal.ImGuiApi.ImString, ref float, float, float, Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiSliderFlags)`
- `bool SliderInt(Brutal.ImGuiApi.ImString, ref int, int, int, Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiSliderFlags)`
- `bool SmallButton(Brutal.ImGuiApi.ImString)`
- `bool TableNextColumn()`
- `bool TreeNode(Brutal.ImGuiApi.ImString)`
- `bool TreeNodeEx(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiTreeNodeFlags)`
- `float GetFrameHeight()`
- `float GetTextLineHeight()`
- `uint ColorConvertFloat4ToU32(ref Brutal.Numerics.float4)`
- `void BeginDisabled(bool)`
- `void Dummy(ref Brutal.Numerics.float2)`
- `void End()`
- `void EndDisabled()`
- `void EndMainMenuBar()`
- `void EndMenu()`
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
- `void SeparatorText(Brutal.ImGuiApi.ImString)`
- `void SetNextItemWidth(float)`
- `void SetNextWindowBgAlpha(float)`
- `void SetNextWindowPos(ref Brutal.Numerics.float2, Brutal.ImGuiApi.ImGuiCond, ref System.Nullable`1<Brutal.Numerics.float2>)`
- `void SetNextWindowSize(ref Brutal.Numerics.float2, Brutal.ImGuiApi.ImGuiCond)`
- `void SetTooltip(Brutal.ImGuiApi.ImString)`
- `void TableNextRow(Brutal.ImGuiApi.ImGuiTableRowFlags, float)`
- `void TableSetupColumn(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiTableColumnFlags, float, Brutal.ImGuiApi.ImGuiID)`
- `void Text(Brutal.ImGuiApi.ImString)`
- `void TextColored(ref Brutal.Numerics.float4, Brutal.ImGuiApi.ImString)`
- `void TextDisabled(Brutal.ImGuiApi.ImString)`
- `void TextWrapped(Brutal.ImGuiApi.ImString)`
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

- `ref bool get_KeyShift()`
- `ref bool get_WantCaptureMouse()`

### Brutal.ImGuiApi.ImGuiInputTextCallback

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiInputTextFlags

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiMouseButton

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiSelectableFlags

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

### Brutal.ImGuiApi.ImGuiTreeNodeFlags

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

### KSA.ActiveEnginePerformance

- `float MassFlowRate`
- `float Thrust`

### KSA.Astronomical

- `Brutal.Numerics.double3 GetPositionEcl()`
- `Brutal.Numerics.double3 GetVelocityEcl()`
- `Brutal.Numerics.doubleQuat GetBodyFixed2Ecl()`
- `KSA.AtmosphereReference GetAtmosphereReference()`
- `KSA.OrbitView OrbitView`
- `KSA.Rendering.Water.Data.OceanReference GetOceanReference()`
- `double get_MeanRadius()`
- `string get_Id()`
- `void UpdatePerFrameData()`

### KSA.AtmosphereReference

- `KSA.PhysicalAtmosphereReference Physical`

### KSA.AttitudeControlSystem

*referenced as a type only*

### KSA.BubbleFrame

*referenced as a type only*

### KSA.BubbleOrigin

- `Brutal.Numerics.double3 PositionBub`
- `Brutal.Numerics.double3 VelocityBub`
- `KSA.BubbleFrame BubFrame`
- `KSA.IParentBody Parent`
- `KSA.UniverseTime Time`

### KSA.Camera

- `Brutal.Numerics.double3 EclToEgo(Brutal.Numerics.double3)`
- `Brutal.Numerics.double3 EgoToEcl(Brutal.Numerics.double3)`
- `Brutal.Numerics.double3 GetForwardEcl()`
- `Brutal.Numerics.double3 GetPositionEgo(KSA.IPosition)`
- `Brutal.Numerics.double3 GetRightEcl()`
- `Brutal.Numerics.double3 GetUpEcl()`
- `Brutal.Numerics.double3 GetVelocityEgo(KSA.IVelocity)`
- `Brutal.Numerics.doubleQuat LookAtRotation(Brutal.Numerics.double3, Brutal.Numerics.double3)`
- `Brutal.Numerics.float2 EclToScreen(Brutal.Numerics.double3, bool)`
- `Brutal.Numerics.float2 EgoToScreen(Brutal.Numerics.double3, bool)`
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
- `void SetFieldOfView(float)`
- `void SetFollow(KSA.IFollowable, bool, bool, bool)`
- `void Unfollow(bool)`
- `void set_NearbyCelestial(KSA.Celestial)`

### KSA.CameraMode

*referenced as a type only*

### KSA.CameraReferenceFrame

*referenced as a type only*

### KSA.Celestial

- `Brutal.Numerics.double3 GetDirCcfFromLatLon(double, double)`
- `Brutal.Numerics.double3 GetRotationAxisCce()`
- `Brutal.Numerics.double3 GetSurfacePositionEclFromCce(Brutal.Numerics.double3, bool)`
- `Brutal.Numerics.doubleQuat GetCce2Ccf()`
- `Brutal.Numerics.doubleQuat GetCce2Cci()`
- `Brutal.Numerics.doubleQuat GetCcf2Cce()`
- `Brutal.Numerics.doubleQuat GetCci2Cce()`
- `Brutal.Numerics.doubleQuat GetCci2Ccf()`
- `double GetAngularVelocity()`
- `double GetLatitudeFromCce(Brutal.Numerics.double3)`
- `double GetLongitudeFromCce(Brutal.Numerics.double3)`
- `double GetTerrainHeightFromDirCce(Brutal.Numerics.double3, bool)`
- `double GetTerrainHeightFromDirCcf(Brutal.Numerics.double3, bool)`
- `double get_Mass()`
- `double get_MaxTerrainHeightApprox()`
- `void AddEmitter(Handle<KSA.Rendering.Particles.ParticleUpdateData, KSA.Rendering.Particles.ParticleRenderData>)`
- `void RemoveEmitter(Handle<KSA.Rendering.Particles.ParticleUpdateData, KSA.Rendering.Particles.ParticleRenderData>)`

### KSA.CelestialSystem

- `KSA.Astronomical GetIndex(int)`
- `KSA.LookupCollection`1<KSA.Astronomical> get_All()`
- `int get_Count()`

### KSA.Control

*referenced as a type only*

### KSA.Controller

- `KSA.Camera Camera`

### KSA.Decoupler

- `Connector Connector`
- `bool get_IsEnabled()`
- `void SetIsActive(KSA.Vehicle, bool)`

### KSA.DefaultVehicleSaves

- `KSA.VehicleSave FindSave(string)`

### KSA.DensityReference

- `double op_Implicit(KSA.DensityReference)`

### KSA.DeviceMeshInterleaved

*referenced as a type only*

### KSA.DistanceReference

- `double InMeters()`
- `double op_Implicit(KSA.DistanceReference)`

### KSA.Double3Ex

- `Brutal.Numerics.double3 Transform(Brutal.Numerics.double3, Brutal.Numerics.double4x4)`
- `Brutal.Numerics.double3 Transform(Brutal.Numerics.double3, Brutal.Numerics.doubleQuat)`

### KSA.FixedController

- `Brutal.Numerics.double3 CameraOffset`
- `Brutal.Numerics.double3 CameraRotation`
- `void .ctor(KSA.Camera, string)`
- `void OnFrame(KSA.Viewport, double)`

### KSA.FlightComputer

- `Brutal.Numerics.double3 CustomAttitudeTarget`
- `Brutal.Numerics.float3 AngleTurnaround`
- `Brutal.Numerics.float3 ErrorAngles`
- `Brutal.Numerics.float3 ErrorRates`
- `Brutal.Numerics.float3 RateBit`
- `KSA.ActiveEnginePerformance ActiveEnginePerformanceMax`
- `KSA.FlightComputerAttitudeMode AttitudeMode`
- `KSA.FlightComputerAttitudeTrackTarget AttitudeTrackTarget`
- `KSA.FlightComputerBurnMode BurnMode`
- `KSA.FlightComputerRollMode RollMode`
- `KSA.PerAxisAttitudeControlSystem ActiveControlSystem`
- `KSA.VehicleReferenceFrame AttitudeFrame`
- `float AngleDeadband`
- `void SetAttitudeProfile(KSA.FlightComputerAttitudeProfile)`

### KSA.FlightComputerAttitudeMode

*referenced as a type only*

### KSA.FlightComputerAttitudeProfile

*referenced as a type only*

### KSA.FlightComputerAttitudeTrackTarget

*referenced as a type only*

### KSA.FlightComputerBurnMode

*referenced as a type only*

### KSA.FlightComputerRollMode

*referenced as a type only*

### KSA.GameAudio

- `KSA.Camera GetAudioCamera()`

### KSA.GameSave

- `string get_Id()`

### KSA.GameSaves

- `KSA.GameSave get_Selected()`
- `string get_SaveFolderPath()`
- `void LoadSaveGame(string)`

### KSA.GameSettings

- `GraphicsSettings Graphics`
- `KSA.GameSettings get_Current()`

### KSA.GameSettings+GraphicsSettings

- `bool Particles`
- `bool ScreenSpaceParticles`

### KSA.GenericMeshRenderer

- `void AddInstance(KSA.MeshReference, ref InstanceData, ref PerDrawData, KSA.Viewport, int)`

### KSA.GenericMeshRenderer+InstanceData

- `Brutal.Numerics.float4 Color`
- `Brutal.Numerics.float4x4 ModelMatrix`

### KSA.GenericMeshRenderer+PerDrawData

- `int AlbedoTextureIndex`
- `int NormalTextureIndex`
- `int PbrTextureIndex`

### KSA.GizmosRenderer

- `void DrawLine(Brutal.Numerics.double3, Brutal.Numerics.double3, Brutal.Numerics.float4)`
- `void DrawSphere(Brutal.Numerics.double3, float, Brutal.Numerics.float4)`

### KSA.IChannel

- `bool IsPlaying()`
- `void ApplyParameters()`
- `void SetParameter(KSA.KeyHash, float)`
- `void SetSpatialAudio(KSA.SpatialAudio)`
- `void Stop(bool)`
- `void set_PitchMultiplier(float)`

### KSA.IFollowable

- `KSA.OrbitView get_OrbitView()`

### KSA.IObjectId

- `string get_Id()`

### KSA.IOrbiter

*referenced as a type only*

### KSA.IOrientation

*referenced as a type only*

### KSA.IParentBody

- `Brutal.Numerics.double3 GetAngularVelocityCce()`
- `Brutal.Numerics.doubleQuat GetCce2Cci()`
- `System.Collections.Generic.List`1<KSA.IOrbiter> get_Children()`
- `double get_Mu()`

### KSA.IPosition

- `Brutal.Numerics.double3 GetPositionEcl()`

### KSA.IRadius

*referenced as a type only*

### KSA.IVelocity

- `Brutal.Numerics.double3 GetVelocityEcl()`

### KSA.InputAction

*referenced as a type only*

### KSA.JobSystems

- `Brutal.Concurrency.Jobs.JobScheduler VehicleSolver`

### KSA.KeyHash

- `KSA.KeyHash Make(System.ReadOnlySpan`1<char>)`

### KSA.KittenEva

- `void .ctor(KSA.CelestialSystem, string, Brutal.Numerics.doubleQuat, Brutal.Numerics.double3, KSA.IParentBody, string, KSA.Part, KSA.Orbit)`

### KSA.LookupCollection`1

*referenced as a type only*

### KSA.MeshReference

- `KSA.DeviceMeshInterleaved[] DeviceMeshesInterleaved`

### KSA.MeshViewModule

*referenced as a type only*

### KSA.Mod

- `string get_DirectoryPath()`
- `string get_Id()`

### KSA.ModEntry

- `bool Enabled`
- `string get_Id()`

### KSA.ModLibrary

- `!!0 Get<1>(string)`
- `KSA.Mod Find(string)`
- `KSA.ModManifest Manifest`
- `string get_LocalModsFolderPath()`

### KSA.ModManifest

- `System.Collections.Generic.List`1<KSA.ModEntry> get_Mods()`

### KSA.ModuleBase

- `KSA.Part get_Parent()`

### KSA.ModuleList

- `System.Span`1<!!0> Get<1>()`
- `bool HasAny<1>()`

### KSA.Module`1

*referenced as a type only*

### KSA.Module`1+List

*referenced as a type only*

### KSA.Orbit

- `KSA.Orbit CreateFromStateCci(KSA.IParentBody, KSA.UniverseTime, Brutal.Numerics.double3, Brutal.Numerics.double3, Brutal.Numerics.byte4)`
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
- `void .ctor(KSA.CameraReferenceFrame)`

### KSA.Part

- `Brutal.Numerics.double3 PositionEgo(ref Brutal.Numerics.double4x4)`
- `Brutal.Numerics.double3 PositionVehicleAsmbOffset(Brutal.Numerics.double3)`
- `Brutal.Numerics.double3 get_PositionParentAsmb()`
- `Brutal.Numerics.double3 get_PositionVehicleAsmb()`
- `Brutal.Numerics.double3 get_Scale()`
- `Brutal.Numerics.doubleQuat get_Asmb2ParentAsmb()`
- `Brutal.Numerics.doubleQuat get_Asmb2VehicleAsmb()`
- `KSA.ModuleList Modules`
- `KSA.Part TreeParent`
- `KSA.Part get_FullPart()`
- `System.ReadOnlySpan`1<KSA.Part> get_SubParts()`
- `bool RayCastEgo(ref Brutal.Numerics.double4x4, KSA.Ray, ref double, ref double, ref Brutal.Numerics.double3, ref Brutal.Numerics.double3, ref Brutal.Numerics.double3, ref Brutal.Numerics.double3, ref KSA.Part, ref KSA.Part)`
- `string get_Id()`
- `void ResetCachedPosMatrixValues()`
- `void set_Asmb2ParentAsmb(Brutal.Numerics.doubleQuat)`
- `void set_Asmb2ParentAsmbSafe(Brutal.Numerics.doubleQuat)`
- `void set_PositionParentAsmb(Brutal.Numerics.double3)`
- `void set_PositionParentAsmbSafe(Brutal.Numerics.double3)`
- `void set_Scale(Brutal.Numerics.double3)`

### KSA.Part+Connection

- `KSA.Part OtherPart(KSA.Part)`

### KSA.Part+Connector

- `Connection Connection`
- `KSA.Part get_ConnectionPart()`

### KSA.PartInstance

*referenced as a type only*

### KSA.PartTemplate

- `System.Collections.Generic.List`1<KSA.PartInstance> SubPartInstances`

### KSA.PartTree

- `KSA.ModuleList Modules`
- `KSA.Part get_Root()`
- `KSA.PartTree DeepCopy()`
- `KSA.SequenceList SequenceList`
- `KSA.SequencePerformanceList PerformanceSequences`
- `List<KSA.Control> Controls`
- `System.ReadOnlySpan`1<KSA.Part> get_Parts()`
- `int get_Count()`
- `void RecomputeAllDerivedData()`

### KSA.PerAxisAttitudeControlSystem

- `KSA.AttitudeControlSystem X`
- `KSA.AttitudeControlSystem Y`
- `KSA.AttitudeControlSystem Z`

### KSA.PhysicalAtmosphereReference

- `KSA.DensityReference SeaLevelDensity`
- `KSA.DistanceReference ScaleHeight`
- `KSA.DistanceReference get_Height()`
- `bool IsValid()`
- `double GetAtmosphericDensityAtAltitude(double)`
- `double GetAtmosphericPressure(KSA.Camera)`

### KSA.PlumeTrailEmitterState

- `void .ctor()`

### KSA.Program

- `KSA.Camera GetMainCamera()`
- `KSA.Camera GetRenderCamera()`
- `KSA.GizmosRenderer GizmosRenderer`
- `KSA.Program get_Instance()`
- `KSA.Rendering.Particles.ParticleSystem`2<KSA.Rendering.Particles.ParticleUpdateData, KSA.Rendering.Particles.ParticleRenderData> ParticleSystem`
- `KSA.Vehicle get_ControlledVehicle()`
- `KSA.VehicleEditor Editor`
- `KSA.Viewport get_MainViewport()`
- `System.Collections.Generic.List`1<KSA.Viewport> Viewports`
- `System.ReadOnlySpan`1<KSA.Vehicle> get_VehiclesInFrame()`
- `int ResourceFrameIndex`
- `void SetCameraUbo(KSA.Viewport)`
- `void UpdateShaderData(double, KSA.Viewport)`
- `void set_ControlledVehicle(KSA.Vehicle)`

### KSA.QuaternionEx

- `Brutal.Numerics.doubleQuat Inverse(Brutal.Numerics.doubleQuat)`

### KSA.Ray

- `Brutal.Numerics.double3 Direction`
- `Brutal.Numerics.double3 Origin`

### KSA.Rendering.Lighting.ELightFlags

*referenced as a type only*

### KSA.Rendering.Lighting.Light

- `KSA.Rendering.Lighting.Light CreatePointLight(Brutal.Numerics.double3, float, Brutal.Numerics.float3, float, KSA.Rendering.Lighting.ELightFlags)`

### KSA.Rendering.Lighting.LightDebug

- `KSA.Vehicle Target`
- `System.Collections.Generic.List`1<KSA.Rendering.Lighting.Light> Lights`

### KSA.Rendering.Particles.ParticleEmitterReference

*referenced as a type only*

### KSA.Rendering.Particles.ParticleEmitter`2

*referenced as a type only*

### KSA.Rendering.Particles.ParticleEmitter`2+EmitterContext

*referenced as a type only*

### KSA.Rendering.Particles.ParticleEmitter`2+EmitterShapeInfo

*referenced as a type only*

### KSA.Rendering.Particles.ParticleEmitter`2+Handle

*referenced as a type only*

### KSA.Rendering.Particles.ParticleEmitter`2+ParticleSpawnInfo

*referenced as a type only*

### KSA.Rendering.Particles.ParticleRenderData

*referenced as a type only*

### KSA.Rendering.Particles.ParticleSystem`2

*referenced as a type only*

### KSA.Rendering.Particles.ParticleUpdateData

*referenced as a type only*

### KSA.Rendering.Water.Data.OceanReference

- `KSA.DensityReference Density`
- `KSA.DistanceReference Level`

### KSA.Sequence

- `System.ReadOnlySpan`1<KSA.Part> get_Parts()`
- `bool Activated`

### KSA.SequenceList

- `System.ReadOnlySpan`1<KSA.Sequence> get_Sequences()`
- `void ActivateNextSequence(KSA.Vehicle)`

### KSA.SequencePerformanceList

- `float get_TotalDeltaV()`

### KSA.SerializedId

- `string get_Id()`

### KSA.SimSpeed

- `void .ctor(double)`

### KSA.SimStep

- `KSA.UniverseTime get_NextTime()`
- `double get_DeltaTime()`

### KSA.Situation

*referenced as a type only*

### KSA.SituationEx

- `bool HasAnyContact(KSA.Situation)`
- `bool IsOnRails(KSA.Situation)`

### KSA.SoundBehavior

- `void Play(KSA.SpatialAudio, float, ref KSA.IChannel, bool)`

### KSA.SpatialAudio

- `void .ctor(Brutal.Numerics.double3, Brutal.Numerics.double3, double)`

### KSA.TextureReference

- `int get_BindlessHandle()`

### KSA.Transform3D

- `Brutal.Numerics.double3 get_PositionEcl()`
- `Brutal.Numerics.doubleQuat LocalRotation`
- `void set_PositionEcl(Brutal.Numerics.double3)`

### KSA.Universe

- `KSA.CelestialSystem get_CurrentSystem()`
- `KSA.SimStep GetLastSimStep()`
- `KSA.UniverseTime GetElapsedTime()`
- `bool IsPaused()`
- `bool get_IsAutoWarpActive()`
- `double get_SimulationSpeed()`
- `void AutoWarpStop(bool)`
- `void AutoWarpTo(KSA.UniverseTime, double)`
- `void DestroyVehicleFromEvent(KSA.Vehicle, KSA.VehicleDestructionEvent)`
- `void SetSimulationSpeed(KSA.SimSpeed)`

### KSA.UniverseTime

- `System.Int128 get_Nanoseconds()`
- `double Seconds()`
- `void .ctor(double)`

### KSA.Vehicle

- `Brutal.Numerics.double3 get_AngularAccelerationBody()`
- `Brutal.Numerics.double3 get_BodyRates()`
- `Brutal.Numerics.double3 get_CenterOfMassAsmb()`
- `Brutal.Numerics.double4x4 GetMatrixAsmb2Ego(Brutal.Numerics.double3)`
- `Brutal.Numerics.double4x4 GetMatrixAsmb2Ego(KSA.Camera)`
- `Brutal.Numerics.doubleQuat get_Asmb2Ego()`
- `Brutal.Numerics.doubleQuat get_Body2Cce()`
- `Brutal.Numerics.doubleQuat get_Ctrl2Body()`
- `Brutal.Numerics.float3 get_BoundingBoxHalfExtentsAsmb()`
- `KSA.FlightComputer get_FlightComputer()`
- `KSA.IParentBody get_Parent()`
- `KSA.Part get_ControlPart()`
- `KSA.PartTree get_Parts()`
- `KSA.Situation get_Situation()`
- `KSA.Vehicle CreateVehicle(KSA.CelestialSystem, Brutal.Numerics.doubleQuat, Brutal.Numerics.double3, KSA.IParentBody, string, KSA.Part, KSA.Orbit)`
- `KSA.Vehicle get_BubbleLeader()`
- `bool IsAnyEnginePropellantAvailable()`
- `bool get_HasPhysicsBubble()`
- `bool get_IsControllable()`
- `bool get_IsDisposed()`
- `float GetManualThrottle()`
- `float GetMinThrottle()`
- `float get_PropellantMass()`
- `float get_TotalMass()`
- `void PrepareWorker(KSA.SimStep)`
- `void ProcessInput(KSA.InputAction, Brutal.GlfwApi.GlfwKeyAction, Brutal.GlfwApi.GlfwModifier)`
- `void SetControlPart(KSA.Part, Connector)`
- `void TeleportToLocation(KSA.Celestial, double, double)`
- `void UpdateAfterPartTreeModification()`

### KSA.VehicleDestructionCause

*referenced as a type only*

### KSA.VehicleDestructionEvent

- `KSA.VehicleDestructionCause Cause`
- `float PeakDynamicPressure`
- `float PeakGLoad`
- `void .ctor()`

### KSA.VehicleEditor

*referenced as a type only*

### KSA.VehicleReferenceFrame

*referenced as a type only*

### KSA.VehicleReferenceFrameEx

- `Brutal.Numerics.double3 QuaternionToEulerAngles(KSA.VehicleReferenceFrame, Brutal.Numerics.doubleQuat)`
- `Brutal.Numerics.doubleQuat GetEclBody2Cci(Brutal.Numerics.doubleQuat)`

### KSA.VehicleSave

- `KSA.PartTree Load(KSA.Viewport)`
- `KSA.VehicleSaveData VehicleSaveData`

### KSA.VehicleSaveData

- `string Character`

### KSA.Viewport

- `Brutal.Numerics.float2 Position`
- `KSA.Camera BaseCamera`
- `KSA.Camera GetCamera()`
- `KSA.CameraMode Mode`
- `KSA.FixedController FixedController`
- `KSA.OrbitController OrbitController`
- `bool IsOffscreen`
- `bool Visible`
- `int Index`
- `int get_Height()`
- `int get_Width()`
- `void SetCameraMode(KSA.CameraMode)`

### KSA.VolumetricTrailRenderer

- `Brutal.Numerics.float4 DebugTrailColor`
- `float ErosionEdgeSharpness`
- `float ErosionMaxDepth`
- `float SkyAmbientBrightness`
- `int SelfShadowStepCount`
- `void SubmitEmitter(KSA.PlumeTrailEmitterState, KSA.Celestial, Brutal.Numerics.double3, float, float, bool)`

## StarMap.API

### StarMap.API.StarMapAfterGuiAttribute

- `void .ctor()`

### StarMap.API.StarMapAfterOnFrameAttribute

- `void .ctor()`

### StarMap.API.StarMapAllModsLoadedAttribute

- `void .ctor()`

### StarMap.API.StarMapBeforeGuiAttribute

- `void .ctor()`

### StarMap.API.StarMapImmediateLoadAttribute

- `void .ctor()`

### StarMap.API.StarMapModAttribute

- `void .ctor()`

### StarMap.API.StarMapUnloadAttribute

- `void .ctor()`
