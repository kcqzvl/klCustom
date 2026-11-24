param(
    [Parameter(Mandatory)]
    [string]$ClangPath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\kzlSystemCustomize-shared-backdrop')
)

$ErrorActionPreference = 'Stop'
$clang = (Resolve-Path -LiteralPath $ClangPath).Path
$release = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName

$native = Join-Path $PSScriptRoot 'native\StartMenuNative.cpp'
$output = Join-Path $release 'kzlStartMenuHost5.dll'
& $clang --target=x86_64-w64-mingw32 -std=c++23 -O2 -shared -static -DUNICODE -D_UNICODE $native -lole32 -loleaut32 -lruntimeobject -ldxguid -lgdi32 -o $output
if ($LASTEXITCODE) { throw "Native build failed ($LASTEXITCODE)" }

# Packaged Windows XAML hosts need read and execute access to the diagnostic component.
icacls.exe $output /grant '*S-1-15-2-1:(RX)' '*S-1-15-2-2:(RX)' | Out-Null
if ($LASTEXITCODE) { throw "Could not set read and execute access on $output" }

$project = Join-Path $PSScriptRoot 'StartStyleToolbox.csproj'
dotnet publish $project -c Release -r win-x64 --self-contained false -p:NuGetAudit=false --disable-build-servers -p:UseSharedCompilation=false -o $release
if ($LASTEXITCODE) { throw "Desktop build failed ($LASTEXITCODE)" }
