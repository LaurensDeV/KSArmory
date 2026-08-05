#!/usr/bin/env bash
#
# Writes a ready-to-fly single-part craft into your KSA vehicle saves, so testing the mod
# does not require a trip through the editor.
#
#     ./tools/install-testcraft.sh
#
# The craft is nothing but the AA-6 launcher, which is its own command source. Launch it,
# open the KSArmory panel, and use the Test targets buttons to fly drones at it.
#
set -euo pipefail

KSA_USER_DIR="$(find /mnt/c/Users -maxdepth 4 -type d -path '*My Games/Kitten Space Agency' 2>/dev/null | head -1 || true)"

if [[ -z "$KSA_USER_DIR" ]]; then
    echo "error: could not find the KSA user folder" >&2
    exit 1
fi

# KSA looks for "Vehicles"; Windows is case-insensitive so an existing "vehicles" is the same
# directory. Prefer whichever already exists to avoid creating a confusing duplicate.
if [[ -d "$KSA_USER_DIR/vehicles" ]]; then
    VEHICLES="$KSA_USER_DIR/vehicles"
else
    VEHICLES="$KSA_USER_DIR/Vehicles"
fi

NAME="AA Defence Site"
TARGET="$VEHICLES/$NAME"
NOW="$(date +%Y-%m-%dT%H:%M:%S.0000000)"

mkdir -p "$TARGET"

cat > "$TARGET/meta.toml" <<EOF
name = "$NAME"
created = $NOW
updated = $NOW
version = "KSArmory-mod"
systems = [ "Sol", ]
EOF

# Mirrors Content/Core/defaultvehicles/*/vehicle.xml. A single root part with no connections
# is the whole craft -- the launcher declares <Control />, so it needs no command pod.
cat > "$TARGET/vehicle.xml" <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<VehicleSaveData Id="AA Defence Site" ActiveSequence="0">
  <RootPartRef InstanceOf="KSArmory_Prefab_Launcher6" LocalInstanceId="1" Stage="0">
    <Transform>
      <Position X="0" Y="0" Z="0" />
      <Rotation X="0" Y="0" Z="0" />
      <Scale X="1" Y="1" Z="1" />
    </Transform>
  </RootPartRef>
</VehicleSaveData>
EOF

echo "installed '$NAME' to $TARGET"
echo
echo "In game: load it from the vehicle list and launch. No editor work needed."
echo "Then open the KSArmory panel -> Test targets -> Overhead."
