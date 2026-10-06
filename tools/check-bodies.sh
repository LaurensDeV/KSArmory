#!/usr/bin/env bash
# The burst code must not know which planet it is on: a custom solar system gains a body by a line in
# KSArmory/Bodies.xml, never by code. Two textual rules, the way check-boundary.sh guards the Sim/Ksa split:
#
#   1. No body is named inside a string literal in the source (comments may cite Earth; code may not
#      branch on it). The shipped data files are the one place names belong.
#   2. An Earth-valued constant -- 340/343 m/s, an 8 km scale height, the 11 km tropopause -- appears only
#      where ALLOWED below says it is a stated reference, with the reason.
#
# The Sim tests over synthetic bodies are the real guard; this keeps the easy mistakes out.
set -euo pipefail
cd "$(dirname "$0")/.."

python3 - <<'PY'
import glob, re, sys

NAMES = ["Mercury", "Venus", "Earth", "Luna", "Moon", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune", "Sol"]
NUMBER = re.compile(r"(?<![\w.])(343|340|11_?000|8_?000)(\.0)?(?![\w.])")
LITERAL = re.compile(r'"(?:[^"\\]|\\.)*"')

# file: the constant's name on that line, and why it is Earth's on purpose.
ALLOWED = {
    ("src/KSArmory/Sim/BlastWave.cs", "SoundMetresPerSecond"): "sea level's sound, the unit the sea-level laws are pinned in",
    ("src/KSArmory/Sim/BurstRegime.cs", "ReferenceScaleHeightMetres"): "the scale height a threshold was calibrated on, scaled by the body's",
    ("src/KSArmory/Sim/XRayGlow.cs", "LayerColumnKgPerM2"): "a column calibrated on KSA's Earth, placed by the body's own air",
    ("src/KSArmory/Sim/Aurora.cs", "FootColumnKgPerM2"): "a column calibrated on KSA's Earth, placed by the body's own air",
    ("src/KSArmory/Sim/MushroomCloud.cs", "TropopauseMetres"): "the cloud top's stratification, Earth's -- a known limit in docs/NUCLEAR-ALTITUDE.md",
    ("src/KSArmory/Ksa/KsaWorld.cs", "TerrainModifierHeadroomMetres"): "terrain headroom, not air",
    ("src/KSArmory/Shaders/KSArmoryCloud.comp", "DustFrontSpeed"): "sea level's sound for the dust ring's timing -- a known limit: a cloud dispatch has no float free to carry the body's",
    ("src/KSArmory/Shaders/KSArmoryCloud.comp", "along / 8000.0"): "an aurora's noise wavelength, not an altitude",
}

faults = []
files = sorted(glob.glob("src/KSArmory/Sim/*.cs") + glob.glob("src/KSArmory/Ksa/**/*.cs", recursive=True)
               + glob.glob("src/KSArmory/Shaders/*.comp"))
for path in files:
    for n, line in enumerate(open(path, encoding="utf-8"), 1):
        code = line.split("//")[0]
        if not code.strip():
            continue
        for lit in LITERAL.finditer(code):
            for name in NAMES:
                if re.search(r"\b" + name + r"\b", lit.group(0)):
                    faults.append(f"{path}:{n}: names {name} in a string literal: {lit.group(0)[:60]}")
        if NUMBER.search(code) and not any(p == path and c in code for (p, c) in ALLOWED):
            faults.append(f"{path}:{n}: an Earth-valued constant outside a stated reference: {code.strip()[:80]}")

if faults:
    print("\n".join(faults))
    print(f"\n{len(faults)} place(s) the burst code knows which planet it is on; add it to Bodies.xml, "
          "or to ALLOWED in tools/check-bodies.sh with the reason")
    sys.exit(1)
print(f"bodies ok: {len(files)} file(s) name no body in code, and {len(ALLOWED)} Earth-valued constant(s) are stated references")
PY
