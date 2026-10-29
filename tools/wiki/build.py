#!/usr/bin/env python3
"""Build ksarmory.com from what the repository already maintains.

    ./tools/wiki/build.py              # the site, into wiki/build/site
    ./tools/wiki/build.py --serve      # ...and serve it at http://127.0.0.1:8000 while editing
    ./tools/wiki/build.py --pages-only # the Markdown alone, into wiki/build/check

Nothing on the site is written for the site except wiki/pages/. The weapon pages are generated from
the family files and the part XML, with each weapon's description taken from README.md's "What ships
with it"; the pack-author pages are docs/WEAPON-PACKS.md and docs/FROM-KSP-MODDING.md; the guide
pages are README.md's own sections. A link into the repository is pointed at GitHub, at the branch
the site describes.
"""

import argparse
import html
import json
import math
import os
import re
import shutil
import subprocess
import sys
import urllib.request
import xml.etree.ElementTree as ET
from datetime import datetime
from pathlib import Path
from urllib.parse import urljoin

sys.path.insert(0, str(Path(__file__).resolve().parent))
from models import slug  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
MOD = ROOT / "src/KSArmory"
WIKI = ROOT / "wiki"
BUILD = WIKI / "build"
DOCS = BUILD / "docs"
REPO = "https://github.com/LaurensDeV/KSArmory"
NODE = Path(__file__).resolve().parent / "node_modules"

# Hosts a page may never load from: the site serves its own viewer and decoder. model-viewer's defaults point
# its decoders at Google, and the setting that stops that is not in its documentation.
FORBIDDEN_HOSTS = ("gstatic.com", "googleapis.com", "jsdelivr.net", "unpkg.com")
BRANCH = "main"

# The order families appear in, and what each is called on the site.
FAMILIES = {
    "Pantsir": "Air defence",
    "Rails": "Missiles",
    "Rockets": "Rocket pods",
    "Guns": "Guns",
    "Sensors": "Sights and sensors",
    "Countermeasures": "Countermeasures",
    "DirectedEnergy": "Directed energy",
    "Strategic": "Nuclear weapons",
}

# A family's folder, where its file's name no longer says what it holds.
FOLDERS = {"Rails": "missiles", "Strategic": "nuclear"}


# What the stores hang from, a section of their own: (model, name, the CarriageMarker of the stores that
# bring one, the line such a store's page says it with). They fire nothing, so no family file defines them;
# the Launch Rail is not even a part, and tools/wiki/models.py draws it from a missile.
MOUNTS_TITLE = "Racks and rails"
MOUNTS_FOLDER = "weapons/racks-and-rails"
MOUNTS = [
    ("KSArmory_Prefab_WeaponsRail", "Ejector Rack", "Carriage",
     "Hangs from an [Ejector Rack]({link}), which appears with it once it is attached."),
    ("KSArmory_Prefab_LaunchRail", "Launch Rail", "LaunchRail", "Slides off a [Launch Rail]({link}), which appears with it once it is attached."),
]


def family_label(family: str) -> str:
    """A family as the in-game switch names it (Ksa/Ui/Ui.cs FamilyLabel): DirectedEnergy reads Directed energy."""
    return re.sub(r"(?<=[a-z])([A-Z])", lambda m: " " + m.group(1).lower(), family)


def folder_of(family: str) -> str:
    return f"weapons/{FOLDERS.get(family, family.lower())}"

# Pages lifted whole from the repository: (source, page, title).
DOC_PAGES = [
    ("docs/WEAPON-PACKS.md", "modding/weapon-packs.md", "Weapon pack reference"),
    ("docs/FROM-KSP-MODDING.md", "modding/from-ksp.md", "Coming from KSP modding"),
]

# README.md sections that are the player's own pages: (heading, page, title).
README_PAGES = [
    ("Install", "guide/install.md", "Installing"),
    ("Use", "guide/using.md", "Using it"),
]



def csharp_defaults(*files: str) -> dict[str, str]:
    """A profile field's default as written in its source, so an attribute left out shows what the game uses."""
    found: dict[str, str] = {}
    pattern = re.compile(r"public\s+[\w?<>\[\]]+\s+(\w+)\s*(?:\{[^}]*\})?\s*=\s*([^;]+);")
    for name in files:
        for field, raw in pattern.findall((MOD / "Sim" / name).read_text()):
            raw = raw.strip()
            if re.fullmatch(r"-?[\d.]+(?:e-?\d+)?[fdm]?", raw):
                raw = raw.rstrip("fdm")
            elif re.fullmatch(r"\w+\.\w+", raw):
                raw = raw.split(".")[-1]  # an enum member: GuidanceMode.Seeker reads as Seeker
            found.setdefault(field, raw)
    return found


