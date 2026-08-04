#!/usr/bin/env bash
#
# Builds the mod and attaches it to a GitHub release.
#
#   ./tools/publish-release.sh              # attach to the release for the current version
#   ./tools/publish-release.sh v0.1.0       # ...or a specific tag
#   ./tools/publish-release.sh v0.1.0 --create   # create the release too, if it does not exist
#
# semantic-release cuts releases from a hosted runner, which cannot build this mod: that needs
# KSA's assemblies, and they are not redistributable. Until a self-hosted runner exists, this is
# the other half of a release - run it from a machine with KSA installed.
#
# Needs the gh CLI, authenticated (`gh auth login`).
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

command -v gh >/dev/null || { echo "error: gh CLI not found (https://cli.github.com)" >&2; exit 1; }
gh auth status >/dev/null 2>&1 || { echo "error: gh is not authenticated; run 'gh auth login'" >&2; exit 1; }

TAG="${1:-}"
CREATE=0
for arg in "$@"; do [[ "$arg" == "--create" ]] && CREATE=1; done
[[ "$TAG" == "--create" ]] && TAG=""

if [[ -z "$TAG" ]]; then
    VERSION="$(grep -oP '(?<=<Version>)[^<]+' src/KSArmory/KSArmory.csproj | head -1)"
    TAG="v$VERSION"
    echo "no tag given; using the version in the project file: $TAG"
else
    VERSION="${TAG#v}"
fi

# The archive must match the tag, not whatever the working tree happens to be.
if ! git rev-parse -q --verify "refs/tags/$TAG" >/dev/null; then
    echo "error: no tag $TAG. Create it, or let semantic-release cut one." >&2
    exit 1
fi

if [[ -n "$(git status --porcelain)" ]]; then
    echo "warning: working tree is dirty; the archive may not match $TAG" >&2
fi

if ! git merge-base --is-ancestor "$TAG" HEAD 2>/dev/null; then
    echo "warning: HEAD is not descended from $TAG; check you are on the right commit" >&2
fi

echo "building $VERSION"
./tools/package.sh --version "$VERSION" >/dev/null
ARCHIVE="dist/KSArmory-$VERSION.zip"
[[ -f "$ARCHIVE" ]] || { echo "error: $ARCHIVE was not produced" >&2; exit 1; }

if gh release view "$TAG" >/dev/null 2>&1; then
    echo "attaching $ARCHIVE to $TAG"
    gh release upload "$TAG" "$ARCHIVE" --clobber
elif (( CREATE )); then
    echo "creating release $TAG"
    gh release create "$TAG" "$ARCHIVE" \
        --title "$TAG" \
        --generate-notes \
        --notes "Drop the \`KSArmory\` folder into your KSA user directory's \`mods/\`, register it in \`manifest.toml\`, and launch through StarMap. See the README for the full install guide."
else
    echo "error: no release exists for $TAG." >&2
    echo "       pass --create to make one, or let a push to main cut it first." >&2
    exit 1
fi

echo
gh release view "$TAG" --json assets -q '.assets[] | "  \(.name)  \(.size) bytes"'
