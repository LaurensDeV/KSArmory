# KSA API surface

Every external type and member `KSArmory.dll` binds to, read out of its
metadata tables by `tools/api-surface.sh`. **Generated - do not edit.**

This is the checklist for a KSA update: anything here that changed shape in the new
build is a breaking change for this mod, and anything not here cannot be. See the
`upgrade-ksa` skill, which diffs the decompiled sources against exactly this list.

224 types and 621 members across 10 assemblies.

## Brutal.Concurrency

### Brutal.Concurrency.Jobs.JobScheduler

- `double GetMaxLastTickTime()`
- `void Wait()`

## Brutal.Core.Common

### Brutal.ByteSize

- `Brutal.ByteSize op_Multiply(int, Brutal.ByteSize)`

### Brutal.ByteSize32

- `Brutal.ByteSize32 op_Explicit(int)`
- `Brutal.ByteSize32 op_Implicit(Brutal.ByteSize)`

### Brutal.ByteSize64

- `Brutal.ByteSize64 op_Explicit(int)`
- `Brutal.ByteSize64 op_Explicit(ulong)`

### Brutal.Pointers.Ptr

*referenced as a type only*

## Brutal.Core.Numerics

### Brutal.Numerics.byte4

- `void .ctor(byte, byte, byte, byte)`

### Brutal.Numerics.double2

- `double X`
- `double Y`

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

### Brutal.Numerics.double4

- `double W`
- `double X`
- `double Y`

### Brutal.Numerics.double4x4

- `Brutal.Numerics.double4x4 Unpack(ref Brutal.Numerics.float4x4)`
- `double get_M11()`
- `double get_M12()`
- `double get_M13()`
- `double get_M14()`
- `double get_M21()`
- `double get_M22()`
- `double get_M23()`
- `double get_M24()`
- `double get_M31()`
- `double get_M32()`
- `double get_M33()`
- `double get_M34()`
- `double get_M41()`
- `double get_M42()`
- `double get_M43()`
- `double get_M44()`
- `void .ctor(double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double)`
- `void set_M41(double)`
- `void set_M42(double)`
- `void set_M43(double)`
- `void set_M44(double)`

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
- `float X`
- `float Y`
- `void .ctor(float, float)`

### Brutal.Numerics.float3

- `float X`
- `float Y`
- `float Z`
- `void .ctor(float, float, float)`

### Brutal.Numerics.float4

- `Brutal.Numerics.float4 get_Zero()`
- `float W`
- `float X`
- `float Y`
- `float Z`
- `void .ctor(float, float, float, float)`

### Brutal.Numerics.float4x4

- `Brutal.Numerics.float4x4 Pack(ref Brutal.Numerics.double4x4)`
- `Brutal.Numerics.float4x4 get_Identity()`

### Brutal.Numerics.int2

- `int X`
- `int Y`

## Brutal.Glfw

### Brutal.GlfwApi.GlfwButtonAction

*referenced as a type only*

### Brutal.GlfwApi.GlfwCursorMode

*referenced as a type only*

### Brutal.GlfwApi.GlfwKeyAction

*referenced as a type only*

### Brutal.GlfwApi.GlfwModifier

*referenced as a type only*

### Brutal.GlfwApi.GlfwMouseButton

*referenced as a type only*

### Brutal.GlfwApi.GlfwWindow

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
- `void PopClipRect(Brutal.ImGuiApi.ImDrawListPtr)`
- `void PushClipRect(Brutal.ImGuiApi.ImDrawListPtr, ref Brutal.Numerics.float2, ref Brutal.Numerics.float2, bool)`

### Brutal.ImGuiApi.ImDrawListPtr

*referenced as a type only*

### Brutal.ImGuiApi.ImGui

