using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace StartStyleToolbox;

internal partial class ColorPickerWindow : Window
{
    private readonly TextBox[] rgb;
    private readonly TextBox[] hsv;
    private readonly TextBox opacity;
    private readonly TextBox hex;
    private bool updating = true;
    private double hue;
    private double saturation;
    private double value;

    internal Color SelectedColor { get; private set; }

    internal ColorPickerWindow(Window owner, Color original, Func<string, string> text)
    {
        InitializeComponent();
        Owner = owner;
        foreach (var controlType in new[] { typeof(Button), typeof(TextBox) })
            Resources[controlType] = owner.FindResource(controlType);

        Title = text("pickerTitle");
        OriginalLabel.Text = text("pickerOriginal");
        NewLabel.Text = text("pickerNew");
        ConfirmButton.Content = text("pickerConfirm");
        CancelButton.Content = text("pickerCancel");
        System.Windows.Automation.AutomationProperties.SetName(ColorPlane, text("pickerPlane"));
        System.Windows.Automation.AutomationProperties.SetName(HuePlane, text("pickerHue"));
        OriginalGrid.Background = NewGrid.Background = (Brush)owner.FindResource("AlphaGrid");
        OriginalColor.Background = new SolidColorBrush(original);

        rgb = [AddField(text("pickerRed"), 3), AddField(text("pickerGreen"), 3), AddField(text("pickerBlue"), 3)];
        Divider();
        hsv = [AddField(text("pickerHue"), 6, "°"), AddField(text("pickerSaturation"), 6, "%"),
            AddField(text("pickerValue"), 6, "%")];
        Divider();
        opacity = AddField(text("pickerAlpha"), 6, "%");
        hex = AddField("HEX", 9, width: 108);

        SetColor(original);
        RenderFields();
    }

    internal void SetBackdrop(bool acrylic, ImageSource? wallpaper, int radius, ResourceDictionary palette)
    {
        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(palette);
        WallpaperLayer.Source = wallpaper;
        WallpaperBlur.Radius = radius;
        Backdrop.SetResourceReference(Border.BackgroundProperty, acrylic || wallpaper is not null ? "Canvas" : "DialogSurface");
    }

