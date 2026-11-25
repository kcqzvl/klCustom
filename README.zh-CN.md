# klCustom

[English](README.md) · **简体中文**

klCustom 是一款独立的 Windows x64 工具，用于自定义系统开始菜单的颜色、透明度和背景材质。

有任何问题或建议请提交 Issue。[GitHub Issues](https://github.com/kcqzvl/klCustom/issues)

## 开始使用

1. 下载 Windows x64 ZIP，将所有文件解压到当前用户可写的目录。[GitHub 发布页](https://github.com/kcqzvl/klCustom/releases)
2. 运行 `klCustom.exe`。自包含构建已附带启动工具箱所需的运行库。

## 自定义开始菜单

- 选择要调整的区域，预览效果后再应用。材质包括纯色、透明、亚克力、模糊和云母拟态。
- 调整主面板背景前，请先在编辑器中启用主面板自定义。
- 透明材质支持颜色、混合色不透明度和亮度混合。将两个滑块都设为 0% 即为完全透明。
- 可在区域编辑器恢复当前区域，或在设置中使用「全部恢复」还原所有区域并清除草稿。编辑草稿保存在本机，已应用的样式持续到开始菜单宿主重启。
- 在「设置」中可选择浅色、深色或跟随 Windows 应用模式。编辑器采用 Windows 强调色，支持简体中文、繁体中文（香港）、英文和日文。

## Windows 兼容性

x64 开始菜单组件会检查 Windows 构建号和开始菜单宿主版本。支持的构建系列可涵盖同系列的累积更新。

| Windows 版本 | 支持的构建号 | 开始菜单区域 |
| --- | --- | --- |
| Windows 10 21H2 LTSC、22H2 | 19044、19045 | 主面板、固定磁贴、所有应用、账户与电源栏 |
| Windows 11 | 22000、22621、22631、26100、26200、26300、28000 | 主面板、固定、推荐、所有应用、搜索框、账户与电源栏，以及系统提供的小组件面板 |

在 Windows 10 上，系统窗口材质不可用时，工具箱窗口的亚克力效果由模糊壁纸与主题叠色生成。

## 从源码构建

需要 .NET 10 SDK、支持 Windows x64 MinGW 目标的 Clang 工具链，以及 C++/WinRT 投影头文件。

```powershell
.\Build-Standalone.ps1 -ClangPath 'path\to\clang++.exe'
dotnet run --project tests/RuleChecks.csproj -c Release
```

构建后，`klCustom.exe` 和随附的开始菜单组件默认位于 `artifacts/klCustom/`。

构建时可用 `-SigningCertificateThumbprint` 指定代码签名证书的指纹，为 EXE 和两个 DLL 签名。用 `-TimestampServer` 为签名添加时间戳。使用自签名证书时需加上 `-AllowSelfSigned`。

应用负责检查宿主版本，并管理样式的应用与恢复。[宿主检测与样式管理](https://github.com/kcqzvl/klCustom/blob/main/NativeStartMenu.cs)

原生组件负责匹配菜单区域、绘制材质，并与应用使用相同的参数格式。[区域映射](https://github.com/kcqzvl/klCustom/blob/main/native/RegionTargets.h) · [材质实现](https://github.com/kcqzvl/klCustom/blob/main/native/Brushes.h) · [参数格式](https://github.com/kcqzvl/klCustom/blob/main/native/StyleProtocol.h)
