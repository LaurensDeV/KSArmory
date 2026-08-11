"""
Builds a Phalanx-pattern CIWS, into the shared atlas.

Imported by pantsir.py rather than run on its own, and built after everything else there, because
the box jitter runs off one seed and inserting a primitive reshuffles every one drawn after it.

## Why it is 3 m and not 2

The real thing is 4.7 m tall on a base of about 2.8 m. KSA's stack sizes are 0.125, 0.25, 0.5, 1,
2 and 3 m, so a 3 m node is within 7% of the real base width: the part can be modelled at true
scale with the mounting flange flaring slightly to meet the node, which is what a deck mount does
anyway. At 2 m it would have to be squashed to 3.4 m to keep its proportions, or left 4.7 m tall
on a 2 m base and read as lanky.

## Coordinate system

Part space, as everywhere else: +X up the stack, +Y the direction the gun faces at rest, +Z its
right. The origin is the mounting face, so every height reads as a height and the connector sits
at zero.

## Anatomy, bottom to top

    flange              fixed, bolted to whatever it stands on
    barbette            traverses
    cheeks              traverse; two blocks either side, carrying the elevation trunnion
    gun mount           elevates between the cheeks: housing, barrels, and the radome above them

**The radome elevates with the gun.** It carries the track antenna, which has to stay boresighted
with the barrels, so the housing and the dome are one rigid body swinging between the cheeks --
the whole head leans back as the gun comes up. Splitting them puts the dome on the traverse and
leaves a gun that appears to articulate at the barrels alone, which is a mount the real one does
not have.

Everything follows from where the trunnion sits: just under the dome, with the gun slung forward
and below it. That is what keeps the swing sane -- a dome perched a long way above its own pivot
would scythe through a two-metre arc on the way to 85 degrees.

## What holds the clearances

Elevation turns about +Z, so **a gap in Z survives every pose** and nothing else does. The dome
and the housing are both narrower than the gap between the cheeks, which is the entire reason
the head can swing between them at any elevation without a sweep having to prove it pose by pose.
"""

import math

# ---------------------------------------------------------------------------
# Dimensions. Published figures for the Mk 15: 4.7 m tall, ~2.8 m base, M61A2 with six 20 mm
# barrels of about 1.52 m.
# ---------------------------------------------------------------------------

TOTAL_HEIGHT = 4.70
NODE_DIAMETER = 3.00                     # the stack node it mates with
BASE_DIAMETER = 2.80

FLANGE_TOP = 0.14

BARBETTE_BOTTOM = 0.10                   # into the flange, never onto its face
BARBETTE_TOP = 1.30
BARBETTE_R = 1.02

# The two blocks the gun swings between. Their inner faces are what every clearance below is
# measured against.
CHEEK_BOTTOM = 1.24                      # into the barbette
CHEEK_TOP = 2.46
CHEEK_Z = 1.03                           # centre of each
CHEEK_HALF_Z = 0.17                      # so the inner faces sit at 0.86
CHEEK_Y = (-0.56, 0.66)

TRUNNION_X = 2.30                        # elevation axis, near the top of the cheeks

# The elevating housing. Narrower than the cheek gap by 0.22 a side.
HOUSING_X = (1.68, 2.82)
HOUSING_Y = (-0.66, 0.60)
HOUSING_HALF_Z = 0.64

# Reaches out to the cheeks' inner faces, which is what holds the elevating group on: coaxial with
# the swing, so the overlap is the same at every elevation and no pose can pull it free.
TRUNNION_R = 0.23
TRUNNION_HALF_Z = 0.90                   # past the cheek inner face at 0.86

RADOME_R = 0.76                          # inside the cheek gap, so it clears at every elevation
RADOME_BOTTOM = 2.74                     # into the housing
RADOME_SHOULDER = 4.44                   # where the cylinder gives way to the cap
RADOME_CAP_R = 0.78                      # as a fraction of RADOME_R

# The radome is a DRUM with a gently rounded top, not a nose cone. A steep taper turns the
# most recognisable part of the silhouette into a missile.

BARREL_LEN = 1.52
BARREL_R = 0.026                         # 20 mm, plus wall
CLUSTER_R = 0.105                        # centre to each barrel
CLUSTER_DROP = 0.30                      # below the trunnion, so the gun is slung under the dome
BARREL_ROOT_Y = 0.46                     # just inside the housing, so the roots are not on its face

# Level, and that is the working pose rather than a stowed one: a CIWS earns its keep against
# sea-skimmers, so a refused elevation write leaves it pointing where it is most wanted. It also
# leaves the dome upright, which a canted reference would not.
GUN_ELEV = 0.0

