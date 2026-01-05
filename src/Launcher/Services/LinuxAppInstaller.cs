using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Launcher.Services;

public static class LinuxSetup
{
    public static void CheckAndInstall()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;

        // 1. Detect if running as an AppImage
        string? appImageSrc = Environment.GetEnvironmentVariable("APPIMAGE");
        if (string.IsNullOrEmpty(appImageSrc)) return;

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string appDir = Path.Combine(home, "Applications");
        string targetPath = Path.Combine(appDir, "OSFRLauncher.AppImage");

        bool exists = File.Exists(targetPath);
        bool needsUpdate = exists && !string.Equals(appImageSrc, targetPath, StringComparison.OrdinalIgnoreCase)
        && File.GetLastWriteTimeUtc(appImageSrc) > File.GetLastWriteTimeUtc(targetPath);

        // Skip if already in Applications and no update is found
        if (string.Equals(appImageSrc, targetPath, StringComparison.OrdinalIgnoreCase)) return;

        // If already installed but user ran an old download, switch to the installed version
        if (exists && !needsUpdate)
        {
            Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = true });
            Environment.Exit(0);
            return;
        }

        // 2. VRCX-Style Install/Update Prompt using Zenity
        string promptTitle = "OSFR Launcher";
        string promptMessage = needsUpdate ? "A newer version of the Launcher is available. Update now?"
        : "Do you want to install OSFR Launcher? It will be moved to your ~/Applications folder.";

        if (!ShowZenityPrompt(promptTitle, promptMessage)) return;

        try
        {
            if (!Directory.Exists(appDir)) Directory.CreateDirectory(appDir);

            // 3. Move/Copy the file
            File.Copy(appImageSrc, targetPath, true);
            File.SetUnixFileMode(targetPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            // 4. Install high-res icon and desktop shortcut
            InstallDesktopIntegration(targetPath);

            // 5. CLEANUP: Delete the original downloaded file
            try { File.Delete(appImageSrc); } catch { }

            // 6. Relaunch from ~/Applications
            Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = true });
            Environment.Exit(0);
        }
        catch (Exception ex) { Console.WriteLine($"[LinuxSetup] Error: {ex.Message}"); }
    }

    public static void Uninstall()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;

        // 1. Confirm Uninstall
        if (!ShowZenityPrompt("Uninstall OSFR Launcher", "Are you sure you want to remove the App and Shortcuts?")) return;

        // 2. Ask about Data (True/Yes = Delete, False/No = Keep)
        bool deleteData = ShowZenityPrompt("Delete Game Data?",
                                           "Do you also want to delete the Game Data folder (~500MB)?\n\n(Select 'No' to keep your downloaded game files)");

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string appPath = Path.Combine(home, "Applications", "OSFRLauncher.AppImage");
        string desktopFile = Path.Combine(home, ".local/share/applications", "osfr-launcher.desktop");
        string gameData = Path.Combine(home, ".local/share/OSFRLauncher");
        string iconDir = Path.Combine(home, ".local/share/icons/hicolor");

        string scriptPath = "/tmp/osfr_uninstall.sh";

        // FIX: Replaced raw string literal with standard string builder to prevent CS8999
        StringBuilder sb = new StringBuilder();
        sb.Append("#!/bin/bash\n");
        sb.Append("sleep 2\n");
        sb.Append($"rm -f \"{appPath}\"\n");
        sb.Append($"rm -f \"{desktopFile}\"\n");
        sb.Append($"find \"{iconDir}\" -name \"osfr-launcher.png\" -type f -delete\n");
        if (deleteData)
        {
            sb.Append($"rm -rf \"{gameData}\"\n");
        }
        sb.Append($"update-desktop-database \"{home}/.local/share/applications\"\n");
        sb.Append($"gtk-update-icon-cache -f -t \"{iconDir}\"\n");
        sb.Append("if command -v zenity >/dev/null 2>&1; then\n");
        sb.Append("    zenity --info --text=\"OSFR Launcher has been removed.\" --width=300\n");
        sb.Append("fi\n");
        sb.Append("rm -- \"$0\"\n");

        string scriptContent = sb.ToString();

        try
        {
            File.WriteAllText(scriptPath, scriptContent);
            Process.Start("chmod", $"+x {scriptPath}");
            Process.Start(new ProcessStartInfo("/bin/bash", scriptPath) { UseShellExecute = false });
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Uninstall Error: {ex.Message}");
        }
    }

    private static bool ShowZenityPrompt(string title, string text)
    {
        try
        {
            var psi = new ProcessStartInfo("zenity")
            {
                Arguments = $"--question --title=\"{title}\" --text=\"{text}\" --width=400",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit();
            return process?.ExitCode == 0; // 0 means 'Yes' clicked
        }
        catch { return true; } // Default to install if Zenity is missing
    }

    private static void InstallDesktopIntegration(string appImagePath)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Use the 512x512 High-DPI folder for a sharp dock icon
        string iconDestDir = Path.Combine(home, ".local/share/icons/hicolor/512x512/apps");
        string iconDestPath = Path.Combine(iconDestDir, "osfr-launcher.png");

        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // Matches the high-res path generated in build.sh
            string bundledIconPath = Path.Combine(baseDir, "../share/icons/hicolor/512x512/apps/osfr-launcher.png");

            if (File.Exists(bundledIconPath))
            {
                if (!Directory.Exists(iconDestDir)) Directory.CreateDirectory(iconDestDir);
                File.Copy(bundledIconPath, iconDestPath, true);

                // Refresh icon cache immediately so it isn't blurry
                try
                {
                    Process.Start(new ProcessStartInfo("gtk-update-icon-cache",
                                                       $"-f -t \"{Path.Combine(home, ".local/share/icons/hicolor")}\"")
                    { UseShellExecute = false, CreateNoWindow = true });
                }
                catch { }
            }
        }
        catch { }

        string desktopFileDir = Path.Combine(home, ".local/share/applications");
        string desktopFilePath = Path.Combine(desktopFileDir, "osfr-launcher.desktop");

        // FIX: Replaced raw string literal with standard string builder to prevent CS8999
        StringBuilder desktopSb = new StringBuilder();
        desktopSb.Append("[Desktop Entry]\n");
        desktopSb.Append("Name=OSFR Launcher\n");
        desktopSb.Append($"Exec=\"{appImagePath}\"\n");
        desktopSb.Append("Icon=osfr-launcher\n");
        desktopSb.Append("Type=Application\n");
        desktopSb.Append("Categories=Game;\n");
        desktopSb.Append("Terminal=false\n");

        string desktopContent = desktopSb.ToString();

        if (!Directory.Exists(desktopFileDir)) Directory.CreateDirectory(desktopFileDir);
        File.WriteAllText(desktopFilePath, desktopContent);
        try { Process.Start(new ProcessStartInfo("update-desktop-database", desktopFileDir)); } catch { }
    }
}
