#!/usr/bin/env bash
#
# Reports which KSA changes actually touch this mod, by reading the decompiled corpus in the
# private repository against docs/KSA-API-SURFACE.md.
#
#   ./tools/ksa-api-diff.sh ../ksa-game-assemblies              # vs the previous commit there
#   ./tools/ksa-api-diff.sh ../ksa-game-assemblies HEAD~3       # vs a specific ref
#   ./tools/ksa-api-diff.sh ../ksa-game-assemblies --members    # only the missing-member check
#   ./tools/ksa-api-diff.sh ../ksa-game-assemblies --files      # only the changed-file check
#
# Two questions:
#
#   1. Missing members: a member in the surface no longer in its assembly -- what the compiler
#      would report, as a list before building.
#   2. Changed files: the decompiled files defining the types the mod uses that this update
#      touched. A member that kept its name and signature and changed its behaviour shows only here.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SURFACE="$REPO_ROOT/docs/KSA-API-SURFACE.md"

TARGET=""
SINCE=""
MODE=all
for arg in "$@"; do
    case "$arg" in
        --members) MODE=members ;;
        --files)   MODE=files ;;
        *) [[ -z "$TARGET" ]] && TARGET="$arg" || SINCE="$arg" ;;
    esac
done

[[ -n "$TARGET" ]] || { echo "usage: $(basename "$0") <ksa-game-assemblies checkout> [ref]" >&2; exit 2; }
[[ -d "$TARGET/.git" ]] || { echo "error: $TARGET is not a git checkout" >&2; exit 1; }
[[ -f "$SURFACE" ]] || { echo "error: $SURFACE missing; run ./tools/api-surface.sh" >&2; exit 1; }
SRC="$TARGET/current/src"
[[ -d "$SRC" ]] || { echo "error: no decompiled sources in $SRC" >&2
                     echo "       run ./tools/decompile-assemblies.sh $TARGET" >&2; exit 1; }

# The surface: "## Assembly" then "### Full.Type.Name" then "- `member`" lines.
declare -A TYPE_ASM        # full type name -> assembly
declare -A TYPE_MEMBERS    # full type name -> newline-separated member texts
assembly=""; type=""
while IFS= read -r line; do
    case "$line" in
        '## '*)  assembly="${line#\#\# }"; type="" ;;
        '### '*) type="${line#\#\#\# }"; TYPE_ASM["$type"]="$assembly" ;;
        '- `'*)
            [[ -n "$type" ]] || continue
            m="${line#- \`}"; m="${m%\`}"
            TYPE_MEMBERS["$type"]="${TYPE_MEMBERS["$type"]:-}${m}"$'\n'
            ;;
    esac
done < "$SURFACE"

echo "surface: ${#TYPE_ASM[@]} types across the assemblies in $(basename "$SURFACE")"

missing_report() {
    echo
    echo "=== members in the surface that are missing from the new corpus ==="
    local found=0
    for type in "${!TYPE_ASM[@]}"; do
        local asm="${TYPE_ASM[$type]}"
        local dir="$SRC/$asm"
        [[ -d "$dir" ]] || { echo "  ?? $asm not decompiled - cannot check ${type}"; found=1; continue; }

        while IFS= read -r member; do
            [[ -n "$member" ]] || continue
            # Pull the identifier: the token before '(' for a method, the last token for a field.
            local name
            if [[ "$member" == *"("* ]]; then
                name="${member%%(*}"; name="${name##* }"; name="${name%%<*}"
            else
                name="${member##* }"
            fi
            # Constructors and operators are named by the language and cannot be renamed.
            [[ "$name" == .ctor || "$name" == .cctor || "$name" == op_* ]] && continue

            # Accessors reach IL as get_X/set_X/add_X/remove_X, a name the decompiled C# never spells.
            case "$name" in
                get_*|set_*)       name="${name#???_}" ;;
                add_*)             name="${name#add_}" ;;
                remove_*)          name="${name#remove_}" ;;
            esac

            # The type's own file first (ILSpy writes <Namespace>/<Type>.cs): searching the whole
            # assembly lets an unrelated caller keep a deleted member looking alive. The assembly is
            # the fallback only when that file cannot be found.
            local leaf="${type##*.}"; leaf="${leaf%%+*}"
            local file
            file="$(find "$dir" -name "${leaf}.cs" -print -quit 2>/dev/null || true)"

            if [[ -n "$file" ]]; then
                grep -qF -- "$name" "$file" && continue
                # Declared on a base type, or moved: still reachable, but worth a look.
                if grep -rqlF --include='*.cs' -- "$name" "$dir" 2>/dev/null; then
                    echo "  MOVED $type.$name — no longer declared in $(basename "$file")"
                    found=1
                    continue
                fi
            elif grep -rqlF --include='*.cs' -- "$name" "$dir" 2>/dev/null; then
                continue
            fi

            echo "  GONE  $type.$name"
            echo "        ($member)"
            found=1
        done <<< "${TYPE_MEMBERS[$type]:-}"
    done
    (( found )) || echo "  none - every member this mod binds to still exists"
}

changed_report() {
    local ref="$SINCE"
    if [[ -z "$ref" ]]; then
        ref="$(git -C "$TARGET" rev-parse --verify -q HEAD~1 || true)"
        [[ -n "$ref" ]] || { echo; echo "=== changed files ==="; \
            echo "  only one commit in $TARGET - nothing to diff against yet"; return 0; }
    fi

    echo
    echo "=== files defining types the mod uses, changed since $(git -C "$TARGET" rev-parse --short "$ref") ==="

    local changed
    changed="$(git -C "$TARGET" diff --name-only "$ref" -- current/src || true)"
    if [[ -z "$changed" ]]; then
        echo "  no decompiled sources changed"
        return 0
    fi

    local hits=0
    for type in "${!TYPE_ASM[@]}"; do
        # ILSpy writes Namespace/Type.cs; a nested type lives in its declaring type's file.
        local leaf="${type##*.}"; leaf="${leaf%%+*}"
        local match
        match="$(printf '%s\n' "$changed" | grep -E "/${leaf}\.cs$" || true)"
        [[ -n "$match" ]] || continue
        echo "  $type"
        printf '%s\n' "$match" | sed 's/^/      /'
        hits=$((hits + 1))
    done

    echo
    if (( hits )); then
        echo "  $hits of our types have changed definitions. Read them:"
        echo "    git -C $TARGET diff $ref -- current/src/<assembly>/<path>"
        echo
        echo "  A member that kept its name and signature can still have changed meaning."
        echo "  That is what these files are for; the compiler will not tell you."
    else
        echo "  none of the types this mod uses were touched"
    fi
    echo
    echo "  (total decompiled files changed: $(printf '%s\n' "$changed" | wc -l))"
}

case "$MODE" in
    members) missing_report ;;
    files)   changed_report ;;
    all)     missing_report; changed_report ;;
esac
