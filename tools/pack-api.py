#!/usr/bin/env python3
"""Records the API a KSArmory weapon pack binds to.

    ./tools/pack-api.py            # regenerate docs/PACK-API-SURFACE.md
    ./tools/pack-api.py --check    # fail if the committed file is stale (for CI)

The mirror of tools/api-surface.sh, pointed the other way. That one records what this mod
depends on, because KSA moves underneath it. This records what depends on *this mod*, because
nothing else can: a pack lives in somebody else's repository, never builds in CI, and its
breakage surfaces as a bug report against a mod that used to work.

Two halves, and the C# is the small one. The half that actually breaks packs is the XML
vocabulary -- rename ChargeKg in MunitionProfile and the mod compiles, every test passes, every
check passes, and every pack setting it has its munitions refused by name. It is not a type, so
no compiler can see it; it is whichever string literals PackReader happens to consume.

That is also why this is read out of the reader's source rather than out of metadata: the
literals are the contract, and they exist nowhere else. It fails closed -- if the shapes below
stop matching, the record shrinks and --check fires.

Committing the regenerated file is the acknowledgement. semantic-release will not help here: a
rename inside Sim/ is a refactor by every rule in CLAUDE.md and cuts no release at all, so the
version number carries no signal that the pack contract moved. Armoury.Schema is the loud
escape hatch for a deliberate break, and this is what reminds you to turn it.
"""
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SIM = REPO / "src" / "KSArmory" / "Sim"
OUT = REPO / "docs" / "PACK-API-SURFACE.md"

# The reader's own vocabulary, in the order a pack meets it.
ELEMENTS = [
    ("Munition", "ReadMunition", "how a round flies, and what it does on arrival"),
    ("Sensor", "ReadSensor", "what a launcher can see"),
    ("Launcher", "ReadLauncher", "the part, and what it does with the round"),
    ("Optic", "ReadOptic", "a sighting head, needing no weapon on the craft"),
]

# Children, which are read by their own helper rather than by a ReadX.
CHILDREN = [
    ("Tube", "public Tube[] Tubes()", "return [", "Launcher"),
    ("Muzzle", "public double3[] Muzzles()", "return [", "Launcher"),
    ("Stage", "public BoostStage[] Stages()", "return [", "Munition"),
    ("Provides", "public List<BuiltInComponent> Provides()", "return rows;", "Launcher"),
]

# What each reader call means to somebody writing a file.
KINDS = {
    "Required": "text, required",
    "Text": "text",
    "Number": "number",
    "Count": "whole number",
    "Flag": "true or false",
    "Angle": "degrees",
    "Vector": "three numbers",
    "Choice": "one of",
    "Reference": "name, required",
    "OptionalReference": "name",
}

CALL = re.compile(
    r'\b[a-z]\.(Required|Text|Number|Count|Flag|Angle|Vector|Choice|Reference|OptionalReference)'
    r'\("([A-Za-z0-9]+)"(?:,\s*([^,)]+))?')

FALLBACK = re.compile(
    r'\b[a-z]\.(?:Text|Number|Count|Flag|Angle|Vector)\("([A-Za-z0-9]+)"\)\s*\?\?\s*("[^"]*"|[\w.]+)')


def fail(message):
    print(f"error: {message}", file=sys.stderr)
    sys.exit(1)


def body(source, opener, closer):
    """The text of one method, from its signature to the marker that ends its element."""
    start = source.find(opener)
    if start < 0:
        fail(f"{opener} is gone from PackReader.cs -- this script needs teaching")

    end = source.find(closer, start)
    if end < 0:
        fail(f"{opener} no longer ends with {closer!r} -- this script needs teaching")

    return source[start:end]


def attributes(text):
    """(name, kind, default) per attribute the given method consumes, first mention wins.

    A child helper reads its attribute with a fallback and *then* faults when it was absent, so
    the fallback is not a default -- it is what the value is while the element is being refused.
    """
    demanded = set(re.findall(r'Attribute\("(\w+)"\) is null', text))
    fallbacks = {name: value.strip('"') for name, value in FALLBACK.findall(text)}
    seen, rows = set(), []

    for kind, name, default in CALL.findall(text):
        if name in seen:
            continue

        seen.add(name)
        required = kind in ("Required", "Reference") or name in demanded
        rows.append((name, kind, (default or "").strip() or fallbacks.get(name, ""), required))

    return rows


def enum_values(name):
    """The values a Choice attribute accepts, which are as much the contract as the name is."""
    for path in SIM.glob("*.cs"):
        text = path.read_text()
        match = re.search(rf"enum {name}\s*\{{(.*?)\}}", text, re.S)
        if not match:
            continue

        return [v for v in re.findall(r"^\s*([A-Z]\w*)\s*(?:=[^,]*)?,?\s*$",
                                      re.sub(r"//.*|/\*.*?\*/|///.*", "", match.group(1)),
                                      re.M)]

    fail(f"enum {name} not found under Sim/")


def reads(kind, default):
    """What the attribute takes. An enum lists its values, because removing one breaks packs."""
    if kind != "Choice":
        return KINDS[kind]

    return "one of " + ", ".join(f"`{v}`" for v in enum_values(default.split(".")[0]))


