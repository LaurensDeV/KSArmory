"""
Builds the mod's mesh atlas, headless.

Every weapon system the mod ships is in here, one atlas, exactly as Core keeps several prefabs
in one file: the Pantsir-S1 below, and the AIM-9J rail in sidewinder.py, which this imports.

Run through tools/model/build.sh rather than by hand - Blender is a Windows binary and needs
Windows paths for both the script and its output.

    blender --background --python pantsir.py -- <outdir> <palette.json> <diffuse.png>

Produces in <outdir>:
    KSArmory_MeshAtlas.glb      every subpart mesh plus its editor-preview variant
    preview_*.png               renders, so the shapes can be judged without the game

## Coordinate system

Part space, which is glTF file space - the exporter runs with export_yup off, so Blender
coordinates *are* the coordinates KSA reads out of the atlas and the ones <Transform> and
<LocationAsmb> in the part XML are expressed in.

    +X  up. This is the part's forward axis in KSA's sense, and the battery's boresight.
    +Y  the direction the vehicle drives. The cab is at +Y.
    +Z  the vehicle's right-hand side.

The origin sits on the ground between the wheels, so the part mounts by its underside and
every height in this file reads as a height.

## Texturing

One material, one palette atlas, and every face UV-mapped to the centre of a flat swatch -
see palette.py. Each primitive is created with a swatch name and gets its UVs written
immediately, so there is no unwrapping step and nothing to get out of sync.

## Missile geometry is exported, not eyeballed

The tube muzzle positions are computed here and printed as a ready-to-paste C# block. They
must match the LauncherProfile's `Tubes` in Sim/Arsenal.cs, which is what puts the launch
markers on the actual tubes. tools/validate-parts.py fails the build if the two disagree.
"""

import json
import random
import math
import os
import sys

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

# Blender runs this by path, so its directory is not on sys.path.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ciws
import bombrack
import optic
import sidewinder

# ---------------------------------------------------------------------------
# Arguments
# ---------------------------------------------------------------------------

argv = sys.argv[sys.argv.index("--") + 1:]
OUT_DIR = argv[0]
PALETTE_PATH = argv[1]
DIFFUSE_PATH = argv[2]

# Optional "elev=<deg>,bearing=<deg>": pose the moving assemblies before rendering. Without it
# the previews show the modelled rest pose, which is the one pose the game never displays once
# the drives are running - so a defect that only appears at another elevation is invisible here.
POSE = {}
if len(argv) > 3 and argv[3]:
    for field in argv[3].split(","):
        if "=" not in field:
            continue
        key, _, value = field.partition("=")
        POSE[key.strip().lower()] = math.radians(float(value))

# Exporting is the slowest phase after rendering and produces a file nothing reads when the
# caller only wants pictures.
EXPORT = "--no-export" not in argv

PALETTE = json.loads(open(PALETTE_PATH).read())["swatches"]

# ---------------------------------------------------------------------------
# Dimensions. Roughly a Pantsir-S1 on its KAMAZ-6560 8x8: 7.9 m long, 3.0 m wide,
# missiles reaching 5.4 m with the pods elevated.
# ---------------------------------------------------------------------------

WHEEL_R = 0.66
WHEEL_W = 0.44
WHEEL_Z = 1.24
AXLE_Y = (2.95, 1.62, -1.72, -3.02)     # two steering axles forward, two driven aft

FRAME_X0, FRAME_X1 = 0.90, 1.44          # chassis rails
BODY_Z = 1.30
DECK_X = 2.00                            # top of the flat deck the turret sits on

CAB_Y0, CAB_Y1 = 1.68, 3.86
CAB_X1 = 3.02
NOSE_Y = 4.04

TURRET_Y = -1.42                         # turret ring centre
TURRET_X0 = 2.02                         # ring bottom, just above the deck

# The turret slews about the part's X axis - the vehicle's up - through this point. The turret
# mesh is exported with it on the origin so KSA's subpart rotation spins it in place; the part
# XML then offsets it back with <Position>, and TUBE offsets are emitted turret-local.
TURRET_PIVOT = (0.0, TURRET_Y, 0.0)

# The pods are modelled at this elevation and rotated away from it at runtime, rather than
# being modelled flat. If the engine ever refuses the transform write, the vehicle is left in
# this pose - which is the one that looks right - instead of with its tubes lying through the
# tracking radar.
POD_ELEV = math.radians(55.0)            # missile pods, from horizontal
POD_TRUNNION = (2.62, -2.05)             # (X, Y) of the elevation pivot
POD_Z = 1.22

# The pods pitch about the Z axis through this point: a line running across the vehicle through
# both trunnions. Their mesh is exported centred on it for the same reason the turret's is.
POD_PIVOT = (POD_TRUNNION[0], POD_TRUNNION[1], 0.0)
TUBE_LEN = 3.30
TUBE_R = 0.105
TUBE_PITCH = 0.225                       # centre-to-centre, both directions
TUBE_ROWS = 3                            # along the pod's own "down" axis
TUBE_COLS = 2                            # across, in Z
POD_STANDOFF = 0.15                      # trunnion to the tube bundle's near end

# Search radar. A double-sided hexagonal array on a turntable at the back of the turret,
# turning continuously - it is a search set, not a tracker, so it never stops and never aims.
RADAR_MAST_Y = TURRET_Y - 1.10
RADAR_R = 0.95                           # hexagon circumradius, so ~1.7 m across the flats
RADAR_SPLAY = math.radians(21.0)         # lean of each face off vertical

# Rotation of each hexagon about its own axis, so it stands on a flat edge rather than on a
# corner. Blender's 6-vertex cylinder starts on a corner; check this by measuring the exported
# mesh rather than by reasoning about the euler.
RADAR_CLOCK = math.pi / 6
RADAR_PIVOT = (4.05, RADAR_MAST_Y, 0.0)  # spin axis, parallel to the part's X

# Electro-optical head. Aimed freely rather than on an axis, so its mesh is recentred on the
# ball's own centre and the mod writes a full rotation. At rest it looks along +Y, the way the
# vehicle drives, which is the direction its lens is modelled sticking out in.
#
# Above the tracking array, which reaches X 3.67. Beside it the array cuts the sight line the
# moment the head looks down and forward - which is exactly where a target on its final approach
# is.
EO_PIVOT = (4.10, TURRET_Y + 1.07, 0.44)

# The 57E6 round: a bronze booster with small tail fins, a cluster of four delta fins at the
# stage joint, and a slim grey sustainer with a blue-grey nose. Modelled nose-along-+X with the
# origin at its centre, which is the frame LauncherPart aims it in.
MISSILE_LEN = 3.10
MISSILE_R = 0.078                        # sustainer body
BOOSTER_R = 0.086
BOOSTER_LEN = 1.20

GUN_ELEV = math.radians(22.0)

