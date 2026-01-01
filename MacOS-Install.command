#!/bin/bash

# 1. Configuration
ZIP_URL="https://github.com/suxhi-space/OSFR-Mac-Launcher/releases/latest/download/OSFR.Launcher.zip"
INSTALL_DIR="/Applications"
APP_NAME="OSFR Launcher.app"

echo "------------------------------------------"
echo "   OSFR Launcher Mac Installer"
echo "------------------------------------------"

# 2. Download the latest release
echo "🔹 Downloading latest release..."
curl -L "$ZIP_URL" -o "/tmp/OSFR_Launcher.zip"

# 3. Clean up old versions
if [ -d "$INSTALL_DIR/$APP_NAME" ]; then
    echo "🔹 Removing old version..."
    rm -rf "$INSTALL_DIR/$APP_NAME"
fi

# 4. Unzip to Applications folder
echo "🔹 Installing to Applications folder..."
unzip -q "/tmp/OSFR_Launcher.zip" -d "$INSTALL_DIR"

# 5. Fix Permissions (Removes the 'Damaged' warning)
echo "🔹 Fixing security permissions..."
# -c removes all attributes, -r is recursive
xattr -cr "$INSTALL_DIR/$APP_NAME"
chmod +x "$INSTALL_DIR/$APP_NAME/Contents/MacOS/launcher.sh"
chmod +x "$INSTALL_DIR/$APP_NAME/Contents/MacOS/Launcher"

# 6. Cleanup
rm "/tmp/OSFR_Launcher.zip"

echo "------------------------------------------"
echo "✅ Installation Complete!"
echo "You can now open $APP_NAME from your Applications folder."
echo "------------------------------------------"
read -p "Press [Enter] to close this window..."