def readable(kind, default, required):
    """A default as a pack author reads it, not as C# spells it."""
    if required:
        return "**required**"

    if kind == "Choice":
        return f"`{default.split('.')[-1]}`"

    # Sentinels first: these name a C# fallback meaning "whatever this pack registered", which is
    # not a value an author can type and so is no default to them.
    if default in ("", "own", "ownRounds", "ownSets", "known"):
        return "*none*"

    if kind in ("Text", "OptionalReference"):
        return f"`{default}`"

    if default == "default":
        return "`0, 0, 0`"

    if default == "float.NaN":
        return "*the modelled pose*"

    if default.startswith("(float"):
        return "*the flight model's own*"

    return f"`{default.rstrip('f')}`"


def csharp(path, kinds):
    """Public members of one entry-point type, positional record parameters included."""
    text = (SIM / path).read_text()
    lines = []

    for declaration in re.finditer(rf"public (?:sealed |readonly |static )*(?:{kinds}) (\w+)"
                                   r"(?:\(([^)]*)\))?", text):
        lines.append(f"### KSArmory.{declaration.group(1)}")
        if declaration.group(2):
            for parameter in declaration.group(2).split(","):
                lines.append(f"- `{parameter.strip()}`")

    for member in re.finditer(r"^\s*public static (?!readonly|class|sealed|record|struct)"
                              r"([\w<>?\[\], ]+?) (\w+)(\([^)]*\))?\s*(?:=>|\{)", text, re.M):
        signature = f"{member.group(1)} {member.group(2)}{member.group(3) or ' { get; }'}"
        lines.append(f"- `static {signature.strip()}`")

    for member in re.finditer(r"^\s*public (?!static|readonly|sealed)([\w<>?\[\], ]+?) (\w+)"
                              r"(\([^)]*\))?\s*(?:=>|\{)", text, re.M):
        signature = f"{member.group(1)} {member.group(2)}{member.group(3) or ' { get; }'}"
        lines.append(f"- `{signature.strip()}`")

    if not lines:
        fail(f"no public members found in {path} -- this script needs teaching")

    return lines


def render(seen=None):
    reader = (SIM / "PackReader.cs").read_text()

    schema = re.search(r"public const int Schema = (\d+);", reader)
    if not schema:
        fail("PackReader.Schema is gone -- this script needs teaching")

    if seen is not None:
        seen.append(schema.group(1))

    out = [
        "# Pack API surface",
        "",
        "Everything a KSArmory weapon pack binds to, read out of `Sim/PackReader.cs` and the entry",
        "point by `tools/pack-api.py`. **Generated - do not edit.**",
        "",
        "This is the checklist for changing KSArmory without breaking somebody else's mod: anything",
        "here that changes shape is a breaking change for every pack, and anything not here cannot be.",
        "A diff against this file is the only warning there is -- a pack lives in another repository,",
        "never builds in CI, and an attribute this build stops knowing is refused by name rather than",
        "ignored.",
        "",
        "`docs/WEAPON-PACKS.md` is the same surface written for the author, with the reasons attached.",
        "",
    ]

    element_rows = {}
    for element, method, _ in ELEMENTS:
        element_rows[element] = attributes(body(reader, f"private static void {method}", "if (!r.Sound())"))

    for element, signature, closer, parent in CHILDREN:
        element_rows[element] = attributes(body(reader, signature, closer))

    total = sum(len(rows) for rows in element_rows.values())
    entry = []
    for path, kinds in [("Armoury.cs", "class"), ("PackResult.cs", "record struct"), ("PackFault.cs", "record struct")]:
        entry += csharp(path, kinds)

    out += [
        f"**Definition schema {schema.group(1)}.** "
        f"{len(element_rows)} elements, {total} attributes, {len(entry)} entry-point lines.",
        "",
        "## Entry point",
        "",
        "What a pack calls. It takes text rather than profiles so that a pack needs no KSA assemblies",
        "to build; widening it to take a profile type would put that back.",
        "",
    ]
    out += entry

    out += ["", "## Definition format", ""]

    for element, _, what in ELEMENTS:
        out += [f"### `<{element}>` - {what}", "",
                "| Attribute | Reads | Default |", "| --- | --- | --- |"]
        for name, kind, default, required in element_rows[element]:
            out.append(f"| `{name}` | {reads(kind, default)} | {readable(kind, default, required)} |")
        out.append("")

    for element, _, _, parent in CHILDREN:
        out += [f"### `<{element}>` - child of `<{parent}>`", "",
                "| Attribute | Reads | Default |", "| --- | --- | --- |"]
        for name, kind, default, required in element_rows[element]:
            out.append(f"| `{name}` | {reads(kind, default)} | {readable(kind, default, required)} |")
        out.append("")

    return "\n".join(out).rstrip() + "\n"


def main():
    check = "--check" in sys.argv
    schema = []
    rendered = render(schema)

    if not check:
        OUT.write_text(rendered)
        print(f"wrote {OUT.relative_to(REPO)}")
        return 0

    if not OUT.is_file():
        fail(f"{OUT.relative_to(REPO)} does not exist -- run ./tools/pack-api.py")

    if OUT.read_text() != rendered:
        print(f"{OUT.relative_to(REPO)} is stale.", file=sys.stderr)
        print("The API packs bind to has moved. Regenerate with ./tools/pack-api.py, and if the",
              file=sys.stderr)
        print("change is one a pack would notice, bump PackReader.Schema so old packs are refused",
              file=sys.stderr)
        print("loudly rather than having their definitions rejected one attribute at a time.",
              file=sys.stderr)
        return 1

    print(f"pack API surface matches the code (schema {schema[0]})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
