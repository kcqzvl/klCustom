using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

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
    private const string DllName = "kzlStartMenuHost5.dll";
    private const int HeaderSize = 32;
    private const int StyleSize = 36;
    private static readonly int LegacyMapSize = HeaderSize + StyleSize * Region.All.Length;
    private static readonly int MapSize = LegacyMapSize + sizeof(int);
    private readonly SemaphoreSlim commitLock = new(1, 1);
    private static string ComponentPath =>
        Path.Combine(Path.GetDirectoryName(typeof(NativeStartMenu).Assembly.Location)!, DllName);

    internal Compatibility GetCompatibility()
    {
        using var host = GetHost();
        if (!File.Exists(ComponentPath))
            return new(false, false, "缺少原生开始菜单组件");
        if (!IsSupportedHost(host))
            return new(false, false, "当前只适配 Windows 11 25H2（26200.9974）");
        var conflicts = CustomizationConflicts.Detect(host!);
        using var map = OpenSession(host!.Id);
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

    internal HashSet<string> AppliedRegions()
    {
        using var host = GetHost();
        if (host is null)
            return [];
        using var map = OpenSession(host.Id);
        using var legacy2 = OpenSession(host.Id, 2);
        using var legacy3 = OpenSession(host.Id, 3);
        using var legacy4 = OpenSession(host.Id, 4);
        using var view = map?.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.Read);
        using var view2 = legacy2?.CreateViewAccessor(0, LegacyMapSize, MemoryMappedFileAccess.Read);
        using var view3 = legacy3?.CreateViewAccessor(0, LegacyMapSize, MemoryMappedFileAccess.Read);
        using var view4 = legacy4?.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.Read);
        int applied = (view?.ReadInt32(16) ?? 0) | (view2?.ReadInt32(16) ?? 0)
            | (view3?.ReadInt32(16) ?? 0) | (view4?.ReadInt32(16) ?? 0);
        return Region.All.Where((_, index) => (applied & (1 << index)) != 0)
            .Select(region => region.Id).ToHashSet(StringComparer.Ordinal);
    }

    internal async Task<CommitResult> RequestCommitAsync(Dictionary<string, StyleOptions> requested, bool mainEnabled)
    {
        await commitLock.WaitAsync();
        try
        {
            return await Task.Run(() => Commit(requested, mainEnabled));
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException
            or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ExternalException)
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
            if (id == "main" && !mainEnabled && options.Material != Material.Original)
                return new(false, "请先启用主背景管理");
        }

        var compatibility = GetCompatibility();
        if (!compatibility.CanApply)
            return new(false, compatibility.Message);
        foreach (var (id, options) in requested)
            if (options.Material != Material.Original && compatibility.Conflict(id) is { } reason)
                return new(false, reason);
        using var host = GetHost();
        if (!IsSupportedHost(host))
            return new(false, "开始菜单宿主已变化，请重新检测");

        using var mutex = new Mutex(false, SessionName(host!.Id, 2) + ".Updates");
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
        values[0] = (uint)style.Material;
        values[1] = Argb(StyleBrushes.ParseColor(style.Color));
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

    private static Process? GetHost()
    {
        using var current = Process.GetCurrentProcess();
        var candidates = Process.GetProcessesByName(HostName);
        var host = candidates.FirstOrDefault(process => process.SessionId == current.SessionId);
        foreach (var process in candidates)
            if (process != host)
                process.Dispose();
        return host;
    }

    private static bool IsSupportedHost(Process? host)
    {
        if (host is null || !Environment.Is64BitProcess)
            return false;
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var version = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            @"SystemApps\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\StartMenuExperienceHost.exe");
        return version?.GetValue("CurrentBuild") as string == "26200" && version.GetValue("UBR") is 9974
            && string.Equals(host.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase)
            && FileVersionInfo.GetVersionInfo(expected).FileVersion?.StartsWith("10.0.26100.9818", StringComparison.Ordinal) == true;
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
