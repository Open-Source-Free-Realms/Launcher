#!/bin/bash
set -e # Stop on any error

# ==========================================
# CONFIGURATION
# ==========================================
APP_NAME="OSFR Launcher"
APP_BUNDLE_NAME="$APP_NAME.app"
REAL_EXECUTABLE="Launcher"
WRAPPER_NAME="launcher.sh" 

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PUBLISH_DIR="$SCRIPT_DIR/publish"
RELEASE_DIR="$SCRIPT_DIR/releases"

# ==========================================
# 1. INPUT VALIDATION & SETUP
# ==========================================
if [ "$#" -ne 1 ]; then
    echo "❌ Error: Version number is required."
    echo "Usage: ./MacBuild.sh [version]"
    exit 1
fi
BUILD_VERSION="$1"

# Detect Mac Architecture
ARCH=$(uname -m)
if [[ "$ARCH" == "arm64" ]]; then
    RID="osx-arm64"
else
    RID="osx-x64"
fi

echo "🍏 OSFR Launcher macOS Build Tool"
echo "🔹 Target: $RID"
echo "🔹 Version: $BUILD_VERSION"

# ==========================================
# 2. COMPILE (.NET PUBLISH)
# ==========================================
echo ""
echo "🔨 Compiling .NET binaries..."
rm -rf "$PUBLISH_DIR"

# -------------------------------------------------------------------------
# CRITICAL FIX: We added /p:Version="$BUILD_VERSION" to stamp the DLLs
# -------------------------------------------------------------------------
dotnet publish ./src/Launcher/Launcher.csproj \
    -c Release \
    --self-contained \
    -r "$RID" \
    -o "$PUBLISH_DIR" \
    /p:Version="$BUILD_VERSION"

if [ ! -f "$PUBLISH_DIR/$REAL_EXECUTABLE" ]; then
    echo "❌ Error: Compilation failed. $REAL_EXECUTABLE not found."
    exit 1
fi

# ==========================================
# 3. CREATE APP BUNDLE
# ==========================================
echo ""
echo "📦 Assembling App Bundle..."

mkdir -p "$RELEASE_DIR"
APP_BUNDLE_PATH="$RELEASE_DIR/$APP_BUNDLE_NAME"
rm -rf "$APP_BUNDLE_PATH"

MACOS_DIR="$APP_BUNDLE_PATH/Contents/MacOS"
RESOURCES_DIR="$APP_BUNDLE_PATH/Contents/Resources"
mkdir -p "$MACOS_DIR"
mkdir -p "$RESOURCES_DIR"

cp -R "$PUBLISH_DIR/"* "$MACOS_DIR/"

# ==========================================
# 4. CREATE WRAPPER SHIM
# ==========================================
echo "   Creating directory fix wrapper..."
cat > "$MACOS_DIR/$WRAPPER_NAME" <<EOF
#!/bin/bash
DIR="\$(cd "\$(dirname "\$0")" && pwd)"
cd "\$DIR"
exec "./$REAL_EXECUTABLE" "\$@"
EOF

chmod +x "$MACOS_DIR/$WRAPPER_NAME"
chmod +x "$MACOS_DIR/$REAL_EXECUTABLE"

# ==========================================
# 5. CREATE INFO.PLIST
# ==========================================
echo "   Generating Info.plist..."
cat > "$APP_BUNDLE_PATH/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundleDisplayName</key>
    <string>$APP_NAME</string>
    <key>CFBundleIdentifier</key>
    <string>com.osfr.launcher</string>
    <key>CFBundleVersion</key>
    <string>$BUILD_VERSION</string>
    <key>CFBundleShortVersionString</key>
    <string>$BUILD_VERSION</string>
    <key>CFBundleExecutable</key>
    <string>$WRAPPER_NAME</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
EOF

# ==========================================
# 6. ICON PROCESSING
# ==========================================
if [ -f "$PUBLISH_DIR/App.ico" ]; then
    echo "   Converting App.ico to AppIcon.icns..."
    sips -s format icns "$PUBLISH_DIR/App.ico" --out "$RESOURCES_DIR/AppIcon.icns" >/dev/null 2>&1
    /usr/libexec/PlistBuddy -c "Add :CFBundleIconFile string AppIcon" "$APP_BUNDLE_PATH/Contents/Info.plist" || true
else
    echo "⚠️ Warning: App.ico not found in publish dir. App will use generic icon."
fi

# ==========================================
# 7. CLEANUP & SIGNING
# ==========================================
rm -f "$MACOS_DIR/$REAL_EXECUTABLE.exe"
rm -f "$MACOS_DIR/$REAL_EXECUTABLE.pdb"

echo "   Signing & De-quarantining..."
xattr -dr com.apple.quarantine "$APP_BUNDLE_PATH" || true
codesign --deep --force --sign - "$APP_BUNDLE_PATH"

# ==========================================
# 8. VELOPACK PACKAGING
# ==========================================
echo ""
echo "🚀 Packaging Release with Velopack..."

vpk pack \
    --packTitle "$APP_NAME" \
    --packAuthors "OSFR Team" \
    --packId "OSFRLauncher" \
    --mainExe "$WRAPPER_NAME" \
    --packDir "$APP_BUNDLE_PATH" \
    --packVersion "$BUILD_VERSION" \
    --outputDir "$RELEASE_DIR"

echo ""
echo "✅ Build Success!"
echo "   Installer Location: $RELEASE_DIR"