MUNITION = csharp_defaults("MunitionProfile.cs")
SENSOR = csharp_defaults("SensorProfile.cs")
OPTIC = csharp_defaults("OpticProfile.cs")
LAUNCHER = csharp_defaults("LauncherProfile.cs")
DISPENSER = csharp_defaults("Dispenser.cs")
BEAM = csharp_defaults("BeamProfile.cs")

# Rounds whose MaxRange is a geometric bound rather than a reach: how far one goes is set by what carries it.
CARRIED_RANGE = {"MK21"}


def value(element: ET.Element, name: str, defaults: dict[str, str]) -> str | None:
    return element.get(name) if element.get(name) is not None else defaults.get(name)


def number(text: str | None) -> float | None:
    try:
        return float(text) if text is not None else None
    except ValueError:
        return None


def distance(metres: float | None) -> str:
    if metres is None or not math.isfinite(metres):
        return "-"
    if metres >= 100_000:
        return f"{metres / 1000:,.0f} km"
    return f"{metres / 1000:.3g} km" if metres >= 1000 else f"{metres:.3g} m"


# GuidanceMode as a reader would say it; a mode not here is shown as the code names it.
GUIDANCE = {"Seeker": "seeker", "AntiRadiation": "anti-radiation", "CommandLink": "command link",
            "Inertial": "inertial", "None": "unguided"}


def charge(kg: float | None) -> str:
    if not kg:
        return "none"
    # A nuclear yield reads in kilotons from a tenth of one, as it is quoted.
    for scale, unit in ((1e9, "Mt"), (1e5, "kt"), (1e3, "t")):
        if kg >= scale:
            return f"{kg / (1e6 if unit == 'kt' else scale):.3g} {unit}"
    return f"{kg:.3g} kg" if kg >= 1 else f"{kg * 1000:.3g} g"


def hidden_parts() -> set[str]:
    """Parts the editor does not offer, which a player cannot build with and so gets no page."""
    root = ET.parse(MOD / "KSArmoryGameData.xml").getroot()
    return {p.get("Id") for p in root.iter("PartGameData")
            if any(t.get("Value") == "Hidden" for t in p.iter("EditorTag"))}


def part_names() -> dict[str, str]:
    root = ET.parse(MOD / "KSArmoryGameData.xml").getroot()
    return {p.get("Id"): html.unescape(p.get("DisplayName", p.get("Id"))) for p in root.iter("PartGameData")}



STOP = {"and", "the", "a", "of", "on", "with"}


def words(text: str) -> set[str]:
    found = set()
    for word in re.findall(r"[\w/\"-]+", text.lower()):
        word = word.strip('"')
        if word in STOP or not word:
            continue
        found.add(word[:-1] if word.endswith("s") and len(word) > 3 else word)
    return found


def readme_blurbs() -> list[tuple[str, str]]:
    """(name, paragraph) for every weapon README.md's "What ships with it" describes."""
    text = (ROOT / "README.md").read_text()
    section = text.split("## What ships with it", 1)[1].split("\n## ", 1)[0]
    blurbs = []
    for paragraph in section.split("\n\n"):
        if m := re.match(r"\*\*([^*]+?)\.\*\*\s+(.*)", paragraph.strip(), re.S):
            blurbs.append((m.group(1), " ".join(m.group(2).split())))
    return blurbs


def blurb_for(names: list[str], blurbs: list[tuple[str, str]]) -> str | None:
    best, score = None, 0.0
    for heading, body in blurbs:
        for name in names:
            a, b = words(heading), words(name)
            s = len(a & b) / len(a | b) if a | b else 0.0
            if s > score:
                best, score = body, s
    return best if score >= 0.5 else None



