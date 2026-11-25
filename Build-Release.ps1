param(
    [Parameter(Mandatory)]
    [string]$NativeDllPath,
    [Parameter(Mandatory)]
    [string]$SevenZipPath,
    [Parameter(Mandatory)]
    [string]$OutputDirectory,
    [string]$SigningCertificateThumbprint,
    [uri]$TimestampServer,
    [switch]$AllowSelfSigned
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'klCustom.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Unexpected release version: $version" }
$native = Get-Item -LiteralPath $NativeDllPath
if ($native.Name -ne 'kzlStartMenuHost5.dll') { throw 'Expected kzlStartMenuHost5.dll.' }
$sevenZip = (Resolve-Path -LiteralPath $SevenZipPath).Path
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory for the release.' }
$output = (New-Item -ItemType Directory -Path $OutputDirectory).FullName
$sign = @{
    CertificateThumbprint = $SigningCertificateThumbprint
    AllowSelfSigned = $AllowSelfSigned
}
if ($TimestampServer) { $sign.TimestampServer = $TimestampServer }

function Publish-Application([string]$directory, [bool]$selfContained) {
    $setting = $selfContained.ToString().ToLowerInvariant()
    dotnet publish $project -c Release -r win-x64 --self-contained $setting -p:NuGetAudit=false -p:DebugType=None --disable-build-servers -p:UseSharedCompilation=false -o $directory
    if ($LASTEXITCODE) { throw "Desktop publish failed ($LASTEXITCODE)." }
    Copy-Item -LiteralPath $native.FullName,(Join-Path $PSScriptRoot 'README.md'),(Join-Path $PSScriptRoot 'README.zh-CN.md') -Destination $directory
    if ($SigningCertificateThumbprint) {
        & (Join-Path $PSScriptRoot 'Sign-Release.ps1') -ReleaseDirectory $directory @sign | Out-Host
    }
}

$full = Join-Path $output 'self-contained'
$light = Join-Path $output 'framework-dependent'
Publish-Application $light $false
$payload = Join-Path $output 'payload.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($light, $payload, [IO.Compression.CompressionLevel]::Optimal, $false)

$downloader = "klCustom-$version-Downloader-x64.exe"
$downloaderPath = Join-Path $output $downloader
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$versionSource = Join-Path ([IO.Path]::GetTempPath()) ("klCustom-$([Guid]::NewGuid().ToString('N')).cs")
@"
[assembly: System.Reflection.AssemblyTitle("kl Custom Downloader")]
[assembly: System.Reflection.AssemblyProduct("kl Custom")]
[assembly: System.Reflection.AssemblyVersion("$version.0")]
[assembly: System.Reflection.AssemblyFileVersion("$version.0")]
"@ | Set-Content -LiteralPath $versionSource -Encoding ascii
$source = Join-Path $PSScriptRoot 'downloader'
$references = 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
    'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', 'System.Net.Http.dll', 'System.Web.Extensions.dll'
$referenceArgs = $references | ForEach-Object { "/reference:$(Join-Path $framework $_)" }
try {
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$downloaderPath" "/resource:$payload,klCustom.payload.zip" @referenceArgs (Join-Path $source 'Program.cs') (Join-Path $source 'Package.cs') $versionSource
    if ($LASTEXITCODE) { throw "Downloader build failed ($LASTEXITCODE)." }
}
finally { Remove-Item -LiteralPath $versionSource }
if ($SigningCertificateThumbprint) {
    & (Join-Path $PSScriptRoot 'Sign-Release.ps1') -ReleaseDirectory $output -BinaryNames $downloader @sign | Out-Host
}

Publish-Application $full $true
$archivePath = Join-Path $output "klCustom-$version-win-x64.7z"
Push-Location -LiteralPath $full
try {
    & $sevenZip a -t7z -mx=9 -m0=LZMA2 -md=128m -mfb=273 -mmt=2 -ms=on -bso0 $archivePath '*'
    if ($LASTEXITCODE) { throw "7z compression failed ($LASTEXITCODE)." }
    & $sevenZip t -bso0 $archivePath
    if ($LASTEXITCODE) { throw "7z verification failed ($LASTEXITCODE)." }
}
finally { Pop-Location }

Get-Item -LiteralPath $archivePath,$downloaderPath
