#!/usr/bin/env bash
#
# Captures the Windows screen to a PNG so the game can be inspected from here.
#
#     ./tools/screenshot.sh                  # capture to a timestamped file
#     ./tools/screenshot.sh shot.png         # capture to a specific name
#     ./tools/screenshot.sh shot.png 5       # wait 5 seconds first
#     FORCE=1 ./tools/screenshot.sh          # capture even if the game is not in front
#
# Refuses unless the game is the foreground window: this grabs the whole primary screen, so an
# unattended run with the game behind something photographs that something instead.
#
# Writes into ./screenshots/ (gitignored). Useful when a change is visual and the log
# cannot tell you whether it worked.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="$REPO_ROOT/screenshots"
mkdir -p "$OUT_DIR"

NAME="${1:-shot-$(date +%H%M%S).png}"
DELAY="${2:-0}"
OUT="$OUT_DIR/$NAME"

if [[ "$DELAY" != "0" ]]; then
    echo "capturing in ${DELAY}s..."
    sleep "$DELAY"
fi

# PowerShell writes to a Windows path; translate afterwards rather than fighting quoting.
WIN_TMP='C:\Windows\Temp\ksa-shot.png'

# Bring the game forward first. This captures the whole screen, so an unfocused game gives a
# picture of whatever is on top: a convincing-looking file that says nothing about the game.
powershell.exe -NoProfile -Command "
\$sm = Get-Process StarMap -ErrorAction SilentlyContinue | Select-Object -First 1
if (\$sm -and \$sm.MainWindowHandle -ne 0) {
  Add-Type -AssemblyName Microsoft.VisualBasic
  [Microsoft.VisualBasic.Interaction]::AppActivate(\$sm.Id)
  Start-Sleep -Milliseconds 600
}
" >/dev/null 2>&1 || true

# Refuse unless the game is actually in front. This captures the whole screen, so a failed
# AppActivate -- which happens, and silently -- means photographing whatever the machine's owner
# had open and writing it into the repo. A missing screenshot is recoverable; a captured desktop
# is not, and nothing downstream would notice either.
#
# Checked here rather than trusted above: AppActivate reports success it did not achieve.
FOREGROUND="$(powershell.exe -NoProfile -Command "
Add-Type -Namespace W -Name A -MemberDefinition '
  [DllImport(\"user32.dll\")] public static extern IntPtr GetForegroundWindow();
  [DllImport(\"user32.dll\")] public static extern int GetWindowThreadProcessId(IntPtr h, out int p);'
\$p = 0
[void][W.A]::GetWindowThreadProcessId([W.A]::GetForegroundWindow(), [ref]\$p)
(Get-Process -Id \$p -ErrorAction SilentlyContinue).ProcessName
" 2>/dev/null | tr -d '\r\n ')"

if [[ "$FOREGROUND" != "StarMap" && "$FOREGROUND" != "KSA" ]]; then
    echo "error: the game is not in front (foreground is '${FOREGROUND:-unknown}')." >&2
    echo "  refusing to capture -- this grabs the whole screen, not the game window." >&2
    echo "  click the game, or pass --force if you really want whatever is on screen." >&2
    [[ "${FORCE:-0}" == "1" ]] || exit 1
fi

powershell.exe -NoProfile -Command "
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
\$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
\$bmp = New-Object System.Drawing.Bitmap \$b.Width, \$b.Height
\$g = [System.Drawing.Graphics]::FromImage(\$bmp)
\$g.CopyFromScreen(\$b.Location, [System.Drawing.Point]::Empty, \$b.Size)
\$bmp.Save('$WIN_TMP', [System.Drawing.Imaging.ImageFormat]::Png)
\$g.Dispose(); \$bmp.Dispose()
" >/dev/null 2>&1

if [[ ! -f /mnt/c/Windows/Temp/ksa-shot.png ]]; then
    echo "error: capture failed -- no file produced" >&2
    exit 1
fi

mv /mnt/c/Windows/Temp/ksa-shot.png "$OUT"
echo "$OUT"