def relink(text: str, source: Path, page: str) -> str:
    """Point every link that leaves the site at GitHub; one to a page or a README section the site carries
    stays on the site."""
    pages = site_links(page)

    def fix(m: re.Match) -> str:
        label, target = m.group(1), m.group(2)
        if target.startswith("#") and source == ROOT / "README.md":
            # A README heading is on whichever guide page took its section, or only on GitHub.
            home = readme_anchors().get(target[1:])
            if home is None:
                return f"[{label}]({REPO}/blob/{BRANCH}/README.md{target})"
            return f"[{label}]({'' if home == page else os.path.relpath(home, Path(page).parent)}{target})"
        if re.match(r"^(https?:|mailto:|#)", target):
            return m.group(0)
        path, _, anchor = target.partition("#")
        resolved = os.path.relpath((source.parent / path).resolve(), ROOT)
        if resolved in pages:
            return f"[{label}]({pages[resolved]}{'#' + anchor if anchor else ''})"
        # Resolved as GitHub resolves it from the file's own page, which also covers ../../releases.
        return f"[{label}]({urljoin(f'{REPO}/blob/{BRANCH}/{source.relative_to(ROOT).as_posix()}', target)})"

    return re.sub(r"(?<!!)\[([^\]]+)\]\(([^)\s]+)\)", fix, text)


def readme_anchors() -> dict[str, str]:
    """Each heading inside a README section the guide carries, to the guide page it is on."""
    found = {}
    for heading, page, _ in README_PAGES:
        for title in re.findall(r"^#{2,4} (.+)$", readme_section(heading), re.M):
            found.setdefault(anchor(title), page)
    return found


def readme_section(heading: str) -> str:
    text = (ROOT / "README.md").read_text()
    m = re.search(rf"^## {re.escape(heading)}\n(.*?)(?=^## |\Z)", text, re.S | re.M)
    if not m:
        sys.exit(f"wiki: README.md has no '## {heading}' section")
    # One level up: a README "###" is a page's "##".
    return re.sub(r"^#(#+) ", r"\1 ", m.group(1), flags=re.M)



def family_files() -> list[tuple[str, ET.Element, str]]:
    found = []
    for path in sorted((MOD / "KSArmory").glob("*.xml")):
        root = ET.parse(path).getroot()
        if root.tag == "WeaponPack" and root.get("Family"):
            found.append((root.get("Family"), root, path.name))
    unknown = [f for f, _, _ in found if f not in FAMILIES]
    if unknown:
        sys.exit(f"wiki: no title for families {unknown}; add them to FAMILIES in tools/wiki/build.py")
    return sorted(found, key=lambda f: list(FAMILIES).index(f[0]))


def round_row(m: ET.Element, shell: bool) -> str:
    # A gun's round is flown as a shell whatever its profile's guidance says, which is only the default.
    guidance = "shell" if shell else GUIDANCE.get(value(m, "Guidance", MUNITION) or "", value(m, "Guidance", MUNITION))
    low, high = number(value(m, "MinRange", MUNITION)), distance(number(value(m, "MaxRange", MUNITION)))
    reach = ("set by its carrier" if m.get("Name") in CARRIED_RANGE
             else f"{distance(low)} to {high}" if low else f"up to {high}")
    fuse = number(value(m, "FuseRadius", MUNITION))
    fuse_text = "contact" if not fuse else "proximity, " + distance(fuse)
    if m.get("TimedFuse") == "true":
        fuse_text = f"timed, with a {distance(fuse)} proximity fuse" if fuse else "timed"
    return f"| {m.get('DisplayName')} | {guidance} | {reach} | {warhead(m)} | {fuse_text} |"


def warhead(m: ET.Element) -> str:
    """The charge, and how far the yield dial goes where a round has one."""
    shipped = charge(number(value(m, "ChargeKg", MUNITION)))
    top = number(m.get("MaxChargeKg"))
    return f"{shipped}, adjustable to {charge(top)}" if top else shipped


def sensor_row(s: ET.Element) -> str:
    kind = ("tracks what it is given" if s.get("CuedOnly") == "true" else
            "infrared" if s.get("Infrared") == "true" else "radar" if s.get("Emits") == "true" else "passive")
    reach = distance(number(value(s, "Range", SENSOR)))
    # An infrared set's range is a ceiling; what it sees depends on how hot the contact is.
    if s.get("Infrared") == "true":
        reach = f"up to {reach}"
    # ConeDeg is a half-angle; what the set or head may look into is twice it.
    return f"| {s.get('DisplayName')} | {kind} | {reach} | {2 * float(value(s, 'ConeDeg', SENSOR)):g}° |"


def front_matter(title: str, description: str, image: str) -> list[str]:
    """What a link preview reads: the title, a sentence or two, and the picture (wiki/overrides/main.html)."""
    return ["---", f"title: {json.dumps(title)}", f"description: {json.dumps(description)}",
            f"image: {json.dumps(image)}", "---", ""]


