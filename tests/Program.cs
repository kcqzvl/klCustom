using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media;
using StartStyleToolbox;

var brushes = new StyleBrushes([Colors.Gray, Colors.Black, Colors.White]);
var bytes = new byte[Region.All.Length * 36];
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
var shared = new StyleOptions(Material.Acrylic, ShareMainBackdrop: true);
NativeStartMenu.WriteStyle(bytes.AsSpan(0, 36), shared, brushes);
var sharedValues = MemoryMarshal.Cast<byte, uint>(bytes.AsSpan(0, 36));
Require(sharedValues[6] == 1, "shared acrylic backdrop");
NativeStartMenu.WriteStyle(bytes.AsSpan(0, 36), shared with { ShareMainBackdrop = false }, brushes);
Require(sharedValues[6] == 0, "independent acrylic backdrop replaces shared flag");
Console.WriteLine("Passed Windows build and host checks, native regions and materials, shared backdrop, and alpha round trips.");

static void Require(bool condition, string check)
{
    if (!condition)
        throw new InvalidOperationException(check);
}