    private TextBox AddField(string title, int maxLength, string unit = "", double width = 86)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var suffix = new TextBlock { Text = unit, Width = 18, TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center };
        suffix.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var input = new TextBox { Width = width, Height = 32, MaxLength = maxLength,
            FontFamily = new FontFamily("Consolas"), VerticalContentAlignment = VerticalAlignment.Center };
        input.TextChanged += FieldChanged;
        input.LostFocus += (_, _) => RenderFields();
        System.Windows.Automation.AutomationProperties.SetLabeledBy(input, label);
        DockPanel.SetDock(suffix, Dock.Right);
        DockPanel.SetDock(input, Dock.Right);
        row.Children.Add(suffix);
        row.Children.Add(input);
        row.Children.Add(label);
        ColorFields.Children.Add(row);
        return input;
    }

    private void Divider()
    {
        var line = new Border { Height = 1, Margin = new Thickness(0, 2, 0, 6) };
        line.SetResourceReference(Border.BackgroundProperty, "ChoiceLine");
        ColorFields.Children.Add(line);
    }

    private void FieldChanged(object sender, TextChangedEventArgs args)
    {
        if (updating || sender is not TextBox input)
            return;

        if (!TryApplyField(input))
        {
            input.SetResourceReference(Control.BorderBrushProperty, "InvalidInput");
            ConfirmButton.IsEnabled = false;
            return;
        }

        RenderFields(input);
    }

    private bool TryApplyField(TextBox input)
    {
        if (input == hex)
        {
            if (!StyleBrushes.TryColor(input.Text.StartsWith('#') ? input.Text : "#" + input.Text, out var color))
                return false;
            SetColor(color);
            return true;
        }

        if (input == opacity)
        {
            if (!StyleBrushes.TryParseOpacity(input.Text, out byte alpha))
                return false;
            SelectedColor = Color.FromArgb(alpha, SelectedColor.R, SelectedColor.G, SelectedColor.B);
            return true;
        }

        int channel = Array.IndexOf(rgb, input);
        if (channel >= 0)
        {
            if (!byte.TryParse(input.Text, NumberStyles.None, CultureInfo.InvariantCulture, out byte number))
                return false;
            var color = SelectedColor;
            if (channel == 0) color.R = number;
            else if (channel == 1) color.G = number;
            else color.B = number;
            SetColor(color);
            return true;
        }

        int component = Array.IndexOf(hsv, input);
        double maximum = component == 0 ? 360 : 100;
        if (!double.TryParse(input.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double entry)
            || entry < 0 || entry > maximum)
            return false;
        if (component == 0) hue = entry;
        else if (component == 1) saturation = entry / 100;
        else value = entry / 100;
        SelectedColor = FromHsv(hue, saturation, value, SelectedColor.A);
        return true;
    }

    private void SetColor(Color color)
    {
        SelectedColor = color;
        double red = color.R / 255.0, green = color.G / 255.0, blue = color.B / 255.0;
        double max = Math.Max(red, Math.Max(green, blue));
        double delta = max - Math.Min(red, Math.Min(green, blue));
        value = max;
        if (max > 0)
            saturation = delta / max;
        if (delta == 0)
            return;
        double sector = max == red ? (green - blue) / delta
            : max == green ? 2 + (blue - red) / delta : 4 + (red - green) / delta;
        hue = (sector * 60 + 360) % 360;
    }

    private static Color FromHsv(double hue, double saturation, double value, byte alpha)
    {
        double sector = hue % 360 / 60;
        double chroma = value * saturation;
        double intermediate = chroma * (1 - Math.Abs(sector % 2 - 1));
        double floor = value - chroma;
        var (red, green, blue) = (int)sector switch
        {
            0 => (chroma, intermediate, 0.0), 1 => (intermediate, chroma, 0.0),
            2 => (0.0, chroma, intermediate), 3 => (0.0, intermediate, chroma),
            4 => (intermediate, 0.0, chroma), _ => (chroma, 0.0, intermediate),
        };
        byte Byte(double component) => (byte)Math.Round((component + floor) * 255, MidpointRounding.AwayFromZero);
        return Color.FromArgb(alpha, Byte(red), Byte(green), Byte(blue));
    }

    private void RenderFields(TextBox? source = null)
    {
        updating = true;
        Write(rgb[0], SelectedColor.R.ToString(CultureInfo.InvariantCulture));
        Write(rgb[1], SelectedColor.G.ToString(CultureInfo.InvariantCulture));
        Write(rgb[2], SelectedColor.B.ToString(CultureInfo.InvariantCulture));
        Write(hsv[0], Format(hue));
        Write(hsv[1], Format(saturation * 100));
        Write(hsv[2], Format(value * 100));
        Write(opacity, StyleBrushes.FormatOpacity(SelectedColor.A));
        Write(hex, StyleBrushes.Hex(SelectedColor));
        ConfirmButton.IsEnabled = true;
        HueFill.Background = new SolidColorBrush(FromHsv(hue, 1, 1, 255));
        NewColor.Background = new SolidColorBrush(SelectedColor);
        UpdateMarkers();
        updating = false;

        static string Format(double number) => number.ToString("0.##", CultureInfo.InvariantCulture);
        void Write(TextBox input, string text)
        {
            input.ClearValue(Control.BorderBrushProperty);
            if (input != source)
                input.Text = text;
        }
    }

    private void UpdateMarkers()
    {
        Canvas.SetLeft(ColorMarker, saturation * ColorPlane.ActualWidth - ColorMarker.Width / 2);
        Canvas.SetTop(ColorMarker, (1 - value) * ColorPlane.ActualHeight - ColorMarker.Height / 2);
        Canvas.SetTop(HueMarker, (1 - hue / 360) * HuePlane.ActualHeight - HueMarker.Height / 2);
    }

    private void Plane_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateMarkers();

    private void Plane_Pressed(object sender, MouseButtonEventArgs args)
    {
        var plane = (FrameworkElement)sender;
        plane.Focus();
        plane.CaptureMouse();
        ChoosePoint(plane, args.GetPosition(plane));
        args.Handled = true;
    }

    private void Plane_Moved(object sender, MouseEventArgs args)
    {
        var plane = (FrameworkElement)sender;
        if (plane.IsMouseCaptured && args.LeftButton == MouseButtonState.Pressed)
            ChoosePoint(plane, args.GetPosition(plane));
    }

    private void Plane_Released(object sender, MouseButtonEventArgs args) => ((UIElement)sender).ReleaseMouseCapture();

    private void ChoosePoint(FrameworkElement plane, Point point)
    {
        double vertical = 1 - Math.Clamp(point.Y / plane.ActualHeight, 0, 1);
        if (plane == HuePlane)
            hue = vertical * 360;
        else
        {
            saturation = Math.Clamp(point.X / plane.ActualWidth, 0, 1);
            value = vertical;
        }
        ApplyHsv();
    }

    private void Plane_KeyDown(object sender, KeyEventArgs args)
    {
        if (sender == HuePlane && args.Key is Key.Up or Key.Down)
            hue = (hue + (args.Key == Key.Up ? 1 : -1) + 360) % 360;
        else if (sender == ColorPlane && args.Key is Key.Left or Key.Right)
            saturation = Math.Clamp(saturation + (args.Key == Key.Right ? 0.01 : -0.01), 0, 1);
        else if (sender == ColorPlane && args.Key is Key.Up or Key.Down)
            value = Math.Clamp(value + (args.Key == Key.Up ? 0.01 : -0.01), 0, 1);
        else
            return;

        ApplyHsv();
        args.Handled = true;
    }

    private void ApplyHsv()
    {
        SelectedColor = FromHsv(hue, saturation, value, SelectedColor.A);
        RenderFields();
    }

    private void Confirm_Click(object sender, RoutedEventArgs args) => DialogResult = true;
}
