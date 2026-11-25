# kzl Custom

将发布 ZIP 完整解压到当前用户可写的目录，然后运行 `kzl Custom.exe`。从源码构建时，默认可执行文件位于 `artifacts/kzl Custom/`。程序需要 [.NET 10 Desktop Runtime（Windows x64）](https://dotnet.microsoft.com/download/dotnet/10.0)。

编辑器使用 Windows 的应用模式和强调色，并沿用已有的颜色与材质草稿。

独立版通过随程序发布的 `kzlStartMenuHost5.dll` 在开始菜单的 UI 线程读取 XAML 根节点。它接收固定格式的区域和材质参数，设置目标控件的背景，恢复时还原原有画刷。应用和恢复均等待宿主确认。

右侧伴侣面板选择「亚克力」后，可启用「跟随主菜单背景」。菜单打开时，工具箱读取靠近分隔线的主菜单背景窄条，以其纵向颜色生成右侧背景。取样数据只在内存中使用。关闭此选项可继续使用右侧自己的亚克力参数。

支持主面板、固定、推荐、所有应用、搜索框、账户与电源栏以及右侧伴侣面板。主面板需要在程序中显式启用。材质包括纯色、透明、亚克力、模糊和云母拟态。

当前适配预览按 Windows 正式版构建系列和系统宿主文件版本识别，兼容同一构建系列的累积更新。开始菜单原生组件适用于 x64。

- Windows 10 22H2（19045）及 21H2 LTSC（19044）使用磁贴布局，显示主面板、固定磁贴、所有应用和账户与电源栏。工具箱自身的亚克力在系统窗口材质不可用时使用模糊壁纸和主题叠色。
- Windows 11 保留原有区域。正式版构建范围为 22000、22621、22631、26100、26200、26300 和 28000。右侧伴侣面板需由当前系统实际提供。各版本还需满足 .NET 10 Desktop Runtime 的系统要求。

MToolBox 模块加载时，主背景默认归 MToolBox 管理，右侧伴侣面板及其他区域仍可单独应用。工具箱也会识别已加载的 Windows 11 Start Menu Styler 背景规则，并暂停受影响的区域。主题无法逐项确定范围时会暂停全部区域。检测到其他程序在应用后替换画刷时，相应区域会暂停至开始菜单宿主重启。

暂停期间可继续编辑预览和草稿。设置草稿保存在本机，已应用的画刷持续到开始菜单宿主重启。

## 构建

需要 .NET 10、支持 Windows x64 的 Clang 工具链和 C++/WinRT 投影头文件。

```powershell
.\Build-Standalone.ps1 -ClangPath 'path\to\clang++.exe'
dotnet run --project tests/RuleChecks.csproj -c Release
```

在 MToolBox 模块已加载的本机环境下，可以运行 `tests/Test-StartMenuCoexistence.ps1` 验证主背景拦截和右侧伴侣面板各材质的应用与恢复。测试会临时修改右侧面板，结束后恢复。

`NativeStartMenu.cs` 管理目标版本、连接、设置提交与回滚。`native/RegionTargets.h` 保存控件映射，`native/Brushes.h` 实现材质，`native/StyleProtocol.h` 定义两端共享的参数布局。
