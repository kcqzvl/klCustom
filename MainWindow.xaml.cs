using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace StartStyleToolbox;

public partial class MainWindow : Window
{
    private readonly Profile profile;
    private readonly NativeStartMenu engine = new();
    private readonly bool persist;
    private readonly DispatcherTimer saveTimer;
    private readonly DispatcherTimer conflictTimer;
    private readonly List<RadioButton> materialButtons = [];
    private readonly List<TextBox> channels = [];
    private Compatibility compatibility = new(false, false, "");
    private HashSet<string> applied = [];
    private bool updating;
    private bool busy;
    private bool validColor = true;
    private StyleBrushes brushes;
    private readonly bool? previewLightMode;
    private ResourceDictionary? palette;
    private (bool Light, Color Accent)? currentTheme;

    public MainWindow() : this(Profile.Load(), true) { }

    internal MainWindow(Profile profile, bool persist, bool? previewLightMode = null)
    {
        this.profile = profile;
        this.persist = persist;
        this.previewLightMode = previewLightMode;
        brushes = new StyleBrushes(WallpaperPalette.Read());
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        saveTimer.Tick += (_, _) => SaveProfile();
        conflictTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        conflictTimer.Tick += (_, _) =>
        {
            if (!busy)
                RefreshCompatibility();
        };
        updating = true;
        InitializeComponent();
        RefreshTheme();
        CreateEditor();
        Regions.ItemsSource = Region.All;
        Regions.SelectedItem = Region.Get("companion");
        updating = false;
        RefreshCompatibility();
        LoadEditor();
        Activated += (_, _) =>
        {
            RefreshTheme();
            RefreshCompatibility();
        };
        if (persist)
            conflictTimer.Start();
    }

    protected override void OnSourceInitialized(EventArgs args)
    {
        base.OnSourceInitialized(args);
        var source = (HwndSource)PresentationSource.FromVisual(this);
        AppPalette.ApplyTitleBar(source.Handle, currentTheme?.Light == true);
        if (previewLightMode is null)
            source.AddHook(HandleThemeMessage);
    }