# Outboard of the pod bundle, whose frame rings reach Z 1.51, and clear of the turret cheeks at
# 1.00-1.24. The inner barrel's muzzle brake is the widest part: GUN_Z - 0.09 - 0.068. Clearing
# the pods at all needs 1.67; clearing them so they do not read as touching needs a good deal
# more, and 1.85 leaves 0.18 m of daylight.
GUN_Z = 1.85
# Inboard face of the gun sponsons. Clear of the tube bundle's widest point (|Z| 1.51 after the
# frame rings), because the sponsons sit inside the disc the bundle sweeps about its trunnion.
SPONSON_INNER_Z = 1.58
# Above the turret deck, which tops out at X 3.38. The cradle is 0.52 tall, so a centre below
# 3.64 puts it inside the turret body, which is a swept intersection for a body that rotates on
# its own trunnion.
GUN_MOUNT = (3.70, -1.35)                # (X, Y) - clear of the deck, and clear of the cab

# The cannon elevate about a line across the vehicle through both mounts, as the pods do about
# their trunnion. Their mesh is exported recentred on it so KSA's subpart rotation pitches them
# in place rather than swinging them round the turret.
GUN_PIVOT = (GUN_MOUNT[0], GUN_MOUNT[1], 0.0)
BARREL_LEN = 2.40
BARREL_R = 0.036

def firing_order():
    """The twelve containers, in the order the battery empties them.

    Top row first, outboard column first, alternating sides - so a salvo walks down the pods
    and rocks left-right rather than emptying one side and then the other.
    """
    return [(side, row, col)
            for row in reversed(range(TUBE_ROWS))
            for col in reversed(range(TUBE_COLS))
            for side in (1, -1)]

# Every box is grown by this much on each side so abutting parts interpenetrate instead of
# sharing a face plane, plus a per-box, per-axis jitter of up to JITTER on top. See box().
SKIN = 0.006
JITTER = 0.010

# Deterministic, so the mesh is reproducible and diffable. Seeded once here rather than per
# call so that adding a primitive reshuffles nothing before it.
_jitter = random.Random(0x9A5D)

# UV scale for the per-face projection, and how far from a swatch's centre it may reach.
#
# Faces are given real UV *area* rather than being collapsed onto the swatch centre. A face
# whose loops all share one UV has a zero UV derivative, so the tangent basis degenerates,
# normalize() on it returns NaN, and NaN * 0 poisons the shading even though the normal map is
# flat. In game that is a vehicle crawling with white speckle; Blender's preview does not show
# it because the preview material has no normal map wired in.
#
# 0.012 UV/m keeps the largest face on the model (the 8 m deck, 4 m from its centre) inside
# 0.048, comfortably within the 0.08 limit, which itself leaves half a cell of margin against
# mip-level bleed.
UV_PER_METRE = 0.012
SWATCH_REACH = 0.08

_objects = {"chassis": [], "turret": [], "pods": [], "radar": [], "guns": [], "optic": [],
            "missile": [], "fins": []}
_group = "chassis"


# ---------------------------------------------------------------------------
# Primitives
# ---------------------------------------------------------------------------

def project_to_swatch(mesh, uv, centre):
    """Box-projects each face into a small patch centred on one palette swatch.

    The patch is what matters. Collapsing a face's loops onto a single UV would give the same
    flat colour and no seams, but it also gives the face zero UV area - and a renderer deriving
    a tangent frame from that gets a zero-length tangent, whose normalize() is NaN. See
    UV_PER_METRE.

    Everything inside the patch is one flat colour, so the projection's orientation and scale
    are cosmetically irrelevant; they only need to be non-degenerate and consistent.
    """
    cu, cv = centre

    for poly in mesh.polygons:
        normal = poly.normal
        # Any reference not parallel to the face normal gives a usable in-plane basis.
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
    """Applies the object's transform and paints every loop at one palette swatch."""
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    # Reuse the layer the add-primitive operators already made. Calling uv_layers.new() here
    # instead silently produces a *second* layer ("UVMap.001") and leaves the generated one
    # first - which is the one the glTF exporter writes as TEXCOORD_0. The model then renders
    # with the whole atlas smeared over every face: candy-striped tubes and magenta wheels.
    mesh = ob.data
    uv = mesh.uv_layers.get("UVMap") or mesh.uv_layers.new(name="UVMap")
    project_to_swatch(mesh, uv, PALETTE[swatch]["uv"])

    mesh.materials.append(bpy.data.materials["KSArmoryPalette"])
    _objects[_group].append(ob)
    return ob


def box(size, loc, rot=(0.0, 0.0, 0.0), swatch="hull"):
    """A box, grown on every side by SKIN plus a per-axis jitter unique to this box.

    That growth is the whole reason this wrapper exists, and the jitter is why a flat SKIN is
    not enough. Laying parts out by naming shared edges - deck bottom at the same X as the hull
    top - produces *coplanar faces*, and the depth buffer then picks a winner per pixel per
    frame. In game that reads as the vehicle crawling with white speckle.

    A uniform skin only fixes faces that point at *each other*: it pushes them past one
    another. Faces pointing the *same* way - two boxes whose outer surfaces both sit on
    DECK_X - move together and stay exactly coplanar, and fight just as hard. Only a
    *different* inflation per box separates those, so each gets its own, drawn from a seeded
    generator so the mesh stays reproducible.

    A centimetre on an eight-metre vehicle is invisible. tools/model/checkmesh.py is what
    proves it worked; do not tune these by eye.

    Cylinders do not go through here, so anything built with cyl() has to be given its
    clearance by hand - see the tube covers and muzzle brakes.
    """
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc, rotation=rot)
    ob = bpy.context.object
    ob.scale = Vector(tuple(
        size[axis] + 2 * (SKIN + _jitter.random() * JITTER) for axis in range(3)))
    return _finish(ob, swatch)


def fin(chord, span_len, thick, loc, roll, swatch="missile", taper=0.42, sweep=0.55):
    """A clipped delta fin: long root chord, shorter tip, swept leading edge.

    Real control surfaces are not slabs. A rectangular fin reads as a placeholder from any angle
    that shows its planform, and on a round this small the planform is most of what you see.

    Built vertex by vertex rather than from a scaled cube, because the taper and the sweep are
    the whole point and neither survives a uniform scale. Local axes match how the fins are
    placed: X is the chord along the body, Y the thickness, Z the span outward. `taper` is the
    tip chord as a fraction of the root, and `sweep` how far the tip's leading edge sits aft as a
    fraction of the root chord.

    Goes through _finish like everything else, so it gets the same swatch projection - a face
    with no UV area produces a zero-length tangent and NaN shading, which is invisible in
    Blender and sparkles in game.
    """
    half_c, half_s, half_t = chord / 2.0, span_len / 2.0, thick / 2.0
    tip_c = chord * taper
    tip_le = half_c - chord * sweep

    # Root inboard at -Z, tip outboard at +Z; leading edge is +X.
    profile = ((half_c, -half_s), (-half_c, -half_s),          # root: leading, trailing
               (tip_le - tip_c, half_s), (tip_le, half_s))     # tip:  trailing, leading

    verts = [(x, y, z) for y in (-half_t, half_t) for (x, z) in profile]
    faces = [(0, 1, 2, 3), (7, 6, 5, 4),                       # the two flat sides
             (0, 3, 7, 4), (1, 0, 4, 5), (2, 1, 5, 6), (3, 2, 6, 7)]

    mesh = bpy.data.meshes.new("fin")
    mesh.from_pydata(verts, [], faces)
    mesh.validate()

    # Make every face point outwards.
    #
    # from_pydata takes the winding it is given and asks no questions, so a face listed the wrong
    # way round keeps an inward normal. Backface culling then shows straight through it and the
    # fin reads as hollow from whichever side got it wrong - while looking perfectly solid from
    # the other, which is what makes it easy to miss. The add-primitive operators never have this
    # problem, so nothing else in this file needs it.
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()

    ob = bpy.data.objects.new("fin", mesh)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = Vector(loc)
    ob.rotation_euler = Euler((roll, 0.0, 0.0))

    # Select it, and only it, before handing over.
    #
    # _finish calls transform_apply, which acts on the *selection* rather than on the active
    # object alone. The add-primitive operators leave their new object as the sole selection, so
    # everything built through box() and cyl() satisfies that by accident. An object created
    # with bpy.data.objects.new is not selected at all, so the apply lands on whatever was
    # selected beforehand and leaves this fin's rotation and offset unbaked, which puts the fin
    # off the body axis.
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    return _finish(ob, swatch)


