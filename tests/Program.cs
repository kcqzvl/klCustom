using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media;
using StartStyleToolbox;

var brushes = new StyleBrushes([Colors.Gray, Colors.Black, Colors.White]);
var bytes = new byte[Region.All.Length * 36];
var defaults = new Profile();
Require(defaults is { MainEnabled: false, ThemeMode: "System", ThemeMaterial: Material.Acrylic },
    "new profiles follow the system and start with main background management disabled");
foreach (var region in Region.All)
{
    var draft = defaults.Regions.GetValueOrDefault(region.Id, StyleOptions.DefaultDraft);
    Require(draft is { Color: "#FF0066FF", Tint: 50, Luminosity: 50, TransparentTint: 50, TransparentLuminosity: 50 },
        $"default customization values for {region.Id}");
}
Require(defaults.Regions["main"].Material == Material.Acrylic, "main panel opens with an acrylic draft");
foreach (var theme in new[] { defaults.LightThemeTint, defaults.DarkThemeTint })
{
    Require(theme is { Color: "#FF0066FF", Tint: 45, Luminosity: 45, Transparency: 80 },
        "default theme tint and transparency");
    Require(theme.EffectiveColor() == Color.FromArgb(51, 0, 102, 255), "theme transparency affects the rendered color");
    var saved = JsonSerializer.Deserialize<ThemeTint>(JsonSerializer.Serialize(theme))!.Normalize();
    Require(saved == theme, "theme HEX and transparency survive saving and reloading");
}
for (int alpha = 0; alpha <= byte.MaxValue; alpha++)
{
    var legacy = JsonSerializer.Deserialize<ThemeTint>($$"""{"Color":"#{{alpha:X2}}2341CD"}""")!;
    var migrated = legacy.Normalize();
    Require(migrated.Color == "#FF2341CD" && migrated.EffectiveColor() == StyleBrushes.ParseColor(legacy.Color),
        "theme transparency migration preserves the rendered color");
}
Require(NativeStartMenu.SupportsHostVersion(19044, new(10, 0, 19041, 1)), "Windows 10 LTSC host family");
Require(NativeStartMenu.SupportsHostVersion(19045, new(10, 0, 19041, 7850)), "Windows 10 22H2 host family");
Require(NativeStartMenu.SupportsHostVersion(22631, new(10, 0, 22621, 1)), "Windows 11 23H2 host family");
Require(NativeStartMenu.SupportsHostVersion(26100, new(10, 0, 26100, 1)), "Windows 11 24H2 host family");
Require(NativeStartMenu.SupportsHostVersion(26200, new(10, 0, 26100, 9818)), "Windows 11 25H2 host family");
Require(NativeStartMenu.SupportsHostVersion(26300, new(10, 0, 26100, 9818)), "Windows 11 26H2 host family");
Require(NativeStartMenu.SupportsHostVersion(26300, new(6, 2, 26100, 9818)), "Windows 11 26H2 legacy host version prefix");
Require(!NativeStartMenu.SupportsHostVersion(26300, new(6, 2, 9200, 0)), "legacy Windows build is not a Windows 11 host");
Require(!NativeStartMenu.SupportsHostVersion(19045, new(6, 2, 26100, 9818)), "legacy prefix retains host family validation");
Require(!NativeStartMenu.SupportsHostVersion(26301, new(10, 0, 26100, 9818)), "unknown Windows build");
Require(!NativeStartMenu.SupportsHostVersion(19045, new(10, 0, 26100, 9818)), "foreign host family");
var packageVersion = NativeStartMenu.ParseHostPackageVersion(
    "Microsoft.Windows.StartMenuExperienceHost_10.0.19041.1_neutral__cw5n1h2txyewy");
Require(packageVersion is not null && NativeStartMenu.SupportsHostVersion(19045, packageVersion),
    "Windows 10 22H2 falls back to the host package's 19041 build");
Require(packageVersion is not null && !NativeStartMenu.SupportsHostVersion(26100, packageVersion),
    "fallback package version still requires a compatible build");
Require(NativeStartMenu.ParseHostPackageVersion(
    "Microsoft.Windows.StartMenuExperienceHost_10.0.19041.1_neutral__other") is null, "different package publisher");
Require(NativeStartMenu.ParseHostPackageVersion(
    "Microsoft.Windows.ShellExperienceHost_10.0.19041.1_neutral__cw5n1h2txyewy") is null, "different system package");
Require(NativeStartMenu.ParseHostPackageVersion(
    "Microsoft.Windows.StartMenuExperienceHost_0.0.0.0_neutral__cw5n1h2txyewy") is null, "missing package version");
var systemHost = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
    @"SystemApps\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\StartMenuExperienceHost.exe");