def plain(markdown: str, limit: int = 220) -> str:
    """Markdown as one line of plain text, cut at a sentence where it can be."""
    text = re.sub(r"\[([^\]]+)\]\([^)]*\)", r"\1", markdown)
    text = re.sub(r"[*_`]", "", text)
    if len(text) <= limit:
        return text
    cut = text.rfind(". ", 0, limit)
    return text[:cut + 1] if cut > 60 else text[:limit].rsplit(" ", 1)[0] + "..."


VIEWER_SCRIPTS = ['<script src="/assets/3d/config.js"></script>',
                  '<script type="module" src="/assets/3d/model-viewer.min.js"></script>']


def weapon_pages(names: dict[str, str]) -> tuple[dict[str, str], list[dict], list[str]]:
    """A page per weapon and an overview per family, one entry per part for the gallery and the table, and
    every part no README paragraph describes."""
    blurbs = readme_blurbs()
    pages, weapons, undescribed = {}, [], []
    carriage_page = {marker: (f"{MOUNTS_FOLDER}/{anchor(name)}.md", line)
                     for _, name, marker, line in MOUNTS if marker}
    carried_by: dict[str, list[str]] = {}
    for _, root, _ in family_files():
        for e in root:
            if (marker := e.get("CarriageMarker")) in carriage_page and e.get("PartId") not in hidden_parts():
                carried_by.setdefault(marker, []).append(names.get(e.get("PartId"), e.get("PartId")))
    # Across every family: the Pantsir's roof director names a sensor Sensors.xml defines.
    all_sensors = {s.get("Name"): s for _, r, _ in family_files() for s in r.iter("Sensor")}
    for family, root, file_name in family_files():
        munitions = {m.get("Name"): m for m in root.iter("Munition")}
        sensors = all_sensors
        decoys = {d.get("Name"): d for d in root.iter("Decoy")}
        folder = folder_of(family)
        family_weapons = []

        parts: dict[str, list[ET.Element]] = {}
        for element in root:
            if element.get("PartId"):
                parts.setdefault(element.get("PartId"), []).append(element)

        for part_id, elements in parts.items():
            if part_id in hidden_parts():
                continue
            name = names.get(part_id, part_id)
            page = f"{folder}/{anchor(name)}.md"
            own_names = [name] + [e.get("DisplayName", "") for e in elements]
            blurb = blurb_for(own_names, blurbs)
            if blurb is None:
                undescribed.append(name)
            blurb = blurb[0].upper() + blurb[1:] if blurb else ""
            out = [*front_matter(name, plain(blurb) or f"{name}, part of KSArmory's {FAMILIES[family].lower()}.",
                                 f"models/{slug(part_id)}-card.png"),
                   *VIEWER_SCRIPTS, "", f"# {name}", ""]
            if blurb:
                out += [relink(blurb, ROOT / "README.md", page), ""]
            out += [viewer(part_id, name), "",
                    f"Part Id `{part_id}`, in [{FAMILIES[family]}](index.md).", ""]
            for e in elements:
                if (hung := carriage_page.get(e.get("CarriageMarker", ""))) is not None:
                    link = Path(os.path.relpath(hung[0], Path(page).parent)).as_posix()
                    out += [hung[1].format(link=link), ""]

            fires, sees, kinds, shells = [], [], [], set()
            for e in elements:
                if e.tag == "Launcher":
                    if e.get("FiresBeam") == "true":
                        kinds.append("laser")
                    if e.findall("Tube"):
                        plural = value(e, "TubeArmamentLabel", LAUNCHER).strip('"').lower()
                        kinds.append(plural if len(e.findall("Tube")) > 1 else plural.rstrip("s"))
                    if e.get("GunMunition"):
                        kinds.append("gun")
                        shells.add((e.get("GunMunition") or "").split(":")[-1])
                    for key in ("Munition", "GunMunition"):
                        if (m := munitions.get((e.get(key) or "").split(":")[-1])) is not None and m not in fires:
                            fires.append(m)
                    if (s := sensors.get((e.get("Sensor") or "").split(":")[-1])) is not None and s not in sees:
                        sees.append(s)
                    tubes = len(e.findall("Tube"))
                    gun = e.get("GunMunition")
                    ammo = int(value(e, "GunAmmo", LAUNCHER)) if gun else 0
                    rate = int(float(value(e, "GunRoundsPerMinute", LAUNCHER))) if gun else 0
                    label = value(e, "TubeArmamentLabel", LAUNCHER).strip('"').lower()
                    facts = [f"{tubes} {label}" if tubes > 1 else f"1 {label.rstrip('s')}" if tubes else None,
                             f"{ammo:,} rounds of ammunition" if ammo else None,
                             f"fires {rate:,} rounds a minute" if rate else None,
                             f"turns at {value(e, 'SlewRateDeg', LAUNCHER)}°/s" if e.get("TurretMarker") else None]
                    if facts := [f for f in facts if f]:
                        out += [f"**{e.get('DisplayName')}**: {', '.join(facts)}.", ""]
                elif e.tag in ("Optic", "SearchSet"):
                    kinds.append("sight" if e.tag == "Optic" else "sensor")
                    if (s := sensors.get((e.get("Sensor") or "").split(":")[-1])) is not None and s not in sees:
                        sees.append(s)
                    if e.tag == "Optic":
                        zoom = value(e, "MaxMagnification", OPTIC)
                        gimbal = value(e, "Gimbal", OPTIC)
                        out += [f"**{e.get('DisplayName')}**: zooms to ×{zoom}, "
                                f"{'roll-nod head' if gimbal == 'RollNod' else 'ball head on a mast'}"
                                f"{', laser rangefinder and designator' if e.get('HasLaser') == 'true' else ''}.", ""]
                        if (b := e.find("Beam")) is not None:
                            power = float(value(b, "PowerWatts", BEAM)) / 1e6
                            out += [f"**Laser**: {power:g} MW, {float(value(b, 'ChemicalSeconds', BEAM)):g} s of "
                                    f"lasing per load of chemicals, in {float(value(b, 'ShotSeconds', BEAM)):g} s shots.", ""]
                elif e.tag == "Dispenser":
                    kinds.append("decoys")
                    d = decoys.get(e.get("Decoy", ""))
                    out += [f"**{e.get('DisplayName')}**: {value(e, 'Count', DISPENSER)} × "
                            f"{d.get('DisplayName') if d is not None else e.get('Decoy')}, "
                            f"{value(e, 'SalvoSize', DISPENSER)} thrown per press.", ""]

            if fires:
                out += ["| Round | Guidance | Range | Warhead | Fuse |", "| --- | --- | --- | --- | --- |",
                        *[round_row(m, m.get("Name") in shells) for m in fires], ""]
            if sees:
                out += ["| Sensor | Type | Range | Coverage |", "| --- | --- | --- | --- |", *[sensor_row(s) for s in sees], ""]

            pages[page] = "\n".join(out)
            first = fires[0] if fires else None
            family_weapons.append({
                "name": name, "part": part_id, "family": FAMILIES[family], "page": page, "id": anchor(name),
                "href": f"/{folder}/{anchor(name)}/",
                "kinds": ", ".join(dict.fromkeys(kinds)),
                "reach": (None if first is None else "set by its carrier" if first.get("Name") in CARRIED_RANGE
                          else distance(number(value(first, "MaxRange", MUNITION)))),
                "warhead": warhead(first) if first is not None else None,
            })

        weapons += family_weapons
        pages[f"{folder}/index.md"] = "\n".join([
            *front_matter(FAMILIES[family], ", ".join(w["name"] for w in family_weapons) + ".",
                          f"models/{slug(family_weapons[0]['part'])}-card.png"),
            f"# {FAMILIES[family]}", "",
            f"Defined in [`{file_name}`]({REPO}/blob/{BRANCH}/src/KSArmory/KSArmory/{file_name}). The whole family "
            f"can be switched off under *KSArmory → Built-in weapons → {family_label(family)}*; the change takes effect "
            "when KSA next starts.", "",
            gallery(family_weapons), ""])

    mounts = []
    for model, name, marker, _ in MOUNTS:
        page = f"{MOUNTS_FOLDER}/{anchor(name)}.md"
        blurb = blurb_for([name], blurbs)
        if blurb is None:
            undescribed.append(name)
        stores = carried_by.get(marker, []) if marker else []
        pages[page] = "\n".join([
            *front_matter(name, plain(blurb or name), f"models/{slug(model)}-card.png"),
            *VIEWER_SCRIPTS, "", f"# {name}", "", relink(blurb or "", ROOT / "README.md", page), "",
            viewer(model, name), "",
            *([f"The stores that come with one: {', '.join(stores)}.", ""] if stores else [])])
        mounts.append({"name": name, "part": model, "family": MOUNTS_TITLE, "page": page, "id": anchor(name),
                       "href": f"/{MOUNTS_FOLDER}/{anchor(name)}/", "kinds": "mount", "reach": None, "warhead": None})
    weapons += mounts
    pages[f"{MOUNTS_FOLDER}/index.md"] = "\n".join([
        *front_matter(MOUNTS_TITLE, ", ".join(m["name"] for m in mounts) + ".",
                      f"models/{slug(mounts[0]['part'])}-card.png"),
        f"# {MOUNTS_TITLE}", "",
        "What the missiles, the bomb and the rocket pods hang from. They fire nothing themselves.", "",
        gallery(mounts), ""])
    return pages, weapons, undescribed