# The whole mount turns about the stack centreline, so only the height matters here.
TURRET_PIVOT = (BARBETTE_BOTTOM, 0.0, 0.0)
GUN_PIVOT = (TRUNNION_X, 0.0, 0.0)

# Sections run *into* each other by this much rather than meeting. Two coaxial cylinders that
# share a cap plane z-fight, and cyl() gets none of the jitter that saves the boxes -- so every
# junction below is an overlap, never an abutment.
OVERLAP = 0.05

GROUPS = ("ciwsbase", "ciwsturret", "ciwsguns")


def build(m):
    """Adds the three assemblies to `m`'s scene. `m` is the pantsir module."""
    for name in GROUPS:
        m._objects[name] = []

    _build_base(m)
    _build_barbette(m)
    _build_gun(m)


def _build_base(m):
    """The mounting flange. Fixed: everything above it turns."""
    m._group = "ciwsbase"

    # Flares from the node to the mount's own width, so it sits on a 3 m stack without a step.
    m.cone(NODE_DIAMETER / 2, BASE_DIAMETER / 2, FLANGE_TOP,
           (FLANGE_TOP / 2, 0.0, 0.0), m.axis_x(), "steel_dark", verts=28)


def _build_barbette(m):
    """The traversing drum, and the two cheeks the gun swings between."""
    m._group = "ciwsturret"

    m.cyl(BARBETTE_R, BARBETTE_TOP - BARBETTE_BOTTOM,
          ((BARBETTE_BOTTOM + BARBETTE_TOP) / 2, 0.0, 0.0), m.axis_x(), "hull", verts=24)

    # Bearing race it turns on, narrower than the drum so neither shares its plane.
    m.cyl(BARBETTE_R * 0.86, 0.10, (BARBETTE_BOTTOM + 0.04, 0.0, 0.0),
          m.axis_x(), "metal", verts=20)

    # Ammunition feed's shoulder, aft. A Phalanx is not symmetrical front to back, and the bulge
    # is most of what says which way it is facing when the barrels are level.
    m.span((0.45, BARBETTE_TOP + 0.16), (-1.08, -0.55), (-0.46, 0.46), "hull_dark")

    for side in (-1, 1):
        m.span((CHEEK_BOTTOM, CHEEK_TOP), CHEEK_Y,
               (side * CHEEK_Z - CHEEK_HALF_Z, side * CHEEK_Z + CHEEK_HALF_Z), "hull")


def _build_gun(m):
    """The elevating head: housing, the M61's six barrels, and the radome above them."""
    m._group = "ciwsguns"

    d, p = m.elevated_frame(GUN_ELEV)

    m.span(HOUSING_X, HOUSING_Y, (-HOUSING_HALF_Z, HOUSING_HALF_Z), "hull_dark")

    # Bearing bosses out to the cheeks. These are what the head actually hangs from, so they live
    # in the moving group rather than on the cheeks: a boss on the cheek reaching inward stops
    # short of a housing narrower than the gap, and then nothing connects the two at all.
    inner = HOUSING_HALF_Z - 0.06
    for side in (-1, 1):
        m.cyl(TRUNNION_R, TRUNNION_HALF_Z - inner,
              (TRUNNION_X, 0.08, side * (TRUNNION_HALF_Z + inner) / 2),
              (0.0, 0.0, 0.0), "steel_dark", verts=16)

    # The radome. White fibreglass, and the reason the thing is recognisable at all. No collar
    # under it: anything spanning that junction is wide at the bottom to meet the housing, and a
    # short primitive wider than the tall one above it is the "cover standing proud" defect
    # checkswept exists to catch. The step from grey box to white drum is shoulder enough.
    m.cyl(RADOME_R, RADOME_SHOULDER - RADOME_BOTTOM,
          ((RADOME_BOTTOM + RADOME_SHOULDER) / 2, 0.0, 0.0), m.axis_x(), "radar", verts=22)

    # Domed cap, as a cone to a small flat rather than a hemisphere: fewer triangles, and at this
    # size the difference is invisible while a sphere would coincide with the cylinder's cap.
    cap_low = RADOME_SHOULDER - OVERLAP
    # Fractionally *under* the drum it caps, never over: a cap a few millimetres proud catches
    # the light as a rim all the way round, and a different facet count is what keeps the two
    # coaxial cylinders off each other's planes without needing the extra radius to do it.
    m.cone(RADOME_R * 0.995, RADOME_R * RADOME_CAP_R, TOTAL_HEIGHT - cap_low,
           ((cap_low + TOTAL_HEIGHT) / 2, 0.0, 0.0), m.axis_x(), "radar", verts=20)

    # A shallow crown, so the dome finishes round rather than cut off flat. Shallow is the point:
    # a steep cap turns the most recognisable part of the silhouette into a nose cone.
    m.cyl(RADOME_R * RADOME_CAP_R, 0.06, (TOTAL_HEIGHT - 0.02, 0.0, 0.0), m.axis_x(), "radar",
          verts=18)

    # The gun itself, slung below the trunnion.
    axis_x = TRUNNION_X - CLUSTER_DROP
    m.cyl(0.28, 0.46, (axis_x, BARREL_ROOT_Y + 0.14, 0.0), m.pitched(GUN_ELEV),
          "steel_dark", verts=16)

    # Muzzle clamp, which on the real gun holds the barrels together at the far end.
    m.cyl(CLUSTER_R * 1.5, 0.10, (axis_x, BARREL_ROOT_Y + BARREL_LEN - 0.14, 0.0),
          m.pitched(GUN_ELEV), "metal", verts=14)

    # Six barrels around the cluster axis. Placed at their rolled positions rather than rotated
    # afterwards, because a cylinder turns about its own origin.
    root = _add(_scale(p, -CLUSTER_DROP), _scale(d, BARREL_ROOT_Y))
    root = _add(_vec(GUN_PIVOT), root)

    for index in range(6):
        roll = index * math.pi / 3.0
        offset = _add(_scale(p, math.cos(roll) * CLUSTER_R),
                      _scale((0.0, 0.0, 1.0), math.sin(roll) * CLUSTER_R))

        centre = _add(_add(root, _scale(d, BARREL_LEN / 2)), offset)
        m.cyl(BARREL_R, BARREL_LEN, _tuple(centre), m.pitched(GUN_ELEV), "steel_dark", verts=8)


