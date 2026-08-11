#!/usr/bin/env bash
#
# Reports which KSA changes actually touch this mod, by reading the decompiled corpus in the
# private repository against docs/KSA-API-SURFACE.md.
#
#   ./tools/ksa-api-diff.sh ../ksa-game-assemblies              # vs the previous commit there
#   ./tools/ksa-api-diff.sh ../ksa-game-assemblies HEAD~3       # vs a specific ref
#   ./tools/ksa-api-diff.sh ../ksa-game-assemblies --members    # only the missing-member check
#
# A KSA update changes thousands of lines across a quarter of a million. Almost none of it can
# possibly affect this mod, which binds only to the members in docs/KSA-API-SURFACE.md. This
# narrows the diff to the ones that can, and separately checks whether any member the mod
# depends on has vanished outright.
#
# Two questions, because they fail differently:
#
#   1. Missing members - a member in the surface that no longer appears anywhere in its
#      assembly. Mechanical, precise, and the same set the compiler would shout about, except
#      you get it as a list before building rather than as cascading errors after.
#   2. Changed files - the decompiled files defining the types the mod uses, that this update
#      touched. This is the one the compiler cannot give you: a member that kept its name and
#      signature and changed its behaviour compiles clean and is wrong at runtime. Read these.
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

# ---------------------------------------------------------------------------------------------
# Parse the surface: "## Assembly" then "### Full.Type.Name" then "- `member`" lines.
# ---------------------------------------------------------------------------------------------
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

# ---------------------------------------------------------------------------------------------
# 1. Members that no longer exist anywhere in their assembly.
# ---------------------------------------------------------------------------------------------
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
            # Constructors and operators are named by the language, not by KSA; a rename is not
            # a thing that can happen to them.
            [[ "$name" == .ctor || "$name" == .cctor || "$name" == op_* ]] && continue

            # Properties and events reach IL as get_X/set_X/add_X/remove_X, but the decompiled
            # C# says `public T X { get; set; }` - the accessor name appears nowhere. Searching
            # for it reports every property this mod uses as missing.
            case "$name" in
                get_*|set_*)       name="${name#???_}" ;;
                add_*)             name="${name#add_}" ;;
                remove_*)          name="${name#remove_}" ;;
            esac

            # Look in the type's own file first. ILSpy writes each type to <Namespace>/<Type>.cs,
            # so that is where a declaration is; searching the whole assembly instead lets an
            # unrelated *caller* elsewhere keep a deleted member looking alive. Fall back to the
            # assembly only when the file cannot be located, so inherited members on a type whose
            # declaration is never seen do not report as missing.
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

# ---------------------------------------------------------------------------------------------
# 2. Decompiled files defining the types the mod uses, that changed in this update.
# ---------------------------------------------------------------------------------------------
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
