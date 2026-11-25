using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace StartStyleToolbox;

internal sealed record Compatibility(bool CanApply, bool CanRestore, string Message,
    IReadOnlyDictionary<string, string>? Conflicts = null)
{
    internal string? Conflict(string id) => Conflicts?.GetValueOrDefault(id);
}
internal sealed record CommitResult(bool Success, string Message);

internal sealed class NativeStartMenu
{
    private const string HostName = "StartMenuExperienceHost";
    private const string HostPackageName = "Microsoft.Windows.StartMenuExperienceHost";
    private const string WindowsPublisherId = "cw5n1h2txyewy";
    private const string DllName = "kzlStartMenuHost5.dll";
    private const int HeaderSize = 32;
    private const int StyleSize = 36;
    private static readonly int LegacyMapSize = HeaderSize + StyleSize * Region.All.Length;
    private static readonly int MapSize = LegacyMapSize + sizeof(int);
    private readonly SemaphoreSlim commitLock = new(1, 1);
    private static string ComponentPath =>
        Path.Combine(Path.GetDirectoryName(typeof(NativeStartMenu).Assembly.Location)!, DllName);
    private static readonly string HostPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "SystemApps", $"{HostPackageName}_{WindowsPublisherId}", $"{HostName}.exe");
    private static readonly Version MissingFileVersion = new(0, 0, 0, 0);

    internal static bool UsesWindows10Layout => Environment.OSVersion.Version.Build < 22000;

    internal static bool SupportsBuild(int build) => build is 19044 or 19045 or 22000 or 22621 or 22631
        or 26100 or 26200 or 26300 or 28000;

    internal static bool SupportsHostVersion(int build, Version hostVersion)
    {
        // The system host can report the legacy 6.2 prefix with a modern build number.
        if (!SupportsBuild(build) || (hostVersion.Major, hostVersion.Minor) is not ((10, 0) or (6, 2)))
            return false;

        return hostVersion.Build == build || (build, hostVersion.Build) switch
        {
            (19044 or 19045, 19041) => true,
            (22631, 22621) => true,
            (26200 or 26300, 26100 or 26200) => true,
            _ => false,
        };
    }

    internal static bool HasRegion(string id, bool windows10) =>
        !windows10 || id is "main" or "pinned" or "apps" or "footer";

    internal static bool IsCompanionPanelVisible()
    {
        if (UsesWindows10Layout)
            return false;

        try
        {
            using var start = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Start");
            if (start?.GetValue("RightCompanionToggledOpen") is not 1)
                return false;

            using var companions = start.OpenSubKey("Companions");
            if (companions is null)
                return false;

            foreach (string name in companions.GetSubKeyNames())
            {
                using var companion = companions.OpenSubKey(name);
                if (companion?.GetValue("IsAvailable") is 1 && companion.GetValue("IsEnabled") is 1)
                    return true;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }

        return false;
    }

    internal Compatibility GetCompatibility()
    {
        using var host = GetSupportedHost(out string message);
        return host is null ? new(false, false, message) : ReadCompatibility(host);
    }

    private static Process? GetSupportedHost(out string message)
    {
        int build = Environment.OSVersion.Version.Build;
        if (RuntimeInformation.OSArchitecture != Architecture.X64 || !Environment.Is64BitProcess || !SupportsBuild(build))
        {
            message = $"此系统暂不支持开始菜单美化（{build}，{RuntimeInformation.OSArchitecture}）";
            return null;
        }
        if (!File.Exists(ComponentPath))
        {
            message = "缺少原生开始菜单组件";
            return null;
        }
        return FindHost(build, out message);
    }

    private static Compatibility ReadCompatibility(Process host)
    {
        var conflicts = CustomizationConflicts.Detect(host);
        using var map = OpenSession(host.Id);
        if (map is not null)
        {
            using var view = map.CreateViewAccessor(0, MapSize);
            view.Write(24, RegionMask(conflicts.Keys));
            int external = view.ReadInt32(28);
            for (int index = 0; index < Region.All.Length; index++)
                if ((external & 1 << index) != 0)
                    conflicts.TryAdd(Region.All[index].Id, "检测到外部背景修改，已暂停此区域");
        }
        return new(true, true, conflicts.Count > 0
            ? $"独立引擎已就绪 · {conflicts.Count} 个区域因冲突暂停"
            : "独立引擎已就绪", conflicts);
    }

    internal Dictionary<string, byte[]>? ReadAppliedStyles()
    {
        using var host = FindHost(Environment.OSVersion.Version.Build, out _);
        var styles = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (host is null)
            return styles;

        var buffer = new byte[StyleSize * Region.All.Length];
        for (int protocol = 5; protocol >= 2; protocol--)
        {
            using var map = OpenSession(host.Id, protocol);
            if (map is null)
                continue;
            using var view = map.CreateViewAccessor(0, LegacyMapSize, MemoryMappedFileAccess.Read);
            int revision = view.ReadInt32(4);
            if (revision < 0 || view.ReadInt32(8) != revision)
                return null;

            int mask = view.ReadInt32(16);
            if (mask != 0)
                view.ReadArray(HeaderSize, buffer, 0, buffer.Length);
            if (view.ReadInt32(8) != revision || view.ReadInt32(4) != revision)
                return null;

            for (int index = 0; index < Region.All.Length; index++)
            {
                string id = Region.All[index].Id;
                if ((mask & (1 << index)) != 0 && !styles.ContainsKey(id))
                    styles.Add(id, buffer.AsSpan(index * StyleSize, StyleSize).ToArray());
            }
        }
        return styles;
    }

    internal async Task<CommitResult> RequestCommitAsync(Dictionary<string, StyleOptions> requested, bool mainEnabled)
    {
        await commitLock.WaitAsync();
        try
        {
            return await Task.Run(() => Commit(requested, mainEnabled));
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException
            or UnauthorizedAccessException or Win32Exception or ExternalException)
        {
            return new(false, error.Message);
        }
        finally
        {
            commitLock.Release();
        }
    }

    private CommitResult Commit(Dictionary<string, StyleOptions> requested, bool mainEnabled)
    {
        foreach (var (id, options) in requested)
        {
            _ = Region.Get(id);
            StyleBrushes.Validate(options);
            if (!HasRegion(id, UsesWindows10Layout) && options.Material != Material.Original)
                return new(false, "当前开始菜单没有此区域");
            if (id == "main" && !mainEnabled && options.Material != Material.Original)
                return new(false, "请先启用主背景管理");
        }

        using var host = GetSupportedHost(out string message);
        if (host is null)
            return new(false, message);
        var compatibility = ReadCompatibility(host);
        foreach (var (id, options) in requested)
            if (options.Material != Material.Original && compatibility.Conflict(id) is { } reason)
                return new(false, reason);

        using var mutex = new Mutex(false, SessionName(host.Id, 2) + ".Updates");
        try
        {
            if (!mutex.WaitOne(0))
                return new(false, "另一个工具箱窗口正在更新开始菜单");
        }
        catch (AbandonedMutexException) { }
        try
        {
            return UpdateHost(host, requested);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static CommitResult UpdateHost(Process host, Dictionary<string, StyleOptions> requested)
    {
        var existing = OpenSession(host!.Id);
        using var map = existing ?? MemoryMappedFile.CreateNew(SessionName(host.Id), MapSize);
        using var view = map.CreateViewAccessor(0, MapSize);
        var conflicts = CustomizationConflicts.Detect(host);
        view.Write(24, RegionMask(conflicts.Keys));
        foreach (var (id, style) in requested)
            if (style.Material != Material.Original && conflicts.TryGetValue(id, out string? reason))
                return new(false, reason);
        int ready = view.ReadInt32(0);
        if (existing is null || ready != 1)
        {
            Connect(host, view);
            ready = view.ReadInt32(0);
            if (ready == 0)
                return new(false, "尚未找到开始菜单的 UI 线程。请先打开一次开始菜单再重试");
            if (ready != 1)
                return new(false, $"开始菜单未接受原生组件（0x{ready:X8}）");
        }

        var previous = new byte[StyleSize * Region.All.Length];
        view.ReadArray(HeaderSize, previous, 0, previous.Length);
        var future = (byte[])previous.Clone();
        int changing = 0;
        int applying = 0;
        var brushes = new StyleBrushes(WallpaperPalette.Read());
        foreach (var (id, options) in requested)
        {
            int index = Array.IndexOf(Region.All, Region.Get(id));
            changing |= 1 << index;
            if (options.Material != Material.Original)
                applying |= 1 << index;
            WriteStyle(future.AsSpan(index * StyleSize, StyleSize),
                id == "companion" ? options : options with { ShareMainBackdrop = false }, brushes);
        }

        using var legacy2 = OpenSession(host.Id, 2);
        using var legacy3 = OpenSession(host.Id, 3);
        using var legacy4 = OpenSession(host.Id, 4);
        using var view2 = legacy2?.CreateViewAccessor(0, LegacyMapSize);
        using var view3 = legacy3?.CreateViewAccessor(0, LegacyMapSize);
        using var view4 = legacy4?.CreateViewAccessor(0, MapSize);
        var released = new List<(MemoryMappedViewAccessor View, byte[] Styles)>();
        foreach (var legacyView in new[] { view2, view3, view4 })
        {
            if (legacyView is null || legacyView.ReadInt32(0) != 1)
                continue;
            var previousStyles = new byte[StyleSize * Region.All.Length];
            legacyView.ReadArray(HeaderSize, previousStyles, 0, previousStyles.Length);
            var remaining = (byte[])previousStyles.Clone();
            for (int i = 0; i < Region.All.Length; i++)
                if ((changing & 1 << i) != 0)
                    remaining.AsSpan(i * StyleSize, StyleSize).Clear();
            if (remaining.AsSpan().SequenceEqual(previousStyles))
                continue;
            released.Add((legacyView, previousStyles));
            int legacyRevision = Math.Abs(legacyView.ReadInt32(4)) + 1;
            Publish(legacyView, remaining, legacyRevision);
            if (WaitUntil(() => legacyView.ReadInt32(8) == legacyRevision)
                && (legacyView.ReadInt32(16) & changing) == 0)
                continue;
            RestoreLegacy(released);
            return new(false, "旧版未能释放该区域的背景");
        }

        int revision = Math.Max(0, view.ReadInt32(4)) + 1;
        Publish(view, future, revision);
        bool completed = WaitUntil(() => view.ReadInt32(8) == revision || host.HasExited);
        int matched = view.ReadInt32(12);
        int applied = view.ReadInt32(16);
        int failed = view.ReadInt32(20);
        if (!completed || host.HasExited || failed < 0 || (failed & changing) != 0
            || (matched & applying) != applying || (applied & changing) != applying)
        {
            Publish(view, previous, revision + 1);
            WaitUntil(() => view.ReadInt32(8) == revision + 1 || host.HasExited);
            RestoreLegacy(released);
            if ((view.ReadInt32(28) & applying) != 0)
                return new(false, "检测到外部背景修改，已暂停此区域");
            var latestConflicts = CustomizationConflicts.Detect(host);
            foreach (var (id, options) in requested)
                if (options.Material != Material.Original && latestConflicts.TryGetValue(id, out string? reason))
                    return new(false, reason);
            if (completed && (failed & changing) != 0)
                return new(false, "目标控件的画刷写入失败或已被其他程序修改");
            return new(false, "未找到目标控件，请打开开始菜单和对应区域后重试");
        }

        bool waitingForSample = requested.GetValueOrDefault("companion") is
            { Material: Material.Acrylic, ShareMainBackdrop: true } && view.ReadInt32(LegacyMapSize) == 0;
        return new(true, applying == 0 ? "已恢复原始背景"
            : waitingForSample ? "已启用统一取样，打开开始菜单后更新背景"
            : "已应用到原生开始菜单");
    }

    private static void RestoreLegacy(List<(MemoryMappedViewAccessor View, byte[] Styles)> released)
    {
        foreach (var (view, styles) in released)
            Publish(view, styles, Math.Abs(view.ReadInt32(4)) + 1);
    }

    internal static void WriteStyle(Span<byte> destination, StyleOptions style, StyleBrushes brushes)
    {
        static uint Argb(System.Windows.Media.Color color) =>
            ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;

        var values = MemoryMarshal.Cast<byte, uint>(destination);
        values.Clear();
        var color = StyleBrushes.ParseColor(style.Color);
        values[0] = (uint)style.Material;
        if (style.Material == Material.Transparent)
        {
            var transparentColor = StyleBrushes.TransparentColor(style, color);
            // The host already supports an alpha-blended solid brush. Use it for tinted transparency.
            if (transparentColor.A != 0)
            {
                values[0] = (uint)Material.Solid;
                color = transparentColor;
            }
        }
        values[1] = Argb(color);
        values[2] = (uint)style.Tint;
        values[3] = (uint)style.Luminosity;
        values[4] = (uint)style.Blur;
        values[5] = (uint)style.Opacity;
        if (style.Material == Material.Mica)
        {
            var colors = brushes.MicaColors(style);
            for (int i = 0; i < colors.Length; i++)
                values[6 + i] = Argb(colors[i]);
        }
        else if (style.Material == Material.Acrylic && style.ShareMainBackdrop)
        {
            values[6] = 1;
        }
    }

    internal static bool MatchesAppliedStyle(ReadOnlySpan<byte> applied, StyleOptions draft, StyleBrushes brushes)
    {
        Span<byte> encoded = stackalloc byte[StyleSize];
        WriteStyle(encoded, draft, brushes);
        var current = MemoryMarshal.Cast<byte, uint>(applied);
        var pending = MemoryMarshal.Cast<byte, uint>(encoded);
        if (current[0] != pending[0])
            return false;

        return (Material)pending[0] switch
        {
            Material.Original or Material.Transparent => true,
            Material.Solid => current[1] == pending[1],
            Material.Acrylic => current[1..4].SequenceEqual(pending[1..4]) && current[6] == pending[6],
            Material.Blur => current[1..3].SequenceEqual(pending[1..3]) && current[4] == pending[4],
            Material.Mica => current[5..].SequenceEqual(pending[5..]),
            _ => false,
        };
    }

    private static void Publish(MemoryMappedViewAccessor view, byte[] data, int revision)
    {
        view.Write(4, -revision);
        Thread.MemoryBarrier();
        view.WriteArray(HeaderSize, data, 0, data.Length);
        Thread.MemoryBarrier();
        view.Write(4, revision);
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(4))
        {
            if (condition())
                return true;
            Thread.Sleep(40);
        }
        return condition();
    }

    private static string SessionName(int id, int protocol = 5) => $"Local\\kzlSystemCustomize.StartMenu.{protocol}.{id}";

    private static MemoryMappedFile? OpenSession(int id, int protocol = 5)
    {
        try { return MemoryMappedFile.OpenExisting(SessionName(id, protocol), MemoryMappedFileRights.ReadWrite); }
        catch (FileNotFoundException) { return null; }
    }

    private static Process? FindHost(int build, out string message)
    {
        const uint queryLimitedInformation = 0x1000;
        using var current = Process.GetCurrentProcess();
        int sessionId = current.SessionId;
        var candidates = Process.GetProcessesByName(HostName);
        Process? host = null;
        bool unreadable = false;
        bool differentPath = false;
        message = "请打开一次开始菜单以启动系统宿主";
        try
        {
            foreach (var candidate in candidates)
            {
                try
                {
                    if (candidate.SessionId != sessionId)
                        continue;
                    using var process = OpenProcess(queryLimitedInformation, false, candidate.Id);
                    string? image = process.IsInvalid ? null : ReadProcessImage(process);
                    if (image is null)
                        unreadable = true;
                    else if (IsSystemHostPath(image))
                    {
                        var version = ReadHostVersion(FileVersionInfo.GetVersionInfo(HostPath), process);
                        if (version is null)
                        {
                            message = $"无法读取开始菜单宿主版本（Windows {build}）";
                            return null;
                        }
                        if (!SupportsHostVersion(build, version))
                        {
                            message = $"此系统的开始菜单宿主版本尚未适配（{build} / {version}）";
                            return null;
                        }
                        host = candidate;
                        return host;
                    }
                    else
                        differentPath = true;
                }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception
                    or IOException or UnauthorizedAccessException)
                {
                    unreadable = true;
                }
            }
            if (unreadable)
                message = "暂时无法读取开始菜单宿主，打开开始菜单后将自动重试";
            else if (differentPath)
                message = "开始菜单宿主路径与系统安装路径不一致";
            return null;
        }
        finally
        {
            foreach (var candidate in candidates)
                if (candidate != host)
                    candidate.Dispose();
        }
    }

    internal static bool IsSystemHostPath(string path)
    {
        const string extendedPrefix = @"\\?\";
        ReadOnlySpan<char> image = path.AsSpan();
        if (image.StartsWith(extendedPrefix, StringComparison.Ordinal))
            image = image[extendedPrefix.Length..];
        return image.Equals(HostPath.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadProcessImage(SafeProcessHandle process)
    {
        var image = new StringBuilder(1024);
        int length = image.Capacity;
        return QueryFullProcessImageName(process, 0, image, ref length) ? image.ToString() : null;
    }

    private static Version? ReadHostVersion(FileVersionInfo info, SafeProcessHandle process)
    {
        var version = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
        if (version != MissingFileVersion)
            return version;

        const int insufficientBuffer = 122;
        uint length = 0;
        if (GetPackageFullName(process, ref length, null) != insufficientBuffer || length == 0)
            return null;

        var fullName = new StringBuilder(checked((int)length));
        return GetPackageFullName(process, ref length, fullName) == 0
            ? ParseHostPackageVersion(fullName.ToString())
            : null;
    }

    internal static Version? ParseHostPackageVersion(string fullName)
    {
        // Package full name fields are name, version, architecture, resource ID and publisher ID.
        var fields = fullName.Split('_');
        if (fields.Length != 5 ||
            !fields[0].Equals(HostPackageName, StringComparison.OrdinalIgnoreCase) ||
            !fields[4].Equals(WindowsPublisherId, StringComparison.OrdinalIgnoreCase) ||
            !Version.TryParse(fields[1], out var version) || version.Revision < 0 || version == MissingFileVersion)
            return null;

        return version;
    }

    private static int RegionMask(IEnumerable<string> ids)
    {
        int mask = 0;
        foreach (var id in ids)
            mask |= 1 << Array.IndexOf(Region.All, Region.Get(id));
        return mask;
    }

    private static void EnsureComponentAccess(string path)
    {
        // Extracting a ZIP does not restore the component's Windows access rules.
        var file = new FileInfo(path);
        var access = file.GetAccessControl(AccessControlSections.Access);
        const FileSystemRights required = FileSystemRights.ReadAndExecute;
        var rules = access.GetAccessRules(true, true, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>();
        bool changed = false;
        foreach (string sid in new[] { "S-1-15-2-1", "S-1-15-2-2" })
        {
            var identity = new SecurityIdentifier(sid);
            if (rules.Any(rule => rule.IdentityReference.Equals(identity)
                && rule.AccessControlType == AccessControlType.Allow
                && (rule.FileSystemRights & required) == required))
                continue;
            access.AddAccessRule(new FileSystemAccessRule(identity, required, AccessControlType.Allow));
            changed = true;
        }
        if (changed)
            file.SetAccessControl(access);
    }

    private static void Connect(Process host, MemoryMappedViewAccessor session)
    {
        const int getMessageHook = 3;
        uint message = RegisterWindowMessage("kzlSystemCustomize.ConnectStartMenu.5");
        if (message == 0)
            throw new System.ComponentModel.Win32Exception();
        string component = ComponentPath;
        EnsureComponentAccess(component);
        IntPtr library = NativeLibrary.Load(component);
        try
        {
            IntPtr callback = NativeLibrary.GetExport(library, "ConnectStartMenu");
            foreach (ProcessThread thread in host.Threads)
            {
                if (session.ReadInt32(0) != 0)
                    break;
                using (thread)
                {
                    IntPtr hook = SetWindowsHookEx(getMessageHook, callback, library, (uint)thread.Id);
                    if (hook == IntPtr.Zero)
                        continue;
                    try
                    {
                        if (PostThreadMessage((uint)thread.Id, message, UIntPtr.Zero, IntPtr.Zero)
                            && SpinWait.SpinUntil(() => session.ReadInt32(0) != 0, 160))
                            return;
                    }
                    finally
                    {
                        UnhookWindowsHookEx(hook);
                    }
                }
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int id);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder image, ref int length);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetPackageFullName(SafeProcessHandle process, ref uint length, StringBuilder? fullName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr SetWindowsHookEx(int id, IntPtr callback, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);
}
