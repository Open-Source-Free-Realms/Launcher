using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Helpers;
using Launcher.Models;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System;

namespace Launcher.ViewModels;

public partial class GraphicsSettings : ObservableObject
{
    private readonly Server _server;
    private readonly string _iniPath;
    private Dictionary<string, Dictionary<string, string>> _sections = new();

    // Bindings
    [ObservableProperty] private string windowMode = "Fullscreen";
    [ObservableProperty] private string resolution = "1920x1080";
    [ObservableProperty] private bool renderFlora;
    [ObservableProperty] private double renderDistance;
    [ObservableProperty] private double brightness;
    [ObservableProperty] private double graphicQuality;
    [ObservableProperty] private double textureQuality;
    [ObservableProperty] private double shadowQuality;
    [ObservableProperty] private bool useAutoDetect;

    public Action? CloseAction { get; set; }

    // List for the dropdowns
    public List<string> ModeOptions { get; } = new() { "Fullscreen", "Windowed" };
    public List<string> ResolutionOptions { get; } = new() 
    { "1024x768", "1152x864", "1280x1024", "1440x900", "1536x960", "1600x900", "1680x1050", "1920x1080" };

    public GraphicsSettings(Server server)
    {
        _server = server;
        _iniPath = Path.Combine(Constants.SavePath, _server.Info.SavePath, "Client", "UserOptions.ini");
        LoadSettings();
    }

    private void LoadSettings()
    {
        if (!File.Exists(_iniPath)) return;
        var lines = File.ReadAllLines(_iniPath);
        string? currentSection = null;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]")) {
                currentSection = trimmed.Substring(1, trimmed.Length - 2);
                if (!_sections.ContainsKey(currentSection)) _sections[currentSection] = new();
            } else if (trimmed.Contains("=") && currentSection != null) {
                var parts = trimmed.Split('=', 2);
                _sections[currentSection][parts[0].Trim()] = parts[1].Trim();
            }
        }

        // Logic from PDF
        WindowMode = GetVal("Display", "Mode", "Fullscreen");
        if (!ModeOptions.Contains(WindowMode)) WindowMode = "Fullscreen"; // Safety fallback

        string w = WindowMode == "Fullscreen" ? GetVal("Display", "FullscreenWidth", "1920") : GetVal("Display", "WindowedWidth", "1440");
        string h = WindowMode == "Fullscreen" ? GetVal("Display", "FullscreenHeight", "1080") : GetVal("Display", "WindowedHeight", "900");
        Resolution = $"{w}x{h}";

        UseAutoDetect = !HasVal("Rendering", "GraphicsQuality");
        RenderFlora = GetVal("Terrain", "RenderFlora", "Off") == "On";
        
        double rd = ParseDouble(GetVal("Rendering", "RenderDistance", "-1.000000"));
        RenderDistance = rd < 0 ? 100 : ((rd - 120) / (99999 - 120)) * 100;
        Brightness = (ParseDouble(GetVal("Rendering", "Gamma", "0.000000")) + 1) * 50;

        GraphicQuality = ParseDouble(GetVal("Rendering", "GraphicsQuality", "3"));
        ShadowQuality = ParseDouble(GetVal("Rendering", "ShadowQuality", "2"));
        TextureQuality = 2 - ParseDouble(GetVal("Rendering", "TextureQuality", "0"));
    }

    partial void OnWindowModeChanged(string value)
    {
        string w = value == "Fullscreen" ? GetVal("Display", "FullscreenWidth", "1920") : GetVal("Display", "WindowedWidth", "1440");
        string h = value == "Fullscreen" ? GetVal("Display", "FullscreenHeight", "1080") : GetVal("Display", "WindowedHeight", "900");
        Resolution = $"{w}x{h}";
    }

    [RelayCommand]
    public void SaveSettings()
    {
        var resParts = Resolution.Split('x');
        if (WindowMode == "Fullscreen") {
            SetVal("Display", "FullscreenWidth", resParts[0]); SetVal("Display", "FullscreenHeight", resParts[1]);
        } else {
            SetVal("Display", "WindowedWidth", resParts[0]); SetVal("Display", "WindowedHeight", resParts[1]);
        }
        SetVal("Display", "Mode", WindowMode);

        if (UseAutoDetect) {
            RemoveVal("Rendering", "GraphicsQuality"); RemoveVal("Rendering", "ShadowQuality"); RemoveVal("Rendering", "TextureQuality");
        } else {
            SetVal("Rendering", "GraphicsQuality", ((int)GraphicQuality).ToString());
            SetVal("Rendering", "ShadowQuality", ((int)ShadowQuality).ToString());
            SetVal("Rendering", "TextureQuality", ((int)(2 - TextureQuality)).ToString());
        }

        double finalRD = RenderDistance >= 99 ? -1.0 : 120 + (RenderDistance / 100) * (99999 - 120);
        SetVal("Rendering", "RenderDistance", finalRD.ToString("F6", CultureInfo.InvariantCulture));
        SetVal("Rendering", "Gamma", ((Brightness / 50) - 1).ToString("F6", CultureInfo.InvariantCulture));
        SetVal("Terrain", "RenderFlora", RenderFlora ? "On" : "Off");

        var output = new List<string>();
        foreach (var section in _sections) {
            output.Add($"[{section.Key}]");
            foreach (var kvp in section.Value) output.Add($"{kvp.Key}={kvp.Value}");
            output.Add(""); 
        }
        File.WriteAllLines(_iniPath, output);
        CloseAction?.Invoke();
    }

    private string GetVal(string s, string k, string def) => _sections.TryGetValue(s, out var sec) && sec.TryGetValue(k, out var v) ? v : def;
    private void SetVal(string s, string k, string v) { if (!_sections.ContainsKey(s)) _sections[s] = new(); _sections[s][k] = v; }
    private bool HasVal(string s, string k) => _sections.TryGetValue(s, out var sec) && sec.ContainsKey(k);
    private void RemoveVal(string s, string k) { if (_sections.TryGetValue(s, out var sec)) sec.Remove(k); }
    private double ParseDouble(string s) => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
}