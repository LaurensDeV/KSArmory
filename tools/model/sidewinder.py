"""
Builds the radially mounted AIM-9J rail and its round, into the Pantsir's atlas.

Imported by pantsir.py rather than run on its own: one Blender session, one scene, one atlas,
exactly as Core keeps several prefabs in CoreElectricalA_MeshAtlas.glb. Every primitive here is
created *after* all the Pantsir's, so the shared box jitter reshuffles nothing that already ships.

## Coordinate system

The rail is in part space, the same convention as the vehicle:

    +X  out of the surface the part attaches to
    +Y  along the host's long axis, the way the missile points
    +Z  the host's right

The round is modelled the other way -- nose along +X, centred on its own origin -- because that
is what the flight code expects of any body mesh, and it is seated onto the rail at runtime by
the tube's direction rather than by being modelled in place.

## The fins are rolled 45 degrees for a reason

A seated round is turned by the shortest rotation carrying +X onto the tube axis, which for a
tube along +Y is a quarter turn about +Z. That leaves the mesh's own +Z alone, so a fin's roll
about the body survives into the part frame and cannot be chosen later. At 45 degrees the four
land in an X, straddling the rail; at 0 one points straight down through it.
"""

import math

# ---------------------------------------------------------------------------
# Dimensions. AIM-9J: 3.05 m long, 127 mm body, 630 mm across the tail wings, 78 kg.
# ---------------------------------------------------------------------------

BODY_LEN = 3.05
BODY_R = 0.0635

WING_SPAN = 0.630                        # tip to tip, rear wings
CANARD_SPAN = 0.560                      # tip to tip; the J's canards are the broad ones

BAND_X, BAND_LEN = 0.42, 0.13            # warhead section, measured from the body's centre
ROOT_INSET = 0.015                       # how far a fin root is buried in the body

# The rail, from the mounting face outward.
BASE_X = 0.04                            # adapter plate against the hull
PYLON_X = 0.19
RAIL_X = 0.29
SHOE_X = 0.345                           # the hangers the missile rides on

RAIL_Y0, RAIL_Y1 = -1.24, 1.30
RAIL_Z = 0.055

# Body axis: clear of the shoes by its own radius.
AXIS_X = SHOE_X + BODY_R

GROUPS = ("sidewinder", "aim9")


def build(m):
    """Adds both meshes to `m`'s scene. `m` is the pantsir module: primitives and group registry."""
    for name in GROUPS:
        m._objects[name] = []

    _build_rail(m)
    _build_round(m)


def _build_rail(m):
    """LAU-7 style launch rail on a short pylon. Nothing on it moves."""
    m._group = "sidewinder"

    # Adapter plate, then the pylon it stands on. Widest at the hull so the part reads as bolted
    # down rather than balanced.
    m.span((0.0, BASE_X), (-0.62, 0.62), (-0.085, 0.085), "hull_dark")
    m.span((BASE_X, PYLON_X), (-0.44, 0.46), (-0.048, 0.048), "hull")

    # Avionics box down one side of the pylon: the umbilical and the seeker's cooling bottle.
    m.span((0.05, 0.17), (-0.34, 0.12), (0.048, 0.105), "detail")

    # The rail itself.
    m.span((PYLON_X, RAIL_X), (RAIL_Y0, RAIL_Y1), (-RAIL_Z, RAIL_Z), "steel_dark")

    # Two hanger shoes the missile hangs from, and a detent at the aft end that holds it until
    # the motor builds thrust.
    for at in (-0.42, 0.44):
        m.span((RAIL_X, SHOE_X), (at - 0.075, at + 0.075), (-0.032, 0.032), "metal")

    m.span((RAIL_X, SHOE_X - 0.012), (RAIL_Y0 + 0.03, RAIL_Y0 + 0.16), (-0.026, 0.026), "black")

    # Blast plate under the nose end, where the exhaust washes the rail on the way off.
    m.span((PYLON_X - 0.012, PYLON_X + 0.004), (0.30, RAIL_Y1), (-0.09, 0.09), "steel_dark")


