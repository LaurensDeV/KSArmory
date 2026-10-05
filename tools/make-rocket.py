#!/usr/bin/env python3
"""Generate KSA vehicle designs: multi-stage rockets carrying the MIRV bus, from a short spec.

The ballistic computer has been flown on essentially one stack, GeoSat FAT. This writes design
library entries (vehicle.xml + meta.toml, the shape of <KSA user dir>/Vehicles/<name>/) for other
stacks, so it can be flown on many.

Nothing about placement is copied from a saved craft: every stacked part's position comes from the
connector offsets in the part definitions, mated end to end. The only measured numbers are the two
the definitions cannot give -- where a radial decoupler sits on a tank's surface, and where a
booster sits on the decoupler -- and they come from GeoSat FAT. `--check-reference` regenerates
GeoSat FAT this way and compares it, part by part, to the craft the game saved.

    ./tools/make-rocket.py --list
    ./tools/make-rocket.py --out DIR --preset "KSA Test Liquid2" --preset "KSA Test SRB4"
    ./tools/make-rocket.py --out DIR --all-presets
    ./tools/make-rocket.py --out DIR --name "My Rocket" --stage A2x4:LF3W6HBx2 --stage A3:LF3W3HB --srb 4
    ./tools/make-rocket.py --check-reference
    ./tools/make-rocket.py --validate DIR/*/vehicle.xml

A stage is ENGINE[xCOUNT]:TANK[xCOUNT], bottom stage first. Short names drop the Core prefixes
(A2 is CorePropulsionA_Prefab_EngineA2, LF3W6HB is CoreFuelTankA_Prefab_LF3W6HB). Copy a
generated folder into <KSA user dir>/Vehicles/ to make it a design (./tools/ksa-user-dir.sh).

Python 3 standard library only. KSA_DIR overrides the game install, as for validate-parts.py.
"""

import argparse
import datetime
import math
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
MOD = REPO / "src" / "KSArmory"
KSA_DIR = Path(os.environ.get("KSA_DIR", "/mnt/c/Program Files/Kitten Space Agency"))
CORE = KSA_DIR / "Content" / "Core"

ENGINE = "CorePropulsionA_Prefab_Engine"
TANK = "CoreFuelTankA_Prefab_"
BUS = "KSArmory_Prefab_MirvBus"
BUS_DECOUPLER = "CoreCouplingA_Prefab_Decoupler3WA"
BRIDGE_3W2W = "CoreFairingA_Prefab_InterstageBridge3W2WB"
RADIAL_DECOUPLER = "CoreStructuralA_Prefab_RadialDecouplerLargeA"

# The bus's base, where GeoSat FAT has it. Any constant would do; this one makes a regenerated
# GeoSat FAT comparable with the saved one coordinate for coordinate.
BUS_BASE_X = -2.7426

# Measured off GeoSat FAT: the radial decoupler's origin on a 3 m tank, and the booster's on the
# decoupler. A surface attachment has no connector to derive these from.
RADIAL_ABOVE_TANK_BOTTOM = 3.5296
RADIAL_RADIUS = 1.916
BOOSTER_RADIUS = 3.3335

# Plates by (stage diameter, engine count, engine connector size): plate, its top connector, its
# rim connector (where an interstage hangs), and the engine connectors in symmetry order.
PLATES = {
    (3, 1, 1): ("CoreStructuralA_Prefab_EnginePlateLowProfile3WB", 0, 2, [1]),
    (3, 1, 2): ("CoreStructuralA_Prefab_EnginePlateLowProfile3WA", 0, 2, [1]),
    (3, 3, 1): ("CoreStructuralA_Prefab_EnginePlateLowProfile3WD", 0, 4, [3, 1, 2]),
    (3, 4, 1): ("CoreStructuralA_Prefab_EnginePlateLowProfile3WC", 0, 5, [2, 4, 1, 3]),
    (2, 1, 1): ("CoreStructuralA_Prefab_EnginePlateLowProfile2WA", 0, 2, [1]),
}
# Interstages by diameter: the decoupler joint is the top connector (index 1), as in GeoSat FAT.
INTERSTAGES = {3: "CoreFairingA_Prefab_Interstage3W3HA", 2: "CoreFairingA_Prefab_Interstage2W2HA"}

# Thrust and exhaust velocity. Flown numbers are read off GeoSat FAT's ICBM log lines (thrust kN,
# mass t, and the mass drop across them); the rest are scaled from those and are estimates.
# Sea-level thrust is the vacuum thrust less ambient pressure on the nozzle exit.
SEA_LEVEL_PA = 101325.0
ENGINES = {
    "A2": dict(vac=6.43e6, exit_d=2.5, ve=4170.0, source="flown (25.7 MN for four, vacuum)"),
    "A3": dict(vac=0.956e6, exit_d=2.5, ve=4700.0, source="flown (955 kN, vacuum)"),
    "A5": dict(vac=0.956e6, exit_d=2.5, ve=4700.0, source="A3's nozzle template; not flown"),
    "A6": dict(vac=0.956e6, exit_d=2.5, ve=4700.0, source="A3's nozzle template; not flown"),
    "A4": dict(vac=0.110e6, exit_d=2.2, ve=4600.0,
               source="estimate: A3 scaled by chamber pressure x throat area; not flown"),
}
# One GeoSat FAT booster: grain as saved, casing as the flown mass drop at separation implies
# (158.4 t before, 129.7 t of core and upper after, over three), thrust at liftoff.
SRB_THRUST_LIFTOFF = 1.58e6
SRB_DRY_KG = 9600.0
SRB_VE = 2560.0
SRB_GRAIN = {"CorePropulsionC_Prefab_SRBESegmentLargeA": 50057.6914,
             "CorePropulsionC_Prefab_SRBEThrustAssemblyA": 33506.2227}

# What the bus carries, as GeoSat FAT saves it.
BUS_PROPELLANT = [("MMH(l)", 51.6452, 0.3846), ("N2O4(l)", 82.6323, 0.6154)]
BUS_CHARGE_J = 50000
BUS_MASS_KG = 2750 + 134.3 + 16  # declared lump, propellant, sphere tank structure

HYDROLOX_H2 = 1.0 / 6.5  # mixture ratio 5.5, as every Core LR91 burns it
G0 = 9.80665

