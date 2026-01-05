#!/bin/bash
set -e

APP_NAME="OSFRLauncher"
VERSION=$1

if [ -z "$VERSION" ]; then
    echo "❌ Error: Version number is required."
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PUBLISH_DIR="$SCRIPT_DIR/publish"
APPDIR="$SCRIPT_DIR/AppDir"
RELEASE_DIR="$SCRIPT_DIR/releases"

echo "🐧 OSFR Launcher Linux Build v$VERSION"

# 1. Compile
dotnet publish ./src/Launcher/Launcher.csproj -c Release -r linux-x64 --self-contained -o "$PUBLISH_DIR" /p:Version="$VERSION"

# 2. Setup AppDir
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"
mkdir -p "$APPDIR/usr/share/icons/hicolor/512x512/apps"
cp -r "$PUBLISH_DIR/"* "$APPDIR/usr/bin/"

# 3. Handle Icon: Target high-res layer [4] and upscale to 512px
ICON_PATH="$PUBLISH_DIR/App.ico"
PNG_DEST="$APPDIR/usr/share/icons/hicolor/512x512/apps/osfr-launcher.png"
ROOT_ICON="$APPDIR/osfr-launcher.png"

if [ -f "$ICON_PATH" ]; then
    echo "🎨 Extracting Crystal Clear Icon from layer [4]..."
    if command -v convert >/dev/null 2>&1; then
        # [4] selects the high-quality 256px layer you identified
        convert "$ICON_PATH[4]" -resize 512x512 "$PNG_DEST"
        cp "$PNG_DEST" "$ROOT_ICON"
    else
        echo "⚠️ Warning: ImageMagick not found."
    fi
fi

# 4. Create AppRun and Desktop Entry
cat > "$APPDIR/AppRun" <<EOF
#!/bin/sh
exec "\$(dirname "\$(readlink -f "\$0")")/usr/bin/Launcher" "\$@"
EOF
chmod +x "$APPDIR/AppRun"

cat > "$APPDIR/OSFRLauncher.desktop" <<EOF
[Desktop Entry]
Name=OSFR Launcher
Exec=Launcher
Icon=osfr-launcher
Type=Application
Categories=Game;
EOF

# 5. Build AppImage
echo "🚀 Packaging with appimagetool..."
mkdir -p "$RELEASE_DIR"
if [ ! -f "$SCRIPT_DIR/appimagetool" ]; then
    wget -q https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage -O appimagetool
fi

chmod +x "$SCRIPT_DIR/appimagetool"
"$SCRIPT_DIR/appimagetool" "$APPDIR" "$RELEASE_DIR/$APP_NAME-$VERSION-x86_64.AppImage"

echo "✅ Build Success!"
