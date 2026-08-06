#!/usr/bin/env bash
#
# The mod must not talk to the network except when a player asks it to.
#
# There is exactly one outgoing request in this mod: the report a player writes and clicks Send
# on. That click is the permission. Anything else -- a version ping at startup, a usage count, a
# "check for updates" -- would be a request nobody agreed to, and would arrive as a surprise in
# someone's firewall log rather than as a feature.
#
# This checks it textually, the way check-boundary.sh guards the Sim/Ksa split: the networking
# types may appear in one file, and that file sends only from Send().
#
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT" || exit 1

# The one file allowed to reach the network, and the panel that calls it.
ALLOWED="src/KSArmory/Ksa/FeedbackClient.cs"

# Types that can open a connection. Sockets included: the point is any egress, not just HTTP.
PATTERN='HttpClient|WebClient|HttpWebRequest|WebRequest|TcpClient|UdpClient|Socket\(|ClientWebSocket|Dns\.'

FAIL=0

while IFS= read -r hit; do
    [[ -z "$hit" ]] && continue

    file="${hit%%:*}"
    if [[ "$file" == "$ALLOWED" ]]; then continue; fi

    echo "  network access outside $ALLOWED:" >&2
    echo "    $hit" >&2
    FAIL=1
done < <(grep -rnE "$PATTERN" src/KSArmory --include='*.cs' 2>/dev/null || true)

# The allowed file must still only send when asked. A request issued from a frame hook or a
# constructor is not something a player clicked.
if [[ -f "$ALLOWED" ]]; then
    if ! grep -q "public void Send(" "$ALLOWED"; then
        echo "  $ALLOWED no longer has a Send() entry point" >&2
        FAIL=1
    fi

    # One place that actually posts. More than one means a second path to audit.
    posts=$(grep -cE "PostAsJsonAsync|PostAsync|SendAsync|GetAsync|GetStringAsync" "$ALLOWED")
    if (( posts > 1 )); then
        echo "  $ALLOWED makes $posts requests; there should be one, from Send()" >&2
        FAIL=1
    fi
fi

if (( FAIL )); then
    echo >&2
    echo "The mod may only reach the network from a report the player clicked Send on." >&2
    exit 1
fi

echo "network ok: one outgoing request, and a player has to ask for it"