PRESETS = {
    "KSA Test Liquid2": dict(stages=["A2x4:LF3W6HBx2", "A3:LF3W3HB"]),
    "KSA Test Liquid3": dict(stages=["A2x4:LF3W6HBx3", "A2:LF3W6HB", "A3:LF3W3HB"]),
    "KSA Test SRB4": dict(stages=["A2x3:LF3W6HBx2", "A3:LF3W3HB"], srb=4),
    "KSA Test Light": dict(stages=["A2:LF2W4HAx2", "A4:LF2W2HA"]),
    "KSA Test Heavy": dict(stages=["A2x4:LF3W6HBx4", "A3:LF3W6HB"]),
    # Lifting off at 1.2-1.7 rather than 10-20, and every stage able to lift what is above it. A liquid
    # first stage stands on four engines: a tall stack on one A2's bell topples where it is parked and
    # breaks in three, and four A2s lift off at 4.5 or more.
    "KSA Real Liquid2": dict(stages=["A3x4:LF3W6HBx2", "A3:LF3W3HB"]),
    "KSA Real Liquid3": dict(stages=["A3x4:LF3W6HBx2", "A3:LF3W3HB", "A3:LF3W3HB"]),
    "KSA Real SRB4": dict(stages=["A2:LF3W6HBx3", "A3:LF3W3HB"], srb=4),
}

# A liquid stage that cannot lift what is above it spends its propellant against gravity, and a first
# stage far above it reaches the closed loop low and fast: flown, KSA Test Light (14.6 at liftoff, a
# 0.67 upper) spent 4.2 km/s of first stage in the air and failed at 150 and 700 km.
MIN_LIFTOFF_TWR = 1.1
MIN_STAGE_TWR = 0.9
HOT_LIFTOFF_TWR = 4.0
REFERENCE_SPEC = dict(stages=["A2x4:LF3W6HBx2", "A3:LF3W3HB"], srb=3)


# ---------------------------------------------------------------------------------------------
# Geometry

def quat_xyz(rx, ry, rz):
    """KSA's QuaternionEx.CreateFromXyzRadians, as (x, y, z, w)."""
    c1, c2, c3 = math.cos(rx / 2), math.cos(ry / 2), math.cos(rz / 2)
    s1, s2, s3 = math.sin(rx / 2), math.sin(ry / 2), math.sin(rz / 2)
    return (-c1 * s2 * s3 + c2 * c3 * s1,
            c1 * c3 * s2 + s1 * c2 * s3,
            c1 * c2 * s3 - s1 * c3 * s2,
            c1 * c2 * c3 + s1 * s2 * s3)


def rotate(q, v):
    x, y, z, w = q
    vx, vy, vz = v
    # v + 2w(q x v) + 2 q x (q x v)
    cx, cy, cz = y * vz - z * vy, z * vx - x * vz, x * vy - y * vx
    ccx, ccy, ccz = y * cz - z * cy, z * cx - x * cz, x * cy - y * cx
    return (vx + 2 * (w * cx + ccx), vy + 2 * (w * cy + ccy), vz + 2 * (w * cz + ccz))


def add(a, b):
    return tuple(p + q for p, q in zip(a, b))


def sub(a, b):
    return tuple(p - q for p, q in zip(a, b))


def mate(parent_pos, parent_rot, parent_conn, child_rot, child_conn):
    """Where a child sits when its connector meets its parent's."""
    at = add(parent_pos, rotate(quat_xyz(*parent_rot), parent_conn))
    return sub(at, rotate(quat_xyz(*child_rot), child_conn))


def wrap(angle):
    a = math.fmod(angle + math.pi, 2 * math.pi)
    return (a + 2 * math.pi if a < 0 else a) - math.pi


# ---------------------------------------------------------------------------------------------
# Part library

def metres(el):
    if el is None:
        return None
    for unit, scale in (("M", 1.0), ("Cm", 0.01), ("Mm", 0.001), ("Km", 1000.0)):
        if unit in el.attrib:
            return float(el.get(unit)) * scale
    return None


def triple(el, default=0.0):
    if el is None:
        return (default, default, default)
    return tuple(float(el.get(k, default)) for k in ("X", "Y", "Z"))


class Connector:
    def __init__(self, el):
        t = el.find("Transform")
        self.id = el.get("Id")
        self.pos = triple(t.find("Position") if t is not None else None)
        self.rot = triple(t.find("Rotation") if t is not None else None)
        scale = t.find("Scale") if t is not None else None
        self.size = float(scale.get("X", 1)) if scale is not None else 1.0
        self.flags = ""

    def normal(self):
        return rotate(quat_xyz(*self.rot), (1.0, 0.0, 0.0))


class PartDef:
    def __init__(self, el, source):
        self.id = el.get("Id")
        self.source = source
        self.connectors = [Connector(c) for c in el.findall("Connector")]
        self.subparts = [s.get("InstanceOf") for s in el.findall("SubPart")]
        self.subpart_names = [s.get("Id") for s in el.findall("SubPart")]
        self.game = None

    def index(self, connector_id):
        return next(i for i, c in enumerate(self.connectors) if c.id == connector_id)