def span(x, y, z, swatch="hull", rot=(0.0, 0.0, 0.0)):
    """A box given as three (lo, hi) pairs - how most of this model is easiest to think about."""
    size = (x[1] - x[0], y[1] - y[0], z[1] - z[0])
    loc = ((x[0] + x[1]) / 2, (y[0] + y[1]) / 2, (z[0] + z[1]) / 2)
    return box(size, loc, rot, swatch)


def cyl(radius, depth, loc, rot=(0.0, 0.0, 0.0), swatch="metal", verts=24, spin=0.0):
    """`spin` rotates the cylinder about its own axis, which is how two coaxial polygons are
    kept from having facets on the same planes - see the search array's two hexagons."""
    bpy.ops.mesh.primitive_cylinder_add(radius=radius, depth=depth, location=loc,
                                        rotation=rot, vertices=verts)
    ob = bpy.context.object
    if spin:
        ob.rotation_euler = (ob.rotation_euler.to_matrix()
                             @ Matrix.Rotation(spin, 3, "Z")).to_euler()
    return _finish(ob, swatch)


def cone(radius_base, radius_tip, depth, loc, rot=(0.0, 0.0, 0.0), swatch="metal", verts=12):
    bpy.ops.mesh.primitive_cone_add(radius1=radius_base, radius2=radius_tip, depth=depth,
                                    location=loc, rotation=rot, vertices=verts)
    return _finish(bpy.context.object, swatch)


def sphere(radius, loc, swatch="glass", segments=20, rings=10):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=radius, location=loc,
                                         segments=segments, ring_count=rings)
    return _finish(bpy.context.object, swatch)


def axis_x(): return (0.0, math.pi / 2, 0.0)
def axis_y(): return (math.pi / 2, 0.0, 0.0)


def pitched(elev):
    """Euler that lays a Z-axis cylinder along (sin elev, cos elev, 0) - i.e. elevated from
    horizontal in the XY plane, which for this part is 'pointing up and forward'."""
    return (0.0, math.pi / 2, math.pi / 2 - elev)


def elevated_frame(elev):
    """Unit vectors for an elevated assembly: d along the barrel, p its 'down' in the XY plane."""
    d = Vector((math.sin(elev), math.cos(elev), 0.0))
    p = Vector((math.cos(elev), -math.sin(elev), 0.0))
    return d, p


# ---------------------------------------------------------------------------
# Scene
# ---------------------------------------------------------------------------

def new_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)

    mat = bpy.data.materials.new("KSArmoryPalette")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = nodes["Principled BSDF"]
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(DIFFUSE_PATH)
    tex.interpolation = "Closest"
    links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.8


# ---------------------------------------------------------------------------
# Chassis: KAMAZ-6560 8x8 with the equipment deck behind the cab
# ---------------------------------------------------------------------------

def build_chassis():
    global _group
    _group = "chassis"

    # Wheels. Axis along Z, which is already the cylinder's own axis.
    for y in AXLE_Y:
        for z in (-WHEEL_Z, WHEEL_Z):
            cyl(WHEEL_R, WHEEL_W, (WHEEL_R, y, z), swatch="rubber", verts=20)
            # Hub, proud of the tyre so it reads at a distance. Sunk a few millimetres into
            # the tyre rather than parked against it: flush would both z-fight and, if the
            # arithmetic drifted the other way, leave the disc floating in space.
            outboard = math.copysign(WHEEL_W / 2 - 0.005, z)
            cyl(0.30, 0.08, (WHEEL_R, y, z + outboard), swatch="metal", verts=16)
        # Axle beam.
        cyl(0.10, 2 * WHEEL_Z, (WHEEL_R, y, 0.0), swatch="steel_dark", verts=12)

    # Chassis rails and the lower hull.
    span((FRAME_X0, FRAME_X1), (-3.86, 3.60), (-0.96, 0.96), "hull_dark")
    span((FRAME_X1, DECK_X), (-3.92, CAB_Y0), (-BODY_Z, BODY_Z), "hull")

    # Deck plate the turret and equipment sit on.
    span((DECK_X, DECK_X + 0.06), (-3.94, CAB_Y0), (-BODY_Z - 0.04, BODY_Z + 0.04), "deck")

    # Mudguards over each axle.
    for y in AXLE_Y:
        for z in (-1.0, 1.0):
            span((WHEEL_R + 0.62, WHEEL_R + 0.70),
                 (y - 0.42, y + 0.42),
                 (z * (WHEEL_Z - 0.34), z * (WHEEL_Z + 0.30)), "hull_dark")

    # Stowage lockers and fuel tanks slung between the axles.
    for z in (-1.0, 1.0):
        side = z * (BODY_Z + 0.03)
        span((1.05, 1.58), (-3.30, -2.20), (min(side, side - z * 0.06), max(side, side - z * 0.06)), "detail")
        span((1.05, 1.58), (0.10, 1.40), (min(side, side - z * 0.06), max(side, side - z * 0.06)), "detail")
        cyl(0.28, 1.05, (1.30, -0.55, z * 1.18), axis_y(), "metal", verts=16)

    # Cab.
    span((FRAME_X1, CAB_X1), (CAB_Y0, CAB_Y1), (-1.28, 1.28), "hull")
    span((CAB_X1, CAB_X1 + 0.08), (CAB_Y0 - 0.06, CAB_Y1 + 0.06), (-1.31, 1.31), "hull_dark")

    # Windscreen: a panel facing +Y, tilted so its top leans aft. It has to sit proud of the
    # cab's front face or the cab box swallows it and the truck ends up blind.
    box((0.74, 0.08, 2.30), (2.56, CAB_Y1 + 0.04, 0.0), (0.0, 0.0, -0.30), "glass")
    # Side glazing.
    for z in (-1.0, 1.0):
        box((0.58, 1.24, 0.06), (2.52, CAB_Y0 + 1.08, z * 1.29), swatch="glass")

    # Front end: bumper, grille, lights.
    span((1.02, 1.70), (CAB_Y1, NOSE_Y), (-1.31, 1.31), "hull")
    span((0.96, 1.14), (CAB_Y1, NOSE_Y + 0.04), (-1.31, 1.31), "steel_dark")
    span((1.76, 2.18), (CAB_Y1, CAB_Y1 + 0.08), (-0.96, 0.96), "black")
    for z in (-1.0, 1.0):
        cyl(0.16, 0.10, (1.44, NOSE_Y, z * 1.10), axis_y(), "detail", verts=14)

    # Auxiliary power unit behind the cab.
    span((DECK_X, 2.72), (0.82, CAB_Y0 - 0.06), (-1.20, 1.20), "hull_dark")
    for z in (-1.0, 1.0):
        span((2.72, 2.80), (1.00, 1.46), (z * 0.30, z * 1.00), "black")

    # Outrigger jacks - the Pantsir plants these before it shoots.
    for y in (-3.55, 1.15):
        for z in (-1.0, 1.0):
            box((0.22, 0.34, 0.34), (1.05, y, z * (BODY_Z + 0.14)), swatch="steel_dark")


