using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace StartStyleToolbox;

internal sealed class StartPreview : Border
{
    private static readonly Color[] TileColors =
    [
        Color.FromRgb(73, 158, 210), Color.FromRgb(94, 175, 150), Color.FromRgb(238, 183, 105),
        Color.FromRgb(131, 112, 198), Color.FromRgb(213, 105, 103), Color.FromRgb(82, 132, 224),
    ];
    private static readonly Brush Text = new SolidColorBrush(Color.FromRgb(248, 250, 255));
    private static readonly Brush SoftText = new SolidColorBrush(Color.FromRgb(210, 221, 243));
    private static readonly Brush Hairline = new SolidColorBrush(Color.FromArgb(52, 255, 255, 255));
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private readonly StyleBrushes brushes;
    private readonly Func<Region, StyleOptions> options;
    private readonly Color backdrop;
    private readonly string selected;

    internal StartPreview(StyleBrushes brushes, bool light, Func<Region, StyleOptions> options, string selected, bool windows10)
    {
        this.brushes = brushes;
        this.options = options;
        this.selected = selected;
        backdrop = light ? Color.FromRgb(171, 173, 189) : Color.FromRgb(47, 55, 70);
        Width = 600;
        Height = 504;
        CornerRadius = new CornerRadius(7);
        ClipToBounds = true;
        var scene = new Canvas { ClipToBounds = true };
        Child = scene;
        DrawDesktop(scene, light, BackdropBlur());
        if (windows10)
        {
            Place(scene, Windows10Panel(), 22, 22);
        }
        else
        {
            Place(scene, MainPanel(), 22, 22);
            Place(scene, CompanionPanel(), 420, 22);
        }
    }

    private double BackdropBlur()
    {
        var style = options(Region.Get(selected));
        return style.Material switch
        {
            Material.Blur => style.Blur * 0.35,
            Material.Acrylic => 10,
            _ => 0,
        };
    }

    private static void DrawDesktop(Canvas scene, bool light, double blur)
    {
        var page = new Canvas { Width = 600, Height = 504, Background = Paint(light ? "#E5E8EC" : "#292F3B") };
        if (blur > 0)
            page.Effect = new BlurEffect { Radius = blur, RenderingBias = RenderingBias.Performance };
        scene.Background = page.Background;
        Place(scene, page, 0, 0);
        Place(page, Card(600, 35, light ? "#3A4050" : "#1E2430", 0), 0, 0);
        Place(page, Card(471, 16, light ? "#5D6678" : "#38435A", 4), 75, 9);
        Place(page, Card(545, 104, light ? "#ACBFCC" : "#3D5164", 6), 29, 54);
        Place(page, Card(204, 104, light ? "#C49C7C" : "#646052", 6), 370, 54);
        Place(page, Card(259, 109, light ? "#C7CED9" : "#353C50", 6), 29, 185);
        Place(page, Card(263, 109, light ? "#9CABA2" : "#42605C", 6), 311, 185);
        Place(page, Card(545, 105, light ? "#BBC7D0" : "#39465B", 6), 29, 327);
        for (int row = 0; row < 3; row++)
            Place(page, Card(396 - row * 42, 5, light ? "#A3ADBA" : "#5A6779", 2), 32, 451 + row * 12);
    }

    private Border Windows10Panel()
    {
        var body = new Canvas { ClipToBounds = true };
        var rail = new Canvas();
        Place(rail, Label("\uE700", 15, Text), 17, 14);
        Place(rail, Label("\uE77B", 15, Text), 17, 322);
        Place(rail, Label("\uE713", 15, Text), 17, 365);
        Place(rail, Label("\uE7E8", 15, Text), 17, 408);
        foreach (TextBlock icon in rail.Children)
            icon.FontFamily = IconFont;
        Place(body, Surface("footer", 50, 448, rail), 5, 5);

        var apps = new Canvas();
        string[] names = ["Edge", "文件资源管理器", "画图", "计算器", "设置", "照片", "邮件", "日历"];
        for (int index = 0; index < names.Length; index++)
        {
            double top = 16 + index * 37;
            Place(apps, Tile(index, 22), 10, top);
            Place(apps, Label(names[index], 12, Text), 43, top + 3);
        }
        Place(body, Surface("apps", 172, 448, apps), 61, 5);

        var tiles = new Canvas();
        Place(tiles, Label("固定磁贴", 13, Text, true), 12, 16);
        for (int index = 0; index < 9; index++)
            Place(tiles, Tile(index, 80), 12 + index % 3 * 89, 47 + index / 3 * 89);
        Place(body, Surface("pinned", 302, 448, tiles), 241, 5);
        return Surface("main", 556, 460, body, true);
    }