def viewer(part_id: str, name: str) -> str:
    """A part's 3D model, loaded as it scrolls into view. Root-absolute paths: the site is always at a root."""
    label = html.escape(name, quote=True)
    return (f'<figure class="part-figure">\n'
            f'<model-viewer class="part-preview" src="/models/{slug(part_id)}.glb" alt="{label}, a 3D model"\n'
            f'  camera-controls auto-rotate rotation-per-second="15deg" camera-orbit="35deg 70deg auto"\n'
            f'  environment-image="neutral" shadow-intensity="0.6" interaction-prompt="none"\n'
            f'  loading="lazy" reveal="auto" touch-action="pan-y">\n'
            f'  <img slot="poster" src="/models/{slug(part_id)}.webp" alt="{label}">\n'
            f'</model-viewer>\n'
            f'<figcaption>Drag to rotate. Scroll or pinch to zoom.</figcaption>\n'
            f'</figure>')


def gallery(weapons: list[dict]) -> str:
    """Every weapon at once: its still, family, name and what it reaches and carries."""
    cards = []
    for w in weapons:
        # A card has room for the plain figures; the page says how a range is set or how far a yield goes.
        reach = None if w["reach"] == "set by its carrier" else w["reach"]
        shipped = (w["warhead"] or "").split(",")[0]
        stats = " · ".join(x for x in (reach, shipped) if x and x != "none") or w["kinds"]
        cards.append(f'<a class="ks-card" id="{w["id"]}" href="{w["href"]}">'
                     f'<img src="/models/{slug(w["part"])}.webp" alt="{html.escape(w["name"], quote=True)}" loading="lazy">'
                     f'<span class="ks-card__body"><span class="ks-card__family">{html.escape(w["family"])}</span>'
                     f'<span class="ks-card__name">{html.escape(w["name"])}</span>'
                     f'<span class="ks-card__stats">{html.escape(stats)}</span></span></a>')
    return '<div class="ks-grid">\n' + "\n".join(cards) + "\n</div>"