# ---------------------------------------------------------------------------
# Turret: missile pods, twin autocannon, tracking array and search radar
# ---------------------------------------------------------------------------

def build_turret():
    global _group
    _group = "turret"

    # Ring the whole assembly turns on.
    cyl(1.06, 0.20, (TURRET_X0 + 0.10, TURRET_Y, 0.0), axis_x(), "steel_dark", verts=32)

    # Turret body: a long box filling the deck behind the cab, with a sloped front plate.
    span((2.14, 3.30), (TURRET_Y - 1.72, TURRET_Y + 1.44), (-1.00, 1.00), "hull")
    box((0.10, 1.20, 1.96), (3.02, TURRET_Y + 1.56, 0.0), (0.0, 0.0, -0.55), "hull_dark")
    span((3.30, 3.38), (TURRET_Y - 1.74, TURRET_Y + 1.34), (-1.02, 1.02), "deck")
    # Cheeks the pods and guns hang off. They reach forward past the trunnion line on purpose:
    # the pods are a separate body hanging on that line, and the cheek is what visually carries
    # them. Stopping it at the trunnion leaves the pods reading as detached.
    for z in (-1.0, 1.0):
        span((2.30, 3.10), (TURRET_Y - 1.30, TURRET_Y + 0.60),
             (min(z * 1.00, z * 1.24), max(z * 1.00, z * 1.24)), "hull_dark")

    build_trunnions()

    # The cannon pitch independently of the turret's traverse, so they need their own mesh and
    # pivot. Built here because they hang off the turret cheeks, but exported separately.
    _group = "guns"
    build_guns()
    _group = "turret"

    build_tracking_radar()
    build_search_radar_mount()

    # Sponsons carrying the cannon trunnions, out from the trunnion line and up to GUN_MOUNT.
    # Part of the turret, not the guns: the cradle pitches on this, so it has to stay put.
    #
    # Their inboard face sits outboard of the tube bundle's widest point. Reaching in to the
    # cheek instead puts them inside the disc the bundle sweeps about its own trunnion, and the
    # 20 cm bundle then passes through them at every elevation, fully immersed. Elevation turns
    # about Z and the traverse is shared, so a gap in Z is the only one that holds at every pose.
    # tools/model/checkswept.py is what proves it.
    #
    # Added last on purpose. The box jitter runs off one seed, so inserting a primitive
    # reshuffles every one after it - putting these earlier drives the tracking radar's faces
    # into a 1.79 mm near-coplanar pair.
    for z in (-1.0, 1.0):
        span((2.90, GUN_MOUNT[0]), (GUN_MOUNT[1] - 0.34, GUN_MOUNT[1] + 0.34),
             (min(z * SPONSON_INNER_Z, z * GUN_Z), max(z * SPONSON_INNER_Z, z * GUN_Z)),
             "hull_dark")

        # Web tying each sponson back to its cheek. Standing the sponson off on its own leaves
        # the whole cannon assembly floating clear of the turret. It reaches inboard *behind*
        # the pod trunnion line, which is the one place it can: forward of that line it would be
        # inside the disc the tube bundle sweeps.
        span((2.90, GUN_MOUNT[0]), (POD_TRUNNION[1] - 0.36, POD_TRUNNION[1] + 0.02),
             (min(z * 1.06, z * GUN_Z), max(z * 1.06, z * GUN_Z)), "hull_dark")

    # The pods are a group of their own: they pitch about the trunnion line independently of
    # the turret's traverse, so they need their own mesh, pivot and transform.
    _group = "pods"
    build_pods()

    # And the search array is a third, turning continuously about its own axis.
    _group = "radar"
    build_search_array()

    # The round itself. Instanced twelve times as subparts and flown by the mod.
    _group = "missile"
    build_missile()


def tube_muzzle(side, row, col):
    """Centre of one container's mouth, in part space. `side` is -1 or +1 in Z."""
    d, p = elevated_frame(POD_ELEV)
    base = Vector((POD_TRUNNION[0], POD_TRUNNION[1], side * POD_Z))
    along = d * (POD_STANDOFF + TUBE_LEN)
    down = p * ((row - (TUBE_ROWS - 1) / 2) * TUBE_PITCH)
    across = Vector((0.0, 0.0, (col - (TUBE_COLS - 1) / 2) * TUBE_PITCH * side))
    return base + along + down + across


def build_trunnions():
    """The arms the pods pitch on. Part of the turret, so they traverse but do not elevate."""
    for side in (-1, 1):
        base = Vector((POD_TRUNNION[0], POD_TRUNNION[1], side * POD_Z))
        box((0.44, 0.50, 0.30), (base.x - 0.10, base.y + 0.10, side * (POD_Z - 0.20)),
            swatch="steel_dark")
        cyl(0.16, 0.36, (base.x, base.y, side * (POD_Z - 0.30)), swatch="metal", verts=16)


def build_pods():
    d, p = elevated_frame(POD_ELEV)
    rot = pitched(POD_ELEV)

    for side in (-1, 1):
        base = Vector((POD_TRUNNION[0], POD_TRUNNION[1], side * POD_Z))

        for row in range(TUBE_ROWS):
            for col in range(TUBE_COLS):
                muzzle = tube_muzzle(side, row, col)
                centre = muzzle - d * (TUBE_LEN / 2)
                cyl(TUBE_R, TUBE_LEN, centre, rot, "tube", verts=16)
                # Frangible cover on the mouth. A cone rather than a cylinder because two
                # coaxial cylinders have parallel side faces a couple of millimetres apart,
                # which is a z-fight with nothing coplanar; a cone's sides are parallel to
                # nothing, so the cover can sit *inside* the container's silhouette instead of
                # proud of it. Any radius above TUBE_R reads as a lip, and a facet count below
                # the tube's makes that lip visibly polygonal.
                cone(TUBE_R * 0.985, TUBE_R * 0.80, 0.10, muzzle - d * 0.02, rot, "tube_cap",
                     verts=16)

        # Frame wrapping the bundle, stopping short so the containers stay visible.
        #
        # `rot` carries a primitive's local Z onto the tube axis, which is what cyl() wants and
        # box() does not: its size tuple is (across Z, along p, along the tube).
        bundle_w = TUBE_COLS * TUBE_PITCH + 0.10
        bundle_h = TUBE_ROWS * TUBE_PITCH + 0.10
        for frac in (0.18, 0.55, 0.88):
            ring = base + d * (POD_STANDOFF + TUBE_LEN * frac)
            box((bundle_w, bundle_h, 0.09), ring, rot, "hull_dark")
        # Spine down the pod's back.
        spine = base + d * (POD_STANDOFF + TUBE_LEN * 0.5) + p * (bundle_h / 2 + 0.04)
        box((bundle_w, 0.09, TUBE_LEN * 0.78), spine, rot, "hull_dark")


