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
                var file = release == null || release.desktop == null ? null :
                    release.desktop.files.FirstOrDefault(item => item.rid == "win-x64" &&
                        item.name == "windowsdesktop-runtime-win-x64.exe");
                if (file == null || file.hash == null || file.hash.Length != 128)
                    throw new IOException("微软的发布信息中找不到 x64 桌面运行库。");

                var url = new Uri(file.url);
                if (url.Scheme != Uri.UriSchemeHttps || url.Host != "builds.dotnet.microsoft.com" ||
                    !url.AbsolutePath.StartsWith("/dotnet/WindowsDesktop/10.", StringComparison.Ordinal) ||
                    !url.AbsolutePath.EndsWith("-win-x64.exe", StringComparison.Ordinal))
                    throw new IOException("微软运行库的下载地址格式发生了变化。");

                var bytes = await client.GetByteArrayAsync(url).ConfigureAwait(false);
                using (var sha512 = SHA512.Create())
                {
                    var hash = BitConverter.ToString(sha512.ComputeHash(bytes)).Replace("-", "");
                    if (!string.Equals(hash, file.hash, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("运行库校验失败，请重新下载。");
                }

                File.WriteAllBytes(Path.Combine(directory, Path.GetFileName(url.AbsolutePath)), bytes);
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
            public Desktop desktop { get; set; }
        }

        private sealed class Desktop
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