class Library:
    def __init__(self, core=CORE, mod=MOD):
        self.parts = {}
        self.subpart_ids = set()
        self.subpart_game = {}
        self._files = {}
        files = sorted(core.glob("*.xml")) + sorted(mod.glob("KSArmory*.xml"))
        if not any(core.glob("*Assets.xml")):
            sys.exit(f"no part definitions under {core}; set KSA_DIR to the game install")
        game = {}
        for path in files:
            try:
                root = ET.parse(path).getroot()
            except ET.ParseError:
                continue
            for el in root:
                if el.tag == "Part" and el.get("Id"):
                    self.parts[el.get("Id")] = PartDef(el, path.name)
                elif el.tag == "SubPart" and el.get("Id"):
                    self.subpart_ids.add(el.get("Id"))
            for el in root.iter("PartGameData"):
                game[el.get("Id")] = el
            for el in root.iter("SubPartGameData"):
                self.subpart_game[el.get("Id")] = el
        for pid, part in self.parts.items():
            part.game = game.get(pid)
            if part.game is None:
                continue
            for gc in part.game.findall("Connector"):
                flags = gc.find("Flags")
                for c in part.connectors:
                    if c.id == gc.get("Id") and flags is not None:
                        c.flags = flags.text or ""

    def get(self, pid):
        if pid not in self.parts:
            sys.exit(f"no part named {pid} in {CORE} or {MOD}")
        return self.parts[pid]

    def decoupler_index(self, pid):
        g = self.get(pid).game
        dec = g.find("Decoupler") if g is not None else None
        return self.get(pid).index(dec.get("ConnectorId")) if dec is not None else None

    def diameter(self, pid):
        d = self.get(pid).game.find("Diameter")
        return int(round(metres(d))) if d is not None else None

    def engine_controller(self, pid):
        c = self.get(pid).game.find("RocketEngineController")
        return c.get("Id") if c is not None else None

    def declared_mass(self, pid):
        g = self.get(pid).game
        m = g.find("SolidSphereMass/Mass") if g is not None else None
        return float(m.get("Kg")) if m is not None else 0.0

    def bell_below_origin(self, pid):
        """How far below its own origin an engine's colliders reach."""
        lowest = 0.0
        for shape in self.get(pid).game.iter():
            loc = shape.find("LocationAsmb")
            if shape.tag not in ("Cylinder", "Sphere", "Box") or loc is None:
                continue
            x = float(loc.get("X", 0))
            if shape.tag == "Sphere":
                extent = metres(shape.find("Radius")) or 0.0
            elif shape.tag == "Cylinder":
                rot = triple(shape.find("Collider2Asmb"))
                along_x = abs(abs(rot[2]) - math.pi / 2) < 0.1
                extent = (metres(shape.find("LengthY")) or 0.0) / 2 if along_x else \
                    (metres(shape.find("Radius")) or 0.0)
            else:
                extent = (metres(shape.find("LengthX")) or 0.0) / 2
            lowest = min(lowest, x - extent)
        return -lowest

    def tank(self, pid):
        """(propellant kg when full of hydrolox, structure kg), off KSA's own tank geometry."""
        t = self.get(pid).game.find("Tank/CylindricalTank")
        length, r = metres(t.find("Length")), metres(t.find("OuterRadius"))
        w = metres(t.find("WallThickness"))
        h = r / math.sqrt(2.0)
        cyl = max(length, 2 * h) - 2 * h
        storage = math.pi * (r - w) ** 2 * cyl + 4.0 / 3.0 * math.pi * (r - w) ** 2 * (h - w)
        shell = math.pi * (r * r - (r - w) ** 2) * cyl \
            + 4.0 / 3.0 * math.pi * (r * r * h - (r - w) ** 2 * (h - w))
        rho_h2, rho_o2 = self.density("H2"), self.density("O2")
        prop = storage / (HYDROLOX_H2 / rho_h2 + (1 - HYDROLOX_H2) / rho_o2)
        return prop, shell * self.density("Aluminum.2014", phase="Solid", file="Materials.xml")

    def density(self, substance, phase="Liquid", file="Volatiles.xml"):
        if file not in self._files:
            self._files[file] = ET.parse(CORE / file).getroot()
        root = self._files[file]
        for s in root.iter("Substance"):
            if s.get("Id") == substance:
                return float(s.find(f"{phase}/StorageDensity").get("KgPerM3"))
        sys.exit(f"no {phase} density for {substance} in {file}")


def full_name(short, prefix):
    return short if short.startswith(("Core", "KSArmory")) else prefix + short


def parse_stage(text):
    m = re.fullmatch(r"(\w+?)(?:x(\d+))?:(\w+?)(?:x(\d+))?", text.strip())
    if not m:
        sys.exit(f"cannot read stage {text!r}: expected ENGINE[xN]:TANK[xN], e.g. A2x4:LF3W6HBx2")
    engine, n_eng, tank, n_tank = m.groups()
    return dict(engine=full_name(engine, ENGINE), engines=int(n_eng or 1),
                tank=full_name(tank, TANK), tanks=int(n_tank or 1))


# ---------------------------------------------------------------------------------------------
# The craft as a tree

class Node:
    def __init__(self, lib, pid, rot=(0.0, 0.0, 0.0)):
        self.lib, self.pid, self.rot = lib, pid, rot
        self.parent = None
        self.parent_conn = None  # index on the parent; None for a surface attachment
        self.own_conn = None     # index on this part; None if it lists no connector to its parent
        self.children = []
        self.pos = None
        self.offset = None       # fixed position relative to the parent, for a surface attachment
        self.modules = []        # (tag, attrs, children), in save order
        self.subpart_modules = {}  # subpart index -> [(tag, attrs, children)]
        self.symmetry = None     # (base node, [nodes]) on the first of a symmetric set
        self.group = 0
        self.order = 0
        self.stage = 0
        self.id = None

    def attach(self, child, parent_conn, own_conn, offset=None):
        child.parent, child.parent_conn, child.own_conn, child.offset = \
            self, parent_conn, own_conn, offset
        self.children.append(child)
        return child

    def walk(self):
        yield self
        for c in self.children:
            yield from c.walk()

    def connection(self, index):
        """The part on the far side of one of this part's connectors."""
        if self.own_conn == index and self.parent is not None:
            return self.parent
        return next((c for c in self.children if c.parent_conn == index), None)


def solve(root, root_pos=(0.0, 0.0, 0.0)):
    root.pos = root_pos
    for node in root.walk():
        if node is root:
            continue
        p = node.parent
        if node.offset is not None:
            node.pos = add(p.pos, node.offset)
        else:
            pc = node.lib.get(p.pid).connectors[node.parent_conn].pos
            cc = node.lib.get(node.pid).connectors[node.own_conn].pos
            node.pos = mate(p.pos, p.rot, pc, node.rot, cc)


def resource_groups(root):
    """KSA's ResourceGroupList.ComputeStageNumbers: which parts share propellant.

    Engines draw from the tanks whose Stage number they share, and the number is only recomputed
    in the editor, so a design loaded straight onto the pad flies on what the file says.
    """
    parts = list(root.walk())
    decouplers = [n for n in parts if n.lib.decoupler_index(n.pid) is not None]
    if not decouplers:
        return
    boundary = set(decouplers)
    far = {n.connection(n.lib.decoupler_index(n.pid)) for n in decouplers} - {None}

    def gather(start, visited):
        group, queue = [start], [start]
        while queue:
            part = queue.pop(0)
            for nb in [part.parent] + part.children:
                if nb is None or nb in far or nb in visited or nb in boundary:
                    continue
                visited.add(nb)
                group.append(nb)
                queue.append(nb)
        return group

    lists, jettisoned_all = [], []
    for start in decouplers:
        across = {start.connection(start.lib.decoupler_index(start.pid))} - {None}
        visited = {start}
        retained, jettisoned = [], []
        for nb in [start.parent] + start.children + list(across):
            if nb is None or nb in visited:
                continue
            visited.add(nb)
            if nb in boundary:
                continue
            (jettisoned if nb in across else retained).append(gather(nb, visited))
        lists.append([start] + [p for g in retained for p in g])
        jettisoned_all.extend(jettisoned)
    i = 0
    while i < len(lists):
        merged = False
        for j in range(i + 1, len(lists)):
            if set(lists[i]) & set(lists[j]):
                lists[i] += [p for p in lists[j] if p not in lists[i]]
                del lists[j]
                merged = True
                break
        if not merged:
            i += 1
    unique = []
    for lst in lists + jettisoned_all:
        if not any(set(lst) == set(u) for u in unique):
            unique.append(lst)
    kept = [lst for lst in unique
            if not any(lst is not o and len(lst) < len(o) and set(lst) <= set(o) for o in unique)]
    for number, lst in enumerate(kept):
        for part in lst:
            part.stage = number


