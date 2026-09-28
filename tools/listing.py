#!/usr/bin/env python3
"""The store listing -- listing/ -- rendered for the two places it is published.

    ./tools/listing.py --check            # offline: every image exists, fits and is referenced
    ./tools/listing.py borea              # the TOML for listings/KSArmory.toml in content-index
    ./tools/listing.py editor             # the same, laid out for Borea's in-app listing editor
    ./tools/listing.py spacedock          # the markdown for SpaceDock's description box
    ./tools/listing.py borea --ref v0.9.1 # ...pinned to a ref other than HEAD

listing/description.md writes an image as `![alt](images/<id>.png)`, which GitHub renders as it
stands. Borea shows only `ksa-image:<id>` backed by a record of the file's hash and size, and
SpaceDock only an absolute URL, so both are rewritten here. Every URL is pinned to a commit rather
than a branch: the index fetches the image and fails the listing when its bytes stop matching the
record, and a branch URL changes under a record whenever the file is replaced.

Nothing here reaches the network, and nothing is published: the output is pasted.
"""
import argparse
import re
import struct
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
LISTING = REPO / "listing"
GITHUB = "LaurensDeV/KSArmory"

IMAGE = re.compile(r"!\[([^\]]*)\]\(([^)\s]+)\)")
ID = re.compile(r"^[A-Za-z0-9](?:[A-Za-z0-9_-]{0,62}[A-Za-z0-9])?$")

# content-index's schemas/authored.schema.json.
MAX_DESCRIPTION_IMAGES = 16
DESCRIPTION_MAX_PX, DESCRIPTION_MAX_BYTES = 2048, 1 << 20
ICON_MIN_PX, ICON_MAX_PX, ICON_MAX_BYTES = 256, 2048, 256 << 10


def dimensions(data: bytes) -> tuple[int, int] | None:
    if data[:8] == b"\x89PNG\r\n\x1a\n" and data[12:16] == b"IHDR":
        return struct.unpack(">II", data[16:24])
    if data[:2] == b"\xff\xd8":
        i = 2
        while i + 9 < len(data):
            if data[i] != 0xFF:
                i += 1
                continue
            marker = data[i + 1]
            length = struct.unpack(">H", data[i + 2:i + 4])[0]
            if marker in (0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7, 0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF):
                h, w = struct.unpack(">HH", data[i + 5:i + 9])
                return w, h
            i += 2 + length
    return None


def references(text: str) -> list[tuple[str, str]]:
    return IMAGE.findall(text)


def problems() -> list[str]:
    out = []
    text = (LISTING / "description.md").read_text()
    if not (LISTING / "abstract.txt").read_text().strip():
        out.append("listing/abstract.txt is empty")

    seen = set()
    for _, target in references(text):
        if not target.startswith("images/"):
            out.append(f"{target}: Borea shows only images under listing/images/")
            continue
        path = LISTING / target
        if path.parent != LISTING / "images" or not ID.match(path.stem):
            out.append(f"{target}: the file name is the image's id -- 1-64 letters, digits, - and _")
            continue
        if path.stem in seen:
            continue
        seen.add(path.stem)
        if not path.is_file():
            out.append(f"{target}: no such file")
            continue
        data = path.read_bytes()
        size = dimensions(data)
        if size is None:
            out.append(f"{target}: not a PNG or JPEG")
        elif max(size) > DESCRIPTION_MAX_PX:
            out.append(f"{target}: {size[0]}x{size[1]}, over {DESCRIPTION_MAX_PX} px")
        if len(data) > DESCRIPTION_MAX_BYTES:
            out.append(f"{target}: {len(data)} bytes, over {DESCRIPTION_MAX_BYTES}")
    if len(seen) > MAX_DESCRIPTION_IMAGES:
        out.append(f"{len(seen)} description images, over {MAX_DESCRIPTION_IMAGES}")

    for path in sorted((LISTING / "images").iterdir()):
        if path.name != ".gitkeep" and path.stem not in seen:
            out.append(f"listing/images/{path.name}: not referenced by description.md")

    icon = (LISTING / "icon.png").read_bytes()
    size = dimensions(icon)
    if size is None:
        out.append("listing/icon.png: not a PNG")
    else:
        short, long = min(size), max(size)
        if not (ICON_MIN_PX <= short and long <= ICON_MAX_PX and long <= 2 * short):
            out.append(f"listing/icon.png: {size[0]}x{size[1]} is not a valid icon")
    if len(icon) > ICON_MAX_BYTES:
        out.append(f"listing/icon.png: {len(icon)} bytes, over {ICON_MAX_BYTES}")
    return out


