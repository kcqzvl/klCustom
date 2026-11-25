using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;

namespace StartStyleToolbox;

public partial class MainWindow : Window
{
    private readonly Profile profile;
    private readonly NativeStartMenu engine = new();
    private readonly bool persist;
    private readonly DispatcherTimer saveTimer;
    private readonly DispatcherTimer conflictTimer;
    private readonly List<RadioButton> materialButtons = [];
    private readonly List<RadioButton> themeMaterialButtons = [];
    private readonly List<TextBox> channels = [];
    private Compatibility compatibility = new(false, false, "");
    private Dictionary<string, byte[]> appliedStyles = [];
    private bool appliedStylesCurrent;
    private bool updating;
    private bool busy;
    private bool validColor = true;
    private StyleBrushes brushes;
    private readonly bool? previewLightMode;
    private ResourceDictionary? palette;
    private (bool Light, Color Accent, Material Material, ThemeTint Tint, bool HasBackdrop, bool SystemDarkFallback)? currentTheme;
    private (bool Light, Material Material, bool Transparency)? nativeTheme;
    private bool hasBackdrop;
    private string? wallpaperPath;
    private ImageSource? wallpaper;
    private bool showingSettings;
    private bool startMenuExpanded = true;
    private bool startMenuAnimating;
    private bool showCompanionPanel;
    private bool? editorIsActive;
    private bool runtimeInstalled;

    public MainWindow() : this(Profile.Load(), true) { }