# ---------------------------------------------------------------------------------------------
# Building a rocket

class Rocket:
    def __init__(self, lib, name, stages, srb=0):
        self.lib, self.name, self.srb = lib, name, srb
        self.stages = [parse_stage(s) if isinstance(s, str) else s for s in stages]
        self.warnings = []
        self.engines_by_stage = []
        self.tanks_by_stage = []
        self.boosters = []
        self.root = None
        self.build()

    def plate_for(self, stage):
        diameter = self.lib.diameter(stage["tank"])
        conn = self.lib.get(stage["engine"]).connectors[0]
        key = (diameter, stage["engines"], int(round(conn.size)))
        if key not in PLATES:
            options = ", ".join(f"{d}W x{n} (size-{s} engine)" for d, n, s in PLATES)
            sys.exit(f"{self.name}: no engine plate for {stage['engines']} x {stage['engine']} "
                     f"on a {diameter}W stage; have {options}")
        return PLATES[key]

    def build(self):
        lib = self.lib
        if not self.stages:
            sys.exit(f"{self.name}: no stages")
        diameters = [lib.diameter(s["tank"]) for s in self.stages]
        for d in diameters:
            if d not in INTERSTAGES:
                sys.exit(f"{self.name}: only 3W and 2W stages are supported, not {d}W")
        for lower, upper in zip(diameters, diameters[1:]):
            if upper > lower:
                sys.exit(f"{self.name}: a {upper}W stage over a {lower}W one needs an adapter "
                         "this tool does not build")
        if self.srb and (diameters[0] != 3 or "LF3W6H" not in self.stages[0]["tank"]):
            sys.exit(f"{self.name}: boosters attach as GeoSat FAT's do, to a 3W 6H first-stage tank")

        above = None  # (node, connector index) the next stage down hangs from
        for k in range(len(self.stages) - 1, -1, -1):
            stage = self.stages[k]
            tanks = []
            for i in range(stage["tanks"]):
                tank = Node(lib, stage["tank"])
                if above is None:
                    self.root = tank
                else:
                    above[0].attach(tank, above[1], 0)
                tanks.append(tank)
                above = (tank, 1)
            plate_id, top, rim, engine_conns = self.plate_for(stage)
            plate = tanks[-1].attach(Node(lib, plate_id, (0.0, 0.0, math.pi)), 1, top)
            engines = []
            for ci in engine_conns:
                engine = Node(lib, stage["engine"])
                offset = rotate(quat_xyz(*plate.rot), lib.get(plate_id).connectors[ci].pos)
                if math.hypot(offset[1], offset[2]) > 1e-6:
                    engine.rot = (wrap(math.atan2(offset[1], -offset[2])), 0.0, 0.0)
                engines.append(plate.attach(engine, ci, 0))
            if len(engines) > 1:
                engines[0].symmetry = (plate, engines)
            self.engines_by_stage.insert(0, engines)
            self.tanks_by_stage.insert(0, tanks)
            if k > 0:
                inter_id = INTERSTAGES[diameters[k]]
                dec = lib.decoupler_index(inter_id)
                inter = plate.attach(Node(lib, inter_id), rim, dec)
                bottom = 1 - dec
                stage["interstage"] = inter
                if diameters[k - 1] != diameters[k]:
                    bridge = inter.attach(Node(lib, BRIDGE_3W2W), bottom, 1)
                    above = (bridge, 0)
                else:
                    above = (inter, bottom)
        self.mount_bus(diameters[-1])
        if self.srb:
            self.add_boosters()
        solve(self.root)
        bus = next(n for n in self.root.walk() if n.pid == BUS)
        shift = (BUS_BASE_X - bus.pos[0], 0.0, 0.0)
        solve(self.root, shift)
        self.sequence()
        resource_groups(self.root)
        self.check_clearance()

    def mount_bus(self, diameter):
        lib = self.lib
        parent, conn = self.root, 0
        if diameter == 2:
            # The adapter narrows upward as it ships; turned over, it flares out to the bus.
            parent = parent.attach(Node(lib, BRIDGE_3W2W, (0.0, 0.0, math.pi)), 0, 1)
            conn = 0
        dec = parent.attach(Node(lib, BUS_DECOUPLER), conn, 1 - lib.decoupler_index(BUS_DECOUPLER))
        bus = dec.attach(Node(lib, BUS, (math.pi, 0.0, 0.0)), lib.decoupler_index(BUS_DECOUPLER), 0)
        bus.modules = [
            ("TankData", {"InstanceOf": "MirvPropellant"},
             [("RoleAffinity", {}, "Thruster")] + [mole(s, kg, f) for s, kg, f in BUS_PROPELLANT]),
            ("BatteryData", {"InstanceOf": BUS}, [("Charge", {"J": str(BUS_CHARGE_J)}, None)]),
            ("ControlData", {"LocalInstanceId": "0", "VehicleName": self.name}, None),
        ]
        for i, sub in enumerate(lib.get(BUS).subparts):
            game = lib.subpart_game.get(sub)
            thrusters = game.findall("RocketThrusterController") if game is not None else []
            if thrusters:
                bus.subpart_modules[i] = [("ThrusterController",
                                           {"InstanceOf": th.get("Id"), "ActiveInStage": "true"},
                                           None) for th in thrusters]
        self.bus_decoupler = dec

    def add_boosters(self):
        lib = self.lib
        tank = self.tanks_by_stage[0][-1]
        sets = {}
        for k in range(self.srb):
            theta = wrap(math.pi / 2 - k * 2 * math.pi / self.srb)
            rot = (theta, 0.0, 0.0)
            radial = lambda r: (0.0, r * math.sin(theta), -r * math.cos(theta))
            bottom = lib.get(tank.pid).connectors[1].pos[0]
            rd = Node(lib, RADIAL_DECOUPLER, rot)
            tank.attach(rd, None, 0, add((bottom + RADIAL_ABOVE_TANK_BOTTOM, 0, 0),
                                         radial(RADIAL_RADIUS)))
            seg = rd.attach(Node(lib, "CorePropulsionC_Prefab_SRBESegmentLargeA", rot), 1, None,
                            sub(radial(BOOSTER_RADIUS), radial(RADIAL_RADIUS)))
            motor = seg.attach(Node(lib, "CorePropulsionC_Prefab_SRBEThrustAssemblyA", rot), 1, 0)
            petal2 = seg.attach(Node(lib, "CoreFairingA_Prefab_InterstageBridgePetal2W1WA", rot), 0, 1)
            d1 = petal2.attach(Node(lib, "CorePropulsionC_Prefab_SRBDSegmentLargeA", rot), 0, 1)
            d2 = d1.attach(Node(lib, "CorePropulsionC_Prefab_SRBDSegmentLargeA", rot), 0, 1)
            petal1 = d2.attach(Node(lib, "CoreFairingA_Prefab_InterstageBridgePetal1WHalfWA", rot), 0, 1)
            seg_c = petal1.attach(Node(lib, "CorePropulsionA_Prefab_SRBCSegmentLargeA", rot), 0, 1)
            nose = seg_c.attach(Node(lib, "CoreFairingA_Prefab_NoseconeD", rot), 0, 0)
            booster = [rd, seg, motor, petal2, d1, d2, petal1, seg_c, nose]
            for i, part in enumerate(booster):
                sets.setdefault(i, []).append(part)
            self.boosters.append(booster)
        for members in sets.values():
            members[0].symmetry = (tank, members)

    def sequence(self):
        """GeoSat FAT's sequences: boosters alone, then each stage lights as the one below drops."""
        seq = 1
        if self.srb:
            for booster in self.boosters:
                rd, seg, motor, petal2, _, _, petal1, _, _ = booster
                seg.modules = [grain(SRB_GRAIN[seg.pid])]
                motor.modules = [grain(SRB_GRAIN[motor.pid]),
                                 ("SolidMotorData", {"InstanceOf": "MotorCore", "DefaultGrain": "Neutral"}, None),
                                 controller("SRBEMotor", seq)]
                for petal in (petal2, petal1):
                    petal.modules = [("DecouplerData", {"Enabled": "false", "Sequence": str(seq)}, None)]
            seq += 1
            for booster in self.boosters:
                booster[0].modules = [("DecouplerData", {"Sequence": str(seq)}, None)]
        for k, stage in enumerate(self.stages):
            for engine in self.engines_by_stage[k]:
                engine.modules = [controller(self.lib.engine_controller(engine.pid), seq)]
            if "interstage" in stage:
                stage["interstage"].modules = [("DecouplerData", {"Sequence": str(seq)}, None)]
            seq += 1
        self.bus_decoupler.modules = [("DecouplerData", {"Sequence": str(seq)}, None)]
        self.last_sequence = seq
        for tanks in self.tanks_by_stage:
            for tank in tanks:
                prop, _ = self.lib.tank(tank.pid)
                tank.modules = [("TankData", {}, [
                    ("RoleAffinity", {}, "Engine"),
                    mole("H2(l)", prop * HYDROLOX_H2, round(HYDROLOX_H2, 4)),
                    mole("O2(l)", prop * (1 - HYDROLOX_H2), round(1 - HYDROLOX_H2, 4))])]
        group = 0
        for node in self.root.walk():
            if node.symmetry and any(m[0] in ("EngineController", "DecouplerData") for m in node.modules):
                group += 1
                for i, member in enumerate(node.symmetry[1]):
                    member.group = group
        orders = {}
        for node in self.root.walk():
            seqs = [m[1].get("Sequence") for m in node.modules if "Sequence" in m[1]]
            if seqs and not any(m[1].get("Enabled") == "false" for m in node.modules):
                orders[seqs[0]] = orders.get(seqs[0], 0) + 1
                node.order = orders[seqs[0]]

    def check_clearance(self):
        for k, stage in enumerate(self.stages[1:], start=1):
            inter = stage.get("interstage")
            lower_top = self.tanks_by_stage[k - 1][0]
            top_of_lower = lower_top.pos[0] + self.lib.get(lower_top.pid).connectors[0].pos[0]
            for engine in self.engines_by_stage[k]:
                bell = engine.pos[0] - self.lib.bell_below_origin(engine.pid)
                if bell < top_of_lower - 0.1:
                    self.warnings.append(
                        f"stage {k + 1}'s {short(engine.pid)} reaches {top_of_lower - bell:.2f} m "
                        f"into stage {k}'s tank top below the {short(inter.pid)}")

    # -- mass and thrust --------------------------------------------------------------------

    def performance(self):
        lib = self.lib
        stage_mass = []
        for k, stage in enumerate(self.stages):
            prop, dry = 0.0, 0.0
            for tank in self.tanks_by_stage[k]:
                p, s = lib.tank(tank.pid)
                prop, dry = prop + p, dry + s
            dry += sum(lib.declared_mass(e.pid) for e in self.engines_by_stage[k])
            stage_mass.append((prop, dry))
        rows = []
        above = BUS_MASS_KG
        masses = []
        for k in range(len(self.stages) - 1, -1, -1):
            prop, dry = stage_mass[k]
            m0 = above + prop + dry
            masses.insert(0, (m0, m0 - prop))
            above = m0
        srb_full = self.srb * (sum(SRB_GRAIN.values()) + SRB_DRY_KG)
        liftoff = masses[0][0] + srb_full
        if self.srb:
            m1 = liftoff - self.srb * sum(SRB_GRAIN.values())
            rows.append(dict(what=f"{self.srb} boosters", m0=liftoff, thrust=self.srb * SRB_THRUST_LIFTOFF,
                             dv=SRB_VE * math.log(liftoff / m1), sl=True))
        for k, stage in enumerate(self.stages):
            perf = ENGINES[short(stage["engine"]).replace("Engine", "")]
            vac = perf["vac"] * stage["engines"]
            sl = (perf["vac"] - SEA_LEVEL_PA * math.pi * perf["exit_d"] ** 2 / 4) * stage["engines"]
            first = k == 0 and not self.srb
            m0, m1 = masses[k]
            rows.append(dict(what=f"stage {k + 1}: {stage['engines']} x {short(stage['engine'])}, "
                                  f"{stage['tanks']} x {short(stage['tank'])}",
                             m0=m0, thrust=sl if first else vac, dv=perf["ve"] * math.log(m0 / m1),
                             sl=first))
        return liftoff, rows


