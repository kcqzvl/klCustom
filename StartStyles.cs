using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace StartStyleToolbox;

internal enum Material { Original, Solid, Transparent, Acrylic, Blur, Mica }

internal sealed record StyleOptions(
    Material Material = Material.Original,
    string Color = "#CC2341CD",
    int Tint = 45,
    int Luminosity = 50,
    int Blur = 28,
    int Opacity = 100,
    bool ShareMainBackdrop = false)
{
    internal static readonly StyleOptions MistyBlue = new(Material.Acrylic);
}

internal sealed record Region(string Id, string Label, string Icon)
{
    internal static readonly Region[] All =
    [
        new("main", "主面板", "\uE80F"),
        new("pinned", "固定", "\uE718"),
        new("recommended", "推荐", "\uE734"),
        new("apps", "所有应用", "\uE71D"),
        new("search", "搜索框", "\uE721"),
        new("footer", "账户和电源栏", "\uE7E8"),
        new("companion", "右侧伴侣面板", "\uE8EA"),
    ];

    internal static Region Get(string id) => All.FirstOrDefault(item => item.Id == id)
        ?? throw new InvalidOperationException("无法识别区域。");
}

internal sealed class StyleBrushes(Color[] wallpaper)
{
    internal static string MaterialLabel(Material material) => material switch
    {
        Material.Original => "保持原样",
        Material.Solid => "纯色",
        Material.Transparent => "透明",
        Material.Acrylic => "亚克力",
        Material.Blur => "模糊",
        Material.Mica => "云母拟态",
        _ => throw new ArgumentOutOfRangeException(nameof(material)),
    };

    internal static bool TryColor(string? text, out Color color)
    {
        color = default;
        if (text is null || !text.StartsWith('#') || text.Length is not (7 or 9)
            || !uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var argb))
            return false;

        byte alpha = text.Length == 7 ? (byte)255 : (byte)(argb >> 24);
        color = Color.FromArgb(alpha, (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        return true;
    }

    internal static Color ParseColor(string text) => TryColor(text, out var color)
        ? color
        : throw new InvalidOperationException("颜色格式应为 #AARRGGBB 或 #RRGGBB。");

    internal static string Hex(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    internal static string FormatOpacity(byte alpha) =>
        (alpha * 100m / byte.MaxValue).ToString("0.##", CultureInfo.InvariantCulture);

    internal static bool TryParseOpacity(string text, out byte alpha)
    {
        alpha = 0;
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var percent)
            || percent is < 0 or > 100)
            return false;

        alpha = (byte)Math.Round(percent * byte.MaxValue / 100m, MidpointRounding.AwayFromZero);
        return true;
    }

    internal static void Validate(StyleOptions options)
    {
        _ = ParseColor(options.Color);
        if (!Enum.IsDefined(options.Material) || options.Tint is < 0 or > 100
            || options.Luminosity is < 0 or > 100 || options.Blur is < 0 or > 64
            || options.Opacity is < 0 or > 100)
            throw new InvalidOperationException("材质参数超出范围。");
    }

    internal Brush Preview(StyleOptions options, Color backdrop, bool panel)
    {
        var color = ParseColor(options.Color);
        switch (options.Material)
        {
            case Material.Transparent:
                return Brushes.Transparent;
            case Material.Solid:
                return new SolidColorBrush(color);
            case Material.Mica:
                var gradient = new LinearGradientBrush { Opacity = options.Opacity / 100.0 };
                var stops = MicaColors(options);
                for (int index = 0; index < stops.Length; index++)
                    gradient.GradientStops.Add(new GradientStop(stops[index], index / 2.0));
                return gradient;
            case Material.Original:
                return panel
                    ? new LinearGradientBrush(Color.FromArgb(235, 37, 56, 126), Color.FromArgb(240, 23, 35, 76), 56)
                    : new SolidColorBrush(Color.FromArgb(84, 18, 29, 65));
            case Material.Acrylic:
            case Material.Blur:
                double tint = options.Tint / 100.0 * color.A / 255.0;
                double diffusion = options.Material == Material.Acrylic
                    ? options.Luminosity / 100.0
                    : options.Blur / 64.0 * 0.3;
                var middle = Mix(backdrop, color, tint + (1 - tint) * diffusion * 0.42);
                var peak = Mix(middle, Colors.White, 0.1);
                var edge = Mix(middle, Color.FromRgb(20, 27, 49), 0.14);
                return new LinearGradientBrush(peak, edge, 57) { Opacity = 0.7 + 0.3 * diffusion };
            default:
                throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    internal Color[] MicaColors(StyleOptions options)
    {
        var tint = ParseColor(options.Color);
        return wallpaper.Select(color => Mix(color, tint, options.Tint / 100.0 * tint.A / 255.0)).ToArray();
    }

    internal static Color Mix(Color background, Color tint, double amount)
    {
        byte Channel(byte first, byte second) => (byte)Math.Round(first + (second - first) * amount);
        return Color.FromRgb(Channel(background.R, tint.R), Channel(background.G, tint.G), Channel(background.B, tint.B));
    }

}

internal static class WallpaperPalette
{
    internal static string? DesktopPath() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "WallPaper", null) as string;

    internal static BitmapImage? LoadImage(string? path, int decodeWidth)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            using var file = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = file;
            image.DecodePixelWidth = decodeWidth;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    internal static Color[] Read()
    {
        int systemTheme = (int?)Registry.GetValue(
            @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "SystemUsesLightTheme", 0) ?? 0;
        var neutral = systemTheme == 0 ? Color.FromRgb(33, 37, 45) : Color.FromRgb(238, 242, 248);
        try
        {
            var image = LoadImage(DesktopPath(), 48);
            if (image is null)
                return FromAccent(neutral);
            var source = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int stride = source.PixelWidth * 4;
            var pixels = new byte[stride * source.PixelHeight];
            source.CopyPixels(pixels, stride, 0);

            var samples = new (long R, long G, long B, int Count)[3];
            for (int y = 0; y < source.PixelHeight; y++)
            for (int x = 0; x < source.PixelWidth; x++)
            {
                int band = Math.Min(2, (int)((x / (double)source.PixelWidth + y / (double)source.PixelHeight) * 1.5));
                int offset = y * stride + x * 4;
                ref var sample = ref samples[band];
                sample.R += pixels[offset + 2];
                sample.G += pixels[offset + 1];
                sample.B += pixels[offset];
                sample.Count++;
            }

            return samples.Select(sample => sample.Count == 0 ? neutral : StyleBrushes.Mix(neutral,
                Color.FromRgb((byte)(sample.R / sample.Count), (byte)(sample.G / sample.Count),
                    (byte)(sample.B / sample.Count)), 0.38)).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return FromAccent(neutral);
        }
    }

    private static Color[] FromAccent(Color neutral)
    {
        var accent = SystemParameters.WindowGlassColor;
        return [StyleBrushes.Mix(neutral, accent, 0.27), StyleBrushes.Mix(neutral, accent, 0.16),
            StyleBrushes.Mix(neutral, accent, 0.06)];
    }
}
