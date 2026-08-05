"""Generates the kitten's shoulder cannon as a standalone glTF, for a character attachment.

Run through tools/model/kittengun.sh, not directly -- Blender is a Windows binary and the paths
have to be Windows paths.

Separate from pantsir.py and separate from the part atlas, because this is not a part. KSA hangs
it off a *bone* through CharacterAttachmentReference, so it ships as its own glTF with its own
material, exactly as Core's helmet and MMU attachments do.

Mounted on the upper back rather than held in a paw. `Spine2_M` carries the whole torso, so the
gun rides the walk cycle without fighting the arm animation, and it reads as mounted rather than
carried -- which is what the weapon actually is: the mod aims it, not the kitten.

The kitten is small. Its bounding half-extents are 0.40 x 0.40 x 0.62 m, so a gun much over a
third of a metre stops looking like equipment and starts looking like a vehicle.

Conventions are pantsir.py's, and for the same reasons -- see its module docstring:

  * every face gets real UV area, or the tangent frame is NaN and the model sparkles
  * boxes inflate by a skin plus a per-box jitter, or coplanar faces z-fight
  * export_yup is off, so the file's axes are the game's axes

Local frame, before the XML's own transform places it on the bone:
  +X  forward, along the barrel
  +Y  left
  +Z  up
"""

import json
import math
import random
import sys

import bpy
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
OUT_PATH = argv[0]
PALETTE_PATH = argv[1]

PALETTE = json.loads(open(PALETTE_PATH).read())["swatches"]

# Same values as the launcher: a centimetre of separation is invisible at any range the model is
# seen from, and it is what stops the depth buffer picking a winner per pixel per frame.
SKIN = 0.0015
JITTER = 0.0025
_jitter = random.Random(0x4B17)

# Smaller than the launcher's because the gun is an order of magnitude smaller: the patch has to
# stay inside one swatch cell, and the reach is what bounds it.
UV_PER_METRE = 0.12
SWATCH_REACH = 0.06

_objects = []


def project_to_swatch(mesh, uv, centre):
    """Box-projects each face into a small patch centred on one palette swatch.

    The patch is the point. Collapsing a face onto a single UV gives the same flat colour and
    zero UV area, and a tangent frame derived from that normalises a zero-length vector to NaN.
    """
    cu, cv = centre

    for poly in mesh.polygons:
        normal = poly.normal
        reference = Vector((0.0, 0.0, 1.0)) if abs(normal.z) < 0.9 else Vector((1.0, 0.0, 0.0))
        tangent = normal.cross(reference)
        if tangent.length < 1e-9:
            tangent = Vector((1.0, 0.0, 0.0))
        tangent.normalize()
        bitangent = normal.cross(tangent)

        origin = poly.center
        for loop_index in poly.loop_indices:
            offset = mesh.vertices[mesh.loops[loop_index].vertex_index].co - origin
            du = max(-SWATCH_REACH, min(SWATCH_REACH, offset.dot(tangent) * UV_PER_METRE))
            dv = max(-SWATCH_REACH, min(SWATCH_REACH, offset.dot(bitangent) * UV_PER_METRE))
            uv.data[loop_index].uv = (cu + du, cv + dv)


def _finish(ob, swatch):
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    # Reuse the layer the primitive operator already made; uv_layers.new() here would leave the
    # generated one first, and that is the one exported as TEXCOORD_0.
    mesh = ob.data
    uv = mesh.uv_layers.get("UVMap") or mesh.uv_layers.new(name="UVMap")
    project_to_swatch(mesh, uv, PALETTE[swatch]["uv"])

    mesh.materials.append(bpy.data.materials["KittenGunPalette"])
    _objects.append(ob)
    return ob


def box(size, loc, rot=(0.0, 0.0, 0.0), swatch="hull"):
    """A box grown by a skin plus a jitter unique to this box.

    The jitter is not decoration. A uniform skin separates faces pointing *at* each other and
    does nothing for two boxes whose outer faces sit on the same constant -- those stay exactly
    coplanar and fight just as hard.
    """
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc, rotation=rot)
    ob = bpy.context.object
    ob.scale = Vector(tuple(
        size[axis] + 2 * (SKIN + _jitter.random() * JITTER) for axis in range(3)))
    return _finish(ob, swatch)


def cyl(radius, depth, loc, rot=(0.0, 0.0, 0.0), swatch="metal", verts=16):
    """A cylinder. Deliberately *not* inflated -- see the coaxial note on the muzzle brake."""
    bpy.ops.mesh.primitive_cylinder_add(radius=radius, depth=depth, location=loc,
                                        rotation=rot, vertices=verts)
    return _finish(bpy.context.object, swatch)


def along_x():
    """Rotation putting a cylinder's axis along +X, which is the barrel's direction."""
    return (0.0, math.pi / 2, 0.0)


def new_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)

    material = bpy.data.materials.new("KittenGunPalette")
    material.use_nodes = True
    bsdf = material.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.6, 0.6, 0.6, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.7
    bsdf.inputs["Metallic"].default_value = 0.0


# ---------------------------------------------------------------------------
# The gun
# ---------------------------------------------------------------------------