def grain(kg):
    return ("SolidGrainSegmentData", {"InstanceOf": "Grain"},
            [("Mole", {"SubstancePhaseId": "APCP(s)"},
              [("Mass", {"Kg": fmt(kg)}, None), ("MassFraction", {"Value": "1"}, None)])])


def mole(substance, kg, fraction):
    return ("Mole", {"SubstancePhaseId": substance},
            [("Mass", {"Kg": fmt(kg)}, None), ("MassFraction", {"Value": fmt(fraction)}, None)])


def controller(instance_of, seq):
    return ("EngineController", {"InstanceOf": instance_of, "ActiveInStage": "false",
                                 "Sequence": str(seq)}, None)


def short(pid):
    for prefix in ("CorePropulsionA_Prefab_", "CorePropulsionC_Prefab_", "CoreFuelTankA_Prefab_",
                   "CoreStructuralA_Prefab_", "CoreFairingA_Prefab_", "CoreCouplingA_Prefab_",
                   "KSArmory_Prefab_"):
        if pid.startswith(prefix):
            return pid[len(prefix):]
    return pid


def fmt(v):
    s = f"{v:.4f}".rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s


# ---------------------------------------------------------------------------------------------
# Writing

def assign_ids(root):
    """In the order KSA numbers a save: a part, its modules, its subparts, then its children."""
    counter = [1]

    def take():
        counter[0] += 1
        return counter[0] - 1

    def visit(node):
        node.id = take()
        node.module_ids = [None if m[1].get("LocalInstanceId") == "0" else take() for m in node.modules]
        node.subpart_ids = []
        for i, _ in enumerate(node.lib.get(node.pid).subparts):
            sid = take()
            node.subpart_ids.append((sid, [take() for _ in node.subpart_modules.get(i, [])]))
        for c in node.children:
            visit(c)
    visit(root)


