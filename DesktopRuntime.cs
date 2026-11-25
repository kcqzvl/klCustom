using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace StartStyleToolbox;

internal static class DesktopRuntime
{
    internal const string DisplayName = ".NET 10 Desktop Runtime (x64)";
    private const string DesktopFramework = "Microsoft.WindowsDesktop.App";
    private const string CoreFramework = "Microsoft.NETCore.App";
    internal const string InstallerName = "windowsdesktop-runtime-win-x64.exe";
    private const string DownloadHost = "builds.dotnet.microsoft.com";
    private static readonly HttpClient Downloads = new(new HttpClientHandler { AllowAutoRedirect = false });

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

    internal static async Task DownloadInstallerAsync(string destination, CancellationToken cancellationToken)
    {
        using var metadataStream = await Downloads.GetStreamAsync(
            $"https://{DownloadHost}/dotnet/release-metadata/10.0/releases.json", cancellationToken).ConfigureAwait(false);
        using var metadata = await JsonDocument.ParseAsync(metadataStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        string? latest = metadata.RootElement.GetProperty("latest-runtime").GetString();
        if (!Version.TryParse(latest, out var version) || version.Major != 10)
            throw new IOException("Microsoft did not list a stable .NET 10 runtime.");

        foreach (var release in metadata.RootElement.GetProperty("releases").EnumerateArray())
        {
            if (!release.TryGetProperty("windowsdesktop", out var desktop) || desktop.GetProperty("version").GetString() != latest)
                continue;

            foreach (var file in desktop.GetProperty("files").EnumerateArray())
            {
                if (file.GetProperty("name").GetString() != InstallerName || file.GetProperty("rid").GetString() != "win-x64")
                    continue;

                if (!Uri.TryCreate(file.GetProperty("url").GetString(), UriKind.Absolute, out var download) ||
                    download.Scheme != Uri.UriSchemeHttps || download.Host != DownloadHost)
                    throw new IOException("Microsoft listed an unexpected .NET installer URL.");

                string? expectedHash = file.GetProperty("hash").GetString();
                await using (var source = await Downloads.GetStreamAsync(download, cancellationToken).ConfigureAwait(false))
                await using (var output = File.Create(destination))
                    await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);

                await using var installer = File.OpenRead(destination);
                var hash = await SHA512.HashDataAsync(installer, cancellationToken).ConfigureAwait(false);
                if (!Convert.ToHexString(hash).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The .NET installer checksum did not match Microsoft’s release metadata.");

                return;
            }
        }

        throw new IOException("The .NET 10 Desktop Runtime installer for Windows x64 was not found.");
    }
}