def weapon_table(weapons: list[dict]) -> list[str]:
    return ["| Weapon | Family | Type | Range | Warhead |", "| --- | --- | --- | --- | --- |",
            *[f"| [{w['name']}]({w['href']}) | {w['family']} | {w['kinds']} | {w['reach'] or '-'} | "
              f"{w['warhead'] or '-'} |" for w in weapons]]


def anchor(title: str) -> str:
    """The id MkDocs gives a heading: lower case, punctuation dropped, spaces to hyphens."""
    return re.sub(r"[\s]+", "-", re.sub(r"[^\w\s-]", "", title.lower())).strip("-")



VIEWER_CONFIG = """// Before the viewer's module: it reads this once, as it loads, to find the decoder on this site.
self.ModelViewerElement = {
  meshoptDecoderLocation: new URL('meshopt_decoder.js', document.currentScript.src).href,
};
"""

GALLERY_MARKER = "<!-- every weapon -->"
PATCH_MARKER = "<!-- patch -->"
LISTING_MARKER = "<!-- listing -->"

# What Borea and ksamods.gg read: the downloads are GitHub's and SpaceDock's added together.
CONTENT_INDEX = "https://ksamodding.github.io/content-index-releases/v1/index.json"
LISTING_ID = "KSArmory"

DOWNLOAD_ICON = ('<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 4v11m0 0-4.5-4.5M12 15l4.5-4.5'
                 'M5 19.5h14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" '
                 'stroke-linejoin="round"/></svg>')
DATE_ICON = ('<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="8.5" fill="none" '
             'stroke="currentColor" stroke-width="2"/><path d="M12 7.5V12l3 2" fill="none" stroke="currentColor" '
             'stroke-width="2" stroke-linecap="round"/></svg>')


