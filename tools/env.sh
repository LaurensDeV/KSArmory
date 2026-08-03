#!/usr/bin/env bash
#
# Puts a .NET 10 SDK on PATH. Source this, do not execute it:
#
#     source tools/env.sh
#
# The mod targets net10.0 because that is what KSA itself runs on. Distro packages are
# typically still on .NET 8, which fails with:
#
#     error NETSDK1045: The current .NET SDK does not support targeting .NET 10.0
#
# so this prefers a private SDK in ~/.dotnet and only falls back to whatever is on PATH.

_ad_has_net10() {
    local dotnet_bin="$1"
    [[ -x "$dotnet_bin" ]] || return 1
    "$dotnet_bin" --list-sdks 2>/dev/null | grep -q '^10\.'
}

if _ad_has_net10 "$HOME/.dotnet/dotnet"; then
    export DOTNET_ROOT="$HOME/.dotnet"
    export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
elif _ad_has_net10 "$(command -v dotnet 2>/dev/null)"; then
    : # already usable
else
    echo "error: no .NET 10 SDK found." >&2
    echo "       Install one with:" >&2
    echo "         curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir \$HOME/.dotnet" >&2
    echo "       then re-run. (Your distro's dotnet is likely 8.x, which cannot target net10.0.)" >&2
    return 1 2>/dev/null || exit 1
fi

unset -f _ad_has_net10
