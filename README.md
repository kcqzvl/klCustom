# kl Custom

**English** · [简体中文](README.zh-CN.md)

kl Custom is a standalone Windows 11 x64 app for customizing the colors, transparency, and materials of individual Start menu areas.

For questions or suggestions, please open an issue. [GitHub Issues](https://github.com/kcqzvl/klCustom/issues)

## Getting started

- **7z portable package**. Extract the entire app folder and run `klCustom.exe`. The package includes the .NET 10 Desktop Runtime in the `.net` folder alongside the executable.
- **EXE installer**. Run `klCustom-<version>-installer-x64.exe`, choose a destination, and choose whether to download the x64 .NET 10 Desktop Runtime from Microsoft. The installer puts the app in its own folder. When selected, the runtime goes into that folder's `.net` subfolder. With the option cleared, the app uses the .NET 10 Desktop Runtime installed in Windows.

[GitHub Releases](https://github.com/kcqzvl/klCustom/releases)

## Customizing the Start menu

- Customize the main panel, pinned apps, recommendations, all apps, search box, and account and power area. The widgets panel appears in the editor and preview when Windows provides it.
- Choose solid color, transparency, acrylic, blur, or a Mica approximation. Adjust the color and material controls, click a percentage to enter a value, and preview the result against a light webpage or dark window. Set both sliders for transparent material to zero for a fully clear background.
- Enable main background management in the editor before applying a style to that area. If the app detects a styling conflict, it pauses changes for the affected area.
- Copy a style to another region, restore an individual region, or use **Restore all** in Settings. Drafts are saved locally. Applied styles last until the Start menu host restarts.

## App appearance and language

- Choose Light, Dark, or Follow system in Settings. The interface uses the Windows accent color. Transparent, blur, and acrylic window materials are available with separate tint settings for light and dark modes.
- The interface supports Simplified Chinese, Traditional Chinese (Hong Kong), English, and Japanese. **Settings → About** shows the app version and links to GitHub and bilibili.

## Windows 11 compatibility

The Start menu component targets Windows 11 x64 builds `22000`, `22621`, `22631`, `26100`, `26200`, `26300`, and `28000`. The app checks both the Windows build and the Start menu host version before applying changes.

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
