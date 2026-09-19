"""
Renders any .glb from a few angles, so an authored asset can be judged before it is declared.

The generated parts get their previews from pantsir.py, which builds the scene it renders. An
authored one arrives as a file, and until now the only way to see it was to ship it and start the
game. This imports and lights it instead.

Run through tools/model/preview.sh rather than by hand -- Blender is a Windows binary and needs
Windows paths for both the script and its output.

    blender --background --python preview-glb.py -- <in.glb> <outdir> [mesh-name-filter]

It renders what is *in the file*, node transforms and all, which is deliberate: that is what the
asset looks like to anything reading it naively, and a body that arrives on its side here will
arrive on its side in game unless something bakes it.
"""

import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
SOURCE, OUT_DIR = argv[0], argv[1]
ONLY = argv[2] if len(argv) > 2 else ""


def clear():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for block in (bpy.data.meshes, bpy.data.materials):
        for item in list(block):
            block.remove(item)


def look_at(cam, loc, target):
    cam.location = Vector(loc)
    direction = Vector(target) - Vector(loc)
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def main():
    clear()
    bpy.ops.import_scene.gltf(filepath=SOURCE)

    shown = [ob for ob in bpy.context.scene.objects
             if ob.type == "MESH"
             and not ob.name.endswith("_VM")
             and not ob.name.startswith("_ColPrim")
             and (not ONLY or ONLY in ob.name)]

    for ob in bpy.context.scene.objects:
        if ob.type == "MESH":
            ob.hide_render = ob not in shown

    if not shown:
        print("NOTHING TO RENDER")
        return

    # The bounds of what is actually being shown, so the camera framing is derived rather than
    # guessed at -- an authored asset can be any size.
    corners = [ob.matrix_world @ Vector(c) for ob in shown for c in ob.bound_box]
    lo = Vector((min(c[i] for c in corners) for i in range(3)))
    hi = Vector((max(c[i] for c in corners) for i in range(3)))
    centre = (lo + hi) / 2
    reach = max((hi - lo).length, 0.2)

    print(f"BOUNDS {lo.x:.4f},{lo.y:.4f},{lo.z:.4f} .. {hi.x:.4f},{hi.y:.4f},{hi.z:.4f}")

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 900
    scene.render.resolution_y = 640
    scene.world = bpy.data.worlds.new("world")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.16, 0.18, 0.22, 1.0)

    for name, energy, rot in (("key", 4.0, (0.9, 0.35, 0.8)), ("fill", 1.6, (2.2, -0.5, -1.1))):
        data = bpy.data.lights.new(name, type="SUN")
        data.energy = energy
        light = bpy.data.objects.new(name, data)
        scene.collection.objects.link(light)
        light.rotation_euler = rot

    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam

    stem = os.path.splitext(os.path.basename(SOURCE))[0]
    views = {
        "3q": Vector((0.9, -1.0, 0.75)),
        "side": Vector((0.02, -1.4, 0.02)),
        "top": Vector((1.5, 0.02, 0.02)),
        "end": Vector((0.05, 0.05, 1.5)),
    }

    for name, offset in views.items():
        look_at(cam, centre + offset.normalized() * reach * 1.7, centre)
        scene.render.filepath = os.path.join(OUT_DIR, f"preview_{stem}_{name}.png")
        bpy.ops.render.render(write_still=True)
        print("RENDER", scene.render.filepath)


main()
print("DONE")
