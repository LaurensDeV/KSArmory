"""
Builds the ejector rack and its Mk 82, into the Pantsir's atlas.

Imported by pantsir.py rather than run on its own, and built *after* everything already there:
the box jitter runs off one seed, so a primitive inserted earlier reshuffles every one drawn
after it and pushes two faces somewhere else onto the same plane.

## Coordinate system

The rack is in part space, the same convention as the Sidewinder rail:

    +X  out of the surface the part attaches to
    +Y  along the host's long axis, the way the bomb points
    +Z  the host's right

The bomb is modelled the other way -- nose along +X, centred on its own origin -- because that
is the body-mesh convention the flight code expects, and it is seated onto the rack at runtime
by the tube's direction rather than by being modelled in place.

## Why the tube points forward and the bomb still falls

The tube direction is +Y, the same as the rail's, which is what hangs the round nose-forward
instead of pointing it out of the wing. Nothing about that launches it: the profile's launch
speed is zero, so the bomb leaves with the aircraft's velocity and no impulse of its own beyond
the ejector's shove away from the mount. Which way it *points* after that is read off its
velocity every frame, so it noses over as it falls, which is what a dumb bomb does.

## The fins are rolled 45 degrees, for the rail's reason

A seated round is turned by the shortest rotation carrying +X onto the tube axis -- for a tube
along +Y, a quarter turn about +Z. That leaves the mesh's own +Z alone, so a fin's roll about the
body survives into the part frame and cannot be chosen later. At 45 degrees the four land in an
X, straddling the rack; at 0 one points straight up into it.
"""

import math

# ---------------------------------------------------------------------------
# Dimensions. Mk 82: 2.22 m long, 273 mm body, 227 kg, of which 87 kg is filler.
# ---------------------------------------------------------------------------

BODY_LEN = 2.22
BODY_R = 0.1365

NOSE_LEN = 0.42                          # ogive, forward of the parallel section
TAIL_LEN = 0.46                          # conical fin cone, aft of it
FIN_SPAN = 0.44                          # tip to tip across the tail fins

BAND_X, BAND_LEN = 0.55, 0.10            # the yellow filler band, forward of centre
ROOT_INSET = 0.02                        # how far a fin root is buried in the cone
JOINT = 0.08                             # how far a cone runs into the body it meets

# The rack, from the mounting face outward.
BASE_X = 0.05                            # adapter plate against the hull
PYLON_X = 0.22
HOOK_X = 0.27                            # the two hooks the lugs hang from

RACK_Y0, RACK_Y1 = -0.62, 0.66
LUG_SPACING = 0.356                      # 14 inches, which is what makes it a 14-inch rack

# Body axis: clear of the hooks by its own radius, so the bomb hangs under them rather than
# through them.
AXIS_X = HOOK_X + BODY_R

GROUPS = ("bombrack", "mk82")


def build(m):
    """Adds both meshes to `m`'s scene. `m` is the pantsir module: primitives and group registry."""
    for name in GROUPS:
        m._objects[name] = []

    _build_rack(m)
    _build_bomb(m)


def _build_rack(m):
    """A 14-inch ejector rack on a short pylon. Nothing on it moves."""
    m._group = "bombrack"

    # Adapter plate, then the pylon it stands on. Widest at the hull so the part reads as bolted
    # down rather than balanced.
    m.span((0.0, BASE_X), (-0.50, 0.52), (-0.10, 0.10), "hull_dark")
    m.span((BASE_X, PYLON_X), (RACK_Y0 + 0.10, RACK_Y1 - 0.10), (-0.062, 0.062), "hull")

    # The rack body, deeper than the pylon: it carries the ejector cartridges.
    m.span((PYLON_X - 0.015, HOOK_X), (RACK_Y0, RACK_Y1), (-0.078, 0.078), "steel_dark")

    # Sway braces fore and aft, which is what stops a hung store swinging. Angled faces would be
    # truer and would also put two of them on the pylon's own planes.
    for at in (RACK_Y0 + 0.14, RACK_Y1 - 0.14):
        for side in (-1, 1):
            m.span((HOOK_X - 0.02, HOOK_X + 0.055),
                   (at - 0.035, at + 0.035),
                   (side * 0.055, side * 0.096), "metal")

    # The two hooks, one per suspension lug, 14 inches apart about the rack's centre.
    for at in (-LUG_SPACING / 2, LUG_SPACING / 2):
        m.span((HOOK_X - 0.01, HOOK_X + 0.038), (at - 0.026, at + 0.026), (-0.030, 0.030), "black")

    # Cartridge breech and the arming solenoid, down one side. Detail, and it breaks the slab.
    m.span((BASE_X + 0.02, PYLON_X - 0.03), (-0.30, 0.02), (0.062, 0.108), "detail")


