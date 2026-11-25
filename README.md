# klCustom

**English** · [简体中文](README.zh-CN.md)

klCustom is a standalone Windows x64 app for customizing the colors, transparency, and background materials of the Windows Start menu.

For questions or suggestions, please open an issue. [GitHub Issues](https://github.com/kcqzvl/klCustom/issues)

## Getting started

- **7z portable package**. Extract the app folder and run `klCustom.exe`. The app loads its .NET 10 Desktop Runtime from the `.net` folder alongside the executable.
- **EXE downloader**. Choose a destination and whether to download the official x64 .NET 10 Desktop Runtime. With the option selected, the app can run directly from the new folder, with its runtime in `.net`. With the option cleared, it uses the runtime installed in Windows.

[GitHub Releases](https://github.com/kcqzvl/klCustom/releases)

## Customizing the Start menu

- Select a region and preview its style before applying it. Materials include solid color, transparency, acrylic, blur, and a Mica approximation.
- Enable main panel customization in the editor before applying a style to its background.
- Transparent material supports color, tint opacity, and luminosity. Set both sliders to zero for a fully clear background.
- Restore a single region from its editor, or use **Restore all** in Settings to restore all regions and clear drafts. The app saves drafts locally. Applied styles last until the Start menu host restarts.
- In Settings, choose a light or dark theme, or follow the Windows app mode. The editor uses the Windows accent color and supports Simplified Chinese, Traditional Chinese (Hong Kong), English, and Japanese.

## Windows compatibility

The x64 Start menu component checks both the Windows build and the Start menu host version. Supported build families can include cumulative updates.

| Windows | Supported builds | Start menu regions |
| --- | --- | --- |
| Windows 10 21H2 LTSC and 22H2 | 19044, 19045 | Main panel, pinned tiles, all apps, and the account and power area |
| Windows 11 | 22000, 22621, 22631, 26100, 26200, 26300, 28000 | Main panel, pinned apps, recommendations, all apps, search box, account and power area, and the widgets panel where available |

On Windows 10, the app window uses blurred wallpaper and a theme tint for acrylic when the system backdrop is unavailable.

## Build from source

Install the .NET 10 SDK, a Clang toolchain that targets Windows x64 with MinGW, and the C++/WinRT projection headers.

```powershell
.\Build-Standalone.ps1 -ClangPath 'path\to\clang++.exe'
dotnet run --project tests/RuleChecks.csproj -c Release
```

The build places `klCustom.exe` and its bundled Start menu component in `artifacts/klCustom/` by default.

To package both release downloads, run `Build-Release.ps1` with `-NativeDllPath`, `-SevenZipPath`, and a new `-OutputDirectory`. Use the native DLL from the standalone build.

Add `-SigningCertificateThumbprint` with your code-signing certificate’s thumbprint to sign the EXE and both DLLs. Use `-TimestampServer` to timestamp the signatures. Self-signed certificates require `-AllowSelfSigned`.

The app checks the host version and manages applying and restoring styles. [Host detection and style management](https://github.com/kcqzvl/klCustom/blob/main/NativeStartMenu.cs)

The native component maps menu regions, creates materials, and shares a parameter format with the app. [Region mappings](https://github.com/kcqzvl/klCustom/blob/main/native/RegionTargets.h) · [Materials](https://github.com/kcqzvl/klCustom/blob/main/native/Brushes.h) · [Parameter format](https://github.com/kcqzvl/klCustom/blob/main/native/StyleProtocol.h)