Require(NativeStartMenu.IsSystemHostPath(systemHost.ToLowerInvariant()), "system host path casing");
Require(NativeStartMenu.IsSystemHostPath(@"\\?\" + systemHost), "extended system host path");
Require(!NativeStartMenu.IsSystemHostPath(Path.Combine(Path.GetTempPath(), Path.GetFileName(systemHost))),
    "same-name executable outside the system package");
Require(Region.All.Where(region => NativeStartMenu.HasRegion(region.Id, true)).Select(region => region.Id)
    .SequenceEqual(new[] { "main", "pinned", "apps", "footer" }), "Windows 10 menu regions");
Require(Region.All.All(region => NativeStartMenu.HasRegion(region.Id, false)), "Windows 11 menu regions");
Require(Region.All.Select(region => region.Id).SequenceEqual(
    new[] { "main", "pinned", "recommended", "apps", "search", "footer", "companion" }), "region order");
foreach (var material in Enum.GetValues<Material>())
{
    for (int index = 0; index < Region.All.Length; index++)
    {
        var options = new StyleOptions(material, "#CC2341CD", 12, 34, 56, 78);
        StyleBrushes.Validate(options);
        var slot = bytes.AsSpan(index * 36, 36);
        slot.Clear();
        NativeStartMenu.WriteStyle(slot, options, brushes);
        var values = MemoryMarshal.Cast<byte, uint>(slot);
        Require(values[..6].SequenceEqual(new uint[] { (uint)material, 0xCC2341CD, 12, 34, 56, 78 }),
            "wire parameters");
        Require(material == Material.Mica
            ? values[6..].ToArray().All(color => (color >> 24) == 255)
            : values[6..].SequenceEqual(new uint[] { 0, 0, 0 }), "mica palette");
    }
}

Require(StyleBrushes.TryColor("#CC2341CD", out var preset) && preset.R == 35 && preset.A == 204, "preset color");
Require(StyleBrushes.FormatOpacity(204) == "80" && StyleBrushes.FormatOpacity(255) == "100", "opacity display");
Require(StyleBrushes.TryParseOpacity("50", out var half) && half == 128, "opacity midpoint");
Require(!StyleBrushes.TryParseOpacity("100.01", out _), "invalid opacity");
for (int value = 0; value <= byte.MaxValue; value++)
{
    string opacity = StyleBrushes.FormatOpacity((byte)value);
    Require(StyleBrushes.TryParseOpacity(opacity, out var alpha) && alpha == value, "alpha round trip");
}
Require(CustomizationConflicts.MaskForSelector("Grid#MainMenu > Windows.UI.Xaml.Controls.Border#AcrylicBorder")
    == 1, "main conflict");
Require(CustomizationConflicts.MaskForSelector("Grid#CompanionRoot > Border#AcrylicBorder")
    == 64, "companion conflict");
Require(CustomizationConflicts.MaskForSelector("Border#AcrylicBorder") == 65, "shared border selector");
Require(CustomizationConflicts.MaskForSelector("StartMenu.PinnedList#StartMenuPinnedList > Grid#Root")
    == 2, "pinned conflict");
var transparent = new StyleOptions(Material.Transparent, "#CC2341CD");
var clearPreview = (SolidColorBrush)brushes.Preview(transparent, Colors.White, true);
Require(clearPreview.Color.A == 0, "existing transparent settings remain clear");
var tinted = transparent with { TransparentTint = 50 };
var mixed = tinted with { TransparentLuminosity = 40 };
var tintPreview = (SolidColorBrush)brushes.Preview(tinted, Colors.White, true);
var mixedPreview = (SolidColorBrush)brushes.Preview(mixed, Colors.White, true);
Require(tintPreview.Color.A == 102 && tintPreview.Color.B == 205, "transparent tint uses color and alpha");
Require(mixedPreview.Color.A > tintPreview.Color.A && mixedPreview.Color.B < tintPreview.Color.B,
    "luminosity adds a neutral layer to the transparent tint");
NativeStartMenu.WriteStyle(bytes.AsSpan(0, 36), mixed, brushes);
var mixedValues = MemoryMarshal.Cast<byte, uint>(bytes.AsSpan(0, 36));
Require(mixedValues[0] == (uint)Material.Solid &&
    mixedValues[1] == Convert.ToUInt32(StyleBrushes.Hex(mixedPreview.Color)[1..], 16),
    "transparent tint uses the same alpha-blended color in preview and the installed host");
NativeStartMenu.WriteStyle(bytes.AsSpan(0, 36), mixed with { Color = "#002341CD" }, brushes);
Require(mixedValues[0] == (uint)Material.Transparent, "zero color alpha remains fully transparent");

var shared = new StyleOptions(Material.Acrylic, ShareMainBackdrop: true);
NativeStartMenu.WriteStyle(bytes.AsSpan(0, 36), shared, brushes);
var sharedValues = MemoryMarshal.Cast<byte, uint>(bytes.AsSpan(0, 36));
Require(sharedValues[6] == 1, "shared acrylic backdrop");
NativeStartMenu.WriteStyle(bytes.AsSpan(0, 36), shared with { ShareMainBackdrop = false }, brushes);
Require(sharedValues[6] == 0, "independent acrylic backdrop replaces shared flag");
var acrylic = new StyleOptions(Material.Acrylic, "#CC2341CD");
var applied = bytes.AsSpan(0, 36);
NativeStartMenu.WriteStyle(applied, acrylic, brushes);
Require(NativeStartMenu.MatchesAppliedStyle(applied, acrylic, brushes), "unchanged acrylic does not need applying");
Require(!NativeStartMenu.MatchesAppliedStyle(applied, acrylic with { Tint = 51 }, brushes), "changed tint needs applying");
Require(NativeStartMenu.MatchesAppliedStyle(applied, acrylic with { Color = "#cc2341cd", Blur = 20 }, brushes),
    "equivalent color and unused settings do not mark acrylic as changed");
Require(!NativeStartMenu.MatchesAppliedStyle(applied, shared, brushes), "legacy shared acrylic can be replaced");
Require(!NativeStartMenu.MatchesAppliedStyle(applied, new(), brushes), "switching to the system style can be applied");
NativeStartMenu.WriteStyle(applied, mixed, brushes);
Require(NativeStartMenu.MatchesAppliedStyle(applied, mixed, brushes), "unchanged tinted transparency matches the host");
Require(!NativeStartMenu.MatchesAppliedStyle(applied, mixed with { TransparentLuminosity = 0 }, brushes),
    "changing the rendered transparent tint can be applied");
Console.WriteLine("Passed Windows build and host checks, native regions and materials, shared backdrop, and alpha round trips.");

static void Require(bool condition, string check)
{
    if (!condition)
        throw new InvalidOperationException(check);
}