def _build_bomb(m):
    """One Mk 82, nose along +X, origin at its centre -- the body-mesh convention.

    Sections overlap rather than butting end to end, and no two coaxial cylinders share a facet
    count: two caps on one plane z-fight, and cyl() gets none of the jitter that saves the boxes.
    """
    m._group = "mk82"
    half = BODY_LEN / 2

    parallel_front = half - NOSE_LEN
    parallel_back = -half + TAIL_LEN

    # Ogive nose, closed with a small cap rather than a point: a cone tip is one vertex with no
    # UV area around it, which is the NaN-shading defect checkmesh exists to catch.
    #
    # Sunk into the body rather than butted against it. Two coaxial sections meeting exactly at a
    # plane put an end cap on an end cap, which z-fights; the cone tapers, so once its base is
    # inside the body nothing of it is coincident with anything.
    m.cone(BODY_R, BODY_R * 0.30, NOSE_LEN + JOINT,
           (parallel_front + NOSE_LEN / 2 - JOINT / 2, 0.0, 0.0), m.axis_x(), "missile", verts=18)
    m.sphere(BODY_R * 0.30, (half - 0.01, 0.0, 0.0), "steel_dark", segments=12, rings=6)

    # Body, in two runs with the filler band set between them. A raised band is the defect
    # checkswept flags -- a cover proud of what it covers catches the light as a rim all the way
    # round -- so it is inset and reads as a joint.
    forward = BAND_X + BAND_LEN / 2
    aft = BAND_X - BAND_LEN / 2

    m.cyl(BODY_R, parallel_front - forward, ((forward + parallel_front) / 2, 0.0, 0.0),
          m.axis_x(), "missile", verts=20)
    m.cyl(BODY_R, aft - parallel_back, ((aft + parallel_back) / 2, 0.0, 0.0),
          m.axis_x(), "missile", verts=22)

    # Runs into both body runs, where its own end caps are hidden.
    m.cyl(BODY_R - 0.005, BAND_LEN + 0.05, (BAND_X, 0.0, 0.0), m.axis_x(), "warn", verts=16)

    # Two suspension lugs, at the hook spacing, standing off the body's +Y.
    #
    # +Y and not +Z: seating turns the body's +X onto the tube axis by the shortest rotation, which
    # for a tube along part +Y is a quarter turn about +Z -- and that carries the body's +Y onto
    # part -X, which is up towards the rack. On +Z they would come out sideways.
    for at in (-LUG_SPACING / 2, LUG_SPACING / 2):
        m.span((at - 0.024, at + 0.024), (BODY_R - 0.01, BODY_R + 0.052), (-0.022, 0.022), "black")

    # Tail cone, and the ring that closes it. The Mk 82's fins are a conical assembly rather than
    # a boat tail, and the ring is what makes the silhouette read as one.
    m.cone(BODY_R * 0.98, BODY_R * 0.62, TAIL_LEN + JOINT,
           (parallel_back - TAIL_LEN / 2 + JOINT / 2, 0.0, 0.0), m.axis_x(), "steel_dark", verts=16)
    m.cyl(BODY_R * 0.66, 0.05, (-half + 0.03, 0.0, 0.0), m.axis_x(), "metal", verts=14)

    # Fuse well in the nose, and the arming vane that spins off it.
    m.cyl(BODY_R * 0.34, 0.06, (parallel_front - 0.02, 0.0, 0.0), m.axis_x(), "metal", verts=12)

    # Four tail fins in an X. See the roll note in the module docstring.
    span_len = (FIN_SPAN - 2 * BODY_R * 0.8) / 2

    for index in range(4):
        roll = math.pi / 4 + index * math.pi / 2

        # Sunk into the cone rather than butted against it: a root face exactly tangent sits
        # fractions of a millimetre off whichever facet it lands on and fights at distance.
        radius = BODY_R * 0.8 + (span_len - ROOT_INSET) / 2
        loc = (parallel_back - TAIL_LEN / 2 - 0.02,
               -radius * math.sin(roll), radius * math.cos(roll))

        m.fin(0.40, span_len + ROOT_INSET, 0.014, loc, roll, "steel_dark", 0.78, 0.30)


def report(muzzles):
    """Emits the rack's tube as a C# block, and records it for tools/validate-parts.py.

    The nose of the seated bomb, in part space, plus the direction it hangs along. A fixed
    launcher has no pods to follow, so unlike the Pantsir's parallel bundle this tube has to
    declare its own axis.
    """
    nose = (AXIS_X, BODY_LEN / 2, 0.0)

    print("\n=== Bomb rack (paste into src/KSArmory/Sim/Arsenal.cs)")
    print(f"        new(new({nose[0]:.5f}, {nose[1]:.5f}, {nose[2]:.5f}), new(0, 1, 0)),")
    print(f"    MuzzleForwardOffset  = {nose[0]:.3f}")
    print(f"    body length          = {BODY_LEN:.3f}   (MunitionProfile.BodyLength)")

    muzzles["bombrack"] = {
        "tubes": [[round(v, 5) for v in nose]],
        "tube_directions": [[0.0, 1.0, 0.0]],
        "body_length": BODY_LEN,
        "muzzle_forward_offset": round(nose[0], 3),
    }
