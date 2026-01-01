#!/bin/bash
set -e # Stop on any error

# ==========================================
# CONFIGURATION
# ==========================================
APP_NAME="OSFR Launcher"
APP_BUNDLE_NAME="$APP_NAME.app"
# The binary name output by .NET (usually matches project name)
REAL_EXECUTABLE="Launcher"
# The wrapper script name that fixes the working directory
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

# Detect Mac Architecture (Apple Silicon vs Intel)
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

# Note: We use the specific .csproj path to avoid confusion
dotnet publish ./src/Launcher/Launcher.csproj \
    -c Release \
    --self-contained \
    -r "$RID" \
    -o "$PUBLISH_DIR"

if [ ! -f "$PUBLISH_DIR/$REAL_EXECUTABLE" ]; then
    echo "❌ Error: Compilation failed. $REAL_EXECUTABLE not found."
    exit 1
fi

# ==========================================
# 3. CREATE APP BUNDLE
# ==========================================
echo ""
echo "📦 Assembling App Bundle..."

# Ensure releases directory exists
mkdir -p "$RELEASE_DIR"

# Define the final App Bundle path inside releases
APP_BUNDLE_PATH="$RELEASE_DIR/$APP_BUNDLE_NAME"

# Clean any existing bundle in releases
rm -rf "$APP_BUNDLE_PATH"

# Create standard macOS Bundle structure
MACOS_DIR="$APP_BUNDLE_PATH/Contents/MacOS"
RESOURCES_DIR="$APP_BUNDLE_PATH/Contents/Resources"
mkdir -p "$MACOS_DIR"
mkdir -p "$RESOURCES_DIR"

# Copy compiled files into the Bundle
cp -R "$PUBLISH_DIR/"* "$MACOS_DIR/"

# ==========================================
# 4. CREATE WRAPPER SHIM
# ==========================================
# This script ensures the app can find its configuration files
echo "   Creating directory fix wrapper..."
cat > "$MACOS_DIR/$WRAPPER_NAME" <<EOF
#!/bin/bash
# Get the directory where this script resides
DIR="\$(cd "\$(dirname "\$0")" && pwd)"
# Switch context to that directory so .NET can find its files
cd "\$DIR"
# Run the real app, passing any arguments (like deep links)
exec "./$REAL_EXECUTABLE" "\$@"
EOF

# Make executable
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
    # 'sips' is built-in to macOS
    sips -s format icns "$PUBLISH_DIR/App.ico" --out "$RESOURCES_DIR/AppIcon.icns" >/dev/null 2>&1
    
    # Register the icon in the plist
    /usr/libexec/PlistBuddy -c "Add :CFBundleIconFile string AppIcon" "$APP_BUNDLE_PATH/Contents/Info.plist" || true
else
    echo "⚠️ Warning: App.ico not found in publish dir. App will use generic icon."
fi

# ==========================================
# 7. CLEANUP & SIGNING
# ==========================================
# Remove Windows artifacts if present
rm -f "$MACOS_DIR/$REAL_EXECUTABLE.exe"
rm -f "$MACOS_DIR/$REAL_EXECUTABLE.pdb"

echo "   Signing & De-quarantining..."
# Remove 'downloaded from internet' attribute
xattr -dr com.apple.quarantine "$APP_BUNDLE_PATH" || true
# Ad-hoc sign the bundle (Required for ARM64 Macs)
codesign --deep --force --sign - "$APP_BUNDLE_PATH"

# ==========================================
# 8. VELOPACK PACKAGING
# ==========================================
echo ""
echo "🚀 Packaging Release with Velopack..."

# We point Velopack specifically to the .app bundle we just created
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
echo "   App Bundle: $APP_BUNDLE_PATH"
echo "   Installer:  $RELEASE_DIR"