def build_guns():
    global _group
    d, _ = elevated_frame(GUN_ELEV)
    rot = pitched(GUN_ELEV)

    for side in (-1, 1):
        mount = Vector((GUN_MOUNT[0], GUN_MOUNT[1], side * GUN_Z))

        box((0.52, 0.86, 0.46), mount, swatch="hull_dark")

        # The ammunition box goes on the turret, not the cradle. Riding the cradle it hangs
        # 0.78 m off the trunnion and sweeps an annulus the sponson sits inside, so it passes
        # through its own mount above 33 degrees of elevation. The belt feeds a gun that
        # pitches; the box it feeds from does not have to. Built here rather than beside the
        # sponsons so the jitter sequence is unchanged.
        _group = "turret"
        box((0.60, 0.70, 0.38), (mount.x - 0.05, mount.y - 0.78, mount.z), swatch="detail")
        _group = "guns"

        # Twin 30 mm barrels.
        for offset in (-0.09, 0.09):
            root = mount + Vector((0.0, 0.0, offset)) + d * 0.30
            cyl(BARREL_R, BARREL_LEN, root + d * (BARREL_LEN / 2), rot, "steel_dark", verts=12)
            # Muzzle brake, overhanging the barrel's end. Eight sides against the barrel's
            # twelve, so no two side faces are ever parallel - see the tube covers.
            cyl(BARREL_R * 1.9, 0.20, root + d * (BARREL_LEN - 0.06), rot, "metal", verts=8)
        # Jacket over the breech end.
        cyl(0.17, 0.70, mount + d * 0.45, rot, "steel_dark", verts=14)


def build_tracking_radar():
    """1RS2-1E: the big flat array across the turret front, plus the electro-optical head."""
    face_y = TURRET_Y + 1.70
    tilt = -0.16          # leans back a touch, as the real housing does

    # Housing, then the radiating face proud of it, then a surround so the face is framed.
    box((1.46, 0.30, 1.62), (2.94, face_y - 0.20, 0.0), (0.0, 0.0, tilt), "radar")
    box((1.30, 0.12, 1.46), (2.96, face_y, 0.0), (0.0, 0.0, tilt), "array")
    for z in (-1.0, 1.0):
        box((1.44, 0.16, 0.10), (2.95, face_y - 0.04, z * 0.76), (0.0, 0.0, tilt), "metal")

    # Electro-optical tracker. The pedestal belongs to the turret; the head above it is a body
    # of its own so it can be slewed onto the track. sphere() and cyl() consume no box jitter,
    # so lifting them into another group leaves the sequence untouched.
    global _group
    box((0.74, 0.44, 0.50), (3.72, TURRET_Y + 1.05, 0.44), swatch="hull_dark")

    _group = "optic"
    sphere(0.26, EO_PIVOT, "radar")
    cyl(0.14, 0.10, (EO_PIVOT[0] + 0.04, EO_PIVOT[1] + 0.23, EO_PIVOT[2]), axis_y(),
        "glass", verts=16)
    _group = "turret"


def build_search_radar_mount():
    """The pedestal the search array turns on. Belongs to the turret; does not spin."""
    mast_x0 = 3.38

    cyl(0.36, RADAR_PIVOT[0] - mast_x0, ((mast_x0 + RADAR_PIVOT[0]) / 2, RADAR_MAST_Y, 0.0),
        axis_x(), "steel_dark", verts=20)
    # Splayed legs, as on the real mount.
    for z in (-1.0, 1.0):
        box((0.54, 0.22, 0.16), (3.62, RADAR_MAST_Y, z * 0.34), (0.0, 0.22 * z, 0.0),
            "steel_dark")
    # Slip-ring housing directly under the turntable.
    cyl(0.30, 0.16, (RADAR_PIVOT[0] - 0.10, RADAR_MAST_Y, 0.0), axis_x(), "metal", verts=18)


def build_missile():
    """One 57E6 round, nose along +X, origin at its centre.

    Its own mesh rather than part of the pods: twelve copies are instanced as subparts, and the
    mod flies each one by writing its transform, so the rounds are real geometry instead of
    tracer gizmos. A round not in the air is scaled to nothing.
    """
    global _group
    half = MISSILE_LEN / 2
    joint = -half + BOOSTER_LEN            # where the booster hands over to the sustainer

    # Booster: bronze, fatter, with a nozzle skirt at the tail.
    cyl(BOOSTER_R, BOOSTER_LEN, ((-half + joint) / 2, 0.0, 0.0), axis_x(), "bronze", verts=14)
    cyl(BOOSTER_R * 0.72, 0.10, (-half - 0.02, 0.0, 0.0), axis_x(), "black", verts=12)
    # Interstage collar.
    cyl(BOOSTER_R * 1.04, 0.09, (joint, 0.0, 0.0), axis_x(), "warn", verts=12)

    # Sustainer: slimmer, pale grey, tapering to a blue-grey nose. It runs back *into* the
    # booster rather than butting against it - two cylinders meeting end to end put their caps
    # on one plane, and cylinders get none of the jitter that saves the boxes.
    tail = joint - 0.14
    cyl(MISSILE_R, half - 0.30 - tail, ((tail + half - 0.30) / 2, 0.0, 0.0), axis_x(),
        "missile", verts=14)
    cone(MISSILE_R, MISSILE_R * 0.22, 0.34, (half - 0.16, 0.0, 0.0), axis_x(), "array", verts=14)

    # Fins are built into their own group, not the body.
    #
    # They fold. A real 57E6 stows with its fins flat against the casing so the round fits the
    # tube, and they snap out once it is clear. The mod animates that by scaling this group
    # radially - Part.Scale is per-axis and applies about the part's own origin - so the fins
    # collapse onto the body axis at a scale of nearly zero and flick out to full span after
    # launch.
    #
    # That works only because this group shares the missile's origin exactly: the collapse is
    # towards the origin, which has to be the body axis. Do not recentre it.
    _group = "fins"

    # Four delta fins at the stage joint, and four small ones at the tail. Each is placed at
    # its rolled position rather than rotated about the missile axis after the fact - a box
    # turns about its own origin, so it has to be put where that roll leaves it.
    for index in range(4):
        roll = index * math.pi / 2
        # span, chord, thickness, station, radius, swatch, taper, sweep.
        #
        # The main wing carries most of the planform, so it gets the strongest taper and sweep.
        # The tail surfaces are stubbier: short-span control fins are close to straight-edged on
        # the real round, so they taper less and barely sweep at all.
        for span_len, chord, thick, at, radius, swatch, taper, sweep in (
                (0.30, 0.62, 0.022, joint + 0.24, BOOSTER_R + 0.15, "missile", 0.38, 0.58),
                (0.11, 0.24, 0.020, -half + 0.14, BOOSTER_R + 0.055, "black", 0.62, 0.30),
                (0.08, 0.18, 0.018, half - 0.62, MISSILE_R + 0.04, "missile", 0.62, 0.28)):
            loc = (at, -radius * math.sin(roll), radius * math.cos(roll))
            fin(chord, span_len, thick, loc, roll, swatch, taper, sweep)


