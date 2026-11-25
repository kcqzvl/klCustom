using System;
using System.Collections.Generic;

namespace StartStyleToolbox;

internal static class Localization
{
    internal static readonly string[] SupportedLanguages = ["zh-CN", "zh-HK", "en-US", "ja-JP"];

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Text =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["zh-CN"] = new Dictionary<string, string>
            {
                ["settings"] = "设置", ["appearance"] = "开始菜单", ["startMenu"] = "开始菜单", ["startWidgets"] = "开始菜单与小组件",
                ["search"] = "查找设置", ["restoreAll"] = "全部恢复", ["breadcrumb"] = "个性化  ›  开始菜单",
                ["material"] = "材质", ["preview"] = "材质预览", ["lightWeb"] = "浅色网页", ["darkWindow"] = "深色窗口",
                ["color"] = "颜色", ["preset"] = "雾蓝紫", ["share"] = "跟随主菜单背景", ["apply"] = "应用",
                ["copy"] = "复制样式到…", ["restore"] = "恢复此区域", ["settingsTitle"] = "设置",
                ["theme"] = "应用主题", ["themeDescription"] = "选择应用界面使用的颜色模式", ["themeMaterial"] = "主题材质",
                ["lightTintSettings"] = "浅色模式设置", ["darkTintSettings"] = "深色模式设置",
                ["themeTintColor"] = "主色调混合色", ["themeLuminosity"] = "亮度混合不透明度",
                ["colorTransparency"] = "颜色透明度", ["themeBlurStrength"] = "模糊强度",
                ["windowMinimize"] = "最小化", ["windowMaximize"] = "最大化或还原", ["windowClose"] = "关闭",
                ["runtimeInstall"] = "安装 .NET 10", ["runtimeInstalled"] = "已安装",
                ["runtimeDownloading"] = "正在下载 .NET 10…", ["runtimeInstalling"] = "正在安装 .NET 10…",
                ["runtimeFailed"] = "无法安装 .NET 10 桌面运行库，请重试。", ["runtimeCancelled"] = "已取消安装 .NET 10。",
                ["runtimeRestart"] = "请重启 Windows 完成 .NET 10 安装。",
                ["pickerTitle"] = "颜色选择器", ["pickerOriginal"] = "当前颜色", ["pickerNew"] = "新颜色",
                ["pickerConfirm"] = "确认", ["pickerCancel"] = "取消", ["pickerPlane"] = "饱和度和亮度色板",
                ["pickerRed"] = "红色 (R)", ["pickerGreen"] = "绿色 (G)", ["pickerBlue"] = "蓝色 (B)",
                ["pickerHue"] = "色相 (H)", ["pickerSaturation"] = "饱和度 (S)", ["pickerValue"] = "亮度 (V)", ["pickerAlpha"] = "不透明度",
                ["system"] = "跟随系统", ["light"] = "浅色", ["dark"] = "深色", ["language"] = "应用语言",
                ["languageDescription"] = "更改工具箱显示语言", ["about"] = "关于", ["author"] = "作者",
                ["repository"] = "工具箱 GitHub 仓库", ["version"] = "版本", ["open"] = "打开链接",
                ["settingsHint"] = "主题、语言和项目链接", ["systemTheme"] = "跟随 Windows 主题和强调色",
                ["original"] = "保持原样", ["solid"] = "纯色", ["transparent"] = "透明", ["acrylic"] = "亚克力", ["blur"] = "模糊", ["mica"] = "云母拟态",
                ["tint"] = "混合色不透明度", ["luminosity"] = "亮度混合", ["blurRadius"] = "模糊半径", ["opacity"] = "背景不透明度",
            },
            ["zh-HK"] = new Dictionary<string, string>
            {
                ["settings"] = "設定", ["appearance"] = "開始功能表", ["startMenu"] = "開始功能表", ["startWidgets"] = "開始功能表與小工具",
                ["search"] = "尋找設定", ["restoreAll"] = "全部還原", ["breadcrumb"] = "個人化  ›  開始功能表",
                ["material"] = "材質", ["preview"] = "材質預覽", ["lightWeb"] = "淺色網頁", ["darkWindow"] = "深色視窗",
                ["color"] = "顏色", ["preset"] = "霧藍紫", ["share"] = "跟隨主功能表背景", ["apply"] = "套用",
                ["copy"] = "複製樣式到…", ["restore"] = "還原此區域", ["settingsTitle"] = "設定",
                ["theme"] = "應用程式主題", ["themeDescription"] = "選擇應用程式介面使用的色彩模式", ["themeMaterial"] = "主題材質",
                ["lightTintSettings"] = "淺色模式設定", ["darkTintSettings"] = "深色模式設定",
                ["themeTintColor"] = "主色調混合色", ["themeLuminosity"] = "亮度混合不透明度",
                ["colorTransparency"] = "顏色透明度", ["themeBlurStrength"] = "模糊強度",
                ["windowMinimize"] = "最小化", ["windowMaximize"] = "最大化或還原", ["windowClose"] = "關閉",
                ["runtimeInstall"] = "安裝 .NET 10", ["runtimeInstalled"] = "已安裝",
                ["runtimeDownloading"] = "正在下載 .NET 10…", ["runtimeInstalling"] = "正在安裝 .NET 10…",
                ["runtimeFailed"] = "無法安裝 .NET 10 桌面執行階段，請重試。", ["runtimeCancelled"] = "已取消安裝 .NET 10。",
                ["runtimeRestart"] = "請重新啟動 Windows 以完成 .NET 10 安裝。",
                ["pickerTitle"] = "選色器", ["pickerOriginal"] = "目前顏色", ["pickerNew"] = "新顏色",
                ["pickerConfirm"] = "確認", ["pickerCancel"] = "取消", ["pickerPlane"] = "飽和度和亮度色板",
                ["pickerRed"] = "紅色 (R)", ["pickerGreen"] = "綠色 (G)", ["pickerBlue"] = "藍色 (B)",
                ["pickerHue"] = "色相 (H)", ["pickerSaturation"] = "飽和度 (S)", ["pickerValue"] = "亮度 (V)", ["pickerAlpha"] = "不透明度",
                ["system"] = "跟隨系統", ["light"] = "淺色", ["dark"] = "深色", ["language"] = "應用程式語言",
                ["languageDescription"] = "變更工具箱顯示語言", ["about"] = "關於", ["author"] = "作者",
                ["repository"] = "工具箱 GitHub 存放庫", ["version"] = "版本", ["open"] = "開啟連結",
                ["settingsHint"] = "主題、語言和專案連結", ["systemTheme"] = "跟隨 Windows 主題和強調色",
                ["original"] = "保持原樣", ["solid"] = "純色", ["transparent"] = "透明", ["acrylic"] = "壓克力", ["blur"] = "模糊", ["mica"] = "雲母擬態",
                ["tint"] = "混合色不透明度", ["luminosity"] = "亮度混合", ["blurRadius"] = "模糊半徑", ["opacity"] = "背景不透明度",
            },
            ["en-US"] = new Dictionary<string, string>
            {
                ["settings"] = "Settings", ["appearance"] = "Start menu", ["startMenu"] = "Start menu", ["startWidgets"] = "Start menu and widgets",
                ["search"] = "Find a setting", ["restoreAll"] = "Restore all", ["breadcrumb"] = "Personalization  ›  Start menu",
                ["material"] = "Material", ["preview"] = "Material preview", ["lightWeb"] = "Light page", ["darkWindow"] = "Dark window",
                ["color"] = "Color", ["preset"] = "Misty blue", ["share"] = "Use the main menu background", ["apply"] = "Apply",
                ["copy"] = "Copy style to…", ["restore"] = "Restore this area", ["settingsTitle"] = "Settings",
                ["theme"] = "App theme", ["themeDescription"] = "Choose the color mode used by the app", ["themeMaterial"] = "Theme material",
                ["lightTintSettings"] = "Light mode settings", ["darkTintSettings"] = "Dark mode settings",
                ["themeTintColor"] = "Tint color", ["themeLuminosity"] = "Luminosity opacity",
                ["colorTransparency"] = "Color transparency", ["themeBlurStrength"] = "Blur strength",
                ["windowMinimize"] = "Minimize", ["windowMaximize"] = "Maximize or restore", ["windowClose"] = "Close",
                ["runtimeInstall"] = "Install .NET 10", ["runtimeInstalled"] = "Installed",
                ["runtimeDownloading"] = "Downloading .NET 10…", ["runtimeInstalling"] = "Installing .NET 10…",
                ["runtimeFailed"] = "Could not install the .NET 10 Desktop Runtime. Try again.", ["runtimeCancelled"] = ".NET 10 installation was cancelled.",
                ["runtimeRestart"] = "Restart Windows to finish installing .NET 10.",
                ["pickerTitle"] = "Color picker", ["pickerOriginal"] = "Current color", ["pickerNew"] = "New color",
                ["pickerConfirm"] = "Confirm", ["pickerCancel"] = "Cancel", ["pickerPlane"] = "Saturation and value",
                ["pickerRed"] = "Red (R)", ["pickerGreen"] = "Green (G)", ["pickerBlue"] = "Blue (B)",
                ["pickerHue"] = "Hue (H)", ["pickerSaturation"] = "Saturation (S)", ["pickerValue"] = "Value (V)", ["pickerAlpha"] = "Opacity",
                ["system"] = "Use system setting", ["light"] = "Light", ["dark"] = "Dark", ["language"] = "App language",
                ["languageDescription"] = "Change the toolbox display language", ["about"] = "About", ["author"] = "Author",
                ["repository"] = "Toolbox GitHub repository", ["version"] = "Version", ["open"] = "Open link",
                ["settingsHint"] = "Theme, language, and project links", ["systemTheme"] = "Follow the Windows theme and accent color",
                ["original"] = "Keep original", ["solid"] = "Solid", ["transparent"] = "Transparent", ["acrylic"] = "Acrylic", ["blur"] = "Blur", ["mica"] = "Mica",
                ["tint"] = "Tint opacity", ["luminosity"] = "Luminosity", ["blurRadius"] = "Blur radius", ["opacity"] = "Background opacity",
            },
            ["ja-JP"] = new Dictionary<string, string>
            {
                ["settings"] = "設定", ["appearance"] = "スタートメニュー", ["startMenu"] = "スタートメニュー", ["startWidgets"] = "スタートメニューとウィジェット",
                ["search"] = "設定を検索", ["restoreAll"] = "すべて復元", ["breadcrumb"] = "個人設定  ›  スタートメニュー",
                ["material"] = "マテリアル", ["preview"] = "マテリアルのプレビュー", ["lightWeb"] = "ライトページ", ["darkWindow"] = "ダークウィンドウ",
                ["color"] = "色", ["preset"] = "ミスティブルー", ["share"] = "メインメニューの背景を使用", ["apply"] = "適用",
                ["copy"] = "スタイルをコピー…", ["restore"] = "この領域を復元", ["settingsTitle"] = "設定",
                ["theme"] = "アプリのテーマ", ["themeDescription"] = "アプリで使用するカラーモードを選択します", ["themeMaterial"] = "テーマのマテリアル",
                ["lightTintSettings"] = "ライトモード設定", ["darkTintSettings"] = "ダークモード設定",
                ["themeTintColor"] = "色合い", ["themeLuminosity"] = "輝度の不透明度",
                ["colorTransparency"] = "色の透明度", ["themeBlurStrength"] = "ぼかしの強さ",
                ["windowMinimize"] = "最小化", ["windowMaximize"] = "最大化または元に戻す", ["windowClose"] = "閉じる",
                ["runtimeInstall"] = ".NET 10 をインストール", ["runtimeInstalled"] = "インストール済み",
                ["runtimeDownloading"] = ".NET 10 をダウンロード中…", ["runtimeInstalling"] = ".NET 10 をインストール中…",
                ["runtimeFailed"] = ".NET 10 デスクトップ ランタイムをインストールできませんでした。もう一度お試しください。",
                ["runtimeCancelled"] = ".NET 10 のインストールをキャンセルしました。",
                ["runtimeRestart"] = "Windows を再起動して .NET 10 のインストールを完了してください。",
                ["pickerTitle"] = "カラー ピッカー", ["pickerOriginal"] = "現在の色", ["pickerNew"] = "新しい色",
                ["pickerConfirm"] = "確定", ["pickerCancel"] = "キャンセル", ["pickerPlane"] = "彩度と明度のパレット",
                ["pickerRed"] = "赤 (R)", ["pickerGreen"] = "緑 (G)", ["pickerBlue"] = "青 (B)",
                ["pickerHue"] = "色相 (H)", ["pickerSaturation"] = "彩度 (S)", ["pickerValue"] = "明度 (V)", ["pickerAlpha"] = "不透明度",
                ["system"] = "システムに合わせる", ["light"] = "ライト", ["dark"] = "ダーク", ["language"] = "アプリの言語",
                ["languageDescription"] = "ツールボックスの表示言語を変更します", ["about"] = "情報", ["author"] = "作者",
                ["repository"] = "ツールボックス GitHub リポジトリ", ["version"] = "バージョン", ["open"] = "リンクを開く",
                ["settingsHint"] = "テーマ、言語、プロジェクトリンク", ["systemTheme"] = "Windows のテーマとアクセントカラーに合わせる",
                ["original"] = "元のまま", ["solid"] = "単色", ["transparent"] = "透明", ["acrylic"] = "アクリル", ["blur"] = "ぼかし", ["mica"] = "マイカ",
                ["tint"] = "色の不透明度", ["luminosity"] = "明るさの混合", ["blurRadius"] = "ぼかし半径", ["opacity"] = "背景の不透明度",
            },
        };

    internal static string Normalize(string? language) =>
        Array.Find(SupportedLanguages, item => string.Equals(item, language, StringComparison.OrdinalIgnoreCase)) ?? "zh-CN";

    internal static string Get(string language, string key) =>
        Text.TryGetValue(Normalize(language), out var languageText) && languageText.TryGetValue(key, out var value)
            ? value : Text["zh-CN"].GetValueOrDefault(key, key);

    internal static string LanguageName(string language) => Normalize(language) switch
    {
        "zh-HK" => "繁體中文（香港）",
        "en-US" => "English",
        "ja-JP" => "日本語",
        _ => "简体中文",
    };

    internal static string MaterialName(string language, Material material) => material switch
    {
        Material.Original => Get(language, "original"), Material.Solid => Get(language, "solid"),
        Material.Transparent => Get(language, "transparent"), Material.Acrylic => Get(language, "acrylic"),
        Material.Blur => Get(language, "blur"), Material.Mica => Get(language, "mica"), _ => material.ToString(),
    };

    internal static string RegionLabel(string language, string id) => Normalize(language) switch
    {
        "zh-HK" => id switch { "main" => "主面板", "pinned" => "固定", "recommended" => "推薦", "apps" => "所有應用程式", "search" => "搜尋框", "footer" => "帳戶和電源列", "companion" => "右側伴侶面板", _ => id },
        "en-US" => id switch { "main" => "Main panel", "pinned" => "Pinned", "recommended" => "Recommended", "apps" => "All apps", "search" => "Search box", "footer" => "Account and power", "companion" => "Companion panel", _ => id },
        "ja-JP" => id switch { "main" => "メインパネル", "pinned" => "固定", "recommended" => "おすすめ", "apps" => "すべてのアプリ", "search" => "検索ボックス", "footer" => "アカウントと電源", "companion" => "右側コンパニオンパネル", _ => id },
        _ => Region.Get(id).Label,
    };
}
