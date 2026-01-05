#!/bin/bash

APP_NAME="osfr-launcher"
INSTALL_PATH="$HOME/Applications/OSFRLauncher.AppImage"
DESKTOP_FILE="$HOME/.local/share/applications/$APP_NAME.desktop"
ICON_DIR="$HOME/.local/share/icons/hicolor"
GAME_DATA_DIR="$HOME/.local/share/OSFRLauncher"

# Helper for Zenity or Terminal prompts
ask_question() {
    if command -v zenity >/dev/null 2>&1; then
        zenity --question --title="$1" --text="$2" --width=400
        return $?
    else
        read -p "$2 (y/N): " confirm
        [[ $confirm =~ ^[Yy]$ ]] && return 0 || return 1
    fi
}

# 1. Main Confirmation
if ! ask_question "Uninstall OSFR Launcher" "Are you sure you want to remove the OSFR Launcher app and shortcuts?"; then
    exit 0
fi

# 2. Optional Data Cleanup
DELETE_DATA=0
if ask_question "Delete Game Data?" "Do you also want to delete the Game Data folder (~500MB)?\n\nLocation: $GAME_DATA_DIR\n(Select 'No' to keep your downloaded game files)"; then
    DELETE_DATA=1
fi

echo "🗑️  Uninstalling..."

# Remove App & Shortcuts
rm -f "$INSTALL_PATH"
rm -f "$DESKTOP_FILE"
find "$ICON_DIR" -name "$APP_NAME.png" -type f -delete

# Remove Data if requested
if [ $DELETE_DATA -eq 1 ]; then
    rm -rf "$GAME_DATA_DIR"
    echo "✅ Game data removed."
else
    echo "ℹ️  Game data kept at $GAME_DATA_DIR"
fi

# Refresh System Caches
update-desktop-database "$HOME/.local/share/applications" >/dev/null 2>&1
gtk-update-icon-cache -f -t "$HOME/.local/share/icons/hicolor" >/dev/null 2>&1

if command -v zenity >/dev/null 2>&1; then
    zenity --info --text="Uninstallation Complete." --width=300
else
    echo "✅ Done."
fi