def build_search_array():
    """1RS1-1E: a double-sided hexagonal array that turns continuously.

    Two hexagonal faces leaning against each other, meeting along a ridge at the top, so the
    side profile is a triangle and both faces look up and outward at once - which is what lets
    the real set watch two directions at the same time while it spins.

    Built about the spin axis at the origin; the mesh is exported recentred on RADAR_PIVOT and
    the mod rewrites its transform each frame.
    """
    px, py = RADAR_PIVOT[0], RADAR_PIVOT[1]          # built in part space, recentred on export

    # A hexagon standing on a flat edge reaches cos(30) of its circumradius, not the full
    # radius - the corners are out at the sides, not at the top. Everything below is measured
    # from that, so the two faces meet along their top *edges* rather than at two points.
    reach = RADAR_R * math.cos(math.pi / 6)
    ridge = reach * math.cos(RADAR_SPLAY)            # apex height above each face's centre
    offset = reach * math.sin(RADAR_SPLAY)           # how far each face sits off the spin axis
    # The faces' bottom edges sit *inside* the turntable rather than resting on top of it.
    # Parked level with it the whole array reads as hovering, however wide the mount is.
    base_x = px + 0.02

    # Turntable, wide enough to actually reach under the splayed faces. A narrow one leaves the
    # whole array visibly hovering above its mount.
    #
    # Sunk 4 cm into the mast, whose cap sits on X = RADAR_PIVOT[0]. Resting on it exactly puts
    # two capped cylinders' faces on one plane, and this pair turns at SearchRadarRpm — so the
    # z-fight rotates. cyl() applies no skin of its own, unlike box().
    cyl(0.60, 0.18, (px + 0.05, py, 0.0), axis_x(), "steel_dark", verts=18)
    cyl(0.40, 0.10, (px + 0.22, py, 0.0), axis_x(), "metal", verts=18)

    for side in (-1, 1):
        # A hexagonal slab whose face leans back by RADAR_SPLAY from vertical - so its normal
        # sits RADAR_SPLAY *above the horizon*, pointing up and outward. pitched() takes that
        # elevation directly; passing its complement instead lays the faces nearly flat.
        elevation = RADAR_SPLAY if side > 0 else math.pi - RADAR_SPLAY
        normal = pitched(elevation)

        axis = Vector((math.sin(elevation), math.cos(elevation), 0.0))   # face normal
        # "Up" within the face, which must genuinely point up on *both* faces. Deriving it from
        # `elevation` gives (cos, -sin) of an obtuse angle on the far side, i.e. straight down,
        # and quietly places every detail upside-down on one of the two faces.
        up = Vector((math.cos(RADAR_SPLAY), -side * math.sin(RADAR_SPLAY), 0.0))
        centre = Vector((base_x + ridge, py + side * offset, 0.0))

        # Housing, then the radiating face proud of it, so the array reads as a surface rather
        # than as a panel painted on a box. Both are clocked by RADAR_CLOCK so each hexagon
        # stands on a flat edge - which is also what `reach` assumes, so the two faces meet
        # along their top edges instead of interpenetrating at a pair of corners.
        cyl(RADAR_R, 0.22, centre, normal, "hull", verts=6, spin=RADAR_CLOCK)
        cyl(RADAR_R * 0.86, 0.30, centre, normal, "array", verts=6, spin=RADAR_CLOCK)


# ---------------------------------------------------------------------------
# Export
# ---------------------------------------------------------------------------

def join_group(name, recentre=None):
    """Merges one group's primitives into a single named mesh, transforms already applied.

    `recentre` shifts the merged vertices so a chosen point lands on the mesh origin. The
    turret needs this: KSA rotates a subpart about *its own* origin, so a turret whose mesh
    carries its offset from the vehicle centre would swing around the chassis like a wrecking
    ball instead of spinning in place. The XML puts the offset back with <Position>.
    """
    objs = _objects[name]
    bpy.ops.object.select_all(action="DESELECT")
    for ob in objs:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()

    joined = bpy.context.object
    if recentre is not None:
        shift = Vector(recentre)
        for vertex in joined.data.vertices:
            vertex.co -= shift

    joined.name = name
    joined.data.name = name
    return joined


def export(path):
    chassis = join_group("chassis")
    turret = join_group("turret", recentre=TURRET_PIVOT)
    pods = join_group("pods", recentre=POD_PIVOT)
    radar = join_group("radar", recentre=RADAR_PIVOT)
    guns = join_group("guns", recentre=GUN_PIVOT)
    eohead = join_group("optic", recentre=EO_PIVOT)
    missile = join_group("missile")
    fins = join_group("fins")
    rail = join_group("sidewinder")
    aim9 = join_group("aim9")
    ciwsbase = join_group("ciwsbase")
    ciwsturret = join_group("ciwsturret", recentre=ciws.TURRET_PIVOT)
    ciwsguns = join_group("ciwsguns", recentre=ciws.GUN_PIVOT)
    rack = join_group("bombrack")
    mk82 = join_group("mk82")
    opticbase = join_group("opticbase")
    optichead = join_group("optichead", recentre=optic.HEAD_PIVOT)

    # KSA looks these up by Id out of the atlas; *_VM is the editor's preview variant, and
    # Core ships one for every subpart. Ours are the same geometry - the part is low-poly
    # enough that a simplified preview would buy nothing.
    for ob, ident in ((chassis, "KSArmory_Subpart_Chassis"),
                      (turret, "KSArmory_Subpart_Turret"),
                      (pods, "KSArmory_Subpart_Pods"),
                      (radar, "KSArmory_Subpart_Radar"),
                      (guns, "KSArmory_Subpart_Guns"),
                      (eohead, "KSArmory_Subpart_Optic"),
                      (missile, "KSArmory_Subpart_Missile"),
                      (fins, "KSArmory_Subpart_Fins"),
                      (rail, "KSArmory_Subpart_SidewinderRail"),
                      (aim9, "KSArmory_Subpart_Aim9"),
                      (ciwsbase, "KSArmory_Subpart_CiwsBase"),
                      (ciwsturret, "KSArmory_Subpart_CiwsTurret"),
                      (ciwsguns, "KSArmory_Subpart_CiwsGuns"),
                      (rack, "KSArmory_Subpart_BombRack"),
                      (mk82, "KSArmory_Subpart_Mk82"),
                      (opticbase, "KSArmory_Subpart_OpticBase"),
                      (optichead, "KSArmory_Subpart_OpticHead")):
        preview = ob.copy()
        preview.data = ob.data.copy()
        preview.name = preview.data.name = ident + "_VM"
        bpy.context.scene.collection.objects.link(preview)
        ob.name = ob.data.name = ident

    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=False,
        export_apply=True,
        # Off: with the Y-up conversion applied, Blender's Y and Z would swap on the way out
        # and the vehicle would export three metres long and eight metres wide. Core's atlas
        # reads back with X as the part's forward axis, and the launcher's hexagon of tubes
        # lands where the XML says it should, so the file's axes are the game's axes.
        export_yup=False,
        export_cameras=False,
        export_lights=False,
    )
    return chassis, turret