def _build_round(m):
    """One AIM-9J, nose along +X, origin at its centre -- the body-mesh convention.

    Sections overlap rather than butting end to end, and no two coaxial cylinders share a facet
    count: two caps on one plane z-fight, and cyl() gets none of the jitter that saves the boxes.
    """
    m._group = "aim9"
    half = BODY_LEN / 2

    # Seeker: a glass dome on the short ogive that carries it.
    m.sphere(BODY_R * 0.92, (half - BODY_R * 0.86, 0.0, 0.0), "glass", segments=16, rings=8)
    m.cone(BODY_R, BODY_R * 0.86, 0.30, (half - 0.20, 0.0, 0.0), m.axis_x(), "missile", verts=16)

    # Body, in two runs with the warhead band recessed between them.
    #
    # A real band is painted on and flush. Colour here comes from which swatch a face projects
    # onto, so it has to be its own geometry -- and a *raised* ring is the defect checkswept
    # exists to catch, because a cover a few millimetres proud of what it covers catches the
    # light as a rim all the way round. Set into the body it reads as a joint instead, and the
    # two body runs never overlap so their shared radius costs nothing.
    forward = BAND_X + BAND_LEN / 2
    aft = BAND_X - BAND_LEN / 2

    m.cyl(BODY_R, (half - 0.20) - forward, ((forward + half - 0.20) / 2, 0.0, 0.0),
          m.axis_x(), "missile", verts=18)
    m.cyl(BODY_R, aft - (-half + 0.02), ((aft - half + 0.02) / 2, 0.0, 0.0),
          m.axis_x(), "missile", verts=20)

    # Runs into both body sections, where its own end caps are hidden.
    m.cyl(BODY_R - 0.006, BAND_LEN + 0.06, (BAND_X, 0.0, 0.0), m.axis_x(), "warn", verts=16)

    # Motor nozzle.
    m.cyl(BODY_R * 0.78, 0.09, (-half + 0.03, 0.0, 0.0), m.axis_x(), "steel_dark", verts=12)
    m.cyl(BODY_R * 0.52, 0.06, (-half - 0.005, 0.0, 0.0), m.axis_x(), "black", verts=10)

    # Canards forward and wings aft, in the same four planes -- see the roll note in the module
    # docstring for why 45 degrees is not a style choice.
    canard = (CANARD_SPAN - 2 * BODY_R) / 2
    wing = (WING_SPAN - 2 * BODY_R) / 2

    for index in range(4):
        roll = math.pi / 4 + index * math.pi / 2

        # span, chord, thickness, station, radius, swatch, taper, sweep.
        #
        # The J's canards are the broad low-sweep ones that distinguish it from a B; the tail
        # wings keep the family's long swept planform.
        for span_len, chord, thick, at, swatch, taper, sweep in (
                (canard, 0.34, 0.016, half - 0.60, "missile", 0.55, 0.38),
                (wing, 0.42, 0.018, -half + 0.30, "missile", 0.52, 0.46)):
            # Sunk into the body rather than butted against it. A root face exactly tangent to
            # the body sits fractions of a millimetre off whichever facet it lands on, which
            # fights at distance; buried, it has nothing to fight. The exposed span is what the
            # airframe figures name, so the fin is built that much longer.
            radius = BODY_R + (span_len - ROOT_INSET) / 2
            loc = (at, -radius * math.sin(roll), radius * math.cos(roll))
            m.fin(chord, span_len + ROOT_INSET, thick, loc, roll, swatch, taper, sweep)

        # Rolleron: the free-spinning wheel in the outboard trailing corner of each tail wing,
        # which is what makes a Sidewinder's tail recognisable at all. Its axis is normal to the
        # wing, so the wheel turns in the wing's own plane -- a quarter turn off the roll.
        tip = BODY_R + wing
        m.cyl(0.030, 0.034, (-half + 0.16, -tip * math.sin(roll), tip * math.cos(roll)),
              (roll - math.pi / 2, 0.0, 0.0), "black", verts=10)


def report(muzzles):
    """Emits the rail's tube as a C# block, and records it for tools/validate-parts.py.

    The nose of the seated round, in part space, plus the direction it leaves along. A fixed
    launcher has no pods to follow, so unlike the Pantsir's parallel bundle this tube has to
    declare its own axis.
    """
    nose = (AXIS_X, BODY_LEN / 2, 0.0)

    print("\n=== Sidewinder rail (paste into src/KSArmory/Sim/Arsenal.cs)")
    print(f"        new(new({nose[0]:.5f}, {nose[1]:.5f}, {nose[2]:.5f}), new(0, 1, 0)),")
    print(f"    MuzzleForwardOffset  = {nose[0]:.3f}")
    print(f"    body length          = {BODY_LEN:.3f}   (MunitionProfile.BodyLength)")

    muzzles["sidewinder"] = {
        "tubes": [[round(v, 5) for v in nose]],
        "tube_directions": [[0.0, 1.0, 0.0]],
        "body_length": BODY_LEN,
        "muzzle_forward_offset": round(nose[0], 3),
    }
