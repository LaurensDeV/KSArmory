#!/usr/bin/env bash
#
# Prints the KSA install -- the folder holding KSA.dll and Content/.
#
#   ./tools/ksa-game-dir.sh                     # print it, or fail saying so
#   KSA_DIR="$(./tools/ksa-game-dir.sh)" ./tools/sync-import.sh
#
# Searched rather than assumed, for the same reason as ksa-user-dir.sh: the install moves. It was
# C:\Program Files\Kitten Space Agency when the game was unpacked by hand, and the Ahwoo/Neutrino
# launcher puts it under its own folder instead. Both are tried, newest convention first, so a
# machine with either keeps working. KSA_DIR overrides the search.
set -euo pipefail

ksa_game_dir() {
    if [[ -n "${KSA_DIR:-}" ]]; then
        printf '%s\n' "$KSA_DIR"
        return 0
    fi

    local candidates=(
        # The Ahwoo/Neutrino launcher's library.
        "/mnt/c/Program Files/Ahwoo/Games/Kitten Space Agency"
        "C:/Program Files/Ahwoo/Games/Kitten Space Agency"
        "$HOME/.local/share/Ahwoo/Games/Kitten Space Agency"
        # Unpacked by hand, which is where it used to go.
        "/mnt/c/Program Files/Kitten Space Agency"
        "C:/Program Files/Kitten Space Agency"
        "/c/Program Files/Kitten Space Agency"
        # Steam, and a native Linux unpack.
        "$HOME/.local/share/Steam/steamapps/common/Kitten Space Agency"
        "$HOME/Kitten Space Agency"
    )

    for dir in "${candidates[@]}"; do
        [[ -n "$dir" && -f "$dir/KSA.dll" ]] && { printf '%s\n' "$dir"; return 0; }
    done

    # A launcher folder this list does not know: look for the game under any Ahwoo-style library
    # rather than give up, since the one thing that identifies an install is KSA.dll beside Content/.
    local hit
    for root in "/mnt/c/Program Files" "/mnt/c/Program Files (x86)" "/mnt/c/Games"; do
        [[ -d "$root" ]] || continue
        hit="$(find "$root" -maxdepth 4 -name 'KSA.dll' 2>/dev/null | head -1 || true)"
        [[ -n "$hit" ]] && { dirname "$hit"; return 0; }
    done
    return 1
}

# Sourced for the function; run for the answer.
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
    if ! ksa_game_dir; then
        echo "error: could not locate the KSA install on this machine." >&2
        echo "       set KSA_DIR to the folder holding KSA.dll and retry" >&2
        exit 1
    fi
fi