- `Brutal.ImGuiApi.ImDrawListPtr GetBackgroundDrawList(Brutal.ImGuiApi.ImGuiViewportPtr)`
- `Brutal.ImGuiApi.ImDrawListPtr GetForegroundDrawList(Brutal.ImGuiApi.ImGuiViewportPtr)`
- `Brutal.ImGuiApi.ImDrawListPtr GetWindowDrawList()`
- `Brutal.ImGuiApi.ImGuiIOPtr GetIO()`
- `Brutal.ImGuiApi.ImGuiViewportPtr FindViewportByID(Brutal.ImGuiApi.ImGuiID)`
- `Brutal.ImGuiApi.ImGuiViewportPtr GetMainViewport()`
- `Brutal.Numerics.float2 CalcTextSize(Brutal.ImGuiApi.ImString, bool, float)`
- `Brutal.Numerics.float2 GetContentRegionAvail()`
- `Brutal.Numerics.float2 GetCursorScreenPos()`
- `Brutal.Numerics.float2 GetItemRectMax()`
- `Brutal.Numerics.float2 GetItemRectMin()`
- `Brutal.Numerics.float2 GetMousePos()`
- `bool Begin(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiWindowFlags)`
- `bool Begin(Brutal.ImGuiApi.ImString, ref bool, Brutal.ImGuiApi.ImGuiWindowFlags)`
- `bool BeginItemTooltip()`
- `bool BeginMainMenuBar()`
- `bool BeginMenu(Brutal.ImGuiApi.ImString, bool)`
- `bool BeginPopup(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiWindowFlags)`
- `bool BeginTabBar(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiTabBarFlags)`
- `bool BeginTabItem(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiTabItemFlags)`
- `bool BeginTable(Brutal.ImGuiApi.ImString, int, Brutal.ImGuiApi.ImGuiTableFlags, ref System.Nullable`1<Brutal.Numerics.float2>, float)`
- `bool Button(Brutal.ImGuiApi.ImString, ref System.Nullable`1<Brutal.Numerics.float2>)`
- `bool Checkbox(Brutal.ImGuiApi.ImString, ref bool)`
- `bool CollapsingHeader(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiTreeNodeFlags)`
- `bool InputDouble(Brutal.ImGuiApi.ImString, ref double, double, double, Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiInputTextFlags)`
- `bool InputText(Brutal.ImGuiApi.ImString, System.ReadOnlySpan`1<byte>, Brutal.ImGuiApi.ImGuiInputTextFlags, Brutal.ImGuiApi.ImGuiInputTextCallback, Brutal.Pointers.Ptr)`
- `bool InputTextMultiline(Brutal.ImGuiApi.ImString, System.ReadOnlySpan`1<byte>, ref System.Nullable`1<Brutal.Numerics.float2>, Brutal.ImGuiApi.ImGuiInputTextFlags, Brutal.ImGuiApi.ImGuiInputTextCallback, Brutal.Pointers.Ptr)`
- `bool InputTextWithHint(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImString, System.ReadOnlySpan`1<byte>, Brutal.ImGuiApi.ImGuiInputTextFlags, Brutal.ImGuiApi.ImGuiInputTextCallback, Brutal.Pointers.Ptr)`
- `bool IsItemClicked(Brutal.ImGuiApi.ImGuiMouseButton)`
- `bool IsItemHovered(Brutal.ImGuiApi.ImGuiHoveredFlags)`
- `bool IsMouseClicked(Brutal.ImGuiApi.ImGuiMouseButton, bool)`
- `bool IsMouseDown(Brutal.ImGuiApi.ImGuiMouseButton)`
- `bool IsWindowAppearing()`
- `bool IsWindowHovered(Brutal.ImGuiApi.ImGuiHoveredFlags)`
- `bool MenuItem(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImString, bool, bool)`
- `bool MenuItem(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImString, ref bool, bool)`
- `bool RadioButton(Brutal.ImGuiApi.ImString, bool)`
- `bool Selectable(Brutal.ImGuiApi.ImString, bool, Brutal.ImGuiApi.ImGuiSelectableFlags, ref System.Nullable`1<Brutal.Numerics.float2>)`
- `bool SliderFloat(Brutal.ImGuiApi.ImString, ref float, float, float, Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiSliderFlags)`
- `bool SliderInt(Brutal.ImGuiApi.ImString, ref int, int, int, Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiSliderFlags)`
- `bool SmallButton(Brutal.ImGuiApi.ImString)`
- `bool TableNextColumn()`
- `bool TreeNode(Brutal.ImGuiApi.ImString)`
- `bool TreeNodeEx(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiTreeNodeFlags)`
- `float GetFontSize()`
- `float GetFrameHeight()`
- `float GetTextLineHeight()`
- `uint ColorConvertFloat4ToU32(ref Brutal.Numerics.float4)`
- `void BeginDisabled(bool)`
- `void CloseCurrentPopup()`
- `void Dummy(ref Brutal.Numerics.float2)`
- `void End()`
- `void EndDisabled()`
- `void EndMainMenuBar()`
- `void EndMenu()`
- `void EndPopup()`
- `void EndTabBar()`
- `void EndTabItem()`
- `void EndTable()`
- `void EndTooltip()`
- `void NewLine()`
- `void OpenPopup(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiPopupFlags)`
- `void PopID()`
- `void PopStyleColor(int)`
- `void PopTextWrapPos()`
- `void ProgressBar(float, ref System.Nullable`1<Brutal.Numerics.float2>, Brutal.ImGuiApi.ImString)`
- `void PushID(int)`
- `void PushStyleColor(Brutal.ImGuiApi.ImGuiCol, ref Brutal.Numerics.float4)`
- `void PushTextWrapPos(float)`
- `void SameLine(float, float)`
- `void Separator()`
- `void SeparatorText(Brutal.ImGuiApi.ImString)`
- `void SetKeyboardFocusHere(int)`
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

- `Brutal.ImGuiApi.ImGuiID op_Implicit(uint)`

### Brutal.ImGuiApi.ImGuiIOPtr

- `ref Brutal.Numerics.float2 get_MousePos()`
- `ref bool get_KeyShift()`
- `ref bool get_WantCaptureKeyboard()`
- `ref bool get_WantCaptureMouse()`

### Brutal.ImGuiApi.ImGuiInputTextCallback

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiInputTextFlags

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiMouseButton

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiPopupFlags

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

- `bool IsNull()`
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

## Brutal.ShaderC

### Brutal.ShaderCApi.CompileOptions

*referenced as a type only*

## Brutal.Vulkan

### Brutal.VulkanApi.CommandBuffer

*referenced as a type only*

### Brutal.VulkanApi.VkAccessFlags2

*referenced as a type only*

### Brutal.VulkanApi.VkBuffer

*referenced as a type only*

### Brutal.VulkanApi.VkBufferMemoryBarrier2

*referenced as a type only*

### Brutal.VulkanApi.VkDescriptorSet

*referenced as a type only*

### Brutal.VulkanApi.VkDescriptorSetLayout

*referenced as a type only*

### Brutal.VulkanApi.VkDeviceExtensions

- `void Dispatch<1>(!!0, int, int, int)`

### Brutal.VulkanApi.VkExtent2D

- `int Height`
- `int Width`
- `void .ctor(int, int)`

### Brutal.VulkanApi.VkFormat

*referenced as a type only*

### Brutal.VulkanApi.VkImageMemoryBarrier2

*referenced as a type only*

### Brutal.VulkanApi.VkImageView

*referenced as a type only*

### Brutal.VulkanApi.VkMemoryBarrier2

- `Brutal.VulkanApi.VkAccessFlags2 DstAccessMask`
- `Brutal.VulkanApi.VkAccessFlags2 SrcAccessMask`
- `Brutal.VulkanApi.VkPipelineStageFlags2 DstStageMask`
- `Brutal.VulkanApi.VkPipelineStageFlags2 SrcStageMask`
- `void .ctor()`

### Brutal.VulkanApi.VkPipelineStageFlags2

*referenced as a type only*

### Brutal.VulkanApi.VkPushConstantRange

- `Brutal.ByteSize32 Offset`
- `Brutal.ByteSize32 Size`
- `Brutal.VulkanApi.VkShaderStageFlags StageFlags`
- `void .ctor()`

### Brutal.VulkanApi.VkSampler

*referenced as a type only*

### Brutal.VulkanApi.VkShaderStageFlags

*referenced as a type only*

### Brutal.VulkanApi.VkSpecializationInfo

*referenced as a type only*

### Brutal.VulkanApi.VkSpecializationMapEntry

- `Brutal.ByteSize32 Offset`
- `Brutal.ByteSize64 Size`
- `int ConstantID`
- `void .ctor()`

## Brutal.Vulkan.Abstractions

### Brutal.VulkanApi.Abstractions.BufferEx

- `Brutal.VulkanApi.VkBuffer get_VkBuffer()`

## KSA

### KSA.ActiveEnginePerformance

- `float MassFlowRate`
- `float Thrust`
- `float get_ExhaustVelocity()`

### KSA.Astronomical

- `Brutal.Numerics.double3 GetPositionEcl()`
- `Brutal.Numerics.double3 GetVelocityEcl()`
- `Brutal.Numerics.doubleQuat GetBodyFixed2Ecl()`
- `KSA.AtmosphereReference GetAtmosphereReference()`
- `KSA.KeyHash get_Hash()`
- `KSA.OrbitView OrbitView`
- `KSA.Rendering.Water.Data.OceanReference GetOceanReference()`
- `double get_MaxTerrainRadius()`
- `double get_MeanRadius()`
- `string get_Id()`
- `void UpdatePerFrameData()`

### KSA.Atmosphere.Rendering.CloudRenderer

- `KSA.Rendering.RenderImage GetLowResolutionCloudColorTarget()`
- `KSA.Rendering.RenderImage GetLowResolutionCloudDistanceTarget()`

### KSA.Atmosphere.Rendering.CloudShadowRenderData

- `Brutal.ByteSize DynamicUboStride`

### KSA.Atmosphere.Rendering.CloudShadowsRenderer

*referenced as a type only*

### KSA.AtmosphereReference

- `KSA.PhysicalAtmosphereReference Physical`

### KSA.AtmosphereRenderer

- `KSA.Rendering.RenderImage get_AerialPerspectiveColorRgbTransmittanceR()`
- `KSA.Rendering.RenderImage get_AerialPerspectiveRange()`
- `KSA.Rendering.RenderImage get_AerialPerspectiveTransmittanceGb()`

### KSA.AttitudeControlSystem

*referenced as a type only*

### KSA.BoundingBoxCdA

- `Brutal.Numerics.float3 Negative`
- `Brutal.Numerics.float3 Positive`

### KSA.BubbleFrame

*referenced as a type only*

### KSA.BubbleFrameEx

- `bool IsCcf(KSA.BubbleFrame)`

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
- `Brutal.Numerics.double4 EgoToClipDouble(Brutal.Numerics.double3)`
- `Brutal.Numerics.doubleQuat LookAtRotation(Brutal.Numerics.double3, Brutal.Numerics.double3)`
- `Brutal.Numerics.float2 EclToScreen(Brutal.Numerics.double3, bool)`
- `Brutal.Numerics.float2 EgoToScreen(Brutal.Numerics.double3, bool)`
- `Brutal.Numerics.int2 FramebufferSize`
- `KSA.Celestial get_NearbyCelestial()`
- `KSA.IFollowable get_Following()`
- `KSA.Ray ScreenToEgoRay(Brutal.Numerics.float2)`
- `KSA.ViewProjection get_MVP()`
- `KSA.ViewProjection get_VPInv()`
- `double CurrentAltitudeKm`
- `double DistanceToNearbyCelestialKm`
- `double DistanceToNearbyCelestialSurfaceMeanKm`
- `double GetObjectDiameterPixels(double, double)`
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
- `Brutal.Numerics.doubleQuat GetCcf2Cci()`
- `Brutal.Numerics.doubleQuat GetCci2Cce()`
- `Brutal.Numerics.doubleQuat GetCci2Ccf()`
- `KSA.IParentBody get_Parent()`
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

### KSA.Constants

- `string get_DocumentsFolderPath()`

### KSA.ConstraintSim

- `KSA.ShapesUnlock UnlockShapesBlocking()`

### KSA.Control

*referenced as a type only*

### KSA.Controller

- `Brutal.GlfwApi.GlfwCursorMode GetCursorMode()`
- `KSA.Camera Camera`
- `bool IsMouseDrag()`
- `bool OnCursorPos(Brutal.GlfwApi.GlfwWindow, Brutal.Numerics.double2)`
- `bool OnMouseButton(Brutal.GlfwApi.GlfwWindow, Brutal.GlfwApi.GlfwMouseButton, Brutal.GlfwApi.GlfwButtonAction, Brutal.GlfwApi.GlfwModifier)`
- `bool OnScroll(Brutal.GlfwApi.GlfwWindow, Brutal.Numerics.double2)`

### KSA.CrewDisposition

*referenced as a type only*

### KSA.Decoupler

- `Connector Connector`
- `bool get_IsEnabled()`
- `void SetIsActive(KSA.Vehicle, bool)`

### KSA.DefaultVehicleSaves

- `KSA.VehicleSave FindSave(string)`

### KSA.DensityReference

- `double op_Implicit(KSA.DensityReference)`

### KSA.DistanceReference

- `double InMeters()`
- `double op_Implicit(KSA.DistanceReference)`

### KSA.Double3Ex

- `Brutal.Numerics.double3 Transform(Brutal.Numerics.double3, Brutal.Numerics.double4x4)`
- `Brutal.Numerics.double3 Transform(Brutal.Numerics.double3, Brutal.Numerics.doubleQuat)`

### KSA.ExplosionContext

- `KSA.BubbleOrigin Anchor`
- `float AmbientPressurePa`
- `float IntensityJ`

### KSA.ExplosionSystem

- `void SpawnPreset(string, ref KSA.ExplosionContext)`

### KSA.FileReference

- `string get_ModPath()`

### KSA.FixedController

- `Brutal.Numerics.double3 CameraOffset`
- `Brutal.Numerics.double3 CameraRotation`
- `void .ctor(KSA.Camera, string)`
- `void OnFrame(KSA.IViewport, double)`

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
- `void SetManualThrustMode(KSA.FlightComputerManualThrustMode)`

### KSA.FlightComputerAttitudeMode

*referenced as a type only*

### KSA.FlightComputerAttitudeProfile

*referenced as a type only*

### KSA.FlightComputerAttitudeTrackTarget

*referenced as a type only*

### KSA.FlightComputerBurnMode

*referenced as a type only*

### KSA.FlightComputerManualThrustMode

*referenced as a type only*

### KSA.FlightComputerOutput

- `bool AnyActuatorCommanded`

### KSA.FlightComputerRollMode

*referenced as a type only*

### KSA.FlightPlan

- `KSA.UniverseTime get_ExpiryGameTime()`

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
- `bool ShowClouds()`

### KSA.GameSettings+GraphicsSettings

- `bool Particles`

### KSA.GizmosRenderer

- `void DrawLine(Brutal.Numerics.double3, Brutal.Numerics.double3, Brutal.Numerics.float4)`
- `void DrawSphere(Brutal.Numerics.double3, float, Brutal.Numerics.float4)`

### KSA.GpuTextureSystem

- `Brutal.VulkanApi.VkDescriptorSet get_DescriptorSet()`
- `Brutal.VulkanApi.VkDescriptorSetLayout get_Layout()`

### KSA.IChannel

- `bool IsPlaying()`
- `void ApplyParameters()`
- `void SetParameter(KSA.KeyHash, float)`
- `void SetPaused(bool)`
- `void SetSpatialAudio(KSA.SpatialAudio)`
- `void Stop(bool)`
- `void set_PitchMultiplier(float)`

### KSA.IFollowable

- `KSA.OrbitView get_OrbitView()`

### KSA.IGameViewport

- `KSA.Camera get_BaseCamera()`
- `KSA.FixedController get_FixedController()`
- `KSA.OrbitController get_OrbitController()`
- `uint get_ImGuiId()`
- `void SetCameraMode(KSA.CameraMode)`
- `void SetName(string)`

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

### KSA.IViewport

- `Brutal.Numerics.float2 get_Position()`
- `KSA.Camera GetCamera()`
- `KSA.CameraMode get_Mode()`
- `KSA.Rendering.RenderTarget get_OffscreenTarget()`
- `KSA.ViewportType get_Type()`
- `bool get_Visible()`
- `int get_Height()`
- `int get_ShaderSlot()`
- `int get_Width()`
- `string get_Name()`
- `void SetVisible(bool)`

### KSA.InputAction

*referenced as a type only*

### KSA.JobSystems

- `Brutal.Concurrency.Jobs.JobScheduler VehicleSolver`

### KSA.KittenEva

- `void .ctor(KSA.CelestialSystem, string, Brutal.Numerics.doubleQuat, Brutal.Numerics.double3, KSA.IParentBody, string, KSA.Part, KSA.Orbit)`

### KSA.LookupCollection`1

*referenced as a type only*

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
- `bool TryGet<1>(string, ref !!0)`
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

- `KSA.IParentBody get_Parent()`
- `KSA.Orbit CreateFromStateCci(KSA.IParentBody, KSA.UniverseTime, Brutal.Numerics.double3, Brutal.Numerics.double3, Brutal.Numerics.byte4)`
- `double get_Apoapsis()`
- `double get_Eccentricity()`
- `double get_Periapsis()`
- `ref KSA.StateVectors get_StateVectors()`

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
- `KSA.PartTemplate Template`
- `System.ReadOnlySpan`1<KSA.Part> get_SubParts()`
- `System.ValueTuple`2<Brutal.Numerics.double3, Brutal.Numerics.double3> get_BoundingBoxVehicleAsmb()`
- `bool RayCastEgo(ref Brutal.Numerics.double4x4, KSA.Ray, ref double, ref double, ref Brutal.Numerics.double3, ref Brutal.Numerics.double3, ref Brutal.Numerics.double3, ref Brutal.Numerics.double3, ref KSA.Part, ref KSA.Part)`
- `bool get_IsAttachedInternal()`
- `double get_CrashTolerancePascals()`
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

### KSA.Part+Connector+Flag

*referenced as a type only*

### KSA.Part+Connector+TemplateBase

- `Flag Flags`

### KSA.PartFailure

- `bool TrippedTheFragmentGuard(int, int)`

### KSA.PartFailureEvent

- `System.Collections.Generic.List`1<KSA.Part> FailedParts`
- `bool DestroyWholeVehicle`
- `void .ctor()`

### KSA.PartInstance

*referenced as a type only*

### KSA.PartTemplate

- `System.Collections.Generic.List`1<KSA.PartInstance> SubPartInstances`
- `System.Collections.Generic.List`1<TemplateBase> Connectors`

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
- `void UpdateRenderData(ref Brutal.Numerics.double4x4, bool, KSA.IViewport, int)`

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
- `double GetAtmosphericPressureAtAltitude(double)`

### KSA.PhysicsBubble

- `bool _forceOffRails`

### KSA.PhysicsStates

- `ref KSA.VehicleProperties Props`

### KSA.PlanetTransparenciesRenderer

- `KSA.Atmosphere.Rendering.CloudRenderer GetCloudRenderer()`

### KSA.PlumeTrailEmitterState

- `void .ctor()`

### KSA.Popup

- `bool Active`
- `bool get_AnyOpen()`

### KSA.ProfilerWindowBase

- `double TicksToMs(long)`

### KSA.Program

- `Brutal.VulkanApi.VkSampler get_LinearClampedSampler()`
- `Brutal.VulkanApi.VkSampler get_PointClampedSampler()`
- `Core.Renderer GetRenderer()`
- `KSA.Atmosphere.Rendering.CloudShadowsRenderer GetCloudShadowsRenderer()`
- `KSA.AtmosphereRenderer get_PlanetAtmosphereRenderer()`
- `KSA.Camera GetMainCamera()`
- `KSA.Camera GetRenderCamera()`
- `KSA.GizmosRenderer GizmosRenderer`
- `KSA.GpuTextureSystem TextureSystem`
- `KSA.IGameViewport get_MainViewport()`
- `KSA.Program get_Instance()`
- `KSA.Rendering.Particles.ParticleSystem`2<KSA.Rendering.Particles.ParticleUpdateData, KSA.Rendering.Particles.ParticleRenderData> ParticleSystem`
- `KSA.Vehicle get_ControlledVehicle()`
- `KSA.VehicleEditor Editor`
- `System.ReadOnlySpan`1<KSA.Vehicle> get_VehiclesInFrame()`
- `bool IsControlledVehicleActive`
- `int ResourceFrameIndex`
- `void OnGameLoaded()`
- `void SetCameraUbo(KSA.IViewport)`
- `void UpdateShaderData(double, KSA.IViewport)`
- `void set_ControlledVehicle(KSA.Vehicle)`

### KSA.QuaternionEx

- `Brutal.Numerics.doubleQuat Inverse(Brutal.Numerics.doubleQuat)`

### KSA.Ray

- `Brutal.Numerics.double3 Direction`
- `Brutal.Numerics.double3 Origin`

### KSA.Rendering.BarrierBatch

- `bool Add(KSA.Rendering.RenderImage, KSA.Rendering.ImageBarrierInfo, int, bool, bool)`
- `void .ctor(System.Span`1<Brutal.VulkanApi.VkImageMemoryBarrier2>)`
- `void .ctor(System.Span`1<Brutal.VulkanApi.VkMemoryBarrier2>, System.Span`1<Brutal.VulkanApi.VkBufferMemoryBarrier2>, System.Span`1<Brutal.VulkanApi.VkImageMemoryBarrier2>)`
- `void Add(ref Brutal.VulkanApi.VkMemoryBarrier2)`
- `void SubmitAndFlush(Brutal.VulkanApi.CommandBuffer)`

### KSA.Rendering.ComputePipelineWrapper

- `void .ctor(System.Span`1<KSA.Rendering.IRenderImage>, System.Span`1<KSA.Rendering.IRenderImage>, System.Span`1<KSA.Rendering.IRenderImage>, System.Span`1<KSA.Rendering.IRenderImage>, KSA.ShaderReference, System.Span`1<Brutal.VulkanApi.VkDescriptorSetLayout>, System.Span`1<Brutal.VulkanApi.VkPushConstantRange>, int, Core.Renderer, string, Brutal.VulkanApi.VkSampler, Brutal.VulkanApi.VkSampler, Brutal.VulkanApi.VkShaderStageFlags, System.Span`1<Brutal.VulkanApi.VkImageView>, System.Span`1<Brutal.VulkanApi.VkBuffer>, System.Nullable`1<Brutal.VulkanApi.VkSpecializationInfo>, System.Span`1<KSA.Rendering.IRenderImage>, System.Span`1<KSA.Rendering.IRenderImage>, System.Span`1<Brutal.VulkanApi.VkBuffer>, System.Span`1<KSA.Rendering.IRenderImage>, Brutal.VulkanApi.VkSampler, System.Span`1<Brutal.VulkanApi.VkBuffer>, System.Span`1<Brutal.ByteSize>, System.Nullable`1<Brutal.ShaderCApi.CompileOptions>)`
- `void BindPipeline<1>(Brutal.VulkanApi.CommandBuffer, int, System.Span`1<Brutal.VulkanApi.VkDescriptorSet>, System.Span`1<Brutal.ByteSize32>, !!0)`

### KSA.Rendering.IRenderImage

*referenced as a type only*

### KSA.Rendering.ImageBarrierInfo

*referenced as a type only*

### KSA.Rendering.ImageBarrierInfo+Presets

- `KSA.Rendering.ImageBarrierInfo SampledReadC`
- `KSA.Rendering.ImageBarrierInfo StorageReadWriteC`

### KSA.Rendering.Lighting.ELightFlags

*referenced as a type only*

### KSA.Rendering.Lighting.Light

- `KSA.Rendering.Lighting.Light CreatePointLight(Brutal.Numerics.double3, float, Brutal.Numerics.float3, float, KSA.Rendering.Lighting.ELightFlags)`

### KSA.Rendering.Lighting.LightDebug

- `KSA.Vehicle Target`
- `System.Collections.Generic.List`1<KSA.Rendering.Lighting.Light> Lights`

### KSA.Rendering.Particles.ExplosionReference

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

### KSA.Rendering.RenderImage

- `KSA.Rendering.RenderImage CreateColorStorage(RenderCore.IVulkanContext, string, Brutal.VulkanApi.VkExtent2D, Brutal.VulkanApi.VkFormat, int, int, KSA.Rendering.RenderImageViewMode)`
- `void Dispose()`

### KSA.Rendering.RenderImageViewMode

*referenced as a type only*

### KSA.Rendering.RenderTarget

- `Brutal.VulkanApi.VkExtent2D get_Extent()`
- `KSA.Rendering.RenderImage get_ColorImage()`
- `KSA.Rendering.RenderImage get_DepthImage()`

### KSA.Rendering.Water.Data.OceanReference

- `KSA.DensityReference Density`
- `KSA.DistanceReference Level`

### KSA.ScreenshotCapture

- `void Request(int, string)`

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

### KSA.ShaderReference

*referenced as a type only*

### KSA.ShapesUnlock

*referenced as a type only*

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

### KSA.StateVectors

- `KSA.UniverseTime StateTime`

### KSA.StellarBody

*referenced as a type only*

### KSA.StructuralLoad

- `double MaxGLoad`
- `double PeakGLoad`
- `double get_GLoadFraction()`

### KSA.SunbloomRenderer

- `void Render(Brutal.VulkanApi.CommandBuffer, KSA.IViewport, int)`

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
- `double GetAchivedSpeedFraction()`
- `double get_SimulationSpeed()`
- `void AutoWarpStop(bool)`
- `void AutoWarpTo(KSA.UniverseTime, double)`
- `void DestroyVehicle(KSA.Vehicle, KSA.CrewDisposition)`
- `void DestroyVehicleFromEvent(KSA.Vehicle, KSA.VehicleDestructionEvent)`
- `void SetSimulationSpeed(KSA.SimSpeed)`

### KSA.UniverseTime

- `KSA.UniverseTime op_Subtraction(KSA.UniverseTime, KSA.UniverseTime)`
- `System.Int128 get_Nanoseconds()`
- `bool Equals(KSA.UniverseTime)`
- `double Seconds()`
- `void .ctor(double)`

### KSA.Vehicle

- `Brutal.Numerics.double3 get_AccelerationBody()`
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
- `KSA.FlightPlan get_FlightPlan()`
- `KSA.IParentBody get_Parent()`
- `KSA.Orbit get_Orbit()`
- `KSA.Part get_ControlPart()`
- `KSA.PartTree get_Parts()`
- `KSA.PhysicsStates GetPhysicsStatesMutable()`
- `KSA.Situation get_Situation()`
- `KSA.Vehicle CreateVehicle(KSA.CelestialSystem, Brutal.Numerics.doubleQuat, Brutal.Numerics.double3, KSA.IParentBody, string, KSA.Part, KSA.Orbit)`
- `KSA.Vehicle get_BubbleLeader()`
- `bool IsAnyEnginePropellantAvailable()`
- `bool get_HasPhysicsBubble()`
- `bool get_IsControllable()`
- `bool get_IsDebris()`
- `bool get_IsDisposed()`
- `bool get_IsEditedVehicle()`
- `float GetManualThrottle()`
- `float GetMinThrottle()`
- `float get_PropellantMass()`
- `float get_TotalMass()`
- `int get_BubbleVehicleCount()`
- `ref KSA.BubbleOrigin get_BubbleOrigin()`
- `ref KSA.StructuralLoad get_StructuralLoad()`
- `ref KSA.VehicleProperties get_Props()`
- `void PrepareWorker(KSA.SimStep)`
- `void ProcessInput(KSA.InputAction, Brutal.GlfwApi.GlfwKeyAction, Brutal.GlfwApi.GlfwModifier)`
- `void SetControlPart(KSA.Part, Connector)`
- `void TeleportToLocation(KSA.Celestial, double, double)`
- `void UpdateAfterPartTreeModification()`
- `void UpdateRenderData(KSA.IViewport, int)`

### KSA.VehicleDestructionCause

*referenced as a type only*

### KSA.VehicleDestructionEvent

- `KSA.VehicleDestructionCause Cause`
- `float PeakDynamicPressure`
- `float PeakGLoad`
- `void .ctor()`

### KSA.VehicleEditor

*referenced as a type only*

### KSA.VehicleProperties

- `KSA.BoundingBoxCdA AerodynamicCdABody`
- `float TotalPropellantMass`
- `float TotalSurfaceArea`
- `float get_TotalMass()`
- `void SetOnRails(bool)`

### KSA.VehicleReferenceFrame

*referenced as a type only*

### KSA.VehicleReferenceFrameEx

- `Brutal.Numerics.double3 QuaternionToEulerAngles(KSA.VehicleReferenceFrame, Brutal.Numerics.doubleQuat)`
- `Brutal.Numerics.doubleQuat GetEclBody2Cci(Brutal.Numerics.doubleQuat)`

### KSA.VehicleSave

- `KSA.PartTree Load(KSA.IViewport)`
- `KSA.VehicleSaveData VehicleSaveData`

### KSA.VehicleSaveData

- `string Character`

### KSA.VehicleUpdateState

- `KSA.FlightComputerOutput FlightComputerOutput`
- `KSA.PartFailureEvent PartFailureEvent`
- `bool AnyActuatorActive()`

### KSA.ViewProjection

- `Brutal.Numerics.float4x4 viewProjection`

### KSA.ViewportRegistry

- `System.ReadOnlySpan`1<KSA.IGameViewport> get_GameViews()`
- `bool TryOpenSecondaryViewport(ref KSA.IGameViewport)`
- `int get_AvailableSecondaryCount()`

### KSA.ViewportType

*referenced as a type only*

### KSA.VolumetricTrailRenderer

- `float ErosionEdgeSharpness`
- `float ErosionMaxDepth`
- `float SkyAmbientBrightness`
- `int SelfShadowStepCount`
- `void SubmitEmitter(KSA.PlumeTrailEmitterState, KSA.Celestial, Brutal.Numerics.double3, float, float, Brutal.Numerics.float3, float, float, bool)`

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
