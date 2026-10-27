#!/usr/bin/env bash
#
# Prints KSA's user folder -- the one holding Logs/, mods/ and the vehicle saves.
#
#   ./tools/ksa-user-dir.sh                     # print it, or fail saying so
#   tail -F "$(./tools/ksa-user-dir.sh)/Logs/KSArmory.log"
#
# Searched rather than assumed: the folder differs between Windows and Linux, and under WSL the
# Windows username need not match the Linux one. KSA_USER_DIR overrides the search.
set -euo pipefail

ksa_user_dir() {
    if [[ -n "${KSA_USER_DIR:-}" ]]; then
        printf '%s\n' "$KSA_USER_DIR"
        return 0
    fi

    local candidates=()

    # The guess by username first: searching /mnt/c/Users is slow and hits other profiles.
    if [[ -d /mnt/c/Users ]]; then
        candidates+=("/mnt/c/Users/$(whoami)/Documents/My Games/Kitten Space Agency")
        while IFS= read -r dir; do
            candidates+=("$dir")
        done < <(find /mnt/c/Users -maxdepth 4 -type d -path '*My Games/Kitten Space Agency' 2>/dev/null)
    fi

    # Native Linux: the folders Log.cs tries, though Log.cs tries Documents/My Games first.
    candidates+=(
        "${XDG_DATA_HOME:-$HOME/.local/share}/Kitten Space Agency"
        "$HOME/.config/Kitten Space Agency"
        "$HOME/My Games/Kitten Space Agency"
        "$HOME/Documents/My Games/Kitten Space Agency"
    )

    for dir in "${candidates[@]}"; do
        [[ -d "$dir" ]] && { printf '%s\n' "$dir"; return 0; }
    done
    return 1
}

# Sourced for the function; run for the answer.
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
    if ! ksa_user_dir; then
        echo "error: could not locate the KSA user folder on this machine." >&2
        echo "       set KSA_USER_DIR to the folder holding Logs/ and mods/ and retry" >&2
        exit 1
    fi
fi
