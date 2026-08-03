"""Minimal check that headless Blender can build, render and export."""
import bpy, sys, os

out = sys.argv[sys.argv.index("--") + 1]

bpy.ops.wm.read_factory_settings(use_empty=True)

bpy.ops.mesh.primitive_cube_add(size=2)
cube = bpy.context.object
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.8, location=(0, 0, 1.6))

cam_data = bpy.data.cameras.new("cam")
cam = bpy.data.objects.new("cam", cam_data)
bpy.context.scene.collection.objects.link(cam)
cam.location = (6, -6, 4)
cam.rotation_euler = (1.1, 0, 0.785)
bpy.context.scene.camera = cam

light_data = bpy.data.lights.new("sun", type="SUN")
light_data.energy = 4
light = bpy.data.objects.new("sun", light_data)
bpy.context.scene.collection.objects.link(light)
light.rotation_euler = (0.6, 0.2, 0.3)

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 640
scene.render.resolution_y = 480
scene.render.filepath = out
bpy.ops.render.render(write_still=True)
print("RENDER_OK")

glb = os.path.splitext(out)[0] + ".glb"
bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", use_selection=False)
print("EXPORT_OK", glb)