def listing_card() -> str:
    """KSArmory as the content index lists it, laid out as ksamods.gg lays out a mod, or nothing when the
    index cannot be read: a site built offline is the same site without the card."""
    try:
        with urllib.request.urlopen(CONTENT_INDEX, timeout=30) as response:
            index = json.load(response)
        listing = next(entry for entry in index["listings"] if entry["id"] == LISTING_ID)
        release = max(listing["releases"], key=lambda r: r["release_date"])
        authored = listing["authored"]
        downloads = int(listing["downloads"]["total"])
        released = datetime.fromisoformat(release["release_date"].replace("Z", "+00:00"))
    except Exception as e:  # noqa: BLE001 -- any failure leaves the card out, never the build
        print(f"wiki: no listing card, the content index could not be read: {e}", file=sys.stderr)
        return ""

    chips = [release["loader"]["id"], f'<span class="ks-chip--mono">{html.escape(release["version"])}</span>',
             f'{release["download"]["size"] / 1e6:.0f} MB']
    return "\n".join([
        '<a class="ks-listing" href="/guide/install/">',
        '<img src="/assets/listing-icon.png" alt="" width="80" height="80">',
        '<div class="ks-listing-body">',
        f'<div class="ks-listing-name">{html.escape(authored["name"])} '
        f'<span>by {html.escape(", ".join(authored["authors"]))}</span></div>',
        '<div class="ks-listing-chips">' + "".join(f'<span class="ks-chip">{c}</span>' for c in chips) + "</div>",
        "</div>",
        '<div class="ks-listing-stats">',
        f'<span title="Downloads from GitHub and SpaceDock, as the KSA content index counts them">'
        f"{DOWNLOAD_ICON}<strong>{downloads:,}</strong></span>",
        f'<span title="When {html.escape(release["version"])} was released">{DATE_ICON}'
        f'<time datetime="{released.date().isoformat()}">{released.day} {released:%b %Y}</time></span>',
        "</div>",
        "</a>",
    ])

# Models change only with a release and the viewer only with its pinned version.
# The guide was at /players/ before it was renamed; links to it are already out there.
REDIRECTS = "/players/* /guide/:splat 301\n"

HEADERS = """/models/*
  Cache-Control: public, max-age=86400
/assets/3d/*
  Cache-Control: public, max-age=604800
"""


def models() -> None:
    """Every part's model, assembled and compressed, and the viewer and its decoder beside them."""
    if not NODE.is_dir():
        sys.exit("wiki: the 3D models need tools/wiki's npm packages; run: cd tools/wiki && npm ci")
    raw = BUILD / "models-raw"
    if raw.exists():
        shutil.rmtree(raw)
    here = Path(__file__).resolve().parent
    subprocess.run([sys.executable, str(here / "models.py"), str(raw)], check=True)
    subprocess.run(["node", str(here / "compress.mjs"), str(raw), str(DOCS / "models")], check=True)

    three_d = DOCS / "assets/3d"

    shutil.copy(NODE / "@google/model-viewer/dist/model-viewer.min.js", three_d / "model-viewer.min.js")
    # The classic build: model-viewer loads the decoder as a plain script that defines MeshoptDecoder.
    shutil.copy(NODE / "meshoptimizer/meshopt_decoder.cjs", three_d / "meshopt_decoder.js")

    fonts = DOCS / "assets/fonts"
    fonts.mkdir(exist_ok=True)
    for family, weights in (("saira-condensed", (600, 700, 800)), ("ibm-plex-mono", (400, 500)),
                            ("ibm-plex-sans", (400, 500, 600))):
        for weight in weights:
            shutil.copy(NODE / f"@fontsource/{family}/files/{family}-latin-{weight}-normal.woff2",
                        fonts / f"{family}-{weight}.woff2")

    # After the viewer is in place: the stills are drawn by it.
    subprocess.run(["node", str(here / "stills.mjs"), str(DOCS)], check=True)


