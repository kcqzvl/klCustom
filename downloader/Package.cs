using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace KlCustom.Downloader
{
    internal static class Package
    {
        private const string Manifest = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json";

        internal static Task<string> PrepareAsync(string directory, bool includeRuntime)
        {
            return Task.Run(async () =>
            {
                if (string.IsNullOrWhiteSpace(directory))
                    throw new IOException("请选择保存位置。");

                directory = Path.GetFullPath(directory);
                Directory.CreateDirectory(directory);
                var version = typeof(Package).Assembly.GetName().Version.ToString(3);
                string target = Path.Combine(directory, "klCustom-" + version);
                if (Directory.Exists(target) || File.Exists(target))
                    throw new IOException("目标文件夹已存在，请选择其他保存位置。");

                string staging = Path.Combine(directory, ".klCustom-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                try
                {
                    using (var payload = typeof(Package).Assembly.GetManifestResourceStream("klCustom.payload.zip"))
                    using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
                        archive.ExtractToDirectory(staging);

                    if (includeRuntime)
                        await DownloadRuntimeAsync(staging).ConfigureAwait(false);

                    Directory.Move(staging, target);
                    return target;
                }
                finally
                {
                    if (Directory.Exists(staging))
                        Directory.Delete(staging, true);
                }
            });
        }

        internal static bool HasDesktopRuntime()
        {
            using (var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var registration = localMachine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64"))
            {
                var registeredRoot = registration == null ? null : registration.GetValue("InstallLocation") as string;
                if (registeredRoot != null && HasFramework(registeredRoot))
                    return true;
            }

            return HasFramework(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"));
        }

        private static bool HasFramework(string root)
        {
            try
            {
                return File.Exists(Path.Combine(root, "dotnet.exe")) &&
                    Directory.EnumerateDirectories(Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App"))
                        .Any(path =>
                        {
                            Version version;
                            return Version.TryParse(Path.GetFileName(path), out version) && version.Major == 10 &&
                                File.Exists(Path.Combine(path, "PresentationFramework.dll")) &&
                                File.Exists(Path.Combine(root, "shared", "Microsoft.NETCore.App", version.ToString(), "coreclr.dll"));
                        });
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static async Task DownloadRuntimeAsync(string directory)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(4) })
            {
                var json = await client.GetStringAsync(Manifest).ConfigureAwait(false);
                var metadata = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }
                    .Deserialize<ReleaseIndex>(json.Replace("\"windowsdesktop\"", "\"desktop\"")
                        .Replace("\"release-version\"", "\"releaseVersion\"")
                        .Replace("\"latest-release\"", "\"latestRelease\""));

                var release = metadata.releases.FirstOrDefault(item => item.releaseVersion == metadata.latestRelease);
                Version version;
                if (release == null || !Version.TryParse(release.releaseVersion, out version) || version.Major != 10)
                    throw new IOException("微软的发布信息中找不到 .NET 10。");

                string runtime = Path.Combine(directory, ".net");
                Directory.CreateDirectory(runtime);
                using (var core = await DownloadZipAsync(client, release.runtime, "dotnet-runtime", version, runtime).ConfigureAwait(false))
                    core.ExtractToDirectory(runtime);
                using (var desktop = await DownloadZipAsync(client, release.desktop, "windowsdesktop-runtime", version, runtime).ConfigureAwait(false))
                {
                    string framework = string.Format("shared/Microsoft.WindowsDesktop.App/{0}/", version);
                    var entries = desktop.Entries.Where(entry => entry.FullName.StartsWith(framework, StringComparison.Ordinal) &&
                        entry.Name.Length > 0).ToArray();
                    if (entries.Length == 0)
                        throw new IOException("下载的压缩包中找不到桌面运行库。");
                    foreach (var entry in entries)
                    {
                        string target = Path.GetFullPath(Path.Combine(runtime, entry.FullName));
                        if (!target.StartsWith(runtime + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("桌面运行库中的文件路径无效。");
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        entry.ExtractToFile(target);
                    }

                    foreach (var name in new[] { "LICENSE.txt", "ThirdPartyNotices.txt" })
                    {
                        var notice = desktop.GetEntry(name);
                        if (notice != null)
                            notice.ExtractToFile(Path.Combine(runtime, "WindowsDesktop-" + name));
                    }
                }

                if (!HasFramework(runtime) || !File.Exists(Path.Combine(runtime, "host", "fxr", version.ToString(), "hostfxr.dll")))
                    throw new IOException("下载的 .NET 桌面运行库不完整。");
            }
        }

        private static async Task<ZipArchive> DownloadZipAsync(HttpClient client, RuntimeComponent component,
            string name, Version version, string directory)
        {
            var file = component == null ? null : component.files.FirstOrDefault(item =>
                item.rid == "win-x64" && item.name == name + "-win-x64.zip");
            if (file == null || file.hash == null || file.hash.Length != 128)
                throw new IOException("微软的发布信息中找不到 x64 运行库压缩包。");

            var url = new Uri(file.url);
            string variant = name == "dotnet-runtime" ? "Runtime" : "WindowsDesktop";
            string expectedPath = string.Format("/dotnet/{0}/{1}/{2}-{1}-win-x64.zip", variant, version, name);
            if (url.Scheme != Uri.UriSchemeHttps || url.Host != "builds.dotnet.microsoft.com" ||
                !string.Equals(url.AbsolutePath, expectedPath, StringComparison.Ordinal))
                throw new IOException("微软运行库的下载地址格式发生了变化。");

            var stream = new FileStream(Path.Combine(directory, name + ".zip"), FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            try
            {
                using (var response = await client.GetAsync(url).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    await response.Content.CopyToAsync(stream).ConfigureAwait(false);
                }
                stream.Position = 0;
                using (var sha512 = SHA512.Create())
                {
                    var hash = BitConverter.ToString(sha512.ComputeHash(stream)).Replace("-", "");
                    if (!string.Equals(hash, file.hash, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("运行库校验失败，请重新下载。");
                }
                stream.Position = 0;
                return new ZipArchive(stream, ZipArchiveMode.Read);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        private sealed class ReleaseIndex
        {
            public string latestRelease { get; set; }
            public List<Release> releases { get; set; }
        }

        private sealed class Release
        {
            public string releaseVersion { get; set; }
            public RuntimeComponent runtime { get; set; }
            public RuntimeComponent desktop { get; set; }
        }

        private sealed class RuntimeComponent
        {
            public List<RuntimeFile> files { get; set; }
        }

        private sealed class RuntimeFile
        {
            public string rid { get; set; }
            public string name { get; set; }
            public string url { get; set; }
            public string hash { get; set; }
        }
    }
}
