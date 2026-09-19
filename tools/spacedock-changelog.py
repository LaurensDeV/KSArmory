#!/usr/bin/env python3
"""
Cuts one release's CHANGELOG.md section down to what SpaceDock will accept.

SpaceDock stores a version's changelog in a 10,000-character column and refuses the whole upload
with a 400 past it -- the archive included. A release's notes list every feat and fix since the
last one, so a release after a long run on dev overruns it: 0.9.0's were 60,691.

A section that fits is sent exactly as written. One that does not loses its commit links first,
which are most of its length and no use to a player, and then the tail of each section, each
cut marked with how many entries it hid and the whole ending on a link to the full notes.

    ./tools/spacedock-changelog.py 0.9.0 <release url> [CHANGELOG.md]   print what SpaceDock gets
    ./tools/spacedock-changelog.py --check      fail if any released section would not fit
"""

import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent

# ModVersion.changelog is Unicode(10000) in SpaceDock's KerbalStuff/objects.py, and the upload
# endpoint compares len() against it -- characters, not bytes. The rendered HTML's own limit is
# 20,000, and this format renders at 1.2-1.3x, so the markdown limit is the one that binds.
LIMIT = 10_000

VERSION_HEADING = re.compile(r"^#{1,2} \[?(\d+\.\d+\.\d+)\]?", re.M)
COMMIT_LINK = re.compile(r" \(\[[0-9a-f]{7,40}\]\([^)]*\)\)")


def section(changelog, version):
    """The release's own notes: its heading down to the next release's."""
    headings = list(VERSION_HEADING.finditer(changelog))
    for i, h in enumerate(headings):
        if h.group(1) == version:
            end = headings[i + 1].start() if i + 1 < len(headings) else len(changelog)
            return changelog[h.start():end].strip()
    return None


def parse(notes):
    """(preamble, [(heading, [entry, ...]), ...]) -- an entry keeps any continuation lines."""
    preamble, sections = [], []
    for line in notes.splitlines():
        if line.startswith("### "):
            sections.append((line, []))
        elif not sections:
            preamble.append(line)
        elif line.startswith("* "):
            sections[-1][1].append(line)
        elif line.strip() and sections[-1][1]:
            sections[-1][1][-1] += "\n" + line
    return "\n".join(preamble).strip(), sections


def render(preamble, sections, kept, footer):
    parts = [preamble]
    for (heading, entries), n in zip(sections, kept):
        lines = entries[:n]
        if n < len(entries):
            lines.append(f"* …and {len(entries) - n} more")
        parts.append(heading + "\n\n" + "\n".join(lines))
    parts.append(footer)
    return "\n\n".join(parts) + "\n"


def shape(notes, url):
    if len(notes) <= LIMIT:
        return notes

    footer = f"The full notes, with every commit linked, are on the GitHub release: {url}"
    preamble, sections = parse(COMMIT_LINK.sub("", notes))

    # Filled in order, Features first, and the first cut is the last: every section after it
    # keeps only its heading and a count, rather than whichever short entries still squeeze in.
    kept = [0] * len(sections)
    for i, (_, entries) in enumerate(sections):
        while kept[i] < len(entries):
            kept[i] += 1
            if len(render(preamble, sections, kept, footer)) > LIMIT:
                kept[i] -= 1
                return render(preamble, sections, kept, footer)
    return render(preamble, sections, kept, footer)


def check():
    changelog = (REPO / "CHANGELOG.md").read_text(encoding="utf-8")
    versions = [m.group(1) for m in VERSION_HEADING.finditer(changelog)]
    problems, largest = 0, (0, None)
    for v in versions:
        notes = section(changelog, v)
        shaped = shape(notes, f"https://example.invalid/v{v}")
        if len(shaped) > LIMIT:
            print(f"  {v}: {len(shaped)} characters after cutting, over SpaceDock's {LIMIT}")
            problems += 1
        if len(notes) <= LIMIT and shaped != notes:
            print(f"  {v}: fits as written, and was changed anyway")
            problems += 1
        largest = max(largest, (len(notes), v))
    if problems:
        return 1
    print(f"every one of {len(versions)} released changelogs fits SpaceDock's {LIMIT} characters "
          f"(the longest, {largest[1]}, is {largest[0]} as written)")
    return 0


def main(argv):
    if argv == ["--check"]:
        return check()
    if len(argv) not in (2, 3):
        sys.exit("usage: spacedock-changelog.py VERSION RELEASE_URL [CHANGELOG.md] | --check")
    version, url = argv[:2]
    path = Path(argv[2]) if len(argv) == 3 else REPO / "CHANGELOG.md"
    notes = section(path.read_text(encoding="utf-8"), version)
    if notes is None:
        sys.exit(f"error: {path} has no section for {version}")
    sys.stdout.write(shape(notes, url))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
