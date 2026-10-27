#!/usr/bin/env bash
#
# Records the exact KSA API surface this mod depends on.
#
#   ./tools/api-surface.sh            # regenerate docs/KSA-API-SURFACE.md
#   ./tools/api-surface.sh --check    # fail if the committed file is stale (for CI)
#
# Read out of the compiled KSArmory.dll's TypeRef and MemberRef tables, not the source, so it is
# exactly what the compiler bound. A KSA change to a member listed here can break the mod; one to a
# member not listed cannot. The upgrade-ksa skill drives off it. Names and signatures only, so it is
# safe to commit.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=/dev/null
source "$REPO_ROOT/tools/env.sh"

OUT="$REPO_ROOT/docs/KSA-API-SURFACE.md"
CHECK=0
[[ "${1:-}" == "--check" ]] && CHECK=1

MOD_DLL="$REPO_ROOT/src/KSArmory/bin/Release/net10.0/KSArmory.dll"

# Always, not only when the DLL is missing: a stale one reports a match that says nothing about the
# code, and an incremental build costs about as long as the check.
echo "building the mod first (the surface is read from the compiled assembly)..."
"$REPO_ROOT/tools/build.sh" >/dev/null
[[ -f "$MOD_DLL" ]] || { echo "error: $MOD_DLL not found; run ./tools/build.sh" >&2; exit 1; }

# Build the extractor rather than `dotnet run` it: run rebuilds on every invocation, and its
# argument forwarding can swallow an argument.
dotnet build -c Release -v quiet --nologo "$REPO_ROOT/tools/apisurface/ApiSurface.csproj" >/dev/null
EXTRACTOR="$REPO_ROOT/tools/apisurface/bin/Release/net10.0/apisurface"
[[ -x "$EXTRACTOR" ]] || { echo "error: apisurface did not build" >&2; exit 1; }

generated="$("$EXTRACTOR" "$MOD_DLL")"

if (( CHECK )); then
    if [[ ! -f "$OUT" ]]; then
        echo "error: $(basename "$OUT") does not exist; run ./tools/api-surface.sh" >&2
        exit 1
    fi
    if ! diff -q <(printf '%s\n' "$generated") "$OUT" >/dev/null; then
        echo "error: docs/KSA-API-SURFACE.md is stale — the mod's KSA dependencies changed." >&2
        echo >&2
        diff <(printf '%s\n' "$generated") "$OUT" | head -40 >&2
        echo >&2
        echo "rerun ./tools/api-surface.sh and commit the result" >&2
        exit 1
    fi
    echo "API surface matches the code ($(grep -c '^- `' "$OUT") members)"
    exit 0
fi

mkdir -p "$(dirname "$OUT")"
printf '%s\n' "$generated" > "$OUT"
sed -n '/^[0-9]* types/p' "$OUT" | sed 's/^/  /'
echo "  wrote docs/$(basename "$OUT")"
