using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StartStyleToolbox;

internal sealed record ThemeTint(string Color, int Tint = 45, int Luminosity = 45, int BlurRadius = 28)
{
    internal static readonly ThemeTint LightDefault = new("#CCEEF4FF");
    internal static readonly ThemeTint DarkDefault = new("#CC2341CD");

    internal ThemeTint Normalize(ThemeTint defaults) => new(
        StyleBrushes.TryColor(Color, out var color) ? StyleBrushes.Hex(color) : defaults.Color,
        Math.Clamp(Tint, 0, 100), Math.Clamp(Luminosity, 0, 100), Math.Clamp(BlurRadius, 0, 64));
}

internal sealed class Profile
{
    public Dictionary<string, StyleOptions> Regions { get; set; } = new(StringComparer.Ordinal);
    public bool MainEnabled { get; set; }
    public string ThemeMode { get; set; } = "System";
    public Material ThemeMaterial { get; set; } = Material.Acrylic;
    public ThemeTint LightThemeTint { get; set; } = ThemeTint.LightDefault;
    public ThemeTint DarkThemeTint { get; set; } = ThemeTint.DarkDefault;
    public string Language { get; set; } = "zh-CN";

    private static string ProfilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StartStyleToolbox", "profile.json");

    internal static Profile Load()
    {
        try
        {
            if (!File.Exists(ProfilePath))
                return new();
            var profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(ProfilePath)) ?? new();
            profile.Regions = Region.All.Where(region => profile.Regions?.ContainsKey(region.Id) == true)
                .ToDictionary(region => region.Id, region => profile.Regions![region.Id]);
            foreach (var options in profile.Regions.Values)
                StyleBrushes.Validate(options);
            profile.ThemeMode = profile.ThemeMode is "System" or "Light" or "Dark" ? profile.ThemeMode : "System";
            profile.ThemeMaterial = profile.ThemeMaterial is Material.Transparent or Material.Blur or Material.Acrylic
                ? profile.ThemeMaterial : Material.Acrylic;
            profile.LightThemeTint = profile.LightThemeTint?.Normalize(ThemeTint.LightDefault) ?? ThemeTint.LightDefault;
            profile.DarkThemeTint = profile.DarkThemeTint?.Normalize(ThemeTint.DarkDefault) ?? ThemeTint.DarkDefault;
            profile.Language = Localization.Normalize(profile.Language);
            return profile;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
            or InvalidOperationException or ArgumentException)
        {
            return new();
        }
    }

    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
        string temporary = ProfilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, ProfilePath, overwrite: true);
    }
}
