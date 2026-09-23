#!/usr/bin/env bash
#
# Compiles the mod's compute shaders here, in a second, rather than in the game after a build, a
# deploy and a launch. KSA compiles a mod's shader only at load, so a GLSL error -- a reserved word
# used as a name, a missing semicolon -- otherwise costs a flight to find.
#
#     ./tools/check-shaders.sh             # compile both, against the game's own shader library
#     ./tools/check-shaders.sh --install   # fetch Khronos' glslangValidator into ~/.cache first
#
# The shader includes KSA's atmosphere library, which is the game's and is not in this repository,
# so a machine with no install skips with a notice -- CI does. glslang takes the include as a search
# path where KSA's shaderc takes Ksa/CoreShaderInclude.cs's absolute one; the header is written
# here the same way, relative.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSA_DIR="${KSA_DIR:-/mnt/c/Program Files/Kitten Space Agency}"
LIBRARY="$KSA_DIR/Content/Core/Shaders"
CACHE="$HOME/.cache/ksarmory/glslang"
VERSION="16.6.0"

if [[ "${1:-}" == "--install" ]]; then
    mkdir -p "$CACHE"
    curl -sSL -o "$CACHE/glslang.tar.gz" \
        "https://github.com/KhronosGroup/glslang/releases/download/$VERSION/glslang-$VERSION-linux-x86_64-release.tar.gz"
    tar xzf "$CACHE/glslang.tar.gz" -C "$CACHE"
    rm "$CACHE/glslang.tar.gz"
fi

GLSLANG="$(command -v glslangValidator || true)"
[[ -z "$GLSLANG" && -x "$CACHE/bin/glslangValidator" ]] && GLSLANG="$CACHE/bin/glslangValidator"

if [[ -z "$GLSLANG" ]]; then
    echo "skipped: no glslangValidator -- ./tools/check-shaders.sh --install fetches one"
    exit 0
fi

if [[ ! -d "$LIBRARY" ]]; then
    echo "skipped: no KSA shader library at '$LIBRARY' -- set KSA_DIR"
    exit 0
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

mkdir -p "$WORK/Content"
printf '#include "Common/Global.glsl"\n#include "Atmosphere/AtmosphereLuts.glsl"\n' > "$WORK/Content/CoreAtmosphere.glsl"

failed=0
for shader in "$REPO_ROOT"/src/KSArmory/Shaders/*.comp; do
    name="$(basename "$shader")"

    # shaderc takes #include without being asked; glslang wants the extension named.
    awk 'NR==1 { print; print "#extension GL_GOOGLE_include_directive : require"; next } 1' \
        "$shader" > "$WORK/$name"

    if out="$("$GLSLANG" -V --target-env vulkan1.3 "-I$LIBRARY" -o /dev/null "$WORK/$name" 2>&1)"; then
        echo "  $name compiles"
    else
        failed=1
        # The line numbers are the file's own plus the one inserted above.
        echo "$out" | grep -E "ERROR" | sed "s#$WORK/##" | sed 's/^/  /'
        echo "  (line numbers are one past the file's: the include directive is inserted)"
    fi
done

exit "$failed"
