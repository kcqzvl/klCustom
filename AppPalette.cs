using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace StartStyleToolbox;

internal static class AppPalette
{
    private const uint UseImmersiveDarkMode = 20;

    internal static bool SystemUsesLightApps() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1) is not 0;

    internal static Color SystemAccent()
    {
        if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null) is int abgr)
            return Color.FromRgb((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16));

        var glass = SystemParameters.WindowGlassColor;
        return Color.FromRgb(glass.R, glass.G, glass.B);
    }

    internal static ResourceDictionary Create(bool light, Color systemAccent)
    {
        Color Base(string lightHex, string darkHex) => StyleBrushes.ParseColor(light ? lightHex : darkHex);
        double Tint(double lightAmount, double darkAmount) => light ? lightAmount : darkAmount;
        Color Surface(string lightHex, string darkHex, double lightAmount, double darkAmount) =>
            StyleBrushes.Mix(Base(lightHex, darkHex), systemAccent, Tint(lightAmount, darkAmount));

        var selected = Surface("#F5F5F5", "#212121", 0.15, 0.37);
        var accentText = ReadableAccent(systemAccent, selected, light);
        var primary = StyleBrushes.Mix(systemAccent, light ? Colors.Black : Colors.White, Tint(0.12, 0.36));
        var primaryInk = Contrast(primary, Colors.White) >= Contrast(primary, Colors.Black)
            ? Colors.White
            : Colors.Black;

        return new ResourceDictionary
        {
            ["Canvas"] = Paint(Surface("#F8F8F8", "#121212", 0.025, 0.04)),
            ["Panel"] = Paint(Surface("#F1F1F1", "#191919", 0.05, 0.09)),
            ["Field"] = Paint(Surface("#FDFDFD", "#242424", 0.018, 0.12)),
            ["Ink"] = Paint(Base("#222428", "#F0F2F5")),
            ["Muted"] = Paint(Base("#555D69", "#B0B7C1")),
            ["Line"] = Paint(Surface("#D7D7D7", "#383838", 0.13, 0.16)),
            ["Accent"] = Paint(accentText),
            ["Primary"] = Paint(primary),
            ["PrimaryInk"] = Paint(primaryInk),
            ["Selected"] = Paint(selected),
            ["SelectedLine"] = Paint(Surface("#E0E0E0", "#393939", 0.37, 0.45)),
            ["Hover"] = Paint(Surface("#ECECEC", "#262626", 0.08, 0.25)),
            ["HoverLine"] = Paint(StyleBrushes.Mix(accentText, selected, 0.36)),
            ["SliderTrack"] = Paint(Surface("#D0D0D0", "#474747", 0.18, 0.28)),
            ["SliderFill"] = Paint(primary),
            ["PreviewWell"] = Paint(Surface("#EDEDED", "#161616", 0.08, 0.12)),
        };
    }

    internal static void ApplyTitleBar(IntPtr handle, bool light)
    {
        int dark = light ? 0 : 1;
        _ = DwmSetWindowAttribute(handle, UseImmersiveDarkMode, in dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, uint attribute, in int value, uint size);

    private static Color ReadableAccent(Color accent, Color background, bool light)
    {
        var limit = light ? Colors.Black : Colors.White;
        double initialShade = light ? 0.15 : 0.43;
        var foreground = StyleBrushes.Mix(accent, limit, initialShade);
        for (int step = 1; Contrast(foreground, background) < 4.5 && step <= 20; step++)
            foreground = StyleBrushes.Mix(accent, limit, initialShade + (1 - initialShade) * (step / 20.0));
        return foreground;
    }

    private static double Contrast(Color first, Color second)
    {
        double firstLuminance = Luminance(first);
        double secondLuminance = Luminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05)
            / (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte value)
        {
            double srgb = value / 255.0;
            return srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private static SolidColorBrush Paint(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