# ---------------------------------------------------------------------------
# Preview renders
# ---------------------------------------------------------------------------

VIEWS = {
    # name: (camera location, point it looks at)
    "3q": ((6.5, 13.0, 11.5), (2.4, -0.5, 0.0)),
    "rear3q": ((6.2, -12.0, 11.0), (2.6, -1.4, 0.0)),
    "side": ((2.9, -0.4, 18.0), (2.7, -0.4, 0.0)),
    "front": ((2.9, 18.0, 0.0), (2.7, 0.0, 0.0)),
    "top": ((19.0, -0.3, 0.0), (2.4, -0.3, 0.0)),
}


def look_at(cam, loc, look):
    """Aims the camera without a TRACK_TO constraint.

    The constraint's up_axis aligns to world Z, and this part's up is world X, so every
    preview came out rolled ninety degrees. Building the matrix directly lets the up
    reference be the part's own up.
    """
    loc, look = Vector(loc), Vector(look)
    forward = (loc - look).normalized()          # cameras look down their local -Z
    up_ref = Vector((1.0, 0.0, 0.0))
    if abs(forward.dot(up_ref)) > 0.98:          # looking straight down: pick another reference
        up_ref = Vector((0.0, 1.0, 0.0))
    right = up_ref.cross(forward).normalized()
    up = forward.cross(right)
    cam.matrix_world = Matrix.Translation(loc) @ Matrix((right, up, forward)).transposed().to_4x4()


def pose_assemblies(bearing_rad, elev_rad):
    """Rotates the moving groups the way the runtime does, so a preview shows a real pose.

    Mirrors TubeGeometry.ElevatingPose and TurretRotation: each assembly pitches about +Z
    through its own pivot by (reference - commanded), then everything riding the turret turns
    about +X through TURRET_PIVOT. Run before joining, while the groups are still separate.
    """
    def turn(group, pivot, axis, angle):
        if abs(angle) < 1e-9:
            return
        p = Vector(pivot)
        m = Matrix.Translation(p) @ Matrix.Rotation(angle, 4, axis) @ Matrix.Translation(-p)
        for ob in _objects[group]:
            ob.matrix_world = m @ ob.matrix_world

    turn("pods", POD_PIVOT, "Z", POD_ELEV - elev_rad)
    turn("guns", GUN_PIVOT, "Z", GUN_ELEV - elev_rad)

    for group in ("turret", "pods", "guns", "radar"):
        turn(group, TURRET_PIVOT, "X", bearing_rad)


def render_previews(out_dir):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 900
    scene.render.resolution_y = 640
    scene.render.film_transparent = False
    scene.world = bpy.data.worlds.new("world")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.16, 0.18, 0.22, 1.0)

    for name, energy, rot in (("key", 4.0, (0.9, 0.35, 0.8)),
                              ("fill", 1.6, (2.2, -0.5, -1.1))):
        data = bpy.data.lights.new(name, type="SUN")
        data.energy = energy
        light = bpy.data.objects.new(name, data)
        bpy.context.scene.collection.objects.link(light)
        light.rotation_euler = rot

    cam_data = bpy.data.cameras.new("cam")
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    def show_only(*groups):
        """Renders only the named groups, or everything when given none."""
        wanted = set(groups)
        for name, objs in _objects.items():
            for ob in objs:
                ob.hide_render = bool(wanted) and name not in wanted

    # The round and the Sidewinder rail are both modelled at the origin, which on the vehicle is
    # between the front wheels. Leave them out of the vehicle shots and give them their own.
    for name, (loc, look) in VIEWS.items():
        show_only()
        for group in ("missile", "fins", "sidewinder", "aim9", "bombrack", "mk82",
                      "ciwsbase", "ciwsturret", "ciwsguns", "opticbase", "optichead"):
            for ob in _objects[group]:
                ob.hide_render = True
        look_at(cam, loc, look)
        scene.render.filepath = os.path.join(out_dir, f"preview_{name}.png")
        bpy.ops.render.render(write_still=True)
        print("RENDER", scene.render.filepath)

    # Body and fins together: they are separate subparts but one object to the eye, and the fin
    # planform is most of what the round's silhouette is.
    show_only("missile", "fins")
    look_at(cam, (1.1, -7.4, 3.4), (0.0, 0.0, 0.0))
    scene.render.filepath = os.path.join(out_dir, "preview_missile.png")
    bpy.ops.render.render(write_still=True)
    print("RENDER", scene.render.filepath)

    # Straight down the round's own axis. The only view that shows whether the fins sit
    # symmetrically about the body - a side view cannot, and the fins are placed by rolling one
    # shape about that axis, so an error there is invisible from anywhere else.
    look_at(cam, (6.0, 0.0, 0.0), (0.0, 0.0, 0.0))
    scene.render.filepath = os.path.join(out_dir, "preview_missile_axial.png")
    bpy.ops.render.render(write_still=True)
    print("RENDER", scene.render.filepath)

    render_sidewinder(out_dir, cam, show_only)

    # The CIWS is 4.7 m and stands on its own origin, so it needs no reassembly - unlike the rail,
    # whose round is modelled in a different frame.
    show_only("ciwsbase", "ciwsturret", "ciwsguns")
    for name, loc in (("ciws", (5.6, 9.8, 5.6)), ("ciws_side", (2.4, 0.0, -13.5))):
        look_at(cam, loc, (2.4, 0.0, 0.0))
        scene.render.filepath = os.path.join(out_dir, f"preview_{name}.png")
        bpy.ops.render.render(write_still=True)
        print("RENDER", scene.render.filepath)

    show_only()


def render_sidewinder(out_dir, cam, show_only):
    """The rail with its round seated, which is the only view that shows whether it fits.

    The two are modelled in different frames - the rail in part space, the round nose-along-+X
    like every body mesh - so the runtime seating is reproduced here: the quarter turn about +Z
    that carries +X onto the tube axis, then the offset to the body line. Restored afterwards,
    because the export is about to read these objects.
    """
    scene = bpy.context.scene
    seat = (Matrix.Translation(Vector((sidewinder.AXIS_X, 0.0, 0.0)))
            @ Matrix.Rotation(math.pi / 2, 4, "Z"))

    rest = [(ob, ob.matrix_world.copy()) for ob in _objects["aim9"]]
    for ob, matrix in rest:
        ob.matrix_world = seat @ matrix

    show_only("sidewinder", "aim9")

    # Three-quarter, pure side, and straight down the body axis. The last is the only one that
    # shows the fins' roll against the rail, which is what decides whether one fouls it.
    for name, loc in (("sidewinder", (3.0, -3.6, 2.2)),
                      ("sidewinder_side", (0.41, 0.0, -4.6)),
                      ("sidewinder_end", (0.41, 2.4, 0.0))):
        look_at(cam, loc, (0.41, 0.0, 0.0))
        scene.render.filepath = os.path.join(out_dir, f"preview_{name}.png")
        bpy.ops.render.render(write_still=True)
        print("RENDER", scene.render.filepath)

    for ob, matrix in rest:
        ob.matrix_world = matrix