    private Border MainPanel()
    {
        var body = new Canvas { ClipToBounds = true };
        var panel = Surface("main", 392, 460, body, true);
        var search = new DockPanel { Margin = new Thickness(12, 0, 8, 0), LastChildFill = true };
        var icon = Label("\uE721", 13, SoftText);
        icon.FontFamily = IconFont;
        icon.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(icon, Dock.Left);
        search.Children.Add(icon);
        var searchText = Label("搜索应用、设置和文档", 11, SoftText);
        searchText.Margin = new Thickness(8, 0, 0, 0);
        searchText.VerticalAlignment = VerticalAlignment.Center;
        search.Children.Add(searchText);
        Place(body, Surface("search", 354, 29, search), 18, 16);

        Place(body, Label("固定", 13, Text, true), 19, 61);
        var pinned = new Canvas();
        for (int index = 0; index < 12; index++)
        {
            int col = index % 6;
            int row = index / 6;
            Place(pinned, Tile(index, 27), 17 + col * 58, 10 + row * 40);
        }
        Place(body, Surface("pinned", 356, 87, pinned), 18, 83);

        Place(body, Label("推荐", 13, Text, true), 19, 187);
        var recommended = new Canvas();
        for (int i = 0; i < 4; i++)
        {
            double left = 8 + i % 2 * 178;
            double top = 7 + i / 2 * 35;
            Place(recommended, Tile(i + 3, 20), left, top);
            Place(recommended, Label(new[] { "照片", "文件资源管理器", "最近使用的文档", "设置" }[i], 10, Text), left + 28, top + 2);
        }
        Place(body, Surface("recommended", 356, 76, recommended), 18, 209);

        Place(body, Label("所有应用", 13, Text, true), 19, 300);
        var apps = new Canvas();
        for (int i = 0; i < 3; i++)
        {
            var category = new Canvas { Width = 103, Height = 60, Background = Paint("#294E6C86") };
            Place(category, Tile(i + 9, 19), 10, 8);
            Place(category, Tile(i + 1, 19), 39, 8);
            Place(category, Label(new[] { "效率", "通讯", "实用工具" }[i], 10, SoftText), 10, 35);
            Place(apps, category, 8 + i * 118, 8);
        }
        Place(body, Surface("apps", 356, 76, apps), 18, 322);

        var footer = new Canvas();
        Place(footer, new Ellipse { Width = 22, Height = 22, Fill = Paint("#C5D5E8") }, 20, 9);
        Place(footer, Label("账户", 11, Text), 51, 12);
        var power = Label("\uE7E8", 16, Text);
        power.FontFamily = IconFont;
        Place(footer, power, 346, 11);
        Place(body, Surface("footer", 388, 41, footer), 1, 416);
        return panel;
    }

    private Border CompanionPanel()
    {
        var body = new Canvas();
        var phone = new Border
        {
            Width = 26, Height = 45, Background = Paint("#4AA8D0"), BorderBrush = Paint("#152C47"),
            BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4),
        };
        Place(body, phone, 66, 34);
        var title = Label("手机连接", 14, Text, true);
        title.Width = 156;
        title.TextAlignment = TextAlignment.Center;
        Place(body, title, 1, 95);
        var setup = Label("设置设备", 10, SoftText);
        setup.Width = 156;
        setup.TextAlignment = TextAlignment.Center;
        Place(body, setup, 1, 117);
        for (int i = 0; i < 3; i++)
        {
            var icon = Label(new[] { "\uE8BD", "\uE717", "\uE72D" }[i], 13, SoftText);
            icon.FontFamily = IconFont;
            Place(body, icon, 28, 167 + i * 31);
            Place(body, Label(new[] { "消息", "通话", "发送文件" }[i], 11, Text), 52, 167 + i * 31);
        }
        Place(body, Card(62, 21, "#526A8D", 4), 14, 294);
        Place(body, Card(62, 21, "#526A8D", 4), 81, 294);
        Place(body, Label("Android", 9, Text), 28, 297);
        Place(body, Label("iPhone", 9, Text), 97, 297);
        Place(body, Card(154, 1, "#518294AF", 0), 1, 416);
        Place(body, Label("···", 17, Text), 125, 425);
        return Surface("companion", 158, 460, body, true);
    }

    private Border Surface(string id, int width, int height, UIElement child, bool panel = false)
    {
        bool isSelected = selected == id;
        var style = options(Region.Get(id));
        if (id == "companion" && style.Material == Material.Acrylic && style.ShareMainBackdrop)
            style = options(Region.Get("main"));
        var border = new Border
        {
            Width = width,
            Height = height,
            Background = brushes.Preview(style, backdrop, panel),
            BorderBrush = Hairline,
            BorderThickness = new Thickness(isSelected ? 2 : 1),
            CornerRadius = new CornerRadius(panel ? 7 : 4),
            ClipToBounds = true,
            Child = child,
        };
        if (isSelected)
            border.SetResourceReference(Border.BorderBrushProperty, "Primary");
        return border;
    }

    private static Border Tile(int index, int size)
    {
        var sub = new Rectangle { Width = size * 0.38, Height = size * 0.38, RadiusX = 2, RadiusY = 2, Fill = Paint("#BFFFFFFF") };
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(5), Child = sub,
            Background = new LinearGradientBrush(TileColors[index % TileColors.Length],
                StyleBrushes.Mix(TileColors[index % TileColors.Length], Colors.Black, 0.23), 52),
        };
    }

    private static void Place(Canvas canvas, UIElement child, double left, double top)
    {
        Canvas.SetLeft(child, left);
        Canvas.SetTop(child, top);
        canvas.Children.Add(child);
    }

    private static Border Card(int width, int height, string color, double radius) => new()
    {
        Width = width, Height = height, Background = Paint(color), CornerRadius = new CornerRadius(radius),
    };

    private static TextBlock Label(string text, double size, Brush color, bool bold = false) => new()
    {
        Text = text, FontSize = size, Foreground = color, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
    };

    private static Brush Paint(string hex) => new SolidColorBrush(StyleBrushes.ParseColor(hex));
}
