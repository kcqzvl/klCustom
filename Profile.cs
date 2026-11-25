using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;

namespace StartStyleToolbox;

internal sealed record ThemeTint(string Color, int Tint = 45, int Luminosity = 45, int BlurRadius = 28,
    double? Transparency = null)
{
    internal static readonly ThemeTint Default = new(StyleOptions.DefaultColor, Transparency: 80);

    internal double ColorTransparency => Transparency ?? 0;

    internal Color EffectiveColor()
    {
        var color = StyleBrushes.ParseColor(Color);
        color.A = (byte)Math.Round(color.A * (100 - ColorTransparency) / 100, MidpointRounding.AwayFromZero);
        return color;
    }

    internal ThemeTint Normalize()
    {
        bool validColor = StyleBrushes.TryColor(Color, out var color);
        double transparency = Transparency ?? (validColor ? (255 - color.A) * 100.0 / 255 : Default.ColorTransparency);
        if (validColor && Transparency is null)
            color.A = 255;

        return new(validColor ? StyleBrushes.Hex(color) : Default.Color,
            Math.Clamp(Tint, 0, 100), Math.Clamp(Luminosity, 0, 100), Math.Clamp(BlurRadius, 0, 64),
            Math.Clamp(transparency, 0, 100));
    }
}

internal sealed class Profile
{
    public Dictionary<string, StyleOptions> Regions { get; set; } = new(StringComparer.Ordinal)
    {
        ["main"] = StyleOptions.DefaultDraft with { Material = Material.Acrylic },
    };
    public bool MainEnabled { get; set; }
    public string ThemeMode { get; set; } = "System";
    public Material ThemeMaterial { get; set; } = Material.Acrylic;
    public ThemeTint LightThemeTint { get; set; } = ThemeTint.Default;
    public ThemeTint DarkThemeTint { get; set; } = ThemeTint.Default;
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
                .ToDictionary(region => region.Id, region => profile.Regions![region.Id] with { ShareMainBackdrop = false });
            foreach (var options in profile.Regions.Values)
                StyleBrushes.Validate(options);
            profile.ThemeMode = profile.ThemeMode is "System" or "Light" or "Dark" ? profile.ThemeMode : "System";
            profile.ThemeMaterial = profile.ThemeMaterial is Material.Transparent or Material.Blur or Material.Acrylic
                ? profile.ThemeMaterial : Material.Acrylic;
            profile.LightThemeTint = profile.LightThemeTint?.Normalize() ?? ThemeTint.Default;
            profile.DarkThemeTint = profile.DarkThemeTint?.Normalize() ?? ThemeTint.Default;
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