    private IntPtr HandleThemeMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int settingChange = 0x001A;
        const int themeChanged = 0x031A;
        const int colorizationColorChanged = 0x0320;
        if (message is settingChange or themeChanged or colorizationColorChanged)
            _ = Dispatcher.InvokeAsync(RefreshTheme, DispatcherPriority.Normal);
        return IntPtr.Zero;
    }

    private void RefreshTheme()
    {
        var theme = (Light: previewLightMode ?? AppPalette.SystemUsesLightApps(), Accent: AppPalette.SystemAccent());
        if (theme == currentTheme)
            return;

        var next = AppPalette.Create(theme.Light, theme.Accent);
        var merged = Application.Current.Resources.MergedDictionaries;
        if (palette is null)
            merged.Add(next);
        else
            merged[merged.IndexOf(palette)] = next;
        palette = next;
        currentTheme = theme;
        if (PresentationSource.FromVisual(this) is HwndSource source)
            AppPalette.ApplyTitleBar(source.Handle, theme.Light);
    }

    private Region SelectedRegion => (Region)Regions.SelectedItem;
    private StyleOptions Options(Region region) => profile.Regions.GetValueOrDefault(region.Id) ?? new();

    private void CreateEditor()
    {
        foreach (var material in Enum.GetValues<Material>())
        {
            var button = new RadioButton
            {
                GroupName = "material",
                Content = StyleBrushes.MaterialLabel(material),
                Tag = material,
                Margin = new Thickness(0, 0, 4, 4),
            };
            button.Checked += MaterialChanged;
            materialButtons.Add(button);
            Materials.Children.Add(button);
        }

        foreach (char channel in "RGBA")
        {
            var label = new TextBlock { Text = channel.ToString(), Style = (Style)FindResource("Secondary") };
            var input = new TextBox { MaxLength = 3, Height = 29, Margin = new Thickness(0, 3, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(input, channel.ToString());
            input.TextChanged += ChannelsChanged;
            channels.Add(input);
            Channels.Children.Add(new StackPanel
            {
                Margin = new Thickness(0, 0, channel == 'A' ? 0 : 6, 0),
                Children = { label, input },
            });
        }
    }

    private void LoadEditor()
    {
        if (Regions.SelectedItem is not Region region)
            return;

        updating = true;
        var options = Options(region);
        RegionTitle.Text = region.Label;
        MainOwner.Visibility = region.Id == "main" ? Visibility.Visible : Visibility.Collapsed;
        MainOwner.IsChecked = profile.MainEnabled;
        foreach (var button in materialButtons)
            button.IsChecked = Equals(button.Tag, options.Material);
        bool canShare = region.Id == "companion" && options.Material == Material.Acrylic;
        bool sharing = canShare && options.ShareMainBackdrop;
        ShareMainBackdrop.Visibility = canShare ? Visibility.Visible : Visibility.Collapsed;
        ShareMainBackdrop.IsChecked = sharing;
        ColorEditor.Visibility = sharing || options.Material is Material.Original or Material.Transparent
            ? Visibility.Collapsed : Visibility.Visible;
        SetColorControls(StyleBrushes.ParseColor(options.Color), updateHex: true);

        Sliders.Children.Clear();
        if (!sharing && options.Material is Material.Acrylic or Material.Blur or Material.Mica)
            AddSlider("混合色不透明度", options.Tint, 100, "%", (old, value) => old with { Tint = value });
        if (!sharing && options.Material == Material.Acrylic)
            AddSlider("亮度混合", options.Luminosity, 100, "%", (old, value) => old with { Luminosity = value });
        if (options.Material == Material.Blur)
            AddSlider("模糊半径", options.Blur, 64, " px", (old, value) => old with { Blur = value });
        if (options.Material == Material.Mica)
            AddSlider("背景不透明度", options.Opacity, 100, "%", (old, value) => old with { Opacity = value });

        validColor = true;
        updating = false;
        UpdateButtons();
        RefreshPreview();
    }

    private void ShareMainBackdrop_Click(object sender, RoutedEventArgs args)
    {
        UpdateDraft(Options(SelectedRegion) with { ShareMainBackdrop = ShareMainBackdrop.IsChecked == true });
        LoadEditor();
    }

    private void AddSlider(string label, int initial, int maximum, string unit,
        Func<StyleOptions, int, StyleOptions> update)
    {
        var valueLabel = new TextBlock { Text = initial + unit, Style = (Style)FindResource("Secondary") };
        DockPanel.SetDock(valueLabel, Dock.Right);
        var heading = new DockPanel
        {
            Children = { valueLabel, new TextBlock { Text = label } },
        };
        var slider = new Slider { Minimum = 0, Maximum = maximum, Value = initial, Margin = new Thickness(0, 4, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(slider, label);
        slider.ValueChanged += (_, args) =>
        {
            int value = (int)args.NewValue;
            valueLabel.Text = value + unit;
            if (!updating)
                UpdateDraft(update(Options(SelectedRegion), value));
        };
        Sliders.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 0, 11), Children = { heading, slider } });
    }

    private void SetColorControls(Color color, bool updateHex)
    {
        if (updateHex)
            HexBox.Text = StyleBrushes.Hex(color);
        byte[] rgba = [color.R, color.G, color.B, color.A];
        for (int index = 0; index < rgba.Length; index++)
            channels[index].Text = rgba[index].ToString(CultureInfo.InvariantCulture);
        SwatchFill.Background = new SolidColorBrush(color);
    }

    private void MaterialChanged(object sender, RoutedEventArgs args)
    {
        if (updating || sender is not RadioButton { Tag: Material material })
            return;
        UpdateDraft(Options(SelectedRegion) with { Material = material });
        LoadEditor();
    }

    private void HexChanged(object sender, TextChangedEventArgs args)
    {
        if (updating)
            return;
        validColor = StyleBrushes.TryColor(HexBox.Text, out Color color);
        if (validColor)
        {
            updating = true;
            SetColorControls(color, updateHex: false);
            updating = false;
            UpdateDraft(Options(SelectedRegion) with { Color = StyleBrushes.Hex(color) });
        }
        else
        {
            StatusText.Text = "请输入 #AARRGGBB 或 #RRGGBB。";
            UpdateButtons();
        }
    }

    private void ChannelsChanged(object sender, TextChangedEventArgs args)
    {
        if (updating)
            return;
        var values = new byte[4];
        validColor = channels.Select((input, index) => byte.TryParse(input.Text, NumberStyles.None,
            CultureInfo.InvariantCulture, out values[index])).All(valid => valid);
        if (!validColor)
        {
            StatusText.Text = "RGBA 通道范围为 0 到 255。";
            UpdateButtons();
            return;
        }
        SetDraftColor(Color.FromArgb(values[3], values[0], values[1], values[2]));
    }

    private void Swatch_Click(object sender, RoutedEventArgs args)
    {
        var original = StyleBrushes.ParseColor(Options(SelectedRegion).Color);
        using var picker = new Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(original.R, original.G, original.B),
        };
        if (picker.ShowDialog() == Forms.DialogResult.OK)
            SetDraftColor(Color.FromArgb(original.A, picker.Color.R, picker.Color.G, picker.Color.B));
    }

    private void SetDraftColor(Color color)
    {
        validColor = true;
        updating = true;
        SetColorControls(color, updateHex: true);
        updating = false;
        UpdateDraft(Options(SelectedRegion) with { Color = StyleBrushes.Hex(color) });
    }

    private void Preset_Click(object sender, RoutedEventArgs args)
    {
        UpdateDraft(StyleOptions.MistyBlue);
        LoadEditor();
    }

    private void UpdateDraft(StyleOptions options)
    {
        profile.Regions[SelectedRegion.Id] = options;
        StatusText.Text = "已更新预览";
        QueueSave();
        UpdateButtons();
        RefreshPreview();
    }

    private void RegionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!updating)
            LoadEditor();
    }

    private void SceneChanged(object sender, RoutedEventArgs args)
    {
        if (!updating && PreviewContent is not null)
            RefreshPreview();
    }

    private void RefreshPreview()
    {
        bool light = LightScene.IsChecked == true;
        PreviewContent.Content = new StartPreview(brushes, light, Options, SelectedRegion.Id);
    }

    private void RefreshCompatibility()
    {
        try
        {
            compatibility = engine.GetCompatibility();
            applied = engine.AppliedRegions();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or InvalidOperationException or System.Security.SecurityException or Win32Exception)
        {
            compatibility = new(false, false, "无法读取开始菜单状态");
            StatusText.Text = error.Message;
        }
        DependencyStatus.Text = compatibility.Message;
        DependencyStatus.SetResourceReference(TextBlock.ForegroundProperty, compatibility.CanApply ? "Accent" : "Muted");
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        if (Regions.SelectedItem is not Region region)
            return;
        string? conflict = compatibility.Conflict(region.Id);
        bool canOwn = region.Id != "main" || profile.MainEnabled;
        bool hasDraft = Options(region).Material != Material.Original;
        bool hasApplied = applied.Contains(region.Id);
        RegionStatus.Text = conflict ?? (!canOwn ? "主背景管理已关闭" : hasApplied ? "已应用到开始菜单" : "保持系统当前样式");
        ApplyButton.IsEnabled = !busy && canOwn && validColor && (conflict is null || !hasDraft) &&
            (hasDraft ? compatibility.CanApply : compatibility.CanRestore && hasApplied);
        ApplyButton.ToolTip = ApplyButton.IsEnabled ? null
            : busy ? "正在更新开始菜单"
            : conflict is not null ? conflict
            : !canOwn ? "请先启用主背景管理"
            : !validColor ? "请输入有效的颜色"
            : !hasDraft && !hasApplied ? "请先选择要应用的材质"
            : compatibility.Message;
        CopyButton.IsEnabled = !busy && validColor;
        RestoreButton.IsEnabled = !busy && (hasApplied ? compatibility.CanRestore : hasDraft);
        RestoreAllButton.IsEnabled = !busy && (applied.Count != 0
            ? compatibility.CanRestore : profile.Regions.Values.Any(options => options.Material != Material.Original));
        MainOwner.IsEnabled = !busy && conflict is null;
    }

    private async void MainOwner_Click(object sender, RoutedEventArgs args)
    {
        if (MainOwner.IsChecked == true)
        {
            var result = MessageBox.Show(this, "启用后，主面板背景将使用本工具选择的材质。",
                "主背景管理", MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (result != MessageBoxResult.OK)
            {
                MainOwner.IsChecked = false;
                return;
            }
        }
        else if (applied.Contains("main") && !await CommitAsync(new() { ["main"] = new() }))
        {
            MainOwner.IsChecked = true;
            return;
        }
        profile.MainEnabled = MainOwner.IsChecked == true;
        QueueSave();
        UpdateButtons();
    }

    private void Copy_Click(object sender, RoutedEventArgs args)
    {
        var source = SelectedRegion;
        var options = Options(source);
        var menu = CreateMenu(CopyButton);
        foreach (var target in Region.All.Where(region => region.Id != source.Id && (region.Id != "main" || profile.MainEnabled)))
        {
            var item = MenuOption(target.Label);
            item.Click += (_, _) =>
            {
                profile.Regions[target.Id] = options;
                Regions.SelectedItem = target;
                StatusText.Text = $"已复制到{target.Label}";
                QueueSave();
            };
            menu.Items.Add(item);
        }
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }

    private async void Apply_Click(object sender, RoutedEventArgs args)
    {
        brushes = new StyleBrushes(WallpaperPalette.Read());
        RefreshPreview();
        var region = SelectedRegion;
        await CommitAsync(new() { [region.Id] = Options(region) });
    }

    private async void Restore_Click(object sender, RoutedEventArgs args)
    {
        var region = SelectedRegion;
        if (!applied.Contains(region.Id) || await CommitAsync(new() { [region.Id] = new() }))
        {
            profile.Regions.Remove(region.Id);
            QueueSave();
            LoadEditor();
        }
    }

    private async void RestoreAll_Click(object sender, RoutedEventArgs args)
    {
        if (applied.Count > 0 && !await CommitAsync(applied.ToDictionary(id => id, _ => new StyleOptions())))
            return;
        profile.Regions.Clear();
        QueueSave();
        LoadEditor();
        StatusText.Text = "已恢复全部区域";
    }

    private async Task<bool> CommitAsync(Dictionary<string, StyleOptions> settings)
    {
        busy = true;
        UpdateButtons();
        StatusText.Text = "正在更新开始菜单…";
        try
        {
            var result = await engine.RequestCommitAsync(settings, profile.MainEnabled);
            StatusText.Text = result.Message;
            RefreshCompatibility();
            return result.Success;
        }
        finally
        {
            busy = false;
            UpdateButtons();
        }
    }

    private void Dependencies_Click(object sender, RoutedEventArgs args)
    {
        RefreshCompatibility();
        StatusText.Text = compatibility.Message;
    }

    private ContextMenu CreateMenu(Button anchor) => new()
    {
        PlacementTarget = anchor,
        Style = (Style)FindResource("ToolMenu"),
    };

    private MenuItem MenuOption(string label) => new()
    {
        Header = label,
        Style = (Style)FindResource("ToolMenuItem"),
    };

    private void QueueSave()
    {
        if (!persist)
            return;
        saveTimer.Stop();
        saveTimer.Start();
    }

    private void SaveProfile()
    {
        saveTimer.Stop();
        try { profile.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "无法保存本地参数。" + error.Message;
        }
    }

    protected override void OnClosing(CancelEventArgs args)
    {
        if (busy)
            args.Cancel = true;
        else if (saveTimer.IsEnabled)
            SaveProfile();
        base.OnClosing(args);
        if (!args.Cancel)
            conflictTimer.Stop();
    }
}
