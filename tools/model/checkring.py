#!/usr/bin/env python3
"""Reports what KSA's flight computer will make of a thruster ring, from the XML alone.

The attitude deadband is 0.5*AngleDeadband + AngleTurnaround, and both halves are driven by
RateBit = MinRotationalImpulse / inertia. MinRotationalImpulse is not a property of one nozzle:
ThrusterController enrols a nozzle in a rotation axis whenever dot(thrust, normalize(axis x r))
exceeds 0.1, and then SUMS the torque of the whole enrolled set. So a nozzle that was never meant
to steer still coarsens the quantum that does, and the coupling is set by where the part's mass
is declared -- which defaults to the mounting face, where nothing physically is.

Nothing else in this repository can see that. The mesh is clean, the pivots agree, checkswept
finds no intersection, and the vehicle simply holds its nose less well than it could.

    ./tools/model/checkring.py                # report every ring
    ./tools/model/checkring.py --check        # ...and fail if one is above its own floor

The floor is the ring's axial nozzles alone: those have a lever arm in the radial plane whatever
the mass seat is, so their contribution cannot be designed away. Anything above it is coupling
that can be.

It also reports the *leak*: the net force a rotation command makes, per unit of the torque it was
asked for. A correctly enrolled set is a pure couple and leaks nothing. A wrongly enrolled one
translates the vehicle every time it corrects its attitude, which is how a pointing fault turns
into a velocity error -- measured on the MIRV bus at 0.378, against 0.000 once the mass was seated.

Assumes the control frame is the part frame. KSA rotates by ctrl2Body first; a part whose control
axes are turned relative to its own would need that rotation applying here.
"""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "src/KSArmory/KSArmoryAssets.xml"
GAMEDATA = ROOT / "src/KSArmory/KSArmoryGameData.xml"

ENROL_THRESHOLD = 0.1          # ThrusterController.ComputeControlMap, torque efficiency
TOLERANCE = 1.01               # how far above its own floor a ring may sit
COUPLE_TOLERANCE = 0.01        # net force per unit torque a rotation command may leak


def xyz(node, default=0.0):
    if node is None:
        return np.array([default, default, default])
    return np.array([float(node.get(k, default)) for k in "XYZ"])


def rotation(rx, ry, rz):
    cx, sx, cy, sy, cz, sz = np.cos(rx), np.sin(rx), np.cos(ry), np.sin(ry), np.cos(rz), np.sin(rz)
    return (np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
            @ np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
            @ np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]]))


def thruster_subparts(gamedata):
    """Which SubPartGameData declare a rocket -- found by what they are, not by name."""
    out = set()
    for spgd in gamedata.iter("SubPartGameData"):
        if spgd.find(".//Combustor") is not None and spgd.find(".//DeLavalNozzle") is not None:
            out.add(spgd.get("Id"))
    return out


def mass_seat(gamedata, part_id):
    for pgd in gamedata.iter("PartGameData"):
        if pgd.get("Id") != part_id:
            continue
        for mass in pgd:
            if mass.tag.endswith("Mass"):
                return xyz(mass.find("LocationAsmb"))
    return np.zeros(3)


def rings(assets, gamedata):
    """Every part carrying more than one thruster subpart, with its nozzles and its mass seat."""
    thrusters = thruster_subparts(gamedata)
    for part in assets.iter("Part"):
        nozzles = []
        for sp in part.iter("SubPart"):
            if sp.get("InstanceOf") not in thrusters:
                continue
            tr = sp.find("Transform")
            rot = tr.find("Rotation") if tr is not None else None
            nozzles.append((sp.get("Id", ""),
                            xyz(tr.find("Position")) if tr is not None else np.zeros(3),
                            rotation(*xyz(rot))))
        if len(nozzles) > 1:
            yield part.get("Id"), nozzles, mass_seat(gamedata, part.get("GameData", part.get("Id")))


def analyse(nozzles, com):
    """MinRotationalImpulse per axis, who is enrolled, and what each command leaks as translation."""
    axes = np.eye(3)
    impulse = np.zeros((3, 2))
    enrolled = [set() for _ in range(3)]
    axial = np.zeros((3, 2))
    leak = np.zeros((3, 2))
    net = [[np.zeros(3), np.zeros(3)] for _ in range(3)]
    for label, pos, rot in nozzles:
        thrust = rot @ np.array([1.0, 0.0, 0.0])
        arm = pos - com
        torque = np.cross(arm, thrust)
        # A nozzle whose thrust is along the ring's own axis keeps its lever arm in the radial
        # plane whatever the mass seat is, so it sets the floor.
        is_axial = abs(thrust[0]) > 0.5
        for a in range(3):
            lever = np.cross(axes[a], arm)
            norm = np.linalg.norm(lever)
            if norm < 1e-9:
                continue
            efficiency = float(thrust @ (lever / norm))
            if abs(efficiency) <= ENROL_THRESHOLD:
                continue
            side = 1 if efficiency > 0 else 0
            impulse[a][side] += abs(torque[a])
            enrolled[a].add(label)
            net[a][side] += thrust
            if is_axial:
                axial[a][side] += abs(torque[a])
    for a in range(3):
        for side in (0, 1):
            spin = impulse[a][side]
            leak[a][side] = np.linalg.norm(net[a][side]) / spin if spin > 1e-9 else 0.0
    return impulse.max(axis=1), axial.max(axis=1), enrolled, leak.max(axis=1)


def main():
    check = "--check" in sys.argv
    assets = ET.parse(ASSETS)
    gamedata = ET.parse(GAMEDATA)
    problems = 0
    found = 0

    for part_id, nozzles, com in rings(assets, gamedata):
        found += 1
        worst, floor, enrolled, leak = analyse(nozzles, com)
        print(f"{part_id}: {len(nozzles)} thrusters, mass seated at "
              f"X={com[0]:.3f} Y={com[1]:.3f} Z={com[2]:.3f}")
        for a, name in enumerate(("roll", "pitch", "yaw")):
            over = worst[a] > floor[a] * TOLERANCE and floor[a] > 0
            leaky = leak[a] > COUPLE_TOLERANCE
            mark = "  <-- coupled" if over else ""
            mark += "  <-- not a couple" if leaky else ""
            print(f"    {name:5s} quantum {worst[a]:6.3f}   floor {floor[a]:6.3f}   "
                  f"{len(enrolled[a]):2d} enrolled   leak {leak[a]:5.3f}{mark}")
            if over or leaky:
                problems += 1
        if problems:
            coupled = sorted(set().union(*enrolled) - {n[0] for n in nozzles if abs((n[2] @ np.array([1.0, 0, 0]))[0]) > 0.5})
            if coupled:
                print(f"    non-axial nozzles steering: {', '.join(c[-5:] for c in coupled)}")

    if not found:
        print("no thruster rings declared")
        return 0
    if check and problems:
        print(f"\nFAILED: {problems} axis/axes above the ring's own floor or leaking translation.\n"
              "A non-axial nozzle is steering. Seat the part's mass in the ring plane, or move\n"
              "the ring into the mass plane -- the coupling is the axial gap between the two.\n"
              "Leak is net force per unit torque: a rotation command that is not a pure couple\n"
              "shoves the vehicle every time it corrects its attitude.",
              file=sys.stderr)
        return 1
    print("\nring ok" if check else "")
    return 0


if __name__ == "__main__":
    sys.exit(main())