# ---------------------------------------------------------------------------

def report_muzzles(out_dir):
    """Emits the firing tubes as a C# array and as JSON.

    The array goes into the LauncherProfile's `Tubes` in src/KSArmory/Sim/Arsenal.cs by hand;
    the JSON is what tools/validate-parts.py compares that file against, so the two cannot
    quietly drift.

    Bare positions, with no direction: this reads tubes off a mesh that has parallel ones, so
    every entry follows the pod axis. A launcher with splayed tubes declares its directions by
    hand -- see the Tube record in Sim/LauncherProfile.cs.
    """
    pivot = Vector(POD_PIVOT)
    # Pod-local, matching the exported mesh: the markers then ride the pods' own transform and
    # follow them through both traverse and elevation, with no extra bookkeeping.
    firing = [tube_muzzle(*t) - pivot for t in firing_order()]

    print("\n=== LauncherProfile.Tubes (paste into src/KSArmory/Sim/Arsenal.cs)")
    for m in firing:
        print(f"        new({m.x:8.5f}, {m.y:8.5f}, {m.z:8.5f}),")

    # These two stay in *part* space: they feed MuzzleEcl, which builds launch positions
    # without a camera and so cannot go through the turret's transform.
    part_frame = [tube_muzzle(*t) for t in firing_order()]
    highest = max(m.x for m in part_frame)
    mean_x = sum(m.x for m in part_frame) / len(part_frame)
    ring = sum(math.hypot(m.y, m.z) for m in part_frame) / len(part_frame)

    # Where the pods hang off the turret, and where the turret sits on the part. Both are
    # needed in C#: the pods are a *sibling* of the turret rather than nested inside it, so the
    # mod composes traverse and elevation itself and writes the pods' position each frame.
    pod_rel_turret = Vector(POD_PIVOT) - Vector(TURRET_PIVOT)
    radar_rel_turret = Vector(RADAR_PIVOT) - Vector(TURRET_PIVOT)
    eo_rel_turret = Vector(EO_PIVOT) - Vector(TURRET_PIVOT)

    print(f"\n    tube count           = {len(firing)}   (LauncherProfile.TubeCount is derived)")
    print(f"    MuzzleForwardOffset  = {mean_x:.3f}   (highest tube mouth {highest:.3f} m)")
    print(f"    TubeRingRadius       = {ring:.3f}")
    print(f"    TurretPivot          = ({TURRET_PIVOT[0]:.3f}, {TURRET_PIVOT[1]:.3f}, {TURRET_PIVOT[2]:.3f})")
    print(f"    PodPivot             = ({POD_PIVOT[0]:.3f}, {POD_PIVOT[1]:.3f}, {POD_PIVOT[2]:.3f})")
    print(f"    PodPivotFromTurret   = ({pod_rel_turret.x:.5f}, {pod_rel_turret.y:.5f}, {pod_rel_turret.z:.5f})")
    print(f"    PodReferenceElevDeg  = {math.degrees(POD_ELEV):.3f}")
    gun_rel_turret = Vector(GUN_PIVOT) - Vector(TURRET_PIVOT)
    print(f"    GunPivotFromTurret   = ({gun_rel_turret.x:.5f}, {gun_rel_turret.y:.5f}, {gun_rel_turret.z:.5f})")
    print(f"    GunReferenceElevDeg  = {math.degrees(GUN_ELEV):.3f}")

    # Barrel muzzles in the cannon subpart's own frame, which is the mesh recentred on GUN_PIVOT.
    # Emitted for the same reason the tube muzzles are: the C# needs them to spawn rounds, and
    # nothing at build or run time would notice the two disagreeing.
    gun_d, _ = elevated_frame(GUN_ELEV)
    gun_muzzles = []
    for side in (-1, 1):
        for offset in (-0.09, 0.09):
            tip = (Vector((0.0, 0.0, side * GUN_Z + offset))
                   + gun_d * (0.30 + BARREL_LEN))
            gun_muzzles.append(tip)
    print("    GunMuzzles           = " + ", ".join(
        f"({m.x:.5f}, {m.y:.5f}, {m.z:.5f})" for m in gun_muzzles))
    print(f"    RadarPivotFromTurret = ({radar_rel_turret.x:.5f}, {radar_rel_turret.y:.5f}, "
          f"{radar_rel_turret.z:.5f})")
    print(f"    OpticPivotFromTurret = ({eo_rel_turret.x:.5f}, {eo_rel_turret.y:.5f}, "
          f"{eo_rel_turret.z:.5f})")

    emitted = {}
    sidewinder.report(emitted)
    ciws.report(emitted)
    optic.report(emitted)
    bombrack.report(emitted)

    with open(os.path.join(out_dir, "muzzles.json"), "w") as fh:
        json.dump({
            **emitted,
            "tubes": [[round(m.x, 5), round(m.y, 5), round(m.z, 5)] for m in firing],
            "muzzle_forward_offset": round(mean_x, 3),
            "tube_ring_radius": round(ring, 3),
            "turret_pivot": [round(v, 5) for v in TURRET_PIVOT],
            "pod_pivot": [round(v, 5) for v in POD_PIVOT],
            "gun_pivot_from_turret": [round(v, 5) for v in gun_rel_turret],
            "gun_reference_elevation_deg": round(math.degrees(GUN_ELEV), 3),
            "gun_muzzles": [[round(m.x, 5), round(m.y, 5), round(m.z, 5)] for m in gun_muzzles],
            "pod_pivot_from_turret": [round(v, 5) for v in pod_rel_turret],
            "pod_reference_elevation_deg": round(math.degrees(POD_ELEV), 3),
            "radar_pivot": [round(v, 5) for v in RADAR_PIVOT],
            "radar_pivot_from_turret": [round(v, 5) for v in radar_rel_turret],
            "eo_pivot_from_turret": [round(v, 5) for v in eo_rel_turret],
        }, fh, indent=2)


def main():
    new_scene()
    build_chassis()
    build_turret()

    # Last, so the shared box jitter reshuffles nothing above it: the generator runs off one
    # seed, and inserting a box moves every box drawn after it onto different planes.
    sidewinder.build(sys.modules[__name__])
    ciws.build(sys.modules[__name__])
    bombrack.build(sys.modules[__name__])
    optic.build(sys.modules[__name__])

    # Render *before* export. Exporting recentres the turret and pod meshes onto their slew
    # pivots, which is right for the game and wrong for a picture: afterwards the scene shows
    # the pods hanging through the chassis and the turret shifted forward. Previews are only
    # useful if they show the vehicle assembled.
    if POSE:
        pose_assemblies(POSE.get("bearing", 0.0), POSE.get("elev", 0.0))
    render_previews(OUT_DIR)

    # A posed scene exports a posed atlas: same vertex buffers, different node transforms. The
    # runtime composes poses itself from the rest library, so that file is never one to install.
    if EXPORT and not POSE:
        glb = os.path.join(OUT_DIR, "KSArmory_MeshAtlas.glb")
        export(glb)
        print("EXPORT_OK", glb)

    report_muzzles(OUT_DIR)
    print("DONE")


main()
