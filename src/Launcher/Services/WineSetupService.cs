using Launcher.Helpers;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Launcher.Services;

public static class WineSetupService
{
    private const string EngineUrl = "https://github.com/Gcenx/winecx/releases/download/crossover-wine-23.7.1-1/wine-crossover-23.7.1-1-osx64.tar.xz";
    private const string DgVoodooUrl = "https://github.com/dege-diosg/dgVoodoo2/releases/download/v2.86.4/dgVoodoo2_86_4.zip";

    public static string WineRootPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "OSFRLauncher", "WineSystem");
    public static string EngineBin => Path.Combine(WineRootPath, "Wine Crossover.app", "Contents", "Resources", "wine", "bin", "wine64");
    public static string PrefixPath => Path.Combine(WineRootPath, "Prefix");

    public static bool IsInstalled() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? File.Exists(EngineBin) && Directory.Exists(PrefixPath) : true;

    public static async Task Install(IProgress<string> status, IProgress<double> progress)
    {
        if (!Directory.Exists(WineRootPath)) Directory.CreateDirectory(WineRootPath);
        if (string.IsNullOrEmpty(EngineBin) || !File.Exists(EngineBin))
        {
            status.Report("Downloading Mac Game Engine...");
            string archivePath = Path.Combine(WineRootPath, "engine.tar.xz");
            await DownloadFileAsync(EngineUrl, archivePath, progress);
            status.Report("Installing Engine...");
            await ExtractNative(archivePath, WineRootPath);
            if (File.Exists(archivePath)) File.Delete(archivePath);
        }

        if (!Directory.Exists(PrefixPath))
        {
            status.Report("Building Wine Bottle...");
            await RunWineCommand("wineboot", "-u");
            status.Report("Applying Performance Fixes...");
            
            // Cleaned Up Overrides: We only disable HTML/Mono popups.
            // D3D9 override is removed so Wine uses its own stable renderer.
            await RunWineCommand("reg", "add HKCU\\Software\\Wine\\DllOverrides /v mshtml /t REG_SZ /d \"\" /f");
            await RunWineCommand("reg", "add HKCU\\Software\\Wine\\DllOverrides /v mscoree /t REG_SZ /d \"\" /f");
        }

        try { await DownloadFileAsync(DgVoodooUrl, Path.Combine(WineRootPath, "dgvoodoo.zip"), null); } catch { }
        status.Report("Setup Complete!");
    }

    public static void EnsureDgVoodoo(string gameDirectory)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
        string zipPath = Path.Combine(WineRootPath, "dgvoodoo.zip");
        if (!File.Exists(zipPath)) return;
        try
        {
            string temp = Path.Combine(WineRootPath, "dg_temp");
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
            ZipFile.ExtractToDirectory(zipPath, temp);
            string x86Src = Path.Combine(temp, "MS", "x86");
            string[] dlls = { "D3D9.dll", "D3DImm.dll", "DDraw.dll", "D3D8.dll" };
            foreach (var dll in dlls) { string src = Path.Combine(x86Src, dll); if (File.Exists(src)) File.Copy(src, Path.Combine(gameDirectory, dll), true); }
            string cpl = Path.Combine(temp, "dgVoodooCpl.exe");
            if (File.Exists(cpl)) File.Copy(cpl, Path.Combine(gameDirectory, "dgVoodooCpl.exe"), true);
            Directory.Delete(temp, true);
        } catch { }
    }

    public static async Task RunWineCommand(string cmd, string args)
    {
        if (string.IsNullOrEmpty(EngineBin)) return;
        var psi = new ProcessStartInfo(EngineBin) { UseShellExecute = false, CreateNoWindow = true };
        psi.Arguments = $"{cmd} {args}";
        psi.EnvironmentVariables["WINEPREFIX"] = PrefixPath;
        psi.EnvironmentVariables["WINEDEBUG"] = "-all";
        string wineBase = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(EngineBin))) ?? string.Empty;
        if (Directory.Exists(Path.Combine(wineBase, "lib"))) psi.EnvironmentVariables["DYLD_LIBRARY_PATH"] = Path.Combine(wineBase, "lib");
        using var p = Process.Start(psi);
        if (p != null) await p.WaitForExitAsync();
    }

    private static async Task DownloadFileAsync(string url, string dest, IProgress<double>? progress)
    {
        using var client = new HttpClient();
        using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1L;
        using var s = await resp.Content.ReadAsStreamAsync();
        using var f = new FileStream(dest, FileMode.Create);
        var buf = new byte[8192];
        long readTotal = 0; int read;
        while ((read = await s.ReadAsync(buf)) > 0) { await f.WriteAsync(buf, 0, read); readTotal += read; if (total != -1 && progress != null) progress.Report((double)readTotal / total * 100); }
    }

    private static async Task ExtractNative(string archivePath, string outputDir)
    {
        var psi = new ProcessStartInfo("tar") { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("-xf"); psi.ArgumentList.Add(archivePath); psi.ArgumentList.Add("-C"); psi.ArgumentList.Add(outputDir);
        using var p = Process.Start(psi);
        if (p != null) await p.WaitForExitAsync();
    }
}