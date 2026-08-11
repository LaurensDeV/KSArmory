# KSA API surface

Every external type and member `KSArmory.dll` binds to, read out of its
metadata tables by `tools/api-surface.sh`. **Generated - do not edit.**

This is the checklist for a KSA update: anything here that changed shape in the new
build is a breaking change for this mod, and anything not here cannot be. See the
`upgrade-ksa` skill, which diffs the decompiled sources against exactly this list.

114 types and 315 members across 6 assemblies.

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

*referenced as a type only*

### Brutal.Numerics.doubleQuat

- `Brutal.Numerics.double3 op_Multiply(Brutal.Numerics.doubleQuat, Brutal.Numerics.double3)`
- `Brutal.Numerics.doubleQuat Conjugate(Brutal.Numerics.doubleQuat)`
- `Brutal.Numerics.doubleQuat CreateFromAxisAngle(Brutal.Numerics.double3, double)`
- `Brutal.Numerics.doubleQuat get_Identity()`
- `Brutal.Numerics.doubleQuat op_Multiply(Brutal.Numerics.doubleQuat, Brutal.Numerics.doubleQuat)`

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

### Brutal.Numerics.float4

- `void .ctor(float, float, float, float)`

### Brutal.Numerics.int2

- `int X`
- `int Y`

## Brutal.ImGui

### Brutal.ImGuiApi.ImColor8

- `void .ctor(byte, byte, byte, byte)`

### Brutal.ImGuiApi.ImDrawListExtensions

- `void AddCircle(Brutal.ImGuiApi.ImDrawListPtr, ref Brutal.Numerics.float2, float, Brutal.ImGuiApi.ImColor8, int, float)`
- `void AddCircleFilled(Brutal.ImGuiApi.ImDrawListPtr, ref Brutal.Numerics.float2, float, Brutal.ImGuiApi.ImColor8, int)`
- `void AddLine(Brutal.ImGuiApi.ImDrawListPtr, ref Brutal.Numerics.float2, ref Brutal.Numerics.float2, Brutal.ImGuiApi.ImColor8, float)`
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
- `bool SliderFloat(Brutal.ImGuiApi.ImString, ref float, float, float, Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiSliderFlags)`
- `bool SliderInt(Brutal.ImGuiApi.ImString, ref int, int, int, Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiSliderFlags)`
- `bool SmallButton(Brutal.ImGuiApi.ImString)`
- `bool TableNextColumn()`
- `bool TreeNode(Brutal.ImGuiApi.ImString)`
- `float GetFrameHeight()`
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

- `Brutal.Numerics.double3 GetSurfacePositionEclFromCce(Brutal.Numerics.double3, bool)`
- `Brutal.Numerics.doubleQuat GetCce2Ccf()`
- `double GetAngularVelocity()`
- `double GetLatitudeFromCce(Brutal.Numerics.double3)`
- `double GetLongitudeFromCce(Brutal.Numerics.double3)`
- `double GetTerrainHeightFromDirCce(Brutal.Numerics.double3, bool)`
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

### KSA.DefaultVehicleSaves

- `KSA.VehicleSave FindSave(string)`

### KSA.DensityReference

- `double op_Implicit(KSA.DensityReference)`

### KSA.DistanceReference

- `double op_Implicit(KSA.DistanceReference)`

### KSA.Double3Ex

- `Brutal.Numerics.double3 Transform(Brutal.Numerics.double3, Brutal.Numerics.double4x4)`
- `Brutal.Numerics.double3 Transform(Brutal.Numerics.double3, Brutal.Numerics.doubleQuat)`

### KSA.FixedController

- `Brutal.Numerics.double3 CameraOffset`
- `Brutal.Numerics.double3 CameraRotation`
- `void .ctor(KSA.Camera, string)`
- `void OnFrame(KSA.Viewport, double)`

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

### KSA.JobSystems

- `Brutal.Concurrency.Jobs.JobScheduler VehicleSolver`

### KSA.KeyHash

- `KSA.KeyHash Make(System.ReadOnlySpan`1<char>)`

### KSA.KittenEva

- `void .ctor(KSA.CelestialSystem, string, Brutal.Numerics.doubleQuat, Brutal.Numerics.double3, KSA.IParentBody, string, KSA.Part, KSA.Orbit)`

### KSA.LookupCollection`1

