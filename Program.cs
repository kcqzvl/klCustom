using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StartStyleToolbox;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application();
        if (args.Length is not (2 or 3) || args[0] != "--render")
            return app.Run(new MainWindow());

        bool? light = args.Length == 3 ? args[2] switch
        {
            "light" => true,
            "dark" => false,
            _ => throw new ArgumentException("Expected light or dark.", nameof(args)),
        } : null;
        var profile = new Profile();
        profile.Regions["companion"] = StyleOptions.MistyBlue;
        var window = new MainWindow(profile, persist: false, previewLightMode: light);
        window.ShowActivated = false;
        window.ContentRendered += (_, _) =>
        {
            var content = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),
                (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(args[1]);
            encoder.Save(output);
            window.Close();
        };
        return app.Run(window);
    }
}
