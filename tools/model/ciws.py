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

    flange + pedestal   fixed, bolted to whatever it stands on
    housing             traverses; the ammunition drum and the mount's bulk
    barrels             elevate on the housing's front
    radome              the white fibreglass dome carrying search and track antennas

The silhouette is the point. A Phalanx is recognised by a white drum on top of a grey barrel with
a gun sticking out of it, which is why the radome is a separate swatch and why the barrels
protrude as far as they really do.
"""

import math

# ---------------------------------------------------------------------------
# Dimensions. Published figures for the Mk 15: 4.7 m tall, ~2.8 m base, M61A2 with six 20 mm
# barrels of about 1.52 m.
# ---------------------------------------------------------------------------

TOTAL_HEIGHT = 4.70
NODE_DIAMETER = 3.00                     # the stack node it mates with
BASE_DIAMETER = 2.80

FLANGE_TOP = 0.12
PEDESTAL_TOP = 0.68

HOUSING_BOTTOM = PEDESTAL_TOP
HOUSING_TOP = 2.38
HOUSING_R = 0.95

COLLAR_TOP = 2.58
RADOME_R = 0.80
RADOME_SHOULDER = 4.34                   # where the cylinder gives way to the cap

# The radome is a DRUM with a gently rounded top, not a nose cone. A steep taper turns the
# most recognisable part of the silhouette into a missile, which is what the first pass did.

BARREL_LEN = 1.52
BARREL_R = 0.026                         # 20 mm, plus wall
CLUSTER_R = 0.105                        # centre to each barrel
GUN_ELEV = math.radians(15.0)            # modelled pose; the drives rotate away from it

# The gun elevates about a trunnion on the front of the housing.
# Proud of the housing's front, not buried in it: the gun assembly hangs off the mount and
# is most of what reads as a Phalanx from the side.
GUN_PIVOT = (1.34, 0.86, 0.0)

# The whole mount turns about the stack centreline, so only the height matters here.
TURRET_PIVOT = (HOUSING_BOTTOM, 0.0, 0.0)

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
    _build_housing(m)
    _build_barrels(m)


def _build_base(m):
    """Flange and pedestal. Fixed: everything above this turns."""
    m._group = "ciwsbase"

    # Flares from the node to the mount's own width, so it sits on a 3 m stack without a step.
    m.cone(NODE_DIAMETER / 2, BASE_DIAMETER / 2, FLANGE_TOP,
           (FLANGE_TOP / 2, 0.0, 0.0), m.axis_x(), "steel_dark", verts=28)

    # Stops short of the housing's underside. The two are separate bodies, so an abutment is a
    # shared plane that no per-mesh check can see -- only validate-parts.py compares across
    # subparts, and it is what caught this. The traverse is about +X, so a gap in X holds at
    # every bearing.
    low = FLANGE_TOP - OVERLAP
    top = PEDESTAL_TOP - 0.06
    m.cyl(BASE_DIAMETER / 2 * 0.92, top - low, ((low + top) / 2, 0.0, 0.0),
          m.axis_x(), "hull_dark", verts=26)

    # Bearing race the housing turns on. Narrower than both so neither shares its plane.
    m.cyl(HOUSING_R * 0.86, 0.10, (PEDESTAL_TOP - 0.02, 0.0, 0.0),
          m.axis_x(), "metal", verts=22)


def _build_housing(m):
    """The traversing bulk, and the radome above it."""
    m._group = "ciwsturret"

    body_h = HOUSING_TOP - HOUSING_BOTTOM
    m.cyl(HOUSING_R, body_h, ((HOUSING_BOTTOM + HOUSING_TOP) / 2, 0.0, 0.0),
          m.axis_x(), "hull", verts=24)

    # Ammunition drum's shoulder, aft. A Phalanx is not symmetrical front to back and the bulge
    # is most of what says which way it is facing when the barrels are level.
    m.span((HOUSING_BOTTOM + 0.25, HOUSING_TOP - 0.20), (-1.05, -0.55), (-0.62, 0.62), "hull_dark")

    # No collar between the housing and the dome. Anything spanning that junction is wide at the
    # bottom to meet the housing, and a short primitive wider than the tall one above it is the
    # "cover standing proud" defect checkswept exists to catch -- a cone included, since it reads
    # by its widest radius. The step from 1.9 m to 1.6 m and grey to white is shoulder enough.
    # The radome. White fibreglass, and the reason the thing is recognisable at all.
    dome_low = HOUSING_TOP - OVERLAP
    m.cyl(RADOME_R, RADOME_SHOULDER - dome_low,
          ((dome_low + RADOME_SHOULDER) / 2, 0.0, 0.0), m.axis_x(), "radar", verts=22)

    # Domed cap, as a cone to a small flat rather than a hemisphere: fewer triangles, and at this
    # size the difference is invisible while a sphere would coincide with the cylinder's cap.
    cap_low = RADOME_SHOULDER - OVERLAP
    m.cone(RADOME_R * 1.005, RADOME_R * 0.62, TOTAL_HEIGHT - cap_low,
           ((cap_low + TOTAL_HEIGHT) / 2, 0.0, 0.0), m.axis_x(), "radar", verts=22)

    # And a shallow crown on top of that, so the dome finishes round rather than cut off flat.
    m.cyl(RADOME_R * 0.62, 0.06, (TOTAL_HEIGHT - 0.02, 0.0, 0.0), m.axis_x(), "radar", verts=18)

    # Trunnion housings the barrels swing in, one each side.
    for side in (-1, 1):
        m.cyl(0.20, 0.16, (GUN_PIVOT[0], GUN_PIVOT[1] * 0.55, side * 0.42),
              m.axis_y(), "steel_dark", verts=14)


def _build_barrels(m):
    """The M61's six barrels and the cradle carrying them."""
    m._group = "ciwsguns"

    d, p = m.elevated_frame(GUN_ELEV)
    pivot = _vec(GUN_PIVOT)

    # The gun housing: chunky, because on the real mount it is a substantial box hanging off the
    # front rather than a sleeve the barrels pass through.
    root = _add(pivot, _scale(d, 0.02))
    m.box((0.56, 0.78, 0.62), _tuple(_add(root, _scale(d, 0.34))), m.pitched(GUN_ELEV), "hull_dark")
    m.cyl(0.24, 0.30, _tuple(_add(root, _scale(d, 0.74))), m.pitched(GUN_ELEV),
          "steel_dark", verts=16)

    # Muzzle clamp, which on the real gun holds the barrels together at the far end.
    m.cyl(CLUSTER_R * 1.5, 0.10, _tuple(_add(root, _scale(d, 0.80 + BARREL_LEN - 0.16))),
          m.pitched(GUN_ELEV), "metal", verts=14)

    # Six barrels around the cluster axis. Placed at their rolled positions rather than rotated
    # afterwards, because a cylinder turns about its own origin.
    for index in range(6):
        roll = index * math.pi / 3.0
        offset = _add(_scale(p, math.cos(roll) * CLUSTER_R),
                      _scale((0.0, 0.0, 1.0), math.sin(roll) * CLUSTER_R))

        # Back into the cradle, so the barrel roots are inside it rather than on its face.
        start = 0.80 - OVERLAP
        centre = _add(_add(root, _scale(d, start + (BARREL_LEN + OVERLAP) / 2)), offset)
        m.cyl(BARREL_R, BARREL_LEN + OVERLAP, _tuple(centre), m.pitched(GUN_ELEV),
              "steel_dark", verts=8)


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
    d, p = None, None

    # Recomputed here rather than shared with the builder: this is the number the C# uses, and the
    # two agreeing by construction is what validate-parts.py is checking for.
    dx, dy = math.sin(GUN_ELEV), math.cos(GUN_ELEV)
    px, py = math.cos(GUN_ELEV), -math.sin(GUN_ELEV)

    pivot_rel_turret = (GUN_PIVOT[0] - TURRET_PIVOT[0],
                        GUN_PIVOT[1] - TURRET_PIVOT[1],
                        GUN_PIVOT[2] - TURRET_PIVOT[2])

    reach = 0.02 + 0.80 + BARREL_LEN

    muzzle_list = []
    for index in range(6):
        roll = index * math.pi / 3.0
        ox = px * math.cos(roll) * CLUSTER_R
        oy = py * math.cos(roll) * CLUSTER_R
        oz = math.sin(roll) * CLUSTER_R

        # In the gun subpart's own frame, which is the mesh recentred on GUN_PIVOT.
        muzzle_list.append((dx * reach + ox, dy * reach + oy, oz))

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
