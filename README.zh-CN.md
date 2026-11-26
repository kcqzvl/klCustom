# kl Custom

[English](README.md) · **简体中文**

kl Custom 是一款独立的 Windows 11 x64 工具，可分别自定义开始菜单各区域的颜色、透明度和背景材质。

有任何问题或建议请提交 Issue。[GitHub Issues](https://github.com/kcqzvl/klCustom/issues)

## 开始使用

- **7z 解压版**。解压完整的工具箱文件夹后运行 `klCustom.exe`。包内的 `.net` 文件夹已包含 .NET 10 桌面运行库。
- **MSI 安装版**。运行 `klCustom-<版本>-installer-x64.msi` 并选择安装目录。检测到兼容的 x64 .NET 10 桌面运行库时跳过下载。缺少时，安装程序需要联网从微软自动下载并校验运行库，然后解压到与 `klCustom.exe` 同级的 `.net` 子目录。

[GitHub 发布页](https://github.com/kcqzvl/klCustom/releases)

## 自定义开始菜单

- 可分别调整主面板、固定、推荐、所有应用、搜索框及账户和电源栏。系统提供小组件面板时，编辑器和预览会显示该区域。
- 材质包括纯色、透明、亚克力、模糊和云母拟态。可调整颜色和材质参数，点击百分比直接输入数值，并在浅色网页或深色窗口场景中预览。透明材质的两个滑块都设为 0% 时，背景完全透明。
- 应用主面板背景前，请先启用「由本工具管理主背景」。检测到美化冲突时，工具箱会暂停应用受影响区域的样式。
- 可将样式复制到其他区域、恢复单个区域，或在设置中选择「全部恢复」。编辑草稿保存在本机，已应用的样式持续到开始菜单宿主重启。

## 应用外观与语言

- 在设置中选择浅色、深色或跟随系统。界面使用 Windows 强调色，窗口材质可设为透明、模糊或亚克力，浅色与深色模式的混合色可分别设置。
- 界面支持简体中文、繁体中文（香港）、英文和日文。「设置 → 关于」显示当前版本，并提供 GitHub 和 bilibili 链接。

## Windows 11 兼容性

开始菜单组件面向 Windows 11 x64 的 `22000`、`22621`、`22631`、`26100`、`26200`、`26300` 和 `28000` 构建。应用样式前，工具箱会检查 Windows 构建号和开始菜单宿主版本。

## 从源码构建

需要 .NET 10 SDK、支持 Windows x64 MinGW 目标的 Clang 工具链，以及 C++/WinRT 投影头文件。

```powershell
.\Build-Standalone.ps1 -ClangPath 'path\to\clang++.exe'
dotnet run --project tests/RuleChecks.csproj -c Release
```

构建后，`klCustom.exe` 和随附的开始菜单组件默认位于 `artifacts/klCustom/`。

打包 MSI 和 7z 前，将 WiX 5.0.2 及其界面扩展安装到同一工具目录。

```powershell
dotnet tool install wix --version 5.0.2 --tool-path artifacts/wix
Push-Location artifacts/wix
.\wix.exe extension add WixToolset.UI.wixext/5.0.2
Pop-Location
.\Build-Release.ps1 -WixPath ./artifacts/wix/wix.exe -NativeDllPath ./artifacts/klCustom/kzlStartMenuHost5.dll -SevenZipPath 'path\to\7z.exe' -OutputDirectory ./artifacts/release
```

构建时可用 `-SigningCertificateThumbprint` 指定代码签名证书的指纹，为应用文件和安装包签名。用 `-TimestampServer` 为签名添加时间戳。使用自签名证书时需加上 `-AllowSelfSigned`。

应用负责检查宿主版本，并管理样式的应用与恢复。[宿主检测与样式管理](https://github.com/kcqzvl/klCustom/blob/main/NativeStartMenu.cs)

原生组件负责匹配菜单区域、绘制材质，并与应用使用相同的参数格式。[区域映射](https://github.com/kcqzvl/klCustom/blob/main/native/RegionTargets.h) · [材质实现](https://github.com/kcqzvl/klCustom/blob/main/native/Brushes.h) · [参数格式](https://github.com/kcqzvl/klCustom/blob/main/native/StyleProtocol.h)