def _vec(t):
    return (t[0], t[1], t[2])


def _add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def _scale(v, s):
    return (v[0] * s, v[1] * s, v[2] * s)


def _tuple(v):
    return (v[0], v[1], v[2])


def report(muzzles):
    """Emits the barrel muzzles as a C# block, and records them for validate-parts.py."""
    # Recomputed here rather than shared with the builder: this is the number the C# uses, and the
    # two agreeing by construction is what validate-parts.py is checking for.
    dx, dy = math.sin(GUN_ELEV), math.cos(GUN_ELEV)
    px, py = math.cos(GUN_ELEV), -math.sin(GUN_ELEV)

    pivot_rel_turret = (GUN_PIVOT[0] - TURRET_PIVOT[0],
                        GUN_PIVOT[1] - TURRET_PIVOT[1],
                        GUN_PIVOT[2] - TURRET_PIVOT[2])

    reach = BARREL_ROOT_Y + BARREL_LEN

    muzzle_list = []
    for index in range(6):
        roll = index * math.pi / 3.0
        radial = math.cos(roll) * CLUSTER_R - CLUSTER_DROP

        # In the gun subpart's own frame, which is the mesh recentred on GUN_PIVOT.
        muzzle_list.append((dx * reach + px * radial,
                            dy * reach + py * radial,
                            math.sin(roll) * CLUSTER_R))

    print("\n=== CIWS (paste into src/KSArmory/Sim/Arsenal.cs)")
    print("        GunMuzzles = " + ", ".join(
        f"new({x:.5f}, {y:.5f}, {z:.5f})" for x, y, z in muzzle_list))
    print(f"    TurretPivot          = ({TURRET_PIVOT[0]:.5f}, {TURRET_PIVOT[1]:.5f}, {TURRET_PIVOT[2]:.5f})")
    print(f"    GunPivotFromTurret   = ({pivot_rel_turret[0]:.5f}, {pivot_rel_turret[1]:.5f}, "
          f"{pivot_rel_turret[2]:.5f})")
    print(f"    GunReferenceElevDeg  = {math.degrees(GUN_ELEV):.3f}")
    print(f"    total height         = {TOTAL_HEIGHT:.2f} m on a {NODE_DIAMETER:.0f} m node")

    muzzles["ciws"] = {
        "gun_muzzles": [[round(v, 5) for v in mz] for mz in muzzle_list],
        "turret_pivot": [round(v, 5) for v in TURRET_PIVOT],
        "gun_pivot_from_turret": [round(v, 5) for v in pivot_rel_turret],
        "gun_reference_elevation_deg": round(math.degrees(GUN_ELEV), 3),
        "total_height": TOTAL_HEIGHT,
    }