def module_xml(parent, tag, attrs, body, mid):
    el = ET.SubElement(parent, tag)
    if "InstanceOf" in attrs:
        el.set("InstanceOf", attrs["InstanceOf"])
    if mid is not None:
        el.set("LocalInstanceId", str(mid))
    for k, v in attrs.items():
        if k != "InstanceOf":
            el.set(k, v)
    if isinstance(body, str):
        el.text = body
    elif body:
        for t, a, b in body:
            module_xml(el, t, a, b, None)
    return el


def part_xml(parent, node, tag="PartRef"):
    el = ET.SubElement(parent, tag)
    el.set("InstanceOf", node.pid)
    el.set("LocalInstanceId", str(node.id))
    el.set("Stage", str(node.stage))
    if node.group:
        el.set("SequenceGroup", str(node.group))
    if node.order:
        el.set("SequenceOrder", str(node.order))
    tr = ET.SubElement(el, "Transform")
    ET.SubElement(tr, "Position", X=fmt(node.pos[0]), Y=fmt(node.pos[1]), Z=fmt(node.pos[2]))
    ET.SubElement(tr, "Rotation", X=fmt(node.rot[0]), Y=fmt(node.rot[1]), Z=fmt(node.rot[2]))
    ET.SubElement(tr, "Scale", X="1", Y="1", Z="1")
    refs = []
    if node.parent is not None and node.own_conn is not None:
        refs.append((node.own_conn, node.parent.id))
    refs += [(c.parent_conn, c.id) for c in node.children if c.parent_conn is not None]
    for index, other in sorted(refs):
        ET.SubElement(el, "PartConnectorRef", Index=str(index), ConnectedLocalInstanceId=str(other))
    if node.symmetry:
        sym = ET.SubElement(el, "SymmetryRef")
        layer = ET.SubElement(sym, "Layer", BaseLocalInstanceId=str(node.symmetry[0].id),
                              Order=str(len(node.symmetry[1])))
        for m in node.symmetry[1]:
            ET.SubElement(layer, "PartRef").text = str(m.id)
    for c in node.children:
        part_xml(el, c)
    for i, sub in enumerate(node.lib.get(node.pid).subparts):
        sid, mids = node.subpart_ids[i]
        s = ET.SubElement(el, "SubPartRef", InstanceOf=sub, LocalInstanceId=str(sid),
                          Stage=str(node.stage))
        if node.group:
            s.set("SequenceGroup", str(node.group))
        if node.order:
            s.set("SequenceOrder", str(node.order))
        for (t, a, b), mid in zip(node.subpart_modules.get(i, []), mids):
            module_xml(s, t, a, b, mid)
    for (t, a, b), mid in zip(node.modules, node.module_ids):
        module_xml(el, t, a, b, mid)


def write(rocket, out_dir, build):
    assign_ids(rocket.root)
    doc = ET.Element("VehicleSaveData", Id=rocket.name, ActiveSequence="0")
    part_xml(doc, rocket.root, "RootPartRef")
    for n in range(1, 3 if rocket.srb else 2):
        ET.SubElement(doc, "SequenceEnvironment", Number=str(n), Environment="Atmospheric")
    ET.SubElement(doc, "LaunchGameTime")
    ET.indent(doc, "  ")
    folder = out_dir / rocket.name
    folder.mkdir(parents=True, exist_ok=True)
    xml = '<?xml version="1.0" encoding="utf-8"?>\n' + ET.tostring(doc, encoding="unicode") + "\n"
    (folder / "vehicle.xml").write_text(xml.replace(" />", " />"), encoding="utf-8")
    now = datetime.datetime.now().strftime("%Y-%m-%dT%H:%M:%S.%f") + "0"
    (folder / "meta.toml").write_text(
        f'name = "{rocket.name}"\ncreated = {now}\nupdated = {now}\n'
        f'version = "v{build}"\nsystems = [ "Sol", ]\n', encoding="utf-8")
    return folder / "vehicle.xml"


def game_build():
    lock = REPO / "ksa-assemblies.lock"
    for line in lock.read_text().splitlines():
        if line.startswith("build "):
            return line.split()[1]
    return "unknown"


# ---------------------------------------------------------------------------------------------
# Checking

def saved_parts(root_el):
    """(element, parent element) for every PartRef in a saved craft, root first."""
    out = []

    def visit(el, parent):
        out.append((el, parent))
        for c in el.findall("PartRef"):
            visit(c, el)
    visit(root_el, None)
    return out


def transform(el):
    t = el.find("Transform")
    return triple(t.find("Position")), triple(t.find("Rotation"))