def assemble(with_models: bool, with_listing: bool = True) -> None:
    if DOCS.exists():
        shutil.rmtree(DOCS)
    shutil.copytree(WIKI / "pages", DOCS)

    (DOCS / "assets/3d").mkdir(parents=True, exist_ok=True)
    shutil.copy(ROOT / "branding/mark.svg", DOCS / "assets/mark.svg")
    shutil.copy(ROOT / "branding/mark-light.svg", DOCS / "assets/mark-light.svg")
    shutil.copy(ROOT / "branding/favicon.svg", DOCS / "assets/favicon.svg")
    shutil.copy(ROOT / "listing/icon.png", DOCS / "assets/listing-icon.png")
    (DOCS / "assets/3d/config.js").write_text(VIEWER_CONFIG)
    (DOCS / "_headers").write_text(HEADERS)
    (DOCS / "_redirects").write_text(REDIRECTS)

    config = (WIKI / "mkdocs.yml").read_text()
    marker = "      # generated: one entry per family\n"
    if marker not in config:
        sys.exit("wiki: wiki/mkdocs.yml has lost the marker the family pages are written at")
    names = part_names()
    pages, weapons, undescribed = weapon_pages(names)
    entries = ""
    for folder, title in [*((folder_of(f), t) for f, t in FAMILIES.items()), (MOUNTS_FOLDER, MOUNTS_TITLE)]:
        members = [w for w in weapons if w["page"].startswith(f"{folder}/")]
        if members:
            entries += f"      - {title}:\n          - {folder}/index.md\n"
            entries += "".join(f"          - {json.dumps(w['name'])}: {w['page']}\n" for w in members)
    (BUILD / "mkdocs.yml").write_text(config.replace(marker, entries))
    # A link preview's picture for each weapon, drawn by stills.mjs beside its still.
    (DOCS / "models").mkdir(exist_ok=True)
    (DOCS / "models/cards.json").write_text(json.dumps(
        {slug(w["part"]): {"name": w["name"], "family": w["family"]} for w in weapons}, indent=1))
    for page, text in pages.items():
        write(page, text)
    write("weapons/index.md", "\n".join([
        "# Weapons", "",
        "Every weapon that comes with KSArmory. The numbers come from the same files as the current release. "
        "You can switch off any family of weapons under *KSArmory → Built-in weapons*; the change takes effect when "
        "KSA next starts.",
        gallery(weapons), "", *weapon_table(weapons), ""]))
    home = DOCS / "index.md"
    if GALLERY_MARKER not in home.read_text():
        sys.exit(f"wiki: wiki/pages/index.md has lost the line {GALLERY_MARKER!r} the gallery is written at")
    # Inline rather than an <img>: its lettering is set on a circle in the site's own fonts.
    patch = (ROOT / "branding/patch.svg").read_text().replace('<svg ', '<svg aria-label="KSArmory mission patch" ', 1)
    card = listing_card() if with_listing else ""
    home.write_text(home.read_text().replace(GALLERY_MARKER, gallery(weapons)).replace(PATCH_MARKER, patch)
                    .replace(LISTING_MARKER, card))

    for src, page, _ in DOC_PAGES:
        write(page, relink((ROOT / src).read_text(), ROOT / src, page))
    for heading, page, title in README_PAGES:
        write(page, f"# {title}\n\n" + relink(readme_section(heading), ROOT / "README.md", page))

    if with_models:
        models()
        # Strict mode does not follow a URL in raw HTML, so a page naming a model nobody built would pass it.
        for page in (DOCS / "weapons").rglob("*.md"):
            for model in re.findall(r'src="/models/([^"]+)"', page.read_text()):
                if not (DOCS / "models" / model).is_file():
                    sys.exit(f"wiki: {page.name} shows models/{model}, which was not built")

    for name in undescribed:
        print(f"wiki: no paragraph in README.md's \"What ships with it\" describes '{name}'", file=sys.stderr)
    if undescribed:
        sys.exit(1)


def site_links(page: str) -> dict[str, str]:
    """Every repository file the site carries, as a link from this page to its own copy."""
    here = Path(page).parent
    return {src: Path(os.path.relpath(target, here)).as_posix() for src, target, _ in DOC_PAGES}


def write(page: str, text: str) -> None:
    target = DOCS / page
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--serve", action="store_true")
    parser.add_argument("--pages-only", action="store_true")
    args = parser.parse_args()

    if args.pages_only:
        # Its own folder: assembled where the site is, it would strip the models from a preview being served.
        global DOCS
        DOCS = BUILD / "check"
        assemble(with_models=False, with_listing=False)
        return 0
    assemble(with_models=True)
    zensical = shutil.which("zensical")
    if zensical is None:
        print("wiki: zensical is not installed; pip install -r wiki/requirements.txt", file=sys.stderr)
        return 2
    command = [zensical, "serve" if args.serve else "build", "-f", str(BUILD / "mkdocs.yml")]
    if args.serve:
        return subprocess.run(command).returncode

    command.append("--strict")
    # Zensical writes into the site folder without emptying it, so a page that was renamed would linger.
    shutil.rmtree(BUILD / "site", ignore_errors=True)
    if (code := subprocess.run(command).returncode) != 0:
        return code
    for page in (BUILD / "site").rglob("*.html"):
        if found := [host for host in FORBIDDEN_HOSTS if host in page.read_text()]:
            print(f"wiki: {page.relative_to(BUILD)} loads from {', '.join(found)}", file=sys.stderr)
            return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
