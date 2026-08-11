"""
Builds a standalone electro-optical director, into the Pantsir's atlas.

Imported by pantsir.py rather than run on its own, and built after everything already there: the
box jitter runs off one seed, so a primitive inserted earlier reshuffles every box drawn after it
and can push two faces in an unrelated assembly onto the same plane.

## What it is

The sight, as its own part. The Pantsir carried one on its turret and nothing else could; this
bolts to anything, finds its own targets and drives the player's view. A craft with no weapons at
all can carry one and be an observation post.

## Coordinate system

Part space, the same convention as every other part here:

    +X  out of the surface it attaches to -- "up" for a director on a deck
    +Y  along the host's long axis
    +Z  the host's right

## Two bodies, and why the head is recentred

The base is fixed and the head slews. A head is aimed *freely* rather than about named axes --
`Sim/PointingDrive.cs` turns it by the shortest rotation onto the commanded direction, which has
no singularity looking straight up. That is what an air-defence sight spends its time doing, and
it is why this is a pointing head rather than a second turret.

A subpart rotates about its own mesh origin, so the head is exported recentred on `HEAD_PIVOT`
and put back by `<Position>` in the asset XML. Without that it would swing around the base like a
wrecking ball.

## Nothing here uses box()

Every primitive is a cylinder, cone or sphere, none of which consumes box jitter. So this module
can be reordered, or dropped entirely, without disturbing a single face anywhere else in the
atlas -- which is the property the Pantsir's own optic head had, and the reason lifting it out of
that model costs nothing.
"""

import math

# ---------------------------------------------------------------------------
# Dimensions. Modelled on a naval EO director: a compact ball on a short mast, small enough to
# bolt to anything and still read as an instrument rather than a lump.
# ---------------------------------------------------------------------------

FLANGE_R = 0.30
FLANGE_TOP = 0.07

MAST_R = 0.125

# Into the flange, never onto its face. Two primitives meeting exactly at a plane z-fight, and a
# coaxial pair is the easiest way to arrange it: the flange's top and the mast's bottom are the
# same constant unless one is pushed through the other.
MAST_BOTTOM = 0.02
MAST_TOP = 0.44

# Where the head turns. High enough that the ball clears the flange at every elevation, so the
# only pose the two bodies contend for is the one MIN_ELEVATION_DEG below rules out.
HEAD_PIVOT = (0.63, 0.0, 0.0)

HEAD_R = 0.215

# The window, on the head's +Y face. Its axis is the head's boresight, so the direction the mod
# points the head is the direction the lens faces, with nothing to correct.
LENS_R = 0.105
LENS_DEPTH = 0.05

# How far in front of the pivot the eye sits. Along the aim, so it slides the camera up and down
# the line of sight and contributes nothing to where the picture points -- see
# WeaponSystem.OpticOriginEcl for why that distinction is load-bearing.
EYE_FORWARD = 0.30

# How far down it can look. The lens and its hood stand about 0.29 m off the pivot, and the mast
# reaches to within 0.19 m of it, so a head pointed straight down puts its window through its own
# mount. No geometry fixes that -- a ball on a mast cannot see past what holds it up -- so it is a
# limit, which is what a real director has for the same reason.
MIN_ELEVATION_DEG = -20.0
MAX_ELEVATION_DEG = 85.0

TOTAL_HEIGHT = HEAD_PIVOT[0] + HEAD_R

GROUPS = ("opticbase", "optichead")


def build(m):
    """Adds both assemblies to `m`'s scene. `m` is the pantsir module."""
    for name in GROUPS:
        m._objects[name] = []

    _build_base(m)
    _build_head(m)


def _build_base(m):
    """Flange and mast. Fixed: everything above the pivot turns."""
    m._group = "opticbase"

    # Flares off the mounting face so the part does not sit on a bare disc. A cone rather than a
    # cylinder, so it cannot share a plane with the mast above it whatever the radii do.
    m.cone(FLANGE_R, FLANGE_R * 0.72, FLANGE_TOP, (FLANGE_TOP / 2, 0.0, 0.0),
           m.axis_x(), "steel_dark", verts=26)

    # The mast. A different facet count from the flange: a coaxial pair at the same count puts
    # facet edges on the same planes however the radii differ.
    m.cyl(MAST_R, MAST_TOP - MAST_BOTTOM, ((MAST_BOTTOM + MAST_TOP) / 2, 0.0, 0.0),
          m.axis_x(), "hull", verts=18)

    # No yoke. Shoulders either side of the pivot would sit inside the shell the lens and hood
    # sweep, adding a second clearance constraint to a part whose only real one is how far it can
    # look down. The mast reaches into the ball instead, which carries it with nothing beside it.


def _build_head(m):
    """The gimballed ball and its window. Recentred on HEAD_PIVOT at export."""
    m._group = "optichead"

    m.sphere(HEAD_R, HEAD_PIVOT, "hull_dark", segments=22, rings=12)

    # The window, proud of the ball on the boresight. Set slightly into it so the two surfaces
    # intersect rather than meeting tangentially, which is where coplanar trouble lives.
    lens_y = HEAD_PIVOT[1] + HEAD_R - LENS_DEPTH * 0.35
    m.cyl(LENS_R, LENS_DEPTH, (HEAD_PIVOT[0], lens_y, HEAD_PIVOT[2]),
          m.axis_y(), "glass", verts=16)

    # A hood over the window. Wider than the lens and shorter than the ball, so it reads as a
    # shade rather than as a rim standing proud of what it covers.
    m.cone(LENS_R * 1.32, LENS_R * 1.12, 0.045,
           (HEAD_PIVOT[0] + 0.012, lens_y + LENS_DEPTH * 0.6, HEAD_PIVOT[2]),
           m.axis_y(), "metal", verts=14)


def report(muzzles):
    """Emits the geometry as a C# block, and records it for validate-parts.py."""
    print()
    print("  Optic director, for Sim/Arsenal.cs:")
    print()
    print(f"        HeadPivot = new({HEAD_PIVOT[0]:.5f}, {HEAD_PIVOT[1]:.5f}, {HEAD_PIVOT[2]:.5f}),")
    print(f"        EyeForward = {EYE_FORWARD:.3f}f,")
    print(f"        MinElevationDeg = {MIN_ELEVATION_DEG:.0f}f,")
    print(f"        MaxElevationDeg = {MAX_ELEVATION_DEG:.0f}f,")
    print()
    print(f"    (total height {TOTAL_HEIGHT:.2f} m)")

    muzzles["optic"] = {
        "head_pivot": [round(v, 5) for v in HEAD_PIVOT],
        "eye_forward": round(EYE_FORWARD, 5),
        "head_radius": round(HEAD_R, 5),
        "min_elevation_deg": MIN_ELEVATION_DEG,
        "max_elevation_deg": MAX_ELEVATION_DEG,
        "total_height": round(TOTAL_HEIGHT, 5),
    }