# Every dimension in metres, against a kitten roughly 1.24 m tall.
MOUNT_LEN = 0.10
BARREL_LEN = 0.20
BARREL_R = 0.014


def build():
    # Saddle across the spine. Wider than it is tall so it reads as strapped on rather than
    # bolted through.
    box((0.09, 0.13, 0.030), (0.0, 0.0, 0.0), swatch="hull_dark")

    # Post lifting the receiver clear of the back, so the barrel is not buried in fur.
    box((0.035, 0.035, 0.055), (0.005, 0.0, 0.042), swatch="hull_dark")

    # Receiver.
    box((MOUNT_LEN, 0.055, 0.050), (0.03, 0.0, 0.088), swatch="hull")

    # Ammo drum on the left, where it does not foul the barrel line.
    cyl(0.030, 0.038, (0.012, 0.046, 0.088), rot=(math.pi / 2, 0.0, 0.0),
        swatch="metal", verts=14)

    # Barrel.
    cyl(BARREL_R, BARREL_LEN, (0.03 + MOUNT_LEN / 2 + BARREL_LEN / 2, 0.0, 0.095),
        rot=along_x(), swatch="metal", verts=16)

    # Muzzle brake. Fatter than the barrel and a *different facet count*: a coaxial pair with the
    # same count shares every side plane no matter the radius, and radius alone will not save it.
    cyl(BARREL_R * 1.55, 0.030,
        (0.03 + MOUNT_LEN / 2 + BARREL_LEN - 0.004, 0.0, 0.095),
        rot=along_x(), swatch="metal", verts=10)

    # Sight block on top, so the thing has a front and a back at a glance.
    box((0.045, 0.020, 0.016), (0.02, 0.0, 0.121), swatch="hull_dark")


def join_all(name):
    bpy.ops.object.select_all(action="DESELECT")
    for ob in _objects:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = _objects[0]
    bpy.ops.object.join()

    joined = bpy.context.object
    joined.name = joined.data.name = name
    return joined


# Character attachments are authored in the rig's centimetre space, not metres. The kitten is
# drawn through CharacterAvatar.Core.Scale = 0.01, and GetBoneTransform returns the bone matrix
# already carrying that 0.01 -- so a mesh in metres arrives a hundred times too small. Core's own
# attachments measure 80.6 units (helmet) and 48.3 (MMU), which is 0.81 m and 0.48 m once scaled.
#
# Applied at export rather than in the dimensions above, so the numbers in this file stay in
# metres and mean what they say. It is baked into the vertices, *not* left on the object: KSA's
# StaticMeshRenderable.Draw writes one instance transform per asset and never reads the glTF's
# node transforms, so an object-level scale would silently do nothing.
CHARACTER_SPACE = 100.0


# Rig space, read off Core's own attachments rather than assumed: X is lateral, Y is up, Z is
# forward. Both Core attachments are symmetric about X (centre X = 0) and offset along Y -- the
# helmet sits +27.8 above Head_M. This file authors X forward, Y left, Z up, so the axes have to
# be permuted on the way out: our X -> their Z, our Y -> their X, our Z -> their Y.
#
# Baked, not expressed as <Rotation> in the XML, for two reasons. The engine composes an
# attachment's axes as RotZ(-90) * RotX(-90) *before* the bone matrix while the body composes the
# reverse *after* the scale, so reasoning about the XML angle means reasoning through that
# asymmetry; and TransformReference reads Euler radians whose order is not stated anywhere. A
# baked permutation is checkable here, against Core's numbers, with no launch.
RIG_AXES = Matrix(((0.0, 1.0, 0.0),
                   (0.0, 0.0, 1.0),
                   (1.0, 0.0, 0.0)))

# Where it sits relative to the socket bone, in rig units, applied after the permutation above so
# these read as (lateral, up, forward).
#
# Head_M, beside the helmet rather than on the spine: mounted low on the back the barrel came out
# under the kitten's chin, because the helmet is enormous relative to the body. Core's helmet mesh
# spans +-40.3 lateral and reaches +63.7 up from this same bone, so 45 clears its side and 30 sits
# it against the upper shell. Forward 8 puts the muzzle just past the visor -- a gun that ends
# behind the face reads as ornament.
#
# Head_M and Spine2_M have identical rest axes to within 0.01, so moving between them needs no
# change to the permutation.
MOUNT_OFFSET = Vector((45.0, 30.0, 8.0))


def export(path):
    gun = join_all("KittenGun")

    bpy.context.view_layer.objects.active = gun
    gun.matrix_world = (Matrix.Translation(MOUNT_OFFSET)
                        @ RIG_AXES.to_4x4()
                        @ Matrix.Scale(CHARACTER_SPACE, 4)
                        @ gun.matrix_world)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=False,
        export_apply=True,
        # Off, for the same reason the launcher exports this way: with the Y-up conversion
        # applied Blender's Y and Z swap on the way out, and the barrel would leave sideways.
        export_yup=False,
        export_cameras=False,
        export_lights=False,
    )


new_scene()
build()
export(OUT_PATH)
print(f"kittengun: wrote {OUT_PATH}")
