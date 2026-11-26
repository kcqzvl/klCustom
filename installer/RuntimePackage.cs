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

namespace KlCustom.Installer
{
    internal static class RuntimePackage
    {
        private const string Manifest = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json";
        private const string OwnerFile = ".klcustom-runtime";
        private const string Owner = "kl Custom MSI private runtime";

        internal static void Ensure(string directory, string productCode)
        {
            string runtime = Path.Combine(directory, ".net");
            if (HasFramework(runtime))
                return;

            if (Directory.Exists(runtime) || File.Exists(runtime))
                throw new IOException("The existing .net folder does not contain a compatible desktop runtime.");

            if (!HasSystemRuntime())
                Provision(directory, productCode);
        }

        internal static void Provision(string directory, string productCode)
        {
            string staging = Path.Combine(directory, ".klcustom-runtime-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                DownloadRuntimeAsync(staging).GetAwaiter().GetResult();
                if (productCode != null)
                {
                    File.WriteAllText(Path.Combine(staging, OwnerFile), Owner);
                    File.WriteAllText(PendingFile(staging, productCode), productCode);
                }

                Directory.Move(staging, Path.Combine(directory, ".net"));
            }
            finally
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
            }
        }

        internal static void Commit(string directory, string productCode)
        {
            string runtime = Path.Combine(directory, ".net");
            if (IsOwned(runtime))
                File.Delete(PendingFile(runtime, productCode));
        }

        internal static void Remove(string directory, string productCode)
        {
            string runtime = Path.Combine(directory, ".net");
            if (!IsOwned(runtime) || (productCode != null && !File.Exists(PendingFile(runtime, productCode))))
                return;

            Directory.Delete(runtime, true);
            if (productCode == null && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }

        private static string PendingFile(string runtime, string productCode)
        {
            return Path.Combine(runtime, ".pending-" + Guid.Parse(productCode).ToString("N"));
        }

        private static bool IsOwned(string runtime)
        {
            string marker = Path.Combine(runtime, OwnerFile);
            return Directory.Exists(runtime) &&
                (File.GetAttributes(runtime) & FileAttributes.ReparsePoint) == 0 &&
                File.Exists(marker) && File.ReadAllText(marker) == Owner;
        }

        private static bool HasSystemRuntime()
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
                if (!Path.IsPathRooted(root) || !File.Exists(Path.Combine(root, "dotnet.exe")) ||
                    !Directory.EnumerateDirectories(Path.Combine(root, "host", "fxr"))
                        .Any(path =>
                        {
                            Version version;
                            return Version.TryParse(Path.GetFileName(path), out version) && version.Major >= 10 &&
                                File.Exists(Path.Combine(path, "hostfxr.dll"));
                        }))
                    return false;

                var core = FrameworkVersions(root, "Microsoft.NETCore.App", "coreclr.dll").ToArray();
                return FrameworkVersions(root, "Microsoft.WindowsDesktop.App", "PresentationFramework.dll")
                    .Any(desktop => core.Any(version => version >= desktop));
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

        private static IEnumerable<Version> FrameworkVersions(string root, string framework, string requiredFile)
        {
            foreach (string path in Directory.EnumerateDirectories(Path.Combine(root, "shared", framework)))
            {
                Version version;
                if (Version.TryParse(Path.GetFileName(path), out version) && version.Major == 10 &&
                    version.Minor == 0 && version.Build >= 0 && File.Exists(Path.Combine(path, requiredFile)) &&
                    File.Exists(Path.Combine(path, framework + ".deps.json")) &&
                    File.Exists(Path.Combine(path, framework + ".runtimeconfig.json")))
                    yield return version;
            }
        }

        private static async Task DownloadRuntimeAsync(string runtime)
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