def pinned(ref: str) -> str:
    """The commit a URL is pinned to, refusing one whose files differ from the worktree's."""
    sha = subprocess.run(["git", "rev-parse", f"{ref}^{{commit}}"], cwd=REPO, check=True,
                         capture_output=True, text=True).stdout.strip()
    stale = []
    for path in [LISTING / "icon.png", *sorted((LISTING / "images").glob("*"))]:
        if path.name == ".gitkeep":
            continue
        rel = path.relative_to(REPO).as_posix()
        at = subprocess.run(["git", "show", f"{sha}:{rel}"], cwd=REPO, capture_output=True)
        if at.returncode != 0 or at.stdout != path.read_bytes():
            stale.append(rel)
    if stale:
        sys.exit(f"{ref} does not hold these as they are on disk -- commit them first:\n  "
                 + "\n  ".join(stale))
    remote = subprocess.run(["git", "branch", "-r", "--contains", sha], cwd=REPO,
                            capture_output=True, text=True).stdout.strip()
    if not remote:
        print(f"warning: {sha[:12]} is on no remote branch yet -- push it before pasting",
              file=sys.stderr)
    return sha


def url(sha: str, rel: str) -> str:
    return f"https://raw.githubusercontent.com/{GITHUB}/{sha}/listing/{rel}"


def record(sha: str, rel: str, image_id: str | None = None) -> str:
    import hashlib
    data = (LISTING / rel).read_bytes()
    w, h = dimensions(data)
    lines = []
    if image_id:
        lines.append(f'id = "{image_id}"')
    lines += [f'url = "{url(sha, rel)}"',
              f'sha256 = "{hashlib.sha256(data).hexdigest()}"',
              f"width = {w}", f"height = {h}", f"size = {len(data)}"]
    return "\n".join(lines)


def toml_string(text: str) -> str:
    return '"""\n' + text.replace("\\", "\\\\").replace('"""', '""\\"') + '"""'


def borea(sha: str) -> str:
    text = (LISTING / "description.md").read_text()
    ids = list(dict.fromkeys(Path(t).stem for _, t in references(text)))
    body = IMAGE.sub(lambda m: f"![{m[1]}](ksa-image:{Path(m[2]).stem})", text)
    abstract = (LISTING / "abstract.txt").read_text().strip().replace('"', '\\"')
    out = [f'abstract = "{abstract}"', f"description = {toml_string(body)}", "",
           "[images.icon]", record(sha, "icon.png")]
    for image_id in ids:
        rel = next(t for _, t in references(text) if Path(t).stem == image_id)
        out += ["", "[[images.description]]", record(sha, rel, image_id)]
    return "\n".join(out) + "\n"


def editor(sha: str) -> str:
    """Borea's editor measures each image itself, so it needs only the ids and URLs."""
    text = (LISTING / "description.md").read_text()
    body = IMAGE.sub(lambda m: f"![{m[1]}](ksa-image:{Path(m[2]).stem})", text)
    targets = list(dict.fromkeys(t for _, t in references(text)))
    rule = "-" * 72
    out = ["ABSTRACT", rule, (LISTING / "abstract.txt").read_text().strip(), "",
           "DESCRIPTION", rule, body.rstrip(), "",
           "IMAGES", rule, f"icon  {url(sha, 'icon.png')}"]
    width = max((len(Path(t).stem) for t in targets), default=0)
    out += [f"{Path(t).stem:<{width}}  {url(sha, t)}" for t in targets]
    return "\n".join(out) + "\n"


def spacedock(sha: str) -> str:
    text = (LISTING / "description.md").read_text()
    return IMAGE.sub(lambda m: f"![{m[1]}]({url(sha, m[2])})", text)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[1],
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("target", nargs="?", choices=["borea", "editor", "spacedock"])
    ap.add_argument("--ref", default="HEAD", help="the commit the image URLs are pinned to")
    ap.add_argument("--check", action="store_true", help="validate listing/ and exit")
    args = ap.parse_args()

    found = problems()
    for p in found:
        print(f"listing: {p}", file=sys.stderr)
    if args.check or found:
        return 1 if found else 0
    if args.target is None:
        ap.error("name a target, or --check")

    sha = pinned(args.ref)
    sys.stdout.write({"borea": borea, "editor": editor, "spacedock": spacedock}[args.target](sha))
    return 0


if __name__ == "__main__":
    sys.exit(main())
