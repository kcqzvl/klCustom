# klCustom

**English** · [简体中文](README.zh-CN.md)

klCustom is a standalone Windows x64 app for customizing the colors, transparency, and background materials of the Windows Start menu.

For questions or suggestions, please open an issue. [GitHub Issues](https://github.com/kcqzvl/klCustom/issues)

## Getting started

1. Download the Windows x64 ZIP and extract all files to a folder you can write to. [GitHub Releases](https://github.com/kcqzvl/klCustom/releases)
2. Run `klCustom.exe`. Self-contained builds include the runtime needed to open the toolbox.
3. To install the system-wide .NET 10 Desktop Runtime, select **Install .NET 10** in the title bar. If it is already installed, the title bar shows **Installed**.

## Customizing the Start menu

- Select a region and preview its style before applying it. Materials include solid color, transparency, acrylic, blur, and a Mica approximation.
- Enable main panel customization in the editor before applying a style to its background.
- On systems with a right companion panel, its acrylic can use independent settings or follow the main menu background. Shared mode samples a narrow strip near the divider while Start is open and keeps the sample in memory.
- Use the restore controls to revert the selected region or all modified regions. The app saves drafts locally. Applied styles last until the Start menu host restarts.
- In Settings, choose a light or dark theme, or follow the Windows app mode. The editor uses the Windows accent color and supports Simplified Chinese, Traditional Chinese (Hong Kong), English, and Japanese.

## Windows compatibility

The x64 Start menu component checks both the Windows build and the Start menu host version. Supported build families can include cumulative updates.

| Windows | Supported builds | Start menu regions |
| --- | --- | --- |
| Windows 10 21H2 LTSC and 22H2 | 19044, 19045 | Main panel, pinned tiles, all apps, and the account and power area |
| Windows 11 | 22000, 22621, 22631, 26100, 26200, 26300, 28000 | Main panel, pinned apps, recommendations, all apps, search box, account and power area, and the right companion panel where available |

On Windows 10, the app window uses blurred wallpaper and a theme tint for acrylic when the system backdrop is unavailable.

## Build from source

Install the .NET 10 SDK, a Clang toolchain that targets Windows x64 with MinGW, and the C++/WinRT projection headers.

```powershell
.\Build-Standalone.ps1 -ClangPath 'path\to\clang++.exe'
dotnet run --project tests/RuleChecks.csproj -c Release
```

The build places `klCustom.exe` and its bundled Start menu component in `artifacts/klCustom/` by default.

Add `-SigningCertificateThumbprint` with your code-signing certificate’s thumbprint to sign the EXE and both DLLs. Use `-TimestampServer` to timestamp the signatures. Self-signed certificates require `-AllowSelfSigned`.

The app checks the host version and manages applying and restoring styles. [Host detection and style management](https://github.com/kcqzvl/klCustom/blob/main/NativeStartMenu.cs)

The native component maps menu regions, creates materials, and shares a parameter format with the app. [Region mappings](https://github.com/kcqzvl/klCustom/blob/main/native/RegionTargets.h) · [Materials](https://github.com/kcqzvl/klCustom/blob/main/native/Brushes.h) · [Parameter format](https://github.com/kcqzvl/klCustom/blob/main/native/StyleProtocol.h)
