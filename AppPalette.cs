using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using Microsoft.Win32;

namespace StartStyleToolbox;

internal static class AppPalette
{
    private const uint UseImmersiveDarkMode = 20;
    private const uint LegacyImmersiveDarkMode = 19;
    private const uint WindowCornerPreference = 33;
    private const uint BorderColor = 34;
    private const uint CaptionColor = 35;
    private const uint TextColor = 36;
    private const uint SystemBackdropType = 38;
    private const int BackdropNone = 1;
    private const int BackdropDesktopAcrylic = 3;
    private const int DwmColorNone = -2;
    private static readonly Color InactiveDarkColor = Color.FromRgb(32, 32, 32);
    private static readonly TimeSpan FocusTransitionDuration = TimeSpan.FromMilliseconds(360);
    private static readonly SineEase FocusEasing = new() { EasingMode = EasingMode.EaseInOut };

    static AppPalette() => FocusEasing.Freeze();

    internal static DoubleAnimation FocusFade(double from, double to) => new(from, to, FocusTransitionDuration)
    {
        EasingFunction = FocusEasing,
        FillBehavior = FillBehavior.Stop,
    };

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

    internal static bool SystemUsesTransparency() => !SystemParameters.HighContrast &&
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "EnableTransparency", 1) is not 0;

    internal static ResourceDictionary Create(bool light, Color systemAccent, Material material, ThemeTint tint,
        bool hasBackdrop, bool systemDarkFallback, ResourceDictionary? transitionFrom = null)
    {
        Color Base(string lightHex, string darkHex) => StyleBrushes.ParseColor(light ? lightHex : darkHex);
        double Tint(double lightAmount, double darkAmount) => light ? lightAmount : darkAmount;
        Color Surface(string lightHex, string darkHex, double lightAmount, double darkAmount) =>
            StyleBrushes.Mix(Base(lightHex, darkHex), systemAccent, Tint(lightAmount, darkAmount));

        var selected = Base("#E5E5E5", "#2D2D2D");
        var accentText = ReadableAccent(systemAccent, selected, light);
        var primary = StyleBrushes.Mix(systemAccent, light ? Colors.Black : Colors.White, Tint(0.12, 0.36));
        var primaryInk = Contrast(primary, Colors.White) >= Contrast(primary, Colors.Black)
            ? Colors.White
            : Colors.Black;

        var baseColor = Base("#F3F3F3", "#161C2D");
        var tintColor = StyleBrushes.ParseColor(tint.Color);
        byte gray = (byte)Math.Round(0.2126 * tintColor.R + 0.7152 * tintColor.G + 0.0722 * tintColor.B);
        var neutral = StyleBrushes.Mix(baseColor, Color.FromRgb(gray, gray, gray), 0.22);
        double tintWeight = tint.Tint / 100.0 * tintColor.A / 255.0;
        double luminosityScale = material switch
        {
            Material.Transparent => 0.25,
            Material.Blur => 0.36,
            _ => 0.55,
        };
        double luminosityWeight = tint.Luminosity / 100.0 * luminosityScale;
        Color CanvasColor(Color foundation)
        {
            double foundationOpacity = material == Material.Blur && hasBackdrop ? (light ? 0.52 : 0.30) : 0;
            double neutralOpacity = luminosityWeight + foundationOpacity * (1 - luminosityWeight);
            double opacity = tintWeight + neutralOpacity * (1 - tintWeight);
            if (!hasBackdrop)
                return StyleBrushes.Mix(StyleBrushes.Mix(foundation, neutral, luminosityWeight), tintColor, tintWeight);

            var blend = StyleBrushes.Mix(neutral, tintColor, opacity == 0 ? 0 : tintWeight / opacity);
            return Alpha(blend, (byte)Math.Round(opacity * 255));
        }

        var surface = Base("#FFFFFF", systemDarkFallback ? "#FFFFFF" : "#D2DFFF");
        var choice = StyleBrushes.Mix(surface, systemAccent, light ? 0.60 : 0.36);
        var dialog = CanvasColor(baseColor);
        var dialogSurface = StyleBrushes.Mix(baseColor, dialog, dialog.A / 255.0);
        var palette = new ResourceDictionary
        {
            ["Canvas"] = hasBackdrop ? Paint(CanvasColor(baseColor)) :
                Gradient(CanvasColor(baseColor), CanvasColor(Base("#F7F9FF", "#101425"))),
            ["InactiveWindowFill"] = Paint(InactiveDarkColor),
            ["Panel"] = systemDarkFallback ? Brushes.Transparent : Paint(Alpha(surface, light ? (byte)36 : (byte)10)),
            ["Card"] = systemDarkFallback ? Paint(Base("#FFFFFF", "#292929")) : Paint(Alpha(surface, light ? (byte)118 : (byte)20)),
            ["Field"] = systemDarkFallback ? Paint(Base("#FFFFFF", "#373737")) : Paint(Alpha(surface, light ? (byte)172 : (byte)32)),
            ["DialogSurface"] = Paint(systemDarkFallback ? selected : dialogSurface),
            ["ChoiceSelected"] = Paint(systemDarkFallback ? selected : Alpha(choice, light ? (byte)90 : (byte)58)),
            ["ChoiceHover"] = Paint(systemDarkFallback ? Base("#ECECEC", "#333333") : Alpha(choice, light ? (byte)60 : (byte)36)),
            ["ChoicePressed"] = Paint(systemDarkFallback ? Base("#DFDFDF", "#3B3B3B") : Alpha(choice, light ? (byte)116 : (byte)80)),
            ["ChoiceLine"] = Paint(Alpha(baseColor, light ? (byte)115 : (byte)95)),
            ["ChoiceSelectedLine"] = Paint(Alpha(accentText, 150)),
            ["RegionSelectionOpacity"] = 1.0,
            ["InvalidInput"] = Paint(Base("#A92B29", "#FFADAA")),
            ["Flyout"] = Paint(systemDarkFallback ? Base("#F9F9F9", "#2B2B2B") : StyleBrushes.Mix(dialogSurface, surface, light ? 0.35 : 0.07)),
            ["ScrollThumb"] = Paint(Base("#737780", systemDarkFallback ? "#A6A6A6" : "#A0AAC0")),
            ["Ink"] = Paint(Base("#1F1F1F", "#F5F5F5")),
            ["Muted"] = Paint(Base("#616161", "#BDBDBD")),
            ["Line"] = Paint(Surface("#D9D9D9", "#3A3A3A", 0.04, 0.0)),
            ["Accent"] = Paint(accentText),
            ["Primary"] = Paint(primary),
            ["PrimaryInk"] = Paint(primaryInk),
            ["HoverLine"] = Paint(StyleBrushes.Mix(accentText, selected, 0.36)),
            ["SliderTrack"] = Paint(Alpha(Base("#1F1F1F", "#FFFFFF"), light ? (byte)65 : (byte)75)),
            ["SliderFill"] = Paint(primary),
            ["PreviewWell"] = Paint(Surface("#E9E9E9", "#202020", 0.03, 0.0)),
        };

        if (transitionFrom is not null)
        {
            // Sample the visible color so reversing a transition does not jump to its old endpoint.
            foreach (var key in new System.Collections.ArrayList(palette.Keys))
            {
                if (key is "Canvas" || palette[key] is not SolidColorBrush target ||
                    transitionFrom[key] is not SolidColorBrush previous || previous.Color == target.Color)
                    continue;

                var brush = new SolidColorBrush(target.Color);
                brush.BeginAnimation(SolidColorBrush.ColorProperty, new FocusBrushFade(previous.Color, target.Color));
                palette[key] = brush;
            }
        }
        return palette;
    }

    internal static bool ApplyWindowAppearance(HwndSource source, bool light, Material material, bool transparency,
        WindowChrome? chrome = null)
    {
        IntPtr handle = source.Handle;
        int dark = light ? 0 : 1;
        var captionColor = light ? Color.FromRgb(243, 243, 243) : Color.FromRgb(31, 31, 31);
        if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, in dark, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(handle, LegacyImmersiveDarkMode, in dark, sizeof(int));
        if (NativeStartMenu.UsesWindows10Layout)
        {
            // The main window and picker supply their own wallpaper-backed material on Windows 10.
            if (source.CompositionTarget is { } legacyTarget)
                legacyTarget.BackgroundColor = captionColor;
            return false;
        }

        int border = DwmColorNone;
        _ = DwmSetWindowAttribute(handle, BorderColor, in border, sizeof(int));
        var textColor = light ? Color.FromRgb(31, 31, 31) : Color.FromRgb(245, 245, 245);
        bool wantsAcrylic = transparency && material == Material.Acrylic;
        int backdrop = wantsAcrylic ? BackdropDesktopAcrylic : BackdropNone;
        bool systemAcrylic = DwmSetWindowAttribute(handle, SystemBackdropType, in backdrop, sizeof(int)) == 0 && wantsAcrylic;
        bool hasBackdrop;
        if (chrome is not null)
        {
            int round = 2;
            _ = DwmSetWindowAttribute(handle, WindowCornerPreference, in round, sizeof(int));
            // Extend glass from the bottom across the client area while leaving the native top margin at zero.
            // A top glass margin can expose the system accent caption over the custom controls.
            chrome.GlassFrameThickness = systemAcrylic ? new Thickness(0, 0, 0, SystemParameters.VirtualScreenHeight) : default;
            hasBackdrop = systemAcrylic;
        }
        else
        {
            int caption = ColorRef(captionColor);
            int text = ColorRef(textColor);
            _ = DwmSetWindowAttribute(handle, CaptionColor, in caption, sizeof(int));
            _ = DwmSetWindowAttribute(handle, TextColor, in text, sizeof(int));
            var margins = systemAcrylic ? new Margins(-1, -1, -1, -1) : default;
            hasBackdrop = DwmExtendFrameIntoClientArea(handle, in margins) == 0 && systemAcrylic;
        }
        if (source.CompositionTarget is { } target)
            target.BackgroundColor = hasBackdrop ? Colors.Transparent : captionColor;
        return hasBackdrop;
    }

    private sealed class FocusBrushFade : ColorAnimationBase
    {
        private readonly Color from;
        private readonly Color to;

        internal FocusBrushFade(Color from, Color to)
        {
            this.from = from;
            this.to = to;
            Duration = FocusTransitionDuration;
            FillBehavior = FillBehavior.Stop;
        }

        protected override Freezable CreateInstanceCore() => new FocusBrushFade(from, to);

        protected override Color GetCurrentValueCore(Color defaultOriginValue, Color defaultDestinationValue, AnimationClock clock)
        {
            double progress = FocusEasing.Ease(clock.CurrentProgress.GetValueOrDefault());
            // Weight each color by its visible contribution to prevent a translucent fill from flashing.
            double fromAlpha = from.A * (1 - progress);
            double toAlpha = to.A * progress;
            double alpha = fromAlpha + toAlpha;
            return alpha == 0 ? Colors.Transparent
                : Alpha(StyleBrushes.Mix(from, to, toAlpha / alpha), (byte)Math.Round(alpha));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Margins(int Left, int Right, int Top, int Bottom);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, uint attribute, in int value, uint size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr window, in Margins margins);

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

    private static LinearGradientBrush Gradient(Color first, Color second)
    {
        var brush = new LinearGradientBrush(first, second, 28);
        brush.Freeze();
        return brush;
    }

    private static int ColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    private static Color Alpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
}