    internal MainWindow(Profile profile, bool persist, bool? previewLightMode = null)
    {
        this.profile = profile;
        this.persist = persist;
        this.previewLightMode = previewLightMode;
        showCompanionPanel = NativeStartMenu.IsCompanionPanelVisible();
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
        WirePercentageInput(ThemeTintOpacityInput, ThemeTintOpacity);
        WirePercentageInput(ThemeLuminosityInput, ThemeLuminosity);
        WirePercentageInput(ThemeColorTransparencyInput, ThemeColorTransparency);
        WirePercentageInput(ThemeBlurStrengthInput, ThemeBlurStrength);
        VersionText.Text = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0].Replace('-', ' ');
        RefreshTheme();
        CreateEditor();
        CreateThemeMaterials();
        LanguageCombo.ItemsSource = Localization.SupportedLanguages.Select(Localization.LanguageName).ToArray();
        runtimeInstalled = DesktopRuntime.IsInstalled();
        ApplyLanguage();
        updating = false;
        RefreshCompatibility();
        LoadEditor();
        Activated += (_, _) =>
        {
            editorIsActive = true;
            RefreshTheme();
            RefreshCompatibility();
            RefreshRuntimeStatus();
        };
        Deactivated += (_, _) =>
        {
            editorIsActive = false;
            RefreshTheme();
        };
        if (persist)
            conflictTimer.Start();
    }

    protected override void OnSourceInitialized(EventArgs args)
    {
        base.OnSourceInitialized(args);
        var source = (HwndSource)PresentationSource.FromVisual(this);
        RefreshTheme();
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
        bool light = previewLightMode ?? profile.ThemeMode switch
        {
            "Light" => true,
            "Dark" => false,
            _ => AppPalette.SystemUsesLightApps(),
        };
        var material = profile.ThemeMaterial;
        bool systemDarkFallback = !light && editorIsActive == false;
        var native = (Light: light, Material: material,
            Transparency: persist && AppPalette.SystemUsesTransparency());
        if (native != nativeTheme && PresentationSource.FromVisual(this) is HwndSource source)
        {
            hasBackdrop = AppPalette.ApplyWindowAppearance(source, native.Light, native.Material, native.Transparency,
                WindowChrome.GetWindowChrome(this));
            nativeTheme = native;
        }

        var tint = light ? profile.LightThemeTint : profile.DarkThemeTint;
        bool useWallpaper = native.Transparency && material == Material.Acrylic && !hasBackdrop;
        UpdateWallpaper(useWallpaper);
        var theme = (Light: light, Accent: AppPalette.SystemAccent(), Material: material,
            Tint: tint, HasBackdrop: hasBackdrop || WallpaperLayer.Source is not null, SystemDarkFallback: systemDarkFallback);
        if (theme == currentTheme)
            return;

        bool fallbackChanged = currentTheme?.SystemDarkFallback != systemDarkFallback;
        bool animateFocus = fallbackChanged && IsLoaded && SystemParameters.ClientAreaAnimation && currentTheme?.Light == light;
        var next = AppPalette.Create(theme.Light, theme.Accent, theme.Material, theme.Tint, theme.HasBackdrop,
            theme.SystemDarkFallback, animateFocus ? palette : null);
        var merged = Application.Current.Resources.MergedDictionaries;
        if (palette is null)
            merged.Add(next);
        else
            merged[merged.IndexOf(palette)] = next;
        palette = next;
        currentTheme = theme;
        if (!fallbackChanged)
            return;

        double target = systemDarkFallback ? 1 : 0;
        double visibleOpacity = InactiveWindowLayer.Opacity;
        InactiveWindowLayer.Opacity = target;
        InactiveWindowLayer.BeginAnimation(OpacityProperty, animateFocus && visibleOpacity != target
            ? AppPalette.FocusFade(visibleOpacity, target) : null);
    }

    private void UpdateWallpaper(bool show)
    {
        if (!show)
        {
            WallpaperLayer.Source = null;
            return;
        }

        string? path = WallpaperPalette.DesktopPath();
        if (path != wallpaperPath)
        {
            wallpaperPath = path;
            wallpaper = WallpaperPalette.LoadImage(path, 1600);
        }
        WallpaperLayer.Source = wallpaper;
    }

    private Region[] LocalizedRegions() => Region.All
        .Where(region => NativeStartMenu.HasRegion(region.Id, NativeStartMenu.UsesWindows10Layout) &&
            (region.Id != "companion" || showCompanionPanel))
        .Select(region => region with { Label = Localization.RegionLabel(profile.Language, region.Id) }).ToArray();

    private void LoadRegions(string? selectedId)
    {
        var regions = LocalizedRegions();
        Regions.ItemsSource = regions;
        Regions.SelectedItem = regions.FirstOrDefault(region => region.Id == (selectedId ?? "main")) ?? regions[0];
    }

    private string L(string key) => Localization.Get(profile.Language, key);

    private void ApplyLanguage()
    {
        string? selectedId = (Regions.SelectedItem as Region)?.Id;
        updating = true;
        AppearanceLabel.Text = L("startMenu");
        StartWidgetsLabel.Text = L("startWidgets");
        MinimizeButton.ToolTip = L("windowMinimize");
        MaximizeButton.ToolTip = L("windowMaximize");
        CloseButton.ToolTip = L("windowClose");
        foreach (var button in new[] { MinimizeButton, MaximizeButton, CloseButton })
            System.Windows.Automation.AutomationProperties.SetName(button, (string)button.ToolTip);
        SettingsButtonLabel.Text = L("settings");
        RestoreAllLabel.Text = L("restoreAll");
        RestoreAllTitle.Text = L("startMenu");
        RestoreAllDescription.Text = L("restoreAllDescription");
        BreadcrumbLabel.Text = L("breadcrumb");
        MaterialLabel.Text = L("material");
        foreach (var button in materialButtons)
            if (button.Tag is Material material)
                button.Content = Localization.MaterialName(profile.Language, material);
        ColorLabel.Text = L("color");
        PreviewLabel.Text = L("preview");
        LightScene.Content = L("lightWeb");
        DarkScene.Content = L("darkWindow");
        CopyButton.Content = L("copy");
        RestoreButton.Content = L("restore");
        ApplyButton.Content = L("apply");
        SettingsTitle.Text = L("settingsTitle");
        SettingsSubtitle.Text = L("settingsHint");
        ThemeTitle.Text = L("theme");
        ThemeDescription.Text = L("themeDescription");
        ThemeMaterialTitle.Text = L("themeMaterial");
        LightTintTab.Content = L("lightTintSettings");
        DarkTintTab.Content = L("darkTintSettings");
        ThemeTintColorLabel.Text = L("themeTintColor");
        ThemeColorTransparencyLabel.Text = L("colorTransparency");
        ThemeBlurStrengthLabel.Text = L("themeBlurStrength");
        ThemeTintOpacityLabel.Text = L("tint");
        ThemeLuminosityLabel.Text = L("themeLuminosity");
        foreach (var button in themeMaterialButtons)
            if (button.Tag is Material material)
                button.Content = Localization.MaterialName(profile.Language, material);
        ThemeSystem.Content = L("system");
        ThemeLight.Content = L("light");
        ThemeDark.Content = L("dark");
        LanguageTitle.Text = L("language");
        LanguageDescription.Text = L("languageDescription");
        AboutTitle.Text = L("about");
        AuthorLabel.Text = L("author");
        RepositoryLabel.Text = L("repository");
        VersionLabel.Text = L("version");
        UpdateRuntimeStatus();
        LoadRegions(selectedId);
        LanguageCombo.SelectedIndex = Array.IndexOf(Localization.SupportedLanguages, profile.Language);
        ApplySettingsState();
        updating = false;
        if (Regions.SelectedItem is Region)
            LoadEditor();
    }

    private void StartMenuExpander_Click(object sender, RoutedEventArgs args)
    {
        if (startMenuAnimating)
            return;
        startMenuExpanded = !startMenuExpanded;
        StartMenuExpanderGlyph.Text = startMenuExpanded ? "\uE70D" : "\uE70E";
        AnimateStartMenuOptions(startMenuExpanded);
    }

    private void AnimateStartMenuOptions(bool expanded)
    {
        startMenuAnimating = true;
        var transform = StartMenuOptionsPanel.RenderTransform as TranslateTransform ?? new TranslateTransform();
        StartMenuOptionsPanel.RenderTransform = transform;
        StartMenuOptionsPanel.RenderTransformOrigin = new Point(0, 0.5);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(180);
        if (expanded)
        {
            StartMenuOptionsPanel.Visibility = Visibility.Visible;
            StartMenuOptionsPanel.IsHitTestVisible = true;
            StartMenuOptionsPanel.Opacity = 0;
            transform.X = -16;
            StartMenuOptionsPanel.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
            var enter = new DoubleAnimation(-16, 0, duration) { EasingFunction = ease };
            enter.Completed += AnimationFinished;
            transform.BeginAnimation(TranslateTransform.XProperty, enter);
            return;
        }

        StartMenuOptionsPanel.IsHitTestVisible = false;
        StartMenuOptionsPanel.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(1, 0, duration) { EasingFunction = ease });
        var exit = new DoubleAnimation(0, -16, duration) { EasingFunction = ease };
        exit.Completed += (_, _) =>
        {
            StartMenuOptionsPanel.Visibility = Visibility.Collapsed;
            StartMenuOptionsPanel.Opacity = 1;
            transform.X = 0;
            StartMenuOptionsPanel.IsHitTestVisible = true;
            startMenuAnimating = false;
        };
        transform.BeginAnimation(TranslateTransform.XProperty, exit);

        void AnimationFinished(object? sender, EventArgs args)
        {
            transform.X = 0;
            startMenuAnimating = false;
        }
    }

    private void ApplySettingsState()
    {
        ThemeSystem.IsChecked = profile.ThemeMode == "System";
        ThemeLight.IsChecked = profile.ThemeMode == "Light";
        ThemeDark.IsChecked = profile.ThemeMode == "Dark";
        foreach (var button in themeMaterialButtons)
            button.IsChecked = Equals(button.Tag, profile.ThemeMaterial);
        LightTintTab.IsChecked = currentTheme?.Light == true;
        DarkTintTab.IsChecked = LightTintTab.IsChecked != true;
        LoadThemeTint();
    }

    private void ShowPage(bool settings)
    {
        if (showingSettings == settings && (settings ? SettingsPage.Visibility : EditorPage.Visibility) == Visibility.Visible)
            return;
        showingSettings = settings;
        var incoming = settings ? (UIElement)SettingsPage : EditorPage;
        var outgoing = settings ? (UIElement)EditorPage : SettingsPage;
        outgoing.Visibility = Visibility.Collapsed;
        incoming.Visibility = Visibility.Visible;
        incoming.Opacity = 0;
        incoming.RenderTransform = new TranslateTransform(settings ? 14 : -14, 0);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        incoming.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        ((TranslateTransform)incoming.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
            new System.Windows.Media.Animation.DoubleAnimation(settings ? 14 : -14, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        ActionButtons.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        if (settings)
            Regions.Resources["RegionSelectionOpacity"] = 0.0;
        else
            Regions.Resources.Remove("RegionSelectionOpacity");
        if (settings)
            SettingsButton.SetResourceReference(Control.BackgroundProperty, "ChoiceSelected");
        else
            SettingsButton.ClearValue(Control.BackgroundProperty);
    }

    private void Minimize_Click(object sender, RoutedEventArgs args) => SystemCommands.MinimizeWindow(this);

    private void MaximizeRestore_Click(object sender, RoutedEventArgs args)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void Close_Click(object sender, RoutedEventArgs args) => SystemCommands.CloseWindow(this);

    private void Settings_Click(object sender, RoutedEventArgs args) => ShowPage(true);

    private void RegionItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (showingSettings && sender is ListBoxItem { IsSelected: true })
            ShowPage(false);
    }

    private void RegionItem_PreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (showingSettings && sender is ListBoxItem { IsSelected: true } && args.Key is Key.Enter or Key.Space)
        {
            ShowPage(false);
            args.Handled = true;
        }
    }

    private void ThemeMode_Click(object sender, RoutedEventArgs args)
    {
        if (updating || sender is not RadioButton { IsChecked: true, Tag: string mode })
            return;
        profile.ThemeMode = mode;
        QueueSave();
        RefreshTheme();
        ApplySettingsState();
    }

    private void ThemeMaterialChanged(object sender, RoutedEventArgs args)
    {
        if (updating || sender is not RadioButton { IsChecked: true, Tag: Material material })
            return;
        profile.ThemeMaterial = material;
        UpdateThemeBlurVisibility();
        QueueSave();
        RefreshTheme();
    }

    private ThemeTint EditedThemeTint => LightTintTab.IsChecked == true ? profile.LightThemeTint : profile.DarkThemeTint;

    private void ThemeTintTab_Click(object sender, RoutedEventArgs args) => LoadThemeTint();

    private void LoadThemeTint()
    {
        bool wasUpdating = updating;
        updating = true;
        var settings = EditedThemeTint;
        ThemeTintHex.ClearValue(Control.BorderBrushProperty);
        ThemeTintHex.Text = settings.Color;
        ThemeTintOpacity.Value = settings.Tint;
        ThemeLuminosity.Value = settings.Luminosity;
        ThemeBlurStrength.Value = settings.BlurRadius;
        var color = StyleBrushes.ParseColor(settings.Color);
        ThemeColorTransparency.Value = settings.ColorTransparency;
        SetThemeTintSwatch(color);
        UpdateThemeBlurVisibility();
        updating = wasUpdating;
    }

    private void ThemeTintHex_Changed(object sender, TextChangedEventArgs args)
    {
        if (updating)
            return;
        if (StyleBrushes.TryColor(ThemeTintHex.Text, out var color))
        {
            ThemeTintHex.ClearValue(Control.BorderBrushProperty);
            SetThemeTintSwatch(color);
            SetThemeTint(EditedThemeTint with { Color = StyleBrushes.Hex(color) });
        }
        else
            ThemeTintHex.SetResourceReference(Control.BorderBrushProperty, "InvalidInput");
    }

    private void ThemeTintHex_LostFocus(object sender, RoutedEventArgs args) => ThemeTintHex.Text = EditedThemeTint.Color;

    private void ThemeTintSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> args)
    {
        if (!updating)
            SetThemeTint(EditedThemeTint with { Tint = (int)ThemeTintOpacity.Value,
                Luminosity = (int)ThemeLuminosity.Value, BlurRadius = (int)ThemeBlurStrength.Value });
    }

    private void ThemeColorTransparency_Changed(object sender, RoutedPropertyChangedEventArgs<double> args)
    {
        if (!updating)
            SetThemeTint(EditedThemeTint with { Transparency = args.NewValue });
    }

    private void UpdateThemeBlurVisibility()
    {
        var visibility = profile.ThemeMaterial == Material.Blur ? Visibility.Visible : Visibility.Collapsed;
        ThemeBlurStrengthLabel.Visibility = ThemeBlurStrengthField.Visibility = visibility;
    }

    private void ThemeTintSwatch_Click(object sender, RoutedEventArgs args)
    {
        if (PickColor(StyleBrushes.ParseColor(EditedThemeTint.Color)) is Color color)
            ThemeTintHex.Text = StyleBrushes.Hex(color);
    }

    private void SetThemeTintSwatch(Color color)
    {
        ThemeTintSwatch.Background = new SolidColorBrush(color);
        ThemeTintPicker.ToolTip = $"RGBA({color.R}, {color.G}, {color.B}, {color.A})";
    }

    private void SetThemeTint(ThemeTint settings)
    {
        if (settings == EditedThemeTint)
            return;
        if (LightTintTab.IsChecked == true)
            profile.LightThemeTint = settings;
        else
            profile.DarkThemeTint = settings;
        QueueSave();
        RefreshTheme();
    }

    private void LanguageChanged(object sender, SelectionChangedEventArgs args)
    {
        if (updating || LanguageCombo.SelectedIndex < 0)
            return;
        profile.Language = Localization.SupportedLanguages[LanguageCombo.SelectedIndex];
        QueueSave();
        ApplyLanguage();
    }

    private void AuthorLink_Click(object sender, RoutedEventArgs args) => OpenUrl("https://github.com/kcqzvl");

    private void RepositoryLink_Click(object sender, RoutedEventArgs args) => OpenUrl("https://github.com/kcqzvl/klCustom");

    private void RefreshRuntimeStatus()
    {
        runtimeInstalled = DesktopRuntime.IsInstalled();
        UpdateRuntimeStatus();
    }

    private void UpdateRuntimeStatus()
    {
        RuntimeStatus.Visibility = runtimeInstalled ? Visibility.Visible : Visibility.Collapsed;
        RuntimeStatus.Text = L("runtimeInstalled");
        RuntimeStatus.ToolTip = DesktopRuntime.DisplayName;
        System.Windows.Automation.AutomationProperties.SetName(RuntimeStatus, $"{DesktopRuntime.DisplayName} · {RuntimeStatus.Text}");
    }

    private void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception)
        {
            StatusText.Text = error.Message;
        }
    }

    private Region SelectedRegion => (Region)Regions.SelectedItem;
    private StyleOptions Options(Region region) => profile.Regions.GetValueOrDefault(region.Id) ?? StyleOptions.DefaultDraft;

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
            bool opacity = channel == 'A';
            var label = new TextBlock { Text = channel.ToString(), Style = (Style)FindResource("Secondary") };
            var input = new TextBox { MaxLength = opacity ? 6 : 3, Height = 29, TextAlignment = TextAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(input, opacity ? "不透明度（百分比）" : channel.ToString());
            input.TextChanged += ChannelsChanged;
            channels.Add(input);
            var field = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
            if (opacity)
            {
                var suffix = new TextBlock
                {
                    Text = "%",
                    Style = (Style)FindResource("Secondary"),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                DockPanel.SetDock(suffix, Dock.Right);
                field.Children.Add(suffix);
            }
            field.Children.Add(input);
            Channels.Children.Add(new StackPanel
            {
                Margin = new Thickness(0, 0, opacity ? 0 : 6, 0),
                Children = { label, field },
            });
        }
    }

    private void CreateThemeMaterials()
    {
        foreach (var material in new[] { Material.Transparent, Material.Blur, Material.Acrylic })
        {
            var button = new RadioButton
            {
                GroupName = "themeMaterial",
                Content = Localization.MaterialName(profile.Language, material),
                Tag = material,
                Margin = new Thickness(0, 0, 12, 4),
            };
            button.Checked += ThemeMaterialChanged;
            themeMaterialButtons.Add(button);
            ThemeMaterials.Children.Add(button);
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
        ColorEditor.Visibility = options.Material == Material.Original
            ? Visibility.Collapsed : Visibility.Visible;
        SetColorControls(StyleBrushes.ParseColor(options.Color), updateHex: true);

        Sliders.Children.Clear();
        if (options.Material is Material.Acrylic or Material.Blur or Material.Mica)
            AddSlider(L("tint"), options.Tint, 100, (old, value) => old with { Tint = value });
        if (options.Material == Material.Acrylic)
            AddSlider(L("luminosity"), options.Luminosity, 100, (old, value) => old with { Luminosity = value });
        if (options.Material == Material.Transparent)
        {
            AddSlider(L("tint"), options.TransparentTint, 100, (old, value) => old with { TransparentTint = value });
            AddSlider(L("luminosity"), options.TransparentLuminosity, 100, (old, value) => old with { TransparentLuminosity = value });
        }
        if (options.Material == Material.Blur)
            AddSlider(L("blurRadius"), options.Blur, 64, (old, value) => old with { Blur = value });
        if (options.Material == Material.Mica)
            AddSlider(L("opacity"), options.Opacity, 100, (old, value) => old with { Opacity = value });

        validColor = true;
        updating = false;
        UpdateButtons();
        RefreshPreview();
    }

    private void AddSlider(string label, int initial, int maximum,
        Func<StyleOptions, int, StyleOptions> update)
    {
        string regionId = SelectedRegion.Id;
        var input = new TextBox { Style = (Style)FindResource("SliderPercentageInput") };
        DockPanel.SetDock(input, Dock.Right);
        var heading = new DockPanel
        {
            Children = { input, new TextBlock { Text = label } },
        };
        var slider = new Slider { Minimum = 0, Maximum = maximum, Value = initial, Margin = new Thickness(0, 4, 0, 0) };
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 11), Children = { heading, slider } };
        System.Windows.Automation.AutomationProperties.SetName(input, label);
        System.Windows.Automation.AutomationProperties.SetName(slider, label);
        slider.ValueChanged += (_, args) =>
        {
            if (!updating)
                UpdateDraft(update(Options(SelectedRegion), (int)args.NewValue));
        };
        WirePercentageInput(input, slider, () => (Regions.SelectedItem as Region)?.Id == regionId && Sliders.Children.Contains(row));
        Sliders.Children.Add(row);
    }

    private void WirePercentageInput(TextBox input, Slider slider, Func<bool>? canCommit = null)
    {
        int Percent(double value) => (int)Math.Round(value * 100 / slider.Maximum, MidpointRounding.AwayFromZero);
        void ShowValue() => input.Text = $"{Percent(slider.Value)}%";
        ShowValue();
        slider.ValueChanged += (_, _) => ShowValue();
        input.PreviewMouseLeftButtonDown += (_, args) =>
        {
            if (!input.IsKeyboardFocusWithin)
                args.Handled = input.Focus();
        };
        input.GotKeyboardFocus += (_, _) =>
        {
            input.Text = Percent(slider.Value).ToString(CultureInfo.InvariantCulture);
            input.SelectAll();
        };
        bool cancelEdit = false;
        input.LostKeyboardFocus += (_, _) =>
        {
            var text = input.Text.Trim();
            if (text.EndsWith('%'))
                text = text[..^1].TrimEnd();
            if (!cancelEdit && !updating && (canCommit is null || canCommit()) &&
                int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int percent) &&
                percent is >= 0 and <= 100 && percent != Percent(slider.Value))
                slider.Value = Math.Round(percent * slider.Maximum / 100.0, MidpointRounding.AwayFromZero);
            cancelEdit = false;
            ShowValue();
        };
        input.PreviewKeyDown += (_, args) =>
        {
            if (args.Key is not (Key.Enter or Key.Escape))
                return;
            args.Handled = true;
            cancelEdit = args.Key == Key.Escape;
            slider.Focus();
        };
    }

    private void SetColorControls(Color color, bool updateHex, bool updateChannels = true)
    {
        if (updateHex)
            HexBox.Text = StyleBrushes.Hex(color);
        if (updateChannels)
        {
            channels[0].Text = color.R.ToString(CultureInfo.InvariantCulture);
            channels[1].Text = color.G.ToString(CultureInfo.InvariantCulture);
            channels[2].Text = color.B.ToString(CultureInfo.InvariantCulture);
            channels[3].Text = StyleBrushes.FormatOpacity(color.A);
        }
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
        if (!byte.TryParse(channels[0].Text, NumberStyles.None, CultureInfo.InvariantCulture, out byte red)
            || !byte.TryParse(channels[1].Text, NumberStyles.None, CultureInfo.InvariantCulture, out byte green)
            || !byte.TryParse(channels[2].Text, NumberStyles.None, CultureInfo.InvariantCulture, out byte blue)
            || !StyleBrushes.TryParseOpacity(channels[3].Text, out byte alpha))
        {
            validColor = false;
            StatusText.Text = "RGB 范围为 0 到 255，不透明度范围为 0% 到 100%。";
            UpdateButtons();
            return;
        }
        SetDraftColor(Color.FromArgb(alpha, red, green, blue), updateChannels: false);
    }

    private void Swatch_Click(object sender, RoutedEventArgs args)
    {
        var original = StyleBrushes.ParseColor(Options(SelectedRegion).Color);
        if (PickColor(original) is Color color)
            SetDraftColor(color);
    }

    private Color? PickColor(Color original)
    {
        var picker = new ColorPickerWindow(this, original, L);
        picker.Activated += (_, _) => ApplyPickerAppearance(picker, active: true);
        picker.Deactivated += (_, _) => ApplyPickerAppearance(picker, active: false);
        picker.SourceInitialized += (_, _) => ApplyPickerAppearance(picker, active: true);
        return picker.ShowDialog() == true ? picker.SelectedColor : null;
    }

    private void ApplyPickerAppearance(ColorPickerWindow picker, bool active)
    {
        if (currentTheme is { } theme && PresentationSource.FromVisual(picker) is HwndSource source)
        {
            bool systemDarkFallback = !theme.Light && !active;
            bool transparency = persist && !systemDarkFallback && AppPalette.SystemUsesTransparency();
            bool systemBackdrop = AppPalette.ApplyWindowAppearance(source, theme.Light, theme.Material, transparency);
            ImageSource? backdrop = transparency && theme.Material == Material.Acrylic && !systemBackdrop ? wallpaper : null;
            var colors = AppPalette.Create(theme.Light, theme.Accent, theme.Material, theme.Tint,
                systemBackdrop || backdrop is not null, systemDarkFallback);
            picker.SetBackdrop(systemBackdrop, backdrop, colors);
        }
    }

    private void SetDraftColor(Color color, bool updateChannels = true)
    {
        validColor = true;
        updating = true;
        SetColorControls(color, updateHex: true, updateChannels: updateChannels);
        updating = false;
        UpdateDraft(Options(SelectedRegion) with { Color = StyleBrushes.Hex(color) });
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
        {
            ShowPage(false);
            LoadEditor();
        }
    }

    private void SceneChanged(object sender, RoutedEventArgs args)
    {
        if (!updating && PreviewContent is not null)
            RefreshPreview();
    }

    private void RefreshPreview()
    {
        bool light = LightScene.IsChecked == true;
        PreviewContent.Content = new StartPreview(brushes, light, Options, SelectedRegion.Id,
            NativeStartMenu.UsesWindows10Layout, showCompanionPanel);
    }

    private void RefreshCompanionPanel()
    {
        bool visible = NativeStartMenu.IsCompanionPanelVisible();
        if (visible == showCompanionPanel)
            return;

        showCompanionPanel = visible;
        string? selectedId = (Regions.SelectedItem as Region)?.Id;
        bool wasUpdating = updating;
        updating = true;
        LoadRegions(selectedId);
        updating = wasUpdating;
        if (SelectedRegion.Id == selectedId)
            RefreshPreview();
        else
            LoadEditor();
    }

    private void RefreshCompatibility()
    {
        RefreshCompanionPanel();
        try
        {
            compatibility = engine.GetCompatibility();
            var snapshot = engine.ReadAppliedStyles();
            appliedStylesCurrent = snapshot is not null;
            if (snapshot is not null)
                appliedStyles = snapshot;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or InvalidOperationException or System.Security.SecurityException or Win32Exception)
        {
            compatibility = new(false, false, "无法读取开始菜单状态");
            appliedStylesCurrent = false;
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
        var draft = Options(region);
        bool hasDraft = draft.Material != Material.Original;
        var appliedStyle = appliedStyles.GetValueOrDefault(region.Id);
        bool hasApplied = appliedStyle is not null;
        bool hasChanges = hasDraft
            ? appliedStyle is null || validColor && !NativeStartMenu.MatchesAppliedStyle(appliedStyle, draft, brushes)
            : hasApplied;
        RegionStatus.Text = conflict ?? (!canOwn ? "主背景管理已关闭" : hasApplied ? "已应用到开始菜单" : "保持系统当前样式");
        ApplyButton.IsEnabled = !busy && appliedStylesCurrent && canOwn && validColor && hasChanges &&
            (hasDraft ? compatibility.CanApply && conflict is null : compatibility.CanRestore);
        ApplyButton.ToolTip = ApplyButton.IsEnabled ? null
            : busy ? "正在更新开始菜单"
            : conflict is not null ? conflict
            : !canOwn ? "请先启用主背景管理"
            : !validColor ? "请输入有效的颜色"
            : !appliedStylesCurrent && compatibility.CanApply ? L("applyChecking")
            : appliedStylesCurrent && !hasChanges ? L("applyNoChanges")
            : compatibility.Message;
        CopyButton.IsEnabled = !busy && validColor;
        RestoreButton.IsEnabled = !busy && (hasApplied ? compatibility.CanRestore : hasDraft);
        RestoreAllButton.IsEnabled = !busy && (appliedStyles.Count != 0
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
        else if (appliedStyles.ContainsKey("main") && !await CommitAsync(new() { ["main"] = new() }))
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
        foreach (var target in LocalizedRegions().Where(region => region.Id != source.Id && (region.Id != "main" || profile.MainEnabled)))
        {
            var item = MenuOption(target.Label);
            item.Click += (_, _) =>
            {
                profile.Regions[target.Id] = options;
                Regions.SelectedValue = target.Id;
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
        var region = SelectedRegion;
        brushes = new StyleBrushes(WallpaperPalette.Read());
        RefreshCompatibility();
        if (!ApplyButton.IsEnabled || SelectedRegion.Id != region.Id)
            return;
        RefreshPreview();
        await CommitAsync(new() { [region.Id] = Options(region) });
    }

    private async void Restore_Click(object sender, RoutedEventArgs args)
    {
        var region = SelectedRegion;
        if (!appliedStyles.ContainsKey(region.Id) || await CommitAsync(new() { [region.Id] = new() }))
        {
            profile.Regions.Remove(region.Id);
            QueueSave();
            LoadEditor();
        }
    }

    private async void RestoreAll_Click(object sender, RoutedEventArgs args)
    {
        if (appliedStyles.Count > 0 && !await CommitAsync(appliedStyles.Keys.ToDictionary(id => id, _ => new StyleOptions())))
            return;
        profile.Regions.Clear();
        QueueSave();
        LoadEditor();
        StatusText.Text = L("restoreAllDone");
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