def validate(path, lib, reference=None):
    """Problems with one vehicle.xml, and what a reference has that it does not."""
    problems, notes = [], []
    try:
        doc = ET.parse(path).getroot()
    except ET.ParseError as e:
        return [f"not well-formed XML: {e}"], notes
    root = doc.find("RootPartRef")
    parts = saved_parts(root)
    by_id = {}
    ids = []
    for el in doc.iter():
        if el.get("LocalInstanceId") not in (None, "0"):
            ids.append(el.get("LocalInstanceId"))
    dupes = sorted({i for i in ids if ids.count(i) > 1}, key=int)
    if dupes:
        problems.append(f"LocalInstanceIds used twice: {', '.join(dupes)}")
    for el, _ in parts:
        by_id[el.get("LocalInstanceId")] = el
    for el, parent in parts:
        pid = el.get("InstanceOf")
        if pid not in lib.parts:
            problems.append(f"{pid} is not a part in Core or KSArmory")
            continue
        subs = [s.get("InstanceOf") for s in el.findall("SubPartRef")]
        for s in subs:
            if s not in lib.subpart_ids:
                problems.append(f"{pid}: subpart {s} is not declared")
        if len(subs) > len(lib.get(pid).subparts):
            problems.append(f"{pid}: {len(subs)} SubPartRefs against {len(lib.get(pid).subparts)} "
                            "declared -- KSA indexes the definition by the save's count")
        mine = {c.get("ConnectedLocalInstanceId"): int(c.get("Index")) for c in el.findall("PartConnectorRef")}
        for other_id, index in mine.items():
            if index >= len(lib.get(pid).connectors):
                problems.append(f"{pid} #{el.get('LocalInstanceId')}: connector {index} does not exist")
                continue
            other = by_id.get(other_id)
            if other is None:
                problems.append(f"{pid} #{el.get('LocalInstanceId')}: connects to missing #{other_id}")
                continue
            if other.get("InstanceOf") not in lib.parts:
                continue
            back = {c.get("ConnectedLocalInstanceId"): int(c.get("Index"))
                    for c in other.findall("PartConnectorRef")}
            if el.get("LocalInstanceId") in back:
                a = lib.get(pid).connectors[index]
                b = lib.get(other.get("InstanceOf")).connectors[back[el.get("LocalInstanceId")]]
                pa, ra = transform(el)
                pb, rb = transform(other)
                wa = add(pa, rotate(quat_xyz(*ra), a.pos))
                wb = add(pb, rotate(quat_xyz(*rb), b.pos))
                gap = math.dist(wa, wb)
                facing = sum(x * y for x, y in zip(rotate(quat_xyz(*ra), a.normal()),
                                                    rotate(quat_xyz(*rb), b.normal())))
                if gap > 0.002:
                    problems.append(f"{short(pid)} #{el.get('LocalInstanceId')} and "
                                    f"{short(other.get('InstanceOf'))} #{other_id}: connectors "
                                    f"{gap * 1000:.1f} mm apart")
                if facing > -0.99:
                    problems.append(f"{short(pid)} #{el.get('LocalInstanceId')} and "
                                    f"{short(other.get('InstanceOf'))} #{other_id}: connectors do "
                                    f"not face each other ({facing:+.2f})")
                if abs(a.size - b.size) > 1e-6:
                    pair = sorted([(short(pid), a.size), (short(other.get("InstanceOf")), b.size)])
                    notes.append("connector sizes differ: "
                                 + " / ".join(f"{n} {z:g}" for n, z in pair))
            else:
                flags = lib.get(pid).connectors[index].flags
                if "Surface" not in flags:
                    problems.append(f"{short(pid)} #{el.get('LocalInstanceId')} lists #{other_id} "
                                    f"one way through connector {index}, which is not a surface "
                                    "connector")
    tanks = {el.get("Stage") for el, _ in parts if el.find("TankData") is not None
             and el.get("InstanceOf") != BUS}
    for el, _ in parts:
        if el.find("EngineController") is not None and el.find("SolidMotorData") is None \
                and el.get("Stage") not in tanks:
            problems.append(f"{short(el.get('InstanceOf'))} #{el.get('LocalInstanceId')} shares its "
                            f"resource group (Stage {el.get('Stage')}) with no tank")
    if reference is not None:
        def shape(tree):
            pairs = set()
            for el in tree.iter():
                for c in el:
                    pairs.add((el.tag, c.tag))
                for a in el.attrib:
                    pairs.add((el.tag, "@" + a))
            return pairs
        design, saved = reference
        missing = shape(saved.find("RootPartRef")) - shape(root)
        missing |= {("VehicleSaveData", c.tag) for c in design} - {("VehicleSaveData", c.tag) for c in doc}
        for parent_tag, child in sorted(missing):
            notes.append(f"reference has {parent_tag} > {child}; this file has none")
    return problems, sorted(set(notes))


def user_dir(given):
    if given:
        return Path(given)
    try:
        out = subprocess.run([str(REPO / "tools" / "ksa-user-dir.sh")], capture_output=True,
                             text=True, check=True).stdout.strip()
        return Path(out)
    except (OSError, subprocess.CalledProcessError):
        sys.exit("cannot find the KSA user directory; pass --user-dir")


def load_reference_save(path, vehicle):
    root = ET.parse(path).getroot()
    for v in root.iter("Vehicle"):
        if v.get("Id") == vehicle:
            return v
    sys.exit(f"no vehicle {vehicle!r} in {path}")


def check_reference(lib, udir, design_name, save_name, vehicle):
    design = udir / "Vehicles" / design_name / "vehicle.xml"
    save = udir / "saves" / save_name / "universe.xml"
    ok = True

    print("1. Connector mating, every node-connected pair in the saved files")
    for label, root in ((f"design {design_name}", ET.parse(design).getroot().find("RootPartRef")),
                        (f"save {save_name} / {vehicle}",
                         load_reference_save(save, vehicle).find("RootPartRef"))):
        worst, count = 0.0, 0
        for el, parent in saved_parts(root):
            if parent is None:
                continue
            mine = {c.get("ConnectedLocalInstanceId"): int(c.get("Index")) for c in el.findall("PartConnectorRef")}
            theirs = {c.get("ConnectedLocalInstanceId"): int(c.get("Index")) for c in parent.findall("PartConnectorRef")}
            pid, cid = parent.get("LocalInstanceId"), el.get("LocalInstanceId")
            if pid not in mine or cid not in theirs:
                continue
            ppos, prot = transform(parent)
            cpos, crot = transform(el)
            pc = lib.get(parent.get("InstanceOf")).connectors[theirs[cid]].pos
            cc = lib.get(el.get("InstanceOf")).connectors[mine[pid]].pos
            predicted = mate(ppos, prot, pc, crot, cc)
            err = math.dist(predicted, cpos)
            worst, count = max(worst, err), count + 1
        print(f"   {label}: {count} pairs, worst {worst * 1000:.2f} mm")
        ok &= worst < 0.002

    print("2. GeoSat FAT regenerated from its spec, against the saved craft")
    rocket = Rocket(lib, vehicle, **REFERENCE_SPEC)
    assign_ids(rocket.root)
    saved = [el for el, _ in saved_parts(load_reference_save(save, vehicle).find("RootPartRef"))]
    mine = list(rocket.root.walk())
    if sorted(n.pid for n in mine) != sorted(el.get("InstanceOf") for el in saved):
        print("   part lists differ:", sorted(set(n.pid for n in mine) ^ set(el.get("InstanceOf") for el in saved)))
        ok = False
    pairing, worst = {}, (0.0, None)
    free = list(saved)
    for node in mine:
        cands = [el for el in free if el.get("InstanceOf") == node.pid]
        best = min(cands, key=lambda el: math.dist(transform(el)[0], node.pos))
        free.remove(best)
        pairing[node] = best
        err = math.dist(transform(best)[0], node.pos)
        if err > worst[0]:
            worst = (err, node)
    print(f"   {len(mine)} parts, worst position {worst[0] * 1000:.2f} mm"
          + (f" ({short(worst[1].pid)})" if worst[1] else ""))
    ok &= worst[0] < 0.002

    groups_mine, groups_saved = {}, {}
    for node, el in pairing.items():
        groups_mine.setdefault(node.stage, set()).add(el.get("LocalInstanceId"))
        groups_saved.setdefault(int(el.get("Stage")), set()).add(el.get("LocalInstanceId"))
    same = sorted(map(sorted, groups_mine.values())) == sorted(map(sorted, groups_saved.values()))
    exact = all(node.stage == int(el.get("Stage")) for node, el in pairing.items())
    print(f"   resource groups (Stage): {'same partition' if same else 'DIFFERENT partition'}"
          f"{', same numbers' if exact else ''}")
    ok &= same

    bad = []
    for node, el in pairing.items():
        for tag, attrs, _ in node.modules:
            if "Sequence" not in attrs:
                continue
            theirs = [m for m in el if m.tag == tag]
            if not theirs or theirs[0].get("Sequence") != attrs["Sequence"]:
                bad.append(f"{short(node.pid)} {tag} {attrs['Sequence']} vs "
                           f"{theirs[0].get('Sequence') if theirs else 'none'}")
    print(f"   sequences: {'all match' if not bad else '; '.join(bad)}")
    ok &= not bad
    return ok


