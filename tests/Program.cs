using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media;
using StartStyleToolbox;

var brushes = new StyleBrushes([Colors.Gray, Colors.Black, Colors.White]);
var bytes = new byte[Region.All.Length * 36];
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
Console.WriteLine("Passed native region/material, shared backdrop, and alpha round-trip checks.");

static void Require(bool condition, string check)
{
    if (!condition)
        throw new InvalidOperationException(check);
}
