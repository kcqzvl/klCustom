using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace StartStyleToolbox;

internal static class DesktopRuntime
{
    internal const string DisplayName = ".NET 10 Desktop Runtime (x64)";
    private const string DesktopFramework = "Microsoft.WindowsDesktop.App";
    private const string CoreFramework = "Microsoft.NETCore.App";

    internal static bool IsInstalled()
    {
        using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var registration = localMachine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64");
        if (registration?.GetValue("InstallLocation") is string registeredRoot && HasRuntime(registeredRoot))
            return true;

        return HasRuntime(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"));
    }

    internal static bool HasRuntime(string root) =>
        Path.IsPathFullyQualified(root) && File.Exists(Path.Combine(root, "dotnet.exe")) &&
        HasFramework(root, DesktopFramework, "PresentationFramework.dll") &&
        HasFramework(root, CoreFramework, "coreclr.dll");

    private static bool HasFramework(string root, string framework, string assembly)
    {
        try
        {
            return Directory.EnumerateDirectories(Path.Combine(root, "shared", framework)).Any(folder =>
                Version.TryParse(Path.GetFileName(folder), out var version) && version.Major == 10 &&
                File.Exists(Path.Combine(folder, assembly)) &&
                File.Exists(Path.Combine(folder, framework + ".deps.json")));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
