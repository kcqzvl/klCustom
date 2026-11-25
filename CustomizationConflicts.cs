using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace StartStyleToolbox;

internal static class CustomizationConflicts
{
    private const string Styler = "windows-11-start-menu-styler";
    private static readonly string[][] TargetNames =
    [
        ["MainMenu", "AcrylicBorder", "AcrylicOverlay", "BackgroundElement"],
        ["StartMenuPinnedList", "PinnedList", "Root", "TileGridView"],
        ["RecommendedList"],
        ["AllAppsGrid", "AppsList", "AllAppsGridListView"],
        ["StartMenuSearchBox", "BorderElement", "SearchBoxToggleButton"],
        ["NavPanePlaceholder", "NavigationPaneGrid"],
        ["CompanionRoot", "RightCompanion", "AcrylicBorder", "AcrylicOverlay"],
    ];

    internal static Dictionary<string, string> Detect(Process host)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] modules = host.Modules.Cast<ProcessModule>().Select(module => module.ModuleName).ToArray();
        if (modules.Contains("MToolExtend.dll", StringComparer.OrdinalIgnoreCase))
            result.Add("main", "主背景当前由 MToolBox 管理");

        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var config = machine.OpenSubKey(@"SOFTWARE\Windhawk\Engine\Mods\" + Styler);
        string? library = config?.GetValue("LibraryFileName") as string;
        bool stylerLoaded = modules.Any(name => name.StartsWith(Styler, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(library)
                && name.Equals(Path.GetFileName(library), StringComparison.OrdinalIgnoreCase)));
        if (!stylerLoaded)
            return result;

        const string source = "Windows 11 Start Menu Styler";
        using var settings = config?.OpenSubKey("Settings");
        if (settings is null || settings.GetValue("theme") is string theme && theme.Length > 0)
        {
            foreach (var region in Region.All)
                result.TryAdd(region.Id, $"检测到 {source} 主题，已暂停此区域");
            return result;
        }

        string[] keys = settings.GetValueNames();
        foreach (string key in keys.Where(key => key.StartsWith("controlStyles[", StringComparison.Ordinal)
            && key.EndsWith(".target", StringComparison.Ordinal)))
        {
            var target = settings.GetValue(key) as string;
            if (string.IsNullOrWhiteSpace(target) || target.StartsWith("//", StringComparison.Ordinal))
                continue;
            string prefix = key[..^6] + "styles[";
            bool editsBackground = keys.Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
                .Select(name => settings.GetValue(name) as string).Any(IsBackgroundStyle);
            if (!editsBackground)
                continue;
            int mask = MaskForSelector(target);
            for (int index = 0; index < Region.All.Length; index++)
                if ((mask & 1 << index) != 0)
                    result.TryAdd(Region.All[index].Id, $"此区域当前由 {source} 管理");
        }
        return result;
    }

    internal static int MaskForSelector(string selector)
    {
        var parts = selector.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split(new[] { '[', '@' }, 2)[0].Trim())
            .Select(part => part.Contains('#') ? part[(part.IndexOf('#') + 1)..] : part.Split('.')[^1])
            .ToArray();
        if (parts.Length == 0)
            return 0;
        if (parts.Contains("MainMenu", StringComparer.Ordinal) && TargetNames[0].Contains(parts[^1]))
            return 1;
        if (parts.Any(part => part is "CompanionRoot" or "RightCompanion") && TargetNames[6].Contains(parts[^1]))
            return 1 << 6;

        int mask = 0;
        for (int index = 0; index < TargetNames.Length; index++)
            if (TargetNames[index].Contains(parts[^1], StringComparer.Ordinal))
                mask |= 1 << index;
        if (mask == 0)
            mask = parts[^1] switch
            {
                "Border" => (1 << 0) | (1 << 4) | (1 << 6),
                "Grid" => (1 << 1) | (1 << 5),
                "GridView" or "ListView" or "AllAppsGridListView" => (1 << 2) | (1 << 3),
                "*" => (1 << Region.All.Length) - 1,
                _ => 0,
            };
        return mask;
    }

    private static bool IsBackgroundStyle(string? value)
    {
        if (value is null)
            return false;
        int assignment = value.IndexOf('=');
        if (assignment < 0)
            return false;
        string property = value[..assignment].Trim().Split('@', ':')[0];
        return property is "Background" or "Fill";
    }
}