*referenced as a type only*

### KSA.MeshViewModule

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
- `System.ReadOnlySpan`1<KSA.Part> get_SubParts()`
- `bool RayCastEgo(ref Brutal.Numerics.double4x4, KSA.Ray, ref double, ref double, ref Brutal.Numerics.double3, ref Brutal.Numerics.double3, ref Brutal.Numerics.double3, ref Brutal.Numerics.double3, ref KSA.Part, ref KSA.Part)`
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
- `double GetAtmosphericPressure(KSA.Camera)`

### KSA.PhysicsBubble

*referenced as a type only*

### KSA.Program

- `KSA.Camera GetMainCamera()`
- `KSA.Camera GetRenderCamera()`
- `KSA.GizmosRenderer GizmosRenderer`
- `KSA.Program get_Instance()`
- `KSA.Rendering.Particles.ParticleSystem`2<KSA.Rendering.Particles.ParticleUpdateData, KSA.Rendering.Particles.ParticleRenderData> ParticleSystem`
- `KSA.Vehicle get_ControlledVehicle()`
- `KSA.Viewport get_MainViewport()`
- `System.Collections.Generic.List`1<KSA.Viewport> Viewports`
- `System.ReadOnlySpan`1<KSA.Vehicle> get_VehiclesInFrame()`
- `void SetCameraUbo(KSA.Viewport)`
- `void UpdateShaderData(double, KSA.Viewport)`
- `void set_ControlledVehicle(KSA.Vehicle)`

### KSA.Ray

- `Brutal.Numerics.double3 Direction`
- `Brutal.Numerics.double3 Origin`

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
- `bool IsValid()`

### KSA.SimSpeed

- `void .ctor(double)`

### KSA.SimStep

- `KSA.UniverseTime get_NextTime()`
- `double get_DeltaTime()`

### KSA.Situation

*referenced as a type only*

### KSA.SituationEx

- `bool IsOnRails(KSA.Situation)`

### KSA.SoundBehavior

- `void Play(KSA.SpatialAudio, float, ref KSA.IChannel, bool)`

### KSA.SpatialAudio

- `void .ctor(Brutal.Numerics.double3, Brutal.Numerics.double3, double)`

### KSA.Transform3D

- `Brutal.Numerics.double3 get_PositionEcl()`
- `Brutal.Numerics.doubleQuat LocalRotation`
- `void set_PositionEcl(Brutal.Numerics.double3)`

### KSA.Universe

- `KSA.CelestialSystem get_CurrentSystem()`
- `KSA.SimStep GetLastSimStep()`
- `KSA.UniverseTime GetElapsedTime()`
- `bool IsPaused()`
- `double get_SimulationSpeed()`
- `void DestroyVehicleFromEvent(KSA.Vehicle, KSA.VehicleDestructionEvent)`
- `void SetSimulationSpeed(KSA.SimSpeed)`

### KSA.UniverseTime

*referenced as a type only*

### KSA.Vehicle

- `Brutal.Numerics.double3 get_CenterOfMassAsmb()`
- `Brutal.Numerics.double4x4 GetMatrixAsmb2Ego(Brutal.Numerics.double3)`
- `Brutal.Numerics.double4x4 GetMatrixAsmb2Ego(KSA.Camera)`
- `Brutal.Numerics.doubleQuat get_Asmb2Ego()`
- `Brutal.Numerics.doubleQuat get_Body2Cce()`
- `Brutal.Numerics.float3 get_BoundingBoxHalfExtentsAsmb()`
- `KSA.IParentBody get_Parent()`
- `KSA.PartTree get_Parts()`
- `KSA.PhysicsBubble get_PhysicsBubble()`
- `KSA.Situation get_Situation()`
- `KSA.Vehicle CreateVehicle(KSA.CelestialSystem, Brutal.Numerics.doubleQuat, Brutal.Numerics.double3, KSA.IParentBody, string, KSA.Part, KSA.Orbit)`
- `KSA.Vehicle get_BubbleLeader()`
- `bool get_IsControllable()`
- `bool get_IsDisposed()`
- `void AddToBubble(KSA.PhysicsBubble)`
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