# ---------------------------------------------------------------------------------------------

def report(rocket, path, lib, references):
    liftoff, rows = rocket.performance()
    print(f"\n{rocket.name}  ->  {path}")
    print(f"   parts {sum(1 for _ in rocket.root.walk())}, liftoff {liftoff / 1000:.1f} t, "
          f"sequences 1-{rocket.last_sequence} (bus decoupler last)")
    total = 0.0
    for r in rows:
        twr = r["thrust"] / (r["m0"] * G0)
        total += r["dv"]
        print(f"   {r['what']:<44} {r['m0'] / 1000:7.1f} t  {r['thrust'] / 1e3:7.0f} kN "
              f"{'SL ' if r['sl'] else 'vac'}  TWR {twr:5.2f}  dv {r['dv']:5.0f} m/s")
    print(f"   ideal dv {total:.0f} m/s, before gravity and drag")
    for w in rocket.warnings:
        print(f"   ! {w}")
    problems, notes = validate(path, lib, references[0] if references else None)
    for i, r in enumerate(rows):
        twr = r["thrust"] / (r["m0"] * G0)
        if i == 0 and twr < MIN_LIFTOFF_TWR:
            problems.append(f"cannot lift off: thrust-to-weight {twr:.2f} under {MIN_LIFTOFF_TWR}")
        elif not r["sl"] and twr < MIN_STAGE_TWR:
            problems.append(f"{r['what'].strip()} cannot lift what is above it: thrust-to-weight {twr:.2f} "
                            f"under {MIN_STAGE_TWR}")
        if i == 0 and twr > HOT_LIFTOFF_TWR:
            print(f"   ! hot: thrust-to-weight {twr:.1f} at liftoff hands the shot to the closed loop low and fast")
    for p in problems:
        print(f"   PROBLEM {p}")
    for n in notes:
        print(f"   note {n}")
    return not problems


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", type=Path, help="directory to write <name>/vehicle.xml and meta.toml into")
    ap.add_argument("--preset", action="append", default=[], help="a named rocket; repeatable")
    ap.add_argument("--all-presets", action="store_true")
    ap.add_argument("--name", help="a rocket of your own: its name, which is also its folder")
    ap.add_argument("--stage", action="append", default=[], help="ENGINE[xN]:TANK[xN], bottom first")
    ap.add_argument("--srb", type=int, default=0, help="radial boosters on the first stage")
    ap.add_argument("--list", action="store_true", help="presets and the parts a stage can use")
    ap.add_argument("--check-reference", action="store_true",
                    help="regenerate GeoSat FAT and compare it with the craft the game saved")
    ap.add_argument("--validate", nargs="+", type=Path, metavar="VEHICLE_XML")
    ap.add_argument("--user-dir", help="KSA user directory (default: tools/ksa-user-dir.sh)")
    ap.add_argument("--reference-design", default="GeoSat FAT")
    ap.add_argument("--reference-save", default="ICBM E2E")
    ap.add_argument("--reference-vehicle", default="GeoSat FAT")
    a = ap.parse_args()
    lib = Library()

    if a.list:
        print("presets:")
        for name, spec in PRESETS.items():
            print(f"  {name:<20} {' / '.join(spec['stages'])}" + (f", {spec['srb']} SRBs" if spec.get("srb") else ""))
        print("engines:", ", ".join(f"{k} ({v['vac'] / 1e3:.0f} kN vac)" for k, v in ENGINES.items()))
        print("tanks:", ", ".join(short(p) for p in sorted(lib.parts) if re.match(TANK + r"LF[23]W", p)))
        print("plates:", ", ".join(f"{d}W x{n}" for d, n, _ in PLATES))
        return 0

    ok = True
    if a.check_reference:
        ok &= check_reference(lib, user_dir(a.user_dir), a.reference_design, a.reference_save,
                              a.reference_vehicle)

    references = []
    if a.out or a.validate:
        try:
            udir = user_dir(a.user_dir)
            references = [(ET.parse(udir / "Vehicles" / a.reference_design / "vehicle.xml").getroot(),
                           load_reference_save(udir / "saves" / a.reference_save / "universe.xml",
                                               a.reference_vehicle))]
        except (OSError, ET.ParseError, SystemExit):
            print("note: no reference craft found; the structural diff is skipped")

    if a.validate:
        for path in a.validate:
            problems, notes = validate(path, lib, references[0] if references else None)
            print(f"{path}: {'ok' if not problems else f'{len(problems)} problem(s)'}")
            for p in problems:
                print(f"   PROBLEM {p}")
            for n in notes:
                print(f"   note {n}")
            ok &= not problems

    specs = {}
    for name in (list(PRESETS) if a.all_presets else a.preset):
        if name not in PRESETS:
            sys.exit(f"no preset {name!r}; --list names them")
        specs[name] = PRESETS[name]
    if a.name:
        specs[a.name] = dict(stages=a.stage, srb=a.srb)
    if specs and not a.out:
        sys.exit("--out is required to write a rocket")
    build = game_build()
    for name, spec in specs.items():
        rocket = Rocket(lib, name, spec["stages"], spec.get("srb", 0))
        path = write(rocket, a.out, build)
        ok &= report(rocket, path, lib, references)
    if not (specs or a.validate or a.check_reference):
        ap.print_usage()
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
