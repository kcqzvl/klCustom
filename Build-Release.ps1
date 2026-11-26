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
$projectXml = [xml](Get-Content -LiteralPath $project -Raw)
$version = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version').InnerText.Trim()
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

$light = Join-Path $output 'framework-dependent'
dotnet publish $project -c Release -r win-x64 --no-self-contained -p:PortableRuntimeLayout=true -p:NuGetAudit=false -p:DebugType=None --disable-build-servers -p:UseSharedCompilation=false -o $light
if ($LASTEXITCODE) { throw "Desktop publish failed ($LASTEXITCODE)." }
Copy-Item -LiteralPath $native.FullName,(Join-Path $PSScriptRoot 'README.md'),(Join-Path $PSScriptRoot 'README.zh-CN.md') -Destination $light
if ($SigningCertificateThumbprint) {
    & (Join-Path $PSScriptRoot 'Sign-Release.ps1') -ReleaseDirectory $light @sign | Out-Host
}

$payload = Join-Path $output 'payload.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($light, $payload, [IO.Compression.CompressionLevel]::Optimal, $false)

$downloader = "klCustom-$version-installer-x64.exe"
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

$layout = Join-Path $output 'archive-layout'
$windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $windowsPowerShell -NoProfile -ExecutionPolicy RemoteSigned -File (Join-Path $source 'Prepare-Archive.ps1') $downloaderPath $layout
if ($LASTEXITCODE) { throw "Could not prepare the archive with its private runtime ($LASTEXITCODE)." }
$archivePath = Join-Path $output "klCustom-$version-win-x64.7z"
Push-Location -LiteralPath $layout
try {
    & $sevenZip a -t7z -mx=9 -m0=LZMA2 -md=128m -mfb=273 -mmt=2 -ms=on -bso0 $archivePath "klCustom-$version"
    if ($LASTEXITCODE) { throw "7z compression failed ($LASTEXITCODE)." }
    & $sevenZip t -bso0 $archivePath
    if ($LASTEXITCODE) { throw "7z verification failed ($LASTEXITCODE)." }
}
finally { Pop-Location }

Get-Item -LiteralPath $archivePath,$downloaderPath
